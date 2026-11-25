"""One-way v8 rank-zero token IDs on an existing authenticated owned stream."""
import array
import struct
import sys
from uuid import UUID

from .artifacts import CustodyError, canonical_id
from .rank_stream import _read_exact, _write_all

_MAGIC = b"DVI1"
_HEADER = struct.Struct("!4s16sQIBBI")
_MAX_SEQ = 32768
_RANK = 0
_DTYPE_U32 = 1


class _OrderedPort:
    def __init__(self, *, attempt_id, first_step, grad_accum, seq_cap, vocab_size):
        identity = UUID(canonical_id(attempt_id))
        if (identity.int == 0 or type(first_step) is not int or not 0 < first_step < 2**64
                or type(grad_accum) is not int or not 0 < grad_accum <= 4096
                or type(seq_cap) is not int or not 2 <= seq_cap <= _MAX_SEQ
                or type(vocab_size) is not int or not 0 < vocab_size <= 2**32):
            raise CustodyError("Invalid admitted v8 batch port.")
        self._identity = identity.bytes
        self._grad_accum, self._seq_cap, self._vocab_size = grad_accum, seq_cap, vocab_size
        self._next = (first_step, 0)
        self._failed = False

    def _begin(self, rank, expected_rank, step, micro):
        if self._failed:
            raise CustodyError("Failed v8 batch port must be retired.")
        self._failed = True  # No resync or retry after unsafe coordinates, validation or I/O failure.
        if (type(rank) is not int or rank != expected_rank or type(step) is not int
                or type(micro) is not int or (step, micro) != self._next or step >= 2**64):
            raise CustodyError("Unexpected v8 microbatch coordinate or rank.")

    def _finish(self, step, micro):
        self._next = (step, micro + 1) if micro + 1 < self._grad_accum else (step + 1, 0)
        self._failed = False

    def _header(self, step, micro, length):
        if not 2 <= length <= self._seq_cap:
            raise CustodyError("V8 input-ID frame exceeds admitted sequence bounds.")
        return _HEADER.pack(_MAGIC, self._identity, step, micro, _RANK, _DTYPE_U32, length)

    def _validate_ids(self, values):
        if any(type(t) is not int or not 0 <= t < self._vocab_size for t in values):
            raise CustodyError("V8 input ID is outside the admitted vocabulary.")


class HostBatchPort(_OrderedPort):
    """Rank-one callback: send only IDs; return original labels to host loss."""
    def __init__(self, sampler, outbound, **admission):
        super().__init__(**admission)
        if not callable(getattr(sampler, "microbatch", None)):
            raise CustodyError("An admitted v8 sampler is required.")
        self._sampler, self._outbound = sampler, outbound

    def __call__(self, rank, step, micro):
        self._begin(rank, 1, step, micro)
        ids, labels = self._sampler.microbatch(step, micro)
        import torch

        if (type(ids) is not torch.Tensor or type(labels) is not torch.Tensor
                or ids.device.type != "cpu" or labels.device.type != "cpu"
                or ids.dtype != torch.long or labels.dtype != torch.long
                or ids.ndim != 2 or ids.shape[0] != 1 or ids.shape != labels.shape):
            raise CustodyError("Invalid original v8 microbatch dtype, device or shape.")
        header = self._header(step, micro, ids.shape[1])
        values = ids[0].tolist()
        self._validate_ids(values)
        payload = array.array("I", values)
        if payload.itemsize != 4:
            raise CustodyError("Unsupported v8 input-ID host wire representation.")
        if sys.byteorder != "little":
            payload.byteswap()
        _write_all(self._outbound, header)
        _write_all(self._outbound, payload.tobytes())
        self._outbound.flush()
        self._finish(step, micro)
        return labels


class GuestBatchPort(_OrderedPort):
    """Rank-zero callback: no labels, files, listener or dataset discovery."""
    def __init__(self, inbound, **admission):
        super().__init__(**admission)
        self._inbound = inbound

    def __call__(self, rank, step, micro):
        self._begin(rank, 0, step, micro)
        header = _read_exact(self._inbound, _HEADER.size)
        *_, length = _HEADER.unpack(header)
        if header != self._header(step, micro, length):
            raise CustodyError("V8 input-ID frame does not match the admitted attempt or route.")
        payload = array.array("I")
        if payload.itemsize != 4:
            raise CustodyError("Unsupported v8 input-ID guest wire representation.")
        payload.frombytes(_read_exact(self._inbound, length * 4))
        if sys.byteorder != "little":
            payload.byteswap()
        self._validate_ids(payload)
        import torch

        ids = torch.tensor(payload.tolist(), dtype=torch.long, device="cpu").unsqueeze(0)
        self._finish(step, micro)
        return ids
