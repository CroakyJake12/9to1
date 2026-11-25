"""Executable CPU ALORA recipe within an existing admitted training worker.

No registration, model download, inference placement or service launch occurs
here. The existing worker bridge owns the one readiness handshake and every
compute permit. A service resume is imported as data into this attempt's custody.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass, asdict
import importlib.util
import json
import os
from pathlib import Path
import re
import time

from custody import ArtifactStore
from custody.artifacts import _open_regular, checked_path
from .workflow import (DEPLOYMENT_SCHEMA, WORKFLOW_SCHEMA, canonical, canonical_sha256,
                       deployment_inputs, read_json, read_roots, verify_artifact,
                       verify_deployment, _write)

_JOB = re.compile(r"[a-f0-9]{32}\Z")
_SHA = re.compile(r"[a-f0-9]{64}\Z")


@dataclass(frozen=True)
class ServiceJob:
    job_id: str
    output: Path
    control_path: Path
    completed_steps: int
    resume_path: Path | None
    resume_sha256: str | None
    resume_size: int | None
    workflow_sha256: str


def _cgroup():
    with Path("/proc/self/cgroup").open() as source:
        return source.read(65537)


def _status_with_pid(path, pid):
    # systemd starts the worker before the supervisor persists MainPID.
    # Waiting here performs no model computation and does not announce ready.
    deadline = time.monotonic() + 10
    while True:
        status = read_json(path, limit=1024 * 1024)
        if status.get("worker_pid") is not None or status.get("phase") not in {"requested", "starting"}:
            return status
        if time.monotonic() >= deadline:
            raise ValueError("The supervisor did not bind the actual worker PID")
        time.sleep(0.05)


def admit_cpu_job(spec, spec_pin):
    """Bind this process to the actual isolated CPU job, before Torch import."""
    job_id = os.environ.get("DULCHE_TRAIN_JOB", "")
    if not _JOB.fullmatch(job_id) or os.environ.get("CUDA_VISIBLE_DEVICES") != "":
        raise ValueError("This executable requires an admitted CPU training job with GPU visibility disabled")
    control_path = Path(os.environ.get("DULCHE_TRAIN_CONTROL", ""))
    output = Path(os.environ.get("DULCHE_TRAIN_OUTPUT", ""))
    checked_path(control_path, directory=False)
    checked_path(output, directory=True)
    job_dir = control_path.parent
    if (control_path.name != "control.json" or job_dir.name != job_id
            or output != job_dir / "output"):
        raise ValueError("Training environment paths do not identify one supervisor job")
    pid = os.getpid()
    unit = f"dulche-training-{job_id}-worker.service"
    cgroup = _cgroup()
    if len(cgroup) > 65536 or not any(
            unit in line.split(":", 2)[-1].split("/") for line in cgroup.splitlines()):
        raise ValueError("This process is not inside its admitted worker cgroup")
    status = _status_with_pid(job_dir / "status.json", pid)
    if (status.get("id") != job_id or status.get("device") != "cpu"
            or type(status.get("worker_pid")) is not int or status["worker_pid"] != pid
            or status.get("worker_unit") != unit
            or status.get("phase") not in {"requested", "starting"}):
        raise ValueError("Supervisor identity, device or preparation phase differs")
    isolation = read_json(output / "isolation.json", limit=65536)
    if (type(isolation.get("pid")) is not int or isolation["pid"] != pid
            or isolation.get("device") != "cpu"
            or type(isolation.get("landlock_abi")) is not int or isolation["landlock_abi"] < 5):
        raise ValueError("The existing worker isolation receipt is missing or inconsistent")
    command = read_json(control_path, limit=65536)
    if command.get("job") != job_id or command.get("action") not in {"wait", "run"}:
        raise ValueError("The supervisor cancelled or mismatched this preparation")
    workflow = read_json(Path(spec["deployment"]["workflow_path"]), limit=4 * 1024 * 1024)
    expected = {
        "schema": WORKFLOW_SCHEMA, "purpose": spec["purpose"], "id": spec["id"],
        "model": spec["model"], "worker_protocol": "checkpoint-yield-v1",
        "arguments": ["--spec", spec_pin["path"], "--spec-sha256", spec_pin["sha256"]],
        "cwd": spec["deployment"]["root"], "inputs": deployment_inputs(spec, spec_pin),
        "read_only_roots": read_roots(spec), "dataset_manifest": spec["dataset"]["manifest"],
        "dataset_root": spec["dataset"]["root"], "limits": spec["limits"], "device": "cpu",
    }
    if (set(workflow) != set(expected) | {"executable"}
            or any(workflow[key] != value for key, value in expected.items())
            or canonical_sha256(workflow) != status.get("workflow_digest")
            or status.get("workflow") != spec["id"]):
        raise ValueError("The admitted workflow differs from the pinned ALORA deployment")
    verify_artifact(workflow["executable"], limit=1024 * 1024)
    if workflow["executable"]["path"] != str(Path(spec["deployment"]["root"]) / "worker"):
        raise ValueError("The registered executable is not this deployment's launcher")
    launch = read_json(job_dir / "launch.json", limit=4 * 1024 * 1024)
    for key in ("executable", "arguments", "cwd", "device"):
        if launch.get(key) != workflow[key]:
            raise ValueError("The isolated launch differs from the admitted workflow")
    if launch.get("gpu_minor") is not None:
        raise ValueError("A CPU recipe cannot inherit a GPU device grant")
    steps = status.get("steps")
    if type(steps) is not int or steps < 0:
        raise ValueError("The supervisor completed-step counter is invalid")
    resume = os.environ.get("DULCHE_RESUME_CHECKPOINT") or None
    receipt = status.get("checkpoint")
    if resume is None:
        if steps != 0 or receipt is not None or status.get("resume_of") is not None:
            raise ValueError("A continued job needs its actual saved checkpoint")
        return ServiceJob(job_id, output, control_path, 0, None, None, None, status["workflow_digest"])
    if (type(receipt) is not dict or type(receipt.get("sha256")) is not str
            or not _SHA.fullmatch(receipt["sha256"])
            or type(receipt.get("size")) is not int or receipt["size"] < 1
            or type(receipt.get("steps")) is not int or receipt["steps"] != steps
            or receipt.get("path") != resume or not status.get("resume_of")):
        raise ValueError("The service resume receipt does not bind path, bytes and completed steps")
    resume_path = Path(resume)
    checked_path(resume_path, directory=False)
    if resume_path.name != receipt["sha256"] or resume_path.stat().st_size != receipt["size"]:
        raise ValueError("The service checkpoint differs from its content-addressed receipt")
    return ServiceJob(job_id, output, control_path, steps, resume_path,
                      receipt["sha256"], receipt["size"], status["workflow_digest"])


def restore_service_checkpoint(session, publisher, job):
    """Import the supervisor CAS snapshot without pickle or rewriting its source."""
    if job.resume_path is None:
        if job.completed_steps != 0:
            raise ValueError("Missing checkpoint for the supervisor's completed steps")
        return None
    if (type(job.resume_sha256) is not str or not _SHA.fullmatch(job.resume_sha256)
            or type(job.resume_size) is not int or not 1 <= job.resume_size <= publisher.max_bytes
            or job.resume_path.name != job.resume_sha256):
        raise ValueError("The saved checkpoint lacks a bounded content-addressed identity")
    with _open_regular(job.resume_path) as source:
        reference = publisher.store.publish(
            publisher.attempt, "service-resume.snapshot", "segment_checkpoint", source,
            max_bytes=publisher.max_bytes)
    if reference.sha256 != job.resume_sha256 or reference.size_bytes != job.resume_size:
        raise ValueError("Imported resume bytes differ from the supervisor's immutable checkpoint")
    from custody.snapshot import load_snapshot
    snapshot = load_snapshot(publisher.store, reference, max_bytes=publisher.max_bytes)
    if (type(snapshot.get("updates")) is not int or snapshot["updates"] != job.completed_steps
            or type(snapshot.get("cursor")) is not int or snapshot["cursor"] != job.completed_steps):
        raise ValueError("The checkpoint cursor differs from the supervisor's completed-step receipt")
    session.restore(snapshot)
    evidence = {
        "schema": 1, "kind": "alora-service-resume-import", "purpose": session.purpose,
        "job_id": job.job_id, "workflow_sha256": job.workflow_sha256,
        "source_path": str(job.resume_path), "source_sha256": job.resume_sha256,
        "source_size_bytes": job.resume_size, "completed_steps": job.completed_steps,
        "imported_artifact": asdict(reference),
    }
    publisher.store.publish_bytes(
        publisher.attempt, "service-resume-evidence.json", "evidence",
        canonical(evidence) + b"\n", inputs=(reference,), max_bytes=1024 * 1024)
    return reference


def _control_client(pin):
    path = verify_artifact(pin, limit=1024 * 1024)
    spec = importlib.util.spec_from_file_location("_alora_pinned_training_control", path)
    if spec is None or spec.loader is None:
        raise ValueError("Cannot import the pinned TrainingControl client")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run_prepared(spec_path, *, spec_sha256):
    """Prepare the exact CPU session, then let the existing worker own readiness."""
    spec_path = Path(spec_path)
    spec, dataset, profile, config, counts = verify_deployment(spec_path, sha256=spec_sha256)
    spec_pin = {"path": str(spec_path), "sha256": spec_sha256}
    job = admit_cpu_job(spec, spec_pin)
    settings, limits = spec["settings"], spec["limits"]
    if settings["target_updates"] <= job.completed_steps:
        raise ValueError("The target must advance the admitted checkpoint's completed steps")
    if settings["threads"] > (os.cpu_count() or 1):
        raise ValueError("CPU thread count exceeds the admitted host's logical processors")
    # These affect this isolated worker only; no inference environment is edited.
    os.environ["OMP_NUM_THREADS"] = str(settings["threads"])
    os.environ["MKL_NUM_THREADS"] = str(settings["threads"])
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    import torch
    if torch.cuda.is_initialized():
        raise ValueError("The CPU training entrypoint cannot inherit initialized CUDA")
    torch.set_num_threads(settings["threads"])
    torch.manual_seed(settings["seed"])
    from .gguf_store import GGUFWeightStore
    from .gguf_model import load_text_model
    from .session import TrainingSession
    from .checkpoints import CheckpointPublisher
    from .worker import run_training

    client = _control_client(spec["control_client"])
    control = client.TrainingControl()
    if (control.job != job.job_id or Path(control.output) != job.output
            or Path(control.control) != job.control_path
            or (control.resume or None) != (str(job.resume_path) if job.resume_path else None)):
        raise ValueError("The actual control client differs from the admitted job")
    weights = None
    try:
        weights = GGUFWeightStore(
            [Path(pin["path"]) for pin in spec["shards"]],
            expected_shards={Path(pin["path"]).name:
                             {"sha256": pin["sha256"], "size_bytes": pin["size_bytes"]}
                             for pin in spec["shards"]},
            max_decoded_bytes=settings["max_decoded_bytes"],
            max_read_bytes=settings["max_read_bytes"],
            max_fingerprints=settings["max_fingerprints"])
        if profile.base_model_sha256 != weights.source_identity:
            raise ValueError("The retained activation profile describes different base GGUF weights")
        selected = profile.select(settings["experts_per_layer"], require_all_categories=True)
        model = load_text_model(weights, config, selections=selected,
                                rank=settings["rank"], alpha=settings["alpha"],
                                device="cpu", dtype=torch.float32, chunk_rows=settings["chunk_rows"])
        session = TrainingSession(
            model, identity={"base_model_sha256": weights.source_identity,
                             "dataset_manifest_sha256": dataset.manifest_sha256,
                             "profile_sha256": spec["profile_record_sha256"]},
            purpose="real_training", learning_rate=settings["learning_rate"],
            max_grad_norm=settings["max_grad_norm"], profile=profile,
            expert_budget_per_layer=settings["experts_per_layer"],
            loss_token_rows=settings["loss_token_rows"])
        store = ArtifactStore(job.output / "custody")
        plan = {
            "purpose": "real_training", "workflow_sha256": job.workflow_sha256,
            "deployment_sha256": spec_sha256, "request_sha256": spec["request_sha256"],
            "target_updates": settings["target_updates"], "identity": session.identity,
        }
        run = store.create_run("ALORA selected-expert training candidate", plan)
        attempt = store.begin_attempt(run, {
            **plan, "job_id": job.job_id,
            "service_resume_sha256": job.resume_sha256,
            "initial_completed_steps": job.completed_steps,
        })
        publisher = CheckpointPublisher(store, attempt, purpose="real_training",
                                        max_bytes=limits["max_checkpoint_bytes"])
        imported = restore_service_checkpoint(session, publisher, job)
        _write(job.output / "run-identity.json", canonical({
            "schema": 1, "purpose": "real_training", "job_id": job.job_id,
            "attempt": asdict(attempt), "identity": session.identity,
            "category_counts": counts, "training_samples": sum(counts.values()),
            "imported_resume": asdict(imported) if imported else None,
            "loader": model.alora_loader_report, "fresh_full_file_hash_verified": False,
            "candidate_only": True,
        }) + b"\n", mode=0o600)
        result = run_training(session, dataset, publisher, control,
                              target_updates=settings["target_updates"],
                              checkpoint_exception=client.CheckpointRequired)
        receipt = {key: result[key] for key in
                   ("phase", "updates", "checkpoint_sha256", "checkpoint_path")}
        receipt.update({"schema": 1, "purpose": "real_training", "candidate_only": True,
                        "job_id": job.job_id, "attempt": asdict(attempt),
                        "weight_store_stats": weights.stats, "fresh_full_file_hash_verified": False})
        _write(job.output / "runner-result.json", canonical(receipt) + b"\n", mode=0o600)
        return receipt
    except BaseException as error:
        _write(job.output / "runner-failure.json", canonical({
            "schema": 1, "purpose": "real_training", "phase": "failed",
            "job_id": job.job_id, "error_type": type(error).__name__, "retry_current_session": False,
        }) + b"\n", mode=0o600)
        raise
    finally:
        if weights is not None:
            weights.close()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--spec", type=Path, required=True)
    parser.add_argument("--spec-sha256", required=True)
    args = parser.parse_args(argv)
    result = run_prepared(args.spec, spec_sha256=args.spec_sha256)
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
