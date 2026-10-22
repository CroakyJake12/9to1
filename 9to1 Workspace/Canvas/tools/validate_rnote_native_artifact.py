#!/usr/bin/env python3
"""Inspect an engine-only ELF artifact, never load it or certify Canvas acceptance.

No third-party Python modules are required. Runtime ABI values and round trips need
separate native tests. Hosted images and apt observations are NOT immutable inputs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import struct
import subprocess
import tomllib

CORE = frozenset("""cake_canvas_abi_version cake_canvas_engine_new
cake_canvas_engine_free cake_canvas_engine_from_rnote cake_canvas_begin_stroke
cake_canvas_update_stroke cake_canvas_end_stroke cake_canvas_set_pen_style
cake_canvas_set_stroke_tool cake_canvas_set_eraser cake_canvas_undo cake_canvas_redo
cake_canvas_save_rnote cake_canvas_buffer_release cake_canvas_render_frame
cake_canvas_render_frame_release""".split())
MACHINES = {"x86_64-unknown-linux-gnu": 62, "aarch64-unknown-linux-gnu": 183}
CANVAS = Path("9to1 Workspace/Canvas")


def receipt(path: Path) -> dict:
    data = path.read_bytes()
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}


def inspect_elf(path: Path) -> dict:
    """Bounded ELF64 LE section-table reader; no ctypes, dlopen, ldd or execution."""
    metadata = path.stat()
    if not stat.S_ISREG(metadata.st_mode):
        raise ValueError("ELF inspection requires a regular file")
    if metadata.st_size > 512 * 1024 * 1024:
        raise ValueError("ELF exceeds 512 MiB inspection bound")
    data = path.read_bytes()

    def unpack(fmt: str, offset: int):
        size = struct.calcsize(fmt)
        if offset < 0 or offset + size > len(data):
            raise ValueError("Truncated ELF structure")
        return struct.unpack_from(fmt, data, offset)

    if len(data) < 64 or data[:7] != b"\x7fELF\x02\x01\x01":
        raise ValueError("Expected ELF64 little-endian version 1")
    kind, machine = unpack("<HH", 16)
    if unpack("<I", 20)[0] != 1 or unpack("<H", 52)[0] != 64:
        raise ValueError("Unsupported ELF version/header size")
    if kind != 3:
        raise ValueError("Expected an ET_DYN shared object")
    section_offset = unpack("<Q", 40)[0]
    section_size, section_count = unpack("<HH", 58)
    if section_size != 64 or not 1 <= section_count <= 8192:
        raise ValueError("Unsupported/missing ELF section table")
    sections = [unpack("<IIQQQQIIQQ", section_offset + i * 64) for i in range(section_count)]

    def section_bytes(index: int) -> bytes:
        if not 0 <= index < len(sections):
            raise ValueError("ELF string-table link is out of bounds")
        section = sections[index]
        offset, size = section[4:6]
        if offset + size > len(data):
            raise ValueError("Truncated ELF section")
        return data[offset:offset + size]

    def string(table: bytes, offset: int) -> str:
        if not 0 <= offset < len(table):
            raise ValueError("ELF string offset is out of bounds")
        end = table.find(b"\0", offset)
        if end < 0:
            raise ValueError("Unterminated ELF string")
        return table[offset:end].decode("utf-8", errors="strict")

    exports, needed, search_paths = set(), [], []
    soname = None
    for index, section in enumerate(sections):
        kind, size, link, entry_size = section[1], section[5], section[6], section[9]
        if kind not in (6, 11):  # SHT_DYNAMIC, SHT_DYNSYM
            continue
        expected = 16 if kind == 6 else 24
        if entry_size != expected or size % expected or size // expected > 1_000_000:
            raise ValueError("Malformed ELF dynamic/symbol entries")
        table, entries = section_bytes(link), section_bytes(index)
        for offset in range(0, len(entries), expected):
            if kind == 6:
                tag, value = struct.unpack_from("<qQ", entries, offset)
                if tag == 0:
                    break
                if tag == 1:
                    needed.append(string(table, value))
                elif tag == 14:
                    soname = string(table, value)
                elif tag in (15, 29):
                    search_paths.append(string(table, value))
            else:
                name, info, visibility, definition, _, _ = struct.unpack_from("<IBBHQQ", entries, offset)
                if definition and info >> 4 in (1, 2) and info & 15 == 2 and visibility & 3 == 0:
                    exports.add(string(table, name))
    return {"machine": machine, "exports": sorted(exports), "needed": needed,
            "soname": soname, "rpath_runpath": search_paths, **receipt(path)}


def dependency_closure(library: Path, machine: int, directories: list[Path], copy_to: Path | None = None) -> list[dict]:
    """Resolve explicit native search roots only; reject loader semantics we do not model."""
    pending, seen, result = [library.resolve(strict=True)], set(), []
    while pending:
        path = pending.pop()
        if path in seen:
            continue
        if len(seen) >= 256:
            raise ValueError("Native dependency closure exceeds 256-object bound")
        seen.add(path)
        item = inspect_elf(path)
        if item["machine"] != machine:
            raise ValueError(f"Wrong ELF architecture: {path}")
        if item["rpath_runpath"]:
            raise ValueError(f"Unmodelled RPATH/RUNPATH needs explicit loader acceptance: {path}")
        edges = []
        for name in item["needed"]:
            if not name or "/" in name or name in (".", ".."):
                raise ValueError("DT_NEEDED must be a bounded library name")
            target = next((directory / name for directory in directories if (directory / name).is_file()), None)
            if target is None:
                raise ValueError(f"Unresolved DT_NEEDED {name} from {path}")
            target = target.resolve(strict=True)
            edges.append({"name": name, "resolved": str(target)})
            pending.append(target)
        item["dependencies"] = edges
        if copy_to is not None:
            destination = copy_to / item["sha256"] / path.name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, destination)
            item["retained_copy"] = str(destination)
        result.append(item)
    return sorted(result, key=lambda item: item["path"])


def source_policy(repo: Path) -> dict:
    manifest_path, lock_path = (repo / CANVAS / "rnote-poc" / name for name in ("Cargo.toml", "Cargo.lock"))
    manifest, lock = tomllib.loads(manifest_path.read_text()), tomllib.loads(lock_path.read_text())
    policy = json.loads((repo / "eng/donor-sources.json").read_text())["upstreams"]["rnote"]
    selected = policy["commit"]
    if not re.fullmatch(r"[0-9a-f]{40}", selected):
        raise ValueError("Selected Rnote revision is not an immutable commit")
    pins = {}
    for name in ("rnote-engine", "rnote-compose"):
        declared = manifest["dependencies"][name]
        matches = [package for package in lock["package"] if package["name"] == name]
        if len(matches) != 1:
            raise ValueError(f"Ambiguous locked package {name}")
        source = matches[0].get("source", "")
        revision = declared.get("rev", "")
        if not re.fullmatch(r"[0-9a-f]{40}", revision) or source != f"git+{declared['git']}?rev={revision}#{revision}":
            raise ValueError(f"Manifest/lock exact source mismatch for {name}")
        pins[name] = revision
    for package in lock["package"]:
        if package.get("source", "").startswith("registry+") and not re.fullmatch(r"[0-9a-f]{64}", package.get("checksum", "")):
            raise ValueError(f"Registry checksum absent/invalid: {package['name']}")
    managed = repo / CANVAS / "RnoteCanvasEngine.cs"
    imports = sorted(set(re.findall(r'EntryPoint\s*=\s*"(cake_canvas_[^\"]+)"', managed.read_text())))
    if not CORE <= set(imports):
        raise ValueError("Managed boundary no longer contains the audited core ABI")
    return {"selected_donor": selected, "locked_sources": pins,
            "selected_matches_locked": all(value == selected for value in pins.values()),
            "managed_imports": imports, "core_imports": sorted(CORE),
            "source_inputs": [receipt(path) for path in (manifest_path, lock_path, managed, repo / "eng/donor-sources.json")],
            "runtime_abi_version_execution": "NOT RUN by artifact inspector"}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--library", type=Path, required=True)
    parser.add_argument("--target", choices=MACHINES, required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--search-dir", type=Path, action="append", default=[])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--copy-closure", type=Path)
    parser.add_argument("--require-release-ready", action="store_true")
    args = parser.parse_args(argv)
    report = {"schema_version": 1, "target": args.target, "release_ready": False,
              "application_acceptance": "NOT RUN; engine-only source/binary inspection",
              "closure_scope": "Static DT_NEEDED closure in explicit roots; not a dynamic-loader trace or optional dlopen proof.",
              "licence_review": "NOT RUN; CI inventories are evidence, not licence qualification",
              "native_environment_reproducible": False,
              "environment_blocker": "Hosted runner image and apt packages lack a reviewed immutable image/snapshot; captured versions do not pin future builds."}
    code = 1
    try:
        actual = subprocess.check_output(["git", "-C", str(args.repo), "rev-parse", "HEAD"], text=True).strip()
        if actual != args.revision:
            raise ValueError("Checkout revision differs from requested immutable candidate")
        subprocess.check_output(["git", "-C", str(args.repo), "diff", "--exit-code", args.revision, "--",
                                 str(CANVAS / "rnote-poc"), str(CANVAS / "RnoteCanvasEngine.cs"),
                                 "eng/donor-sources.json", ".github/workflows/canvas-rnote-poc.yml"], text=True)
        report["revision"] = actual
        report["source"] = source_policy(args.repo)
        report["closure"] = dependency_closure(args.library, MACHINES[args.target], args.search_dir, args.copy_closure)
        artifact = next(item for item in report["closure"] if Path(item["path"]) == args.library.resolve())
        exports = set(artifact["exports"])
        report["missing_core"] = sorted(CORE - exports)
        report["missing_full"] = sorted(set(report["source"]["managed_imports"]) - exports)
        report["core_binary_contract"] = "VERIFIED EXPORTS ONLY" if not report["missing_core"] else "BLOCKED"
        report["release_blockers"] = [report["environment_blocker"]]
        if not report["source"]["selected_matches_locked"]:
            report["release_blockers"].append("Selected donor differs from actual locked Rust producer; do not relabel the old engine.")
        if report["missing_full"]:
            report["release_blockers"].append("Managed full API imports missing from actual native exports.")
        code = 1 if report["missing_core"] else (2 if args.require_release_ready else 0)
    except (OSError, ValueError, KeyError, struct.error, subprocess.SubprocessError) as error:
        report["inspection_failure"] = {"type": type(error).__name__, "message": str(error)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".tmp")
    temporary.write_text(json.dumps(report, indent=2) + "\n")
    os.replace(temporary, args.output)
    print(json.dumps({"report": str(args.output), "exit_code": code, "release_ready": False}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
