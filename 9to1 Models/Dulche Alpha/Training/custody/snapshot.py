"""Data-only archive for the efficient trainer's CPU snapshots."""

from __future__ import annotations

from collections.abc import Mapping
import json
import math
from pathlib import Path
import sys
from uuid import uuid4
import zipfile

from .artifacts import ArtifactRef, ArtifactStore, AttemptRef, CustodyError, checked_path

_INDEX = "snapshot.json"
_METADATA_BYTES = 4 * 1024 * 1024
_MAX_TENSORS = 16384
_MAX_DEPTH = 40
_DTYPES = ("uint8", "int8", "int16", "int32", "int64", "bool", "float16", "bfloat16", "float32", "float64")


def _limit(max_bytes: int) -> None:
    if type(max_bytes) is not int or max_bytes < 1:
        raise CustodyError("A positive snapshot byte limit is required.")
    if sys.byteorder != "little":
        raise CustodyError("Snapshot schema 1 requires a little-endian CPU.")


def _finite(value) -> bool:
    if value is None or type(value) in (bool, str):
        return True
    if type(value) is int:
        return -(1 << 63) <= value < (1 << 64)
    return type(value) is float and math.isfinite(value)


def _t_name(index: int) -> str:
    return f"tensors/{index:05d}.bin"


def _encoder(value, torch, tensors, depth=0):
    if depth > _MAX_DEPTH:
        raise CustodyError("Snapshot structure is too deeply nested.")
    if isinstance(value, torch.Tensor):
        dtype = str(value.dtype).removeprefix("torch.")
        if (value.device.type != "cpu" or value.layout != torch.strided or dtype not in _DTYPES or value.dim() > 16
                or len(tensors) >= _MAX_TENSORS):
            raise CustodyError("Snapshots only support bounded dense CPU tensors and known dtypes.")
        tensors.append(value.detach().contiguous())
        return {"tensor": len(tensors) - 1}
    if _finite(value):
        return value
    if isinstance(value, Mapping):
        if any(type(key) not in (str, int) or not _finite(key) for key in value):
            raise CustodyError("Snapshot dictionaries require string or integer keys.")
        return {"map": [[key, _encoder(item, torch, tensors, depth + 1)] for key, item in value.items()]}
    if type(value) in (tuple, list):
        return {type(value).__name__: [_encoder(item, torch, tensors, depth + 1) for item in value]}
    raise CustodyError("Snapshot contains an unsupported or non-finite value.")


def _decoder(value, tensors, seen, depth=0):
    if depth > _MAX_DEPTH:
        raise CustodyError("Snapshot structure is too deeply nested.")
    if _finite(value):
        return value
    if type(value) is not dict or len(value) != 1:
        raise CustodyError("Snapshot data node is invalid.")
    kind, body = next(iter(value.items()))
    if kind == "tensor":
        if type(body) is not int or not 0 <= body < len(tensors) or body in seen:
            raise CustodyError("Snapshot tensor reference is invalid or duplicated.")
        seen.add(body)
        return tensors[body]
    if type(body) is not list:
        raise CustodyError("Snapshot sequence is invalid.")
    if kind == "map":
        decoded = {}
        for pair in body:
            if (type(pair) is not list or len(pair) != 2 or type(pair[0]) not in (str, int)
                    or not _finite(pair[0]) or pair[0] in decoded):
                raise CustodyError("Snapshot dictionary keys are invalid or duplicated.")
            decoded[pair[0]] = _decoder(pair[1], tensors, seen, depth + 1)
        return decoded
    if kind == "list":
        return [_decoder(item, tensors, seen, depth + 1) for item in body]
    if kind == "tuple":
        return tuple(_decoder(item, tensors, seen, depth + 1) for item in body)
    raise CustodyError("Snapshot data node type is unsupported.")


def _write_snapshot(path: Path, snapshot: Mapping, max_bytes: int) -> None:
    import torch

    if type(snapshot.get("schema")) is not int or snapshot["schema"] != 1:
        raise CustodyError("Expected an efficient trainer schema-1 snapshot.")
    tensors = []
    tree = _encoder(snapshot, torch, tensors)
    descriptors = [{"dtype": str(value.dtype).removeprefix("torch."),
                    "shape": list(value.shape), "bytes": value.numel() * value.element_size()} for value in tensors]
    metadata = json.dumps({"archive": 1, "snapshot": tree, "tensors": descriptors}, allow_nan=False).encode("utf-8")
    declared = sum(descriptor["bytes"] for descriptor in descriptors)
    if len(metadata) > _METADATA_BYTES or declared + len(metadata) > max_bytes:
        raise CustodyError("The checkpoint exceeds its byte allowance.")
    checked_path(path.parent, directory=True)
    # No compression or pickle. Work copy remains as evidence even if publication fails.
    with path.open("x+b") as output:
        with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_STORED) as archive:
            archive.writestr(_INDEX, metadata)
            for index, tensor in enumerate(tensors):
                with archive.open(_t_name(index), "w", force_zip64=True) as target:
                    target.write(memoryview(tensor.reshape(-1).view(torch.uint8).numpy()))
        if output.tell() > max_bytes:
            raise CustodyError("The complete checkpoint exceeds its byte allowance.")


def publish_snapshot(
    store: ArtifactStore, attempt: AttemptRef, name: str, snapshot: Mapping, *,
    max_bytes: int, kind: str = "checkpoint",
) -> ArtifactRef:
    """Keep the workspace copy and publish a hashed immutable checkpoint."""
    _limit(max_bytes)
    if kind not in {"checkpoint", "segment_checkpoint"}:
        raise CustodyError("Snapshot kind must be a checkpoint.")
    filename = f"snapshot-{uuid4().hex}.zip"
    path = store.attempt_path(attempt) / "work" / filename
    _write_snapshot(path, snapshot, max_bytes)
    return store.publish_file(attempt, name, kind, filename, max_bytes=max_bytes)


def load_snapshot(store: ArtifactStore, artifact: ArtifactRef, *, max_bytes: int) -> dict:
    """Integrity-check an artifact, then decode contiguous CPU tensors as data."""
    _limit(max_bytes)
    if type(artifact.size_bytes) is not int or not 1 <= artifact.size_bytes <= max_bytes:
        raise CustodyError("Snapshot is outside its materialization allowance.")
    import torch

    path = store.verify(artifact)
    try:
        with zipfile.ZipFile(path) as archive:
            listed = archive.infolist()
            if (not 1 <= len(listed) <= _MAX_TENSORS + 1
                    or len({item.filename for item in listed}) != len(listed)
                    or any(item.compress_type != zipfile.ZIP_STORED or item.flag_bits & 1 for item in listed)
                    or sum(item.file_size for item in listed) > max_bytes):
                raise CustodyError("Snapshot archive entries are invalid or oversized.")
            if archive.getinfo(_INDEX).file_size > _METADATA_BYTES:
                raise CustodyError("Snapshot metadata exceeds its allowance.")
            metadata = json.loads(archive.read(_INDEX))
            if (type(metadata) is not dict or set(metadata) != {"archive", "snapshot", "tensors"}
                    or type(metadata["archive"]) is not int or metadata["archive"] != 1
                    or type(metadata["tensors"]) is not list or len(metadata["tensors"]) != len(listed) - 1):
                raise CustodyError("Snapshot archive schema is unsupported.")
            tensors = []
            for index, descriptor in enumerate(metadata["tensors"]):
                if type(descriptor) is not dict or set(descriptor) != {"dtype", "shape", "bytes"}:
                    raise CustodyError("Snapshot tensor metadata is invalid.")
                dtype, shape, nbytes = descriptor["dtype"], descriptor["shape"], descriptor["bytes"]
                if (type(dtype) is not str or dtype not in _DTYPES or type(shape) is not list or len(shape) > 16
                        or any(type(dimension) is not int or not 0 <= dimension <= (1 << 31) for dimension in shape)
                        or type(nbytes) is not int or not 0 <= nbytes <= max_bytes
                        or archive.getinfo(_t_name(index)).file_size != nbytes):
                    raise CustodyError("Snapshot tensor dimensions or bytes are invalid.")
                tensor_dtype = getattr(torch, dtype)
                element_size = torch.empty((), dtype=tensor_dtype, device="cpu").element_size()
                if math.prod(shape) * element_size != nbytes:
                    raise CustodyError("Snapshot tensor data does not match its shape.")
                if nbytes:
                    buffer = bytearray(archive.read(_t_name(index)))
                    tensor = torch.frombuffer(buffer, dtype=tensor_dtype).reshape(shape)
                else:
                    tensor = torch.empty(shape, dtype=tensor_dtype, device="cpu")
                tensors.append(tensor)
            used = set()
            snapshot = _decoder(metadata["snapshot"], tensors, used)
            if (len(used) != len(tensors) or type(snapshot) is not dict
                    or type(snapshot.get("schema")) is not int or snapshot["schema"] != 1):
                raise CustodyError("Snapshot entries do not match the trainer schema.")
            return snapshot
    except (zipfile.BadZipFile, UnicodeError, json.JSONDecodeError, KeyError, OverflowError) as error:
        raise CustodyError("Checkpoint archive is malformed.") from error
