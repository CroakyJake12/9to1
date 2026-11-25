"""Sparse routed-expert adapters over immutable, bounded GGUF reads.

The native router still chooses experts and routing weights. Only chosen adapter
parameters are resident and trainable; base expert matrices are rematerialized
for their input gradients by the paging primitive.
"""
from __future__ import annotations

import math
from collections.abc import Mapping, Sequence

import torch
from torch import nn
from torch.nn import functional as F

from .adapters import SelectedExperts, _ExpertAdapter
from .paging import frozen_linear, _dtype, _device


def _selection(ids, experts, hidden, intermediate, rank, alpha):
    if (not isinstance(ids, (tuple, list)) or
            any(type(e) is not int or not 0 <= e < experts for e in ids)
            or len(set(ids)) != len(ids)):
        raise ValueError("Paged expert selections must contain distinct in-range integer IDs")
    if type(rank) is not int or not 1 <= rank <= min(hidden, intermediate):
        raise ValueError("Rank must fit the expert hidden and intermediate dimensions")
    if type(alpha) not in (int, float) or not math.isfinite(alpha) or alpha <= 0:
        raise ValueError("LoRA alpha must be finite and positive")
    return tuple(sorted(ids))


class PagedExperts(SelectedExperts):
    """Native packed-expert interface without resident base Parameters.

    An empty selection is an explicitly frozen calibration graph. Selecting
    adapters is a separate transaction that must precede optimizer construction.
    """
    def __init__(self, store, layer_idx, expert_ids=(), *, rank=8, alpha=16,
                 device="cpu", dtype=torch.float32):
        nn.Module.__init__(self)
        if type(layer_idx) is not int or layer_idx < 0:
            raise ValueError("A nonnegative native layer index is required")
        names = tuple(f"blk.{layer_idx}.ffn_{projection}_exps.weight"
                      for projection in ("gate", "up", "down"))
        infos = [store.tensor_info(name) for name in names]
        shapes = [info.get("shape") for info in infos]
        gate, up, down = shapes
        if (not isinstance(gate, tuple) or len(gate) != 3
                or any(type(n) is not int or n <= 0 for n in gate)
                or up != gate or down != (gate[0], gate[2], gate[1])):
            raise ValueError("GGUF gate/up/down shapes disagree with native expert layout")
        experts, intermediate, hidden = gate
        ids = _selection(expert_ids, experts, hidden, intermediate, rank, alpha)
        device, dtype = _device(device), _dtype(dtype)
        source = getattr(store, "source_identity", None)
        if type(source) is not str or len(source) != 64 or any(c not in "0123456789abcdef" for c in source):
            raise ValueError("Paged experts require an immutable source identity")
        self.store, self.layer_idx = store, layer_idx
        self.tensor_names, self.quantization = names, tuple(info["qtype"] for info in infos)
        self.source_identity = source
        self.num_experts, self.hidden_dim, self.intermediate_dim = experts, hidden, intermediate
        self.rank, self.alpha, self.expert_ids = rank, float(alpha), ids
        self.register_buffer("_anchor", torch.empty(0, device=device, dtype=dtype), persistent=False)
        self.adapters = nn.ModuleDict({
            str(e): _ExpertAdapter(hidden, intermediate, rank, alpha, self._anchor)
            for e in ids
        })

    @property
    def device(self):
        return self._anchor.device

    @property
    def dtype(self):
        return self._anchor.dtype

    def _refs(self, expert_id):
        if self.store.source_identity != self.source_identity:
            raise ValueError("Paged expert source changed after graph construction")
        return tuple(self.store.matrix_ref(name, expert=expert_id) for name in self.tensor_names)

    def forward(self, hidden_states, top_k_index, top_k_weights):
        if (not all(isinstance(t, torch.Tensor) for t in (hidden_states, top_k_index, top_k_weights))
                or hidden_states.ndim != 2 or hidden_states.shape[1] != self.hidden_dim
                or top_k_index.ndim != 2 or top_k_index.shape[0] != hidden_states.shape[0]
                or top_k_index.shape != top_k_weights.shape or top_k_index.shape[1] < 1
                or top_k_index.dtype != torch.int64 or not top_k_weights.is_floating_point()
                or top_k_index.device != hidden_states.device
                or top_k_weights.device != hidden_states.device
                or hidden_states.device != self.device or hidden_states.dtype != self.dtype):
            raise ValueError("Dispatch tensors do not match the paged expert interface")
        if ((top_k_index < 0).any().item() or (top_k_index > self.num_experts).any().item()
                or not torch.isfinite(top_k_weights).all().item()
                or (top_k_weights < 0).any().item()):
            raise ValueError("Dispatch contains invalid expert IDs or routing weights")
        final = torch.zeros_like(hidden_states)
        # Match the native masked-route sentinel without a full E x tokens x K mask.
        for expert_id in torch.unique(top_k_index, sorted=True).tolist():
            if expert_id == self.num_experts:
                continue
            token_idx, route_idx = torch.where(top_k_index == expert_id)
            value = hidden_states[token_idx]
            gate_ref, up_ref, down_ref = self._refs(expert_id)
            gate = frozen_linear(value, self.store, gate_ref)
            up = frozen_linear(value, self.store, up_ref)
            adapter = self.adapters[str(expert_id)] if str(expert_id) in self.adapters else None
            if adapter is not None:
                gate_delta, up_delta = adapter.gate_up(value).chunk(2, dim=-1)
                gate, up = gate + gate_delta.to(gate.dtype), up + up_delta.to(up.dtype)
            activated = F.silu(gate) * up
            result = frozen_linear(activated, self.store, down_ref)
            if adapter is not None:
                result = result + adapter.down(activated).to(result.dtype)
            result = result * top_k_weights[token_idx, route_idx, None]
            final.index_add_(0, token_idx, result.to(final.dtype))
        return final

    def plan_record(self):
        return {
            "experts": list(self.expert_ids), "rank": self.rank, "alpha": self.alpha,
            "packed_shapes": [[self.num_experts, 2 * self.intermediate_dim, self.hidden_dim],
                              [self.num_experts, self.hidden_dim, self.intermediate_dim]],
            "backend": "gguf-paged-experts-v1", "base_source_identity": self.source_identity,
            "tensors": list(self.tensor_names), "quantization": list(self.quantization),
        }

    @torch.no_grad()
    def merged_slices(self):
        """Yield one selected dequantized CPU expert; never modify a source shard."""
        if any(not torch.isfinite(p).all().item() for p in self.adapters.parameters()):
            raise ValueError("Cannot export non-finite adapter weights")
        for expert_id in self.expert_ids:
            adapter = self.adapters[str(expert_id)]
            gate_ref, up_ref, down_ref = self._refs(expert_id)
            gate_up = torch.cat([torch.from_numpy(self.store.read(ref)) for ref in (gate_ref, up_ref)])
            down = torch.from_numpy(self.store.read(down_ref))
            merged = {
                "gate_up_proj": gate_up + adapter.gate_up.delta().detach().float().cpu(),
                "down_proj": down + adapter.down.delta().detach().float().cpu(),
            }
            if any(not torch.isfinite(value).all().item() for value in merged.values()):
                raise ValueError("Merged expert arithmetic overflowed")
            yield expert_id, merged


def attach_paged_experts(model, selections: Mapping[str, Sequence[int]], *, rank, alpha):
    """Stage a complete observed plan before changing a frozen calibration graph.

    Re-selection after any adapter installation is refused, so this path cannot
    replace parameters underneath an existing adapter optimizer.
    """
    if not isinstance(model, nn.Module) or not isinstance(selections, Mapping) or not selections:
        raise ValueError("A native model and nonempty observed expert plan are required")
    modules = dict(model.named_modules())
    wrappers = {path: module for path, module in modules.items() if type(module) is PagedExperts}
    if not wrappers or set(selections) != set(wrappers):
        raise ValueError("The selection must cover every paged expert layer exactly")
    if any(wrapper.expert_ids or len(wrapper.adapters) for wrapper in wrappers.values()):
        raise ValueError("Adapters have already been selected on this graph")
    if any(p.requires_grad or p.grad is not None for p in model.parameters()):
        raise ValueError("Selection requires an entirely frozen calibration graph")
    if any(hasattr(module, "_hf_hook") for module in model.modules()):
        raise ValueError("Inference offload hooks are not supported by this training backend")
    aliases = [id(module) for _, module in model.named_modules(remove_duplicate=False)
               if type(module) is PagedExperts]
    if len(aliases) != len(set(aliases)):
        raise ValueError("Aliased expert layers require an explicit shared-adapter policy")
    staged = {}
    for path, wrapper in wrappers.items():
        ids = _selection(selections[path], wrapper.num_experts, wrapper.hidden_dim,
                         wrapper.intermediate_dim, rank, alpha)
        if not ids:
            raise ValueError("Every observed layer must select at least one expert")
        staged[path] = (ids, nn.ModuleDict({
            str(e): _ExpertAdapter(wrapper.hidden_dim, wrapper.intermediate_dim,
                                   rank, alpha, wrapper._anchor) for e in ids
        }))
    for path, (ids, adapters) in staged.items():
        wrapper = wrappers[path]
        wrapper.adapters = adapters
        wrapper.expert_ids, wrapper.rank, wrapper.alpha = ids, rank, float(alpha)
    return wrappers
