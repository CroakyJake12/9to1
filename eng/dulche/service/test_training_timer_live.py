#!/usr/bin/python3
"""Explicit isolated timer-transport check. No training process or production monitor is created."""

import argparse
import json
import os
import time
import uuid
from pathlib import Path

import training_jobs as t


def verify(state):
    state = Path(state).resolve()
    ident = "fixture-" + uuid.uuid4().hex[:16]
    base = "dulche-fixture-timer-" + ident
    path = state / "training/jobs" / ident
    (path / "output").mkdir(parents=True, exist_ok=False)
    # Advance only the fixture's *hypothetical* launch epoch: the production +3600 contract
    # remains unchanged. There was no worker launch. Its health must therefore be Unknown.
    synthetic = time.time() - 3593
    due = synthetic + 3600
    rec = {
        "id": ident,
        "purpose": "software_timer_transport_only",
        "worker_unit": base + "-absent.service",
        "worker_pid": None,
        "phase": "never-started-fixture",
        "device": "cpu",
        "steps": 0,
        "checkpoint": None,
        "started_epoch": None,
        "monitor": {"unit": base + ".timer", "scheduled_epoch": due},
    }
    t.atomic_json(path / "status.json", rec)
    contents = [
        x.replace(
            "Dulche one-hour training observation",
            "Dulche isolated timer transport fixture",
        ).replace(
            "Dulche independent training health observation",
            "Dulche isolated health-report fixture",
        )
        for x in t.render_check_units(state, ident, synthetic)
    ]
    units = (
        Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
        / "systemd/user"
    )
    names = [units / (base + "." + suffix) for suffix in ("service", "timer")]
    t.require(not any(p.exists() for p in names), "Fixture unit names already exist")
    try:
        t.install_calendar_units(base, contents)
        enabled = t.run(
            ["systemctl", "--user", "is-enabled", base + ".timer"]
        ).stdout.strip()
        end = time.monotonic() + 30
        while time.monotonic() < end and not (path / "hour-check.json").is_file():
            time.sleep(0.1)
        result = t.json_file(path / "hour-check.json")
        t.require(
            result["purpose"] == "software_timer_transport_only"
            and result["health"] == "Unknown",
            "Unexpected fixture result",
        )
        t.require(result["actual_check_epoch"] >= due - 0.01, "Fixture timer ran early")
        proof = {
            "purpose": "software_timer_transport_only",
            "training_started": False,
            "production_monitor_registered": False,
            "fixture_unit": base + ".timer",
            "enabled": enabled,
            "fixture_simulated_epoch": synthetic,
            "scheduled_epoch": due,
            "actual_epoch": result["actual_check_epoch"],
            "elapsed_from_synthetic_epoch": result["actual_check_epoch"] - synthetic,
            "report_path": str(path / "hour-check.json"),
            "health": result["health"],
            "gpu_now": result["gpu_now"],
        }
    finally:
        t.run(["systemctl", "--user", "disable", "--now", base + ".timer"], check=False)
        t.run(["systemctl", "--user", "stop", base + ".service"], check=False)
        t.run(
            ["systemctl", "--user", "clean", "--what=state", base + ".timer"],
            check=False,
        )
        for p, expected in zip(names, contents, strict=True):
            if p.is_file() and p.read_text() == expected:
                p.unlink()
        t.run(["systemctl", "--user", "daemon-reload"])
    proof["fixture_units_removed"] = all(not p.exists() for p in names)
    return proof


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--fixture-state", required=True, type=Path)
    a = ap.parse_args()
    if (
        a.fixture_state.resolve()
        == t.TrainingJobs(Path.home() / ".local/state/9to1/dulche").state
    ):
        raise SystemExit("Use a separate fixture-only directory")
    print(json.dumps(verify(a.fixture_state), indent=2))
