from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import PurePosixPath
from typing import Any


class ManifestError(ValueError):
    pass


_ALLOWED_BACKENDS = {"wine", "winboat"}
_ALLOWED_NETWORK = {"none", "internet", "lan"}
_ALLOWED_GPU = {"none", "render"}
_ALLOWED_MOUNT_MODES = {"ro", "rw"}
_ALLOWED_APP_FIELDS = {
    "id",
    "displayName",
    "backend",
    "runtime",
    "entrypoint",
    "network",
    "clipboard",
    "audioOutput",
    "microphone",
    "gpu",
    "mounts",
}
_ALLOWED_MOUNT_FIELDS = {"source", "target", "mode"}
_SHARE_ROOT = PurePosixPath("/mnt/haven-share")


def _is_safe_identifier(value: str) -> bool:
    return bool(value) and all(
        c in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-" for c in value
    ) and value not in {".", ".."}


def _optional_string(value: dict[str, Any], key: str, default: str) -> str:
    raw = value.get(key, default)
    if not isinstance(raw, str):
        raise ManifestError(f"{key} must be a string")
    return raw.strip()


def _optional_bool(value: dict[str, Any], key: str, default: bool = False) -> bool:
    raw = value.get(key, default)
    if not isinstance(raw, bool):
        raise ManifestError(f"{key} must be boolean")
    return raw


@dataclass(frozen=True)
class MountGrant:
    source: str
    target: str
    mode: str = "ro"

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "MountGrant":
        if not isinstance(value, dict):
            raise ManifestError("each mount grant must be an object")
        unknown = set(value) - _ALLOWED_MOUNT_FIELDS
        if unknown:
            raise ManifestError(f"unexpected mount fields: {', '.join(sorted(unknown))}")

        source = _optional_string(value, "source", "")
        target = _optional_string(value, "target", "")
        mode = _optional_string(value, "mode", "ro").lower()
        if "\x00" in source or "\x00" in target:
            raise ManifestError("mount paths must not contain NUL bytes")
        if not source.startswith("/"):
            raise ManifestError("mount source must be an absolute host path")
        if not target.startswith("/"):
            raise ManifestError("mount target must be an absolute sandbox path")
        if mode not in _ALLOWED_MOUNT_MODES:
            raise ManifestError(f"unsupported mount mode: {mode}")
        if source in {"/", "/home", "/root", "/tmp"}:
            raise ManifestError("broad host filesystem mounts are forbidden")

        target_path = PurePosixPath(target)
        if ".." in target_path.parts:
            raise ManifestError("mount target traversal is forbidden")
        if target_path == _SHARE_ROOT or _SHARE_ROOT not in target_path.parents:
            raise ManifestError("mount targets must be beneath /mnt/haven-share/<name>")
        return cls(source=source, target=target, mode=mode)

    def to_dict(self) -> dict[str, Any]:
        return {
            "source": self.source,
            "target": self.target,
            "mode": self.mode,
        }


@dataclass(frozen=True)
class AppManifest:
    app_id: str
    backend: str
    runtime: str
    entrypoint: str
    display_name: str | None = None
    network: str = "none"
    clipboard: bool = False
    audio_output: bool = False
    microphone: bool = False
    gpu: str = "none"
    mounts: tuple[MountGrant, ...] = field(default_factory=tuple)

    @classmethod
    def from_dict(cls, value: dict[str, Any]) -> "AppManifest":
        if not isinstance(value, dict):
            raise ManifestError("manifest must be an object")
        unknown = set(value) - _ALLOWED_APP_FIELDS
        if unknown:
            raise ManifestError(f"unexpected manifest fields: {', '.join(sorted(unknown))}")

        app_id = _optional_string(value, "id", "")
        backend = _optional_string(value, "backend", "wine").lower()
        runtime = _optional_string(value, "runtime", "")
        entrypoint = _optional_string(value, "entrypoint", "")

        display_name_raw = value.get("displayName")
        if display_name_raw is not None and not isinstance(display_name_raw, str):
            raise ManifestError("displayName must be a string")
        display_name = display_name_raw.strip() if isinstance(display_name_raw, str) else None
        display_name = display_name or None

        network = _optional_string(value, "network", "none").lower()
        gpu = _optional_string(value, "gpu", "none").lower()
        clipboard = _optional_bool(value, "clipboard")
        audio_output = _optional_bool(value, "audioOutput")
        microphone = _optional_bool(value, "microphone")

        if not _is_safe_identifier(app_id):
            raise ManifestError("id must be a simple non-traversing identifier")
        if backend not in _ALLOWED_BACKENDS:
            raise ManifestError(f"unsupported backend: {backend}")
        if not _is_safe_identifier(runtime):
            raise ManifestError("runtime must be a simple non-traversing identifier")
        if not entrypoint:
            raise ManifestError("entrypoint is required")
        if "\x00" in entrypoint:
            raise ManifestError("entrypoint must not contain NUL bytes")
        if display_name is not None:
            if len(display_name) > 120 or any(ord(character) < 32 for character in display_name):
                raise ManifestError("displayName must be at most 120 printable characters")
        if network not in _ALLOWED_NETWORK:
            raise ManifestError(f"unsupported network policy: {network}")
        if gpu not in _ALLOWED_GPU:
            raise ManifestError(f"unsupported gpu policy: {gpu}")

        mounts_raw = value.get("mounts", [])
        if not isinstance(mounts_raw, list):
            raise ManifestError("mounts must be a list")
        mounts = tuple(MountGrant.from_dict(item) for item in mounts_raw)
        targets = [mount.target for mount in mounts]
        if len(targets) != len(set(targets)):
            raise ManifestError("mount targets must be unique")

        return cls(
            app_id=app_id,
            backend=backend,
            runtime=runtime,
            entrypoint=entrypoint,
            display_name=display_name,
            network=network,
            clipboard=clipboard,
            audio_output=audio_output,
            microphone=microphone,
            gpu=gpu,
            mounts=mounts,
        )

    def to_dict(self) -> dict[str, Any]:
        value: dict[str, Any] = {
            "id": self.app_id,
            "backend": self.backend,
            "runtime": self.runtime,
            "entrypoint": self.entrypoint,
            "network": self.network,
            "clipboard": self.clipboard,
            "audioOutput": self.audio_output,
            "microphone": self.microphone,
            "gpu": self.gpu,
            "mounts": [mount.to_dict() for mount in self.mounts],
        }
        if self.display_name is not None:
            value["displayName"] = self.display_name
        return value
