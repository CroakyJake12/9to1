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

MISSING_RECORD_REQUIRED_FIELDS = ("logical", "kind", "engine", "required", "reason")


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


def _validate_missing_records(missing: list[Any]) -> None:
    """Validate missingAssets entries. Malformed records are a hard invalid.

    We never trust a possibly-rewritten ``state`` field; each record must itself
    be well-formed so the verifier can recompute closure from it.
    """
    for item in missing:
        if not isinstance(item, dict):
            raise IdentityError("missingAssets entries must be objects")
        missing_fields = [key for key in MISSING_RECORD_REQUIRED_FIELDS if key not in item]
        if missing_fields:
            raise IdentityError(f"missingAsset record missing fields: {', '.join(missing_fields)}")
        if not isinstance(item.get("logical"), str) or not item.get("logical"):
            raise IdentityError("missingAsset record must have a non-empty logical id")
        if not isinstance(item.get("kind"), str) or not item.get("kind"):
            raise IdentityError("missingAsset record must have a non-empty kind")
        if not isinstance(item.get("engine"), str) or not item.get("engine"):
            raise IdentityError("missingAsset record must have a non-empty engine")
        if not isinstance(item.get("required"), bool):
            raise IdentityError("missingAsset record 'required' must be a boolean")
        if not isinstance(item.get("reason"), str) or not item.get("reason"):
            raise IdentityError("missingAsset record must have a non-empty reason")


def _recompute_state(
    *,
    missing: list[dict[str, Any]],
    statuses: list[AssetStatus],
    unexpected: list[str],
    advertised_state: str,
    assets_present: bool,
) -> str:
    """Recompute ready/degraded/invalid from the complete required closure.

    This intentionally does NOT trust the manifest's ``state`` field. We derive
    the effective state from:
    - any required missingAssets entry (=> invalid),
    - any installed-asset status other than present (=> invalid),
    - a strict-closure unexpected file (=> invalid),
    - optional-only missingAssets when everything else is fine (=> degraded),
    - otherwise ready (provided at least one admitted asset exists).
    A contradictory advertised state never grants readiness.
    """
    if not assets_present:
        return STATE_INVALID

    required_missing = any(item.get("required") for item in missing)
    if required_missing:
        return STATE_INVALID

    if any(status.state != "present" for status in statuses):
        return STATE_INVALID

    if unexpected:
        return STATE_INVALID

    if missing:
        # Only optional missing assets remain and everything present verified.
        return STATE_DEGRADED

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

    try:
        _validate_missing_records(parsed.get("missingAssets", []))
    except IdentityError as exc:
        return VerifyResult(state=STATE_INVALID, asset_statuses=[], unexpected_paths=[], manifest=parsed)

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

    state = _recompute_state(
        missing=parsed.get("missingAssets", []),
        statuses=statuses,
        unexpected=unexpected,
        advertised_state=parsed.get("state", STATE_INVALID),
        assets_present=bool(parsed["assets"]),
    )

    return VerifyResult(state=state, asset_statuses=statuses, unexpected_paths=unexpected, manifest=parsed)


def _read_json(path: pathlib.Path) -> dict[str, Any]:
    import json

    return json.loads(path.read_text(encoding="utf-8"))
