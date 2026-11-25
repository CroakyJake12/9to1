"""Cooperative compute boundaries and existing Dulche GPU-context validation.

Importing this module neither imports Torch nor probes an accelerator. CUDA is
available only to the exact currently supervised local RTX worker, with a fresh
permit and the explicitly pinned native reference-kernel policy. This verifies
the existing supervisor contract; it does not grant a lease or issue a permit.
"""
from __future__ import annotations

from contextlib import contextmanager
from contextvars import ContextVar
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import stat
import sys
import time

_CALLBACK = ContextVar("alora_compute_callback", default=None)
_DEVICE = ContextVar("alora_compute_device", default=None)
_JOB = re.compile(r"[0-9a-f]{32}")
_SHA = re.compile(r"[0-9a-f]{64}")
# Existing local physical_admission.training_claim accepts this registered RTX.
# A different device requires its own reviewed physical admission mapping.
_RTX_UUID = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"
_MAX_DOCUMENT_BYTES = 256 * 1024
_KERNEL_CACHE = None


class ComputeAdmissionError(RuntimeError):
    """The existing supervised compute context is missing or no longer valid."""


def current_compute_callback():
    """Capture this callback in custom autograd contexts for engine threads."""
    return _CALLBACK.get()


@contextmanager
def compute_scope(check_permit, *, device=None):
    """Install a per-context callback; nested scopes restore their predecessor.

    A None callback preserves an enclosing scope and otherwise permits only CPU
    work. Backward implementations must re-enter with their captured callback,
    because Python ContextVars are not automatically inherited by engine threads.
    """
    if check_permit is not None and not callable(check_permit):
        raise ValueError("A compute permit callback must be callable or None")
    callback = current_compute_callback() if check_permit is None else check_permit
    callback_token = _CALLBACK.set(callback)
    device_token = _DEVICE.set(_DEVICE.get() if device is None else str(device))
    try:
        yield
    finally:
        _DEVICE.reset(device_token)
        _CALLBACK.reset(callback_token)


def _read_json(path):
    """Read a bounded, regular, user-owned supervisor file without following links."""
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
        with os.fdopen(fd, "rb") as stream:
            before = os.fstat(stream.fileno())
            if (not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid()
                    or before.st_mode & 0o022 or before.st_size > _MAX_DOCUMENT_BYTES):
                raise ComputeAdmissionError("Invalid supervisor document custody")
            data = stream.read(_MAX_DOCUMENT_BYTES + 1)
            after = os.fstat(stream.fileno())
            if (len(data) > _MAX_DOCUMENT_BYTES
                    or (before.st_ino, before.st_size, before.st_mtime_ns)
                    != (after.st_ino, after.st_size, after.st_mtime_ns)):
                raise ComputeAdmissionError("Supervisor document changed during its read")
        value = json.loads(data)
    except ComputeAdmissionError:
        raise
    except (OSError, ValueError, TypeError) as error:
        raise ComputeAdmissionError("Cannot read the current supervised compute context") from error
    if type(value) is not dict:
        raise ComputeAdmissionError("Supervisor document must be an object")
    return value


def _worker_cgroup():
    try:
        return Path("/proc/self/cgroup").read_text()
    except OSError as error:
        raise ComputeAdmissionError("Cannot verify the current worker cgroup") from error


def validate_cuda_admission(device="cuda:0", *, require_permit=True):
    """Validate current files/environment/cgroup, without querying CUDA or a GPU.

    The owning service already performs physical leasing, thermal/VRAM checks,
    inference priority and process confinement. No caller-supplied admission
    dictionary, flag, or token can bypass these current supervised facts.
    """
    if str(device) != "cuda:0":
        raise ComputeAdmissionError("Only the explicitly admitted logical cuda:0 is supported")
    job = os.environ.get("DULCHE_TRAIN_JOB", "")
    control_value = os.environ.get("DULCHE_TRAIN_CONTROL", "")
    output_value = os.environ.get("DULCHE_TRAIN_OUTPUT", "")
    if not _JOB.fullmatch(job) or not control_value or not output_value:
        raise ComputeAdmissionError("CUDA requires an existing supervised Dulche worker")
    control = Path(control_value)
    output = Path(output_value)
    job_dir = control.parent
    if (not control.is_absolute() or not output.is_absolute()
            or control.name != "control.json" or job_dir.name != job
            or job_dir.parent.name != "jobs" or output != job_dir / "output"
            or any(part.is_symlink() for part in (control, output, job_dir))):
        raise ComputeAdmissionError("Training control/output paths do not identify the same owned attempt")
    status = _read_json(job_dir / "status.json")
    launch = _read_json(job_dir / "launch.json")
    command = _read_json(control)
    worker_unit = f"dulche-training-{job}-worker.service"
    physical_uuid = status.get("device")
    if (status.get("id") != job or status.get("worker_pid") != os.getpid()
            or status.get("worker_unit") != worker_unit
            or status.get("supervisor_unit") != f"dulche-training-{job}.service"
            or type(status.get("workflow_digest")) is not str
            or not _SHA.fullmatch(status["workflow_digest"])
            or physical_uuid != _RTX_UUID
            or launch.get("device") != physical_uuid
            or type(launch.get("gpu_minor")) is not int or launch["gpu_minor"] < 0
            or os.environ.get("CUDA_VISIBLE_DEVICES") != physical_uuid):
        raise ComputeAdmissionError("Worker, physical GPU, visibility, or workflow identity does not match admission")
    paths = [line.split(":", 2)[-1].split("/") for line in _worker_cgroup().splitlines()]
    if not any(worker_unit in parts for parts in paths):
        raise ComputeAdmissionError("The current process is not in the admitted worker cgroup")
    if command.get("job") != job:
        raise ComputeAdmissionError("Compute control identity changed")
    until = command.get("permit_until_monotonic")
    if require_permit:
        now = time.monotonic()
        if (status.get("phase") != "running" or command.get("action") != "run"
                or type(until) not in (int, float) or not math.isfinite(until)
                or not now < until <= now + 2.0):
            raise ComputeAdmissionError("The current supervised CUDA permit is absent or expired")
    return {
        "schema": 1, "kind": "existing-dulche-single-rtx-worker",
        "job": job, "workflow_sha256": status["workflow_digest"],
        "physical_gpu_uuid": physical_uuid, "logical_device": "cuda:0",
        "worker_unit": worker_unit, "gpu_minor": launch["gpu_minor"],
    }


def reference_kernel_policy():
    """Require the already-imported native Torch fallback, with no hub downloads."""
    global _KERNEL_CACHE
    if os.environ.get("USE_HUB_KERNELS", "").upper() not in {"NO", "0", "FALSE", "OFF"}:
        raise ComputeAdmissionError("CUDA reference execution requires USE_HUB_KERNELS=NO before imports")
    if os.environ.get("NVIDIA_TF32_OVERRIDE") != "0":
        raise ComputeAdmissionError("CUDA FP32 reference execution requires NVIDIA_TF32_OVERRIDE=0 before imports")
    hub = sys.modules.get("transformers.integrations.hub_kernels")
    if hub is None or getattr(hub, "_kernels_enabled", None) is not False:
        raise ComputeAdmissionError("Transformers hub kernels were not disabled before native imports")
    if any(package in sys.modules for package in ("fla", "causal_conv1d")):
        raise ComputeAdmissionError("An unqualified optional DeltaNet kernel package is loaded")
    if _KERNEL_CACHE is not None and _KERNEL_CACHE[0] is hub:
        return dict(_KERNEL_CACHE[1])
    for package in ("fla", "causal_conv1d"):
        try:
            present = package in sys.modules or importlib.util.find_spec(package) is not None
        except (ValueError, ImportError, AttributeError) as error:
            raise ComputeAdmissionError("Cannot establish native reference-kernel dispatch") from error
        if present:
            raise ComputeAdmissionError("An unqualified optional DeltaNet kernel package is present")
    path = Path(hub.__file__)
    policy = {
        "schema": 1, "kind": "native-torch-reference-fp32",
        "hub_kernels": False, "optional_delta_kernels": False, "tf32_override": "0",
        "hub_dispatch_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    }
    _KERNEL_CACHE = (hub, dict(policy))
    return policy


def _fence_cuda(device):
    # Only reached after current supervisor validation. is_initialized is a
    # process-state check, not a capability probe and does not create a context.
    import torch
    if torch.cuda.is_initialized():
        torch.cuda.synchronize(device)


def check_compute(*, device=None):
    """Fence the owned device and renew/check its permit before another operation."""
    target = str(device) if device is not None else (_DEVICE.get() or "cpu")
    callback = current_compute_callback()
    if target == "cpu":
        if callback is not None:
            callback()
        return
    if target != "cuda:0" or callback is None:
        raise ComputeAdmissionError("CUDA compute requires an explicit supervised compute_scope")
    # Validate process identity before waiting or touching CUDA. The callback
    # owns normal duty-window waiting and checkpoint/stop exceptions.
    validate_cuda_admission(target, require_permit=False)
    callback()
    validate_cuda_admission(target)
    reference_kernel_policy()
    _fence_cuda(target)
    # A fence can consume the remainder of a short permit. Ask the supervisor
    # again before a new submission, then recheck the current control document.
    callback()
    validate_cuda_admission(target)
    _DEVICE.set(target)
