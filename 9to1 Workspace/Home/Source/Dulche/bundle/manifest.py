"""Versioned bundle manifest model.

The manifest is the honest, durable record of what a bundled Dulche runtime
contains: engine and host facts, every admitted asset with its immutable
identity (SHA-256, size, format, licence, source, platform, canonical install
path), and anything that is missing/degraded.
"""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any

from . import MANIFEST_SCHEMA_VERSION, STATE_DEGRADED, STATE_INVALID, STATE_READY
from .identity import IdentityError, validate_sha256
from .licence import validate_license, validate_redistribution

PURPOSE = "bundled-dulche-runtime-model-assets"
ADMISSION_POLICY = "explicit-supplied-files"


@dataclass
class AdmittedAsset:
    logical: str
    kind: str
    engine: str
    platform: dict
    runtime_format: str
    source: str
    license_id: str
    redistribution: str
    install_path: str
    digest: str
    size: int
    capabilities: tuple[str, ...] = ()
    format_meta: dict = field(default_factory=dict)

    def to_dict(self) -> dict:
        return {
            "logical": self.logical,
            "kind": self.kind,
            "engine": self.engine,
            "platform": dict(self.platform),
            "runtimeFormat": self.runtime_format,
            "source": self.source,
            "license": self.license_id,
            "redistribution": self.redistribution,
            "installPath": self.install_path,
            "sha256": self.digest,
            "size": self.size,
            "capabilities": list(self.capabilities),
            "format": dict(self.format_meta),
            "admitted": True,
            "state": "present",
        }


@dataclass
class MissingAsset:
    logical: str
    kind: str
    engine: str
    required: bool
    reason: str = "not-supplied"

    def to_dict(self) -> dict:
        return {
            "logical": self.logical,
            "kind": self.kind,
            "engine": self.engine,
            "required": self.required,
            "reason": self.reason,
        }


def _state_for(admitted: list[AdmittedAsset], missing: list[MissingAsset]) -> str:
    if any(asset.required for asset in missing):
        return STATE_INVALID
    if missing:
        return STATE_DEGRADED
    if not admitted:
        return STATE_INVALID
    return STATE_READY


def _engine_state(assets: list[AdmittedAsset], missing: list[MissingAsset]) -> dict[str, dict]:
    engines: dict[str, dict[str, Any]] = {}
    for asset in assets:
        entry = engines.setdefault(asset.engine, {"present": False, "required": False})
        entry["present"] = True
    for miss in missing:
        entry = engines.setdefault(miss.engine, {"present": False, "required": False})
        if miss.required:
            entry["required"] = True
    return engines


def render_manifest(
    *,
    bundle_id: str,
    bundle_version: str,
    host: dict,
    admitted: list[AdmittedAsset],
    missing: list[MissingAsset],
    generated_at_utc: str | None = None,
    scheme_version: int = MANIFEST_SCHEMA_VERSION,
) -> dict[str, Any]:
    admitted_sorted = sorted(admitted, key=lambda a: a.logical)
    missing_sorted = sorted(missing, key=lambda m: m.logical)
    body: dict[str, Any] = {
        "manifestSchemaVersion": scheme_version,
        "bundleId": bundle_id,
        "bundleVersion": bundle_version,
        "purpose": PURPOSE,
        "host": {"os": host["os"], "arch": host["arch"]},
        "state": _state_for(admitted_sorted, missing_sorted),
        "admission": {
            "policy": ADMISSION_POLICY,
            "zeroHashAdmitted": False,
            "inventedPins": 0,
            "networkUsed": False,
        },
        "assets": [asset.to_dict() for asset in admitted_sorted],
        "missingAssets": [miss.to_dict() for miss in missing_sorted],
        "engineState": _engine_state(admitted_sorted, missing_sorted),
    }
    if generated_at_utc:
        body["generatedAtUtc"] = generated_at_utc
    return body


def canonical_dumps(value: dict[str, Any]) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def parse_manifest(raw: Any, *, allow_invalid: bool = False) -> dict[str, Any]:
    if not isinstance(raw, dict):
        raise IdentityError("bundle manifest must be a JSON object")
    if raw.get("manifestSchemaVersion") != MANIFEST_SCHEMA_VERSION:
        raise IdentityError(f"unsupported manifest schema version: {raw.get('manifestSchemaVersion')}")
    for key in ("bundleId", "bundleVersion", "purpose", "host", "state", "assets", "missingAssets"):
        if key not in raw:
            raise IdentityError(f"manifest missing required field: {key}")
    state = raw["state"]
    if state not in (STATE_READY, STATE_DEGRADED, STATE_INVALID):
        raise IdentityError(f"manifest has invalid state: {state}")
    if not allow_invalid and state == STATE_INVALID:
        raise IdentityError("manifest is invalid; refusing to treat it as an admitted bundle")
    host = raw["host"]
    if not isinstance(host, dict) or not host.get("os") or not host.get("arch"):
        raise IdentityError("manifest host must include os/arch")
    assets = raw["assets"]
    if not isinstance(assets, list):
        raise IdentityError("manifest assets must be a list")
    for asset in assets:
        _validate_manifest_asset(asset)
    missing = raw["missingAssets"]
    if not isinstance(missing, list):
        raise IdentityError("manifest missingAssets must be a list")
    return raw


def _validate_manifest_asset(asset: Any) -> None:
    if not isinstance(asset, dict):
        raise IdentityError("manifest asset must be an object")
    for key in ("logical", "kind", "engine", "platform", "runtimeFormat", "source",
                "license", "redistribution", "installPath", "sha256", "size"):
        if key not in asset:
            raise IdentityError(f"manifest asset missing field: {key}")
    validate_sha256(asset["sha256"])
    validate_license(asset["license"])
    validate_redistribution(asset["redistribution"])
    if not isinstance(asset["platform"], dict) or not asset["platform"].get("os") or not asset["platform"].get("arch"):
        raise IdentityError("manifest asset platform must include os/arch")
    try:
        size = int(asset["size"])
    except (TypeError, ValueError) as exc:
        raise IdentityError("manifest asset has invalid size") from exc
    if size <= 0:
        raise IdentityError("manifest asset has non-positive size")
