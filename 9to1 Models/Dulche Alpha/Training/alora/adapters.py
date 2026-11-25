"""Sparse adapters for the installed Transformers Qwen4Exp packed expert interface.

Only selected expert IDs own optimizer parameters. Frozen experts still participate
in autograd with respect to their inputs. This is not PEFT's token-triggered aLoRA.
"""
from __future__ import annotations

import math
from collections import Counter
from collections.abc import Mapping, Sequence

import torch
from torch import nn
from torch.nn import functional as F

_SUPPORTED = (
    "transformers.models.qwen4_exp.modeling_qwen4_exp",
    "Qwen4ExpTextExperts",
)
_DTYPES = {torch.float16, torch.bfloat16, torch.float32, torch.float64}


def _expert_shape(module: nn.Module) -> tuple[int, int, int]:
    if (type(module).__module__, type(module).__name__) != _SUPPORTED:
        raise ValueError("Only the verified Qwen4ExpTextExperts interface is supported")
    gu, down = module.gate_up_proj, module.down_proj
    if (type(gu) is not nn.Parameter or type(down) is not nn.Parameter
            or gu.ndim != 3 or down.ndim != 3 or gu.dtype not in _DTYPES
            or down.dtype != gu.dtype or gu.device != down.device
            or gu.device.type not in {"cpu", "cuda"}):
        raise ValueError("Experts require materialized, unquantized compatible packed weights")
    experts, twice_intermediate, hidden = gu.shape
    if (experts < 1 or hidden < 1 or twice_intermediate < 2 or twice_intermediate % 2
            or tuple(down.shape) != (experts, hidden, twice_intermediate // 2)
            or (module.num_experts, module.hidden_dim, module.intermediate_dim)
            != (experts, hidden, twice_intermediate // 2)):
        raise ValueError("Expert metadata and packed weight shapes disagree")
    return experts, hidden, twice_intermediate // 2


class _LowRank(nn.Module):
    def __init__(self, incoming: int, outgoing: int, rank: int, alpha: float, reference):
        super().__init__()
        dtype = torch.float64 if reference.dtype == torch.float64 else torch.float32
        self.a = nn.Parameter(torch.empty(rank, incoming, device=reference.device, dtype=dtype))
        self.b = nn.Parameter(torch.zeros(outgoing, rank, device=reference.device, dtype=dtype))
        nn.init.kaiming_uniform_(self.a, a=math.sqrt(5))
        self.scale = alpha / rank

    def forward(self, value):
        return F.linear(F.linear(value.to(self.a.dtype), self.a), self.b) * self.scale

    def delta(self):
        return (self.b @ self.a) * self.scale


class _ExpertAdapter(nn.Module):
    def __init__(self, hidden, intermediate, rank, alpha, reference):
        super().__init__()
        self.gate_up = _LowRank(hidden, 2 * intermediate, rank, alpha, reference)
        self.down = _LowRank(intermediate, hidden, rank, alpha, reference)


class SelectedExperts(nn.Module):
    """An in-memory training wrapper; it never changes a base weight tensor."""
    def __init__(self, base: nn.Module, expert_ids: Sequence[int], *, rank: int, alpha: float, _freeze_base=True):
        super().__init__()
        experts, hidden, intermediate = _expert_shape(base)
        ids = tuple(expert_ids)
        if (not ids or any(type(e) is not int or not 0 <= e < experts for e in ids)
                or len(set(ids)) != len(ids)):
            raise ValueError("Selected experts must be distinct in-range integer IDs")
        if type(rank) is not int or not 1 <= rank <= min(hidden, intermediate):
            raise ValueError("Rank must fit the expert's hidden and intermediate dimensions")
        if type(alpha) not in (int, float) or not math.isfinite(alpha) or alpha <= 0:
            raise ValueError("LoRA alpha must be finite and positive")
        self.base = base
        self.expert_ids = tuple(sorted(ids))
        self.rank, self.alpha = rank, float(alpha)
        self.num_experts, self.hidden_dim, self.intermediate_dim = experts, hidden, intermediate
        self.adapters = nn.ModuleDict({
            str(e): _ExpertAdapter(hidden, intermediate, rank, alpha, base.gate_up_proj)
            for e in self.expert_ids
        })
        if _freeze_base:
            self.base.requires_grad_(False)

    def forward(self, hidden_states, top_k_index, top_k_weights):
        if (hidden_states.ndim != 2 or hidden_states.shape[1] != self.hidden_dim
                or top_k_index.ndim != 2 or top_k_index.shape[0] != hidden_states.shape[0]
                or top_k_index.shape != top_k_weights.shape or top_k_index.shape[1] < 1
                or top_k_index.dtype != torch.int64 or not top_k_weights.is_floating_point()
                or top_k_index.device != hidden_states.device
                or top_k_weights.device != hidden_states.device
                or hidden_states.device != self.base.gate_up_proj.device):
            raise ValueError("Dispatch tensors do not match the expert interface")
        if ((top_k_index < 0).any().item() or (top_k_index > self.num_experts).any().item()
                or not torch.isfinite(top_k_weights).all().item()
                or (top_k_weights < 0).any().item()):
            raise ValueError("Dispatch contains invalid expert IDs or routing weights")
        final = torch.zeros_like(hidden_states)
        # No full [experts,tokens,top_k] one-hot allocation. The sentinel E is
        # deliberately ignored, matching the native implementation's masked route.
        for expert_id in torch.unique(top_k_index, sorted=True).tolist():
            if expert_id == self.num_experts:
                continue
            token_idx, route_idx = torch.where(top_k_index == expert_id)
            value = hidden_states[token_idx]
            gate_up = F.linear(value, self.base.gate_up_proj[expert_id])
            adapter = self.adapters[str(expert_id)] if str(expert_id) in self.adapters else None
            if adapter is not None:
                gate_up = gate_up + adapter.gate_up(value).to(gate_up.dtype)
            gate, up = gate_up.chunk(2, dim=-1)
            activated = self.base.act_fn(gate) * up
            result = F.linear(activated, self.base.down_proj[expert_id])
            if adapter is not None:
                result = result + adapter.down(activated).to(result.dtype)
            result = result * top_k_weights[token_idx, route_idx, None]
            final.index_add_(0, token_idx, result.to(final.dtype))
        return final

    @torch.no_grad()
    def merged_slices(self):
        """Yield selected CPU weight slices for a separate immutable export.

        Never merge into the active base or write model files. Full checkpoint
        export, quantization and inference-engine qualification belong to its owner.
        """
        if any(not torch.isfinite(p).all().item() for p in self.adapters.parameters()):
            raise ValueError("Cannot export non-finite adapter weights")
        for expert_id in self.expert_ids:
            adapter = self.adapters[str(expert_id)]
            gu = self.base.gate_up_proj[expert_id]
            down = self.base.down_proj[expert_id]
            merged = {
                "gate_up_proj": (gu.to(adapter.gate_up.a.dtype) + adapter.gate_up.delta()).to(gu.dtype).cpu().clone(),
                "down_proj": (down.to(adapter.down.a.dtype) + adapter.down.delta()).to(down.dtype).cpu().clone(),
            }
            if any(not torch.isfinite(value).all().item() for value in merged.values()):
                raise ValueError("Merged expert arithmetic or output dtype overflowed")
            yield expert_id, merged


def attach_selected_experts(model: nn.Module, selections: Mapping[str, Sequence[int]],
                            *, rank: int, alpha: float) -> dict[str, SelectedExperts]:
    """Validate all paths before freezing the model and installing adapters.

    Caller supplies an independently admitted, materialized model. Inference
    offload hooks and quantized packed tensors require another qualified backend.
    """
    if not isinstance(selections, Mapping) or not selections:
        raise ValueError("An explicit nonempty expert selection is required")
    if any(isinstance(module, SelectedExperts) for module in model.modules()):
        raise ValueError("This model already has an expert adapter plan")
    if any(hasattr(module, "_hf_hook") for module in model.modules()):
        raise ValueError("Inference dispatch/offload hooks are not a training backend")
    if any(p.device.type not in {"cpu", "cuda"} for p in model.parameters()):
        raise ValueError("The base model must be fully materialized on admitted devices")
    modules = dict(model.named_modules())
    occurrences = Counter(id(module) for _, module in model.named_modules(remove_duplicate=False))
    parameter_occurrences = Counter(id(p) for _, p in model.named_parameters(remove_duplicate=False))
    # Validate everything without mutating a base requires_grad flag.
    for path, ids in selections.items():
        if type(path) is not str or not path or path not in modules:
            raise ValueError("Every selection must name one existing expert module")
        module = modules[path]
        parent_path, _, attr = path.rpartition(".")
        if modules[parent_path]._modules.get(attr) is not module:
            raise ValueError("An expert must be an ordinary registered child module")
        experts, hidden, intermediate = _expert_shape(module)
        if occurrences[id(module)] != 1 or any(parameter_occurrences[id(p)] != 1 for p in module.parameters()):
            raise ValueError("Aliased expert modules or weights need an explicit shared adapter policy")
        if (not isinstance(ids, (tuple, list)) or not ids
                or any(type(e) is not int or not 0 <= e < experts for e in ids)
                or len(set(ids)) != len(ids)):
            raise ValueError("Each selection must contain distinct in-range expert IDs")
        if type(rank) is not int or not 1 <= rank <= min(hidden, intermediate):
            raise ValueError("Rank is incompatible with an expert module")
    if type(alpha) not in (int, float) or not math.isfinite(alpha) or alpha <= 0:
        raise ValueError("LoRA alpha must be finite and positive")
    # Construction can allocate memory but never updates weight contents.
    wrappers = {path: SelectedExperts(modules[path], selections[path], rank=rank, alpha=alpha, _freeze_base=False)
                for path in sorted(selections)}
    model.requires_grad_(False)
    for p in model.parameters():
        p.grad = None
    for path, wrapper in wrappers.items():
        parent_path, _, attr = path.rpartition(".")
        parent = modules[parent_path]
        parent._modules[attr] = wrapper
        wrapper.adapters.requires_grad_(True)
    return wrappers


def trainable_adapters(model: nn.Module) -> dict[str, nn.Parameter]:
    expected = {
        id(parameter)
        for module in model.modules() if isinstance(module, SelectedExperts)
        for parameter in module.adapters.parameters()
    }
    actual = {name: p for name, p in model.named_parameters() if p.requires_grad}
    if not expected or {id(p) for p in actual.values()} != expected:
        raise ValueError("Trainable parameters must be exactly the selected expert adapters")
    return actual


def adapter_plan(wrappers: Mapping[str, SelectedExperts]) -> dict:
    return {
        path: {"experts": list(w.expert_ids), "rank": w.rank, "alpha": w.alpha,
               "packed_shapes": [list(w.base.gate_up_proj.shape), list(w.base.down_proj.shape)]}
        for path, w in sorted(wrappers.items())
    }
