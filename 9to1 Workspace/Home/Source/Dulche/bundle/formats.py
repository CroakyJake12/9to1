"""Runtime-format sniffers for admitted asset files.

These are lightweight, side-effect-free header validators. They prove that a
supplied file has the *format* it claims; they never decode weights or execute
anything. Unknown but explicitly labelled formats are handled with an explicit
``opaque`` runtime format whose bytes are still hashed and size-checked.
"""
from __future__ import annotations

import json
import pathlib
import struct

from .identity import IdentityError

GGUF_MAGIC = b"GGUF"
SUPPORTED_GGUF_VERSIONS = frozenset({2, 3})

ELF_MAGIC = b"\x7fELF"
ELF_CLASS_64 = 2
ELF_DATA_LITTLE = 1
# e_machine constants (Linux ELF).
ELF_MACHINE_X86_64 = 62
ELF_MACHINE_AARCH64 = 183
ELF_MACHINE_RISCV = 243
ELF_MACHINE_ARM = 40
ELF_MACHINE_AMDGPU = 224


def sniff_gguf(path: pathlib.Path) -> dict:
    with path.open("rb") as handle:
        header = handle.read(8)
    if len(header) != 8:
        raise IdentityError("GGUF header is truncated")
    if header[:4] != GGUF_MAGIC:
        raise IdentityError("file does not have GGUF magic")
    version = struct.unpack("<I", header[4:8])[0]
    if version not in SUPPORTED_GGUF_VERSIONS:
        raise IdentityError(f"unsupported GGUF version {version}; expected 2 or 3")
    return {"format": "gguf", "ggufVersion": version}


def sniff_safetensors(path: pathlib.Path) -> dict:
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
    # Optional but useful structural check: tensor weights encode offset+shape+data type.
    for name, tensor in parsed.items():
        if name == "__metadata__":
            continue
        if not isinstance(tensor, dict) or not {"data_offsets", "shape", "dtype"}.issubset(tensor):
            raise IdentityError(f"safetensors tensor {name!r} is malformed")
    return {"format": "safetensors", "headerBytes": header_len, "tensors": sum(1 for k in parsed if k != "__metadata__")}


def sniff_elf(path: pathlib.Path) -> dict:
    with path.open("rb") as handle:
        head = handle.read(20)
    if len(head) < 20:
        raise IdentityError("ELF header is truncated")
    if head[:4] != ELF_MAGIC:
        raise IdentityError("file does not have ELF magic")
    if head[4] != ELF_CLASS_64:
        raise IdentityError("ELF is not 64-bit class")
    if head[5] != ELF_DATA_LITTLE:
        raise IdentityError("ELF is not little-endian")
    machine = int.from_bytes(head[18:20], "little")
    osabi = head[7] if len(head) > 7 else 0
    return {"format": "elf", "machine": machine, "osabi": osabi}


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
    """Validate *declared* runtime format against the actual file head.

    ``declared`` is one of: ``gguf``, ``safetensors``, ``elf``, ``opaque``.
    """
    kind = str(declared).strip().lower()
    if kind == "opaque":
        return {"format": "opaque", "declared": "opaque"}
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
