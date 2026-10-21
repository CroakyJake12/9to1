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
# Discriminants verified against upstream ggml.h (ggml-org/llama.cpp master,
# SHA 475beaa7e959be8c383279d9ce9d5191f37426e6b338270cfb12b26a798129d3):
#   F32=0, F16=1, I8=24, I16=25, I32=26, I64=27, F64=28, BF16=30.
# Anything else is not a valid scalar width and is rejected (or, for quantized
# types, handled via GGUF_QUANTIZED_TYPES) rather than assumed complete.
GGUF_PLAIN_TYPE_BYTES = {
    0: 4,   # F32
    1: 2,   # F16
    24: 1,  # I8
    25: 2,  # I16
    26: 4,  # I32
    27: 8,  # I64
    28: 8,  # F64
    30: 2,  # BF16
}
# Quantized GGUF ggml tensor types we can compute a block-based byte extent for.
# Only Q6_K (tag 14) is supported; block parameters verified against the official
# ggml structs: block_q6_K has 256 elements (QK_K) that 210 bytes
# (2-byte half scale + 128 ql + 64 qh + 16 scales). Other quantized types stay
# fail-closed. Element count must be divisible by the block element count.
GGUF_QUANTIZED_TYPES = {
    14: {"name": "Q6_K", "block_elements": 256, "block_bytes": 210},
}
GGUF_MAX_RANK = 64
# GGUF tensor data begins aligned to the file's `general.alignment` metadata
# value (uint32), which defaults to 32 bytes when absent.
GGUF_DEFAULT_ALIGNMENT = 32

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


def _decode_gguf_scalar(vtype: int, raw: bytes):
    """Decode a scalar GGUF metadata value by its type tag."""
    if vtype == GGUF_TYPE_UINT8:
        return raw[0]
    if vtype == GGUF_TYPE_INT8:
        return struct.unpack("<b", raw)[0]
    if vtype == GGUF_TYPE_UINT16:
        return struct.unpack("<H", raw)[0]
    if vtype == GGUF_TYPE_INT16:
        return struct.unpack("<h", raw)[0]
    if vtype == GGUF_TYPE_UINT32:
        return struct.unpack("<I", raw)[0]
    if vtype == GGUF_TYPE_INT32:
        return struct.unpack("<i", raw)[0]
    if vtype == GGUF_TYPE_FLOAT32:
        return struct.unpack("<f", raw)[0]
    if vtype == GGUF_TYPE_BOOL:
        return raw[0] != 0
    if vtype == GGUF_TYPE_UINT64:
        return struct.unpack("<Q", raw)[0]
    if vtype == GGUF_TYPE_INT64:
        return struct.unpack("<q", raw)[0]
    if vtype == GGUF_TYPE_FLOAT64:
        return struct.unpack("<d", raw)[0]
    raise AssertionError(f"unreachable GGUF scalar type {vtype}")


def _read_gguf_metadata_value(reader: _Reader):
    """Read one GGUF metadata value and return ``(vtype, value)``.

    The declared type tag is preserved so callers can enforce exact scalar-type
    contracts (e.g. ``general.alignment`` must be uint32). Strings are decoded
    (lossy) to str; arrays become lists; scalars become int/float/bool per their
    type tag. Unknown types fail closed with a precise ``unsupported``
    diagnostic.
    """
    vtype = reader.u32()
    if vtype == GGUF_TYPE_STRING:
        return vtype, reader.string().decode("utf-8", "replace")
    if vtype == GGUF_TYPE_ARRAY:
        elem_type = reader.u32()
        count = reader.u64()
        if count > reader.size:
            raise IdentityError("malformed GGUF array count exceeds file size")
        values = []
        for _ in range(count):
            if elem_type == GGUF_TYPE_STRING:
                values.append(reader.string().decode("utf-8", "replace"))
            elif elem_type in GGUF_SCALAR_SIZE:
                values.append(_decode_gguf_scalar(elem_type, reader.take(GGUF_SCALAR_SIZE[elem_type])))
            else:
                raise IdentityError(f"unknown GGUF array element type {elem_type}")
        return vtype, values
    size = GGUF_SCALAR_SIZE.get(vtype)
    if size is None:
        raise IdentityError(f"unknown GGUF metadata value type {vtype}")
    return vtype, _decode_gguf_scalar(vtype, reader.take(size))


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

        metadata: dict[str, tuple[int, object]] = {}
        for _ in range(metadata_kv_count):
            key = reader.string().decode("utf-8", "replace")
            vtype, value = _read_gguf_metadata_value(reader)
            if key in metadata:
                raise IdentityError(f"duplicate GGUF metadata key {key!r}")
            metadata[key] = (vtype, value)

        # GGUF tensor data begins right after the tensor-info table, aligned to
        # the file's declared `general.alignment`. The GGUF contract types this
        # key uint32 and defaults it to 32. We preserve the declared scalar type
        # (must be exactly uint32) rather than accepting any signed/64-bit int;
        # bool/float/string/array and all non-uint32 integer types fail closed.
        alignment = GGUF_DEFAULT_ALIGNMENT
        if "general.alignment" in metadata:
            declared_vtype, declared_alignment = metadata["general.alignment"]
            if declared_vtype != GGUF_TYPE_UINT32:
                raise IdentityError(
                    "GGUF general.alignment must be a uint32 scalar "
                    f"(got type tag {declared_vtype})"
                )
            if declared_alignment <= 0:
                raise IdentityError("GGUF general.alignment must be a positive uint32")
            alignment = declared_alignment

        tensor_offsets: list[tuple[int, int]] = []  # (relative offset, byte extent)
        for _ in range(tensor_count):
            reader.string()  # tensor name
            n_dimensions = reader.u32()
            if n_dimensions > GGUF_MAX_RANK:
                raise IdentityError(f"GGUF tensor declares implausible rank {n_dimensions}")
            element_count = 1
            first_dim: int | None = None
            for dim_index in range(n_dimensions):
                dim = reader.u64()
                if dim < 0:
                    raise IdentityError("GGUF tensor dimension is negative")
                if dim_index == 0:
                    first_dim = dim
                element_count *= dim
            ggml_type = reader.u32()
            if ggml_type in GGUF_PLAIN_TYPE_BYTES:
                bytes_per_element = GGUF_PLAIN_TYPE_BYTES[ggml_type]
                data_size = element_count * bytes_per_element
            elif ggml_type in GGUF_QUANTIZED_TYPES:
                info = GGUF_QUANTIZED_TYPES[ggml_type]
                block_elements = info["block_elements"]
                block_bytes = info["block_bytes"]
                # ggml stores quantized tensors in blocks that run along the
                # innermost (row) dimension ne[0]. The row dimension must
                # therefore be divisible by the block element count; checking
                # only the total element count would wrongly admit e.g.
                # shape [128,2] (total 256) for a 256-element block.
                if first_dim is None or first_dim % block_elements != 0:
                    raise IdentityError(
                        f"GGUF tensor row dimension {first_dim} is not divisible "
                        f"by {info['name']} block size {block_elements}"
                    )
                if element_count % block_elements != 0:
                    raise IdentityError(
                        f"GGUF tensor element count {element_count} is not divisible "
                        f"by {info['name']} block size {block_elements}"
                    )
                data_size = (element_count // block_elements) * block_bytes
            else:
                raise IdentityError(
                    f"unsupported GGUF ggml tensor type {ggml_type}; "
                    "cannot assert structural completeness for this type"
                )
            offset = reader.u64()
            tensor_offsets.append((offset, data_size))

        # Align after the tensor-info table using the validated alignment. Each
        # tensor offset is relative to this aligned data region.
        data_section_start = ((reader.pos + alignment - 1) // alignment) * alignment
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
        "dataAlignment": alignment,
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

    buffer_len = size - (8 + header_len)
    tensor_count = 0
    intervals: list[tuple[int, int]] = []  # non-empty tensor slices (start, end)
    for name, tensor in parsed.items():
        if name == "__metadata__":
            # The safetensors __metadata__ map must be string -> string. Any
            # array/scalar/nested-object impostor is rejected before a
            # structurallyComplete claim.
            if not isinstance(tensor, dict):
                raise IdentityError("safetensors __metadata__ must be a JSON object")
            for meta_key, meta_value in tensor.items():
                if not isinstance(meta_key, str) or not isinstance(meta_value, str):
                    raise IdentityError("safetensors __metadata__ must map strings to strings")
            continue
        if not isinstance(tensor, dict) or not {"data_offsets", "shape", "dtype"}.issubset(tensor):
            raise IdentityError(f"safetensors tensor {name!r} is malformed")
        dtype = tensor.get("dtype")
        # dtype must be a plain string before dict lookup; an unhashable or
        # non-string dtype must raise a typed IdentityError, never a TypeError.
        if not isinstance(dtype, str):
            raise IdentityError(
                f"unsupported safetensors dtype {dtype!r}; cannot assert structural completeness"
            )
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
        # Python bools are ints; they must NOT count as valid offset integers.
        if (not isinstance(start, int)) or (not isinstance(end, int)) or isinstance(start, bool) or isinstance(end, bool):
            raise IdentityError(f"safetensors tensor {name!r} data_offsets must be integers")
        if start < 0 or end < start:
            raise IdentityError(f"safetensors tensor {name!r} has invalid data_offsets range")
        # The declared byte slice must exactly equal the dtype/shape extent; any
        # larger slice (contradictory padding) or smaller slice (truncation) is
        # rejected. Zero-size tensors (start == end) remain valid.
        if end - start != extent:
            raise IdentityError(
                f"safetensors tensor {name!r} declared byte extent {extent} "
                f"does not exactly match offsets [{start},{end})"
            )
        if end > buffer_len:
            raise IdentityError(
                f"safetensors tensor {name!r} declared data extends beyond end of file"
            )
        if start < end:
            intervals.append((start, end))
        tensor_count += 1

    if tensor_count == 0:
        raise IdentityError("safetensors file declares no tensors (not a usable model asset)")

    # Non-empty tensor slices must be non-overlapping and must exactly tile the
    # entire data buffer (no holes, no trailing uncovered bytes).
    intervals.sort()
    cursor = 0
    for start, end in intervals:
        if start < cursor:
            raise IdentityError("safetensors tensor regions overlap")
        if start != cursor:
            raise IdentityError("safetensors tensor regions leave an uncovered gap")
        cursor = end
    if cursor != buffer_len:
        raise IdentityError("safetensors tensor regions do not cover the whole data buffer")

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
