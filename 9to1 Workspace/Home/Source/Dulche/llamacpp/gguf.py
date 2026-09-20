"""Minimal, side-effect-free GGUF header validation for Haven model admission."""
from __future__ import annotations

import pathlib
import struct

SUPPORTED_GGUF_VERSIONS = frozenset({2, 3})


class GgufValidationError(ValueError):
    pass


def read_gguf_version(path: pathlib.Path) -> int:
    with path.open("rb") as handle:
        header = handle.read(8)
    if len(header) != 8:
        raise GgufValidationError("GGUF header is truncated")
    if header[:4] != b"GGUF":
        raise GgufValidationError("file does not have GGUF magic")
    version = struct.unpack("<I", header[4:8])[0]
    if version not in SUPPORTED_GGUF_VERSIONS:
        raise GgufValidationError(f"unsupported GGUF version {version}; Slice 0 accepts versions 2 and 3")
    return version
