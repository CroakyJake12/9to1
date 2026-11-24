"""Rank-local v8 frozen pager for an admitted upper-only Qwen3.5 step.

This is the early/V100 adapter only. The approved BF16 checkpoint remains the
backing store; the caller must explicitly admit FP16 CUDA compute for the
frozen half. No checkpoint loading, worker launch or implicit dtype fallback.
"""
from collections.abc import Mapping
from functools import partial

from .artifacts import CustodyError
from .qwen35_range import Qwen35TextRange
from .qwen35_rank_assembly import AssembledQwen35Rank


def _require_frozen_upper_lane(segment, precision, device, capability, count):
    if (segment != "upper" or precision != "float16" or device != "cuda:0"
            or capability != (7, 0) or count != 1):
        raise CustodyError("Early v8 pager requires approved upper-only FP16 on one admitted V100.")


def _pager_types(torch):
    from torch import nn
    from torch.nn import functional as F

    class FrozenLinearFn(torch.autograd.Function):
        @staticmethod
        def forward(ctx, x, layer):
            ctx.layer, ctx.input_dtype = layer, x.dtype
            weight = layer.materialize(x.device)
            bias = None if layer.bias is None else layer.bias.to(device=x.device, dtype=weight.dtype)
            return F.linear(x.to(weight.dtype), weight, bias)

        @staticmethod
        def backward(ctx, grad):
            layer = ctx.layer
            weight = layer.materialize(grad.device)
            layer.duty()
            return (grad.to(weight.dtype) @ weight).to(ctx.input_dtype), None

    class FrozenLinear(nn.Linear):
        def __init__(self, original, duty, compute_dtype, layer_id):
            nn.Module.__init__(self)
            self.in_features, self.out_features = original.in_features, original.out_features
            self.weight = nn.Parameter(torch.empty(0, dtype=torch.bfloat16), requires_grad=False)
            # Intentionally not a registered Parameter/buffer; casting or moving
            # the lightweight module must not move its large frozen backing.
            self.source = original.weight.detach()
            self.bias = None if original.bias is None else original.bias.detach()
            self.duty, self.compute_dtype, self.layer_id = duty, compute_dtype, layer_id

        def materialize(self, device):
            self.duty()
            return self.source.to(device=device, dtype=self.compute_dtype)

        def forward(self, x):
            return FrozenLinearFn.apply(x, self)

    class FrozenEmbedding(nn.Module):
        def __init__(self, original, duty, compute_dtype):
            super().__init__()
            self.source = original.weight.detach()
            self.padding_idx = original.padding_idx
            self.duty, self.compute_dtype = duty, compute_dtype

        def forward(self, ids):
            self.duty()
            hidden = F.embedding(ids.cpu(), self.source, self.padding_idx)
            # Equivalent to v8 enable_input_require_grads: a frozen early
            # rank still participates in the bridge and backward pass.
            return hidden.to(device=ids.device, dtype=self.compute_dtype).requires_grad_(True)

    return FrozenLinear, FrozenEmbedding


_TARGETS = {"q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"}


def _original_name(index, relative):
    return f"model.language_model.layers.{index}.{relative}"


def _owned_v8_lora(rows, frozen_lora, torch):
    # The controller verifies exact predecessor custody and extracts entries
    # from v8's existing `adapter` map, removing only `base_model.model.`.
    wanted = {}
    if not isinstance(frozen_lora, Mapping):
        raise CustodyError("Exact v8 early adapter state is required; inactive does not mean disabled.")
    for index, relative, linear in rows:
        if relative.rsplit(".", 1)[-1] not in _TARGETS:
            continue
        path = _original_name(index, relative)
        for component, shape in (("A", (16, linear.in_features)), ("B", (linear.out_features, 16))):
            key = f"{path}.lora_{component}.default.weight"
            value = frozen_lora.get(key)
            if (type(value) is not torch.Tensor or value.device.type != "cpu"
                    or value.dtype != torch.float32 or tuple(value.shape) != shape
                    or value.layout != torch.strided or not torch.isfinite(value).all().item()):
                raise CustodyError(f"Missing or incompatible frozen v8 adapter: {key}")
            wanted[key] = value
    if not wanted or wanted.keys() != frozen_lora.keys():
        raise CustodyError("Frozen v8 adapter state differs from the exact set of early targets.")
    return wanted


def place_frozen_early_v8(assembled, *, device, approved_segment, approved_precision, duty, frozen_lora):
    """Place the exactly-owned layers without moving BF16 backing into VRAM.

    Caller owns the per-rank run lease, UUID validation, any explicit transport
    FP16 autocast, transport conversion, and exact extraction of frozen
    adapter state from the admitted initialization or predecessor.
    Discard the rank on placement failure; the immutable CPU checkpoint remains.
    """
    import torch
    from torch import nn
    from torch.utils.checkpoint import checkpoint
    from peft.tuners.lora.layer import Linear as PeftLinear

    if (type(assembled) is not AssembledQwen35Rank or type(assembled.text) is not Qwen35TextRange
            or assembled.lm_head is not None or assembled.text._rank != 0 or not callable(duty)):
        raise CustodyError("The v8 frozen pager needs an assembled early-only text rank.")
    device = torch.device(device)
    # Check the admitted mode before touching CUDA or any backing tensor.
    _require_frozen_upper_lane(approved_segment, approved_precision, str(device),
                               (7, 0), 1)
    count = torch.cuda.device_count()
    capability = torch.cuda.get_device_capability(device) if count == 1 else None
    _require_frozen_upper_lane(approved_segment, approved_precision, str(device), capability, count)
    text = assembled.text
    if (text._config.num_hidden_layers != 64 or len(text._layers) != 32
            or type(text._edge) is not nn.Embedding
            or any(getattr(layer, "layer_idx", i) != i or not hasattr(layer, "gradient_checkpointing")
                   for i, (_, layer) in enumerate(text._layers))):
        raise CustodyError("Unsupported v8 early decoder range or checkpointing ABI.")
    modules = tuple(layer for _, layer in text._layers)
    for root in (text._edge, *modules):
        if any(p.device.type != "cpu" or p.dtype != torch.bfloat16 for p in root.parameters()):
            raise CustodyError("The v8 frozen early rank needs complete CPU BF16 backing.")
    linear_type, embedding_type = _pager_types(torch)
    rows = [(index, name, child) for index, layer in enumerate(modules)
            for name, child in layer.named_modules() if name and type(child) is nn.Linear]
    initial = _owned_v8_lora(rows, frozen_lora, torch)
    placed = []
    for global_idx, name, child in rows:
        parent_name, _, attribute = name.rpartition(".")
        parent = modules[global_idx].get_submodule(parent_name)
        current = linear_type(child, duty, torch.float16, global_idx)
        path = _original_name(global_idx, name)
        if name.rsplit(".", 1)[-1] in _TARGETS:
            # Reuse v8's exact PEFT operator and dropout, even when frozen.
            # The original run's adapter tensors, not fresh parameters,
            # determine this rank's forward pass.
            with torch.random.fork_rng(devices=[]):
                current = PeftLinear(current, "default", r=16, lora_alpha=32, lora_dropout=0.05,
                                     init_lora_weights=False)
            with torch.no_grad():
                for component in ("A", "B"):
                    adapter = getattr(current, f"lora_{component}")["default"].to(dtype=torch.float32)
                    adapter.weight.copy_(initial[f"{path}.lora_{component}.default.weight"])
        setattr(parent, attribute, current)
        placed.append(path)
    for layer in modules:
        layer.requires_grad_(False)
        # v8's prepare_model_for_kbit_training promotes non-paged frozen
        # parameters to FP32. Buffers retain their original dtype.
        for child in layer.modules():
            for name, param in tuple(child.named_parameters(recurse=False)):
                child.register_parameter(name, nn.Parameter(param.detach().to(device, torch.float32),
                                                           requires_grad=False))
        layer.to(device=device)
        layer.gradient_checkpointing = True
        layer._gradient_checkpointing_func = partial(checkpoint, use_reentrant=False)
        layer.train()
    if not placed:
        raise CustodyError("No owned v8 projections were paged.")
    text._rotary.to(device=device)
    text._edge = embedding_type(text._edge, duty, torch.float16)
    return tuple(placed)
