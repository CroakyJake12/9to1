from __future__ import annotations

import hashlib
import json
import os
import tempfile
from pathlib import Path
from typing import Any

from .manifest import AppManifest, ManifestError


class RegistryError(RuntimeError):
    pass


class AppRegistry:
    """Atomic per-user registry for HUI-visible compatibility applications."""

    def __init__(self, root: Path | None = None) -> None:
        data_home = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
        self.root = root or data_home / "haven" / "compat" / "registry"

    def register(self, manifest: AppManifest) -> AppManifest:
        self._ensure_root()
        record = {
            "schemaVersion": 1,
            "manifest": manifest.to_dict(),
        }
        target = self._record_path(manifest.app_id)

        fd, temporary_name = tempfile.mkstemp(
            prefix=f".{target.stem}.",
            suffix=".tmp",
            dir=self.root,
            text=True,
        )
        temporary = Path(temporary_name)
        try:
            os.fchmod(fd, 0o600)
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                json.dump(record, handle, indent=2, sort_keys=True)
                handle.write("\n")
                handle.flush()
                os.fsync(handle.fileno())
            fd = -1
            os.replace(temporary, target)
            _fsync_directory(self.root)
        except OSError as exc:
            if fd >= 0:
                try:
                    os.close(fd)
                except OSError:
                    pass
            try:
                temporary.unlink()
            except FileNotFoundError:
                pass
            raise RegistryError(f"cannot persist compatibility registry record: {manifest.app_id}") from exc
        except Exception:
            if fd >= 0:
                try:
                    os.close(fd)
                except OSError:
                    pass
            try:
                temporary.unlink()
            except FileNotFoundError:
                pass
            raise
        return manifest

    def get(self, app_id: str) -> AppManifest:
        path = self._record_path(app_id)
        if not path.is_file():
            raise RegistryError(f"compatibility application is not registered: {app_id}")
        return self._read_record(path, expected_app_id=app_id)

    def list(self) -> list[AppManifest]:
        if not self.root.exists():
            return []
        self._validate_root()
        manifests: list[AppManifest] = []
        for path in sorted(self.root.glob("*.json")):
            manifests.append(self._read_record(path))
        return sorted(manifests, key=lambda item: item.app_id)

    def remove(self, app_id: str) -> None:
        path = self._record_path(app_id)
        if not path.exists():
            return
        if path.is_dir():
            raise RegistryError(f"registry record is not a file: {path}")
        try:
            path.unlink()
            _fsync_directory(self.root)
        except OSError as exc:
            raise RegistryError(f"cannot remove compatibility registry record: {app_id}") from exc

    def _record_path(self, app_id: str) -> Path:
        digest = hashlib.sha256(app_id.encode("utf-8")).hexdigest()
        return self.root / f"{digest}.json"

    def _ensure_root(self) -> None:
        try:
            self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
            os.chmod(self.root, 0o700)
        except OSError as exc:
            raise RegistryError(f"cannot secure registry directory: {self.root}") from exc
        self._validate_root()

    def _validate_root(self) -> None:
        if self.root.is_symlink():
            raise RegistryError("compatibility registry directory must not be a symlink")
        if not self.root.is_dir():
            raise RegistryError(f"compatibility registry path is not a directory: {self.root}")

    def _read_record(self, path: Path, expected_app_id: str | None = None) -> AppManifest:
        if path.is_symlink():
            raise RegistryError(f"registry record must not be a symlink: {path}")
        try:
            with path.open("r", encoding="utf-8") as handle:
                record: Any = json.load(handle)
        except (OSError, json.JSONDecodeError) as exc:
            raise RegistryError(f"cannot read compatibility registry record: {path.name}") from exc

        if not isinstance(record, dict) or record.get("schemaVersion") != 1:
            raise RegistryError(f"unsupported compatibility registry record: {path.name}")
        raw_manifest = record.get("manifest")
        if not isinstance(raw_manifest, dict):
            raise RegistryError(f"registry manifest is invalid: {path.name}")
        try:
            manifest = AppManifest.from_dict(raw_manifest)
        except ManifestError as exc:
            raise RegistryError(f"registry manifest failed validation: {path.name}") from exc
        if expected_app_id is not None and manifest.app_id != expected_app_id:
            raise RegistryError("registry record identity mismatch")
        if self._record_path(manifest.app_id).name != path.name:
            raise RegistryError("registry record filename does not match its application identity")
        return manifest


def _fsync_directory(path: Path) -> None:
    """Durably commit a directory entry where the host supports directory fsync.

    Linux/Unix filesystems can fsync an opened directory after os.replace/unlink
    so the rename itself is crash-durable. Windows does not permit opening a
    directory through os.open this way; the record file itself has already been
    flushed before the atomic os.replace, so skip this POSIX-only strengthening
    there rather than turning successful registry writes into failures.
    """

    if os.name == "nt":
        return

    flags = os.O_RDONLY
    if hasattr(os, "O_DIRECTORY"):
        flags |= os.O_DIRECTORY
    descriptor = os.open(path, flags)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)
