from __future__ import annotations

import hashlib
import os
import shutil
import subprocess
from dataclasses import dataclass
from typing import Any


class LifecycleError(RuntimeError):
    pass


@dataclass(frozen=True)
class UnitStatus:
    unit: str
    load_state: str
    active_state: str
    sub_state: str
    result: str | None = None
    main_pid: int | None = None

    @property
    def running(self) -> bool:
        return self.active_state in {"active", "activating", "reloading"}

    def as_dict(self) -> dict[str, Any]:
        return {
            "unit": self.unit,
            "loadState": self.load_state,
            "activeState": self.active_state,
            "subState": self.sub_state,
            "result": self.result,
            "mainPid": self.main_pid,
            "running": self.running,
        }


def unit_name(app_id: str) -> str:
    # Do not embed user-controlled app identifiers directly in systemd unit
    # names. A stable digest avoids escaping/quoting ambiguity and keeps names
    # bounded even if the manifest format grows later.
    digest = hashlib.sha256(app_id.encode("utf-8")).hexdigest()[:20]
    return f"haven-compat-{digest}.service"


class UserSystemdSupervisor:
    """Own one transient systemd --user service per compatibility app."""

    def __init__(
        self,
        systemd_run: str | None = None,
        systemctl: str | None = None,
        env_bin: str | None = None,
        journalctl: str | None = None,
    ) -> None:
        self.systemd_run = systemd_run or shutil.which("systemd-run")
        self.systemctl = systemctl or shutil.which("systemctl")
        self.env_bin = env_bin or shutil.which("env")
        self.journalctl = journalctl or shutil.which("journalctl")

    @property
    def available(self) -> bool:
        return bool(self.systemd_run and self.systemctl and self.env_bin)

    def build_start_argv(self, app_id: str, launch_argv: tuple[str, ...], service_env: dict[str, str]) -> tuple[str, ...]:
        if not self.available:
            raise LifecycleError("systemd user-service supervision is required for compatibility slice 1")

        clean_env = [self.env_bin, "-i"]
        for key in ("PATH", "LANG"):
            value = service_env.get(key)
            if value:
                clean_env.append(f"{key}={value}")

        return tuple([
            self.systemd_run,
            "--user",
            f"--unit={unit_name(app_id)}",
            "--collect",
            "--property=KillMode=control-group",
            "--property=TimeoutStopSec=10s",
            "--",
            *clean_env,
            *launch_argv,
        ])

    def start(self, app_id: str, launch_argv: tuple[str, ...], service_env: dict[str, str]) -> UnitStatus:
        current = self.status(app_id)
        if current.running:
            raise LifecycleError(f"compatibility application is already running: {current.unit}")

        command = self.build_start_argv(app_id, launch_argv, service_env)
        completed = subprocess.run(
            command,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=_control_env(),
            timeout=15,
            check=False,
        )
        if completed.returncode != 0:
            detail = completed.stdout.strip() or "systemd-run failed"
            raise LifecycleError(detail)
        return self.status(app_id)

    def status(self, app_id: str) -> UnitStatus:
        if not self.systemctl:
            raise LifecycleError("systemctl is required for compatibility lifecycle status")

        unit = unit_name(app_id)
        completed = subprocess.run(
            [
                self.systemctl,
                "--user",
                "show",
                unit,
                "--property=LoadState",
                "--property=ActiveState",
                "--property=SubState",
                "--property=Result",
                "--property=MainPID",
                "--no-pager",
            ],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=_control_env(),
            timeout=5,
            check=False,
        )
        values = _parse_properties(completed.stdout)
        if completed.returncode != 0 and not values:
            detail = completed.stdout.strip() or "systemctl --user is unavailable"
            raise LifecycleError(detail)

        load_state = values.get("LoadState", "not-found")
        active_state = values.get("ActiveState", "inactive")
        sub_state = values.get("SubState", "dead")
        main_pid_raw = values.get("MainPID", "0")
        try:
            main_pid = int(main_pid_raw)
        except ValueError:
            main_pid = 0

        return UnitStatus(
            unit=unit,
            load_state=load_state,
            active_state=active_state,
            sub_state=sub_state,
            result=values.get("Result") or None,
            main_pid=main_pid or None,
        )

    def stop(self, app_id: str) -> UnitStatus:
        if not self.systemctl:
            raise LifecycleError("systemctl is required for compatibility lifecycle stop")

        current = self.status(app_id)
        if current.load_state == "not-found" or not current.running:
            return current

        completed = subprocess.run(
            [self.systemctl, "--user", "stop", current.unit],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=_control_env(),
            timeout=20,
            check=False,
        )
        if completed.returncode != 0:
            detail = completed.stdout.strip() or "systemctl stop failed"
            raise LifecycleError(detail)
        return self.status(app_id)

    def logs(self, app_id: str, lines: int = 200) -> str:
        if not self.journalctl:
            raise LifecycleError("journalctl is required for compatibility lifecycle logs")
        if lines < 1 or lines > 1000:
            raise LifecycleError("log line count must be between 1 and 1000")

        completed = subprocess.run(
            [
                self.journalctl,
                f"--user-unit={unit_name(app_id)}",
                "--no-pager",
                "--output=short-iso",
                "-n",
                str(lines),
            ],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=_control_env(),
            timeout=10,
            check=False,
        )
        if completed.returncode != 0:
            detail = completed.stdout.strip() or "journalctl failed"
            raise LifecycleError(detail)
        return completed.stdout


def _parse_properties(output: str) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in output.splitlines():
        if "=" not in line:
            continue
        key, value = line.split("=", 1)
        values[key] = value
    return values


def _control_env() -> dict[str, str]:
    # systemctl/systemd-run need session-bus routing, but the compatibility
    # service itself is launched through `env -i` so unrelated user/session
    # environment variables are not leaked into Windows applications.
    env = {
        "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
        "LANG": os.environ.get("LANG", "C.UTF-8"),
    }
    for key in ("XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS"):
        value = os.environ.get(key)
        if value:
            env[key] = value
    return env
