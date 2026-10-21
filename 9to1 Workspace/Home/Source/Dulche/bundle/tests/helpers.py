"""Test-only fixture builders.

Every fixture here is generated at test time from small byte payloads and is
explicitly TESTONLY. These are NOT real models or real runtimes; they exist only
to exercise the packager/verifier validation logic. Do not claim them as real
generation or as production assets.

The default builders emit STRUCTURALLY COMPLETE (non-truncated) byte structures so
positive tests can be distinguished from explicit truncation tests. Truncated
variants are produced by dedicated ``make_truncated_*`` helpers.
"""
from __future__ import annotations

import hashlib
import json
import pathlib
import struct

ELF64_MIN_HEADER_BYTES = 64


def make_gguf(version: int = 3, payload: bytes = b"", tensor_count: int = 0, metadata_kv_count: int = 0) -> bytes:
    """A structurally valid GGUF v2/v3 primary header (24 bytes) plus payload.

    Default: zero tensors and zero metadata pairs, which parses as a complete
    GGUF header table. ``payload`` is appended after the header (optional).
    """
    header = b"GGUF" + struct.pack("<I", version)
    header += struct.pack("<Q", tensor_count)
    header += struct.pack("<Q", metadata_kv_count)
    return header + payload


def make_truncated_gguf(version: int = 3) -> bytes:
    """A 12-byte header-truncated GGUF (magic + version + 4 payload bytes)."""
    return b"GGUF" + struct.pack("<I", version) + b"\x00\x01\x02\x03"


def make_gguf_single_tensor(
    *,
    version: int = 3,
    dims: tuple[int, ...] = (1,),
    ggml_type: int = 0,  # F32
    data: bytes = b"\x01\x02\x03\x04",
) -> bytes:
    """A structurally valid GGUF with one tensor, uint64 dimensions and a real
    aligned payload region. ``data`` must be at least the dtype's byte extent.
    """
    header = b"GGUF" + struct.pack("<IQQ", version, 1, 0)
    name = b"x"
    ti = struct.pack("<Q", len(name)) + name
    ti += struct.pack("<I", len(dims))
    for dim in dims:
        ti += struct.pack("<Q", dim)
    ti += struct.pack("<I", ggml_type)
    offset = 0
    ti += struct.pack("<Q", offset)
    base = header + ti
    data_start = (len(base) + 7) & ~7
    padding = data_start - len(base)
    return base + b"\x00" * padding + data


def make_gguf_high_offset() -> bytes:
    """A 57-byte GGUF declaring a rank-1 uint64 dimension but a 4GiB tensor
    data offset with no payload. This mirrors the independent FAIL probe.
    """
    bad = b"GGUF" + struct.pack("<IQQ", 3, 1, 0)  # version 3, tensor_count 1, kv 0
    bad += struct.pack("<Q", 1) + b"x"  # tensor name
    bad += struct.pack("<I", 1)  # n_dimensions = 1
    bad += struct.pack("<Q", 1)  # dimension (uint64) = 1
    bad += struct.pack("<I", 0)  # ggml type F32
    bad += struct.pack("<Q", 1 << 32)  # data offset = 4GiB
    return bad


def _gguf_str(value: str) -> bytes:
    encoded = value.encode("utf-8")
    return struct.pack("<Q", len(encoded)) + encoded


def make_gguf_probe_tensor(*, padding: int = 31, data: bytes = b"\x00" * 4) -> bytes:
    """The exact 65-byte rank-1 F32 tensor-info table from the independent probe,
    followed by ``padding`` zero bytes and ``data``.

    With default GGUF alignment (32), padding=31 places the 4-byte payload at the
    aligned data start (offset 96), a structurally valid positive fixture. With
    padding=7 (the probe), the same 76-byte file is truncated relative to the
    required 32-byte alignment and must be rejected.
    """
    table = b"GGUF" + struct.pack("<IQQ", 3, 1, 0)  # version 3, tensor_count 1, kv 0
    table += struct.pack("<Q", 9) + b"tensor001"
    table += struct.pack("<I", 1)  # n_dimensions = 1
    table += struct.pack("<Q", 1)  # dimension (uint64) = 1
    table += struct.pack("<I", 0)  # ggml type F32
    table += struct.pack("<Q", 0)  # data offset, relative to aligned data start
    assert len(table) == 65
    return table + b"\x00" * padding + data


def build_gguf(
    *,
    version: int = 3,
    metadata_items: list[tuple[str, int, bytes]] | None = None,
    tensors: list[tuple[str, tuple[int, ...], int, int, bytes]] | None = None,
    alignment: int = 32,
) -> bytes:
    """Build a structurally valid GGUF with metadata and tensors aligned to
    ``alignment``.

    ``metadata_items`` are ``(key, value_type_tag, encoded_value_bytes)`` triples
    (one per key). ``tensors`` are ``(name, dims, ggml_type, offset, data)``
    tuples where ``offset`` is relative to the aligned data start and ``data`` is
    appended in listed order. TESTONLY fixture builder only.
    """
    metadata_items = metadata_items or []
    tensors = tensors or []
    body = b"GGUF" + struct.pack("<IQQ", version, len(tensors), len(metadata_items))
    for key, vtype, valbytes in metadata_items:
        body += _gguf_str(key) + struct.pack("<I", vtype) + valbytes
    for name, dims, ggml_type, offset, _data in tensors:
        body += _gguf_str(name)
        body += struct.pack("<I", len(dims))
        for dim in dims:
            body += struct.pack("<Q", dim)
        body += struct.pack("<I", ggml_type)
        body += struct.pack("<Q", offset)
    table_end = len(body)
    aligned = ((table_end + alignment - 1) // alignment) * alignment
    body += b"\x00" * (aligned - table_end)
    for _name, _dims, _ggml_type, _offset, data in tensors:
        body += data
    return body


def make_safetensors(tensor_count: int = 1) -> bytes:
    """A structurally valid safetensors file with ``tensor_count`` tensors.

    Each tensor declares a shape/data_offsets whose payload region is fully
    materialised after the 8-byte length prefix.
    """
    tensors = {}
    offset = 0
    for index in range(tensor_count):
        shape = [4]
        length = 4 * 4
        tensors[f"tensor{index}"] = {
            "dtype": "F32",
            "shape": shape,
            "data_offsets": [offset, offset + length],
        }
        offset += length
    header = json.dumps(tensors, separators=(",", ":")).encode("utf-8")
    header_len = struct.pack("<Q", len(header))
    weights = b"\x00" * offset
    return header_len + header + weights


def make_safetensors_out_of_bounds() -> bytes:
    """A safetensors whose declared data_offsets point beyond the real file."""
    header_obj = {"tensor0": {"dtype": "F32", "shape": [4], "data_offsets": [0, 100000]}}
    header = json.dumps(header_obj, separators=(",", ":")).encode("utf-8")
    header_len = struct.pack("<Q", len(header))
    return header_len + header + b"\x00" * 4


def make_elf(machine: int = 62, *, data_little: int = 1, class64: int = 2) -> bytes:
    """A structurally valid 64-byte ELF64 file header.

    The returned header is the full e_ident + file-header fields declared by the
    ELF64 specification (64 bytes, e_ehsize == 64). It is a structural fixture,
    not a runnable program.
    """
    e_ident = bytearray(16)
    e_ident[0:4] = b"\x7fELF"
    e_ident[4] = class64
    e_ident[5] = data_little
    e_ident[6] = 1  # e_version
    e_ident[7] = 0  # osabi (System V)
    head = bytearray(e_ident)
    head += struct.pack("<H", 2)  # e_type = ET_EXEC
    head += struct.pack("<H", machine)  # e_machine
    head += struct.pack("<I", 1)  # e_version
    head += struct.pack("<Q", 0)  # e_entry
    head += struct.pack("<Q", 0)  # e_phoff
    head += struct.pack("<Q", 0)  # e_shoff
    head += struct.pack("<I", 0)  # e_flags
    head += struct.pack("<H", 64)  # e_ehsize
    head += struct.pack("<H", 56)  # e_phentsize
    head += struct.pack("<H", 0)  # e_phnum
    head += struct.pack("<H", 64)  # e_shentsize
    head += struct.pack("<H", 0)  # e_shnum
    head += struct.pack("<H", 0)  # e_shstrndx
    return bytes(head)


def make_truncated_elf(machine: int = 62) -> bytes:
    """A 20-byte ELF64 header truncated far below the required 64 bytes."""
    return b"\x7fELF" + bytes([2, 1, 1]) + bytes(13) + struct.pack("<H", machine)


def make_elf_with_program_table(
    *,
    machine: int = 62,
    phoff: int = 64,
    phentsize: int = 56,
    phnum: int = 1,
    append_table: bool = True,
) -> bytes:
    """A structurally valid 64-byte ELF64 header plus (optionally) a
    program-header table.

    Defaults declare one 56-byte program header at offset 64 (immediately after
    the header) and append it, forming a valid bounded non-empty table. Set
    ``append_table=False`` to produce a header that *declares* a table at
    ``phoff`` without the table bytes actually present (out-of-bounds /
    malformed). Pass non-default values to construct other malformed forms.
    """
    elf = bytearray(make_elf(machine))
    struct.pack_into("<Q", elf, 32, phoff)  # e_phoff
    struct.pack_into("<H", elf, 54, phentsize)  # e_phentsize
    struct.pack_into("<H", elf, 56, phnum)  # e_phnum
    head = bytes(elf)
    if append_table and phnum > 0 and phoff >= ELF64_MIN_HEADER_BYTES:
        head += b"\x00" * (phentsize * phnum)
    return head


def write_fixture(root: pathlib.Path, relative: str, data: bytes) -> pathlib.Path:
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return path


def sha256_of(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def minimal_spec(
    *,
    bundle_id: str = "dulche-bundle-test",
    bundle_version: str = "0.0.1-test",
    host_arch: str = "x86_64",
    assets: list[dict] | None = None,
) -> dict:
    if assets is None:
        assets = [
            {
                "logical": "smollm-test",
                "kind": "model.gguf",
                "engine": "llamacpp",
                "platform": {"os": "linux", "arch": host_arch},
                "runtimeFormat": "gguf",
                "source": "https://example.invalid/model-card",
                "license": "Apache-2.0",
                "path": "fixtures/smollm.gguf",
            }
        ]
    return {
        "bundleId": bundle_id,
        "bundleVersion": bundle_version,
        "host": {"os": "linux", "arch": host_arch},
        "assets": assets,
    }
