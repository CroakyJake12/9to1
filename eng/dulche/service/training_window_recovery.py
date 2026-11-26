"""A per-window systemd timer restores inference even when the first launcher disappears."""

import fcntl
import hashlib
import json
import os
from pathlib import Path
import signal
import sys
import time

from engine_profiles import atomic_json
from engines import _payload, _unit
from training_park import PRIMARY, RTX, require_no_training
from training_window_client import request


def read(path):
    return json.loads(path.read_text())


def resume(config):
    pid = config["scheduler_pid"]
    unit = _unit("dulche-independent-qwen-owner.service")
    if (
        unit.get("InvocationID") != config["scheduler_invocation"]
        or int(unit.get("MainPID") or 0) != pid
    ):
        return "original scheduler exited; no signal delivered"
    proc = Path("/proc") / str(pid)
    fd = os.pidfd_open(pid)
    try:
        stat = (proc / "stat").read_text().rsplit(") ", 1)[1].split()
        if (
            int(stat[19]) != config["scheduler_start_ticks"]
            or hashlib.sha256((proc / "cmdline").read_bytes()).hexdigest()
            != config["scheduler_argv_sha256"]
            or (proc / "cgroup").read_text().strip() != "0::" + unit["ControlGroup"]
        ):
            raise ValueError("Original scheduler identity no longer matches")
        if stat[0] == "T":
            signal.pidfd_send_signal(fd, signal.SIGCONT)
            return "resumed original scheduler"
        if stat[0] in ("R", "S", "D", "I"):
            return "original scheduler already running"
        if stat[0] in ("X", "Z"):
            return "original scheduler exited; no signal delivered"
        raise ValueError("Unexpected scheduler state")
    finally:
        os.close(fd)


def tick(root, config):
    state = Path(config["state_dir"])
    owner = config["owner"]
    window = request(owner=owner)
    phase = window.get("phase")
    if window.get("owner") != owner:
        raise ValueError("Original training window is missing")
    if phase == "restored":
        entry = next(e for e in window["models"] if e["id"] == PRIMARY)
        if (entry.get("restore") or {}).get("operator_suspended"):
            return (
                "operator-suspended",
                "manual suspension retained; owner scheduler remains paused",
            )
        active = request()
        if active.get("owner") != owner and active.get("phase") not in (
            "restored",
            "idle",
        ):
            return "waiting", "another cycle holds the mentor"
        cfg = (state / "engines/strata-owned.json").read_bytes()
        if hashlib.sha256(cfg).hexdigest() != entry["native"]["profile_digest"]:
            raise ValueError("Restored mentor profile identity differs")
        health = _payload(18500, "/v1/status", key=json.loads(cfg)["api_key"])
        if health.get("loaded") is not True or health.get("model") != PRIMARY:
            return "waiting", "awaiting live original mentor model"
        return "completed", resume(config)
    if phase in ("restoring", "releasing") and window.get("runner_active"):
        return "waiting", phase
    cycle_file = state / "training/cycles" / config["cycle"] / "cycle.json"
    cycle = read(cycle_file) if cycle_file.exists() else {}
    plan = cycle.get("plan", {})
    workflow = plan.get("workflow") if plan.get("first_window_owner") == owner else None
    due = time.time() >= config["deadline_epoch"]
    failed = (root / "admission-failed.json").is_file()
    if workflow:
        for p in (state / "training/jobs").glob("*/status.json"):
            job = read(p)
            if (
                job.get("workflow") == workflow
                and job.get("device") == RTX
                and job.get("created_epoch", 0) >= window["created_epoch"]
                and job.get("phase") in ("completed", "paused", "failed")
            ):
                due = True
        failed = failed or cycle.get("phase") in (
            "failed",
            "final_failed",
            "no_training",
        )
    if not (due or failed or window.get("direction") == "restoring"):
        return (
            "armed",
            "awaiting training completion, recorded launch failure, or deadline",
        )
    # The supervisor serializes new registrations under this same lock. Releasing the
    # lease immediately fences fresh cycle admissions; wait for any existing job first.
    with (state / "training/admission.lock").open("a") as admission:
        fcntl.flock(admission, fcntl.LOCK_EX | fcntl.LOCK_NB)
        require_no_training(state)
        for p in (state / "training/jobs").glob("*/status.json"):
            j = read(p)
            if j.get("device") == RTX and j.get("phase") not in (
                "completed",
                "paused",
                "failed",
            ):
                return (
                    "waiting",
                    "training supervisor has not published a terminal receipt",
                )
        result = request("release", owner)
        return "waiting", result["phase"]


def main(root):
    result = root / "recovery-status.json"
    if result.is_file() and read(result).get("state") in (
        "completed",
        "operator-suspended",
    ):
        return
    try:
        state, detail = tick(root, read(root / "config.json"))
    except Exception as exc:
        state, detail = "retrying", str(exc)[:400]
    atomic_json(result, {"state": state, "detail": detail, "time": time.time()})


if __name__ == "__main__":
    main(Path(sys.argv[1]))
