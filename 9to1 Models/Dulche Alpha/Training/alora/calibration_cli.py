"""Standalone CPU calibration under the existing SDK lock and memory guard.

The public command starts only the pinned development guard and its private CPU
child. It never registers a training workflow, issues TrainingControl progress,
downloads weights or changes an inference service.
"""
from __future__ import annotations

import argparse
import fcntl
import json
import os
from pathlib import Path
import signal
import subprocess
import time

from custody.artifacts import _open_regular, checked_path
from . import workflow


class CalibrationPreparationInterrupted(RuntimeError):
    """The guard requested release before a complete model was available."""


def _process(pid):
    directory = Path("/proc") / str(pid)
    info = directory.stat()
    with (directory / "stat").open() as source:
        fields = source.read(65537).rsplit(")", 1)[1].split()
    with (directory / "cmdline").open("rb") as source:
        raw = source.read(65537)
    if len(raw) > 65536:
        raise ValueError("The guard process command exceeds its bound")
    return {"pid": pid, "uid": info.st_uid, "ppid": int(fields[1]),
            "start_ticks": int(fields[19]),
            "argv": [part.decode() for part in raw.rstrip(b"\0").split(b"\0")],
            "executable": str((directory / "exe").resolve(strict=True))}


def _held_sdk_lock(path, pid):
    info = checked_path(path, directory=False)
    with Path("/proc/locks").open() as source:
        data = source.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024:
        raise ValueError("Cannot bound the kernel lock inventory")
    for line in data.splitlines():
        fields = line.split()
        if len(fields) < 8 or fields[1:4] != ["FLOCK", "ADVISORY", "WRITE"]:
            continue
        device = fields[5].split(":")
        if len(device) != 3:
            continue
        if (int(fields[4]) == pid and int(device[0], 16) == os.major(info.st_dev)
                and int(device[1], 16) == os.minor(info.st_dev) and int(device[2]) == info.st_ino):
            return True
    return False


def _memory_mib():
    with Path("/proc/meminfo").open() as source:
        for line in source:
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) // 1024
    raise ValueError("The existing host memory floor cannot be observed")


class CalibrationPermit:
    """Observe the actual parent guard; never manufacture a compute admission."""
    def __init__(self, spec, guard, owner):
        self.spec, self.guard, self.owner = spec, guard, owner
        self.interruption_type = CalibrationPreparationInterrupted
        self.stop_requested = False
        self.last_checked = float("-inf")
        self.old_term = None
        self.started = guard["start_ticks"] / os.sysconf("SC_CLK_TCK")

    def __enter__(self):
        self.old_term = signal.getsignal(signal.SIGTERM)
        signal.signal(signal.SIGTERM, self._term)
        return self

    def __exit__(self, *_args):
        signal.signal(signal.SIGTERM, self.old_term)

    def _term(self, _signum, _frame):
        self.stop_requested = True

    def __call__(self):
        if self.stop_requested:
            raise self.interruption_type("The memory guard requested cooperative release")
        now = time.monotonic()
        if now - self.last_checked < 0.1:
            return
        self.last_checked = now
        try:
            guard = _process(self.guard["pid"])
            owner = _process(self.owner["pid"])
            if (os.getppid() != self.guard["pid"]
                    or guard != self.guard or owner != self.owner
                    or not _held_sdk_lock(Path(self.spec["guard"]["sdk_lock"]), owner["pid"])):
                raise self.interruption_type("The actual guard or shared SDK exclusion changed")
            elapsed = time.clock_gettime(time.CLOCK_BOOTTIME) - self.started
            timeout = self.spec["guard"]["timeout_seconds"]
            reserve = min(30, timeout / 4)
            if elapsed >= timeout - reserve:
                raise self.interruption_type("Calibration is retaining its profile before the guard deadline")
            if _memory_mib() <= self.spec["guard"]["floor_mib"] + 256:
                raise self.interruption_type("Calibration is retaining its profile before the memory floor")
        except (OSError, ValueError, IndexError, UnicodeError) as error:
            raise self.interruption_type("The active guard observation became unavailable") from error


def admit_calibration_guard(spec, spec_pin):
    """Validate real process lineage and kernel flock state before Torch import."""
    lock = workflow.validate_calibration_guard(spec["guard"])
    if (os.environ.get("CUDA_VISIBLE_DEVICES") != "-1"
            or any(name.startswith("DULCHE_TRAIN_") for name in os.environ)):
        raise ValueError("Standalone calibration must be CPU-only and outside a training job")
    threads = str(spec["settings"]["threads"])
    if os.environ.get("OMP_NUM_THREADS") != threads or os.environ.get("MKL_NUM_THREADS") != threads:
        raise ValueError("The guarded calibration child lacks its exact CPU thread cap")
    guard = _process(os.getppid())
    expected = workflow.calibration_guard_command(spec, spec_pin)
    if (guard["uid"] != os.getuid() or guard["argv"] != expected
            or guard["executable"] != spec["deployment"]["python_resolved"]["path"]):
        raise ValueError("The actual parent is not the exact pinned memory-guard invocation")
    owner = _process(guard["ppid"])
    if owner["uid"] != os.getuid() or not _held_sdk_lock(lock, owner["pid"]):
        raise ValueError("The guard's actual parent does not hold the existing shared SDK lock")
    return CalibrationPermit(spec, guard, owner)


def _child(spec_path, spec_sha256):
    spec_path = Path(spec_path)
    pin = {"path": str(spec_path), "sha256": spec_sha256}
    initial = workflow.read_json(spec_path, sha256=spec_sha256, limit=4 * 1024 * 1024)
    permit = admit_calibration_guard(initial, pin)
    output = Path(initial["deployment"]["root"])
    weights = None
    try:
        with permit:
            permit()
            spec, dataset, config, counts = workflow.verify_calibration_deployment(spec_path, sha256=spec_sha256)
            permit()
            settings = spec["settings"]
            if settings["threads"] > (os.cpu_count() or 1):
                raise ValueError("The CPU thread cap exceeds the host's logical processor count")
            import torch
            if torch.cuda.is_initialized():
                raise ValueError("The CPU calibration child cannot inherit initialized CUDA")
            torch.set_num_threads(settings["threads"])
            torch.manual_seed(settings["seed"])
            from .session import ComputeInterrupted
            from .compute import compute_scope
            from .gguf_store import GGUFWeightStore
            from .gguf_model import load_text_model
            from .calibration import calibrate
            permit.interruption_type = ComputeInterrupted
            with compute_scope(permit, device="cpu"):
                permit()
                weights = GGUFWeightStore(
                    [Path(item["path"]) for item in spec["shards"]],
                    expected_shards={Path(item["path"]).name:
                                     {"size_bytes": item["size_bytes"], "sha256": item["sha256"]}
                                     for item in spec["shards"]},
                    max_decoded_bytes=settings["max_decoded_bytes"],
                    max_read_bytes=settings["max_read_bytes"],
                    max_fingerprints=settings["max_fingerprints"])
                model = load_text_model(weights, config, selections={}, rank=1, alpha=1,
                                        device="cpu", dtype=torch.float32,
                                        chunk_rows=settings["chunk_rows"])
                result = calibrate(
                    model, dataset, base_model_sha256=weights.source_identity,
                    output_path=Path(spec["deployment"]["profile_path"]),
                    samples_per_category=settings["samples_per_category"],
                    check_permit=permit, resume=spec["resume"])
            result = {**result, "guarded_cpu": True, "deployment_sha256": spec_sha256,
                      "category_counts": counts, "weight_store_stats": weights.stats,
                      "fresh_full_file_hash_verified": False}
    except BaseException as error:
        if isinstance(error, permit.interruption_type):
            result = {
                "schema": 1, "phase": "interrupted", "complete": False,
                "purpose": initial["purpose"], "reason": "model_preparation_interrupted",
                "guarded_cpu": True, "deployment_sha256": spec_sha256,
                "profile_path": None, "resume": None,
                "fresh_full_file_hash_verified": False,
            }
        else:
            workflow._write(output / "calibration-failure.json", workflow.canonical({
                "schema": 1, "phase": "failed", "purpose": initial["purpose"],
                "error_type": type(error).__name__, "retry_partial_row": False,
            }) + b"\n", mode=0o600)
            raise
    finally:
        if weights is not None:
            weights.close()
    workflow._write(output / "calibration-result.json", workflow.canonical(result) + b"\n", mode=0o600)
    return result


def run_calibration(request_path, *, request_sha256, output):
    """Acquire the existing exclusion and run one bounded CPU calibration child."""
    if any(name.startswith("DULCHE_TRAIN_") for name in os.environ):
        raise ValueError("Calibration cannot nest inside a production training worker")
    request = workflow.read_json(Path(request_path), sha256=request_sha256, limit=4 * 1024 * 1024)
    lock_path = workflow.validate_calibration_guard(request.get("guard"))
    with _open_regular(lock_path) as lock:
        try:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise RuntimeError("The shared 9to1 SDK lock is busy; retain this request and retry when its owner releases it") from error
        try:
            if _memory_mib() <= request["guard"]["floor_mib"] + 512:
                raise RuntimeError("Available memory is too close to the approved calibration floor")
            request, _, _, _ = workflow.validate_calibration_request(request, verify_corpus=False)
            source_root = Path(__file__).resolve().parent.parent
            spec, spec_pin, launcher = workflow._emit_calibration_bundle(
                request, Path(output), request_sha256=request_sha256, source_root=source_root)
            command = workflow.calibration_guard_command(spec, spec_pin)
            env = dict(os.environ)
            env.update(CUDA_VISIBLE_DEVICES="-1", OMP_NUM_THREADS=str(spec["settings"]["threads"]),
                       MKL_NUM_THREADS=str(spec["settings"]["threads"]),
                       HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", USE_HUB_KERNELS="NO",
                       PYTHONDONTWRITEBYTECODE="1")
            # The guard performs memory/time monitoring and owns its child group.
            # This process retains the existing SDK flock throughout that lifetime.
            completed = subprocess.run(command, cwd=spec["deployment"]["root"], env=env,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, check=False)
            guard_receipt_path = Path(spec["deployment"]["guard_log_path"] + ".guard.json")
            guard = workflow.read_json(guard_receipt_path, limit=1024 * 1024)
            expected_child = command[11:]
            if (guard.get("command") != expected_child
                    or guard.get("floor_mib") != spec["guard"]["floor_mib"]):
                raise ValueError("The retained guard receipt differs from the exact child invocation")
            invocation = {
                "schema": 1, "kind": "alora-calibration-guard-invocation", "purpose": request["purpose"],
                "request_sha256": request_sha256, "deployment": spec_pin, "launcher": launcher,
                "guard_script": spec["guard"]["script"], "guard_receipt": workflow.artifact(guard_receipt_path),
                "sdk_lock": str(lock_path), "guard_exit_code": completed.returncode,
                "command": command, "guard_result": guard.get("result"),
            }
            workflow._write(Path(output) / "calibration-invocation.json",
                            workflow.canonical(invocation) + b"\n", mode=0o600)
            if completed.returncode != 0 or guard.get("result") != "OK":
                raise RuntimeError("Calibration was stopped or failed; the guard log, receipt and any partial profile remain in " + str(output))
            result = workflow.read_json(Path(output) / "calibration-result.json", limit=4 * 1024 * 1024)
            if type(result.get("complete")) is not bool or result.get("phase") not in {"completed", "interrupted"}:
                raise ValueError("The calibration child did not publish an explicit completion state")
            if result["complete"] != (result["phase"] == "completed"):
                raise ValueError("Calibration completion and phase disagree")
            if result.get("profile_path") is not None:
                profile_path = Path(result["profile_path"])
                expected_profile = Path(output) / ("profile.json" if result["complete"] else "profile.json.partial.json")
                if profile_path != expected_profile:
                    raise ValueError("The calibration profile escaped its exclusive output path")
                workflow.verify_artifact({"path": str(profile_path), "sha256": result["raw_sha256"]})
                if result["receipt_path"] != str(profile_path) + ".receipt.json":
                    raise ValueError("The calibration receipt is not the profile's retained sibling")
                workflow.verify_artifact({"path": result["receipt_path"], "sha256": result["receipt_sha256"]})
                if result.get("resume") != {
                        "profile": {"path": str(profile_path), "sha256": result["raw_sha256"]},
                        "receipt": {"path": result["receipt_path"], "sha256": result["receipt_sha256"]}}:
                    raise ValueError("The continuation descriptor differs from retained calibration bytes")
            elif result["complete"] or result.get("resume") is not None:
                raise ValueError("A completed calibration requires an actual retained profile")
            return {**result, "guard_receipt": invocation["guard_receipt"],
                    "guard_script": spec["guard"]["script"], "output_directory": str(output)}
        finally:
            fcntl.flock(lock.fileno(), fcntl.LOCK_UN)


def child_main(argv=None):
    parser = argparse.ArgumentParser(description="Pinned CPU calibration child of the existing memory guard")
    parser.add_argument("--spec", type=Path, required=True)
    parser.add_argument("--spec-sha256", required=True)
    args = parser.parse_args(argv)
    result = _child(args.spec, args.spec_sha256)
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return 0
