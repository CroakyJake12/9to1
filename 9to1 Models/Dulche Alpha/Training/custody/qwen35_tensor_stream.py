"""Transfer one verified Qwen rank's original BF16 backing over an owned stream."""

from dataclasses import asdict
import hashlib
from math import prod
import struct
import sys
from uuid import UUID

from .artifacts import CustodyError, _encode_record, canonical_id
from .qwen35_shard_plan import (
    MAX_MODEL_BYTES, MAX_SHARD_BYTES, MAX_SHARDS, MAX_TENSORS, RankPlan, SHARD,
    TensorHeader, TensorPlacement, _rank,
)
from .qwen35_shard_reader import RankShardManifest, RankTensorReader, ShardPin, TensorPin, _is_digest
from .rank_stream import _read_exact, _write_all

_OPEN = struct.Struct("!4s16sBIQ32s")  # version, fresh attempt, rank, count, bytes, plan/manifest hash
_ITEM = struct.Struct("!IQ32s")        # ordinal, exact byte length, trusted BF16 digest
_END = struct.Struct("!4sI32s")
_CHUNK = 1024 * 1024


class _Contract:
    def __init__(self, attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes):
        attempt = UUID(canonical_id(attempt_id))
        if (attempt.int == 0 or type(plan) is not RankPlan or type(manifest) is not RankShardManifest
                or not _is_digest(manifest.index_sha256)
                or type(plan.tensors) is not tuple or not 0 < len(plan.tensors) <= MAX_TENSORS
                or type(plan.shards) is not tuple or not 0 < len(plan.shards) <= MAX_SHARDS
                or type(manifest.shards) is not tuple or len(manifest.shards) != len(plan.shards)
                or type(manifest.tensors) is not tuple or len(manifest.tensors) != len(plan.tensors)
                or type(max_tensor_bytes) is not int or not 0 < max_tensor_bytes <= MAX_SHARD_BYTES
                or type(max_backing_bytes) is not int or not 0 < max_backing_bytes <= MAX_MODEL_BYTES
                or type(plan.frozen_backing_bytes) is not int
                or not 0 < plan.frozen_backing_bytes <= max_backing_bytes):
            raise CustodyError("Invalid admitted BF16 tensor-stream contract or byte budget.")
        span = (plan.start_layer, plan.stop_layer)
        if any(type(i) is not int for i in span) or span not in ((0, 32), (32, 64)):
            raise CustodyError("The BF16 stream requires the exact owned Qwen rank range.")
        rank = int(span[0] == 32)
        self.by_key, self.digests, self.shards = {}, {}, {}
        for shard in manifest.shards:
            if (type(shard) is not ShardPin or type(shard.name) is not str or not SHARD.fullmatch(shard.name)
                    or shard.name in self.shards or not _is_digest(shard.header_sha256)
                    or type(shard.file_bytes) is not int
                    or not 8 < shard.file_bytes <= MAX_SHARD_BYTES + 16 * 1024 * 1024 + 8):
                raise CustodyError("Invalid pinned shard in BF16 stream contract.")
            self.shards[shard.name] = shard
        for pin in manifest.tensors:
            if (type(pin) is not TensorPin or type(pin.key) is not str or pin.key in self.digests
                    or not _is_digest(pin.sha256)):
                raise CustodyError("Duplicate or invalid tensor digest in BF16 stream contract.")
            self.digests[pin.key] = pin.sha256
        total = 0
        for p in plan.tensors:
            if (type(p) is not TensorPlacement or type(p.key) is not str or not 0 < len(p.key) <= 256
                    or p.key in self.by_key or p.key not in self.digests
                    or type(p.shard) is not str or p.shard not in self.shards or type(p.header) is not TensorHeader):
                raise CustodyError("Duplicate, unpinned, or invalid BF16 stream placement.")
            try:
                owner, _ = _rank(p.key)
            except ValueError as error:
                raise CustodyError("Foreign namespace in BF16 stream placement.") from error
            h = p.header
            if (owner != rank or h.dtype != "BF16" or type(h.shape) is not tuple or not 1 <= len(h.shape) <= 8
                    or any(type(n) is not int or not 0 < n <= MAX_SHARD_BYTES // 2 for n in h.shape)
                    or type(h.start) is not int or type(h.end) is not int
                    or not 0 <= h.start < h.end <= min(MAX_SHARD_BYTES, self.shards[p.shard].file_bytes - 8)
                    or h.end - h.start > max_tensor_bytes or h.end - h.start != 2 * prod(h.shape)):
                raise CustodyError("Foreign, invalid, or oversized BF16 stream placement.")
            total += h.end - h.start
            self.by_key[p.key] = p
        if (self.by_key.keys() != self.digests.keys() or tuple(sorted(self.shards)) != plan.shards
                or set(self.shards) != {p.shard for p in self.by_key.values()}
                or total != plan.frozen_backing_bytes):
            raise CustodyError("The BF16 stream plan and manifest do not cover the exact owned backing.")
        self.order = plan.tensors
        digest = hashlib.sha256(_encode_record({"plan": asdict(plan), "manifest": asdict(manifest)})).digest()
        self.prefix = _OPEN.pack(b"DQS1", attempt.bytes, rank, len(self.order), total, digest)
        self.footer = _END.pack(b"DQE1", len(self.order), digest)

    def item(self, ordinal):
        p = self.order[ordinal]
        length = p.header.end - p.header.start
        digest = bytes.fromhex(self.digests[p.key])
        return _ITEM.pack(ordinal, length, digest), length, digest


def write_rank_tensors(stream, *, attempt_id, plan, manifest, reader: RankTensorReader,
                       max_tensor_bytes, max_backing_bytes):
    """Write each verified tensor once. Reader must be open on the exact host pins."""
    contract = _Contract(attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes)
    if (type(reader) is not RankTensorReader or reader._stack is None or reader._index != manifest.index_sha256
            or reader._pins != contract.shards or reader._digests != contract.digests
            or reader._placements != contract.by_key):
        raise CustodyError("The host reader does not match the admitted BF16 stream manifest.")
    _write_all(stream, contract.prefix)
    for ordinal, p in enumerate(contract.order):
        data = reader._read_bytes(p)
        header, _, _ = contract.item(ordinal)
        _write_all(stream, header)
        _write_all(stream, data)
        del data
    _write_all(stream, contract.footer)
    stream.flush()


class StreamRankTensorReader:
    """Assembly callback: bounded owned backing, no file, no duplicate tensor copy.

    Wire order is the immutable plan order. Assembly may request those same
    placements in module order. Earlier verified tensors remain pending until
    requested; each storage is delivered once. Call finish() before using the
    assembled model. Any fault retires this reader and its stream.
    """

    def __init__(self, stream, *, attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes):
        self._contract = _Contract(attempt_id, plan, manifest, max_tensor_bytes, max_backing_bytes)
        self._stream = stream
        self._ordinal = 0
        self._pending = {}
        self._delivered = set()
        self._closed = False
        self._begun = False

    def _retire(self):
        self._closed = True
        self._pending.clear()

    def _take(self, placement):
        try:
            if (self._closed or type(placement) is not TensorPlacement or type(placement.key) is not str
                    or self._contract.by_key.get(placement.key) != placement or placement.key in self._delivered):
                raise CustodyError("Foreign, duplicate, or retired BF16 stream request.")
            key = placement.key
            if not self._begun:
                if _read_exact(self._stream, _OPEN.size) != self._contract.prefix:
                    raise CustodyError("The BF16 stream attempt, rank or plan does not match.")
                self._begun = True
            while key not in self._pending:
                expected, length, expected_digest = self._contract.item(self._ordinal)
                if _read_exact(self._stream, _ITEM.size) != expected:
                    raise CustodyError("The BF16 tensor frame size, order or digest pin does not match.")
                data, digest, offset = bytearray(length), hashlib.sha256(), 0
                with memoryview(data) as view:
                    while offset < length:
                        chunk = view[offset:offset + _CHUNK]
                        count = self._stream.readinto(chunk)
                        if type(count) is not int or not 0 < count <= len(chunk):
                            raise CustodyError("The admitted BF16 tensor stream was truncated.")
                        digest.update(chunk[:count])
                        offset += count
                if digest.digest() != expected_digest:
                    raise CustodyError("The BF16 stream tensor failed its payload digest.")
                self._pending[self._contract.order[self._ordinal].key] = data
                self._ordinal += 1
            self._delivered.add(key)
            return self._pending.pop(key)
        except Exception:
            self._retire()
            raise

    def __call__(self, placement):
        if sys.byteorder != "little":
            raise CustodyError("The BF16 tensor stream requires a little-endian CPU.")
        import torch

        data = self._take(placement)
        shape = self._contract.by_key[placement.key].header.shape
        try:
            return torch.frombuffer(data, dtype=torch.bfloat16).reshape(shape)
        except Exception:
            self._retire()
            raise

    def finish(self):
        try:
            if (self._closed or len(self._delivered) != len(self._contract.order) or self._pending
                    or _read_exact(self._stream, _END.size) != self._contract.footer):
                raise CustodyError("The owned BF16 tensor stream did not complete exactly once.")
        finally:
            self._retire()
