"""Order the frozen rank's input, autograd and completion on one owned pipe."""

from contextlib import contextmanager
import hashlib
import struct
from uuid import UUID

from .artifacts import CustodyError, canonical_id
from .rank_stream import BoundaryFrame, _read_exact, _write_all
from .v8_batch_port import GuestBatchPort, HostBatchPort
from .v8_frozen_receipt import FrozenV100Receipt

_DONE = struct.Struct("!4s16sQI32sII32s32s")
_MAGIC = b"DVD1"
_MAX_STATE = 64 * 1024


class FrozenGuestMainChannel:
    """One exact step; the caller owns a pinned process, pipes and timeouts.

    Host wire: input IDs -> activation received -> gradient sent, repeated
    original grad_accum times, then receive one typed guest completion.
    Guest observes the reverse. Weight requests/control have other owned
    channels. This object does not authenticate the transport itself.
    """

    def __init__(self, *, rank, attempt_id, step, owner, grad_accum, seq_cap,
                 vocab_size, inbound, outbound, sampler=None):
        identity = UUID(canonical_id(attempt_id))
        if (type(rank) is not int or rank not in (0, 1) or identity.int == 0
                or type(owner) is not str or not 0 < len(owner) <= 128
                or (sampler is None) != (rank == 0)):
            raise CustodyError("Invalid owned frozen guest endpoint.")
        options = dict(attempt_id=attempt_id, first_step=step, grad_accum=grad_accum,
                       seq_cap=seq_cap, vocab_size=vocab_size)
        self._batch = (GuestBatchPort(inbound, **options) if rank == 0
                       else HostBatchPort(sampler, outbound, **options))
        self._rank, self._attempt, self._identity, self._step = rank, attempt_id, identity.bytes, step
        self._owner, self._limit, self._in, self._out = owner, grad_accum, inbound, outbound
        self._next, self._phase = 0, "batch"

    def read_batch(self, rank, step, micro):
        phase, self._phase = self._phase, "failed"
        if (phase != "batch" or type(rank) is not int or rank != self._rank
                or type(step) is not int or step != self._step
                or type(micro) is not int or micro != self._next or micro >= self._limit):
            raise CustodyError("Guest main channel batch is out of order.")
        batch = self._batch(rank, step, micro)
        self._phase = "frames"
        return batch

    @contextmanager
    def frames(self, rank, frame):
        phase, self._phase = self._phase, "failed"
        if (phase != "frames" or type(rank) is not int or rank != self._rank
                or type(frame) is not BoundaryFrame or frame.attempt_id != self._attempt
                or frame.step != self._step or frame.microbatch != self._next
                or frame.source_rank != 0 or frame.direction != "activation" or frame.dtype != "float16"):
            raise CustodyError("Guest main channel autograd frame is out of order.")
        yield self._in, self._out
        self._next += 1
        self._phase = "done" if self._next == self._limit else "batch"

    def _consume_done(self, rank):
        phase, self._phase = self._phase, "failed"
        if self._rank != rank or phase != "done" or self._next != self._limit:
            raise CustodyError("Frozen V100 completion is out of order.")

    def send_done(self, receipt, cuda_rng, cpu_rng):
        self._consume_done(0)
        if (type(receipt) is not FrozenV100Receipt or receipt.attempt_id != self._attempt
                or type(receipt.step) is not int or receipt.step != self._step
                or receipt.ownership_id != self._owner
                or type(receipt.completed_microbatches) is not int
                or receipt.completed_microbatches != self._limit
                or type(cuda_rng) is not bytes or not 0 < len(cuda_rng) <= _MAX_STATE
                or type(cpu_rng) is not bytes or not 0 < len(cpu_rng) <= _MAX_STATE):
            raise CustodyError("Invalid frozen V100 completion.")
        cuda_hash, cpu_hash = hashlib.sha256(cuda_rng), hashlib.sha256(cpu_rng)
        try:
            trace = bytes.fromhex(receipt.transcript_sha256)
        except (ValueError, TypeError) as error:
            raise CustodyError("Invalid frozen guest transcript digest.") from error
        if (len(trace) != 32 or trace.hex() != receipt.transcript_sha256
                or cuda_hash.hexdigest() != receipt.cuda_rng_sha256
                or cpu_hash.hexdigest() != receipt.cpu_rng_sha256):
            raise CustodyError("Frozen guest completion state failed validation.")
        _write_all(self._out, _DONE.pack(_MAGIC, self._identity, self._step, self._limit, trace,
                                         len(cuda_rng), len(cpu_rng), cuda_hash.digest(), cpu_hash.digest()))
        _write_all(self._out, cuda_rng)
        _write_all(self._out, cpu_rng)
        self._out.flush()

    def receive_done(self):
        """Read syntax and digests only; caller still must authenticate completion."""
        self._consume_done(1)
        fields = _DONE.unpack(_read_exact(self._in, _DONE.size))
        magic, identity, step, count, trace, cuda_length, cpu_length, cuda_sha, cpu_sha = fields
        if (magic != _MAGIC or identity != self._identity or step != self._step or count != self._limit
                or not 0 < cuda_length <= _MAX_STATE or not 0 < cpu_length <= _MAX_STATE):
            raise CustodyError("Frozen V100 completion contradicts the admitted attempt.")
        cuda_rng, cpu_rng = _read_exact(self._in, cuda_length), _read_exact(self._in, cpu_length)
        if hashlib.sha256(cuda_rng).digest() != cuda_sha or hashlib.sha256(cpu_rng).digest() != cpu_sha:
            raise CustodyError("Frozen V100 completion state was corrupted.")
        return (FrozenV100Receipt(self._attempt, step, self._owner, count, trace.hex(),
                                   cuda_sha.hex(), cpu_sha.hex()), cuda_rng, cpu_rng)
