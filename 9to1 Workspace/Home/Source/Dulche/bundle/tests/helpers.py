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
