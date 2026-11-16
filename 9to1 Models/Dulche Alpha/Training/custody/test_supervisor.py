from dataclasses import replace
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
import zipfile

from custody import ArtifactStore, CustodyError
from custody.registered_runtime import PackagePin, RegisteredRuntime, RegisteredWorker
from custody.resources import HostObservation, Limits, OperatorPolicy, ResourceUse
from custody.supervisor import ProcessSupervisor


_WORKER = b'''import json, os, sys, time
from pathlib import Path

envelope = json.loads(Path(sys.argv[1]).read_text())
request = envelope["request"]
assert os.environ["CUDA_VISIBLE_DEVICES"] == "-1"
assert Path(os.environ["TEMP"]) == Path.cwd() / "temp"
assert all(os.environ[key] == str(envelope["cpu_threads"]) for key in
           ("OMP_NUM_THREADS", "MKL_NUM_THREADS", "OPENBLAS_NUM_THREADS", "DULCHE_TORCH_THREADS"))
print("started registered worker", flush=True)
Path("first-segment.bin").write_bytes(b"checkpoint one")
if request["mode"] == "exit":
    raise SystemExit(3)
if request["mode"] == "descendant":
    import subprocess
    subprocess.Popen([sys.executable, "-I", "-S", "-c",
                      "import time; time.sleep(.4); open('survived.bin', 'wb').write(b'escaped')"])
    raise SystemExit(0)
if request["mode"] == "log-limit":
    print("x" * 10000, flush=True)
if request["mode"] == "imports":
    assert Path.cwd() not in (Path(path) for path in sys.path)
    assert os.environ["CUDA_VISIBLE_DEVICES"] == "-1"
    import runtime_dependency
    assert runtime_dependency.SOURCE == "registered package"
    raise SystemExit(0)
if request["mode"] == "attempt-cuda":
    import torch
    try:
        torch._C._cuda_init()
    except RuntimeError as error:
        assert "forbidden" in str(error)
    else:
        raise AssertionError("The simulated CUDA initialization reached the package")
    raise SystemExit(0)
if request["mode"] == "early-success":
    os._exit(0)
pause = Path(sys.argv[2])
while not pause.exists():
    time.sleep(0.01)
assert json.loads(pause.read_text())["reason"] == "pause_requested"
Path("paused-segment.bin").write_bytes(b"checkpoint two")
'''


class SupervisorTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="dulche-supervision-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.store = ArtifactStore(self.root / "store")
        run = self.store.create_run("Supervise a CPU software fixture", {"purpose": "software_test_only"})
        self.attempt = self.store.begin_attempt(run, {})
        self.work = self.store.attempt_path(self.attempt) / "work"
        self.worker_root = self.root / "worker"
        self.worker_root.mkdir()
        self.source = self.worker_root / "registered_worker.zip"
        with zipfile.ZipFile(self.source, "x") as archive:
            archive.writestr("registered_worker.py", _WORKER)
        self.worker = RegisteredWorker(self.source.name, hashlib.sha256(self.source.read_bytes()).hexdigest(),
                                       "registered_worker", ("registered_worker.py",))
        python = Path(sys.executable).resolve()
        self.runtime = RegisteredRuntime(python, hashlib.sha256(python.read_bytes()).hexdigest(), (), ())
        self.policy = OperatorPolicy((), cpu_threads=1, minimum_free_ram_bytes=32)
        self.supervisor = self.supervisor_for(self.runtime)
        self.host = HostObservation("Fixture CPU", 256 * 1024 * 1024, 64 * 1024,
                                    game_or_protected_work_active=False, logical_cpus=1)
        self.limits = Limits(4, 64 * 1024 * 1024, 0, 32 * 1024, 800, 80, 100, 4)

    def supervisor_for(self, runtime):
        return ProcessSupervisor(self.store, self.worker_root, {
            "fixture": self.worker,
            "wrong-hash": replace(self.worker, sha256="0" * 64),
            "extra-entry": replace(self.worker, source_entries=("registered_worker.py", "unexpected.py")),
        }, self.policy, runtime)

    @staticmethod
    def usage(_):
        return ResourceUse(256, 0, 32, 64 * 1024, 1, game_or_protected_work_active=False, free_ram_bytes=128)

    def run_fixture(self, mode="pause", worker="fixture", **options):
        settings = {"purpose": "software_test_only", "limits": self.limits, "host": self.host,
                    "device_uuid": None, "observe": self.usage}
        settings.update(options)
        return self.supervisor.run(self.attempt, worker, {"mode": mode}, **settings)

    def test_owns_one_process_and_preserves_both_work_checkpoints_on_pause(self):
        pids = []

        def observe(pid):
            pids.append(pid)
            return self.usage(pid)

        result = self.run_fixture(observe=observe, pause_requested=lambda: (self.work / "first-segment.bin").exists())
        self.assertEqual((result.status, result.reason, result.exit_code), ("paused", "pause_requested", 0))
        self.assertEqual(len(set(pids)), 1)
        self.assertEqual((self.work / "first-segment.bin").read_bytes(), b"checkpoint one")
        self.assertEqual((self.work / "paused-segment.bin").read_bytes(), b"checkpoint two")
        self.assertIn("started registered worker", (self.work / "stdout.log").read_text())
        with self.assertRaises(FileExistsError):
            self.run_fixture()
        self.assertEqual(len(self.store.verify_attempt(self.attempt).artifacts), 1)

    def test_rejects_unregistered_or_unverified_executable_before_launch(self):
        for worker in ("unknown", "wrong-hash", "extra-entry"):
            with self.subTest(worker=worker):
                with self.assertRaises(CustodyError):
                    self.run_fixture(worker=worker)
        self.assertFalse((self.work / "stdout.log").exists())

    def test_blocks_changed_source_and_python_pins_before_start(self):
        self.supervisor = self.supervisor_for(replace(self.runtime, sha256="0" * 64))
        with self.assertRaisesRegex(CustodyError, "Python executable"):
            self.run_fixture(mode="exit")
        self.supervisor = self.supervisor_for(self.runtime)
        with self.source.open("ab") as changed:
            changed.write(b"tampered archive")
        with self.assertRaisesRegex(CustodyError, "archive does not match"):
            self.run_fixture(mode="exit")
        self.assertFalse((self.work / "stdout.log").exists())

    def test_child_refuses_archive_changed_after_parent_verification(self):
        verify = self.store.verify

        def change_after_verify(reference):
            path = verify(reference)
            with path.open("ab") as changed:
                changed.write(b"simulated post-verification change")
            return path

        with patch.object(self.store, "verify", side_effect=change_after_verify):
            result = self.run_fixture(mode="exit")
        self.assertEqual((result.status, result.exit_code), ("failed", 1))
        self.assertIn("attempt source archive does not match", (self.work / "stderr.log").read_text())
        self.assertFalse((self.work / "first-segment.bin").exists())

    def test_uses_only_registered_import_paths_and_skips_site_hooks(self):
        dependency = self.root / "site-packages"
        dependency.mkdir()
        metadata = dependency / "runtime_dependency-1.2.0.dist-info"
        metadata.mkdir()
        (metadata / "METADATA").write_text("Name: runtime-dependency\nVersion: 1.2.0\n")
        (dependency / "runtime_dependency.py").write_text('SOURCE = "registered package"\n')
        (dependency / "intrusion.pth").write_text("import intrusion\n")
        for hook in ("intrusion", "sitecustomize", "usercustomize"):
            (dependency / f"{hook}.py").write_text('open("hook-ran", "w").close()\n')
        (self.work / "runtime_dependency.py").write_text("raise RuntimeError('working directory injected')\n")
        pin = PackagePin("runtime-dependency", "runtime_dependency", "1.2.0", 0)
        self.supervisor = self.supervisor_for(replace(self.runtime, dependency_roots=(dependency,), packages=(pin,)))
        result = self.run_fixture(mode="imports")
        self.assertEqual((result.status, result.exit_code), ("passed", 0), (self.work / "stderr.log").read_text())
        self.assertFalse((self.work / "hook-ran").exists())

        self.attempt = self.store.begin_attempt(self.attempt.run_id, {})
        self.work = self.store.attempt_path(self.attempt) / "work"
        wrong_pin = replace(pin, version="1.3.0")
        self.supervisor = self.supervisor_for(replace(self.runtime, dependency_roots=(dependency,), packages=(wrong_pin,)))
        result = self.run_fixture(mode="imports")
        self.assertEqual((result.status, result.exit_code), ("failed", 1))
        self.assertIn("Registered package identity mismatch", (self.work / "stderr.log").read_text())
        self.assertFalse((self.work / "first-segment.bin").exists())

        self.attempt = self.store.begin_attempt(self.attempt.run_id, {})
        self.work = self.store.attempt_path(self.attempt) / "work"
        shadow = self.root / "shadow" / "site-packages"
        shadow.mkdir(parents=True)
        (shadow / "runtime_dependency.py").write_text("raise RuntimeError('shadow package ran')\n")
        misplaced_pin = replace(pin, root_index=1)
        self.supervisor = self.supervisor_for(replace(self.runtime, dependency_roots=(shadow, dependency),
                                                      packages=(misplaced_pin,)))
        result = self.run_fixture(mode="imports")
        self.assertEqual((result.status, result.exit_code), ("failed", 1))
        self.assertIn("Registered package identity mismatch", (self.work / "stderr.log").read_text())
        self.assertFalse((self.work / "first-segment.bin").exists())

    def test_cpu_guard_refuses_simulated_cuda_init_even_if_worker_catches_it(self):
        dependency = self.root / "site-packages"
        metadata = dependency / "torch-1.0.dist-info"
        metadata.mkdir(parents=True)
        (metadata / "METADATA").write_text("Name: torch\nVersion: 1.0\n")
        (dependency / "torch.py").write_text(
            "from types import SimpleNamespace\n"
            "cuda = SimpleNamespace(is_initialized=lambda: False)\n"
            "def set_default_device(device):\n"
            "    assert device == 'cpu'\n"
            "def unsafe_init():\n"
            "    open('unsafe-init-reached', 'x').close()\n"
            "_C = SimpleNamespace(_cuda_init=unsafe_init)\n"
        )
        self.supervisor = self.supervisor_for(replace(
            self.runtime, dependency_roots=(dependency,), packages=(PackagePin("torch", "torch", "1.0", 0),)
        ))
        result = self.run_fixture(mode="attempt-cuda")
        self.assertEqual((result.status, result.exit_code), ("failed", 1))
        self.assertFalse((self.work / "unsafe-init-reached").exists())

        control = self.work.parent / "control"
        initial = json.loads((control / "cpu-runtime-start.json").read_text())
        final = json.loads((control / "cpu-runtime-finish.json").read_text())
        self.assertEqual(initial, {"schema": 1, "cuda_mask": "-1", "cuda_initialized": False, "init_refusals": 0})
        self.assertEqual(final, {**initial, "init_refusals": 1})

        self.attempt = self.store.begin_attempt(self.attempt.run_id, {})
        self.work = self.store.attempt_path(self.attempt) / "work"
        result = self.run_fixture(mode="early-success")
        self.assertEqual((result.status, result.reason, result.exit_code),
                         ("interrupted", "cpu_runtime_state_unconfirmed", 0))

    def test_monitors_real_child_exit_and_owns_hard_wall_deadline(self):
        failed = self.run_fixture(mode="exit")
        self.assertEqual((failed.status, failed.exit_code), ("failed", 3))
        self.attempt = self.store.begin_attempt(self.attempt.run_id, {})
        self.work = self.store.attempt_path(self.attempt) / "work"

        def slow_observer(pid):
            time.sleep(0.3)
            return self.usage(pid)

        ended = self.run_fixture(observe=slow_observer, limits=replace(self.limits, wall_seconds=0.1,
                                                                        checkpoint_grace_seconds=0))
        self.assertEqual((ended.status, ended.reason), ("paused", "wall_time_capacity"))
        self.assertNotEqual(ended.exit_code, 0)

    def test_descendant_cannot_outlive_registered_leader_and_logs_are_bounded(self):
        result = self.run_fixture(mode="descendant")
        self.assertEqual((result.status, result.exit_code), ("passed", 0))
        time.sleep(0.5)
        self.assertFalse((self.work / "survived.bin").exists())
        self.attempt = self.store.begin_attempt(self.attempt.run_id, {})
        self.work = self.store.attempt_path(self.attempt) / "work"
        result = self.run_fixture(mode="log-limit")
        self.assertEqual((result.status, result.reason), ("paused", "log_capacity"))
        self.assertEqual((self.work / "stdout.log").stat().st_size, self.limits.storage_bytes // 8)
        self.assertEqual((self.work / "first-segment.bin").read_bytes(), b"checkpoint one")
