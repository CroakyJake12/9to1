"""Apply the Studio request to the existing artifact store and process supervisor."""

from dataclasses import asdict
import heapq
import itertools
import json
import os
from pathlib import Path
import platform
import re
import shutil
import time

from custody import ArtifactStore, CustodyError
from custody.artifacts import _open_regular, _read_record, canonical_id, checked_path, write_record
from custody.integration_supervised_fixture import _checkpoint_members
from custody.registered_runtime import PackagePin, RegisteredRuntime, RegisteredWorker
from custody.resources import BudgetRefusal, HostObservation, Limits, OperatorPolicy, ResourceUse, admit
from custody.supervisor import ProcessSupervisor, _cpu_runtime_verified

from .prepare_bundle import ENTRIES, ENTRY
from .protocol import (
    CHECKPOINT_BYTES, JOB, PURPOSE, REQUEST_BYTES, SHA256, WorkerError, attempt_record,
    attempt_ref, checkpoint_ref, decode, result_page,
)
from .windows_host import WindowsHost

_IMAGE = re.compile(r"[A-Za-z0-9_. -]{1,124}\.exe", re.IGNORECASE)
_MAX_SOURCE_BYTES = 1024 * 1024
_LIMITS = Limits(45, 4 * 1024**3, 0, 32 * 1024**2, 128 * 1024**2, 80, 100, 3)
_POLICY = OperatorPolicy((), cpu_threads=1, minimum_free_ram_bytes=512 * 1024**2)
_MAX_REPORT_DELAY = _LIMITS.wall_seconds + 20
_PAUSE_REQUEST = "studio-pause-request.json"
_CPU_RATE_HUNDREDTHS = 1_000


def _absolute(value):
    path = Path(value)
    if not path.is_absolute():
        raise CustodyError("Operator paths must be absolute.")
    return path


def _pin(value):
    if type(value) is not dict or value.keys() != {"distribution", "module", "version", "root_index"}:
        raise CustodyError("An exact dependency pin is required.")
    if any(type(value[key]) is not str for key in ("distribution", "module", "version")):
        raise CustodyError("A dependency pin requires string fields.")
    return PackagePin(**value)


class WorkerApi:
    def __init__(self, config_path: Path):
        try:
            with _open_regular(config_path) as stream:
                data = stream.read(REQUEST_BYTES + 1)
            if len(data) > REQUEST_BYTES:
                raise CustodyError("The operator config is too large.")
            config = decode(data)
            fields = {"schema", "artifact_root", "worker_bundle", "worker_sha256", "python",
                      "python_sha256", "dependency_roots", "packages", "protected_process_images"}
            if config.keys() != fields or type(config["schema"]) is not int or config["schema"] != 1:
                raise CustodyError("Invalid operator config schema.")
            if any(type(config[key]) is not list for key in ("dependency_roots", "packages", "protected_process_images")):
                raise CustodyError("Operator configuration requires arrays for roots, pins and protected images.")
            images = config["protected_process_images"]
            if not 1 <= len(images) <= 32 or any(type(name) is not str or not _IMAGE.fullmatch(name) for name in images):
                raise CustodyError("Known protected process images are required in operator configuration.")
            root = _absolute(config["artifact_root"])
            bundle = _absolute(config["worker_bundle"])
            checked_path(root, directory=True)
            checked_path(root / "runs", directory=True)
            if bundle.is_relative_to(root):
                raise CustodyError("The approved source bundle is independent of attempt storage.")
            self.runtime = RegisteredRuntime(
                _absolute(config["python"]), config["python_sha256"],
                tuple(_absolute(item) for item in config["dependency_roots"]),
                tuple(_pin(item) for item in config["packages"]),
            )
            if not any(pin.module == "torch" for pin in self.runtime.packages):
                raise CustodyError("The CPU self-check requires the guarded Torch registration.")
            self.worker = RegisteredWorker(bundle.name, config["worker_sha256"], ENTRY, ENTRIES)
            if type(self.worker.sha256) is not str or not SHA256.fullmatch(self.worker.sha256):
                raise CustodyError("The approved bundle hash is required.")
            self.worker_root = bundle.parent
            self.store = ArtifactStore(root)
            self.host = WindowsHost(frozenset(name.casefold() for name in images))
            self.supervisor = ProcessSupervisor(self.store, self.worker_root, {JOB: self.worker}, _POLICY, self.runtime)
        except WorkerError:
            raise
        except (OSError, ValueError, TypeError, RecursionError) as error:
            raise WorkerError("operator_config_invalid", "The registered local worker configuration is unavailable.") from error

    def _host_observation(self):
        return HostObservation(
            platform.machine() or "Windows CPU", self.host.free_ram(), shutil.disk_usage(self.store.root).free,
            game_or_protected_work_active=self.host.protected_active(), logical_cpus=os.cpu_count(),
        )

    def _verify_registration(self):
        try:
            self.runtime.verified_record(self.supervisor._excluded_roots)
            self.worker.verified_bundle(self.worker_root, {pin.module for pin in self.runtime.packages})
        except (OSError, ValueError) as error:
            raise WorkerError("registration_invalid", "The approved local Python or source bundle could not be verified.") from error

    def readiness(self):
        self._verify_registration()
        try:
            with self.host.single_job(self.store.root):
                admit(_LIMITS, self._host_observation(), policy=_POLICY, device_uuid=None, purpose=PURPOSE)
            ready = True
        except (BudgetRefusal, WorkerError, OSError):
            ready = False
        return {"ready": ready, "job": JOB, "purpose": PURPOSE}

    def _attempt_info(self, reference):
        try:
            location = self.store.attempt_path(reference)
        except FileNotFoundError as error:
            raise WorkerError("attempt_not_found", "The retained self-check attempt was not found.") from error
        record = _read_record(location / "attempt.json")
        config = record.get("configuration")
        if (type(config) is not dict or config.get("job") != JOB or config.get("purpose") != PURPOSE
                or config.get("worker_api") != 1):
            raise WorkerError("attempt_not_found", "The retained self-check attempt was not found.")
        return location, record

    def _sealed_evidence(self, reference, location, bundle_hash):
        directory = location / "artifacts"
        checked_path(directory, directory=True)
        entries = tuple(itertools.islice(directory.iterdir(), 4))
        if len(entries) > 3:
            raise CustodyError("Too many artifacts in a CPU self-check.")
        artifacts, steps = [], {}
        sources = 0
        for item in sorted(entries):
            try:
                ref = self.store._manifest_ref(reference, item.name)
                manifest = _read_record(item / "manifest.json", _read_record(item / "seal.json")["sha256"])
            except FileNotFoundError:
                continue  # Private staging can persist on an interrupted attempt.
            name, kind = manifest.get("name"), manifest.get("kind")
            if (name == "supervised-source.zip" and kind == "script"
                    and ref.sha256 == bundle_hash and ref.size_bytes <= _MAX_SOURCE_BYTES and sources == 0):
                sources += 1
            elif (name in ("step-1", "step-2") and kind == "segment_checkpoint"
                  and 1 <= ref.size_bytes <= CHECKPOINT_BYTES and name not in steps):
                steps[name] = ref
            else:
                raise CustodyError("Unexpected evidence in a CPU self-check.")
            self.store.verify(ref)
            artifacts.append(ref)
        return tuple(artifacts), steps, sources

    def _result(self, reference, independent_check=True):
        location, record = self._attempt_info(reference)
        config = record["configuration"]
        try:
            artifacts, steps, sources = self._sealed_evidence(reference, location, config["bundle_sha256"])
            resumed = record["resume_from"]
            updates = 2 if "step-2" in steps else 1 if "step-1" in steps or resumed else 0
            try:
                outcome = _read_record(location / "outcome.json")
            except FileNotFoundError:
                seconds = max(0.0, time.time() - (location / "attempt.json").stat().st_mtime)
                if seconds > _MAX_REPORT_DELAY:
                    return attempt_record(reference, "interrupted", reason="outcome_missing",
                                          updates=updates, artifacts=artifacts)
                status = "running" if (location / "control" / "started.json").exists() else "prepared"
                return attempt_record(reference, status, updates=updates, artifacts=artifacts)
            status = outcome["status"]
            if (outcome["run_id"], outcome["attempt_id"]) != (reference.run_id, reference.attempt_id):
                raise CustodyError("The attempt outcome belongs to another attempt.")
            if status not in ("passed", "failed", "paused", "interrupted", "cancelled"):
                raise CustodyError("Unexpected self-check outcome.")
            reason = outcome["details"].get("reason")
            if type(reason) is not str or len(reason) > 100:
                raise CustodyError("Invalid self-check outcome reason.")
            if status != "passed":
                return attempt_record(reference, status, reason=reason, updates=updates, artifacts=artifacts)

            expected = {"step-2"} if resumed else {"step-1", "step-2"}
            if (sources != 1 or steps.keys() != expected or len(resumed) > 1
                    or outcome["details"].get("exit_code") != 0
                    or not _cpu_runtime_verified(location / "control")):
                raise CustodyError("The successful attempt is missing its CPU checkpoint evidence.")
            with _open_regular(location / "work" / "checkpoint-receipt.json") as stream:
                raw = stream.read(REQUEST_BYTES + 1)
            if len(raw) > REQUEST_BYTES:
                raise CustodyError("The worker checkpoint receipt is too large.")
            # The fixture's receipt is a JSON array, and each published ref is independently verified above.
            if json.loads(raw) != [asdict(steps[name]) for name in sorted(steps)]:
                raise CustodyError("The worker did not acknowledge these checkpoints.")
            if resumed and independent_check:
                previous = checkpoint_ref(resumed[0])
                base_location, base_record = self._attempt_info(previous.attempt)
                if base_record["resume_from"] or base_record["configuration"]["bundle_sha256"] != config["bundle_sha256"]:
                    raise CustodyError("The CPU checkpoint does not belong to the uninterrupted baseline.")
                original_result = self._result(previous.attempt, independent_check=False)
                if original_result["resume_checkpoint"] != asdict(previous):
                    raise CustodyError("The uninterrupted CPU checkpoint is unverified.")
                _, original, _ = self._sealed_evidence(previous.attempt, base_location, config["bundle_sha256"])
                if _checkpoint_members(self.store, original["step-2"]) != _checkpoint_members(self.store, steps["step-2"]):
                    return attempt_record(reference, "failed", reason="resume_mismatch", artifacts=artifacts)
            return attempt_record(reference, "passed", reason=reason, updates=2,
                                  artifacts=artifacts, resume=steps.get("step-1"))
        except (OSError, ValueError, KeyError, TypeError) as error:
            if isinstance(error, WorkerError):
                raise
            return attempt_record(reference, "interrupted", reason="evidence_integrity_failed")

    def status(self, reference):
        return self.readiness() if reference is None else self._result(reference)

    def pause(self, reference):
        location, _ = self._attempt_info(reference)
        current = self._result(reference)
        if current["status"] in ("prepared", "running"):
            record = {"schema": 1, **asdict(reference)}
            requested = location / "control" / _PAUSE_REQUEST
            try:
                write_record(requested, record)
            except FileExistsError:
                if _read_record(requested) != record:
                    raise CustodyError("The retained pause request is invalid.")
        return self._result(reference)

    def list(self, limit, after):
        def attempts():
            for run in (self.store.root / "runs").iterdir():
                try:
                    run_id = canonical_id(run.name)
                    checked_path(run / "attempts", directory=True)
                except (CustodyError, FileNotFoundError):
                    continue
                for candidate in (run / "attempts").iterdir():
                    try:
                        reference = attempt_ref({"run_id": run_id, "attempt_id": candidate.name})
                        if after and (reference.run_id, reference.attempt_id) <= (after.run_id, after.attempt_id):
                            continue
                        self._attempt_info(reference)
                    except (WorkerError, CustodyError, FileNotFoundError):
                        continue
                    yield reference

        refs = heapq.nsmallest(limit + 1, attempts(), key=lambda ref: (ref.run_id, ref.attempt_id))
        return result_page(refs, limit, self._result)

    def execute(self, checkpoint, emit):
        self._verify_registration()
        with self.host.single_job(self.store.root):
            if checkpoint is not None:
                try:
                    _, original = self._attempt_info(checkpoint.attempt)
                    approved = self._result(checkpoint.attempt)["resume_checkpoint"]
                    if original["configuration"]["bundle_sha256"] != self.worker.sha256 or approved != asdict(checkpoint):
                        raise CustodyError("Incompatible checkpoint.")
                except (WorkerError, CustodyError, OSError) as error:
                    raise WorkerError("checkpoint_ineligible", "Select a verified step-one CPU self-check checkpoint.") from error
                run = checkpoint.run_id
            else:
                run = self.store.create_run("Dulche engine CPU self-check", {"job": JOB, "purpose": PURPOSE})
            reference = self.store.begin_attempt(
                run, {"worker_api": 1, "job": JOB, "purpose": PURPOSE, "bundle_sha256": self.worker.sha256},
                resume_from=(checkpoint,) if checkpoint is not None else (),
            )
            location = self.store.attempt_path(reference)
            last_update = 1 if checkpoint is not None else 0

            def pause_requested():
                try:
                    request = _read_record(location / "control" / _PAUSE_REQUEST)
                except FileNotFoundError:
                    return False
                if request != {"schema": 1, **asdict(reference)}:
                    raise CustodyError("The retained pause request is invalid.")
                return True

            def observe(pid):
                nonlocal last_update
                artifacts, steps, _ = self._sealed_evidence(reference, location, self.worker.sha256)
                updates = 2 if "step-2" in steps else 1 if "step-1" in steps or checkpoint is not None else 0
                if updates > last_update:
                    last_update = updates
                    emit("progress", attempt=attempt_record(reference, "running", updates=updates, artifacts=artifacts))
                used = 0
                for file in location.rglob("*"):
                    try:
                        used += file.stat().st_size if file.is_file() else 0
                    except FileNotFoundError:
                        continue  # Artifact publication replaces the staged name during the scan.
                return ResourceUse(
                    self.host.process_ram(pid), 0, used, shutil.disk_usage(self.store.root).free, updates,
                    game_or_protected_work_active=self.host.protected_active(), free_ram_bytes=self.host.free_ram(),
                )

            try:
                emit("accepted", attempt=asdict(reference))
                self.supervisor.run(
                    reference, JOB, {"mode": "resume" if checkpoint else "continuous",
                                     "resume": asdict(checkpoint) if checkpoint else None},
                    purpose=PURPOSE, limits=_LIMITS, host=self._host_observation(), device_uuid=None,
                    observe=observe, prior_iterations=last_update, pause_requested=pause_requested,
                    cpu_rate_hundredths=_CPU_RATE_HUNDREDTHS,
                )
            except BudgetRefusal as error:
                self.store.record_outcome(reference, "paused", {"reason": error.code})
            except (OSError, CustodyError):
                if not (location / "outcome.json").exists():
                    self.store.record_outcome(reference, "interrupted", {"reason": "local_worker_unavailable"})
            return self._result(reference)
