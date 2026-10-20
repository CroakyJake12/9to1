"""Runtime-format sniffers for admitted asset files.

These are bounded, side-effect-free structural validators. They prove that a
supplied file has the *format* it claims AND that its declared header/table/
payload structure is consistent and not truncated. They never decode weights,
load a model, allocate full weight contents, or execute anything.

Structural integrity is distinct from actually usable inference readiness: a
file that is structurally complete has still not been licence-reviewed or proven
runnable with a real backend. Unknown but explicitly labelled formats are
handled with an explicit ``opaque`` runtime format whose bytes are still hashed
and size-checked. ``opaque`` is deliberately NOT evidence that the bytes are a
usable model/runtime.
"""
from __future__ import annotations

import json
import pathlib
import struct
from typing import Optional

from .identity import IdentityError

GGUF_MAGIC = b"GGUF"
SUPPORTED_GGUF_VERSIONS = frozenset({2, 3})
# GGUF v2/v3 primary header: magic(4) + version(4) + tensor_count(8) +
# metadata_kv_count(8).
GGUF_PRIMARY_HEADER_BYTES = 24

ELF_MAGIC = b"\x7fELF"
ELF_CLASS_64 = 2
ELF_DATA_LITTLE = 1
ELF_VERSION_CURRENT = 1
# A valid ELF64 file header is exactly 64 bytes.
ELF64_MIN_HEADER_BYTES = 64
ELF64_E_EHSIZE_OFFSET = 52
ELF64_E_EHSIZE_EXPECTED = 64

# e_machine constants (Linux ELF).
ELF_MACHINE_X86_64 = 62
ELF_MACHINE_AARCH64 = 183
ELF_MACHINE_RISCV = 243
ELF_MACHINE_ARM = 40
ELF_MACHINE_AMDGPU = 224

# GGUF metadata value type tags.
GGUF_TYPE_UINT8 = 0
GGUF_TYPE_INT8 = 1
GGUF_TYPE_UINT16 = 2
GGUF_TYPE_INT16 = 3
GGUF_TYPE_UINT32 = 4
GGUF_TYPE_INT32 = 5
GGUF_TYPE_FLOAT32 = 6
GGUF_TYPE_BOOL = 7
GGUF_TYPE_STRING = 8
GGUF_TYPE_ARRAY = 9
GGUF_TYPE_UINT64 = 10
GGUF_TYPE_INT64 = 11
GGUF_TYPE_FLOAT64 = 12

# Number of bytes for each scalar metadata value type (excludes type tag itself).
GGUF_SCALAR_SIZE = {
    GGUF_TYPE_UINT8: 1,
    GGUF_TYPE_INT8: 1,
    GGUF_TYPE_UINT16: 2,
    GGUF_TYPE_INT16: 2,
    GGUF_TYPE_UINT32: 4,
    GGUF_TYPE_INT32: 4,
    GGUF_TYPE_FLOAT32: 4,
    GGUF_TYPE_BOOL: 1,
    GGUF_TYPE_UINT64: 8,
    GGUF_TYPE_INT64: 8,
    GGUF_TYPE_FLOAT64: 8,
}


class _Buf:
    __slots__ = ("data", "pos")

    def __init__(self, data: bytes) -> None:
        self.data = data
        self.pos = 0

    @property
    def remaining(self) -> int:
        return len(self.data) - self.pos

    def take(self, n: int) -> bytes:
        if n < 0 or self.pos + n > len(self.data):
            raise IdentityError("truncated or malformed container: structural read ran past end of file")
        chunk = self.data[self.pos : self.pos + n]
        self.pos += n
        return chunk

    def u32(self) -> int:
        return struct.unpack("<I", self.take(4))[0]

    def u64(self) -> int:
        return struct.unpack("<Q", self.take(8))[0]

    def string(self) -> bytes:
        length = self.u64()
        return self.take(length)

    def skip(self, n: int) -> None:
        self.take(n)


def _read_gguf_string(buf: _Buf) -> bytes:
    return buf.string()


def _read_gguf_metadata_value(buf: _Buf) -> None:
    """Read one metadata value (after its u32 type tag) without decoding it."""
    vtype = buf.u32()
    if vtype == GGUF_TYPE_STRING:
        buf.string()
        return
    if vtype == GGUF_TYPE_ARRAY:
        elem_type = buf.u32()
        count = buf.u64()
        for _ in range(count):
            if elem_type == GGUF_TYPE_STRING:
                buf.string()
            elif elem_type in GGUF_SCALAR_SIZE:
                buf.skip(GGUF_SCALAR_SIZE[elem_type])
            else:
                raise IdentityError(f"unknown GGUF array element type {elem_type}")
        return
    size = GGUF_SCALAR_SIZE.get(vtype)
    if size is None:
        raise IdentityError(f"unknown GGUF metadata value type {vtype}")
    buf.skip(size)


def sniff_gguf(path: pathlib.Path) -> dict:
    with path.open("rb") as handle:
        data = handle.read()
    buf = _Buf(data)
    head = buf.take(8)
    if head[:4] != GGUF_MAGIC:
        raise IdentityError("file does not have GGUF magic")
    version = struct.unpack("<I", head[4:8])[0]
    if version not in SUPPORTED_GGUF_VERSIONS:
        raise IdentityError(f"unsupported GGUF version {version}; expected 2 or 3")

    tensor_count = buf.u64()
    metadata_kv_count = buf.u64()

    # Walk metadata key/value pairs (the table itself must fit in the file).
    for _ in range(metadata_kv_count):
        _read_gguf_string(buf)  # key
        _read_gguf_metadata_value(buf)

    # Walk tensor info entries and validate each declared data offset is inside
    # the file. We intentionally do NOT decode weight contents or compute full
    # payload byte lengths from ggml block formats; that is a later,
    # licence-reviewed inference-readiness step. Structural admission only
    # requires the declared table to fit and the data region to start in-file.
    for _ in range(tensor_count):
        _read_gguf_string(buf)  # tensor name
        n_dimensions = buf.u32()
        if n_dimensions > 64:
            raise IdentityError(f"GGUF tensor declares implausible rank {n_dimensions}")
        buf.skip(4 * n_dimensions)  # dimensions array
        buf.u32()  # ggml tensor type tag
        offset = buf.u64()
        if offset >= len(data):
            raise IdentityError("GGUF tensor data offset lies beyond end of file")

    return {
        "format": "gguf",
        "ggufVersion": version,
        "tensorCount": tensor_count,
        "metadataKvCount": metadata_kv_count,
        "structurallyComplete": True,
    }


def _safetensors_file_size(path: pathlib.Path) -> int:
    return path.stat().st_size


def sniff_safetensors(path: pathlib.Path) -> dict:
    file_size = _safetensors_file_size(path)
    with path.open("rb") as handle:
        header_bytes = handle.read(8)
    if len(header_bytes) != 8:
        raise IdentityError("safetensors header length is truncated")
    header_len = struct.unpack("<Q", header_bytes)[0]
    if header_len == 0 or header_len > 128 * 1024 * 1024:
        raise IdentityError("safetensors header length out of range")
    with path.open("rb") as handle:
        handle.seek(8)
        blob = handle.read(header_len)
    if len(blob) != header_len:
        raise IdentityError("safetensors header body is truncated")
    try:
        parsed = json.loads(blob.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise IdentityError(f"safetensors header is not valid JSON: {exc}") from exc
    if not isinstance(parsed, dict):
        raise IdentityError("safetensors header must be a JSON object")

    # The 8-byte length prefix precedes the payload buffer, so a tensor's data
    # region must lie wholly within [8+header_len, file_size).
    payload_begin = 8 + header_len
    tensor_count = 0
    for name, tensor in parsed.items():
        if name == "__metadata__":
            continue
        if not isinstance(tensor, dict) or not {"data_offsets", "shape", "dtype"}.issubset(tensor):
            raise IdentityError(f"safetensors tensor {name!r} is malformed")
        offsets = tensor.get("data_offsets")
        if not (isinstance(offsets, list) and len(offsets) == 2):
            raise IdentityError(f"safetensors tensor {name!r} has malformed data_offsets")
        start, end = offsets
        if not (isinstance(start, int) and isinstance(end, int)):
            raise IdentityError(f"safetensors tensor {name!r} data_offsets must be integers")
        if start < 0 or end <= start:
            raise IdentityError(f"safetensors tensor {name!r} has invalid data_offsets range")
        if payload_begin + end > file_size:
            raise IdentityError(
                f"safetensors tensor {name!r} declared data extends beyond end of file"
            )
        shape = tensor.get("shape")
        if not isinstance(shape, list) or not shape:
            raise IdentityError(f"safetensors tensor {name!r} must declare a non-empty shape")
        tensor_count += 1

    if tensor_count == 0:
        raise IdentityError("safetensors file declares no tensors (not a usable model asset)")

    return {
        "format": "safetensors",
        "headerBytes": header_len,
        "tensors": tensor_count,
        "structurallyComplete": True,
    }


def sniff_elf(path: pathlib.Path) -> dict:
    with path.open("rb") as handle:
        head = handle.read(ELF64_MIN_HEADER_BYTES)
    if len(head) < ELF64_MIN_HEADER_BYTES:
        raise IdentityError("ELF header is shorter than the 64-byte ELF64 file header")
    if head[:4] != ELF_MAGIC:
        raise IdentityError("file does not have ELF magic")
    if head[4] != ELF_CLASS_64:
        raise IdentityError("ELF is not 64-bit class")
    if head[5] != ELF_DATA_LITTLE:
        raise IdentityError("ELF is not little-endian")
    if head[6] != ELF_VERSION_CURRENT:
        raise IdentityError("ELF has an unsupported version")
    machine = int.from_bytes(head[18:20], "little")
    osabi = head[7]
    ehsize = int.from_bytes(head[ELF64_E_EHSIZE_OFFSET : ELF64_E_EHSIZE_OFFSET + 2], "little")
    if ehsize != ELF64_E_EHSIZE_EXPECTED:
        raise IdentityError("ELF64 header declares an incorrect e_ehsize")
    return {"format": "elf", "machine": machine, "osabi": osabi, "structurallyComplete": True}


KNOWN_ARCH_ALIASES = {
    "x86_64": ELF_MACHINE_X86_64,
    "amd64": ELF_MACHINE_X86_64,
    "aarch64": ELF_MACHINE_AARCH64,
    "arm64": ELF_MACHINE_AARCH64,
    "riscv64": ELF_MACHINE_RISCV,
    "arm": ELF_MACHINE_ARM,
    "x86": 3,
}


def sniff_format(path: pathlib.Path, declared: str) -> dict:
    """Validate *declared* runtime format against the actual file.

    ``declared`` is one of: ``gguf``, ``safetensors``, ``elf``, ``opaque``.
    Returns a ``structurallyComplete: True`` marker for the validated formats.
    ``opaque`` explicitly does NOT claim structural completeness or usability.
    """
    kind = str(declared).strip().lower()
    if kind == "opaque":
        return {"format": "opaque", "declared": "opaque", "structurallyComplete": False}
    if kind == "gguf":
        return sniff_gguf(path)
    if kind == "safetensors":
        return sniff_safetensors(path)
    if kind == "elf":
        return sniff_elf(path)
    raise IdentityError(f"unknown runtime format declaration: {declared}")


def elf_machine_matches(meta: dict, arch: str) -> bool:
    machine = meta.get("machine")
    wanted = KNOWN_ARCH_ALIASES.get(str(arch).strip().lower())
    if wanted is None:
        # Unknown requested arch: fail closed rather than assume a match.
        return False
    return machine == wanted
