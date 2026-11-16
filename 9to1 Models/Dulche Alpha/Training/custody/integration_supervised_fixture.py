"""Run the two-process CPU checkpoint fixture using a local operator JSON file."""

from dataclasses import asdict
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
from uuid import uuid4
import zipfile

from .artifacts import ArtifactRef, ArtifactStore, create_directory
from .registered_runtime import PackagePin, RegisteredRuntime, RegisteredWorker
from .resources import HostObservation, Limits, OperatorPolicy, ResourceUse
from .supervisor import ProcessSupervisor

_MAX_SNAPSHOT = 100_000
_ENTRIES = ("efficient/__init__.py", "efficient/core.py", "custody/__init__.py",
            "custody/artifacts.py", "custody/snapshot.py", "custody/test_snapshot.py", "checkpoint_fixture.py")
_WORKER = """\
import json
import os
from dataclasses import asdict
from pathlib import Path
import sys

import torch
from torch import nn
from custody import ArtifactRef, ArtifactStore, AttemptRef
from custody.snapshot import load_snapshot, publish_snapshot
from efficient import Config, CpuTrainer

envelope = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
assert envelope["purpose"] == "software_test_only"
assert os.environ["CUDA_VISIBLE_DEVICES"] == "-1"
assert not torch.cuda.is_initialized()
torch.set_num_threads(int(os.environ["DULCHE_TORCH_THREADS"]))
if envelope["request"]["mode"] == "snapshot-tests":
    import unittest

    os.environ["DULCHE_TEST_ARTIFACT_ROOT"] = str(Path.cwd())
    suite = unittest.defaultTestLoader.loadTestsFromName("custody.test_snapshot")
    raise SystemExit(not unittest.TextTestRunner(verbosity=2).run(suite).wasSuccessful())

torch.manual_seed(148)
attempt = AttemptRef(envelope["run_id"], envelope["attempt_id"])
store = ArtifactStore(Path.cwd().parents[4])
assert store.attempt_path(attempt) / "work" == Path.cwd()
saved = []

def checkpoint(state, step):
    artifact = publish_snapshot(store, attempt, f"step-{step.update}", state,
                                max_bytes=100000, kind="segment_checkpoint")
    saved.append(asdict(artifact))

model = nn.Sequential(nn.Linear(2, 3), nn.Tanh(), nn.Linear(3, 1)).double().cpu()
blocks = (("0.weight", "0.bias"), ("2.weight", "2.bias"))
trainer = CpuTrainer(model, blocks, Config(mode="block_coordinate", optimizer="adam"),
                     on_update=checkpoint)
inputs = torch.tensor([[1, 2], [2, 3]], dtype=torch.float64, device="cpu")
targets = torch.tensor([[0], [1]], dtype=torch.float64, device="cpu")
resume = envelope["request"]["resume"]
pause = Path(sys.argv[2])
try:
    if resume is None:
        trainer.advance(inputs, targets, cancelled=pause.exists)
    else:
        trainer.restore(load_snapshot(store, ArtifactRef(**resume), max_bytes=100000))
    trainer.advance(inputs, targets, cancelled=pause.exists)
except InterruptedError:
    if not pause.exists():
        raise
    raise SystemExit(0)
assert trainer.snapshot()["updates"] == 2
with Path("checkpoint-receipt.json").open("x", encoding="utf-8") as stream:
    json.dump(saved, stream)
"""


def _checkpoint_members(store, reference):
    if reference.size_bytes > _MAX_SNAPSHOT:
        raise AssertionError("The software fixture checkpoint is unexpectedly large.")
    with zipfile.ZipFile(store.verify(reference)) as archive:
        return {name: archive.read(name) for name in archive.namelist()}


def run_fixture(config_path: Path):
    if os.name != "nt":
        raise RuntimeError("This fixture requires the Windows Job launcher.")
    config = json.loads(config_path.read_text(encoding="utf-8"))
    runtime = RegisteredRuntime(
        Path(config["python"]), config["python_sha256"],
        tuple(Path(root) for root in config["dependency_roots"]),
        tuple(PackagePin(**pin) for pin in config["packages"]),
    )
    runtime.verified_record(())
    protected = config.get("protected_process_images")
    if (type(protected) is not list or not 1 <= len(protected) <= 32
            or any(type(name) is not str or not re.fullmatch(r"[A-Za-z0-9_. -]{1,124}\.exe", name)
                   for name in protected)):
        raise ValueError("Register the protected process images before running a live CPU fixture.")
    parent = Path(config["fixture_artifact_parent"])
    create_directory(parent)
    directory = parent / str(uuid4())
    create_directory(directory, exclusive=True)
    print(f"Software fixture evidence: {directory}", flush=True)
    _exercise_fixture(runtime, directory, protected)


def _exercise_fixture(runtime, root, protected_images):
    from worker_api.windows_host import WindowsHost

    host_monitor = WindowsHost(frozenset(name.casefold() for name in protected_images))

    store = ArtifactStore(root / "store")
    source = root / "source"
    source.mkdir()
    bundle = source / "checkpoint-fixture.zip"
    training = Path(__file__).resolve().parents[1]
    with zipfile.ZipFile(bundle, "x", compression=zipfile.ZIP_DEFLATED) as archive:
        for entry in _ENTRIES[:-1]:
            archive.write(training / entry, entry)
        archive.writestr(_ENTRIES[-1], _WORKER)
    worker_hash = hashlib.sha256(bundle.read_bytes()).hexdigest()
    worker = RegisteredWorker(bundle.name, worker_hash, "checkpoint_fixture", _ENTRIES)
    policy = OperatorPolicy((), cpu_threads=1, minimum_free_ram_bytes=512 * 1024**2)
    limits = Limits(45, 4 * 1024**3, 0, 32 * 1024**2, 128 * 1024**2, 80, 100, 2)
    supervisor = ProcessSupervisor(store, source, {"checkpoint-fixture": worker}, policy, runtime)
    run = store.create_run("Two-process CPU resume verification", {"purpose": "software_test_only"})

    def execute(mode, resume=None):
        inputs = (resume,) if resume is not None else ()
        attempt = store.begin_attempt(run, {"fixture": mode, "purpose": "software_test_only"}, resume_from=inputs)
        work = store.attempt_path(attempt) / "work"
        (work / "efficient.py").write_text("raise RuntimeError('checkout-independent import failed')\n")
        free_storage = shutil.disk_usage(root).free
        host = HostObservation("Software fixture CPU", host_monitor.free_ram(), free_storage,
                               game_or_protected_work_active=host_monitor.protected_active(), logical_cpus=os.cpu_count())

        def observe(pid):
            ram = host_monitor.process_ram(pid)
            used = 0
            for file in work.parent.rglob("*"):
                try:
                    used += file.stat().st_size if file.is_file() else 0
                except FileNotFoundError:
                    # Publication replaces staged names while the observer scans.
                    continue
            return ResourceUse(ram, 0, used, shutil.disk_usage(root).free, 0,
                               game_or_protected_work_active=host_monitor.protected_active(), free_ram_bytes=host_monitor.free_ram())

        result = supervisor.run(
            attempt, "checkpoint-fixture", {"mode": mode, "resume": asdict(resume) if resume else None},
            purpose="software_test_only", limits=limits, host=host, device_uuid=None, observe=observe,
            cpu_rate_hundredths=1000,
        )
        if result.status != "passed":
            raise AssertionError(f"{result}: {(work / 'stderr.log').read_text(encoding='utf-8')}")
        for phase in ("start", "finish"):
            path = work.parent / "control" / f"cpu-runtime-{phase}.json"
            receipt = json.loads(path.read_text(encoding="utf-8"))
            if receipt != {"schema": 1, "cuda_mask": "-1", "cuda_initialized": False, "init_refusals": 0}:
                raise AssertionError("The CPU worker reported unexpected CUDA state.")
        verified = store.verify_attempt(attempt)
        if mode == "snapshot-tests":
            if (verified.incomplete_artifact_ids or len(verified.artifacts) != 1
                    or verified.artifacts[0].sha256 != worker_hash):
                raise AssertionError("The snapshot test worker did not preserve its approved source.")
            return ()
        envelope = json.loads((work / "checkpoint-receipt.json").read_text(encoding="utf-8"))
        refs = tuple(ArtifactRef(**ref) for ref in envelope)
        if (verified.incomplete_artifact_ids
                or len(verified.artifacts) != len(refs) + 1
                or not set(refs).issubset(verified.artifacts)
                or worker_hash not in {artifact.sha256 for artifact in verified.artifacts}):
            raise AssertionError("The attempt source or checkpoint evidence is incomplete.")
        return refs

    checkpoint, continuous = execute("continuous")
    resumed, = execute("resume", checkpoint)
    if _checkpoint_members(store, continuous) != _checkpoint_members(store, resumed):
        raise AssertionError("Checkpoint metadata or tensor bytes differ after second-process resume.")
    print("Two Windows Job workers reproduced the second CPU update with identical checkpoint state.")
    execute("snapshot-tests")
    print("Both snapshot tests passed in a third Windows Job. All fixture artifacts were retained.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Supply the operator runtime config path for this software fixture.")
    run_fixture(Path(sys.argv[1]))
