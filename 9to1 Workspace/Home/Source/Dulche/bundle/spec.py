"""Input specification for a bundled Dulche art.

A spec describes what real assets the integrator intends to bundle. Every asset
names a logical id, kind, engine, platform, runtime format, source, licence and
(critically) the *relative path of the already-supplied file* under the assets
root. The packager never searches for or downloads assets: if a file is not
supplied, the asset is reported missing and the bundle is degraded/BLOCKED.

Pins: ``expectedSha256`` / ``expectedSize`` are optional *supplier* claims.
The packager refuses to admit an *invented* pin (all-zero hash) and refuses to
use a pin as a substitute for actually hashing the supplied file.
"""
from __future__ import annotations

import json
import pathlib
from dataclasses import dataclass, field
from typing import Any

from .identity import IdentityError, validate_sha256
from .licence import validate_license, validate_redistribution, validate_source_reference

ZERO_SHA256 = "0" * 64

ALLOWED_KINDS = frozenset({"model.gguf", "model.safetensors", "runtime.binary", "runtime.bundle"})
ALLOWED_ENGINES = frozenset({"llamacpp", "strata"})
ALLOWED_FORMATS = frozenset({"gguf", "safetensors", "elf", "opaque"})
REQUIRED_ASSET_FIELDS = ("logical", "kind", "engine", "platform", "runtimeFormat", "source", "license", "path")


@dataclass
class AssetSpec:
    logical: str
    kind: str
    engine: str
    platform: dict
    runtime_format: str
    source: str
    license_id: str
    path: str
    install_path: str
    capabilities: tuple[str, ...] = ()
    expected_sha256: str | None = None
    expected_size: int | None = None
    redistribution: str = "review-required"
    required: bool = True

    @property
    def logical_id(self) -> str:
        return self.logical


def default_install_path(logical: str, kind: str, engine: str) -> str:
    kind = kind.strip().lower()
    engine = engine.strip().lower()
    if kind == "model.gguf":
        return f"assets/models/{logical}.gguf"
    if kind == "model.safetensors":
        return f"assets/models/{logical}.safetensors"
    if kind == "runtime.binary":
        return f"assets/engines/{engine}/{logical}"
    if kind == "runtime.bundle":
        return f"assets/engines/{engine}/{logical}"
    return f"assets/{logical}"


@dataclass
class BundleSpec:
    bundle_id: str
    bundle_version: str
    host: dict
    assets: list[AssetSpec] = field(default_factory=list)

    @property
    def logical_ids(self) -> list[str]:
        return [asset.logical for asset in self.assets]


def _validate_platform(platform: Any) -> dict:
    if not isinstance(platform, dict):
        raise IdentityError("asset 'platform' must be an object")
    os_name = platform.get("os")
    arch = platform.get("arch")
    if not isinstance(os_name, str) or not os_name or not isinstance(arch, str) or not arch:
        raise IdentityError("asset 'platform' must include non-empty 'os' and 'arch'")
    return {"os": os_name.strip().lower(), "arch": arch.strip().lower()}


def _parse_asset(raw: Any) -> AssetSpec:
    if not isinstance(raw, dict):
        raise IdentityError("each asset must be an object")
    missing = [key for key in REQUIRED_ASSET_FIELDS if key not in raw]
    if missing:
        raise IdentityError(f"asset missing required fields: {', '.join(missing)}")
    kind = str(raw["kind"]).strip().lower()
    if kind not in ALLOWED_KINDS:
        raise IdentityError(f"unknown asset kind: {kind}")
    engine = str(raw["engine"]).strip().lower()
    if engine not in ALLOWED_ENGINES:
        raise IdentityError(f"unknown engine: {engine}")
    fmt = str(raw["runtimeFormat"]).strip().lower()
    if fmt not in ALLOWED_FORMATS:
        raise IdentityError(f"unknown runtime format: {fmt}")
    logical = str(raw["logical"]).strip()
    if not logical or any(char.isspace() for char in logical):
        raise IdentityError(f"invalid logical asset id: {raw['logical']!r}")
    rel_path = str(raw["path"]).strip()
    if not rel_path:
        raise IdentityError(f"asset {logical} has an empty path")
    install_path = str(raw.get("installPath", "")).strip()
    if not install_path:
        install_path = default_install_path(logical, kind, engine)
    if install_path.startswith("/") or install_path.startswith("\\") or ".." in pathlib.PurePosixPath(install_path).parts:
        raise IdentityError(f"asset {logical} has an unsafe installPath: {install_path}")
    source = validate_source_reference(raw["source"])
    license_id = validate_license(raw["license"])
    platform = _validate_platform(raw["platform"])
    redistribution = validate_redistribution(raw.get("redistribution", "review-required"))

    expected_sha256: str | None = None
    if "expectedSha256" in raw and raw["expectedSha256"] is not None:
        candidate = validate_sha256(raw["expectedSha256"])
        if candidate == ZERO_SHA256:
            raise IdentityError(
                f"asset {logical} declares the all-zero placeholder SHA-256; a real pin "
                "must be supplied or the field omitted (no invented pins)"
            )
        expected_sha256 = candidate
    expected_size: int | None = None
    if "expectedSize" in raw and raw["expectedSize"] is not None:
        try:
            expected_size = int(raw["expectedSize"])
        except (TypeError, ValueError) as exc:
            raise IdentityError(f"asset {logical} has an invalid expectedSize") from exc
        if expected_size <= 0:
            raise IdentityError(f"asset {logical} has a non-positive expectedSize")

    capabilities = raw.get("capabilities", [])
    if isinstance(capabilities, str):
        capabilities = [capabilities]
    if not isinstance(capabilities, list):
        raise IdentityError(f"asset {logical} capabilities must be a list")
    cleaned: list[str] = []
    for cap in capabilities:
        value = str(cap).strip().lower()
        if not value:
            continue
        if value not in cleaned:
            cleaned.append(value)

    required = bool(raw.get("required", True))
    return AssetSpec(
        logical=logical,
        kind=kind,
        engine=engine,
        platform=platform,
        runtime_format=fmt,
        source=source,
        license_id=license_id,
        path=rel_path,
        install_path=install_path,
        capabilities=tuple(cleaned),
        expected_sha256=expected_sha256,
        expected_size=expected_size,
        redistribution=redistribution,
        required=required,
    )


def load_spec(path: pathlib.Path | str) -> BundleSpec:
    raw = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    return parse_spec(raw)


def parse_spec(raw: Any) -> BundleSpec:
    if not isinstance(raw, dict):
        raise IdentityError("bundle spec must be a JSON object")
    bundle_id = str(raw.get("bundleId", "")).strip()
    bundle_version = str(raw.get("bundleVersion", "")).strip()
    if not bundle_id or not bundle_version:
        raise IdentityError("bundle spec requires non-empty 'bundleId' and 'bundleVersion'")
    host = raw.get("host")
    if not isinstance(host, dict) or not host.get("os") or not host.get("arch"):
        raise IdentityError("bundle spec requires 'host' with 'os' and 'arch'")
    assets_raw = raw.get("assets")
    if not isinstance(assets_raw, list) or not assets_raw:
        raise IdentityError("bundle spec requires a non-empty 'assets' list")
    host = {"os": str(host["os"]).strip().lower(), "arch": str(host["arch"]).strip().lower()}
    assets = [_parse_asset(item) for item in assets_raw]
    seen: set[str] = set()
    for asset in assets:
        if asset.logical_id in seen:
            raise IdentityError(f"duplicate logical asset id in spec: {asset.logical_id}")
        seen.add(asset.logical_id)
    return BundleSpec(bundle_id=bundle_id, bundle_version=bundle_version, host=host, assets=assets)
