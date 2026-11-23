#!/usr/bin/python3
"""Persist and supervise training/review cycles around independently registered workers and evaluators."""

from __future__ import annotations

import argparse
import datetime as dt
import fcntl
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import time
import tempfile
import uuid
from urllib.parse import quote

from dulchectl import LocalHTTP
from engine_profiles import atomic_json
import training_jobs as jobs

SCHEMA = "dulche-training-cycle-v1"
TERMINAL = {"completed", "failed", "final_failed", "no_training"}
GATES = {"baseline", "review", "final"}


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def audit_artifact(ref):
    path = jobs.verified_artifact(ref)
    return {"path": str(path), "sha256": ref["sha256"]}


def deadline(value):
    parsed = dt.datetime.fromisoformat(value)
    jobs.require(
        parsed.utcoffset() is not None, "Deadline requires an explicit time zone"
    )
    return parsed.timestamp()


def mean(values, fallback=0.0):
    usable = [v for v in values if finite(v) and v >= 0]
    return sum(usable) / len(usable) if usable else fallback


def allocation(state, now):
    """Reserve observed review and transition costs. Bootstrap by one real update, never a fixed interval."""
    history = [r for r in state["rounds"] if r.get("optimizer_verified")]
    remaining = max(0.0, state["deadline_epoch"] - now)
    gate_time = max(
        state["baseline_seconds"] or 0, mean([r.get("review_seconds") for r in history])
    )
    transition = mean(
        [r.get("release_seconds", 0) + r.get("restore_seconds", 0) for r in history]
    )
    startup = mean([r.get("startup_seconds") for r in history])
    checkpoint = max(
        state["checkpoint_grace"], mean([r.get("checkpoint_seconds") for r in history])
    )
    runs_left = min(
        state["plan"]["max_cycles"] - len(state["rounds"]),
        max(1, state["plan"]["target_cycles"] - len(history)),
    )
    gains = [r["gain"] for r in history if finite(r.get("gain"))]
    if (
        len(gains) >= 2
        and gains[-1] < gains[-2]
        and runs_left < state["plan"]["max_cycles"] - len(state["rounds"])
    ):
        runs_left += 1
    rates = [
        r["optimizer_updates"] / r["update_seconds"]
        for r in history
        if r.get("update_seconds", 0) > 0
    ]
    rate = mean(rates)
    overhead = startup + checkpoint + transition + gate_time
    while (
        runs_left > 1
        and rate
        and remaining - gate_time < runs_left * (overhead + 1 / rate)
    ):
        runs_left -= 1
    useful = max(0.0, (remaining - gate_time) / max(1, runs_left) - overhead)
    target = max(1, math.floor(useful * rate)) if rate else 1
    return {
        "remaining_seconds": remaining,
        "cycles_budgeted": runs_left,
        "measured_updates_s": rate or None,
        "measured_gate_seconds": gate_time,
        "measured_transition_seconds": transition,
        "measured_startup_seconds": startup,
        "checkpoint_reserve_seconds": checkpoint,
        "useful_seconds": useful,
        "target_updates": target,
        "basis": "measured prior cycles"
        if rates
        else "calibrate with first verified optimizer update",
    }


def system_api(path, data=None):
    socket_path = os.environ.get("DULCHE_SOCKET") or str(
        Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
        / "9to1-dulche/api.sock"
    )
    conn = LocalHTTP(socket_path, 8)
    try:
        conn.request(
            "GET" if data is None else "POST",
            "/v1/" + path,
            None if data is None else json.dumps(data),
            {"Content-Type": "application/json"},
        )
        response = conn.getresponse()
        body = json.loads(response.read(4 * 1024 * 1024))
        if response.status >= 400:
            raise RuntimeError(
                f"HTTP {response.status} {str(body.get('error', body))[:260]}"
            )
        return body
    finally:
        conn.close()


def split_signatures(workflow):
    manifest = jobs.json_file(
        jobs.verified_artifact(workflow["dataset_manifest"]), limit=64 * 1024 * 1024
    )
    signatures = {}
    for split in ("train", "validation", "final_held_out"):
        files = manifest.get("files", [])
        hashes = sorted(row["sha256"] for row in files if row.get("split") == split)
        jobs.require(bool(hashes), f"Registered dataset lacks {split} evidence")
        signatures[split] = jobs.canonical_digest(hashes)
    jobs.require(
        len(set(signatures.values())) == 3, "Training and benchmark splits must differ"
    )
    return signatures


def validate_plan(raw, trainer):
    jobs.require(raw.get("schema") == SCHEMA, "Unsupported cycle plan")
    jobs.name(raw["id"])
    for key in ("objective", "mentor", "workflow", "validation_id", "heldout_id"):
        jobs.require(
            isinstance(raw.get(key), str) and raw[key].strip(), f"Missing {key}"
        )
    jobs.require(
        raw["validation_id"] != raw["heldout_id"], "Held-out identity must be separate"
    )
    models = raw.get("models")
    jobs.require(
        isinstance(models, list)
        and raw["mentor"] in models
        and len(models) == len(set(models))
        and all(isinstance(i, str) and i for i in models),
        "Declare exact conflicting model IDs including the mentor",
    )
    checks = raw.get("required_checks")
    jobs.require(
        isinstance(checks, list)
        and checks
        and len(set(checks)) == len(checks)
        and all(isinstance(k, str) and k for k in checks),
        "Declare independent quality/behavior checks",
    )
    target, maximum = raw.get("target_cycles"), raw.get("max_cycles")
    jobs.require(
        type(target) is int and type(maximum) is int and 2 <= target <= maximum <= 128,
        "Target at least two review cycles; maximum 128",
    )
    finish = deadline(raw["deadline"])
    jobs.require(finish > time.time(), "Training deadline already passed")
    if "gate_timeout_seconds" in raw:
        jobs.require(
            type(raw["gate_timeout_seconds"]) is int
            and raw["gate_timeout_seconds"] > 0,
            "Invalid independent gate runtime budget",
        )
    if "gate_executable" in raw:
        path = jobs.verified_artifact(raw["gate_executable"])
        jobs.require(
            os.access(path, os.X_OK), "Independent gate adapter is not executable"
        )
    w, sha = trainer.workflow(raw["workflow"])
    signatures = split_signatures(w)
    if raw.get("validation_benchmark"):
        signatures["validation"] = audit_artifact(raw["validation_benchmark"])["sha256"]
    if raw.get("final_benchmark"):
        signatures["final_held_out"] = audit_artifact(raw["final_benchmark"])["sha256"]
    jobs.require(
        len(set(signatures.values())) == 3,
        "Held-out benchmark cannot reuse the development benchmark",
    )
    if "first_window_owner" in raw:
        import re

        jobs.require(
            isinstance(raw["first_window_owner"], str)
            and re.fullmatch(
                r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}", raw["first_window_owner"]
            ),
            "Invalid first resource reservation owner",
        )
    return w, sha, signatures, finish


def unit_state(unit):
    p = jobs.run(
        ["systemctl", "--user", "show", unit, "-p", "LoadState", "-p", "ActiveState"],
        check=False,
        timeout=5,
    )
    fields = dict(line.split("=", 1) for line in p.stdout.splitlines() if "=" in line)
    if fields.get("LoadState") == "not-found":
        return "absent"
    jobs.require(fields.get("ActiveState"), "Cannot confirm user service state")
    return fields["ActiveState"]


class Cycle:
    def __init__(
        self,
        state,
        cycle_id,
        *,
        api=system_api,
        trainer=None,
        unit=unit_state,
        clock=time.time,
    ):
        self.state = Path(state).resolve()
        self.id = jobs.name(cycle_id)
        self.root = self.state / "training/cycles" / self.id
        self.file = self.root / "cycle.json"
        self.api, self.trainer, self.unit, self.clock = (
            api,
            trainer or jobs.TrainingJobs(state),
            unit,
            clock,
        )

    def save(self, d):
        atomic_json(self.file, d)

    def preserve(self, ref):
        original = jobs.verified_artifact(ref)
        store = self.root / "artifacts"
        store.mkdir(mode=0o700, exist_ok=True)
        target = store / ref["sha256"]
        if not target.exists():
            size = original.stat().st_size
            jobs.require(
                jobs.host_sample(store)["available_disk_bytes"] >= 50 * jobs.GiB + size,
                "Insufficient disk for gate evidence",
            )
            jobs.custody_path()
            from custody.artifacts import _open_regular, _write_atomic

            try:
                with _open_regular(original) as stream:
                    _write_atomic(
                        target, stream, max_bytes=size, expected=(ref["sha256"], size)
                    )
            except FileExistsError:
                pass
        return audit_artifact({"path": str(target), "sha256": ref["sha256"]})

    def event(self, d, phase, reason, **details):
        stamp = self.clock()
        d["phase"], d["phase_epoch"], d["reason"] = phase, stamp, reason
        d["events"].append({"time": stamp, "phase": phase, "reason": reason, **details})
        self.save(d)

    def create(self, plan, sha):
        jobs.require(jobs.digest(plan) == sha, "Cycle plan digest mismatch")
        raw = jobs.json_file(plan)
        jobs.require(raw["id"] == self.id, "Cycle id mismatch")
        w, identity, splits, finish = validate_plan(raw, self.trainer)
        self.root.mkdir(mode=0o700, parents=True, exist_ok=False)
        now = self.clock()
        d = {
            "schema": SCHEMA,
            "id": self.id,
            "owner": uuid.uuid4().hex,
            "plan": raw,
            "plan_sha256": sha,
            "workflow_sha256": identity,
            "deadline_epoch": finish,
            "splits": splits,
            "checkpoint_grace": w["limits"]["checkpoint_grace_seconds"],
            "created_epoch": now,
            "gate_timeout": raw.get(
                "gate_timeout_seconds", w["limits"]["progress_timeout_seconds"]
            ),
            "baseline_seconds": None,
            "best": None,
            "best_score": None,
            "context": None,
            "directives": None,
            "rounds": [],
            "events": [],
            "active": None,
            "paused": False,
        }
        self.event(
            d,
            "baseline",
            "Awaiting independent baseline benchmark, mentor context and training plan",
        )
        return d

    def control(self, action):
        jobs.require(action in {"pause", "resume", "deadline"}, "Unknown cycle control")
        d = jobs.json_file(self.file)
        if d["phase"] in TERMINAL:
            return d
        if action == "deadline":
            jobs.require(
                self.clock() >= d["deadline_epoch"], "Absolute deadline not reached"
            )
        if action in ("pause", "deadline"):
            atomic_json(self.root / (action + ".json"), {"time": self.clock()})
            active = d.get("active") or {}
            attempt = active.get("attempt")
            if (
                attempt
                and (self.trainer.root / "jobs" / attempt / "status.json").exists()
            ):
                status = jobs.json_file(self.trainer.job(attempt) / "status.json")
                jobs.require(
                    jobs.cycle_owns(self.trainer.root, status, self.id),
                    "Unexpected training attempt owner",
                )
                self.trainer.pause(attempt)
                if action == "deadline" and status["phase"] not in jobs.TERMINAL:
                    jobs.require(
                        status["worker_unit"]
                        == "dulche-training-" + attempt + "-worker.service",
                        "Unexpected worker unit",
                    )
                    if self.unit(status["worker_unit"]) in (
                        "activating",
                        "active",
                        "reloading",
                    ):
                        jobs.run(
                            [
                                "systemctl",
                                "--user",
                                "stop",
                                "--no-block",
                                status["worker_unit"],
                            ]
                        )
        else:
            (self.root / "pause.json").unlink(missing_ok=True)
        return {"id": self.id, "request": action, "phase": d["phase"]}

    def stop_proved(self, j):
        for name_ in ("worker_unit", "supervisor_unit"):
            if self.unit(j[name_]) not in ("absent", "inactive", "failed"):
                return False
        if j.get("worker_pid") is not None:
            if j.get("worker_stop_verified") is not True:
                return False
            if j["device"] != "cpu" and (
                j.get("worker_gpu_release_verified") is not True
                or j.get("final_worker_vram_mib") != 0
            ):
                return False
        return True

    def stage_dir(self, d):
        return self.root / "gates" / f"{len(d['rounds']):04d}-{d['phase']}"

    def request(self, d):
        folder = self.stage_dir(d)
        folder.mkdir(mode=0o700, parents=True, exist_ok=True)
        target = folder / "request.json"
        if target.exists():
            return jobs.json_file(target)
        phase = d["phase"]
        checkpoint = d["active"].get("checkpoint") if phase == "review" else d["best"]
        gate = {
            "schema": "dulche-cycle-gate-v1",
            "cycle": self.id,
            "request_id": uuid.uuid4().hex,
            "stage": phase,
            "round": len(d["rounds"]),
            "created_epoch": self.clock(),
            "mentor": d["plan"]["mentor"],
            "objective": d["plan"]["objective"],
            "workflow": d["plan"]["workflow"],
            "workflow_sha256": d["workflow_sha256"],
            "benchmark_id": d["plan"][
                "heldout_id" if phase == "final" else "validation_id"
            ],
            "benchmark_sha256": d["splits"][
                "final_held_out" if phase == "final" else "validation"
            ],
            "checkpoint": checkpoint,
            "best_score": d["best_score"],
            "mentor_context": d["context"],
            "required_checks": d["plan"]["required_checks"],
            "deadline_epoch": d["deadline_epoch"],
        }
        if phase == "final" and d["plan"].get("heldout_pending"):
            reserved = d.get("heldout_reservation")
            jobs.require(reserved, "A separate held-out reservation is required")
            gate.update(
                benchmark_id=reserved["benchmark_id"],
                benchmark_sha256=reserved["artifact"]["sha256"],
                benchmark_artifact=reserved["artifact"],
            )
        elif phase != "final" and d["plan"].get("validation_benchmark"):
            gate["benchmark_artifact"] = d["plan"]["validation_benchmark"]
        if phase == "review":
            gate["attempt"] = d["active"]["attempt"]
            gate["optimizer_updates_expected"] = d["active"]["optimizer_updates"]
        atomic_json(target, gate)
        return gate

    def receipt(self, d, gate):
        folder = self.stage_dir(d)
        path = folder / "receipt.json"
        if path.exists():
            return jobs.json_file(path)
        configured = d["plan"].get("gate_executable")
        if not configured:
            return None
        exe = jobs.verified_artifact(configured)
        marker = folder / "dispatched.json"
        unit = f"dulche-gate-{gate['request_id']}"
        if marker.exists():
            if self.clock() - jobs.json_file(marker)["at"] > 10 and self.unit(
                unit + ".service"
            ) in ("failed", "inactive", "absent"):
                raise ValueError(
                    "Independent gate returned no receipt; submit a matching independent receipt or explicitly retry the gate"
                )
            return None
        atomic_json(marker, {"unit": unit, "at": self.clock()})
        gate_limit = d["gate_timeout"]
        if gate["stage"] != "final" and self.clock() < d["deadline_epoch"]:
            gate_limit = min(
                gate_limit, max(1, int(d["deadline_epoch"] - self.clock()))
            )
        jobs.run(
            [
                "systemd-run",
                "--user",
                "--collect",
                "--unit=" + unit,
                "-p",
                "Type=exec",
                "-p",
                "Nice=10",
                "-p",
                f"RuntimeMaxSec={gate_limit}s",
                "-p",
                "KillMode=control-group",
                "-p",
                "NoNewPrivileges=yes",
                "-p",
                "ProtectSystem=strict",
                "-p",
                "ProtectHome=read-only",
                "-p",
                "ReadWritePaths=" + jobs.unit_argument(folder),
                str(exe),
                str(folder / "request.json"),
                str(path),
            ]
        )
        return None

    def check_receipt(self, d, gate, r):
        for k in ("request_id", "stage", "cycle", "benchmark_id", "benchmark_sha256"):
            jobs.require(r.get(k) == gate[k], f"Gate receipt {k} mismatch")
        expected = (gate.get("checkpoint") or {}).get("sha256")
        jobs.require(
            "checkpoint_sha256" in r and r["checkpoint_sha256"] == expected,
            "Gate evaluated a different checkpoint",
        )
        jobs.require(
            finite(r.get("score")), "Missing finite independent benchmark score"
        )
        audited = self.preserve(r["evidence"])
        checks = r.get("checks")
        jobs.require(
            isinstance(checks, dict)
            and all(
                k in checks
                and (
                    type(checks[k]) is bool
                    or (gate["stage"] == "baseline" and checks[k] is None)
                )
                for k in gate["required_checks"]
            ),
            "Independent behavior/regression checks are absent",
        )
        result = {
            "score": r["score"],
            "checks": checks,
            "evidence": audited,
            "all_passed": all(checks[k] for k in gate["required_checks"]),
        }
        if gate["stage"] != "final":
            result["context"] = self.preserve(r["mentor_context"])
            if r.get("training_directives"):
                result["directives"] = self.preserve(r["training_directives"])
        if gate["stage"] == "review":
            proof = r.get("optimizer", {})
            jobs.require(
                proof.get("updates") == gate["optimizer_updates_expected"]
                and proof["updates"] > 0,
                "Optimizer-update count differs from supervised progress",
            )
            jobs.require(
                jobs.DIGEST.fullmatch(str(proof.get("optimizer_state_sha256", ""))),
                "Missing independent inspection of the optimizer state in this checkpoint",
            )
            if proof.get("parameter_before_sha256") or proof.get(
                "parameter_after_sha256"
            ):
                for key in ("parameter_before_sha256", "parameter_after_sha256"):
                    jobs.require(
                        jobs.DIGEST.fullmatch(str(proof.get(key, ""))),
                        "Missing independently inspected parameter digest",
                    )
                jobs.require(
                    proof["parameter_before_sha256"] != proof["parameter_after_sha256"],
                    "No verified parameter change",
                )
            else:
                log = (
                    self.trainer.job(gate["attempt"])
                    / "output/optimizer-evidence.jsonl"
                )
                jobs.require(
                    log.is_file(),
                    "Missing genuine per-step parameter-change measurements",
                )
                lo = d["active"]["prior_steps"]
                hi = gate["checkpoint"]["steps"]
                steps = set()
                with log.open() as stream:
                    for line in stream:
                        row = json.loads(line)
                        if (
                            type(row.get("step")) is int
                            and lo < row["step"] <= hi
                            and finite(row.get("max_abs_update"))
                            and row["max_abs_update"] > 0
                            and row.get("parameter")
                        ):
                            steps.add(row["step"])
                jobs.require(
                    len(steps) == proof["updates"],
                    "Optimizer log does not prove all reported updates",
                )
                result["optimizer_evidence"] = self.preserve(
                    {"path": str(log), "sha256": jobs.digest(log)}
                )
            result["optimizer"] = proof
            jobs.require(
                r.get("decision") in ("continue", "finish", "rollback"),
                "Missing mentor review decision",
            )
        if gate["stage"] == "final":
            jobs.require(
                r.get("heldout_previously_unseen") is True,
                "Missing independent held-out isolation attestation",
            )
        return result

    def gate(self, d):
        phase = d["phase"]
        if (
            phase == "final"
            and d["plan"].get("heldout_pending")
            and not d.get("heldout_reservation")
        ):
            raise ValueError(
                "Independent held-out benchmark has not been reserved; final assessment is blocked"
            )
        if not (self.stage_dir(d) / "receipt.json").is_file():
            model = self.api("models/" + quote(d["plan"]["mentor"], safe=""))
            if model.get("manual_suspended"):
                raise ValueError("Mentor is manually suspended; waiting for its owner")
            if model.get("state") != "loaded":
                if model.get("state") == "unloaded" and not d.get(
                    "mentor_load_requested"
                ):
                    self.api(
                        "models/" + quote(d["plan"]["mentor"], safe="") + "/load", {}
                    )
                    d["mentor_load_requested"] = True
                    self.save(d)
                return
            d["mentor_load_requested"] = False
        gate = self.request(d)
        r = self.receipt(d, gate)
        if r is None:
            return
        result = self.check_receipt(d, gate, r)
        elapsed = max(0, self.clock() - gate["created_epoch"])
        if phase == "baseline":
            d.update(
                best_score=result["score"],
                baseline_seconds=elapsed,
                baseline=result,
                context=result["context"],
                directives=result.get("directives"),
            )
            self.event(
                d, "plan", "Independent baseline recorded and mentor context pinned"
            )
        elif phase == "final":
            d["final"] = result
            passed = (
                result["all_passed"]
                and any(x.get("optimizer_verified") for x in d["rounds"])
                and d.get("best") is not None
            )
            self.event(
                d,
                "completed" if passed else "final_failed",
                "Held-out assessment recorded",
                passed=passed,
            )
        else:
            active = d["active"]
            previous = d["best_score"]
            accepted = (
                result["all_passed"]
                and result["score"] >= previous
                and r["decision"] != "rollback"
            )
            if accepted:
                d["best"] = active["checkpoint"]
                d["best_score"] = result["score"]
            active.update(
                review=result,
                review_seconds=elapsed,
                gain=result["score"] - previous,
                accepted=accepted,
                optimizer_verified=True,
            )
            d["rounds"].append(active)
            d["active"] = None
            d["context"] = result["context"]
            if result.get("directives"):
                d["directives"] = result["directives"]
            final = (
                r["decision"] == "finish"
                or len(d["rounds"]) >= d["plan"]["max_cycles"]
                or self.clock() >= d["deadline_epoch"]
            )
            self.event(
                d,
                "final" if final else "plan",
                "Independent mentor review completed; "
                + ("checkpoint accepted" if accepted else "prior best retained"),
            )

    def stop_before(self, d):
        return (
            self.clock() >= d["deadline_epoch"]
            or (self.root / "deadline.json").exists()
        )

    def hold(self, d, phase, why):
        d["resume_phase"] = phase
        self.event(d, "paused", why)

    def after_abort(self, d, why):
        d.setdefault("incomplete_attempts", []).append({**d["active"], "reason": why})
        d["active"] = None
        if self.stop_before(d):
            self.event(
                d, "final" if d["best"] else "no_training", "Training cutoff; " + why
            )
        else:
            atomic_json(self.root / "pause.json", {"time": self.clock(), "reason": why})
            self.hold(d, "plan", why)

    def run_step(self, d):
        now = self.clock()
        phase = d["phase"]
        paused = (self.root / "pause.json").exists()
        expired = self.stop_before(d)
        if phase in TERMINAL:
            return
        if phase == "paused":
            if expired and d["resume_phase"] in ("baseline", "plan"):
                self.event(
                    d,
                    "final" if d["best"] else "no_training",
                    "Deadline elapsed while paused",
                )
            elif not paused:
                self.event(d, d["resume_phase"], "Operator resumed")
            return
        if phase in GATES:
            if phase == "baseline" and expired:
                self.event(
                    d,
                    "no_training",
                    "Deadline elapsed before an independent baseline was recorded",
                )
            elif paused:
                self.hold(d, phase, "Operator paused; no additional training admitted")
            else:
                self.gate(d)
            return
        if phase == "plan":
            if paused:
                self.hold(d, "plan", "Operator paused")
                return
            estimate = allocation(d, now)
            if (
                expired
                or estimate["cycles_budgeted"] <= 0
                or estimate["useful_seconds"] <= 0
            ):
                self.event(
                    d,
                    "final" if d["best"] else "no_training",
                    "Insufficient budget for another update, verified save, restoration and assessment",
                    allocation=estimate,
                )
                return
            attempt = uuid.uuid4().hex
            d["active"] = {
                "attempt": attempt,
                "window_owner": (
                    d["plan"].get("first_window_owner")
                    if not d["rounds"] and not d.get("incomplete_attempts")
                    else None
                )
                or str(uuid.uuid4()),
                "allocation": estimate,
                "requested_epoch": now,
                "parent": (d["best"] or {}).get("attempt"),
                "prior_steps": (d["best"] or {}).get("steps", 0),
                "context": audit_artifact(d["context"]),
                "directives": audit_artifact(d["directives"])
                if d["directives"]
                else None,
            }
            self.event(
                d,
                "acquire",
                "Mentor context pinned; requesting exclusive release of declared inference instances",
            )
            return
        a = d["active"]
        owner = a["window_owner"]
        if phase == "acquire":
            if expired or paused:
                window = self.api("resources/training-window")
                if window.get("owner") != owner or window.get("phase") in (
                    "restored",
                    "idle",
                ):
                    self.after_abort(d, "No training window or worker started")
                else:
                    a["reason"] = "Training stopped before worker admission"
                    self.event(d, "restore", "Reversing this cycle's acquired window")
                return
            window = self.api(
                "resources/training-window/acquire",
                {"owner": owner, "models": d["plan"]["models"]},
            )
            jobs.require(
                window.get("owner") == owner, "Unexpected training window owner"
            )
            a["resource_window"] = window
            if window.get("phase") == "held":
                supplied = {m.get("id") for m in window.get("models", [])}
                jobs.require(
                    set(d["plan"]["models"]) == supplied,
                    "Reservation did not account for every requested model",
                )
                for m in window["models"]:
                    if m.get("prior_state") == "loaded":
                        jobs.require(
                            (m.get("release") or {}).get("release_verified") is True,
                            "Inference allocation release is not independently verified",
                        )
                a["release_seconds"] = now - a["requested_epoch"]
                self.event(
                    d,
                    "launch",
                    "Exclusive window verified; inference release receipts recorded",
                )
            elif window.get("phase") == "blocked":
                raise ValueError(
                    "Inference reservation blocked: " + str(window.get("error"))
                )
            else:
                self.save(d)
            return
        if phase == "launch":
            attempt_path = self.trainer.root / "jobs" / a["attempt"] / "status.json"
            if (expired or paused) and not attempt_path.exists():
                a["reason"] = "Training stopped before worker admission"
                self.event(d, "restore", "No worker started")
                return
            current = self.api("resources/training-window")
            jobs.require(
                current.get("owner") == owner
                and current.get("phase") == "held"
                and current.get("release_valid") is True,
                "Training release is not currently verified; refusing launch",
            )
            try:
                j = self.trainer.launch(
                    d["plan"]["workflow"],
                    a["parent"],
                    cycle=self.id,
                    attempt=a["attempt"],
                    cutoff=d["deadline_epoch"],
                    directives=a.get("directives"),
                )
            except (ValueError, OSError, RuntimeError, subprocess.SubprocessError) as e:
                if not attempt_path.exists():
                    a["reason"] = "Worker admission failed: " + str(e)[:400]
                    self.event(
                        d, "restore", "Admission failed before any worker was created"
                    )
                    return
                j = jobs.json_file(attempt_path)
            jobs.require(
                jobs.cycle_owns(self.trainer.root, j, self.id),
                "Trainer ownership mismatch",
            )
            a["launch_epoch"] = j.get("created_epoch", now)
            self.event(
                d,
                "training",
                "Independent worker attempt registered",
                attempt=a["attempt"],
            )
            return
        if phase == "training":
            j = jobs.json_file(self.trainer.job(a["attempt"]) / "status.json")
            jobs.require(
                jobs.cycle_owns(self.trainer.root, j, self.id),
                "Trainer ownership mismatch",
            )
            delta = j["steps"] - a["prior_steps"]
            started = j.get("ready_epoch") or j.get("started_epoch")
            a["live"] = {
                k: j.get(k)
                for k in (
                    "phase",
                    "steps",
                    "last_progress_epoch",
                    "mean_compute_pct",
                    "resources",
                    "reason",
                    "worker_pid",
                )
            }
            if j["phase"] not in jobs.TERMINAL:
                try:
                    window = self.api("resources/training-window")
                    idle = (
                        window.get("owner") == owner
                        and window.get("phase") == "held"
                        and window.get("release_valid") is True
                        and not window.get("direct_inference_waiting")
                        and not window.get("broker_inference_waiting")
                    )
                except (OSError, ValueError, RuntimeError):
                    idle = False
                if not idle:
                    a["interruption"] = (
                        "Inference requested or training window could not prove continued exclusivity"
                    )
                if (
                    not idle
                    or expired
                    or paused
                    or (
                        started
                        and (
                            delta > 0
                            or a["allocation"]["measured_updates_s"] is not None
                        )
                        and now - started >= a["allocation"]["useful_seconds"]
                    )
                    or delta >= a["allocation"]["target_updates"]
                ):
                    if not a.get("pause_epoch"):
                        a["pause_epoch"] = now
                        self.save(d)
                    self.trainer.pause(a["attempt"])
                if (
                    self.unit(j["supervisor_unit"]) in ("inactive", "failed", "absent")
                    and now - a["launch_epoch"] > 15
                ):
                    raise ValueError(
                        "Supervisor interrupted before recording trainer stop/release evidence; retain the window for recovery"
                    )
                self.save(d)
                return
            if not self.stop_proved(j):
                self.save(d)
                return
            a["stopped_epoch"] = now
            a["stop_evidence"] = {
                k: j.get(k)
                for k in (
                    "worker_unit",
                    "supervisor_unit",
                    "worker_stop_verified",
                    "worker_gpu_release_verified",
                    "final_worker_vram_mib",
                    "device",
                    "ended_epoch",
                )
            }
            cp = j.get("checkpoint")
            if (
                j["phase"] in ("completed", "paused")
                and delta > 0
                and cp
                and cp["steps"] == j["steps"]
            ):
                try:
                    pinned = audit_artifact(cp)
                except (ValueError, OSError) as e:
                    a["reason"] = "Unverifiable checkpoint: " + str(e)[:300]
                else:
                    a.update(
                        checkpoint={
                            **pinned,
                            "steps": cp["steps"],
                            "attempt": a["attempt"],
                        },
                        optimizer_updates=delta,
                        startup_seconds=max(0, (started or now) - a["launch_epoch"]),
                        update_seconds=max(
                            0,
                            (j.get("last_progress_epoch") or now)
                            - (started or a["launch_epoch"]),
                        ),
                        checkpoint_seconds=max(
                            0,
                            now
                            - (
                                a.get("pause_epoch")
                                or j.get("last_progress_epoch")
                                or now
                            ),
                        ),
                    )
            else:
                a["reason"] = "No new verified terminal checkpoint: " + str(
                    j.get("reason")
                )
            self.event(
                d,
                "restore",
                "Trainer stopped; own resource release checked; restoring inference",
            )
            return
        if phase == "restore":
            jpath = self.trainer.root / "jobs" / a["attempt"] / "status.json"
            if jpath.exists() and not self.stop_proved(jobs.json_file(jpath)):
                raise ValueError("Trainer stop or GPU release is not yet verified")
            restored = self.api("resources/training-window/release", {"owner": owner})
            jobs.require(restored.get("owner") == owner, "Unexpected restoration owner")
            a["restoration"] = restored
            if restored.get("phase") == "restored":
                a["restore_seconds"] = max(0, now - d["phase_epoch"])
                if not a.get("checkpoint"):
                    self.after_abort(d, a.get("reason", "No checkpoint to assess"))
                elif paused:
                    self.hold(
                        d,
                        "review",
                        "Resources restored; checkpoint awaits independent review",
                    )
                else:
                    self.event(
                        d,
                        "review",
                        "Resources restored; independent mentor must inspect optimizer and specialist",
                    )
            elif restored.get("phase") == "blocked":
                raise ValueError(
                    "Inference restoration blocked: " + str(restored.get("error"))
                )
            else:
                self.save(d)
            return
        raise ValueError("Unknown cycle phase")

    def tick(self):
        with (self.root / "driver.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            d = jobs.json_file(self.file, limit=16 * 1024 * 1024)
            try:
                self.run_step(d)
                if d.pop("last_error", None) is not None:
                    self.save(d)
            except (
                ValueError,
                KeyError,
                TypeError,
                OSError,
                RuntimeError,
                subprocess.SubprocessError,
            ) as e:
                msg = str(e)[:600]
                if d.get("last_error") != msg:
                    d["last_error"] = msg
                    d["events"].append(
                        {"time": self.clock(), "phase": d["phase"], "error": msg}
                    )
                    self.save(d)
            return d

    def adopt(self, attempt, owner):
        """Join an existing, explicitly named first-window attempt. Never start or restart a worker."""
        with (self.root / "driver.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            d = jobs.json_file(self.file)
            active = d.get("active")
            if (
                active
                and active.get("attempt") == attempt
                and active.get("window_owner") == owner
            ):
                return {"cycle": self.id, "attempt": attempt, "already_adopted": True}
            jobs.require(
                d["phase"] == "plan"
                and not d["rounds"]
                and not active
                and d.get("baseline"),
                "Adoption requires a recorded baseline and no prior attempt",
            )
            jobs.require(
                owner == d["plan"].get("first_window_owner"),
                "First reservation owner differs from plan",
            )
            window = self.api("resources/training-window")
            jobs.require(
                window.get("owner") == owner
                and window.get("phase") == "held"
                and window.get("release_valid") is True,
                "Cannot adopt without the exact active physically verified training window",
            )
            jobs.require(
                set(d["plan"]["models"])
                == {m.get("id") for m in window.get("models", [])},
                "Window resource set differs from cycle",
            )
            p = self.trainer.job(jobs.name(attempt))
            j = jobs.json_file(p / "status.json")
            jobs.require(
                j.get("id") == attempt
                and j.get("workflow") == d["plan"]["workflow"]
                and j.get("workflow_digest") == d["workflow_sha256"]
                and not j.get("resume_of")
                and j.get("absolute_deadline") == d["deadline_epoch"]
                and j.get("cycle") in (None, self.id),
                "Existing attempt has a different workflow, deadline or owner",
            )
            jobs.require(
                j.get("phase")
                in (
                    "requested",
                    "starting",
                    "running",
                    "pausing",
                    "paused",
                    "completed",
                    "failed",
                ),
                "Existing attempt phase is unsupported",
            )
            binding = {
                "cycle": self.id,
                "attempt": attempt,
                "workflow_digest": d["workflow_sha256"],
                "absolute_deadline": d["deadline_epoch"],
            }
            marker = p / "cycle-owner.json"
            # Atomic create-if-absent permits exactly one cycle to bind this independent attempt.
            fd, tmp = tempfile.mkstemp(dir=p, prefix=".owner-")
            try:
                with os.fdopen(fd, "w") as stream:
                    json.dump(binding, stream)
                    stream.flush()
                    os.fsync(stream.fileno())
                try:
                    os.link(tmp, marker)
                except FileExistsError:
                    jobs.require(
                        jobs.json_file(marker) == binding,
                        "Training attempt is already bound to another owner",
                    )
            finally:
                Path(tmp).unlink(missing_ok=True)
            d["active"] = {
                "attempt": attempt,
                "window_owner": owner,
                "allocation": allocation(d, self.clock()),
                "requested_epoch": window.get("created_epoch", j["created_epoch"]),
                "launch_epoch": j["created_epoch"],
                "parent": None,
                "prior_steps": 0,
                "context": audit_artifact(d["context"]),
                "directives": d.get("directives"),
                "resource_window": window,
                "adopted_existing": True,
            }
            self.event(
                d,
                "training",
                "Existing admitted first-window worker bound without relaunch",
                attempt=attempt,
            )
            return {
                "cycle": self.id,
                "attempt": attempt,
                "phase": "training",
                "worker_started_by_cycle": False,
            }

    def reserve_final(self, manifest, checksum):
        jobs.require(
            jobs.digest(manifest) == checksum,
            "Held-out custody receipt digest mismatch",
        )
        supplied = jobs.json_file(manifest)
        with (self.root / "driver.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            d = jobs.json_file(self.file)
            jobs.require(
                d["plan"].get("heldout_pending") and d["phase"] not in TERMINAL,
                "This cycle does not admit a new final benchmark",
            )
            if d.get("heldout_reservation"):
                jobs.require(
                    d["heldout_reservation"]["receipt_sha256"] == checksum,
                    "The final reservation is immutable",
                )
                return d["heldout_reservation"]
            jobs.require(
                supplied.get("cycle") == self.id
                and supplied.get("previously_unseen") is True
                and isinstance(supplied.get("independent_source"), str)
                and supplied["independent_source"].strip(),
                "Missing independent final benchmark custody attestation",
            )
            ref = self.preserve(supplied["artifact"])
            jobs.require(
                supplied.get("benchmark_id")
                and supplied["benchmark_id"] != d["plan"]["validation_id"]
                and ref["sha256"]
                not in (d["splits"]["train"], d["splits"]["validation"]),
                "The final benchmark cannot reuse development or training identity",
            )
            w, _ = self.trainer.workflow(d["plan"]["workflow"])
            dataset = jobs.json_file(
                jobs.verified_artifact(w["dataset_manifest"]), limit=64 * 1024 * 1024
            )
            jobs.require(
                ref["sha256"]
                not in {
                    r["sha256"]
                    for r in dataset["files"]
                    if r["split"] in ("train", "validation")
                },
                "Final evaluation bytes were already in a training or validation file",
            )
            record = {
                "receipt_sha256": checksum,
                "reserved_epoch": self.clock(),
                "benchmark_id": supplied["benchmark_id"],
                "artifact": ref,
                "receipt": self.preserve(
                    {"path": str(manifest.resolve()), "sha256": checksum}
                ),
                "before_first_worker": not d["rounds"]
                and not (d.get("active") or {}).get("launch_epoch"),
            }
            d["heldout_reservation"] = record
            self.save(d)
            return record

    def import_baseline(self, report, context, score_field, benchmark_field):
        """Wrap independent pre-existing benchmark evidence without inventing pass/fail outcomes."""
        d = jobs.json_file(self.file)
        jobs.require(
            d["phase"] == "baseline" and d["plan"].get("validation_benchmark"),
            "An imported baseline must bind an explicitly pinned development benchmark",
        )
        gate = self.request(d)

        def field(document, path):
            for k in path.split("."):
                document = document[k]
            return document

        observed = jobs.json_file(report, limit=64 * 1024 * 1024)
        jobs.require(
            field(observed, benchmark_field) == gate["benchmark_sha256"],
            "The existing report measured a different evaluation set",
        )
        receipt = {
            k: gate[k]
            for k in (
                "request_id",
                "cycle",
                "stage",
                "benchmark_id",
                "benchmark_sha256",
            )
        }
        receipt.update(
            checkpoint_sha256=None,
            score=field(observed, score_field),
            checks={k: None for k in gate["required_checks"]},
            evidence={"path": str(report.resolve()), "sha256": jobs.digest(report)},
            mentor_context={
                "path": str(context.resolve()),
                "sha256": jobs.digest(context),
            },
        )
        self.check_receipt(d, gate, receipt)
        with tempfile.NamedTemporaryFile(mode="w", dir=self.root, suffix=".json") as f:
            json.dump(receipt, f)
            f.flush()
            return self.submit(Path(f.name))

    def retry_gate(self):
        with (self.root / "driver.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            d = jobs.json_file(self.file)
            jobs.require(d["phase"] in GATES, "No independent gate is pending")
            folder = self.stage_dir(d)
            gate = jobs.json_file(folder / "request.json")
            marker = folder / "dispatched.json"
            if marker.exists():
                jobs.require(
                    self.unit(jobs.json_file(marker)["unit"] + ".service")
                    in ("absent", "inactive", "failed"),
                    "The previous independent gate handler is still active",
                )
            receipt = folder / "receipt.json"
            if receipt.exists():
                try:
                    self.check_receipt(d, gate, jobs.json_file(receipt))
                except (ValueError, TypeError, KeyError, OSError):
                    receipt.rename(folder / ("rejected-" + uuid.uuid4().hex + ".json"))
                else:
                    raise ValueError("A valid receipt is already awaiting consumption")
            marker.unlink(missing_ok=True)
            return {
                "cycle": self.id,
                "request_id": gate["request_id"],
                "retry_requested": True,
            }

    def submit(self, source):
        with (self.root / "driver.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            d = jobs.json_file(self.file)
            jobs.require(d["phase"] in GATES, "Cycle is not awaiting a gate")
            gate = self.request(d)
            value = jobs.json_file(source)
            self.check_receipt(d, gate, value)
            path = self.stage_dir(d) / "receipt.json"
            if path.exists():
                jobs.require(
                    jobs.json_file(path) == value,
                    "A different receipt is already recorded",
                )
            else:
                atomic_json(path, value)
            return {
                "cycle": self.id,
                "stage": d["phase"],
                "request_id": gate["request_id"],
                "submitted": True,
            }

    def start(self):
        d = jobs.json_file(self.file)
        jobs.require(
            d["phase"] not in TERMINAL and not self.stop_before(d),
            "Cycle is terminal or expired",
        )
        root = (
            Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config"))
            / "systemd/user"
        )
        root.mkdir(parents=True, exist_ok=True)
        base = "dulche-cycle-" + d["owner"]
        driver = jobs.unit_argument(str(Path(__file__).resolve()))
        command = f"{jobs.unit_argument(sys.executable)} -B {driver} --state {jobs.unit_argument(self.state)}"
        when = dt.datetime.fromtimestamp(d["deadline_epoch"], dt.UTC).strftime(
            "%Y-%m-%d %H:%M:%S UTC"
        )
        service = (
            "[Unit]\nDescription=Dulche independent training cycle\n[Service]\nType=simple\nNice=10\n"
            f"Restart=on-failure\nRestartSec=5\nExecStart={command} supervise {self.id}\n[Install]\nWantedBy=default.target\n"
        )
        timer = f"[Unit]\nDescription=Dulche absolute training cutoff\n[Timer]\nOnCalendar={when}\nAccuracySec=1s\nRandomizedDelaySec=0\nPersistent=true\n[Install]\nWantedBy=timers.target\n"
        cutoff = f"[Unit]\nDescription=Stop training at its absolute cutoff\n[Service]\nType=oneshot\nExecStart={command} deadline {self.id}\n"
        for suffix, body in (
            (".service", service),
            ("-cutoff.service", cutoff),
            ("-cutoff.timer", timer),
        ):
            path = root / (base + suffix)
            if path.exists():
                jobs.require(path.read_text() == body, "Existing cycle unit differs")
            else:
                with path.open("x") as f:
                    os.fchmod(f.fileno(), 0o600)
                    f.write(body)
                    f.flush()
                    os.fsync(f.fileno())
        jobs.run(["systemctl", "--user", "daemon-reload"])
        jobs.run(["systemctl", "--user", "enable", "--now", base + "-cutoff.timer"])
        jobs.require(
            self.unit(base + "-cutoff.timer") == "active",
            "Absolute cutoff timer did not arm",
        )
        jobs.run(["systemctl", "--user", "enable", "--now", base + ".service"])
        return {
            "cycle": self.id,
            "unit": base + ".service",
            "cutoff_timer": base + "-cutoff.timer",
            "training_started": False,
        }

    def supervise(self):
        while True:
            d = self.tick()
            if d["phase"] in TERMINAL:
                return
            time.sleep(3 if not d.get("last_error") else 8)


def list_cycles(state):
    out = []
    for p in sorted(
        (Path(state) / "training/cycles").glob("*/cycle.json"),
        key=lambda p: p.stat().st_mtime_ns,
        reverse=True,
    )[:100]:
        try:
            d = jobs.json_file(p, limit=16 * 1024 * 1024)
            a = d.get("active") or {}
            out.append(
                {
                    "id": d["id"],
                    "phase": d["phase"],
                    "deadline_epoch": d["deadline_epoch"],
                    "reason": d.get("last_error") or d.get("reason"),
                    "rounds_reviewed": len(d["rounds"]),
                    "verified_optimizer_updates": sum(
                        r.get("optimizer_updates", 0)
                        for r in d["rounds"]
                        if r.get("optimizer_verified")
                    ),
                    "best_checkpoint": (d.get("best") or {}).get("sha256"),
                    "current_attempt": a.get("attempt"),
                    "training_progress": a.get("live"),
                }
            )
        except (ValueError, OSError, KeyError):
            continue
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--state",
        type=Path,
        default=Path(
            os.environ.get(
                "DULCHE_STATE_DIR",
                Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state"))
                / "9to1/dulche",
            )
        ),
    )
    sub = parser.add_subparsers(dest="action", required=True)
    sub.add_parser("list")
    c = sub.add_parser("create")
    c.add_argument("plan", type=Path)
    c.add_argument("--sha256", required=True)
    for verb in (
        "start",
        "status",
        "tick",
        "supervise",
        "pause",
        "resume",
        "deadline",
        "retry-gate",
    ):
        sub.add_parser(verb).add_argument("id")
    sub.add_parser("submit").add_argument("id")
    sub.choices["submit"].add_argument("receipt", type=Path)
    adopt = sub.add_parser("adopt")
    adopt.add_argument("id")
    adopt.add_argument("attempt")
    adopt.add_argument("--window-owner", required=True)
    reserve = sub.add_parser("reserve-final")
    reserve.add_argument("id")
    reserve.add_argument("manifest", type=Path)
    reserve.add_argument("--sha256", required=True)
    baseline = sub.add_parser("import-baseline")
    baseline.add_argument("id")
    baseline.add_argument("--report", type=Path, required=True)
    baseline.add_argument("--mentor-context", type=Path, required=True)
    baseline.add_argument("--score-field", required=True)
    baseline.add_argument("--benchmark-sha-field", required=True)
    args = parser.parse_args()
    if args.action == "list":
        result = list_cycles(args.state)
    else:
        id = jobs.json_file(args.plan)["id"] if args.action == "create" else args.id
        manager = Cycle(args.state, id)
        if args.action == "create":
            result = manager.create(args.plan, args.sha256)
        elif args.action == "status":
            result = jobs.json_file(manager.file, limit=16 * 1024 * 1024)
        elif args.action == "adopt":
            result = manager.adopt(args.attempt, args.window_owner)
        elif args.action == "reserve-final":
            result = manager.reserve_final(args.manifest, args.sha256)
        elif args.action == "import-baseline":
            result = manager.import_baseline(
                args.report,
                args.mentor_context,
                args.score_field,
                args.benchmark_sha_field,
            )
        elif args.action == "submit":
            result = manager.submit(args.receipt)
        elif args.action == "retry-gate":
            result = manager.retry_gate()
        elif args.action == "start":
            result = manager.start()
        elif args.action == "supervise":
            manager.supervise()
            return
        elif args.action == "tick":
            result = manager.tick()
        else:
            result = manager.control(args.action)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (
        ValueError,
        KeyError,
        OSError,
        RuntimeError,
        subprocess.SubprocessError,
    ) as e:
        sys.exit(f"Dulche cycle: {e}")
