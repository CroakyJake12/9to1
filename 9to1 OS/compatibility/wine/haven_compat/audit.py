from __future__ import annotations

import os
import platform
import shutil
import subprocess
from pathlib import Path
from typing import Any


def audit_environment(runtime_root: Path | None = None) -> dict[str, Any]:
    data_home = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local" / "share"))
    runtime_root = runtime_root or data_home / "haven" / "compat" / "wine" / "runtimes"

    commands = {
        "bwrap": shutil.which("bwrap"),
        "wine": shutil.which("wine"),
        "vulkaninfo": shutil.which("vulkaninfo"),
        "wpctl": shutil.which("wpctl"),
        "systemd-run": shutil.which("systemd-run"),
        "systemctl": shutil.which("systemctl"),
        "journalctl": shutil.which("journalctl"),
        "env": shutil.which("env"),
        "podman": shutil.which("podman"),
        "docker": shutil.which("docker"),
        "freerdp": _first_command("wlfreerdp3", "xfreerdp3", "wlfreerdp", "xfreerdp"),
    }

    runtime_dir = os.environ.get("XDG_RUNTIME_DIR")
    wayland_display = os.environ.get("WAYLAND_DISPLAY")
    wayland_socket = Path(runtime_dir) / wayland_display if runtime_dir and wayland_display else None
    render_nodes = _render_nodes()
    managed_runtimes = _managed_runtimes(runtime_root)
    systemd_user_reachable = _systemd_user_reachable(commands["systemctl"])

    facts: dict[str, Any] = {
        "schemaVersion": 1,
        "platform": {
            "system": platform.system(),
            "release": platform.release(),
            "machine": platform.machine(),
        },
        "commands": commands,
        "versions": {
            "bwrap": _command_version(commands["bwrap"], "--version"),
            "wine": _command_version(commands["wine"], "--version"),
            "systemd": _command_version(commands["systemctl"], "--version"),
            "podman": _command_version(commands["podman"], "--version"),
            "docker": _command_version(commands["docker"], "--version"),
            "freerdp": _command_version(commands["freerdp"], "/version"),
        },
        "session": {
            "type": os.environ.get("XDG_SESSION_TYPE"),
            "xdgRuntimeDir": runtime_dir,
            "waylandDisplay": wayland_display,
            "waylandSocketExists": bool(wayland_socket and wayland_socket.exists()),
            "display": os.environ.get("DISPLAY"),
            "pipeWireSocketExists": bool(runtime_dir and (Path(runtime_dir) / "pipewire-0").exists()),
            "systemdUserReachable": systemd_user_reachable,
        },
        "devices": {
            "kvmExists": Path("/dev/kvm").exists(),
            "kvmReadable": os.access("/dev/kvm", os.R_OK),
            "kvmWritable": os.access("/dev/kvm", os.W_OK),
            "renderNodes": render_nodes,
        },
        "userNamespaces": {
            "unprivilegedUsernsClone": _read_text(Path("/proc/sys/kernel/unprivileged_userns_clone")),
            "maxUserNamespaces": _read_text(Path("/proc/sys/user/max_user_namespaces")),
        },
        "managedWineRuntimes": managed_runtimes,
    }
    facts["preflight"] = evaluate_preflight(facts)
    return facts


def evaluate_preflight(facts: dict[str, Any]) -> dict[str, Any]:
    system = str(facts.get("platform", {}).get("system", "")).lower()
    machine = str(facts.get("platform", {}).get("machine", "")).lower()
    commands = facts.get("commands", {})
    session = facts.get("session", {})
    devices = facts.get("devices", {})
    runtimes = facts.get("managedWineRuntimes", [])

    wine_missing: list[str] = []
    if system != "linux":
        wine_missing.append("linux-host")
    if machine not in {"x86_64", "amd64"}:
        wine_missing.append("x86_64-host")
    if not commands.get("bwrap"):
        wine_missing.append("bubblewrap")
    if not session.get("waylandSocketExists"):
        wine_missing.append("wayland-socket")
    if not any(runtime.get("executable") for runtime in runtimes):
        wine_missing.append("managed-wine-runtime")
    if not all(commands.get(name) for name in ("systemd-run", "systemctl", "journalctl", "env")):
        wine_missing.append("systemd-user-supervisor-tools")
    elif not session.get("systemdUserReachable"):
        wine_missing.append("systemd-user-manager")

    winboat_missing: list[str] = []
    if system != "linux":
        winboat_missing.append("linux-host")
    if machine not in {"x86_64", "amd64"}:
        winboat_missing.append("x86_64-host")
    if not devices.get("kvmExists") or not devices.get("kvmReadable") or not devices.get("kvmWritable"):
        winboat_missing.append("usable-kvm")
    if not (commands.get("podman") or commands.get("docker")):
        winboat_missing.append("container-runtime")
    if not commands.get("freerdp"):
        winboat_missing.append("freerdp-3")

    return {
        "wineSlice1": {
            "prerequisitesPresent": not wine_missing,
            "missing": wine_missing,
            "note": "This is a read-only prerequisite check, not runtime proof.",
        },
        "winboatFuture": {
            "prerequisitesPresent": not winboat_missing,
            "missing": winboat_missing,
            "note": "WinBoat remains disabled; this does not authorize or create a Windows VM.",
        },
    }


def _first_command(*names: str) -> str | None:
    for name in names:
        path = shutil.which(name)
        if path:
            return path
    return None


def _command_version(binary: str | None, argument: str) -> str | None:
    if not binary:
        return None
    try:
        completed = subprocess.run(
            [binary, argument],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            timeout=3,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    first_line = completed.stdout.strip().splitlines()
    return first_line[0] if first_line else None


def _systemd_user_reachable(systemctl: str | None) -> bool:
    if not systemctl:
        return False
    try:
        completed = subprocess.run(
            [systemctl, "--user", "show-environment"],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=3,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return completed.returncode == 0


def _managed_runtimes(runtime_root: Path) -> list[dict[str, Any]]:
    if not runtime_root.is_dir():
        return []
    result: list[dict[str, Any]] = []
    for child in sorted(runtime_root.iterdir()):
        wine = child / "bin" / "wine"
        if child.is_dir() and wine.is_file():
            executable = os.access(wine, os.X_OK)
            result.append({
                "id": child.name,
                "winePath": str(wine),
                "executable": executable,
                "version": _command_version(str(wine), "--version") if executable else None,
            })
    return result


def _render_nodes() -> list[str]:
    dri = Path("/dev/dri")
    if not dri.is_dir():
        return []
    return [str(path) for path in sorted(dri.glob("renderD*")) if path.exists()]


def _read_text(path: Path) -> str | None:
    try:
        return path.read_text(encoding="utf-8").strip()
    except OSError:
        return None
