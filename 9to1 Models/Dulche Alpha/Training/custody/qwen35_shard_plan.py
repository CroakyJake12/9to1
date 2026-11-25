"""Plan the two local Qwen 27B text ranges from admitted Safetensors metadata.

This maps ranges, not provenance: the caller must pin and verify the index,
configuration, and file identities before loading. No files are opened here.
Reported bytes are the frozen BF16 tensor payload only, not a RAM/VRAM fit
claim; scratch buffers, converted tensors, adapters and optimizer are extra.
"""
from dataclasses import dataclass
from math import prod
import re
from typing import Mapping


GIB = 1 << 30
MAX_SHARD_BYTES = 8 * GIB
MAX_MODEL_BYTES = 128 * GIB
MAX_TENSORS = 16_384
MAX_SHARDS = 128
SHARD = re.compile(r"model-[0-9]{5}-of-[0-9]{5}\.safetensors", re.ASCII)
IDENTIFIER = r"[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*"
BLOCK = re.compile(r"model\.language_model\.layers\.(0|[1-9][0-9]?)\." + IDENTIFIER, re.ASCII)
SIBLING = re.compile(
    r"(?:model\.visual\.(?:blocks|merger|patch_embed|pos_embed)|mtp\.layers\.0)\." + IDENTIFIER,
    re.ASCII,
)
EMBEDDING = "model.language_model.embed_tokens.weight"
NORM = "model.language_model.norm.weight"
HEAD = "lm_head.weight"
MTP_ROOT_KEYS = frozenset(
    {"mtp.fc.weight", "mtp.norm.weight", "mtp.pre_fc_norm_embedding.weight", "mtp.pre_fc_norm_hidden.weight"}
)


@dataclass(frozen=True)
class TensorHeader:
    dtype: str
    shape: tuple[int, ...]
    start: int
    end: int


@dataclass(frozen=True)
class ShardHeader:
    # Measured size of the payload region after the external header envelope.
    payload_bytes: int
    tensors: Mapping[str, TensorHeader]


@dataclass(frozen=True)
class TensorPlacement:
    key: str
    shard: str
    header: TensorHeader


@dataclass(frozen=True)
class RankPlan:
    start_layer: int
    stop_layer: int  # exclusive, in the original model numbering
    tensors: tuple[TensorPlacement, ...]
    shards: tuple[str, ...]
    frozen_backing_bytes: int


@dataclass(frozen=True)
class ShardPlan:
    ranks: tuple[RankPlan, RankPlan]
    omitted_tensors: int
    omitted_bytes: int


def _rank(key: str) -> tuple[int | None, int | None]:
    if key == EMBEDDING:
        return 0, None
    if key in (NORM, HEAD):
        return 1, None
    m = BLOCK.fullmatch(key)
    if m and (layer := int(m[1])) < 64:
        return int(layer >= 32), layer
    if key in MTP_ROOT_KEYS or SIBLING.fullmatch(key):
        return None, None
    raise ValueError(f"unsupported tensor namespace: {key}")


def plan_qwen35_text_shards(
    weight_map: Mapping[str, str],
    headers: Mapping[str, ShardHeader],
    *,
    index_payload_bytes: int | float,
    tied_embeddings: bool,
) -> ShardPlan:
    """Validate complete metadata and place original blocks 0–31 and 32–63."""
    if tied_embeddings is not False:
        raise ValueError("expected an explicitly untied text embedding and head")
    # The real index encodes its byte count as a whole-number JSON float.
    if (type(index_payload_bytes) not in (int, float)
            or not 0 < index_payload_bytes <= MAX_MODEL_BYTES
            or int(index_payload_bytes) != index_payload_bytes):
        raise ValueError("unsafe index payload size")
    if not 0 < len(weight_map) <= MAX_TENSORS or not 0 < len(headers) <= MAX_SHARDS:
        raise ValueError("unsafe tensor or shard count")

    indexed: dict[str, set[str]] = {}
    for key, name in weight_map.items():
        if type(key) is not str or not 0 < len(key) <= 256:
            raise ValueError("invalid tensor key")
        if type(name) is not str or not SHARD.fullmatch(name):
            raise ValueError("invalid relative shard name")
        indexed.setdefault(name, set()).add(key)
    if set(headers) != set(indexed):
        raise ValueError("header set disagrees with the index")

    total = omitted_bytes = omitted_tensors = 0
    placements: tuple[list[TensorPlacement], list[TensorPlacement]] = ([], [])
    layers: set[int] = set()
    outer_shapes: dict[str, tuple[int, ...]] = {}
    for name in sorted(headers):
        shard = headers[name]
        if (type(shard) is not ShardHeader or type(shard.payload_bytes) is not int
                or not 0 < shard.payload_bytes <= MAX_SHARD_BYTES):
            raise ValueError("unsafe shard payload size")
        if len(shard.tensors) != len(indexed[name]) or set(shard.tensors) != indexed[name]:
            raise ValueError("tensor set disagrees with the shard index")
        total += shard.payload_bytes
        if total > index_payload_bytes:
            raise ValueError("shard payload total exceeds the index")
        intervals = []
        for key in sorted(indexed[name]):
            t = shard.tensors[key]
            if (type(t) is not TensorHeader or t.dtype != "BF16"
                    or type(t.shape) is not tuple or not 1 <= len(t.shape) <= 8
                    or any(type(n) is not int or not 0 < n <= MAX_SHARD_BYTES // 2 for n in t.shape)
                    or type(t.start) is not int or type(t.end) is not int
                    or not 0 <= t.start < t.end <= shard.payload_bytes
                    or t.end - t.start != 2 * prod(t.shape)):
                raise ValueError("invalid BF16 tensor size or offsets")
            intervals.append((t.start, t.end))
            rank, layer = _rank(key)
            if rank is None:
                omitted_tensors += 1
                omitted_bytes += t.end - t.start
            else:
                placements[rank].append(TensorPlacement(key, name, t))
                if layer is not None:
                    layers.add(layer)
                else:
                    outer_shapes[key] = t.shape
        cursor = 0
        for start, end in sorted(intervals):
            if start != cursor:
                raise ValueError("shard payload offsets have gaps or overlaps")
            cursor = end
        if cursor != shard.payload_bytes:
            raise ValueError("shard payload has unindexed bytes")

    if total != index_payload_bytes:
        raise ValueError("shard payload total disagrees with the index")
    if layers != set(range(64)):
        raise ValueError("metadata lacks the complete original decoder layer ranges")
    if set(outer_shapes) != {EMBEDDING, NORM, HEAD}:
        raise ValueError("metadata lacks the required embedding, norm or head")
    embedding = outer_shapes[EMBEDDING]
    if len(embedding) != 2 or outer_shapes[HEAD] != embedding or outer_shapes[NORM] != (embedding[1],):
        raise ValueError("embedding, norm and head dimensions disagree")

    def rank_plan(rank: int) -> RankPlan:
        assigned = tuple(sorted(placements[rank], key=lambda p: p.key))
        return RankPlan(
            32 * rank, 32 * (rank + 1), assigned,
            tuple(sorted({p.shard for p in assigned})),
            sum(p.header.end - p.header.start for p in assigned),
        )

    return ShardPlan((rank_plan(0), rank_plan(1)), omitted_tensors, omitted_bytes)
