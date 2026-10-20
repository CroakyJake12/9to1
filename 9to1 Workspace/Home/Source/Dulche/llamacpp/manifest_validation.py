"""Model-store manifest validation for Haven's local llama.cpp store.

This is a new, standalone validation utility for the model-store manifest JSON
format (the same shape produced by ``modelctl.py`` and sketched in
``model-manifest.example.json``). It deliberately does NOT modify ``broker.py``
or ``modelctl.py``; it exists so packaging/asset-admission callers and acceptance
can validate a manifest independently of the store manager.

Key honesty rules:
- A manifest whose ``sha256`` is the all-zero placeholder (the value used in the
  example template) or any zero/short digest is NOT an admissible record.
- The blob path must equal the canonical content address derived from the digest.
- Size must be positive; GGUF versions must be a non-empty subset of {2, 3}.
- License, source, capabilities and redistribution policy must be non-empty and
  well-formed.
"""
from __future__ import annotations

import pathlib
import re
from typing import Any

MODEL_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
CAPABILITY_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
HEX_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
ZERO_SHA256 = "0" * 64
SUPPORTED_GGUF_VERSIONS = frozenset({2, 3})
REDISTRIBUTION_POLICIES = frozenset({"review-required", "allowed", "prohibited", "unknown"})
REQUIRED_FIELDS = ("id", "displayName", "sha256", "size", "blob", "license", "source", "ggufVersions")


class ManifestValidationError(ValueError):
    pass


def is_zero_sha256(digest: str) -> bool:
    return str(digest).lower() == ZERO_SHA256


def _canonical_blob_relative(digest: str) -> str:
    return str(pathlib.PurePosixPath(digest[:2]) / f"{digest}.gguf")


def validate_model_manifest(manifest: Any, *, filename_stem: str | None = None) -> dict[str, Any]:
    if not isinstance(manifest, dict):
        raise ManifestValidationError("manifest must be a JSON object")
    missing = [key for key in REQUIRED_FIELDS if key not in manifest]
    if missing:
        raise ManifestValidationError(f"manifest missing required fields: {', '.join(missing)}")

    model_id = str(manifest["id"]).strip()
    if not MODEL_ID_RE.fullmatch(model_id):
        raise ManifestValidationError("id must use only letters, digits, '.', '_' or '-' and be <=128 chars")
    if filename_stem is not None and filename_stem != model_id:
        raise ManifestValidationError(f"filename stem {filename_stem!r} does not match id {model_id!r}")

    digest = str(manifest["sha256"]).lower()
    if not HEX_SHA256_RE.fullmatch(digest):
        raise ManifestValidationError("sha256 must be a 64-character hex digest")
    if is_zero_sha256(digest):
        raise ManifestValidationError("sha256 is the all-zero placeholder; a real digest is required (no invented pin)")

    expected_blob = _canonical_blob_relative(digest)
    if str(manifest["blob"]) != expected_blob:
        raise ManifestValidationError(f"blob path must be the canonical content address {expected_blob}")

    try:
        size = int(manifest["size"])
    except (TypeError, ValueError) as exc:
        raise ManifestValidationError("size must be an integer") from exc
    if size <= 0:
        raise ManifestValidationError("size must be positive")

    versions = manifest["ggufVersions"]
    if not isinstance(versions, list) or not versions:
        raise ManifestValidationError("ggufVersions must be a non-empty list")
    parsed_versions = set()
    for version in versions:
        try:
            parsed_versions.add(int(version))
        except (TypeError, ValueError) as exc:
            raise ManifestValidationError("ggufVersions must be integers") from exc
    if not parsed_versions.issubset(SUPPORTED_GGUF_VERSIONS):
        raise ManifestValidationError(f"ggufVersions must be a subset of {sorted(SUPPORTED_GGUF_VERSIONS)}")

    license_id = str(manifest.get("license", "")).strip()
    source = str(manifest.get("source", "")).strip()
    if not license_id or not source:
        raise ManifestValidationError("license and source are required and must be non-empty")

    capabilities = manifest.get("capabilities", ["chat"])
    if isinstance(capabilities, str):
        capabilities = [capabilities]
    if not isinstance(capabilities, list):
        raise ManifestValidationError("capabilities must be a list")
    for cap in capabilities:
        value = str(cap).strip().lower()
        if not CAPABILITY_RE.fullmatch(value):
            raise ManifestValidationError(f"invalid capability: {cap}")

    redistribution = str(manifest.get("redistribution", "review-required")).strip().lower()
    if redistribution not in REDISTRIBUTION_POLICIES:
        raise ManifestValidationError(f"redistribution must be one of {sorted(REDISTRIBUTION_POLICIES)}")

    return manifest


def validate_model_manifest_file(path: pathlib.Path | str) -> dict[str, Any]:
    import json

    manifest_path = pathlib.Path(path)
    raw = json.loads(manifest_path.read_text(encoding="utf-8"))
    return validate_model_manifest(raw, filename_stem=manifest_path.stem)
