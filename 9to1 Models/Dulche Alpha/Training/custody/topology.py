"""Preflight for an RTX-host / V100-guest training topology. Never launches workers."""

from dataclasses import dataclass
import re

from .resources import BudgetRefusal, GpuObservation, Limits, _GPU_UUID, _RTX_NAME, _quantity, _scalar

_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}")
_SHA256 = re.compile(r"[a-f0-9]{64}")
_LANES = (("native", (_RTX_NAME,)),
          ("legacy_vm", ("tesla v100-pcie-16gb", "nvidia tesla v100-pcie-16gb")))
_TRAINING_CAPABILITY = "optimizer-participant-v1"


@dataclass(frozen=True, slots=True)
class TrainingLane:
    rank: int
    kind: str
    host_id: str
    gpu_uuid: str
    runtime_sha256: str


@dataclass(frozen=True, slots=True)
class OwnedLaneObservation:
    """Verified exclusively by the caller's trusted lane owner, never from job JSON.

    The owner must authenticate its runtime, confirm a live exclusive lease and
    obtain current hardware observations. This module does not do that I/O.
    """

    lane: TrainingLane
    attempt_id: str
    ownership_id: str
    capability: str
    exclusive_ownership_verified: bool
    cooling_verified: bool
    mentor_gpu_released: bool
    protected_work_active: bool | None
    gpu: GpuObservation


def preflight_heterogeneous_training(
    policy: tuple[TrainingLane, ...], observations: tuple[OwnedLaneObservation, ...], limits: Limits,
    *, expected_attempt_id: str,
) -> None:
    """Validate the exact ordered operator policy against trusted owner observations.

    Success is a preflight check, not a renewable lease or execution permission.
    Apply the same operator limit to each lane, never to a pooled total.
    An executor still needs to own each rank through launch, telemetry and teardown.
    """
    # The caller mints this identity freshly for every attempt and binds both owner records to it.
    if type(expected_attempt_id) is not str or not _ID.fullmatch(expected_attempt_id):
        raise BudgetRefusal("invalid_training_attempt_identity")
    if type(policy) is not tuple or len(policy) != 2 or type(limits) is not Limits or limits.vram_bytes == 0:
        raise BudgetRefusal("invalid_heterogeneous_training_policy")
    for rank, (lane, (kind, _)) in enumerate(zip(policy, _LANES)):
        if (type(lane) is not TrainingLane or type(lane.rank) is not int or lane.rank != rank or lane.kind != kind
                or type(lane.host_id) is not str or not _ID.fullmatch(lane.host_id)
                or type(lane.gpu_uuid) is not str or not _GPU_UUID.fullmatch(lane.gpu_uuid)
                or type(lane.runtime_sha256) is not str or not _SHA256.fullmatch(lane.runtime_sha256)):
            raise BudgetRefusal("invalid_heterogeneous_training_policy")
    if policy[0].host_id == policy[1].host_id or policy[0].gpu_uuid == policy[1].gpu_uuid:
        raise BudgetRefusal("duplicate_training_lane")
    if type(observations) is not tuple or len(observations) != 2:
        raise BudgetRefusal("training_lane_observations_missing")
    owners = set()
    for lane, observation, (_, models) in zip(policy, observations, _LANES):
        if (type(observation) is not OwnedLaneObservation or type(observation.lane) is not TrainingLane
                or observation.lane != lane):
            raise BudgetRefusal("training_lane_identity_mismatch")
        if (type(observation.attempt_id) is not str or observation.attempt_id != expected_attempt_id):
            raise BudgetRefusal("training_lane_attempt_mismatch")
        if (type(observation.ownership_id) is not str or not _ID.fullmatch(observation.ownership_id)
                or observation.ownership_id in owners or observation.exclusive_ownership_verified is not True):
            raise BudgetRefusal("training_lane_not_exclusively_owned")
        owners.add(observation.ownership_id)
        if observation.capability != _TRAINING_CAPABILITY:
            raise BudgetRefusal("training_lane_capability_missing")
        if observation.cooling_verified is not True:
            raise BudgetRefusal("training_lane_cooling_unverified")
        if observation.mentor_gpu_released is not True:
            raise BudgetRefusal("training_lane_gpu_not_released")
        if observation.protected_work_active is not False:
            raise BudgetRefusal("protected_work_active_or_unknown")
        gpu = observation.gpu
        if (type(gpu) is not GpuObservation or gpu.uuid != lane.gpu_uuid or type(gpu.name) is not str
                or gpu.name.strip().casefold() not in models):
            raise BudgetRefusal("training_lane_gpu_mismatch")
        if _quantity(gpu.free_vram_bytes) < limits.vram_bytes:
            raise BudgetRefusal("vram_capacity")
        if _scalar(gpu.temperature_c) >= limits.temperature_c:
            raise BudgetRefusal("temperature_capacity")
        if _scalar(gpu.power_watts) >= limits.power_watts:
            raise BudgetRefusal("power_capacity")


def refuse_heterogeneous_on_local_executor(topology: object) -> None:
    """No current canonical executor owns a training topology across hosts."""
    if topology is not None:
        raise BudgetRefusal("heterogeneous_training_executor_unavailable")
