"""CPU-only protocol tests: mock GPU admission, never initialize an accelerator."""
from contextlib import contextmanager
import copy
import json
import os
from pathlib import Path
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock

from hf_backend.cpu_guard import install
_GUARD = install()

from . import compute


@contextmanager
def _supervised_fixture():
    with tempfile.TemporaryDirectory(prefix="alora-compute-protocol-") as directory:
        job = "a" * 32
        root = Path(directory) / "training" / "jobs" / job
        output = root / "output"
        output.mkdir(parents=True)
        unit = f"dulche-training-{job}-worker.service"
        status = {
            "id": job, "device": compute._RTX_UUID, "worker_pid": os.getpid(),
            "worker_unit": unit, "supervisor_unit": f"dulche-training-{job}.service",
            "workflow_digest": "b" * 64, "phase": "running",
        }
        launch = {"device": compute._RTX_UUID, "gpu_minor": 0}
        command = {"job": job, "action": "run", "permit_until_monotonic": time.monotonic() + 0.8}
        for name, value in (("status.json", status), ("launch.json", launch), ("control.json", command)):
            (root / name).write_text(json.dumps(value))
            (root / name).chmod(0o600)
        environment = {
            "DULCHE_TRAIN_JOB": job,
            "DULCHE_TRAIN_CONTROL": str(root / "control.json"),
            "DULCHE_TRAIN_OUTPUT": str(output),
            "CUDA_VISIBLE_DEVICES": compute._RTX_UUID,
        }
        with mock.patch.dict(os.environ, environment), mock.patch.object(
            compute, "_worker_cgroup", return_value=f"0::/user.slice/app.slice/{unit}\n"
        ):
            yield root, status, launch, command


def _write(root, name, value):
    (root / name).write_text(json.dumps(value))
    (root / name).chmod(0o600)


class ComputeProtocolTests(unittest.TestCase):
    def test_cpu_scope_nesting_and_exception_restoration(self):
        calls = []
        self.assertIsNone(compute.current_compute_callback())
        compute.check_compute(device="cpu")
        with compute.compute_scope(lambda: calls.append("outer")):
            compute.check_compute(device="cpu")
            with compute.compute_scope(None):
                compute.check_compute(device="cpu")
            with self.assertRaisesRegex(RuntimeError, "stop"):
                with compute.compute_scope(lambda: calls.append("inner")):
                    compute.check_compute(device="cpu")
                    raise RuntimeError("stop")
            compute.check_compute(device="cpu")
        self.assertEqual(calls, ["outer", "outer", "inner", "outer"])
        self.assertIsNone(compute.current_compute_callback())

    def test_captured_callback_can_reenter_on_an_engine_thread(self):
        calls = []
        with compute.compute_scope(lambda: calls.append(threading.get_ident())):
            captured = compute.current_compute_callback()
        observed = []
        def worker():
            observed.append(compute.current_compute_callback())
            with compute.compute_scope(captured, device="cpu"):
                compute.check_compute()
            observed.append(compute.current_compute_callback())
        thread = threading.Thread(target=worker)
        thread.start()
        thread.join(timeout=5)
        self.assertFalse(thread.is_alive())
        self.assertEqual(observed, [None, None])
        self.assertEqual(len(calls), 1)
        self.assertNotEqual(calls[0], threading.get_ident())

    def test_unscoped_cuda_refuses_before_any_fence(self):
        with mock.patch.object(compute, "_fence_cuda") as fence:
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.check_compute(device="cuda:0")
            fence.assert_not_called()
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def test_exact_supervised_files_validate_without_gpu_calls(self):
        with _supervised_fixture():
            result = compute.validate_cuda_admission("cuda:0")
            self.assertEqual(result["physical_gpu_uuid"], compute._RTX_UUID)
            self.assertEqual(result["logical_device"], "cuda:0")
            self.assertEqual(result["workflow_sha256"], "b" * 64)
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def test_bad_identity_paths_visibility_and_cgroup_refused_before_fence(self):
        changes = (
            ("status.json", "worker_pid", os.getpid() + 1),
            ("status.json", "device", "GPU-11111111-1111-1111-1111-111111111111"),
            ("status.json", "workflow_digest", "not-a-digest"),
            ("launch.json", "gpu_minor", None),
            ("launch.json", "device", "cpu"),
            ("control.json", "job", "c" * 32),
        )
        for name, key, value in changes:
            with self.subTest(name=name, key=key), _supervised_fixture() as (root, status, launch, command):
                doc = {"status.json": status, "launch.json": launch, "control.json": command}[name]
                doc[key] = value
                _write(root, name, doc)
                with mock.patch.object(compute, "_fence_cuda") as fence:
                    with compute.compute_scope(lambda: None), self.assertRaises(compute.ComputeAdmissionError):
                        compute.check_compute(device="cuda:0")
                    fence.assert_not_called()
        with _supervised_fixture(), mock.patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": "0"}):
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.validate_cuda_admission()
        with _supervised_fixture(), mock.patch.object(compute, "_worker_cgroup", return_value="0::/unrelated\n"):
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.validate_cuda_admission()

    def test_expired_wait_unready_and_far_future_permits_refused(self):
        for field, value in (("permit_until_monotonic", 0), ("action", "wait"),
                             ("permit_until_monotonic", time.monotonic() + 100)):
            with self.subTest(field=field), _supervised_fixture() as (root, status, launch, command):
                command[field] = value
                _write(root, "control.json", command)
                with self.assertRaises(compute.ComputeAdmissionError):
                    compute.validate_cuda_admission()
        with _supervised_fixture() as (root, status, _, _):
            status["phase"] = "starting"
            _write(root, "status.json", status)
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.validate_cuda_admission()

    def test_document_links_and_writable_custody_refused(self):
        with _supervised_fixture() as (root, status, _, _):
            target = root / "other-status.json"
            target.write_text(json.dumps(status))
            (root / "status.json").unlink()
            (root / "status.json").symlink_to(target)
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.validate_cuda_admission()
        with _supervised_fixture() as (root, _, _, _):
            (root / "launch.json").chmod(0o666)
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.validate_cuda_admission()

    def test_cuda_boundary_order_validates_before_fencing_and_renews_after(self):
        calls = []
        def admission(device, *, require_permit=True):
            calls.append("permit" if require_permit else "identity")
            return {}
        with mock.patch.object(compute, "validate_cuda_admission", side_effect=admission), \
             mock.patch.object(compute, "reference_kernel_policy", side_effect=lambda: calls.append("kernel")), \
             mock.patch.object(compute, "_fence_cuda", side_effect=lambda d: calls.append("fence")):
            with compute.compute_scope(lambda: calls.append("callback")):
                compute.check_compute(device="cuda:0")
        self.assertEqual(calls, ["identity", "callback", "permit", "kernel", "fence", "callback", "permit"])
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def test_reference_kernel_policy_requires_preimport_disable_and_no_optional_packages(self):
        hub = SimpleNamespace(_kernels_enabled=False, __file__=__file__)
        environment = {"USE_HUB_KERNELS": "NO", "NVIDIA_TF32_OVERRIDE": "0"}
        with mock.patch.dict(os.environ, environment), \
             mock.patch.dict(compute.sys.modules, {"transformers.integrations.hub_kernels": hub}), \
             mock.patch.object(compute.importlib.util, "find_spec", return_value=None), \
             mock.patch.object(compute, "_KERNEL_CACHE", None):
            policy = compute.reference_kernel_policy()
            self.assertEqual(policy["kind"], "native-torch-reference-fp32")
            self.assertEqual(len(policy["hub_dispatch_sha256"]), 64)
            hub._kernels_enabled = True
            with self.assertRaises(compute.ComputeAdmissionError):
                compute.reference_kernel_policy()
            hub._kernels_enabled = False
            with mock.patch.dict(os.environ, {"USE_HUB_KERNELS": "YES"}):
                with self.assertRaises(compute.ComputeAdmissionError):
                    compute.reference_kernel_policy()
            with mock.patch.dict(compute.sys.modules, {"fla": SimpleNamespace()}):
                with self.assertRaises(compute.ComputeAdmissionError):
                    compute.reference_kernel_policy()


if __name__ == "__main__":
    unittest.main()
