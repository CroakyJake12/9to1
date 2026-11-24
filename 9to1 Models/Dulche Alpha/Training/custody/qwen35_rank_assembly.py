"""Assemble one Qwen3.5 rank from a trusted shard plan and tensor reader."""

from dataclasses import dataclass
from math import prod
from typing import Callable

from .artifacts import CustodyError
from .qwen35_range import Qwen35TextRange, _TRANSFORMERS
from .qwen35_shard_plan import HEAD, MAX_SHARD_BYTES, RankPlan, SHARD, TensorHeader, TensorPlacement

_TEXT = "model.language_model."


@dataclass(frozen=True, slots=True)
class AssembledQwen35Rank:
    text: Qwen35TextRange
    lm_head: object | None
    deferred: tuple[TensorPlacement, ...] = ()


def assemble_qwen35_rank(config, plan: RankPlan, *, split: int, rank: int,
                         read_tensor: Callable[[TensorPlacement], object],
                         defer_v100_weights: bool = False) -> AssembledQwen35Rank:
    """Assign owned BF16 CPU tensors in place, without full-model weight storage.

    Reader must verify the admitted shard identity independently and read only
    the exact placement supplied. By default this is CPU backing, not a
    VRAM fit claim. The streamed V100 placement explicitly opts out of
    loading its exact owned linear and embedding weights; a pager must
    replace those named placeholders before any use.
    """
    import torch
    import transformers
    from transformers.models.qwen3_5.configuration_qwen3_5 import Qwen3_5TextConfig
    from transformers.models.qwen3_5.modeling_qwen3_5 import Qwen3_5TextModel

    if (transformers.__version__ != _TRANSFORMERS or type(config) is not Qwen3_5TextConfig
            or config.tie_word_embeddings is not False or type(split) is not int
            or not 0 < split < config.num_hidden_layers or type(rank) is not int or rank not in (0, 1)
            or type(plan) is not RankPlan or not callable(read_tensor)
            or type(defer_v100_weights) is not bool or (defer_v100_weights and rank != 0)):
        raise CustodyError("Unadmitted Qwen3.5 rank assembly configuration.")
    span = (0, split) if rank == 0 else (split, config.num_hidden_layers)
    if (type(plan.start_layer) is not int or type(plan.stop_layer) is not int
            or (plan.start_layer, plan.stop_layer) != span or type(plan.tensors) is not tuple
            or type(plan.shards) is not tuple or type(plan.frozen_backing_bytes) is not int):
        raise CustodyError("The shard plan does not match the owned Qwen3.5 range.")

    # Construct unloaded scaffolding. Do not call from_pretrained or to_empty.
    with torch.device("meta"):
        native = Qwen3_5TextModel(config)
        head = torch.nn.Linear(config.hidden_size, config.vocab_size, bias=False) if rank == 1 else None
    if any(not parameter.is_meta for parameter in native.parameters()):
        raise CustodyError("Native Qwen3.5 assembly initialized non-meta model weights.")
    # Rotary inv_freq/original_inv_freq are derived, non-checkpoint state.
    with torch.device("cpu"):
        native.rotary_emb = type(native.rotary_emb)(config)
    text = Qwen35TextRange(native, split=split, rank=rank)
    owned = {f"{_TEXT}layers.{i}.": native.layers[i] for i in range(*span)}
    edge_name = "embed_tokens" if rank == 0 else "norm"
    owned[f"{_TEXT}{edge_name}."] = getattr(native, edge_name)
    if head is not None:
        owned["lm_head."] = head
    del native

    expected = {prefix + name: tuple(tensor.shape) for prefix, module in owned.items()
                for name, tensor in module.state_dict().items()}
    deferred_keys = set()
    if defer_v100_weights:
        for prefix, module in owned.items():
            for relative, child in module.named_modules():
                if type(child) in (torch.nn.Linear, torch.nn.Embedding):
                    key = prefix + (relative + "." if relative else "") + "weight"
                    if key not in expected or child.weight.device.type != "meta":
                        raise CustodyError("Unexpected streamed V100 placeholder.")
                    deferred_keys.add(key)
        if f"{_TEXT}embed_tokens.weight" not in deferred_keys or len(deferred_keys) < 2:
            raise CustodyError("No admitted V100 projections or token embedding to stream.")
    placements = {}
    for place in plan.tensors:
        if (type(place) is not TensorPlacement or type(place.key) is not str or place.key not in expected
                or place.key in placements or type(place.shard) is not str or not SHARD.fullmatch(place.shard)
                or type(place.header) is not TensorHeader or place.header.dtype != "BF16"
                or type(place.header.shape) is not tuple or place.header.shape != expected[place.key]
                or any(type(dim) is not int for dim in place.header.shape)
                or type(place.header.start) is not int or type(place.header.end) is not int
                or not 0 <= place.header.start < place.header.end <= MAX_SHARD_BYTES
                or place.header.end - place.header.start != 2 * prod(place.header.shape)):
            raise CustodyError("The shard plan contains a foreign, duplicate, or incompatible Qwen3.5 tensor.")
        placements[place.key] = place
    if (placements.keys() != expected.keys() or (HEAD in placements) != (rank == 1)
            or plan.shards != tuple(sorted({place.shard for place in placements.values()}))
            or plan.frozen_backing_bytes != sum(place.header.end - place.header.start for place in placements.values())):
        raise CustodyError("The shard plan is incomplete or disagrees with the owned BF16 backing bytes.")

    for prefix, module in owned.items():
        data = {}
        for name, slot in module.state_dict().items():
            key = prefix + name
            place = placements[key]
            if key in deferred_keys:
                data[name] = slot.to(dtype=torch.bfloat16)
                continue
            tensor = read_tensor(place)
            if (type(tensor) is not torch.Tensor or tensor.device.type != "cpu" or tensor.layout != torch.strided
                    or not tensor.is_contiguous() or tensor.requires_grad or tensor.is_neg()
                    or tensor.dtype != torch.bfloat16 or tuple(tensor.shape) != place.header.shape):
                raise CustodyError("The admitted Qwen3.5 tensor does not match its BF16 CPU placement.")
            data[name] = tensor
        module.load_state_dict(data, strict=True, assign=True)

    for prefix, module in (*owned.items(), ("", text._rotary)):
        for tensors in (module.named_parameters(), module.named_buffers()):
            for name, tensor in tensors:
                device_type = "meta" if prefix + name in deferred_keys else "cpu"
                if tensor.device.type != device_type:
                    raise CustodyError("A rank-local parameter or buffer differs from its admitted placement.")
    deferred = tuple(place for place in plan.tensors if place.key in deferred_keys)
    return AssembledQwen35Rank(text, head, deferred)
