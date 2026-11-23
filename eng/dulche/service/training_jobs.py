#!/usr/bin/python3
"""Supervise an independently supplied local workflow. This module contains no training recipe or data."""

from __future__ import annotations

import argparse
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import os
import re
import sqlite3
import stat
import subprocess
import sys
import time
import uuid
from pathlib import Path

from engine_profiles import atomic_json
from training_budget import (
    ComputeBudget,
    GiB,
    MiB,
    gpu_sample,
    group_counters,
    host_sample,
)

HERE = Path(__file__).resolve()
REPO = HERE.parents[3]
SCHEMA = "dulche-independent-workflow-v1"
ID = re.compile(r"[a-z0-9][a-z0-9-]{0,62}\Z")
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
TERMINAL = {"completed", "paused", "failed"}
UTC = dt.UTC


def require(value, message):
    if not value:
        raise ValueError(message)


def run(args, check=True, timeout=15):
    p = subprocess.run(
        args, text=True, capture_output=True, check=False, timeout=timeout
    )
    if check and p.returncode:
        raise RuntimeError(
            (p.stderr or p.stdout or "systemd operation failed").strip()[:500]
        )
    return p


def json_file(path, limit=1024**2):
    with Path(path).open("rb") as f:
        data = f.read(limit + 1)
    require(len(data) <= limit, "Manifest or status exceeds the maximum size")
    result = json.loads(data)
    require(isinstance(result, dict), "Expected a JSON object")
    return result


def canonical_digest(value):
    return hashlib.sha256(
        json.dumps(
            value, sort_keys=True, separators=(",", ":"), allow_nan=False
        ).encode()
    ).hexdigest()


def digest(path):
    with Path(path).open("rb") as data:
        return hashlib.file_digest(data, "sha256").hexdigest()


def name(value):
    require(
        isinstance(value, str) and ID.fullmatch(value),
        "Use a lowercase local workflow/job identifier",
    )
    return value


def absolute(value):
    require(
        isinstance(value, str)
        and Path(value).is_absolute()
        and "\n" not in value
        and "\0" not in value,
        "Expected an absolute local path",
    )
    return Path(value).resolve(strict=True)


def verified_artifact(obj):
    require(
        isinstance(obj, dict)
        and isinstance(obj.get("sha256"), str)
        and DIGEST.fullmatch(obj["sha256"]),
        "A pinned artifact needs its SHA-256 digest",
    )
    p = absolute(obj["path"])
    require(
        p.is_file() and digest(p) == obj["sha256"],
        "Pinned artifact bytes differ from the independent registration",
    )
    return p


def custody_path():
    if not (HERE.parent / "custody/provenance.py").is_file():
        custody = REPO / "9to1 Models/Dulche Alpha/Training"
        require(
            (custody / "custody/provenance.py").is_file(),
            "Verified training custody validators are not installed",
        )
        if str(custody) not in sys.path:
            sys.path.insert(0, str(custody))


def verify_workflow(w):
    require(
        w.get("schema") == SCHEMA and w.get("purpose") == "real_training",
        "Only separately configured real workflows can be registered",
    )
    name(w["id"])
    require(
        isinstance(w.get("model"), str) and w["model"].strip(),
        "Independent model identity is absent",
    )
    require(
        w.get("worker_protocol") == "checkpoint-yield-v1",
        "Worker must support the checkpoint/release contract",
    )
    executable = verified_artifact(w["executable"])
    require(os.access(executable, os.X_OK), "Pinned worker is not executable")
    args = w.get("arguments", [])
    require(
        isinstance(args, list)
        and len(args) <= 128
        and all(
            isinstance(v, str) and len(v) <= 4096 and "\0" not in v and "\n" not in v
            for v in args
        ),
        "Invalid worker arguments",
    )
    require(
        not any(option in args for option in ("-c", "--command", "-e", "--eval")),
        "Inline shell or interpreter programs are not independent pinned artifacts",
    )
    pinned = {str(verified_artifact(obj)) for obj in w.get("inputs", [])}
    for arg in args:
        candidate = Path(arg) if Path(arg).is_absolute() else absolute(w["cwd"]) / arg
        if candidate.is_file():
            require(
                str(candidate.resolve()) in pinned,
                "File arguments must be pinned in inputs",
            )
    require(absolute(w["cwd"]).is_dir(), "Worker directory is missing")
    for root in w.get("read_only_roots", []):
        allowed = absolute(root)
        require(
            allowed.is_dir()
            and allowed != Path("/")
            and Path("/dev") not in (allowed, *allowed.parents),
            "Invalid additional read-only root",
        )
    require(
        Path(w["cwd"]).resolve() != Path("/")
        and Path("/dev")
        not in (Path(w["cwd"]).resolve(), *Path(w["cwd"]).resolve().parents),
        "Worker directory cannot expose the device filesystem",
    )
    # The same provenance contract is used by the existing Dulche training custody layer.
    custody_path()
    from custody.provenance import validate_dataset_manifest, verify_dataset_files

    manifest = verified_artifact(w["dataset_manifest"])
    verify_dataset_files(
        validate_dataset_manifest(json_file(manifest), purpose="real_training"),
        absolute(w["dataset_root"]),
    )
    limits = w["limits"]
    for k in (
        "ram_mib",
        "min_free_ram_mib",
        "checkpoint_reserve_bytes",
        "max_checkpoint_bytes",
        "wall_seconds",
        "progress_timeout_seconds",
        "checkpoint_grace_seconds",
    ):
        require(
            type(limits.get(k)) is int and limits[k] > 0,
            f"Missing or invalid approved limit {k}",
        )
    require(
        limits["checkpoint_grace_seconds"] <= limits["wall_seconds"],
        "Invalid checkpoint grace period",
    )
    require(
        type(limits.get("vram_mib")) is int and limits["vram_mib"] >= 0,
        "Missing VRAM limit",
    )
    require(
        w["device"] == "cpu" or re.fullmatch(r"GPU-[0-9a-fA-F-]{36}", w["device"]),
        "Expected a CPU or physical GPU UUID",
    )
    if w["device"] != "cpu":
        require(
            limits["vram_mib"] > 0
            and type(limits.get("max_gpu_temperature_c")) in (int, float)
            and 0 < limits["max_gpu_temperature_c"] <= 68,
            "GPU jobs require explicit memory and thermal limits",
        )
    if "startup_timeout_seconds" in limits:
        require(
            type(limits["startup_timeout_seconds"]) is int
            and 0 < limits["startup_timeout_seconds"] <= limits["wall_seconds"],
            "Startup timeout must fit within the approved wall budget",
        )
    return w


def unit_props(unit):
    props = (
        "MainPID",
        "ExecMainPID",
        "ActiveState",
        "SubState",
        "Result",
        "ExecMainStatus",
        "ExecMainStartTimestampMonotonic",
        "ControlGroup",
    )
    p = run(
        [
            "systemctl",
            "--user",
            "show",
            unit,
            *[arg for prop in props for arg in ("-p", prop)],
        ],
        check=False,
    )
    return dict(line.split("=", 1) for line in p.stdout.splitlines() if "=" in line)


def epoch_started(props):
    usec = int(props.get("ExecMainStartTimestampMonotonic") or 0)
    require(usec > 0, "Worker start time is unverified")
    return time.time() - (time.monotonic() - usec / 1e6)


def unit_argument(value):
    value = str(value)
    require(not any(c in value for c in "\x00\r\n"), "Invalid systemd path")
    return (
        '"'
        + value.replace("\\", "\\\\")
        .replace('"', '\\"')
        .replace("%", "%%")
        .replace("$", "$$")
        + '"'
    )


def render_check_units(state, job, started):
    job = name(job)
    when = dt.datetime.fromtimestamp(started + 3600, UTC).strftime(
        "%Y-%m-%d %H:%M:%S.%f UTC"
    )
    timer = f"[Unit]\nDescription=Dulche one-hour training observation\n[Timer]\nOnCalendar={when}\nPersistent=true\nAccuracySec=1us\nRandomizedDelaySec=0\n[Install]\nWantedBy=timers.target\n"
    command = " ".join(
        unit_argument(part)
        for part in (sys.executable, HERE, "--state", state, "hour-check", job)
    )
    service = f"[Unit]\nDescription=Dulche independent training health observation\n[Service]\nType=oneshot\nNice=15\nIOSchedulingClass=idle\nCPUQuota=50%\nExecStart={command}\n"
    return service, timer


def register_timer(state, job, started):
    # OnCalendar + a persisted unit/enablement survives user-manager restarts and catches missed checks.
    base = "dulche-training-check-" + name(job)
    contents = render_check_units(state, job, started)
    install_calendar_units(base, contents)
    return {
        "unit": base + ".timer",
        "scheduled_epoch": started + 3600,
        "registered_epoch": time.time(),
    }


def install_calendar_units(base, contents):
    require(
        re.fullmatch(r"dulche-(training-check|fixture-timer)-[a-z0-9-]+", base),
        "Invalid health timer scope",
    )
    units = (
        Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
        / "systemd/user"
    )
    units.mkdir(parents=True, exist_ok=True)
    created = []
    enabling = False
    try:
        for suffix, text in zip(("service", "timer"), contents, strict=True):
            path = units / (base + "." + suffix)
            if path.exists():
                require(
                    path.read_text() == text,
                    "An existing monitoring unit has different contents",
                )
                continue
            with path.open("x", encoding="utf-8") as f:
                f.write(text)
                f.flush()
                os.fsync(f.fileno())
            created.append(path)
        run(["systemctl", "--user", "daemon-reload"])
        enabling = True
        run(["systemctl", "--user", "enable", "--now", base + ".timer"])
        require(
            run(
                ["systemctl", "--user", "is-active", base + ".timer"], check=False
            ).stdout.strip()
            == "active",
            "One-hour timer failed to become active",
        )
    except BaseException:
        if enabling:
            run(
                ["systemctl", "--user", "disable", "--now", base + ".timer"],
                check=False,
            )
        for path in created:
            path.unlink(missing_ok=True)
        run(["systemctl", "--user", "daemon-reload"], check=False)
        raise


def checkpoint(output, receipt, max_size, store=None):
    require(
        isinstance(receipt, dict) and DIGEST.fullmatch(str(receipt.get("sha256", ""))),
        "Incomplete checkpoint receipt",
    )
    relative = Path(receipt["path"])
    require(
        not relative.is_absolute() and ".." not in relative.parts,
        "Checkpoint must be inside this attempt",
    )
    root = (output / "checkpoints").resolve(strict=True)
    p = (root / relative).resolve(strict=True)
    require(
        root in p.parents
        and stat.S_ISREG(p.stat().st_mode)
        and 0 < p.stat().st_size <= max_size,
        "Invalid checkpoint artifact",
    )
    steps = receipt.get("steps")
    require(
        type(steps) is int and steps >= 0, "Checkpoint must identify its completed step"
    )
    if store is not None:
        # Publish into a supervisor-only content-addressed store. The worker retains write
        # access only to its own output and cannot overwrite a verified resume point.
        custody_path()
        from custody.artifacts import _open_regular, _write_atomic

        store.mkdir(parents=True, exist_ok=True, mode=0o700)
        final = store / receipt["sha256"]
        if not final.exists():
            size = p.stat().st_size
            require(
                host_sample(store)["available_disk_bytes"] >= 50 * GiB + size,
                "Checkpoint publication would cross the disk floor",
            )
            with _open_regular(p) as source:
                try:
                    _write_atomic(
                        final,
                        source,
                        max_bytes=size,
                        expected=(receipt["sha256"], size),
                    )
                except FileExistsError:
                    pass
        p = final
    require(digest(p) == receipt["sha256"], "Checkpoint digest does not verify")
    return {
        "path": str(p),
        "sha256": receipt["sha256"],
        "size": p.stat().st_size,
        "steps": steps,
    }


def inference_busy(state):
    p = Path(state) / "service.sqlite3"
    if not p.exists():
        return False
    # Inspect only scheduling state. No prompts or generated content enter the training workflow.
    with contextlib.closing(
        sqlite3.connect(p.resolve().as_uri() + "?mode=ro", uri=True, timeout=2)
    ) as db:
        return (
            db.execute(
                "select 1 from runs where state in ('queued','running','canceling') limit 1"
            ).fetchone()
            is not None
        )


def cycle_owns(root, status, cycle):
    if status.get("cycle") == cycle:
        return True
    marker = Path(root) / "jobs" / name(status["id"]) / "cycle-owner.json"
    if not marker.is_file():
        return False
    bound = json_file(marker)
    return (
        bound.get("cycle") == cycle
        and bound.get("attempt") == status["id"]
        and bound.get("workflow_digest") == status.get("workflow_digest")
        and bound.get("absolute_deadline") == status.get("absolute_deadline")
    )


class TrainingJobs:
    def __init__(self, state):
        self.state = Path(state).resolve()
        self.root = self.state / "training"

    def job(self, id):
        p = self.root / "jobs" / name(id)
        require(p.is_dir(), "Unknown training attempt")
        return p

    def register(self, manifest, sha):
        require(
            DIGEST.fullmatch(sha or "") and digest(manifest) == sha,
            "Independent workflow manifest digest mismatch",
        )
        w = verify_workflow(json_file(manifest))
        path = self.root / "workflows" / (name(w["id"]) + ".json")
        if path.exists():
            require(
                json_file(path)["digest"] == sha,
                "Use a distinct ID for a changed workflow",
            )
        atomic_json(
            path,
            {
                "manifest": w,
                "digest": sha,
                "record_digest": canonical_digest(w),
                "registered_epoch": time.time(),
            },
        )
        return {"registered": w["id"], "model": w["model"], "training_launched": False}

    def workflow(self, id):
        p = self.root / "workflows" / (name(id) + ".json")
        require(p.exists(), "No independently registered workflow with this ID")
        rec = json_file(p)
        require(
            canonical_digest(rec["manifest"]) == rec.get("record_digest"),
            "Independent workflow registration changed",
        )
        return verify_workflow(rec["manifest"]), rec["digest"]

    def launch(
        self,
        workflow,
        parent=None,
        *,
        cycle=None,
        attempt=None,
        cutoff=None,
        directives=None,
    ):
        w, sha = self.workflow(workflow)
        if cycle is not None:
            name(cycle)
            require(
                isinstance(attempt, str) and re.fullmatch(r"[0-9a-f]{32}", attempt),
                "A cycle requires an exact durable attempt identity",
            )
            existing = self.root / "jobs" / attempt / "status.json"
            if existing.is_file():
                current = json_file(existing)
                require(
                    current.get("cycle") == cycle
                    and current["workflow_digest"] == sha
                    and current.get("resume_of") == parent
                    and current.get("absolute_deadline") == cutoff,
                    "Cycle attempt identity conflict",
                )
                return current
            require(
                isinstance(cutoff, (float, int)) and time.time() < cutoff,
                "The cycle training deadline has passed",
            )
            if directives:
                verified_artifact(directives)
        else:
            require(
                attempt is None and directives is None,
                "Cycle admission fields require a cycle",
            )
        if cutoff is not None:
            require(
                type(cutoff) in (int, float) and time.time() < cutoff < 32_503_680_000,
                "Use a future absolute training deadline",
            )
        before = host_sample(self.state)
        require(
            before["available_disk_bytes"]
            >= 50 * GiB + w["limits"]["checkpoint_reserve_bytes"],
            "Insufficient disk space",
        )
        require(
            before["available_ram_bytes"]
            >= (w["limits"]["min_free_ram_mib"] + w["limits"]["ram_mib"]) * MiB,
            "Insufficient RAM for approved training and desktop headroom",
        )
        require(
            not inference_busy(self.state), "Inference work is active; defer training"
        )
        if w["device"] != "cpu":
            gpu = gpu_sample(w["device"], set())
            require(
                gpu["other_sm_pct"] <= 5
                and gpu["free_vram_mib"] >= w["limits"]["vram_mib"]
                and gpu["temperature_c"] < w["limits"]["max_gpu_temperature_c"],
                "Selected GPU is busy, hot or lacks capacity",
            )
        self.root.mkdir(parents=True, exist_ok=True, mode=0o700)
        with (self.root / "admission.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            for f in (self.root / "jobs").glob("*/status.json"):
                j = json_file(f)
                if j["phase"] not in TERMINAL and j["device"] == w["device"]:
                    observed = [
                        unit_props(j[k]).get("ActiveState")
                        for k in ("worker_unit", "supervisor_unit")
                    ]
                    if any(
                        value in ("activating", "active", "deactivating", "reloading")
                        for value in observed
                    ):
                        raise ValueError(
                            "An existing attempt holds this device; inspect its status"
                        )
                    j.update(
                        phase="failed",
                        reason="Supervisor and worker are absent. Explicit resumption requires a verified checkpoint.",
                    )
                    atomic_json(f, j)
            previous = None
            prior_steps = 0
            if parent:
                old = json_file(self.job(parent) / "status.json")
                require(
                    old["workflow_digest"] == sha
                    and (not cycle or cycle_owns(self.root, old, cycle))
                    and (
                        old["phase"] in ("paused", "failed")
                        or (
                            cycle
                            and old["phase"] == "completed"
                            and cycle_owns(self.root, old, cycle)
                        )
                    ),
                    "Attempt is not eligible for explicit resume",
                )
                previous = old.get("checkpoint")
                prior_steps = previous.get("steps", 0) if previous else 0
                require(
                    previous and digest(previous["path"]) == previous["sha256"],
                    "No valid resume checkpoint",
                )
            id = attempt or uuid.uuid4().hex
            job = self.root / "jobs" / id
            if cycle and (job / "status.json").exists():
                current = json_file(job / "status.json")
                require(
                    current.get("cycle") == cycle
                    and current["workflow_digest"] == sha
                    and current.get("resume_of") == parent
                    and current.get("absolute_deadline") == cutoff,
                    "Cycle attempt identity conflict",
                )
                return current
            out = job / "output"
            (out / "checkpoints").mkdir(parents=True, mode=0o700)
            base = "dulche-training-" + id
            value = {
                "id": id,
                "workflow": w["id"],
                "model": w["model"],
                "device": w["device"],
                "workflow_digest": sha,
                "phase": "requested",
                "created_epoch": time.time(),
                "supervisor_unit": base + ".service",
                "worker_unit": base + "-worker.service",
                "started_epoch": None,
                "worker_pid": None,
                "monitor": None,
                "steps": prior_steps,
                "checkpoint": previous,
                "resume_of": parent,
                "last_progress_epoch": None,
                "mean_compute_pct": None,
            }
            if cutoff is not None:
                value["absolute_deadline"] = cutoff
            if cycle:
                value.update(
                    cycle=cycle, absolute_deadline=cutoff, cycle_directives=directives
                )
            atomic_json(job / "status.json", value)
            try:
                run(
                    [
                        "systemd-run",
                        "--user",
                        "--collect",
                        "--unit=" + base,
                        "-p",
                        "Type=exec",
                        "-p",
                        "Restart=no",
                        "-p",
                        "Nice=10",
                        sys.executable,
                        str(HERE),
                        "--state",
                        str(self.state),
                        "supervise",
                        id,
                    ]
                )
            except Exception as exc:
                value.update(phase="failed", reason=str(exc))
                atomic_json(job / "status.json", value)
                raise
            return value

    def pause(self, id):
        p = self.job(id)
        s = json_file(p / "status.json")
        if s["phase"] not in TERMINAL:
            atomic_json(p / "pause.json", {"requested_epoch": time.time()})
        return {"id": id, "pause_requested": s["phase"] not in TERMINAL}

    def _start_worker(self, w, s, job):
        out = job / "output"
        quota = 10 * max(os.cpu_count() or 1, 1)
        args = [
            "systemd-run",
            "--user",
            "--unit=" + s["worker_unit"],
            "-p",
            "Type=exec",
            "-p",
            "RemainAfterExit=yes",
            "-p",
            "Restart=no",
            "-p",
            "KillMode=control-group",
            "-p",
            "TimeoutStopSec=5s",
            "-p",
            "OOMPolicy=stop",
            "-p",
            "BindsTo=" + s["supervisor_unit"],
            "-p",
            "After=" + s["supervisor_unit"],
            "-p",
            "Nice=15",
            "-p",
            "CPUQuota=" + str(quota) + "%",
            "-p",
            "MemoryMax=" + str(w["limits"]["ram_mib"]) + "M",
            "-p",
            "MemorySwapMax=0",
            "-p",
            "NoNewPrivileges=yes",
            "-p",
            "PrivateTmp=yes",
            "-p",
            "ProtectSystem=strict",
            "-p",
            "ProtectHome=read-only",
            "-p",
            "ReadWritePaths=" + unit_argument(out),
            "-p",
            "WorkingDirectory=" + w["cwd"],
            "-p",
            "StandardOutput=append:" + str(out / "worker.log"),
            "-p",
            "StandardError=inherit",
            "--setenv=DULCHE_TRAIN_CONTROL=" + str(job / "control.json"),
            "--setenv=DULCHE_TRAIN_OUTPUT=" + str(out),
            "--setenv=DULCHE_TRAIN_JOB=" + s["id"],
            "--setenv=CUDA_VISIBLE_DEVICES="
            + ("" if w["device"] == "cpu" else w["device"]),
        ]
        if s.get("absolute_deadline"):
            remaining = int(s["absolute_deadline"] - time.time()) - 5
            require(remaining > 0, "Cycle deadline reached before worker launch")
            args += ["-p", f"RuntimeMaxSec={remaining}s"]
        if s.get("cycle_directives"):
            args += [
                "--setenv=DULCHE_CYCLE_DIRECTIVES="
                + str(verified_artifact(s["cycle_directives"]))
            ]
        if s["checkpoint"]:
            args += ["--setenv=DULCHE_RESUME_CHECKPOINT=" + s["checkpoint"]["path"]]
        if w["device"] == "cpu":
            # PrivateDevices cannot drop host capabilities in this Ubuntu user manager. Hide
            # each actual compute device with its supported mount-namespace restriction.
            device_nodes = [
                *Path("/dev").glob("nvidia*"),
                Path("/dev/dri"),
                Path("/dev/kfd"),
                Path("/dev/vfio"),
                Path("/dev/iommu"),
            ]
            args += [
                "-p",
                "InaccessiblePaths="
                + " ".join("-" + str(path) for path in device_nodes),
            ]
        read_roots = [os.fspath(x["path"]) for x in w.get("inputs", [])]
        if "dataset_root" in w:
            read_roots.append(w["dataset_root"])
        read_roots.extend(w.get("read_only_roots", []))
        if s.get("cycle_directives"):
            read_roots.append(str(verified_artifact(s["cycle_directives"])))
        if s.get("checkpoint"):
            read_roots.append(s["checkpoint"]["path"])
        minor = None
        if w["device"] != "cpu":
            text = run(
                [
                    "nvidia-smi",
                    "--query-gpu=uuid,minor_number",
                    "--format=csv,noheader,nounits",
                ]
            ).stdout
            mapping = dict(
                [part.strip() for part in row.split(",")]
                for row in text.splitlines()
                if "," in row
            )
            minor = int(mapping[w["device"]])
        atomic_json(
            job / "launch.json",
            {
                "executable": w["executable"],
                "arguments": w.get("arguments", []),
                "cwd": w["cwd"],
                "device": w["device"],
                "gpu_minor": minor,
                "read_roots": read_roots,
            },
        )
        run(
            args
            + [
                sys.executable,
                "-I",
                "-S",
                "-B",
                str(HERE.with_name("training_worker_exec.py")),
                str(job),
            ]
        )
        p = unit_props(s["worker_unit"])
        pid = int(p.get("ExecMainPID") or p.get("MainPID") or 0)
        require(pid > 0, "Worker process launch could not be verified")
        return pid, epoch_started(p)

    def supervise(self, id):
        job = self.job(id)
        with (job / "supervisor.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            s = json_file(job / "status.json")
            props = unit_props(s["supervisor_unit"])
            require(
                int(props.get("MainPID") or 0) == os.getpid(),
                "Supervisor must run in its independently owned user service",
            )
            return self._supervise(id)

    def _supervise(self, id):
        job = self.job(id)
        path = job / "status.json"
        s = json_file(path)
        require(
            s["phase"] == "requested",
            "Refusing duplicate execution of an existing attempt",
        )
        w, sha = self.workflow(s["workflow"])
        require(sha == s["workflow_digest"], "Workflow registration changed")
        limits = w["limits"]
        out = job / "output"
        control = job / "control.json"
        external = out / "progress.json"
        budget = ComputeBudget()
        first_steps = s["steps"]
        deadline = time.monotonic() + limits["wall_seconds"]
        s.update(phase="starting")
        atomic_json(path, s)
        atomic_json(control, {"action": "wait", "job": id, "permit_until_monotonic": 0})
        last_checkpoint_input = None
        pause_reason = None
        pause_deadline = None
        worker_started = False
        last_pids = set()
        last_gpu = None
        try:
            pid, started = self._start_worker(w, s, job)
            worker_started = True
            s.update(worker_pid=pid, process_launch_epoch=started)
            atomic_json(path, s)
            while True:
                now = time.monotonic()
                wall = time.time()
                props = unit_props(s["worker_unit"])
                live = (
                    props.get("SubState") == "running"
                    and int(props.get("MainPID") or 0) == pid
                )
                progress = json_file(external) if external.is_file() else {}
                if progress:
                    require(
                        progress.get("job") == id and progress.get("pid") == pid,
                        "Progress identity mismatch",
                    )
                    steps = progress.get("steps")
                    require(
                        type(steps) is int and steps >= s["steps"],
                        "Invalid or reversed worker progress",
                    )
                    if steps > s["steps"]:
                        s.update(steps=steps, last_progress_epoch=wall)
                    c = progress.get("checkpoint")
                    if c and c != last_checkpoint_input:
                        saved = checkpoint(
                            out,
                            c,
                            limits["max_checkpoint_bytes"],
                            self.root / "checkpoints",
                        )
                        require(
                            saved["steps"] <= steps
                            and saved["steps"]
                            >= (s.get("checkpoint") or {}).get("steps", 0),
                            "Checkpoint step falls outside the confirmed progress range",
                        )
                        s["checkpoint"] = saved
                        last_checkpoint_input = c
                if (
                    s["started_epoch"] is None
                    and progress.get("training_started") is True
                ):
                    require(
                        live or int(props.get("ExecMainPID") or 0) == pid,
                        "Unverified training process identity",
                    )
                    s.update(started_epoch=started, ready_epoch=wall, phase="running")
                    s["monitor"] = register_timer(self.state, id, started)
                    atomic_json(path, s)
                if not live:
                    rc = int(props.get("ExecMainStatus") or 0)
                    if (
                        s["started_epoch"]
                        and s["monitor"]
                        and rc == 0
                        and progress.get("phase") == "completed"
                        and s["steps"] > first_steps
                        and last_checkpoint_input
                        and s["checkpoint"]["steps"] == s["steps"]
                    ):
                        s.update(
                            phase="completed",
                            reason="Worker completed; progress and checkpoint verified",
                        )
                    elif (
                        pause_reason
                        and s["checkpoint"]
                        and progress.get("phase") == "paused"
                        and rc in (0, 75)
                    ):
                        s.update(phase="paused", reason=pause_reason)
                    else:
                        s.update(
                            phase="failed",
                            reason=f"Worker exited ({props.get('Result')}, status {rc}); completion unverified",
                        )
                    break
                if s["started_epoch"] is None and wall - started > min(
                    limits.get("startup_timeout_seconds", 180), limits["wall_seconds"]
                ):
                    pause_reason = pause_reason or "startup-handshake-timeout"
                if s.get("absolute_deadline") is not None:
                    if wall >= s["absolute_deadline"]:
                        s.update(
                            phase="failed",
                            reason="Absolute training cutoff reached before terminal checkpoint",
                        )
                        break
                    if (
                        wall
                        >= s["absolute_deadline"] - limits["checkpoint_grace_seconds"]
                    ):
                        pause_reason = pause_reason or "absolute-cycle-deadline"
                if (job / "pause.json").exists():
                    pause_reason = pause_reason or "operator-pause"
                if now >= deadline:
                    pause_reason = pause_reason or "wall-time-budget"
                if (
                    s["started_epoch"]
                    and wall - (s["last_progress_epoch"] or started)
                    > limits["progress_timeout_seconds"]
                ):
                    pause_reason = pause_reason or "progress-stalled"
                if inference_busy(self.state):
                    pause_reason = pause_reason or "inference-priority"
                if (out / "worker.log").exists() and (
                    out / "worker.log"
                ).stat().st_size > 32 * MiB:
                    pause_reason = pause_reason or "worker-log-limit"
                try:
                    host = host_sample(job)
                    cg = group_counters(props["ControlGroup"])
                    last_pids.update(cg["pids"])
                    gpu = (
                        gpu_sample(w["device"], cg["pids"])
                        if w["device"] != "cpu"
                        else None
                    )
                    if gpu:
                        last_gpu = gpu
                        if (
                            gpu["visible_gpu_pids"]
                            and gpu["worker_sm_pct"] > 0
                            and gpu["worker_measurement"] == "attributed"
                        ):
                            s["attributed_gpu_progress_observed"] = True
                    allow, reason, pct = budget.assess(limits, host, cg, gpu)
                    if reason not in (
                        "measuring",
                        "budget-idle",
                        "cpu-quota",
                        "gpu-duty-window",
                    ):
                        pause_reason = pause_reason or reason
                    if pct is not None:
                        s["mean_compute_pct"] = round(budget.mean, 3)
                    sample = {
                        "epoch": wall,
                        "measured_compute_pct": pct,
                        "mean_compute_pct": s["mean_compute_pct"],
                        "worker_ram_bytes": cg["memory_bytes"],
                        "worker_oom": cg["cgroup_oom_kills"],
                        "reason": reason,
                        "host": host,
                        "gpu": gpu,
                    }
                    with (job / "resources.jsonl").open("a") as f:
                        f.write(json.dumps(sample, separators=(",", ":")) + "\n")
                    s["resources"] = sample
                    s["peak_worker_ram_bytes"] = max(
                        s.get("peak_worker_ram_bytes", 0), cg["memory_bytes"]
                    )
                    s["min_free_disk_bytes"] = min(
                        s.get("min_free_disk_bytes", host["available_disk_bytes"]),
                        host["available_disk_bytes"],
                    )
                    if gpu:
                        s["peak_worker_vram_mib"] = max(
                            s.get("peak_worker_vram_mib", 0), gpu["worker_vram_mib"]
                        )
                except (
                    OSError,
                    ValueError,
                    KeyError,
                    subprocess.SubprocessError,
                ) as exc:
                    allow = False
                    pause_reason = pause_reason or "telemetry-unavailable"
                    s["telemetry_error"] = str(exc)[:200]
                if pause_reason:
                    pause_deadline = (
                        pause_deadline or now + limits["checkpoint_grace_seconds"]
                    )
                    s.update(phase="pausing", reason=pause_reason)
                    atomic_json(
                        control,
                        {
                            "action": "checkpoint-and-exit",
                            "reason": pause_reason,
                            "job": id,
                            "permit_until_monotonic": 0,
                        },
                    )
                    if now >= pause_deadline:
                        s.update(
                            phase="failed",
                            reason="Worker exceeded checkpoint grace: " + pause_reason,
                        )
                        break
                else:
                    permit = 0
                    issued = time.monotonic()
                    if allow and s["started_epoch"]:
                        permit = issued + (
                            max(0, min(0.9, 1 - ((issued - budget.origin) % 10)))
                            if gpu
                            else 2
                        )
                    atomic_json(
                        control,
                        {
                            "action": "run" if permit else "wait",
                            "job": id,
                            "permit_until_monotonic": permit,
                        },
                    )
                atomic_json(path, s)
                time.sleep(0.4 if w["device"] != "cpu" else 1)
        except Exception as exc:  # noqa: BLE001 -- persist unexpected worker failures before always stopping its cgroup.
            s.update(phase="failed", reason=str(exc)[:500])
        finally:
            try:
                atomic_json(
                    control, {"action": "stop", "job": id, "permit_until_monotonic": 0}
                )
            except OSError as exc:
                s.update(
                    phase="failed",
                    reason=f"Control write failed; stopping owned worker: {exc}",
                )
            if worker_started:
                run(
                    ["systemctl", "--user", "stop", s["worker_unit"]],
                    check=False,
                    timeout=40,
                )
                post = unit_props(s["worker_unit"])
                stopped = post.get("ActiveState") not in (
                    "active",
                    "activating",
                    "deactivating",
                )
                s["worker_stop_verified"] = stopped
                if not stopped:
                    s.update(phase="failed", reason="Worker stop was not verified")
                if w["device"] != "cpu":
                    try:
                        final_gpu = gpu_sample(w["device"], last_pids)
                        s["worker_gpu_release_verified"] = bool(
                            last_gpu and not final_gpu["visible_gpu_pids"] and stopped
                        )
                        s["final_worker_vram_mib"] = final_gpu["worker_vram_mib"]
                    except (OSError, ValueError, subprocess.SubprocessError):
                        s["worker_gpu_release_verified"] = False
            s["ended_epoch"] = time.time()
            atomic_json(path, s)
        return s

    def check(self, id, scheduled=False):
        job = self.job(id)
        s = json_file(job / "status.json")
        result = health_report(s, job)
        if scheduled:
            require(
                s.get("monitor")
                and time.time() >= s["monitor"]["scheduled_epoch"] - 0.01,
                "No due, registered training health check",
            )
        atomic_json(
            job / ("hour-check.json" if scheduled else "inspection.json"), result
        )
        return result


def health_report(s, job):
    now = time.time()
    p = unit_props(s["worker_unit"])
    active = (
        p.get("SubState") == "running"
        and s.get("worker_pid")
        and int(p.get("MainPID") or 0) == s["worker_pid"]
    )
    cp = s.get("checkpoint")
    good_checkpoint = False
    if cp:
        try:
            good_checkpoint = (
                Path(cp["path"]).stat().st_size == cp["size"]
                and digest(cp["path"]) == cp["sha256"]
            )
        except OSError:
            pass
    if s["phase"] == "completed":
        health = "Completed" if good_checkpoint and s["steps"] else "Degraded"
    elif s["phase"] == "failed":
        health = "Failed"
    elif s["phase"] in ("paused", "pausing"):
        health = "Degraded"
    elif s["started_epoch"] is None:
        health = "Unknown"
    elif not active:
        health = "Failed"
    elif (
        (
            not s.get("last_progress_epoch")
            or now - s["last_progress_epoch"] > 300
            or not good_checkpoint
        )
        or s.get("mean_compute_pct") is None
        or s["mean_compute_pct"] > 13
        or s.get("device") != "cpu"
        and not s.get("attributed_gpu_progress_observed")
    ):
        health = "Degraded"
    else:
        health = "Healthy"
    log = job / "output/worker.log"
    tail = ""
    if log.exists():
        with log.open("rb") as f:
            f.seek(max(0, log.stat().st_size - 4096))
            tail = f.read().decode(errors="replace")
    errors = [
        line[:200]
        for line in tail.splitlines()
        if re.search(
            r"\b(oom|out of memory|error|exception|traceback)\b", line, re.IGNORECASE
        )
    ]
    if errors and health == "Healthy":
        health = "Degraded"
    gpu_now = None
    try:
        visible = run(
            [
                "nvidia-smi",
                "--query-gpu=uuid,name,utilization.gpu,memory.total,memory.used,temperature.gpu",
                "--format=csv,noheader,nounits",
            ],
            timeout=5,
        )
        gpu_now = [
            dict(
                zip(
                    ("uuid", "name", "sm_pct", "total_mib", "used_mib", "temp_c"),
                    [w.strip() for w in line.split(",")],
                    strict=True,
                )
            )
            for line in visible.stdout.splitlines()
        ]
    except (OSError, ValueError, subprocess.SubprocessError, RuntimeError):
        if s.get("device") != "cpu" and health == "Healthy":
            health = "Degraded"
    return {
        "schema": 1,
        "purpose": s.get("purpose", "real_training"),
        "gpu_now": gpu_now,
        "health": health,
        "id": s["id"],
        "actual_check_epoch": now,
        "scheduled_epoch": (s.get("monitor") or {}).get("scheduled_epoch"),
        "worker_pid": s["worker_pid"],
        "worker_alive": bool(active),
        "phase": s["phase"],
        "reason": s.get("reason"),
        "steps": s["steps"],
        "last_progress_epoch": s.get("last_progress_epoch"),
        "checkpoint_verified": good_checkpoint,
        "worker_log_path": str(log),
        "log_error_count_in_last_4k": len(errors),
        "latest_resources": s.get("resources"),
        "mean_compute_pct": s.get("mean_compute_pct"),
        "target_compute_pct": 10,
        "budget_observation": (
            "unmeasured"
            if s.get("mean_compute_pct") is None
            else "exceeded guard"
            if s["mean_compute_pct"] > 13
            else "within observed guard"
        ),
        "host_now": host_sample(job),
    }


def cycle_summaries(training_root):
    from training_cycle import list_cycles

    return list_cycles(Path(training_root).parent)


def training_status(state_dir):
    root = Path(state_dir) / "training"
    flows = []
    jobs = []
    for p in sorted((root / "workflows").glob("*.json")):
        try:
            r = json_file(p)
            w = r["manifest"]
            flows.append(
                {
                    "id": w["id"],
                    "model": w["model"],
                    "device": w["device"],
                    "sha256": r["digest"],
                }
            )
        except (OSError, KeyError, ValueError):
            continue
    for p in sorted(
        (root / "jobs").glob("*/status.json"),
        key=lambda f: f.stat().st_mtime_ns,
        reverse=True,
    )[:100]:
        try:
            j = json_file(p)
            if j["phase"] not in TERMINAL:
                unit = unit_props(j["supervisor_unit"])
                if unit.get("ActiveState") not in ("active", "activating"):
                    j.update(
                        phase="interrupted", reason="Supervisor is no longer running"
                    )
            hour = p.parent / "hour-check.json"
            j["hour_check"] = json_file(hour) if hour.exists() else None
            jobs.append(j)
        except (OSError, KeyError, ValueError):
            continue
    return {
        "state": "unconfigured" if not flows else "configured",
        "registered_workflows": flows,
        "cycles": cycle_summaries(root),
        "jobs": jobs,
        "monitor_registered": any(j.get("monitor") for j in jobs),
        "detail": "No independently configured training workflow is registered."
        if not flows
        else "Only explicitly launched independent workflows run.",
    }


def main():
    d = Path(
        os.environ.get("DULCHE_STATE_DIR")
        or Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state"))
        / "9to1/dulche"
    )
    a = argparse.ArgumentParser(description=__doc__)
    a.add_argument("--state", type=Path, default=d)
    sub = a.add_subparsers(dest="action", required=True)
    sub.add_parser("status")
    r = sub.add_parser("register")
    r.add_argument("manifest", type=Path)
    r.add_argument("--sha256", required=True)
    for verb in ("launch", "supervise", "resume", "pause", "check", "hour-check"):
        sub.add_parser(verb).add_argument("id")
    for action in ("launch", "resume"):
        sub.choices[action].add_argument(
            "--deadline", help="Absolute ISO wall-clock cutoff with timezone"
        )
    opt = a.parse_args()
    cutoff = None
    if getattr(opt, "deadline", None):
        from training_cycle import deadline

        cutoff = deadline(opt.deadline)

    m = TrainingJobs(opt.state)
    if opt.action == "status":
        result = training_status(opt.state)
    elif opt.action == "register":
        result = m.register(opt.manifest, opt.sha256)
    elif opt.action == "launch":
        result = m.launch(opt.id, cutoff=cutoff)
    elif opt.action == "resume":
        result = m.launch(
            json_file(m.job(opt.id) / "status.json")["workflow"], opt.id, cutoff=cutoff
        )
    elif opt.action == "supervise":
        result = m.supervise(opt.id)
    elif opt.action == "pause":
        result = m.pause(opt.id)
    else:
        result = m.check(opt.id, scheduled=opt.action == "hour-check")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (
        OSError,
        ValueError,
        RuntimeError,
        KeyError,
        subprocess.SubprocessError,
    ) as error:
        sys.exit("Dulche training: " + str(error))
