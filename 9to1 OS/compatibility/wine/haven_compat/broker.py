from __future__ import annotations

import json
import os
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

from .audit import audit_environment
from .lifecycle import LifecycleError, UnitStatus, UserSystemdSupervisor, unit_name
from .manifest import AppManifest
from .registry import AppRegistry, RegistryError


class CompatibilityError(RuntimeError):
    pass


@dataclass(frozen=True)
class LaunchPlan:
    backend: str
    argv: tuple[str, ...]
    env: dict[str, str]
    prefix_path: str


class CompatibilityBroker:
    """Policy-enforcing broker for optional Windows compatibility backends."""

    def __init__(
        self,
        state_root: Path | None = None,
        runtime_root: Path | None = None,
        supervisor: UserSystemdSupervisor | None = None,
        registry: AppRegistry | None = None,
    ):
        data_home = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
        self.state_root = state_root or data_home / "haven" / "compat" / "wine" / "apps"
        self.runtime_root = runtime_root or data_home / "haven" / "compat" / "wine" / "runtimes"
        self.supervisor = supervisor or UserSystemdSupervisor()
        self.registry = registry or AppRegistry(data_home / "haven" / "compat" / "registry")

    def capabilities(self) -> dict[str, Any]:
        return {
            "schemaVersion": 1,
            "providers": {
                "wine": {
                    "enabled": True,
                    "slice": 1,
                    "network": ["none"],
                    "display": ["wayland"],
                    "filesystem": ["none", "explicit-ro", "explicit-rw"],
                    "gpu": ["none", "render"],
                    "clipboard": False,
                    "audioOutput": False,
                    "microphone": False,
                    "lifecycle": ["launch", "status", "stop", "logs", "reset"],
                },
                "winboat": {
                    "enabled": False,
                    "reason": "VM/container provider remains deferred until its isolation boundary is runtime-proven",
                },
            },
        }

    def health(self) -> dict[str, Any]:
        return {
            "schemaVersion": 1,
            "audit": audit_environment(self.runtime_root),
            "supervisor": {
                "type": "systemd-user",
                "available": self.supervisor.available,
            },
        }

    def register_app(self, manifest: AppManifest) -> dict[str, Any]:
        try:
            self.registry.register(manifest)
        except RegistryError as exc:
            raise CompatibilityError(str(exc)) from exc
        return self._app_summary(manifest)

    def list_apps(self) -> list[dict[str, Any]]:
        try:
            manifests = self.registry.list()
        except RegistryError as exc:
            raise CompatibilityError(str(exc)) from exc
        return [self._app_summary(manifest) for manifest in manifests]

    def get_registered_app(self, app_id: str) -> AppManifest:
        try:
            return self.registry.get(app_id)
        except RegistryError as exc:
            raise CompatibilityError(str(exc)) from exc

    def unregister_app(self, app_id: str, delete_state: bool = False) -> dict[str, Any]:
        manifest = self.get_registered_app(app_id)
        current = self.status(manifest)
        if current.running:
            raise CompatibilityError("refusing to unregister a running compatibility application; stop it first")
        if delete_state:
            self.reset(manifest)
        try:
            self.registry.remove(app_id)
        except RegistryError as exc:
            raise CompatibilityError(str(exc)) from exc
        return {
            "id": app_id,
            "unregistered": True,
            "stateDeleted": delete_state,
        }

    def plan(self, manifest: AppManifest) -> LaunchPlan:
        if manifest.backend == "winboat":
            raise CompatibilityError("WinBoat provider is not enabled in compatibility slice 1")
        if manifest.backend != "wine":
            raise CompatibilityError(f"unsupported backend: {manifest.backend}")
        if manifest.network != "none":
            raise CompatibilityError("network permissions are not implemented in compatibility slice 1")
        if manifest.clipboard:
            raise CompatibilityError("clipboard permission is not implemented in compatibility slice 1")
        if manifest.audio_output or manifest.microphone:
            raise CompatibilityError("PipeWire media permissions are not implemented in compatibility slice 1")

        bwrap = shutil.which("bwrap")
        if not bwrap:
            raise CompatibilityError("bubblewrap is required; refusing unsandboxed Wine execution")

        runtime_root = self.runtime_root.resolve()
        runtime_dir = (runtime_root / manifest.runtime).resolve()
        if runtime_root not in runtime_dir.parents:
            raise CompatibilityError("Wine runtime escaped the managed runtime root")
        wine_bin = runtime_dir / "bin" / "wine"
        if not wine_bin.is_file():
            raise CompatibilityError(f"Wine runtime is unavailable: {wine_bin}")
        if not os.access(wine_bin, os.X_OK):
            raise CompatibilityError(f"Wine runtime is not executable: {wine_bin}")

        xdg_runtime_dir, wayland_display, wayland_socket = _wayland_socket()

        state_root = self.state_root.resolve()
        app_root = (state_root / manifest.app_id).resolve()
        if state_root not in app_root.parents:
            raise CompatibilityError("application state escaped the compatibility state root")
        prefix = app_root / "prefix"
        data = app_root / "data"
        prefix.mkdir(parents=True, exist_ok=True, mode=0o700)
        data.mkdir(parents=True, exist_ok=True, mode=0o700)

        argv: list[str] = [
            bwrap,
            "--die-with-parent",
            "--new-session",
            "--unshare-all",
            "--proc", "/proc",
            "--dev", "/dev",
            "--tmpfs", "/tmp",
            "--dir", xdg_runtime_dir,
            "--dir", "/mnt",
            "--dir", "/mnt/haven-share",
            "--ro-bind", str(runtime_dir), "/opt/haven-wine",
            "--bind", str(prefix), "/var/lib/haven-wine/prefix",
            "--bind", str(data), "/var/lib/haven-wine/data",
            "--ro-bind", wayland_socket, wayland_socket,
        ]

        for host_path in ("/usr", "/lib", "/lib64"):
            if Path(host_path).exists():
                argv.extend(["--ro-bind", host_path, host_path])

        for mount in manifest.mounts:
            source = self._validated_mount_source(Path(mount.source))
            flag = "--ro-bind" if mount.mode == "ro" else "--bind"
            argv.extend([flag, str(source), mount.target])

        if manifest.gpu == "render":
            render_nodes = tuple(_existing_render_nodes())
            if not render_nodes:
                raise CompatibilityError("GPU render permission requested but no render node is available")
            for render_node in render_nodes:
                argv.extend(["--dev-bind", render_node, render_node])

        argv.extend([
            "--setenv", "WINEPREFIX", "/var/lib/haven-wine/prefix",
            "--setenv", "HOME", "/var/lib/haven-wine/data",
            "--setenv", "XDG_RUNTIME_DIR", xdg_runtime_dir,
            "--setenv", "WAYLAND_DISPLAY", wayland_display,
            "/opt/haven-wine/bin/wine",
            manifest.entrypoint,
        ])

        env = {
            "PATH": "/usr/bin:/bin",
            "LANG": os.environ.get("LANG", "C.UTF-8"),
        }

        return LaunchPlan(
            backend="wine",
            argv=tuple(argv),
            env=env,
            prefix_path=str(prefix),
        )

    def launch(self, manifest: AppManifest) -> UnitStatus:
        plan = self.plan(manifest)
        try:
            return self.supervisor.start(manifest.app_id, plan.argv, plan.env)
        except LifecycleError as exc:
            raise CompatibilityError(str(exc)) from exc

    def launch_registered(self, app_id: str) -> UnitStatus:
        return self.launch(self.get_registered_app(app_id))

    def status(self, manifest: AppManifest) -> UnitStatus:
        try:
            return self.supervisor.status(manifest.app_id)
        except LifecycleError as exc:
            raise CompatibilityError(str(exc)) from exc

    def status_registered(self, app_id: str) -> UnitStatus:
        return self.status(self.get_registered_app(app_id))

    def stop(self, manifest: AppManifest) -> UnitStatus:
        try:
            return self.supervisor.stop(manifest.app_id)
        except LifecycleError as exc:
            raise CompatibilityError(str(exc)) from exc

    def stop_registered(self, app_id: str) -> UnitStatus:
        return self.stop(self.get_registered_app(app_id))

    def logs(self, manifest: AppManifest, lines: int = 200) -> str:
        try:
            return self.supervisor.logs(manifest.app_id, lines)
        except LifecycleError as exc:
            raise CompatibilityError(str(exc)) from exc

    def logs_registered(self, app_id: str, lines: int = 200) -> str:
        return self.logs(self.get_registered_app(app_id), lines)

    def reset(self, manifest: AppManifest) -> None:
        if manifest.backend != "wine":
            raise CompatibilityError("environment reset is only implemented for the Wine provider")
        try:
            current = self.supervisor.status(manifest.app_id)
        except LifecycleError as exc:
            raise CompatibilityError(f"cannot verify lifecycle state before reset: {exc}") from exc
        if current.running:
            raise CompatibilityError("refusing to reset a running compatibility application; stop it first")

        app_root = (self.state_root / manifest.app_id).resolve()
        if self.state_root.resolve() not in app_root.parents:
            raise CompatibilityError("refusing to reset outside compatibility state root")
        if app_root.exists():
            shutil.rmtree(app_root)

    def reset_registered(self, app_id: str) -> None:
        self.reset(self.get_registered_app(app_id))

    def lifecycle_unit(self, manifest: AppManifest) -> str:
        return unit_name(manifest.app_id)

    def _app_summary(self, manifest: AppManifest) -> dict[str, Any]:
        return {
            "id": manifest.app_id,
            "displayName": manifest.display_name or manifest.app_id,
            "backend": manifest.backend,
            "runtime": manifest.runtime,
            "entrypoint": manifest.entrypoint,
            "unit": unit_name(manifest.app_id),
            "permissions": {
                "network": manifest.network,
                "clipboard": manifest.clipboard,
                "audioOutput": manifest.audio_output,
                "microphone": manifest.microphone,
                "gpu": manifest.gpu,
                "mounts": [mount.to_dict() for mount in manifest.mounts],
            },
        }

    def _validated_mount_source(self, source: Path) -> Path:
        try:
            resolved = source.resolve(strict=True)
        except FileNotFoundError as exc:
            raise CompatibilityError(f"mount source does not exist: {source}") from exc

        home = Path.home().resolve()
        if resolved in {Path("/"), Path("/home"), home, Path("/tmp")}:
            raise CompatibilityError("refusing a broad host filesystem grant")

        forbidden_roots = tuple(
            Path(path).resolve()
            for path in ("/etc", "/proc", "/sys", "/dev", "/boot", "/usr", "/bin", "/sbin", "/lib", "/lib64", "/var", "/run", "/root")
            if Path(path).exists()
        )
        if any(resolved == root or root in resolved.parents for root in forbidden_roots):
            raise CompatibilityError(f"refusing sensitive host path grant: {resolved}")

        compatibility_root = self.state_root.resolve().parent
        runtime_root = self.runtime_root.resolve()
        if (
            resolved == compatibility_root
            or compatibility_root in resolved.parents
            or resolved == runtime_root
            or runtime_root in resolved.parents
        ):
            raise CompatibilityError("refusing access to compatibility backend state")
        return resolved


def load_manifest(path: Path) -> AppManifest:
    with path.open("r", encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise CompatibilityError("manifest root must be an object")
    return AppManifest.from_dict(value)


def _wayland_socket() -> tuple[str, str, str]:
    runtime_dir = os.environ.get("XDG_RUNTIME_DIR")
    display = os.environ.get("WAYLAND_DISPLAY")
    if not runtime_dir or not display:
        raise CompatibilityError("Wayland session is required in compatibility slice 1")
    socket = Path(runtime_dir) / display
    if not socket.exists():
        raise CompatibilityError(f"Wayland socket is unavailable: {socket}")
    return runtime_dir, display, str(socket)


def _existing_render_nodes() -> Iterable[str]:
    dri = Path("/dev/dri")
    if not dri.is_dir():
        return ()
    return tuple(str(path) for path in sorted(dri.glob("renderD*")) if path.is_char_device())
