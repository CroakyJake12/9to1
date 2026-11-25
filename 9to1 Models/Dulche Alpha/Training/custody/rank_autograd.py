"""PyTorch tensor boundary for a caller-owned two-rank forward/backward pass.

Import is inert. The caller owns devices, process lifetimes, loss scaling, and
optimizer state. On failure, retire both streams and recover the attempt.
"""

from dataclasses import dataclass, replace
import ctypes
import sys
from typing import BinaryIO

from .artifacts import CustodyError
from .rank_stream import BoundaryFrame, _wire, receive_frame, send_frame


@dataclass(frozen=True, slots=True)
class AutogradBoundary:
    activation: BoundaryFrame
    max_bytes: int

    def __post_init__(self):
        _wire(self.activation, self.max_bytes)
        if self.activation.direction != "activation":
            raise CustodyError("An autograd boundary must begin with an activation frame.")

    @property
    def gradient(self) -> BoundaryFrame:
        return replace(self.activation, source_rank=1, direction="gradient")

    def early_backward(self, hidden, outbound: BinaryIO, inbound: BinaryIO) -> None:
        """Send early hidden state and backpropagate returned grad into its graph."""
        torch = _torch()
        if not isinstance(hidden, torch.Tensor) or not hidden.requires_grad:
            raise CustodyError("The early hidden tensor must retain its autograd graph.")
        send_frame(outbound, self.activation, self._pack(torch, hidden), max_bytes=self.max_bytes)
        data = receive_frame(inbound, self.gradient, max_bytes=self.max_bytes)
        gradient = self._unpack(torch, data, hidden.device)
        torch.autograd.backward(hidden, gradient)

    def tail_forward(self, inbound: BinaryIO, *, device):
        """Receive a fresh detached leaf for the tail caller's existing forward/loss."""
        if device is None:
            raise CustodyError("The tail device must be explicit.")
        torch = _torch()
        data = receive_frame(inbound, self.activation, max_bytes=self.max_bytes)
        return self._unpack(torch, data, device).requires_grad_(True)

    def tail_backward(self, leaf, outbound: BinaryIO) -> None:
        """Return the boundary gradient after the caller's one backward pass."""
        torch = _torch()
        if (not isinstance(leaf, torch.Tensor) or not leaf.is_leaf or not leaf.requires_grad
                or leaf.grad is None):
            raise CustodyError("The tail leaf is missing its boundary gradient.")
        self._validate_tensor(torch, leaf)
        data = self._pack(torch, leaf.grad)
        send_frame(outbound, self.gradient, data, max_bytes=self.max_bytes)

    def _validate_tensor(self, torch, tensor):
        if (tensor.layout != torch.strided or tuple(tensor.shape) != self.activation.shape
                or tensor.dtype != getattr(torch, self.activation.dtype)):
            raise CustodyError("Boundary tensor differs from the expected shape or dtype.")

    def _pack(self, torch, tensor) -> bytes:
        self._validate_tensor(torch, tensor)
        cpu = tensor.detach().resolve_neg().to(device="cpu").contiguous()
        if not torch.isfinite(cpu).all().item():
            raise CustodyError("Boundary tensor contains nonfinite values.")
        _, byte_count = _wire(self.activation, self.max_bytes)
        # The live, contiguous CPU tensor owns this memory. Copy the exact
        # bounded span; avoid dtype conversions and an optional NumPy dependency.
        return ctypes.string_at(cpu.data_ptr(), byte_count)

    def _unpack(self, torch, data: bytes, device):
        # Clone so the leaf/source does not retain transport buffers. bytearray
        # provides writable backing without a warning from torch.frombuffer.
        tensor = torch.frombuffer(bytearray(data), dtype=getattr(torch, self.activation.dtype))
        tensor = tensor.reshape(self.activation.shape).clone().to(device=device)
        if not torch.isfinite(tensor).all().item():
            raise CustodyError("Boundary tensor contains nonfinite values.")
        return tensor


def _torch():
    if sys.byteorder != "little":
        raise CustodyError("The autograd boundary requires a little-endian tensor runtime.")
    try:
        import torch
    except ImportError as error:
        raise CustodyError("The autograd boundary requires an installed PyTorch runtime.") from error
    return torch
