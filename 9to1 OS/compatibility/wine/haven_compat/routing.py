"""Eligibility-first package routing shared by the daemon and development CLI."""
from __future__ import annotations

import hashlib
import json
import os
import stat
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path

from .packages import CompatibilityPackage
from .registry import _fsync_directory


class RoutingError(ValueError):
    pass


@dataclass(frozen=True)
class CompatibilityBackend:
    id: str
    display_name: str
    formats: tuple[str, ...]
    architectures: tuple[str, ...]
    available: bool
    priority: int
    unavailable_reason: str | None = None

    def as_dict(self) -> dict:
        return asdict(self)


@dataclass(frozen=True)
class CompatibilityPolicy:
    enabled: bool = True
    allowed_backends: frozenset[str] | None = None
    allowed_identities: frozenset[str] | None = None
    allow_backend_preferences: bool = True

    def check(self, identity: str, backend: str | None = None) -> str | None:
        if not self.enabled:
            return "Foreign application compatibility is managed and disabled by administrator policy"
        if self.allowed_identities is not None and identity not in self.allowed_identities:
            return "Application identity is not allowed by administrator policy"
        if backend is not None and self.allowed_backends is not None and backend not in self.allowed_backends:
            return "Framework is not allowed by administrator policy"
        return None


class AdministratorPolicySource:
    """No client-facing mutation API. Every operation re-reads the system policy."""
    def __init__(self, path: Path = Path("/etc/9to1/compatibility-policy.json")):
        self.path = path

    def read(self) -> CompatibilityPolicy:
        try:
            info = self.path.lstat()
        except FileNotFoundError:
            return CompatibilityPolicy()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise RoutingError("Administrator compatibility policy must be a root-owned regular file without group/other write access")
        try:
            fd = os.open(self.path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
            with os.fdopen(fd, "r", encoding="utf-8") as handle:
                opened = os.fstat(handle.fileno())
                if (info.st_dev, info.st_ino) != (opened.st_dev, opened.st_ino) or opened.st_size > 1024 * 1024:
                    raise RoutingError("Administrator compatibility policy changed or exceeds the size limit")
                record = json.load(handle)
            if not isinstance(record, dict) or record.get("schemaVersion") != 1 or set(record) - {"schemaVersion", "enabled", "allowedBackends", "allowedIdentities", "allowBackendPreferences"}:
                raise RoutingError("Unsupported administrator compatibility policy schema")
            enabled, preferences = record.get("enabled", True), record.get("allowBackendPreferences", True)
            if not isinstance(enabled, bool) or not isinstance(preferences, bool):
                raise RoutingError("Administrator compatibility policy flags must be boolean")
            def names(key):
                value = record.get(key)
                if value is None:
                    return None
                if not isinstance(value, list) or any(not isinstance(item, str) or not item or len(item) > 512 for item in value):
                    raise RoutingError("Administrator compatibility policy identity lists are invalid")
                return frozenset(value)
            return CompatibilityPolicy(enabled, names("allowedBackends"), names("allowedIdentities"), preferences)
        except (OSError, ValueError) as exc:
            if isinstance(exc, RoutingError):
                raise
            raise RoutingError("Administrator compatibility policy cannot be read; execution is denied") from exc


class BackendPreferences:
    """Preferences are keyed by stable identity, never the installer filename."""
    def __init__(self, root: Path):
        self.root = root

    def _path(self, identity: str) -> Path:
        if not isinstance(identity, str) or not identity or len(identity) > 512:
            raise RoutingError("A bounded stable application/package identity is required")
        return self.root / (hashlib.sha256(identity.encode()).hexdigest() + ".json")

    def _validate_root(self):
        if self.root.is_symlink() or not self.root.is_dir():
            raise RoutingError("Framework preference directory must be a real directory")

    def get(self, identity: str) -> str | None:
        path = self._path(identity)
        if not self.root.exists():
            return None
        self._validate_root()
        if path.is_symlink():
            raise RoutingError("Framework preference must not be a symlink")
        if not path.exists():
            return None
        try:
            fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
            with os.fdopen(fd, "r", encoding="utf-8") as handle:
                if not stat.S_ISREG(os.fstat(handle.fileno()).st_mode) or os.fstat(handle.fileno()).st_size > 4096:
                    raise RoutingError("Invalid framework preference file")
                record = json.load(handle)
            if not isinstance(record, dict) or set(record) != {"schemaVersion", "identity", "backend"} or record["schemaVersion"] != 1 or record["identity"] != identity or not isinstance(record["backend"], str):
                raise RoutingError("Framework preference identity/schema mismatch")
            return record["backend"]
        except (OSError, ValueError) as exc:
            raise RoutingError("Framework preference cannot be read safely") from exc

    def set(self, identity: str, backend: str | None):
        path = self._path(identity)
        if self.root.is_symlink():
            raise RoutingError("Framework preference directory must not be a symlink")
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        self._validate_root()
        os.chmod(self.root, 0o700)
        if backend is None:
            if path.exists() or path.is_symlink():
                path.unlink()
                _fsync_directory(self.root)
            return
        fd, temporary = tempfile.mkstemp(dir=self.root, prefix=".preference-", suffix=".tmp")
        try:
            os.fchmod(fd, 0o600)
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                json.dump({"schemaVersion": 1, "identity": identity, "backend": backend}, handle)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, path)
            _fsync_directory(self.root)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)


def route_package(package: CompatibilityPackage, backends: tuple[CompatibilityBackend, ...],
                  policy: CompatibilityPolicy, preferred: str | None = None) -> dict:
    denied = policy.check(package.identity)
    if denied:
        return {"status": "policy-denied", "reason": denied, "package": package.as_dict(), "eligibleBackends": [], "selectedBackend": None}
    eligible, excluded = [], []
    for backend in sorted(backends, key=lambda value: (-value.priority, value.id)):
        reason = policy.check(package.identity, backend.id)
        if reason is None and package.format not in backend.formats:
            reason = "Package format is not supported by this framework"
        if reason is None and (not package.architectures and package.format == "windows-msi"):
            reason = "Windows Installer architecture has not been resolved"
        if reason is None and package.architectures and not set(package.architectures).intersection(backend.architectures):
            reason = "Package architecture/ABI is not supported by this framework"
        if reason is None and not backend.available:
            reason = backend.unavailable_reason or "Framework is unavailable"
        if reason:
            excluded.append({"backend": backend.id, "reason": reason})
        else:
            eligible.append(backend.id)
    selected = preferred if preferred in eligible else eligible[0] if eligible and preferred is None else None
    status = "no-eligible-backend" if not eligible else "preferred-backend-unavailable" if preferred and preferred not in eligible else "ready"
    # Inspection does not establish signing/trust and must never trigger execution.
    if selected and package.trust != "verified":
        status, selected = "requires-package-trust", None
    return {"status": status, "package": package.as_dict(), "eligibleBackends": eligible,
            "excludedBackends": excluded, "preferredBackend": preferred,
            "selectedBackend": selected, "requiresInstallationReview": True}
