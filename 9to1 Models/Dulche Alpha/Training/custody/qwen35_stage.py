"""One Qwen3.5 text microbatch over already-owned rank streams."""

from contextlib import nullcontext
from dataclasses import dataclass
from typing import BinaryIO, Callable

from .artifacts import CustodyError
from .qwen35_range import Qwen35TextRange
from .rank_autograd import AutogradBoundary


@dataclass(frozen=True, slots=True)
class Qwen35RankStage:
    """Bound range + autograd transport; no launch, optimizer, or recovery policy."""

    text: Qwen35TextRange
    boundary: AutogradBoundary

    def __post_init__(self):
        if type(self.text) is not Qwen35TextRange or type(self.boundary) is not AutogradBoundary:
            raise CustodyError("A Qwen3.5 stage requires an exact native range and autograd boundary.")
        _, length, width = self.boundary.activation.shape
        if width != self.text._config.hidden_size or length > self.text._config.max_position_embeddings:
            raise CustodyError("The Qwen3.5 stage boundary does not match the admitted text shape.")

    def _confirm(self, rank: int, step: int, microbatch: int) -> None:
        spec = self.boundary.activation
        if (self.text._rank != rank or type(step) is not int or step != spec.step
                or type(microbatch) is not int or microbatch != spec.microbatch):
            raise CustodyError("The local Qwen3.5 stage rank or microbatch coordinates do not match.")

    def early(self, input_ids, inbound: BinaryIO, outbound: BinaryIO, *, step: int, microbatch: int, forward_fp16=False) -> None:
        self._confirm(0, step, microbatch)
        if (not isinstance(input_ids, self.text._torch.Tensor)
                or tuple(input_ids.shape) != self.boundary.activation.shape[:2]):
            raise CustodyError("The early token batch does not match the boundary coordinates.")
        torch = self.text._torch
        if forward_fp16 is not False and (forward_fp16 is not True or self.boundary.activation.dtype != "float16"
                                         or input_ids.device.type != "cuda"):
            raise CustodyError("Volta autocast requires the admitted CUDA FP16 activation lane.")
        with torch.autocast(device_type="cuda", dtype=torch.float16) if forward_fp16 else nullcontext():
            hidden = self.text.forward(input_ids=input_ids)
        self.boundary.early_backward(hidden, outbound, inbound)

    def tail(self, inbound: BinaryIO, outbound: BinaryIO, *, step: int, microbatch: int,
             device, backward_loss: Callable, hidden_dtype=None):
        """Forward tail hidden to the caller, which runs its own loss/backward once."""
        self._confirm(1, step, microbatch)
        if not callable(backward_loss):
            raise CustodyError("A caller-owned loss and backward function is required.")
        torch = self.text._torch
        if hidden_dtype is not None and (self.boundary.activation.dtype != "float16"
                                        or hidden_dtype is not torch.bfloat16):
            raise CustodyError("Only the admitted Volta FP16 to RTX BF16 boundary cast is supported.")
        leaf = self.boundary.tail_forward(inbound, device=device)
        if hidden_dtype is None:
            hidden = self.text.forward(hidden=leaf)
        else:
            with torch.autocast(device_type=leaf.device.type, dtype=hidden_dtype):
                hidden = self.text.forward(hidden=leaf.to(dtype=hidden_dtype))
        result = backward_loss(hidden)
        self.boundary.tail_backward(leaf, outbound)
        return result
