#!/usr/bin/env python3
"""Bounded structural-admission entrypoint for a single model blob.

This is the exact artifact the root-owned packaged consumer can ship beside the
canonical broker (e.g. ``/usr/lib/haven/inference/bundle/structural_validate.py``)
so that packaged closure verification can call the SAME bounded structural sniffer
that this lane validates. It performs structural format/provenance admission only:

  * it never decodes weights, allocates tensors, loads a model or executes it,
  * it returns exit 0 only when the container is structurally complete for a
    supported declared format (gguf / safetensors / elf),
  * any failure (unknown format, unsupported type, malformed bounds, truncation)
    returns a non-zero exit and a one-line diagnostic on stderr.

It must never be used to claim runtime/model readiness: structural completeness is
a distinct, narrower claim than an installed/verified/running/capable model.
"""
from __future__ import annotations

import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

from bundle.formats import sniff_format  # noqa: E402
from bundle.identity import IdentityError  # noqa: E402


def _classify_path(path: pathlib.Path) -> str:
    suffix = path.suffix.lower()
    if suffix == ".safetensors":
        return "safetensors"
    if suffix == ".gguf":
        return "gguf"
    return "elf"


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print("usage: structural_validate.py <path> [declared-format]", file=sys.stderr)
        return 2
    path = pathlib.Path(argv[1])
    if not path.is_file():
        print(f"structural_validate: not a regular file: {path}", file=sys.stderr)
        return 2
    declared = argv[2] if len(argv) > 2 else _classify_path(path)
    try:
        meta = sniff_format(path, declared)
    except IdentityError as exc:
        print(f"structural_validate: reject: {exc}", file=sys.stderr)
        return 1
    if not meta.get("structurallyComplete"):
        print(f"structural_validate: not structurally complete: {meta}", file=sys.stderr)
        return 1
    print(f"structural_validate: pass ({declared})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
