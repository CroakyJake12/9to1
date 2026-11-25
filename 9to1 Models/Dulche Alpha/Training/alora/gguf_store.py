"""Read-only, bounded mixed-quantization slices from externally attested GGUF.

The supplied shard SHA-256 values are existing external full-file attestations.
This store checks sizes and stable file identities; it does NOT rehash full model
files or claim to verify those attestations freshly. Every decoded slice is
hashed from its actual copied quantized bytes and pinned for subsequent reads.
Fingerprint capacity is explicit and fails closed without evicting existing pins.
No Torch import, model construction, device selection or inference mutation.
"""
from __future__ import annotations

from collections.abc import Mapping
import copy
from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys
import threading

import numpy as np
from gguf import GGUFReader, GGMLQuantizationType, GGML_QUANT_SIZES, GGUFValueType
from gguf import quants
from custody.artifacts import _open_regular, checked_path
from .read_limits import FingerprintCapacityExceeded

_SHA = re.compile(r"[0-9a-f]{64}")
_SUPPORTED = {GGMLQuantizationType[name] for name in
              ("F32", "F16", "BF16", "IQ3_S", "IQ4_XS", "IQ4_NL", "Q8_0", "Q6_K")}
_MAX_BOUND = 1024 * 1024 * 1024
_MAX_FINGERPRINTS = 1024 * 1024


@dataclass(frozen=True, slots=True)
class MatrixRef:
    tensor_name: str
    expert: int | None
    row_start: int
    row_count: int
    shape: tuple[int, int]
    qtype: str
    source_identity: str


@dataclass(slots=True)
class _Shard:
    path: Path
    stream: object
    reader: object
    stat: dict
    pin: dict
    metadata: dict


def _stat(info):
    return {"device": info.st_dev, "inode": info.st_ino, "size_bytes": info.st_size,
            "mtime_ns": info.st_mtime_ns, "ctime_ns": info.st_ctime_ns}


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
                      allow_nan=False).encode("ascii")


def _metadata(reader):
    result = {}
    for name, field in reader.fields.items():
        if name not in ("general.architecture", "general.name") and not name.startswith(("qwen4exp.", "split.")):
            continue
        if field.types and field.types[0] == GGUFValueType.ARRAY:
            if sum(field.parts[index].size for index in field.data) > 4096:
                raise ValueError("GGUF structural metadata array exceeds its bound")
        result[name] = field.contents()
    if len(_canonical(result)) > 65536:
        raise ValueError("GGUF structural metadata exceeds its byte bound")
    return result


class GGUFWeightStore:
    """Own read-only maps; decode only the requested matrix/expert/embedding rows."""

    def __init__(self, paths, *, expected_shards,
                 max_decoded_bytes=64 * 1024 * 1024, max_read_bytes=64 * 1024 * 1024,
                 max_fingerprints=262144):
        for bound in (max_decoded_bytes, max_read_bytes):
            if type(bound) is not int or not 1 <= bound <= _MAX_BOUND:
                raise ValueError("Slice byte limits must be bounded positive integers")
        if type(max_fingerprints) is not int or not 1 <= max_fingerprints <= _MAX_FINGERPRINTS:
            raise ValueError("Fingerprint limit must be a bounded positive integer")
        if isinstance(paths, (str, bytes, Path)):
            raise ValueError("Supply a sequence of explicit shard paths")
        paths = tuple(Path(path) for path in paths)
        if not 1 <= len(paths) <= 256 or len({path.name for path in paths}) != len(paths):
            raise ValueError("Shard filenames must be unique and bounded")
        if not isinstance(expected_shards, Mapping) or set(expected_shards) != {p.name for p in paths}:
            raise ValueError("Exact external attestations are required for every supplied shard")
        for name, pin in expected_shards.items():
            if (type(pin) is not dict or set(pin) != {"size_bytes", "sha256"}
                    or type(pin["size_bytes"]) is not int or pin["size_bytes"] <= 0
                    or type(pin["sha256"]) is not str or not _SHA.fullmatch(pin["sha256"])):
                raise ValueError("Each shard needs its external SHA-256 and exact positive size")
        if sys.byteorder != "little":
            raise ValueError("This GGUF dequantization boundary requires a little-endian host")
        self.max_decoded_bytes, self.max_read_bytes = max_decoded_bytes, max_read_bytes
        self._max_fingerprints = max_fingerprints
        self._lock = threading.RLock()
        self._shards, self._tensors, self._slice_hashes = [], {}, {}
        self._closed = False
        self._stats = {"read_count": 0, "decode_count": 0, "gather_count": 0,
                       "total_read_bytes": 0, "total_decoded_bytes": 0,
                       "max_read_bytes": 0, "max_decoded_bytes": 0}
        try:
            metadata = {}
            for path in paths:
                stream = _open_regular(path)
                reader = None
                try:
                    captured = _stat(os.fstat(stream.fileno()))
                    pin = copy.deepcopy(expected_shards[path.name])
                    if captured["size_bytes"] != pin["size_bytes"]:
                        raise ValueError("Shard size differs from its external attestation")
                    # GGUFReader passes this handle to np.memmap. NumPy accepts an
                    # open file object, binding the map to the inspected descriptor.
                    reader = GGUFReader(stream, mode="r")
                    if reader.byte_order != "I":
                        raise ValueError("Byte-swapped GGUF weights are outside this boundary")
                    fields = _metadata(reader)
                    shard = _Shard(path, stream, reader, captured, pin, fields)
                    self._shards.append(shard)
                    self._check_shard(shard)
                except BaseException:
                    if not self._shards or self._shards[-1].stream is not stream:
                        if reader is not None:
                            reader.data._mmap.close()
                        stream.close()
                    raise
                for key, value in fields.items():
                    if key.startswith("split."):
                        continue
                    if key in metadata and _canonical(metadata[key]) != _canonical(value):
                        raise ValueError("Conflicting GGUF structural metadata across shards")
                    metadata[key] = value
                intervals = []
                for tensor in reader.tensors:
                    name = tensor.name
                    if type(name) is not str or not name or len(name) > 1024 or name in self._tensors:
                        raise ValueError("Duplicate or invalid tensor name across GGUF shards")
                    shape = tuple(int(value) for value in reversed(tensor.shape))
                    if (not shape or any(value <= 0 for value in shape)
                            or math.prod(shape) != tensor.n_elements
                            or tensor.n_bytes != tensor.data.nbytes
                            or tensor.data_offset < reader.data_offset
                            or tensor.data_offset + tensor.n_bytes > captured["size_bytes"]):
                        raise ValueError("Tensor shape or byte extent is inconsistent")
                    intervals.append((tensor.data_offset, tensor.data_offset + tensor.n_bytes))
                    self._tensors[name] = (shard, tensor, shape)
                intervals.sort()
                if any(left[1] > right[0] for left, right in zip(intervals, intervals[1:])):
                    raise ValueError("GGUF tensor byte extents overlap")
            if metadata.get("general.architecture") != "qwen4exp":
                raise ValueError("This store requires the actual qwen4exp architecture")
            if not self._tensors:
                raise ValueError("No GGUF model tensors were supplied")
            split_fields = [s.metadata for s in self._shards]
            if any("split.count" in values for values in split_fields):
                if (any(values.get("split.count") != len(paths) for values in split_fields)
                        or {values.get("split.no") for values in split_fields} != set(range(len(paths)))):
                    raise ValueError("GGUF split metadata does not identify the exact shard set")
            self._identity = {
                "schema": 1, "format": "alora-gguf-weight-store-v1",
                "verification": "external_sha256_attestation_not_rehashed",
                "fresh_full_file_hash_verified": False, "metadata": metadata,
                "shards": [{"filename": shard.path.name, "path": str(shard.path),
                            "attested_sha256": shard.pin["sha256"],
                            "size_bytes": shard.pin["size_bytes"], "stat": shard.stat,
                            "split_metadata": {k: v for k, v in shard.metadata.items() if k.startswith("split.")}}
                           for shard in sorted(self._shards, key=lambda s: s.path.name)],
            }
            self.source_identity = hashlib.sha256(_canonical(self._identity)).hexdigest()
        except BaseException:
            self.close()
            raise

    def _ensure_open(self):
        if self._closed:
            raise ValueError("The GGUF weight store is closed")

    def _check_shard(self, shard):
        if (_stat(checked_path(shard.path, directory=False)) != shard.stat
                or _stat(os.fstat(shard.stream.fileno())) != shard.stat):
            raise ValueError("GGUF source identity changed after admission")

    @property
    def identity(self):
        return copy.deepcopy(self._identity)

    @property
    def stats(self):
        with self._lock:
            return {**self._stats, "fingerprint_count": len(self._slice_hashes),
                    "fingerprint_limit": self._max_fingerprints}

    @property
    def tensor_names(self):
        with self._lock:
            self._ensure_open()
            return tuple(sorted(self._tensors))

    def tensor_info(self, name):
        with self._lock:
            self._ensure_open()
            if type(name) is not str or name not in self._tensors:
                raise ValueError("Unknown GGUF tensor")
            shard, tensor, shape = self._tensors[name]
            return {"shape": shape, "qtype": tensor.tensor_type.name,
                    "bytes": int(tensor.n_bytes), "shard": shard.path.name}

    def matrix_ref(self, name, *, expert=None, row_start=0, row_count=None):
        with self._lock:
            info = self.tensor_info(name)
            shape = info["shape"]
            if len(shape) == 3:
                if type(expert) is not int or not 0 <= expert < shape[0]:
                    raise ValueError("A packed expert tensor requires one valid expert index")
                rows, columns = shape[1:]
            elif len(shape) == 2 and expert is None:
                rows, columns = shape
            else:
                raise ValueError("A matrix reference requires a 2D matrix or one packed 3D expert")
            if type(row_start) is not int or not 0 <= row_start < rows:
                raise ValueError("Matrix row start is outside its tensor")
            if row_count is None:
                row_count = rows - row_start
            if type(row_count) is not int or not 1 <= row_count <= rows - row_start:
                raise ValueError("Matrix row count is outside its tensor")
            qtype = GGMLQuantizationType[info["qtype"]]
            if qtype not in _SUPPORTED:
                raise ValueError("GGUF tensor quantization is not qualified by this slice loader")
            block, encoded_size = GGML_QUANT_SIZES[qtype]
            if columns % block:
                raise ValueError("Tensor rows do not align with their actual quantization blocks")
            read_bytes = row_count * (columns // block) * encoded_size
            decoded_bytes = row_count * columns * 4
            if read_bytes > self.max_read_bytes or decoded_bytes > self.max_decoded_bytes:
                raise ValueError("Requested matrix slice exceeds its read or decoded byte budget")
            return MatrixRef(name, expert, row_start, row_count, (row_count, columns),
                             qtype.name, self.source_identity)

    def read(self, ref):
        with self._lock:
            self._ensure_open()
            if type(ref) is not MatrixRef:
                raise ValueError("Expected an immutable GGUF matrix reference")
            actual = self.matrix_ref(ref.tensor_name, expert=ref.expert,
                                     row_start=ref.row_start, row_count=ref.row_count)
            if ref != actual:
                raise ValueError("Matrix reference differs from this admitted store")
            shard, tensor, _ = self._tensors[ref.tensor_name]
            self._check_shard(shard)
            end = ref.row_start + ref.row_count
            view = tensor.data[ref.row_start:end] if ref.expert is None else tensor.data[ref.expert, ref.row_start:end]
            return self._decode_slice(ref, shard, tensor, view, ref.shape)

    def _decode_slice(self, key, shard, tensor, view, shape):
        # Select experts/rows and enforce both bounds before any copy/decode.
        if view.nbytes > self.max_read_bytes or math.prod(shape) * 4 > self.max_decoded_bytes:
            raise ValueError("Tensor slice exceeds its read or decoded byte budget")
        # Never evict an existing forward/backward content pin to admit a new
        # slice. Capacity is a runtime budget, not part of source provenance.
        previous = self._slice_hashes.get(key)
        if previous is None and len(self._slice_hashes) >= self._max_fingerprints:
            raise FingerprintCapacityExceeded("Fingerprint capacity exhausted; existing pins are retained")
        self._check_shard(shard)
        encoded = np.array(view, copy=True, order="C")
        digest = hashlib.sha256(memoryview(encoded).cast("B")).hexdigest()
        self._stats["read_count"] += 1
        self._stats["total_read_bytes"] += encoded.nbytes
        self._stats["max_read_bytes"] = max(self._stats["max_read_bytes"], encoded.nbytes)
        if previous is not None and previous != digest:
            raise ValueError("Previously read quantized slice bytes changed")
        decoded = quants.dequantize(encoded, tensor.tensor_type)
        if decoded.shape != shape or decoded.nbytes > self.max_decoded_bytes:
            raise ValueError("Dequantized slice shape or byte count differs from its reference")
        result = np.array(decoded, dtype=np.float32, order="C", copy=True)
        if not np.isfinite(result).all():
            raise ValueError("Dequantized frozen weights must be finite")
        self._check_shard(shard)
        self._slice_hashes[key] = digest
        self._stats["decode_count"] += 1
        self._stats["total_decoded_bytes"] += result.nbytes
        self._stats["max_decoded_bytes"] = max(self._stats["max_decoded_bytes"], result.nbytes)
        return result

    def read_tensor(self, name):
        """Read an entire small tensor in its ordinary shape, subject to both bounds.

        Norm/conv/router tensors may fit. Full packed-expert and large embedding
        tensors must use matrix_ref/gather_rows whenever they exceed the bounds.
        """
        with self._lock:
            info = self.tensor_info(name)
            shape = info["shape"]
            qtype = GGMLQuantizationType[info["qtype"]]
            if qtype not in _SUPPORTED:
                raise ValueError("GGUF tensor quantization is not qualified by this slice loader")
            block, _ = GGML_QUANT_SIZES[qtype]
            if shape[-1] % block:
                raise ValueError("Tensor rows do not align with their quantization blocks")
            if info["bytes"] > self.max_read_bytes or math.prod(shape) * 4 > self.max_decoded_bytes:
                raise ValueError("Whole tensor exceeds its read or decoded byte budget")
            shard, tensor, _ = self._tensors[name]
            return self._decode_slice(("tensor", name, self.source_identity),
                                      shard, tensor, tensor.data, shape)

    def gather_rows(self, name, indices):
        """Read unique embedding rows, preserving requested order and duplicates."""
        with self._lock:
            info = self.tensor_info(name)
            if len(info["shape"]) != 2:
                raise ValueError("Embedding row gathering requires a 2D tensor")
            if isinstance(indices, np.ndarray):
                if indices.ndim != 1 or indices.dtype.kind not in "iu":
                    raise ValueError("Embedding indices must be one-dimensional integers")
            elif type(indices) not in (list, tuple):
                raise ValueError("Embedding indices must be a bounded integer sequence")
            total_rows, columns = info["shape"]
            if len(indices) * columns * 4 > self.max_decoded_bytes:
                raise ValueError("Gathered output exceeds the decoded byte budget")
            positions = {}
            for position, index in enumerate(indices):
                if (not isinstance(index, (int, np.integer)) or isinstance(index, (bool, np.bool_))
                        or not 0 <= int(index) < total_rows):
                    raise ValueError("Embedding row index is outside its tensor")
                positions.setdefault(int(index), []).append(position)
            qtype = GGMLQuantizationType[info["qtype"]]
            if qtype not in _SUPPORTED:
                raise ValueError("GGUF tensor quantization is not qualified by this slice loader")
            block, encoded_size = GGML_QUANT_SIZES[qtype]
            if columns % block:
                raise ValueError("Embedding rows do not align with their quantization blocks")
            if len(positions) * (columns // block) * encoded_size > self.max_read_bytes:
                raise ValueError("Unique embedding rows exceed the read byte budget")
            refs = {index: self.matrix_ref(name, row_start=index, row_count=1) for index in positions}
            new_pins = sum(ref not in self._slice_hashes for ref in refs.values())
            if len(self._slice_hashes) + new_pins > self._max_fingerprints:
                raise FingerprintCapacityExceeded("Fingerprint capacity exhausted; existing pins are retained")
            result = np.empty((len(indices), columns), dtype=np.float32)
            for index, destinations in positions.items():
                row = self.read(refs[index])
                result[destinations] = row[0]
            self._stats["gather_count"] += 1
            self._stats["max_decoded_bytes"] = max(self._stats["max_decoded_bytes"], result.nbytes)
            return result

    def close(self):
        with self._lock:
            if self._closed:
                return
            self._closed = True
            self._tensors.clear()
            self._slice_hashes.clear()
            for shard in self._shards:
                shard.reader.tensors.clear()
                shard.reader.fields.clear()
                shard.reader.data._mmap.close()
                shard.stream.close()
            self._shards.clear()

    def __enter__(self):
        self._ensure_open()
        return self

    def __exit__(self, *_args):
        self.close()
