"""Bounded supervision for registered, cooperative, single-process workers."""

from __future__ import annotations

from dataclasses import asdict, dataclass
import hashlib
import os
from pathlib import Path
import subprocess
import threading
import time
from typing import Callable, Mapping

from .artifacts import (
    ArtifactStore, AttemptRef, CustodyError, _copy, _encode_record, _open_regular, _read_record, checked_path,
    create_directory, write_record,
)
from .provenance import DatasetManifest, verify_dataset_files
from .registered_runtime import BOOTSTRAP, RegisteredRuntime, RegisteredWorker
from .resources import Admission, BudgetRefusal, HostObservation, Limits, OperatorPolicy, ResourceUse, admit, check_running
from .windows_job import WindowsJob

_POLL = 0.1
_URGENT = {"temperature_capacity", "power_capacity", "protected_work_active_or_unknown", "cpu_telemetry_missing",
           "gpu_observation_missing", "gpu_not_allowlisted", "v100_cooling_hold", "unexpected_gpu_use",
           "log_capacity", "log_write_failed"}
@dataclass(frozen=True, slots=True)
class Outcome:
    status: str
    reason: str
    exit_code: int | None


def _safe_environment(admission: Admission, temp: Path, python: Path) -> dict[str, str]:
    # The registered single process inherits no application credentials.
    durable_host_vars = ("SYSTEMROOT", "WINDIR", "LANG")
    env = {key: os.environ[key] for key in durable_host_vars if key in os.environ}
    system = Path(env["SYSTEMROOT"])
    env["PATH"] = os.pathsep.join(map(str, (python.parent, system / "System32", system)))
    env["CUDA_VISIBLE_DEVICES"] = admission.device_uuid or "-1"
    for key in ("OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS", "DULCHE_TORCH_THREADS"):
        env[key] = str(admission.policy.cpu_threads)
    for key in ("TEMP", "TMP", "TMPDIR"):
        env[key] = str(temp)
    return env


def _cpu_runtime_verified(control: Path) -> bool:
    expected = {"schema": 1, "cuda_mask": "-1", "cuda_initialized": False, "init_refusals": 0}
    try:
        return all(_read_record(control / f"cpu-runtime-{phase}.json") == expected for phase in ("start", "finish"))
    except (OSError, ValueError):
        return False


class ProcessSupervisor:
    """Own a Windows Job and run only a registered source bundle inside it.

    The owner integration supplies trustworthy host and owned-process telemetry.
    Missing or invalid telemetry stops the Job. Logs and time are also bounded
    directly, even if the measurement adapter fails to return.
    """

    def __init__(
        self,
        store: ArtifactStore,
        worker_root: Path | str,
        workers: Mapping[str, RegisteredWorker],
        policy: OperatorPolicy,
        runtime: RegisteredRuntime,
    ):
        self.store = store
        self.worker_root = Path(worker_root)
        checked_path(self.worker_root, directory=True)
        self._workers = dict(workers)
        self._policy = policy
        self._runtime = runtime
        source_file = Path(__file__)
        checkout = None
        if source_file.is_file():
            checkout = next((path for path in source_file.resolve().parents if (path / ".git").exists()), None)
        self._excluded_roots = (self.worker_root, store.root) + ((checkout,) if checkout is not None else ())

    def run(
        self, attempt: AttemptRef, worker_id: str, request: Mapping, *,
        purpose: str, limits: Limits, host: HostObservation, device_uuid: str | None,
        observe: Callable[[int], ResourceUse], pause_requested: Callable[[], bool] = lambda: False,
        dataset: DatasetManifest | None = None, dataset_root: Path | str | None = None,
        prior_iterations: int = 0, cpu_rate_hundredths: int | None = None,
    ) -> Outcome:
        """Run a registered worker once. Request values are JSON data, never shell arguments."""
        if os.name != "nt":
            raise CustodyError("Windows Job Object ownership is required for this launcher.")
        if purpose not in {"software_test_only", "real_training"}:
            raise CustodyError("Unsupported supervised execution purpose.")
        if purpose == "software_test_only" and device_uuid is not None:
            raise BudgetRefusal("software_fixtures_are_cpu_only")
        worker = self._workers.get(worker_id)
        if worker is None:
            raise CustodyError("This worker is not registered for experiment execution.")
        runtime = self._runtime.verified_record(self._excluded_roots)
        source = worker.verified_bundle(self.worker_root, {pin.module for pin in self._runtime.packages})
        if purpose == "real_training" or dataset is not None:
            if dataset is None or dataset_root is None or dataset.purpose != purpose:
                raise CustodyError("A dataset admitted for this execution purpose is required.")
            verify_dataset_files(dataset, dataset_root)
        admission = admit(limits, host, policy=self._policy, device_uuid=device_uuid, purpose=purpose,
                          observed_iterations=prior_iterations)
        runtime_record = {"schema": 1, **runtime, "device_uuid": admission.device_uuid}
        runtime_hash = hashlib.sha256(_encode_record(runtime_record)).hexdigest()
        attempt_dir = self.store.attempt_path(attempt)
        work, control = attempt_dir / "work", attempt_dir / "control"
        checked_path(work, directory=True)
        checked_path(control, directory=True)
        # The single immutable request claims this attempt before spawning.
        request_path = control / "request.json"
        write_record(request_path, {
            "schema": 1, **asdict(attempt), "request": dict(request), "purpose": purpose, "worker_id": worker_id,
            "worker_sha256": worker.sha256, "runtime_sha256": runtime_hash,
            "dataset_manifest_sha256": None if dataset is None else dataset.sha256,
            "device_uuid": device_uuid, "limits": asdict(limits), "prior_iterations": prior_iterations,
            "cpu_threads": admission.policy.cpu_threads, "minimum_free_ram_bytes": admission.policy.minimum_free_ram_bytes,
            "cpu_rate_hundredths": cpu_rate_hundredths,
        })
        if dataset is not None:
            write_record(control / "dataset.json", dataset.as_record())
        runtime_path = control / "runtime.json"
        write_record(runtime_path, runtime_record)
        temp = work / "temp"
        create_directory(temp, exclusive=True)
        artifact = self.store.publish_bytes(attempt, "supervised-source.zip", "script", source)
        bundle = self.store.verify(artifact)
        python = self._runtime.executable
        process = None
        creation_flags = subprocess.CREATE_NO_WINDOW
        if cpu_rate_hundredths is not None:
            creation_flags |= subprocess.IDLE_PRIORITY_CLASS
        try:
            with (work / "stdout.log").open("xb") as stdout, (work / "stderr.log").open("xb") as stderr:
                with WindowsJob(limits.ram_bytes, cpu_rate_hundredths=cpu_rate_hundredths) as job:
                    process = subprocess.Popen(
                        [str(python), "-I", "-S", "-B", "-c", BOOTSTRAP, str(runtime_path), runtime_hash, str(bundle),
                         artifact.sha256, worker.entry_module, str(request_path), str(control / "pause.json")],
                        shell=False, cwd=work, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        env=_safe_environment(admission, temp, python), creationflags=creation_flags,
                    )
                    job.assign(process)
                    outcome = self._run_job(process, job, admission, control, (stdout, stderr),
                                            observe, pause_requested, prior_iterations)
            log_hashes = {}
            for name in ("stdout.log", "stderr.log"):
                with _open_regular(work / name) as log:
                    digest, size = _copy(log, None, min(64 * 1024, max(1, limits.storage_bytes // 8)))
                log_hashes[name] = {"sha256": digest, "size_bytes": size}
            write_record(control / "logs.json", {"schema": 1, "files": log_hashes})
            if (outcome.status == "passed" and admission.device_uuid is None
                    and any(pin.module == "torch" for pin in self._runtime.packages)
                    and not _cpu_runtime_verified(control)):
                outcome = Outcome("interrupted", "cpu_runtime_state_unconfirmed", outcome.exit_code)
        except BaseException:
            if process is not None and process.poll() is None:
                process.kill()
                process.wait()
            self.store.record_outcome(attempt, "interrupted", {"reason": "supervisor_interrupted_or_launch_failed"})
            raise
        self.store.record_outcome(attempt, outcome.status, {"reason": outcome.reason, "exit_code": outcome.exit_code})
        return outcome

    def _run_job(self, process, job, admission, control, logs, observe, pause_requested, prior_iterations):
        started = time.monotonic()
        expired = threading.Event()
        log_fault: list[str] = []
        per_log = max(1, min(64 * 1024, admission.limits.storage_bytes // 8))

        def drain(source, output):
            remaining = per_log
            try:
                with source:
                    while chunk := source.read1(64 * 1024):
                        output.write(chunk[:remaining])
                        remaining -= len(chunk)
                        if remaining < 0:
                            log_fault.append("log_capacity")
                            job.terminate()
                            return
            except OSError:
                log_fault.append("log_write_failed")
                job.terminate()

        def stop_at_deadline():
            if process.poll() is None:
                expired.set()
                job.terminate()

        drains = [threading.Thread(target=drain, args=(source, output), daemon=True)
                  for source, output in zip((process.stdout, process.stderr), logs)]
        for thread in drains:
            thread.start()
        watchdog = threading.Timer(admission.limits.wall_seconds, stop_at_deadline)
        watchdog.daemon = True
        watchdog.start()
        try:
            write_record(control / "started.json", {"schema": 1, "pid": process.pid, "max_bytes_per_log": per_log})
            with process.stdin as barrier:
                barrier.write(b"G")
                barrier.flush()
            outcome = self._monitor(process, job, admission, control, observe, pause_requested, prior_iterations, started)
        finally:
            watchdog.cancel()
            # A leader can exit before its descendants. Closing the Job also
            # closes their inherited log pipe handles.
            job.close()
            for thread in drains:
                thread.join(timeout=2)
        if expired.is_set():
            return Outcome("paused", "wall_time_capacity", process.wait())
        if log_fault:
            return Outcome("paused", log_fault[0], process.wait())
        if any(thread.is_alive() for thread in drains):
            return Outcome("interrupted", "log_drain_unconfirmed", process.wait())
        return outcome

    def _monitor(
        self, process: subprocess.Popen, job: WindowsJob, admission: Admission, control: Path,
        observe: Callable[[int], ResourceUse], pause_requested: Callable[[], bool], prior_iterations: int,
        started: float,
    ) -> Outcome:
        deadline = started + admission.limits.wall_seconds
        prior = prior_iterations
        while process.poll() is None:
            reason = None
            urgent = False
            try:
                elapsed = time.monotonic() - started
                if elapsed >= admission.limits.wall_seconds:
                    raise BudgetRefusal("wall_time_capacity")
                reported = observe(process.pid)
                if not isinstance(reported, ResourceUse) or reported.completed_iterations < prior:
                    raise BudgetRefusal("resource_observation_missing_or_regressed")
                check_running(admission, reported, elapsed_seconds=time.monotonic() - started)
                prior = reported.completed_iterations
                if pause_requested() is not False:
                    reason = "pause_requested"
            except BudgetRefusal as error:
                reason = error.code
                urgent = reason in _URGENT or reason.startswith(("invalid_", "resource_observation_"))
            except Exception:
                reason, urgent = "resource_observation_unavailable", True
            if reason:
                # Workers checkpoint only at their own safe update boundary.
                # Hardware and protected-work faults must not wait for it.
                write_record(control / "pause.json", {"schema": 1, "reason": reason})
                remaining = min(admission.limits.checkpoint_grace_seconds, max(0.0, deadline - time.monotonic()))
                if not urgent and remaining > 0:
                    try:
                        return Outcome("paused", reason, process.wait(timeout=remaining))
                    except subprocess.TimeoutExpired:
                        pass
                if process.poll() is None:
                    job.terminate()
                return Outcome("paused", reason, process.wait())
            remaining = max(0.0, deadline - time.monotonic())
            try:
                process.wait(timeout=min(_POLL, remaining))
            except subprocess.TimeoutExpired:
                continue
        code = process.returncode
        return Outcome("passed" if code == 0 else "failed", "worker_process_exited", code)
