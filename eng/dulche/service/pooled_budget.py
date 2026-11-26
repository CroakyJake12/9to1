"""Pure admission planning for Jacob's RTX 5080 + V100 inference pool.

The byte-exact display policy is an accounting limit, not a hardware partition.
Only a caller that owns the physical lifecycle lock may act on a fresh plan.
It must claim both devices atomically, account for ALL its model/KV/compute
allocations, and recheck after allocation. This module launches nothing.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
import re
from typing import Iterable, Mapping

RTX_UUID = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"
DISPLAY_RESERVED_BYTES = 1_400_000_000
MIB = 1_048_576
MAX_INTEGER = (1 << 63) - 1
_UUID = re.compile(r"GPU-[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\Z")


class PlanningError(ValueError):
    """Telemetry, policy or a request cannot support a trustworthy pool plan."""


def _integer(value: int, name: str, *, positive: bool = False) -> int:
    if type(value) is not int or not (int(positive) <= value <= MAX_INTEGER):
        raise PlanningError(f"{name} must be a {'positive' if positive else 'nonnegative'} signed-64-bit integer")
    return value


def _uuid(value: str) -> str:
    if not isinstance(value, str) or not _UUID.fullmatch(value):
        raise PlanningError("A complete NVIDIA GPU UUID is required")
    return "GPU-" + value[4:].lower()


def _endpoint(value: str) -> str:
    if not isinstance(value, str) or not 1 <= len(value) <= 256 or any(c.isspace() or ord(c) < 32 or ord(c) == 127 for c in value):
        raise PlanningError("Endpoint identity must be a nonempty, bounded identifier without whitespace")
    return value


@dataclass(frozen=True)
class PoolPolicy:
    rtx_endpoint: str
    v100_uuid: str
    v100_endpoint: str
    max_age_ns: int
    display_reserved_bytes: int = field(default=DISPLAY_RESERVED_BYTES, init=False)
    rtx_uuid: str = field(default=RTX_UUID, init=False)

    def identities(self) -> tuple[tuple[str, str, str], ...]:
        _integer(self.max_age_ns, "max_age_ns", positive=True)
        rtx_endpoint, v100_endpoint = _endpoint(self.rtx_endpoint), _endpoint(self.v100_endpoint)
        v100_uuid = _uuid(self.v100_uuid)
        if v100_uuid == RTX_UUID or rtx_endpoint == v100_endpoint:
            raise PlanningError("The pool requires two distinct physical GPUs and endpoint identities")
        return (("rtx5080", RTX_UUID, rtx_endpoint), ("v100", v100_uuid, v100_endpoint))


@dataclass(frozen=True)
class Snapshot:
    gpu_uuid: str
    endpoint_id: str
    observed_at_ns: int
    total_bytes: int
    used_bytes: int
    owned_bytes: int
    healthy: bool
    endpoint_ready: bool


@dataclass(frozen=True)
class DeviceBudget:
    role: str
    gpu_uuid: str
    endpoint_id: str
    observed_at_ns: int
    total_bytes: int
    used_bytes: int
    owned_bytes: int
    external_bytes: int
    display_reserved_bytes: int
    owned_limit_bytes: int
    additional_headroom_bytes: int
    current_deficit_bytes: int
    unconsumed_reserve_bytes: int
    fit_target_mib: int
    fit_target_bytes: int
    fit_rounding_bytes: int


@dataclass(frozen=True)
class PoolBudget:
    observed_at_ns: int
    evaluated_at_ns: int
    devices: tuple[DeviceBudget, ...]
    physical_devices: tuple[str, ...]
    reported_total_bytes: int
    owned_limit_bytes: int
    current_owned_bytes: int
    additional_headroom_bytes: int
    current_deficit_bytes: int
    display_reserved_bytes: int = field(default=DISPLAY_RESERVED_BYTES, init=False)
    hard_reservation_supported: bool = field(default=False, init=False)
    fit_target_is_hard_limit: bool = field(default=False, init=False)

    def to_dict(self) -> dict:
        return asdict(self)


def budget_pool(policy: PoolPolicy, snapshots: Iterable[Snapshot], *, now_ns: int) -> PoolBudget:
    """Return observed capacity only when both expected endpoints are healthy.

    owned_bytes includes every inference-owned allocation, including workspaces
    and caches. external_bytes = used_bytes - owned_bytes includes display use.
    The reserved display amount therefore replaces (rather than adds to) lower
    existing external use. fit_target is a rounded *remaining free-memory*
    target; it does not reserve memory or enforce the byte-exact owned limit.
    """
    _integer(now_ns, "now_ns")
    if not isinstance(policy, PoolPolicy):
        raise PlanningError("A PoolPolicy is required")
    identities = policy.identities()
    measured = {}
    for snapshot in snapshots:
        if not isinstance(snapshot, Snapshot):
            raise PlanningError("Every endpoint requires a Snapshot")
        gpu_uuid = _uuid(snapshot.gpu_uuid)
        if gpu_uuid in measured:
            raise PlanningError("Duplicate physical GPU telemetry")
        measured[gpu_uuid] = snapshot
    expected = {gpu_uuid for _, gpu_uuid, _ in identities}
    if set(measured) != expected:
        raise PlanningError("Fresh telemetry for exactly both expected GPUs is required")

    devices = []
    for role, gpu_uuid, endpoint_id in identities:
        sample = measured[gpu_uuid]
        if _endpoint(sample.endpoint_id) != endpoint_id:
            raise PlanningError(f"{role}: endpoint identity mismatch")
        if sample.healthy is not True or sample.endpoint_ready is not True:
            raise PlanningError(f"{role}: endpoint is unhealthy or unavailable")
        observed = _integer(sample.observed_at_ns, f"{role}.observed_at_ns")
        if not 0 <= now_ns - observed <= policy.max_age_ns:
            raise PlanningError(f"{role}: telemetry is stale or from the future")
        total = _integer(sample.total_bytes, f"{role}.total_bytes", positive=True)
        used = _integer(sample.used_bytes, f"{role}.used_bytes")
        owned = _integer(sample.owned_bytes, f"{role}.owned_bytes")
        if not owned <= used <= total:
            raise PlanningError(f"{role}: allocations must satisfy owned <= used <= total")
        reserve = DISPLAY_RESERVED_BYTES if role == "rtx5080" else 0
        if reserve > total:
            raise PlanningError("RTX reported total cannot accommodate the exact display reserve")
        external = used - owned
        owned_limit = total - max(reserve, external)
        unconsumed = max(0, reserve - external)
        fit_mib = (unconsumed + MIB - 1) // MIB
        devices.append(DeviceBudget(
            role=role, gpu_uuid=gpu_uuid, endpoint_id=endpoint_id, observed_at_ns=observed,
            total_bytes=total, used_bytes=used, owned_bytes=owned, external_bytes=external,
            display_reserved_bytes=reserve, owned_limit_bytes=owned_limit,
            additional_headroom_bytes=max(0, owned_limit - owned),
            current_deficit_bytes=max(0, owned - owned_limit),
            unconsumed_reserve_bytes=unconsumed, fit_target_mib=fit_mib,
            fit_target_bytes=fit_mib * MIB, fit_rounding_bytes=fit_mib * MIB - unconsumed,
        ))
    sums = {name: sum(getattr(d, name) for d in devices) for name in (
        "total_bytes", "owned_limit_bytes", "owned_bytes", "additional_headroom_bytes", "current_deficit_bytes")}
    for name, value in sums.items():
        _integer(value, "pool." + name)
    return PoolBudget(
        observed_at_ns=min(d.observed_at_ns for d in devices), evaluated_at_ns=now_ns,
        devices=tuple(devices), physical_devices=tuple(d.gpu_uuid for d in devices),
        reported_total_bytes=sums["total_bytes"], owned_limit_bytes=sums["owned_limit_bytes"],
        current_owned_bytes=sums["owned_bytes"], additional_headroom_bytes=sums["additional_headroom_bytes"],
        current_deficit_bytes=sums["current_deficit_bytes"],
    )


@dataclass(frozen=True)
class Allocation:
    """Desired FINAL owned footprint on one device, including every buffer."""
    weights_bytes: int
    kv_bytes: int
    compute_bytes: int
    other_bytes: int = 0

    def total(self) -> int:
        values = asdict(self)
        for name, value in values.items():
            _integer(value, name)
        return _integer(sum(values.values()), "requested_total_bytes")


@dataclass(frozen=True)
class DeviceAdmission:
    gpu_uuid: str
    allocation: Allocation
    requested_total_bytes: int
    additional_bytes: int
    planned_headroom_bytes: int
    requested_deficit_bytes: int
    current_deficit_bytes: int


@dataclass(frozen=True)
class AdmissionPlan:
    budget: PoolBudget
    devices: tuple[DeviceAdmission, ...]
    admissible: bool
    device_claims: tuple[str, ...]
    all_or_nothing: bool = field(default=True, init=False)
    performs_acquisition: bool = field(default=False, init=False)
    hard_reservation_supported: bool = field(default=False, init=False)

    def to_dict(self) -> dict:
        return asdict(self)


def plan_admission(policy: PoolPolicy, snapshots: Iterable[Snapshot],
                   allocations: Mapping[str, Allocation], *, now_ns: int) -> AdmissionPlan:
    """Plan under one snapshot; a deficit on either device returns NO claims.

    This is not an acquisition or a lease. The caller must prevent competing
    allocations and revalidate telemetry when executing. A request describes
    the desired final footprint of the same accounting owner, not an extra
    model to add to its current footprint.
    """
    budget = budget_pool(policy, snapshots, now_ns=now_ns)
    if not isinstance(allocations, Mapping):
        raise PlanningError("Per-device allocations must be a mapping")
    requested = {}
    for gpu_uuid, allocation in allocations.items():
        gpu_uuid = _uuid(gpu_uuid)
        if gpu_uuid in requested or not isinstance(allocation, Allocation):
            raise PlanningError("Unique, complete per-GPU Allocation requests are required")
        requested[gpu_uuid] = allocation
    if set(requested) != set(budget.physical_devices):
        raise PlanningError("An explicit allocation is required for each physical GPU")
    devices = []
    for device in budget.devices:
        allocation = requested[device.gpu_uuid]
        total = allocation.total()
        devices.append(DeviceAdmission(
            gpu_uuid=device.gpu_uuid, allocation=allocation, requested_total_bytes=total,
            additional_bytes=max(0, total - device.owned_bytes),
            planned_headroom_bytes=max(0, device.owned_limit_bytes - total),
            requested_deficit_bytes=max(0, total - device.owned_limit_bytes),
            current_deficit_bytes=device.current_deficit_bytes,
        ))
    admitted = all(d.requested_deficit_bytes == 0 and d.current_deficit_bytes == 0 for d in devices)
    return AdmissionPlan(budget=budget, devices=tuple(devices), admissible=admitted,
                         device_claims=budget.physical_devices if admitted else ())
