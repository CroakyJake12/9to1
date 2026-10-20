"""Installed-package closure verification.

Before a bundled runtime is admitted for inference, the verifier confirms that
the installed tree matches the recorded manifest: every declared asset exists at
its canonical install path as a regular file (never a symlink escape), has the
exact recorded SHA-256 and size, still parses as the declared runtime format,
and is architecture-compatible with the manifest host. It can also assert that
no unexpected files were smuggled into the owned assets subtree.
"""
from __future__ import annotations

import pathlib
from dataclasses import dataclass, field
from typing import Any

from . import STATE_DEGRADED, STATE_INVALID, STATE_READY
from .formats import elf_machine_matches, sniff_format
from .identity import (
    IdentityError,
    ensure_path_has_no_symlinks,
    file_identity,
    is_regular_file_no_follow,
    resolve_within,
)
from .manifest import parse_manifest


@dataclass
class AssetStatus:
    logical: str
    install_path: str
    state: str  # present | missing | hash-mismatch | size-mismatch | format-invalid | architecture-incompatible | unexpected
    detail: str = ""


@dataclass
class VerifyResult:
    state: str
    asset_statuses: list[AssetStatus] = field(default_factory=list)
    unexpected_paths: list[str] = field(default_factory=list)
    manifest: dict[str, Any] | None = None

    def ok(self) -> bool:
        return self.state == STATE_READY


def _asset_state_from_statuses(statuses: list[AssetStatus]) -> str:
    for status in statuses:
        if status.state != "present":
            return STATE_INVALID
    return STATE_READY


def verify_installed(
    install_root: pathlib.Path | str,
    manifest: dict[str, Any] | pathlib.Path | str,
    *,
    strict_closure: bool = True,
) -> VerifyResult:
    if isinstance(manifest, (pathlib.Path, str)):
        raw = _read_json(pathlib.Path(manifest))
    else:
        raw = manifest
    parsed = parse_manifest(raw, allow_invalid=True)
    root = pathlib.Path(install_root)
    host_arch = parsed["host"].get("arch", "")

    statuses: list[AssetStatus] = []
    declared_install_paths: set[str] = set()

    for asset in parsed["assets"]:
        install_path = str(asset["installPath"])
        declared_install_paths.add(install_path)
        try:
            ensure_path_has_no_symlinks(root, install_path)
            resolved = resolve_within(root, install_path)
        except IdentityError as exc:
            statuses.append(AssetStatus(asset["logical"], install_path, "path-unsafe", str(exc)))
            continue
        if not is_regular_file_no_follow(resolved):
            statuses.append(AssetStatus(asset["logical"], install_path, "missing", "not a regular file"))
            continue
        try:
            identity = file_identity(resolved)
        except IdentityError as exc:
            statuses.append(AssetStatus(asset["logical"], install_path, "unreadable", str(exc)))
            continue
        if identity.digest != asset["sha256"]:
            statuses.append(AssetStatus(asset["logical"], install_path, "hash-mismatch", "SHA-256 differs from manifest"))
            continue
        if identity.size != int(asset["size"]):
            statuses.append(AssetStatus(asset["logical"], install_path, "size-mismatch", "byte size differs from manifest"))
            continue
        try:
            format_meta = sniff_format(resolved, asset["runtimeFormat"])
        except IdentityError as exc:
            statuses.append(AssetStatus(asset["logical"], install_path, "format-invalid", str(exc)))
            continue
        if asset["kind"] == "runtime.binary" and format_meta.get("format") == "elf":
            if not elf_machine_matches(format_meta, host_arch):
                statuses.append(
                    AssetStatus(
                        asset["logical"],
                        install_path,
                        "architecture-incompatible",
                        f"host={host_arch} elf-machine={format_meta.get('machine')}",
                    )
                )
                continue
        statuses.append(AssetStatus(asset["logical"], install_path, "present"))

    unexpected: list[str] = []
    if strict_closure:
        assets_dir = root / "assets"
        if assets_dir.exists():
            for candidate in sorted(assets_dir.rglob("*")):
                if candidate.is_symlink() or not candidate.is_file():
                    continue
                rel = candidate.relative_to(root).as_posix()
                if rel not in declared_install_paths:
                    unexpected.append(rel)

    if unexpected:
        state = STATE_INVALID
    else:
        state = _asset_state_from_statuses(statuses)
        if not parsed["assets"]:
            state = STATE_INVALID

    return VerifyResult(state=state, asset_statuses=statuses, unexpected_paths=unexpected, manifest=parsed)


def _read_json(path: pathlib.Path) -> dict[str, Any]:
    import json

    return json.loads(path.read_text(encoding="utf-8"))
