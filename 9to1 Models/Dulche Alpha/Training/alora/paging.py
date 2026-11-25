"""Frozen GGUF matrix paging with input-gradient rematerialization.

The supplied store owns immutable source verification and bounded decoding.
These modules load no model, choose no accelerator and grant no compute permit.
Construct/move them only onto a caller-admitted device. Base weights are never
registered as Parameters or retained by an autograd context.
"""

from __future__ import annotations

import numpy as np
import torch
from torch import nn
from torch.nn import functional as F
from torch.autograd.function import once_differentiable

from .compute import check_compute, compute_scope, current_compute_callback

_FLOAT_DTYPES = frozenset((torch.float16, torch.bfloat16, torch.float32, torch.float64))


def _dtype(dtype):
    if not isinstance(dtype, torch.dtype) or dtype not in _FLOAT_DTYPES:
        raise ValueError("Paging requires an explicit supported floating-point dtype.")
    return dtype


def _device(device):
    if not isinstance(device, (str, torch.device)):
        raise ValueError("Paging requires an explicit device name or torch.device.")
    try:
        result = torch.device(device)
    except (RuntimeError, ValueError) as error:
        raise ValueError("Paging received an invalid explicit device.") from error
    if result.type == "meta":
        raise ValueError("Paged computation requires a materialized admitted device.")
    return result


def _shape(ref):
    shape = getattr(ref, "shape", None)
    if (not isinstance(shape, tuple) or len(shape) != 2
            or any(type(value) is not int or value <= 0 for value in shape)):
        raise ValueError("A frozen matrix reference must declare a positive two-dimensional shape.")
    return shape


def _array_tensor(array, shape, *, device, dtype):
    """Accept the store's owned decode buffer without silently copying bad layouts."""
    if (type(array) is not np.ndarray or array.dtype != np.float32
            or array.shape != shape or not array.flags.c_contiguous
            or not array.flags.owndata):
        raise ValueError("The weight store must return owned contiguous float32 bytes of the declared shape.")
    if not np.isfinite(array).all():
        raise ValueError("The weight store returned nonfinite frozen values.")
    # A finite float32 source may still overflow the explicitly selected compute
    # dtype. Refuse before copying/casting it onto an accelerator.
    maximum = torch.finfo(dtype).max
    if maximum < float(np.finfo(np.float32).max) and array.size and np.any(np.abs(array) > maximum):
        raise ValueError("Frozen values cannot be represented by the selected compute dtype.")
    check_compute(device=device)
    return torch.from_numpy(array).to(device=device, dtype=dtype)


def _materialize(store, ref, *, device, dtype):
    return _array_tensor(store.read(ref), _shape(ref), device=device, dtype=dtype)


def _input(x, *, width=None, device=None, dtype=None):
    if (not isinstance(x, torch.Tensor) or x.layout != torch.strided
            or x.ndim < 1 or x.device.type == "meta"):
        raise ValueError("Paged linear input must be a materialized strided tensor.")
    _dtype(x.dtype)
    if x.device.type == "cuda" and x.dtype != torch.float32:
        raise ValueError("The supervised single-CUDA candidate supports only FP32.")
    if width is not None and x.shape[-1] != width:
        raise ValueError("Paged linear input width differs from the frozen matrix.")
    if device is not None and x.device != device:
        raise ValueError("Paged linear input must already be on the module's admitted device.")
    if dtype is not None and x.dtype != dtype:
        raise ValueError("Paged linear input must already use the module's compute dtype.")


class _FrozenLinear(torch.autograd.Function):
    @staticmethod
    def forward(ctx, x, store, ref):
        _input(x, width=_shape(ref)[1])
        ctx.callback = current_compute_callback()
        check_compute(device=x.device)
        ctx.store = store
        ctx.ref = ref
        ctx.input_dtype = x.dtype
        ctx.input_device = x.device
        weight = _materialize(store, ref, device=x.device, dtype=x.dtype)
        # Custom Function.forward records no internal autograd operations. Neither
        # x nor this temporary decoded weight is saved for the backward pass.
        check_compute(device=x.device)
        return F.linear(x, weight)

    @staticmethod
    @once_differentiable
    def backward(ctx, grad_output):
        with compute_scope(ctx.callback, device=ctx.input_device):
            check_compute(device=ctx.input_device)
            weight = _materialize(ctx.store, ctx.ref,
                                  device=ctx.input_device, dtype=ctx.input_dtype)
            check_compute(device=ctx.input_device)
            grad_input = torch.matmul(grad_output.to(dtype=ctx.input_dtype), weight)
        return grad_input, None, None


def frozen_linear(x, store, ref):
    """Apply one immutable matrix; backward re-reads/revalidates its exact source.

    Only the input is differentiable. Higher-order derivatives are deliberately
    unsupported and refused by once_differentiable.
    """
    _input(x, width=_shape(ref)[1])
    return _FrozenLinear.apply(x, store, ref)


class PagedLinear(nn.Module):
    """A bias-free matrix projection decoded in bounded output-row chunks."""

    def __init__(self, store, name, *, expert=None, device="cpu",
                 dtype=torch.float32, chunk_rows=256):
        super().__init__()
        if type(name) is not str or not name:
            raise ValueError("Paged linear requires a tensor name.")
        if type(chunk_rows) is not int or chunk_rows <= 0:
            raise ValueError("chunk_rows must be a positive integer.")
        dtype, device = _dtype(dtype), _device(device)
        if device.type == "cuda" and dtype != torch.float32:
            raise ValueError("The supervised single-CUDA candidate supports only FP32.")
        info = store.tensor_info(name)
        shape = info.get("shape") if type(info) is dict else None
        if (not isinstance(shape, tuple) or len(shape) not in (2, 3)
                or any(type(value) is not int or value <= 0 for value in shape)):
            raise ValueError("Paged linear requires a matrix or an indexed expert matrix.")
        if len(shape) == 3:
            if type(expert) is not int or not 0 <= expert < shape[0]:
                raise ValueError("A packed expert projection requires an explicit valid expert ID.")
            self.out_features, self.in_features = shape[1:]
        else:
            if expert is not None:
                raise ValueError("An ordinary matrix cannot select an expert ID.")
            self.out_features, self.in_features = shape
        refs = []
        for start in range(0, self.out_features, chunk_rows):
            count = min(chunk_rows, self.out_features - start)
            ref = store.matrix_ref(name, expert=expert, row_start=start, row_count=count)
            if _shape(ref) != (count, self.in_features):
                raise ValueError("The store changed a matrix chunk's declared shape.")
            refs.append(ref)
        self.store, self.tensor_name, self.expert = store, name, expert
        self.chunk_rows, self._refs = chunk_rows, tuple(refs)
        check_compute(device=device)
        self.register_buffer("_anchor", torch.empty(0, device=device, dtype=dtype), persistent=False)

    @property
    def device(self):
        return self._anchor.device

    @property
    def dtype(self):
        return self._anchor.dtype

    def forward(self, x):
        _input(x, width=self.in_features, device=self.device, dtype=self.dtype)
        outputs = [frozen_linear(x, self.store, ref) for ref in self._refs]
        return outputs[0] if len(outputs) == 1 else torch.cat(outputs, dim=-1)


class PagedEmbedding(nn.Module):
    """Frozen row lookup without materializing or impersonating a full weight table."""

    def __init__(self, store, name, *, device="cpu", dtype=torch.float32):
        super().__init__()
        if type(name) is not str or not name:
            raise ValueError("Paged embedding requires a tensor name.")
        dtype, device = _dtype(dtype), _device(device)
        if device.type == "cuda" and dtype != torch.float32:
            raise ValueError("The supervised single-CUDA candidate supports only FP32.")
        info = store.tensor_info(name)
        shape = info.get("shape") if type(info) is dict else None
        if (not isinstance(shape, tuple) or len(shape) != 2
                or any(type(value) is not int or value <= 0 for value in shape)):
            raise ValueError("Paged embedding requires a positive two-dimensional table.")
        self.num_embeddings, self.embedding_dim = shape
        self.store, self.tensor_name = store, name
        check_compute(device=device)
        self.register_buffer("_anchor", torch.empty(0, device=device, dtype=dtype), persistent=False)

    @property
    def device(self):
        return self._anchor.device

    @property
    def dtype(self):
        return self._anchor.dtype

    def forward(self, indices):
        if (not isinstance(indices, torch.Tensor) or indices.layout != torch.strided
                or indices.device.type == "meta"
                or indices.dtype not in (torch.int32, torch.int64)
                or indices.device != self.device):
            raise ValueError("Embedding IDs must be integer tensors on the module's admitted device.")
        check_compute(device=self.device)
        shape = tuple(indices.shape) + (self.embedding_dim,)
        if indices.numel() == 0:
            return self._anchor.new_empty(shape)
        selected = indices.detach().to(device="cpu", dtype=torch.int64).reshape(-1).contiguous().numpy()
        if np.any(selected < 0) or np.any(selected >= self.num_embeddings):
            raise ValueError("Embedding IDs lie outside the frozen table.")
        rows = self.store.gather_rows(self.tensor_name, selected)
        result = _array_tensor(rows, (len(selected), self.embedding_dim),
                               device=self.device, dtype=self.dtype)
        return result.reshape(shape)
