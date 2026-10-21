"""The bundled Dulche packager.

Given an explicit BundleSpec and a root containing the real files the integrator
supplied, the packager:

1. safely reads each supplied file (regular file, no symlink escape),
2. computes its SHA-256 and size (never trusting a pin in place of hashing),
3. validates runtime format and platform/architecture compatibility,
4. cross-checks any supplier-declared expected hash/size,
5. records honest per-asset state (present / missing / rejected),
6. emits a versioned manifest plus a deterministic receipt.

It never downloads anything, invents a pin, or fabricates a hash.
"""
from __future__ import annotations

import datetime
import json
import pathlib
from dataclasses import dataclass, field
from typing import Any

from . import STATE_DEGRADED, STATE_INVALID, STATE_READY
from .formats import elf_machine_matches, sniff_format
from .identity import IdentityError, file_identity, is_regular_file_no_follow, resolve_within
from .manifest import AdmittedAsset, MissingAsset, canonical_dumps, render_manifest
from .receipt import build_receipt, receipt_bytes
from .spec import BundleSpec


@dataclass
class PackResult:
    manifest: dict[str, Any]
    receipt: dict[str, Any]
    admitted: list[AdmittedAsset] = field(default_factory=list)
    missing: list[MissingAsset] = field(default_factory=list)
    rejected: list[dict[str, Any]] = field(default_factory=list)

    @property
    def state(self) -> str:
        return self.manifest["state"]

    def write(self, out_dir: pathlib.Path, *, prefix: str = "bundle") -> dict[str, str]:
        out = pathlib.Path(out_dir)
        out.mkdir(parents=True, exist_ok=True)
        manifest_path = out / f"{prefix}-manifest.json"
        receipt_path = out / f"{prefix}-receipt.json"
        manifest_path.write_bytes(canonical_dumps(self.manifest))
        receipt_path.write_bytes(receipt_bytes(self.receipt))
        return {
            "manifest": str(manifest_path),
            "receipt": str(receipt_path),
            "state": self.state,
        }


def _utc_now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")


def pack_bundle(
    spec: BundleSpec,
    assets_root: pathlib.Path | str,
    *,
    include_timestamp: bool = False,
) -> PackResult:
    root = pathlib.Path(assets_root)
    admitted: list[AdmittedAsset] = []
    missing: list[MissingAsset] = []
    rejected: list[dict[str, Any]] = []

    logicals_with_digest: dict[str, str] = {}
    install_path_digests: dict[str, str] = {}

    for asset in spec.assets:
        try:
            source = resolve_within(root, asset.path)
        except IdentityError as exc:
            rejected.append(_rejected(asset, f"path-unsafe:{exc}"))
            continue
        if not is_regular_file_no_follow(source):
            missing.append(
                MissingAsset(
                    logical=asset.logical_id,
                    kind=asset.kind,
                    engine=asset.engine,
                    required=asset.required,
                    reason="not-supplied-or-not-regular-file",
                )
            )
            continue

        try:
            identity = file_identity(source)
        except IdentityError as exc:
            rejected.append(_rejected(asset, f"unreadable-file:{exc}"))
            continue

        # A declared expected size/hash that does not match is a substitution.
        if asset.expected_size is not None and identity.size != asset.expected_size:
            rejected.append(
                _rejected(asset, f"size-mismatch:expected={asset.expected_size} actual={identity.size}")
            )
            continue
        if asset.expected_sha256 is not None and identity.digest != asset.expected_sha256:
            rejected.append(_rejected(asset, "sha256-mismatch"))
            continue

        try:
            format_meta = sniff_format(source, asset.runtime_format)
        except IdentityError as exc:
            rejected.append(_rejected(asset, f"runtime-format:{exc}"))
            continue

        # Architecture gate for native binaries.
        if asset.kind == "runtime.binary" and format_meta.get("format") == "elf":
            host_arch = spec.host.get("arch", "")
            if not elf_machine_matches(format_meta, host_arch):
                rejected.append(
                    _rejected(
                        asset,
                        f"architecture-incompatible:host={host_arch} elf-machine={format_meta.get('machine')}",
                    )
                )
                continue

        prior = logicals_with_digest.get(asset.logical_id)
        if prior is not None and prior != identity.digest:
            rejected.append(_rejected(asset, "conflicting-digest-for-logical-id"))
            continue
        logicals_with_digest[asset.logical_id] = identity.digest

        # Reject two distinct assets claiming the same canonical install path with
        # different content (an installer could otherwise be ambiguous).
        if asset.install_path in install_path_digests and install_path_digests[asset.install_path] != identity.digest:
            rejected.append(_rejected(asset, f"conflicting-install-path:{asset.install_path}"))
            continue
        install_path_digests[asset.install_path] = identity.digest

        admitted.append(
            AdmittedAsset(
                logical=asset.logical_id,
                kind=asset.kind,
                engine=asset.engine,
                platform=dict(asset.platform),
                runtime_format=asset.runtime_format,
                source=asset.source,
                license_id=asset.license_id,
                redistribution=asset.redistribution,
                install_path=asset.install_path,
                digest=identity.digest,
                size=identity.size,
                capabilities=asset.capabilities,
                format_meta=format_meta,
            )
        )

    generated_at = _utc_now() if include_timestamp else None
    # Rejected required assets are treated as failed/missing for state purposes.
    for item in rejected:
        missing.append(
            MissingAsset(
                logical=item["logical"],
                kind=item["kind"],
                engine=item["engine"],
                required=item["required"],
                reason="rejected:" + item["reason"],
            )
        )

    manifest = render_manifest(
        bundle_id=spec.bundle_id,
        bundle_version=spec.bundle_version,
        host=spec.host,
        admitted=admitted,
        missing=missing,
        generated_at_utc=generated_at,
    )
    receipt = build_receipt(manifest)
    return PackResult(manifest=manifest, receipt=receipt, admitted=admitted, missing=missing, rejected=rejected)


def _rejected(asset: Any, reason: str) -> dict[str, Any]:
    return {
        "logical": asset.logical_id,
        "kind": asset.kind,
        "engine": asset.engine,
        "required": asset.required,
        "reason": reason,
    }
