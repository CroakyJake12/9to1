"""Deterministic receipts for bundled asset run.

A receipt is a short, byte-reproducible record that two identical inputs
produce identical receipt bytes. It contains no wall-clock timestamp; a separate
human-readable manifest may carry an optional timestamp for audit purposes.
"""
from __future__ import annotations

import hashlib
import json
from typing import Any

from . import PACKAGE_NAME, RECEIPT_SCHEMA_VERSION, VERSION
from .manifest import canonical_dumps


def manifest_sha256(manifest: dict[str, Any]) -> str:
    return hashlib.sha256(canonical_dumps(manifest)).hexdigest()


def build_receipt(manifest: dict[str, Any]) -> dict[str, Any]:
    assets = {}
    for asset in sorted(manifest.get("assets", []), key=lambda a: a["logical"]):
        assets[asset["logical"]] = {"sha256": asset["sha256"], "size": asset["size"]}
    engine_state = {engine: dict(s) for engine, s in sorted(manifest.get("engineState", {}).items())}
    missing_required = sum(1 for miss in manifest.get("missingAssets", []) if miss.get("required"))
    return {
        "receiptSchemaVersion": RECEIPT_SCHEMA_VERSION,
        "packer": PACKAGE_NAME,
        "packerVersion": VERSION,
        "bundleId": manifest["bundleId"],
        "bundleVersion": manifest["bundleVersion"],
        "manifestSha256": manifest_sha256(manifest),
        "state": manifest["state"],
        "assetCount": len(assets),
        "admittedAssets": assets,
        "missingRequiredCount": missing_required,
        "engineState": engine_state,
        "deterministic": True,
    }


def receipt_bytes(receipt: dict[str, Any]) -> bytes:
    return canonical_dumps(receipt)
