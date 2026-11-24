"""Pinned, same-descriptor binary reader for one admitted Qwen3.5 rank."""

from contextlib import ExitStack
from dataclasses import dataclass
import hashlib
import json
from math import prod
import os
from pathlib import Path
import re
import sys

from .artifacts import CustodyError, _open_regular
from .qwen35_shard_plan import (
    MAX_SHARD_BYTES, MAX_SHARDS, MAX_TENSORS, RankPlan, SHARD, TensorHeader, TensorPlacement, _rank,
)

_DIGEST = re.compile(r"[0-9a-f]{64}")
_INDEX_NAME = "model.safetensors.index.json"
_META_LIMIT = 16 * 1024 * 1024
_CHUNK = 1024 * 1024


@dataclass(frozen=True, slots=True)
class ShardPin:
    name: str
    file_bytes: int
    header_sha256: str  # includes the 8-byte length prefix


@dataclass(frozen=True, slots=True)
class TensorPin:
    key: str
    sha256: str


@dataclass(frozen=True, slots=True)
class RankShardManifest:
    """Trusted pins. Index/header metadata alone cannot authenticate tensor bytes."""
    index_sha256: str
    shards: tuple[ShardPin, ...]
    tensors: tuple[TensorPin, ...]


def _is_digest(value):
    return type(value) is str and _DIGEST.fullmatch(value) is not None


def _pairs(items):
    result = {}
    for key, value in items:
        if key in result:
            raise CustodyError("Duplicate key in pinned Safetensors metadata.")
        result[key] = value
    return result


def _document(raw):
    try:
        doc = json.loads(raw.decode("utf-8"), object_pairs_hook=_pairs)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise CustodyError("Invalid pinned Safetensors metadata.") from error
    if type(doc) is not dict:
        raise CustodyError("Pinned Safetensors metadata must be an object.")
    return doc


class RankTensorReader:
    """Use inside a context manager, with one caller accessing the open rank."""

    def __init__(self, root: Path, plan: RankPlan, manifest: RankShardManifest, *, max_tensor_bytes: int):
        if (type(plan) is not RankPlan or type(manifest) is not RankShardManifest
                or not _is_digest(manifest.index_sha256)
                or type(max_tensor_bytes) is not int or not 0 < max_tensor_bytes <= MAX_SHARD_BYTES
                or type(plan.tensors) is not tuple or not 0 < len(plan.tensors) <= MAX_TENSORS
                or type(plan.shards) is not tuple or not 0 < len(plan.shards) <= MAX_SHARDS
                or type(manifest.shards) is not tuple or len(manifest.shards) != len(plan.shards)
                or type(manifest.tensors) is not tuple or len(manifest.tensors) != len(plan.tensors)):
            raise CustodyError("Invalid pinned Qwen3.5 rank reader policy.")
        span = (plan.start_layer, plan.stop_layer)
        if any(type(i) is not int for i in span) or span not in ((0, 32), (32, 64)):
            raise CustodyError("The reader requires an admitted Qwen3.5 rank range.")
        rank = int(plan.start_layer == 32)
        self._root, self._rank, self._index = Path(root), rank, manifest.index_sha256
        self._placements, self._pins, self._digests = {}, {}, {}
        for place in plan.tensors:
            if (type(place) is not TensorPlacement or type(place.key) is not str
                    or len(place.key) > 256 or place.key in self._placements
                    or type(place.shard) is not str or not SHARD.fullmatch(place.shard)
                    or type(place.header) is not TensorHeader):
                raise CustodyError("The plan contains an invalid Qwen3.5 placement.")
            try:
                owned, _ = _rank(place.key)
            except ValueError as error:
                raise CustodyError("The plan contains an unsupported Qwen3.5 tensor.") from error
            h = place.header
            if (owned != rank or h.dtype != "BF16" or type(h.start) is not int or type(h.end) is not int
                    or not 0 <= h.start < h.end <= MAX_SHARD_BYTES or h.end - h.start > max_tensor_bytes
                    or type(h.shape) is not tuple or not 1 <= len(h.shape) <= 8
                    or any(type(d) is not int or not 0 < d <= MAX_SHARD_BYTES // 2 for d in h.shape)
                    or 2 * prod(h.shape) != h.end - h.start):
                raise CustodyError("The plan contains a foreign or oversized BF16 tensor.")
            self._placements[place.key] = place
        for pin in manifest.shards:
            if (type(pin) is not ShardPin or type(pin.name) is not str or not SHARD.fullmatch(pin.name)
                    or pin.name in self._pins or not _is_digest(pin.header_sha256)
                    or type(pin.file_bytes) is not int or not 8 < pin.file_bytes <= MAX_SHARD_BYTES + _META_LIMIT + 8):
                raise CustodyError("Invalid pinned Qwen3.5 shard.")
            self._pins[pin.name] = pin
        for pin in manifest.tensors:
            if (type(pin) is not TensorPin or type(pin.key) is not str or pin.key in self._digests
                    or not _is_digest(pin.sha256)):
                raise CustodyError("Invalid pinned Qwen3.5 tensor digest.")
            self._digests[pin.key] = pin.sha256
        if (tuple(sorted(self._pins)) != plan.shards
                or set(self._pins) != {place.shard for place in self._placements.values()}
                or self._digests.keys() != self._placements.keys()
                or type(plan.frozen_backing_bytes) is not int
                or plan.frozen_backing_bytes != sum(p.header.end - p.header.start for p in self._placements.values())):
            raise CustodyError("The rank manifest does not exactly cover the admitted placements.")
        self._stack = None
        self._open = {}

    def __enter__(self):
        if self._stack is not None:
            raise CustodyError("The rank tensor reader is already open.")
        with ExitStack() as stack:
            with _open_regular(self._root / _INDEX_NAME) as source:
                index = source.read(_META_LIMIT + 1)
            if len(index) > _META_LIMIT or hashlib.sha256(index).hexdigest() != self._index:
                raise CustodyError("The pinned Safetensors index failed verification.")
            weights = _document(index).get("weight_map")
            if (type(weights) is not dict or not 0 < len(weights) <= MAX_TENSORS
                    or any(not 0 < len(k) <= 256 or type(v) is not str or not SHARD.fullmatch(v)
                           for k, v in weights.items())):
                raise CustodyError("Invalid pinned Safetensors weight map.")
            try:
                owned_keys = {key for key in weights if _rank(key)[0] == self._rank}
            except ValueError as error:
                raise CustodyError("The pinned index includes an unsupported Qwen3.5 namespace.") from error
            if (owned_keys != self._placements.keys()
                    or any(weights[place.key] != place.shard for place in self._placements.values())):
                raise CustodyError("The pinned Safetensors index differs from the rank plan.")
            opened = {}
            for name, pin in self._pins.items():
                # Keep the validated file descriptor, but bypass BufferedReader
                # so header-only admission cannot prefetch unowned payload.
                source = stack.enter_context(_open_regular(self._root / name)).raw
                size = os.fstat(source.fileno()).st_size
                length_bytes = source.read(8)
                length = int.from_bytes(length_bytes, "little")
                if (size != pin.file_bytes or len(length_bytes) != 8
                        or not 0 < length <= _META_LIMIT or 8 + length >= size):
                    raise CustodyError("The pinned Safetensors shard size is invalid.")
                raw = source.read(length)
                if len(raw) != length or hashlib.sha256(length_bytes + raw).hexdigest() != pin.header_sha256:
                    raise CustodyError("The pinned Safetensors header failed verification.")
                header = _document(raw)
                header.pop("__metadata__", None)
                if set(header) != {key for key, value in weights.items() if value == name}:
                    raise CustodyError("The pinned Safetensors header and index disagree.")
                intervals = []
                for value in header.values():
                    if (type(value) is not dict or set(value) != {"dtype", "shape", "data_offsets"}
                            or value["dtype"] != "BF16" or type(value["shape"]) is not list
                            or not 1 <= len(value["shape"]) <= 8
                            or any(type(dim) is not int or not 0 < dim <= MAX_SHARD_BYTES // 2
                                   for dim in value["shape"])
                            or type(value["data_offsets"]) is not list or len(value["data_offsets"]) != 2
                            or any(type(offset) is not int for offset in value["data_offsets"])):
                        raise CustodyError("Invalid pinned BF16 tensor header.")
                    start, end = value["data_offsets"]
                    if not 0 <= start < end <= size - (8 + length) or end - start != 2 * prod(value["shape"]):
                        raise CustodyError("Invalid pinned BF16 tensor interval.")
                    intervals.append((start, end))
                cursor = 0
                for start, end in sorted(intervals):
                    if start != cursor:
                        raise CustodyError("The pinned Safetensors payload has overlapping or missing intervals.")
                    cursor = end
                if cursor != size - (8 + length):
                    raise CustodyError("The pinned Safetensors payload size differs from its intervals.")
                for key in header.keys() & self._placements.keys():
                    h = self._placements[key].header
                    if (header[key] != {"dtype": "BF16", "shape": list(h.shape), "data_offsets": [h.start, h.end]}
                            or h.end > size - (8 + length)):
                        raise CustodyError("The pinned Safetensors header differs from the owned placement.")
                opened[name] = (source, 8 + length)
            self._open = opened
            self._stack = stack.pop_all()
        return self

    def __exit__(self, *exc):
        if self._stack is not None:
            self._stack.close()
            self._stack = None
            self._open = {}

    def _read_bytes(self, placement: TensorPlacement) -> bytearray:
        if (self._stack is None or type(placement) is not TensorPlacement
                or type(placement.key) is not str or self._placements.get(placement.key) != placement):
            raise CustodyError("The tensor is not an open, admitted Qwen3.5 placement.")
        placement = self._placements[placement.key]  # use the admitted record, never request fields
        source, payload = self._open[placement.shard]
        header = placement.header
        source.seek(payload + header.start)
        data = bytearray(header.end - header.start)
        digest = hashlib.sha256()
        with memoryview(data) as view:
            offset = 0
            while offset < len(view):
                span = view[offset:offset + _CHUNK]
                count = source.readinto(span)
                if not count:
                    raise CustodyError("The admitted BF16 tensor is truncated.")
                digest.update(span[:count])
                offset += count
        if digest.hexdigest() != self._digests[placement.key]:
            raise CustodyError("The admitted BF16 tensor failed its digest check.")
        return data

    def __call__(self, placement: TensorPlacement):
        if sys.byteorder != "little":
            raise CustodyError("The BF16 shard reader requires a little-endian CPU.")
        import torch
        data = self._read_bytes(placement)
        shape = self._placements[placement.key].header.shape
        return torch.frombuffer(data, dtype=torch.bfloat16).reshape(shape)
