"""Frozen Volta step runnable over owned transport, without writable host state."""
from contextlib import contextmanager
import hashlib
import struct

from .artifacts import CustodyError
from .qwen35_stage import Qwen35RankStage
from .rank_autograd import AutogradBoundary
from .rank_stream import BoundaryFrame
from .v8_frozen_receipt import FrozenV100Receipt
from .v8_joint_step import PreparedV8Rank, _admit, _batch_identity


class _ReadTrace:
    def __init__(self, stream, trace):
        self.stream, self.trace = stream, trace

    def read(self, count):
        data = self.stream.read(count)
        if type(data) is bytes:
            self.trace.update(data)
        return data


class _WriteTrace:
    def __init__(self, stream, trace):
        self.stream, self.trace = stream, trace

    def write(self, data):
        count = self.stream.write(data)
        if type(count) is int and 0 < count <= len(data):
            self.trace.update(data[:count])
        return count

    def flush(self):
        self.stream.flush()


class FrozenGuestTranscript:
    """Host and guest independently record already-owned sample and frame I/O."""

    def __init__(self):
        self._trace = hashlib.sha256(b"v8-frozen-transcript-v1\x00")
        self.count = 0

    def record(self, step, micro, input_ids, activations, gradients):
        ids = tuple(input_ids)
        if (type(step) is not int or not 0 < step < 2**64 or type(micro) is not int
                or micro != self.count or not 2 <= len(ids) <= 32768
                or any(type(value) is not int or not 0 <= value < 2**32 for value in ids)
                or type(activations) is not bytes or len(activations) != 32
                or type(gradients) is not bytes or len(gradients) != 32):
            raise CustodyError("Invalid input/frame coordinates in owned frozen V100 transcript.")
        self._trace.update(struct.pack(f"!QII{len(ids)}I", step, micro, len(ids), *ids))
        self._trace.update(activations)
        self._trace.update(gradients)
        self.count += 1

    def hexdigest(self):
        return self._trace.hexdigest()


class HostFrozenTranscriptPort:
    """Host-side observer around the process owner's already-open frames."""

    def __init__(self, sampler, open_owned_frames, *, attempt_id, step, grad_accum):
        if (not callable(getattr(sampler, "microbatch", None)) or not callable(open_owned_frames)
                or type(grad_accum) is not int or not 0 < grad_accum <= 4096):
            raise CustodyError("An exact admitted host sampler and owned frame port are required.")
        self._sampler, self._open = sampler, open_owned_frames
        self._attempt, self._step, self._grad_accum = attempt_id, step, grad_accum
        self._transcript = FrozenGuestTranscript()

    @contextmanager
    def __call__(self, rank, frame):
        micro = self._transcript.count
        if (type(rank) is not int or rank != 1 or type(frame) is not BoundaryFrame
                or frame.attempt_id != self._attempt or frame.step != self._step
                or frame.microbatch != micro or micro >= self._grad_accum):
            raise CustodyError("Host frozen transcript received invalid rank coordinates.")
        ids, _ = self._sampler.microbatch(frame.step, micro)
        activation, gradient = hashlib.sha256(), hashlib.sha256()
        with self._open(rank, frame) as (inbound, outbound):
            yield _ReadTrace(inbound, activation), _WriteTrace(outbound, gradient)
        self._transcript.record(frame.step, micro, ids[0].tolist(), activation.digest(), gradient.digest())

    def matches(self, receipt):
        return (type(receipt) is FrozenV100Receipt and receipt.attempt_id == self._attempt
                and receipt.step == self._step and receipt.completed_microbatches == self._grad_accum
                and self._transcript.count == self._grad_accum
                and receipt.transcript_sha256 == self._transcript.hexdigest())


def run_frozen_guest_step(*, prepared, attempt_id, step, owner, grad_accum, seq_len, frame_budget):
    """Guest returns exact receipt + two RNG states over its pinned owner channel.

    The launcher binds original CUDA UUID and runtime, receives this data on
    the already-authenticated guest channel, and independently observes the
    host side of each input/activation/gradient for stage_frozen_v100.
    """
    import torch

    if (type(prepared) is not PreparedV8Rank or prepared.tail is not None
            or prepared.control.rank != 0 or prepared.text._rank != 0
            or type(grad_accum) is not int or not 0 < grad_accum <= 4096):
        raise CustodyError("Frozen guest entrypoint cannot own optimiser parameters.")
    text, control = prepared.text, prepared.control
    ports = control.ports
    device = torch.device("cuda:0")
    transcript = FrozenGuestTranscript()
    with control.running(step):
        for micro in range(grad_accum):
            _admit(ports, rank=0, step=step)
            ids = ports.read_microbatch(0, step, micro)
            shape = _batch_identity(ids, text._config, seq_len, torch)
            frame = BoundaryFrame(attempt_id, step, micro, 0, "activation", "float16", shape)
            stage = Qwen35RankStage(text, AutogradBoundary(frame, frame_budget))
            activation, gradient = hashlib.sha256(), hashlib.sha256()
            with ports.open_frame_streams(0, frame) as (inbound, outbound):
                stage.early(ids.to(device), _ReadTrace(inbound, gradient), _WriteTrace(outbound, activation),
                            step=step, microbatch=micro, forward_fp16=True)
            torch.cuda.synchronize(device)
            transcript.record(step, micro, ids[0].tolist(), activation.digest(), gradient.digest())
        _admit(ports, rank=0, step=step)
        cuda_rng, cpu_rng = (bytes(state.tolist()) for state in (torch.cuda.get_rng_state(device), torch.get_rng_state()))
        receipt = FrozenV100Receipt(attempt_id, step, owner, transcript.count, transcript.hexdigest(),
                                    hashlib.sha256(cuda_rng).hexdigest(), hashlib.sha256(cpu_rng).hexdigest())
        return receipt, cuda_rng, cpu_rng
