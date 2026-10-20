"""Runtime-format sniffers for admitted asset files.

These are bounded, side-effect-free structural validators. They prove that a
supplied file has the *format* it claims AND that its declared header/table/
payload structure is consistent, aligned and not truncated. They never decode
weights, load a model, allocate full weight contents, or execute anything.

Structural integrity is distinct from actually usable inference readiness: a
file that is structurally complete has still not been licence-reviewed or proven
runnable with a real backend. Unknown but explicitly labelled formats are
handled with an explicit ``opaque`` runtime format whose bytes are still hashed
and size-checked. ``opaque`` is deliberately NOT evidence that the bytes are a
usable model/runtime.

Where only a subset of a format is supported (e.g. a specific safetensors dtype
or GGUF ggml tensor type), the validator fails closed with a precise
``unsupported`` diagnostic rather than claiming structural completeness.
"""
from __future__ import annotations

import json
import os
import pathlib
import struct

from .identity import IdentityError

GGUF_MAGIC = b"GGUF"
SUPPORTED_GGUF_VERSIONS = frozenset({2, 3})
# GGUF primary header: magic(4) + version(4) + tensor_count(8) +
# metadata_kv_count(8).
GGUF_PRIMARY_HEADER_BYTES = 24

# Plain (non-quantized) GGUF ggml tensor types we can compute a byte extent for.
# Anything else is rejected as unsupported rather than assumed complete.
GGUF_PLAIN_TYPE_BYTES = {
    0: 4,   # F32
    1: 2,   # F16
    14: 1,  # I8
    15: 2,  # I16
    16: 4,  # I32
    17: 8,  # I64
    18: 8,  # F64
}
GGUF_MAX_RANK = 64

ELF_MAGIC = b"\x7fELF"
ELF_CLASS_64 = 2
ELF_DATA_LITTLE = 1
ELF_VERSION_CURRENT = 1
# A valid ELF64 file header is exactly 64 bytes.
ELF64_MIN_HEADER_BYTES = 64
ELF64_E_EHSIZE_OFFSET = 52
ELF64_E_EHSIZE_EXPECTED = 64
ELF64_E_PHOFF_OFFSET = 32
ELF64_E_PHENTSIZE_OFFSET = 54
ELF64_E_PHNUM_OFFSET = 56
ELF64_E_SHOFF_OFFSET = 40
ELF64_E_SHENTSIZE_OFFSET = 58
ELF64_E_SHNUM_OFFSET = 60
ELF64_PHDR_SIZE = 56
ELF64_SHDR_SIZE = 64
ELF_PN_XNUM = 0xFFFF

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

# safetensors dtypes with known byte widths. Anything else is unsupported.
SAFETENSORS_DTYPE_BYTES = {
    "F64": 8,
    "F32": 4,
    "F16": 2,
    "BF16": 2,
    "I64": 8,
    "I32": 4,
    "I16": 2,
    "I8": 1,
    "U64": 8,
    "U32": 4,
    "U16": 2,
    "U8": 1,
    "BOOL": 1,
    "F8_E4M3": 1,
    "F8_E5M2": 1,
}


class _Reader:
    """Bounded sequential reader over an open file.

    Only the bytes actually needed are read (seek + read); the whole file is
    never loaded into memory. Every read is bounds-checked against the file size.
    """

    __slots__ = ("fd", "size", "pos")

    def __init__(self, fd: int, size: int) -> None:
        self.fd = fd
        self.size = size
        self.pos = 0

    def take(self, n: int) -> bytes:
        if n < 0 or self.pos + n > self.size:
            raise IdentityError(
                "truncated or malformed container: structural read ran past end of file"
            )
        os.lseek(self.fd, self.pos, os.SEEK_SET)
        chunk = os.read(self.fd, n)
        if len(chunk) != n:
            raise IdentityError("truncated or malformed container: short read")
        self.pos += n
        return chunk

    def u32(self) -> int:
        return struct.unpack("<I", self.take(4))[0]

    def u64(self) -> int:
        return struct.unpack("<Q", self.take(8))[0]

    def string(self) -> bytes:
        length = self.u64()
        if length > self.size - self.pos:
            raise IdentityError("truncated or malformed container: string length exceeds file")
        return self.take(length)

    def skip(self, n: int) -> None:
        self.take(n)


def _read_gguf_metadata_value(reader: _Reader) -> None:
    vtype = reader.u32()
    if vtype == GGUF_TYPE_STRING:
        reader.string()
        return
    if vtype == GGUF_TYPE_ARRAY:
        elem_type = reader.u32()
        count = reader.u64()
        if count > reader.size:
            raise IdentityError("malformed GGUF array count exceeds file size")
        for _ in range(count):
            if elem_type == GGUF_TYPE_STRING:
                reader.string()
            elif elem_type in GGUF_SCALAR_SIZE:
                reader.skip(GGUF_SCALAR_SIZE[elem_type])
            else:
                raise IdentityError(f"unknown GGUF array element type {elem_type}")
        return
    size = GGUF_SCALAR_SIZE.get(vtype)
    if size is None:
        raise IdentityError(f"unknown GGUF metadata value type {vtype}")
    reader.skip(size)


def sniff_gguf(path: pathlib.Path) -> dict:
    with path.open("rb") as handle:
        fd = handle.fileno()
        size = os.fstat(fd).st_size
        reader = _Reader(fd, size)

        head = reader.take(8)
        if head[:4] != GGUF_MAGIC:
            raise IdentityError("file does not have GGUF magic")
        version = struct.unpack("<I", head[4:8])[0]
        if version not in SUPPORTED_GGUF_VERSIONS:
            raise IdentityError(f"unsupported GGUF version {version}; expected 2 or 3")

        tensor_count = reader.u64()
        metadata_kv_count = reader.u64()

        for _ in range(metadata_kv_count):
            reader.string()  # key
            _read_gguf_metadata_value(reader)

        tensor_offsets: list[tuple[int, int]] = []  # (relative offset, byte extent)
        for _ in range(tensor_count):
            reader.string()  # tensor name
            n_dimensions = reader.u32()
            if n_dimensions > GGUF_MAX_RANK:
                raise IdentityError(f"GGUF tensor declares implausible rank {n_dimensions}")
            element_count = 1
            for _ in range(n_dimensions):
                dim = reader.u64()
                if dim < 0:
                    raise IdentityError("GGUF tensor dimension is negative")
                element_count *= dim
            ggml_type = reader.u32()
            bytes_per_element = GGUF_PLAIN_TYPE_BYTES.get(ggml_type)
            if bytes_per_element is None:
                raise IdentityError(
                    f"unsupported GGUF ggml tensor type {ggml_type}; "
                    "cannot assert structural completeness for this type"
                )
            data_size = element_count * bytes_per_element
            offset = reader.u64()
            tensor_offsets.append((offset, data_size))

        # The tensor data section begins after the tensor-info table, aligned to
        # 8 bytes. Each tensor offset is relative to that aligned data region.
        data_section_start = (reader.pos + 7) & ~7
        for offset, data_size in tensor_offsets:
            if offset < 0:
                raise IdentityError("GGUF tensor data offset is negative")
            if data_section_start + offset + data_size > size:
                raise IdentityError("GGUF tensor data extent lies beyond end of file")

    return {
        "format": "gguf",
        "ggufVersion": version,
        "tensorCount": tensor_count,
        "metadataKvCount": metadata_kv_count,
        "structurallyComplete": True,
    }


def sniff_safetensors(path: pathlib.Path) -> dict:
    size = path.stat().st_size
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

    payload_begin = 8 + header_len
    tensor_count = 0
    for name, tensor in parsed.items():
        if name == "__metadata__":
            continue
        if not isinstance(tensor, dict) or not {"data_offsets", "shape", "dtype"}.issubset(tensor):
            raise IdentityError(f"safetensors tensor {name!r} is malformed")
        dtype = tensor.get("dtype")
        dtype_bytes = SAFETENSORS_DTYPE_BYTES.get(dtype)
        if dtype_bytes is None:
            raise IdentityError(
                f"unsupported safetensors dtype {dtype!r}; cannot assert structural completeness"
            )
        shape = tensor.get("shape")
        if not isinstance(shape, list):
            raise IdentityError(f"safetensors tensor {name!r} must declare a shape list")
        element_count = 1
        for dim in shape:
            if not isinstance(dim, int) or isinstance(dim, bool) or dim < 0:
                raise IdentityError(f"safetensors tensor {name!r} has a non-integer shape entry")
            element_count *= dim
        extent = element_count * dtype_bytes
        offsets = tensor.get("data_offsets")
        if not (isinstance(offsets, list) and len(offsets) == 2):
            raise IdentityError(f"safetensors tensor {name!r} has malformed data_offsets")
        start, end = offsets
        if not (isinstance(start, int) and isinstance(end, int)):
            raise IdentityError(f"safetensors tensor {name!r} data_offsets must be integers")
        if start < 0 or end < start:
            raise IdentityError(f"safetensors tensor {name!r} has invalid data_offsets range")
        if end - start < extent:
            raise IdentityError(
                f"safetensors tensor {name!r} declared byte extent {extent} "
                f"does not fit in offsets [{start},{end})"
            )
        if payload_begin + end > size:
            raise IdentityError(
                f"safetensors tensor {name!r} declared data extends beyond end of file"
            )
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
    size = path.stat().st_size
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

    phoff = int.from_bytes(head[ELF64_E_PHOFF_OFFSET : ELF64_E_PHOFF_OFFSET + 8], "little")
    phentsize = int.from_bytes(
        head[ELF64_E_PHENTSIZE_OFFSET : ELF64_E_PHENTSIZE_OFFSET + 2], "little"
    )
    phnum = int.from_bytes(head[ELF64_E_PHNUM_OFFSET : ELF64_E_PHNUM_OFFSET + 2], "little")
    shoff = int.from_bytes(head[ELF64_E_SHOFF_OFFSET : ELF64_E_SHOFF_OFFSET + 8], "little")
    shentsize = int.from_bytes(
        head[ELF64_E_SHENTSIZE_OFFSET : ELF64_E_SHENTSIZE_OFFSET + 2], "little"
    )
    shnum = int.from_bytes(head[ELF64_E_SHNUM_OFFSET : ELF64_E_SHNUM_OFFSET + 2], "little")

    # PN_XNUM/SHN_XNUM indicate extended section-count forms; unsupported.
    if phnum == ELF_PN_XNUM:
        raise IdentityError("unsupported ELF extended program-header count (PN_XNUM)")
    if shnum == ELF_PN_XNUM:
        raise IdentityError("unsupported ELF extended section count (SHN_XNUM)")

    if phnum > 0:
        if phentsize < ELF64_PHDR_SIZE:
            raise IdentityError("ELF program-header entry size is smaller than ELF64 Phdr")
        if phoff + phentsize * phnum > size:
            raise IdentityError("ELF program-header table lies beyond end of file")
    if shnum > 0:
        if shentsize < ELF64_SHDR_SIZE:
            raise IdentityError("ELF section-header entry size is smaller than ELF64 Shdr")
        if shoff + shentsize * shnum > size:
            raise IdentityError("ELF section-header table lies beyond end of file")

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
