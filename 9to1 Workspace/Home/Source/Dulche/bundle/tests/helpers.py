"""Test-only fixture builders.

Every fixture here is generated at test time from small byte payloads and is
explicitly TESTONLY. These are NOT real models or real runtimes; they exist only
to exercise the packager/verifier validation logic. Do not claim them as real
generation or as production assets.
"""
from __future__ import annotations

import hashlib
import json
import pathlib
import struct


def make_gguf(version: int = 3, payload: bytes = b"\x00\x01\x02\x03") -> bytes:
    return b"GGUF" + struct.pack("<I", version) + payload


def make_safetensors(tensor_count: int = 1) -> bytes:
    tensors = {}
    offset = 0
    for index in range(tensor_count):
        shape = [4]
        dtype = "F32"
        length = 4 * 4
        tensors[f"tensor{index}"] = {
            "dtype": dtype,
            "shape": shape,
            "data_offsets": [offset, offset + length],
        }
        offset += length
    header = json.dumps(tensors, separators=(",", ":")).encode("utf-8")
    header_len = struct.pack("<Q", len(header))
    weights = b"\x00" * offset
    return header_len + header + weights


def make_elf(machine: int = 62, *, data_little: int = 1, class64: int = 2) -> bytes:
    head = bytearray(20)
    head[0:4] = b"\x7fELF"
    head[4] = class64
    head[5] = data_little
    head[7] = 0  # System V OSABI
    head[16:18] = (2).to_bytes(2, "little")  # e_type = ET_EXEC
    head[18:20] = machine.to_bytes(2, "little")
    return bytes(head)


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
