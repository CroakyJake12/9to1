"""Fail-closed admission and measurements for a bounded training attempt."""

from __future__ import annotations

from dataclasses import dataclass
import math
import re

from .artifacts import CustodyError

_RTX_NAME = "nvidia geforce rtx 5080"
_GPU_UUID = re.compile(r"GPU-[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}")


class BudgetRefusal(CustodyError):
    """A known budget or safety boundary refused the attempt."""

    def __init__(self, code: str):
        self.code = code
        super().__init__(code)


def _quantity(value, *, positive: bool = False) -> int:
    if type(value) is not int or value < int(positive):
        raise BudgetRefusal("invalid_integer_measurement")
    return value


def _scalar(value, *, positive: bool = False) -> float:
    if type(value) not in (int, float) or not math.isfinite(value) or value < 0 or (positive and value == 0):
        raise BudgetRefusal("invalid_scalar_measurement")
    return value


@dataclass(frozen=True, slots=True)
class Limits:
    wall_seconds: float
    ram_bytes: int
    vram_bytes: int
    storage_bytes: int
    minimum_free_storage_bytes: int
    temperature_c: float
    power_watts: float
    iterations: int
    checkpoint_grace_seconds: float = 2.0

    def __post_init__(self):
        _scalar(self.wall_seconds, positive=True)
        _quantity(self.ram_bytes, positive=True)
        _quantity(self.vram_bytes)
        _quantity(self.storage_bytes, positive=True)
        _quantity(self.minimum_free_storage_bytes)
        _scalar(self.temperature_c, positive=True)
        _scalar(self.power_watts, positive=True)
        _quantity(self.iterations, positive=True)
        _scalar(self.checkpoint_grace_seconds)
        if self.checkpoint_grace_seconds > self.wall_seconds:
            raise BudgetRefusal("checkpoint_grace_exceeds_wall_budget")


@dataclass(frozen=True, slots=True)
class OperatorPolicy:
    """Trusted local configuration, never imported from a dataset or job request."""

    allowed_gpu_uuids: tuple[str, ...]
    cpu_threads: int
    minimum_free_ram_bytes: int

    def __post_init__(self):
        _quantity(self.cpu_threads, positive=True)
        _quantity(self.minimum_free_ram_bytes, positive=True)
        if (type(self.allowed_gpu_uuids) is not tuple
                or any(type(item) is not str or not _GPU_UUID.fullmatch(item) for item in self.allowed_gpu_uuids)
                or len(set(self.allowed_gpu_uuids)) != len(self.allowed_gpu_uuids)):
            raise BudgetRefusal("invalid_operator_gpu_allowlist")


@dataclass(frozen=True, slots=True)
class GpuObservation:
    uuid: str
    name: str
    free_vram_bytes: int
    temperature_c: float
    power_watts: float


@dataclass(frozen=True, slots=True)
class HostObservation:
    cpu_identity: str
    free_ram_bytes: int
    free_storage_bytes: int
    gpus: tuple[GpuObservation, ...] = ()
    game_or_protected_work_active: bool | None = None
    logical_cpus: int | None = None
    cpu_temperature_c: float | None = None
    cpu_power_watts: float | None = None


@dataclass(frozen=True, slots=True)
class ResourceUse:
    """Observed owned-process usage, plus current host pressure."""

    ram_bytes: int
    vram_bytes: int
    written_bytes: int
    free_storage_bytes: int
    completed_iterations: int
    gpu: GpuObservation | None = None
    game_or_protected_work_active: bool | None = None
    free_ram_bytes: int | None = None
    cpu_temperature_c: float | None = None
    cpu_power_watts: float | None = None


@dataclass(frozen=True, slots=True)
class Admission:
    device_uuid: str | None
    limits: Limits
    policy: OperatorPolicy
    requires_cpu_telemetry: bool


def _check_gpu(gpu: GpuObservation | None, device_uuid: str, limits: Limits, policy: OperatorPolicy) -> None:
    if gpu is None or type(gpu.name) is not str:
        raise BudgetRefusal("gpu_observation_missing")
    name = gpu.name.strip().casefold()
    if "v100" in name:
        raise BudgetRefusal("v100_cooling_hold")
    if device_uuid not in policy.allowed_gpu_uuids or gpu.uuid != device_uuid or name != _RTX_NAME:
        raise BudgetRefusal("gpu_not_allowlisted")
    _quantity(gpu.free_vram_bytes)
    if _scalar(gpu.temperature_c) >= limits.temperature_c:
        raise BudgetRefusal("temperature_capacity")
    if _scalar(gpu.power_watts) >= limits.power_watts:
        raise BudgetRefusal("power_capacity")


def _check_cpu_telemetry(temperature: float | None, power: float | None, limits: Limits) -> None:
    if temperature is None or power is None:
        raise BudgetRefusal("cpu_telemetry_missing")
    if _scalar(temperature) >= limits.temperature_c:
        raise BudgetRefusal("temperature_capacity")
    if _scalar(power) >= limits.power_watts:
        raise BudgetRefusal("power_capacity")


def admit(
    limits: Limits, host: HostObservation, *, policy: OperatorPolicy, device_uuid: str | None,
    purpose: str = "software_test_only", observed_iterations: int = 0,
) -> Admission:
    """Evaluate explicitly supplied observations. This function enumerates no devices."""
    if type(host.cpu_identity) is not str or not host.cpu_identity.strip():
        raise BudgetRefusal("cpu_observation_missing")
    if purpose not in {"software_test_only", "real_training"}:
        raise BudgetRefusal("invalid_execution_purpose")
    if _quantity(host.logical_cpus, positive=True) < policy.cpu_threads:
        raise BudgetRefusal("cpu_thread_capacity")
    if host.game_or_protected_work_active is not False:
        raise BudgetRefusal("protected_work_active_or_unknown")
    if _quantity(observed_iterations) >= limits.iterations:
        raise BudgetRefusal("iteration_capacity")
    if _quantity(host.free_ram_bytes) < limits.ram_bytes + policy.minimum_free_ram_bytes:
        raise BudgetRefusal("ram_capacity")
    if _quantity(host.free_storage_bytes) < limits.storage_bytes + limits.minimum_free_storage_bytes:
        raise BudgetRefusal("storage_capacity")
    requires_cpu_telemetry = device_uuid is None and purpose == "real_training"
    if device_uuid is None:
        if limits.vram_bytes != 0:
            raise BudgetRefusal("cpu_job_has_gpu_allocation")
        if requires_cpu_telemetry:
            _check_cpu_telemetry(host.cpu_temperature_c, host.cpu_power_watts, limits)
    else:
        matching = [gpu for gpu in host.gpus if gpu.uuid == device_uuid]
        if len(matching) != 1:
            raise BudgetRefusal("gpu_observation_missing_or_ambiguous")
        _check_gpu(matching[0], device_uuid, limits, policy)
        if limits.vram_bytes == 0 or matching[0].free_vram_bytes < limits.vram_bytes:
            raise BudgetRefusal("vram_capacity")
    return Admission(device_uuid, limits, policy, requires_cpu_telemetry)


def check_running(admission: Admission, sample: ResourceUse, *, elapsed_seconds: float) -> None:
    """Check a fresh observation before another step or supervisor wait."""
    limits = admission.limits
    if sample.game_or_protected_work_active is not False:
        raise BudgetRefusal("protected_work_active_or_unknown")
    if admission.device_uuid is not None:
        _check_gpu(sample.gpu, admission.device_uuid, limits, admission.policy)
    elif sample.gpu is not None:
        raise BudgetRefusal("unexpected_gpu_use")
    elif admission.requires_cpu_telemetry:
        _check_cpu_telemetry(sample.cpu_temperature_c, sample.cpu_power_watts, limits)
    if _scalar(elapsed_seconds) >= limits.wall_seconds:
        raise BudgetRefusal("wall_time_capacity")
    if _quantity(sample.ram_bytes) > limits.ram_bytes:
        raise BudgetRefusal("ram_capacity")
    if _quantity(sample.free_ram_bytes) < admission.policy.minimum_free_ram_bytes:
        raise BudgetRefusal("host_ram_pressure")
    if _quantity(sample.vram_bytes) > limits.vram_bytes:
        raise BudgetRefusal("vram_capacity")
    if (_quantity(sample.written_bytes) > limits.storage_bytes
            or _quantity(sample.free_storage_bytes) < limits.minimum_free_storage_bytes):
        raise BudgetRefusal("storage_capacity")
    if _quantity(sample.completed_iterations) >= limits.iterations:
        raise BudgetRefusal("iteration_capacity")
