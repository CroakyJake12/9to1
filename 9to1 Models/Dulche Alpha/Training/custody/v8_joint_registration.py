"""Separate v8 real-training registration; never parse a legacy scalar job.

This is called by the trusted daemon/registrar. An expected SHA from request
JSON is not a trust anchor. A successful registration validates configuration,
not process ownership, Flash absence, capacity, or any compute lease.
"""
from dataclasses import dataclass
import hashlib
import re

from .artifacts import CustodyError, _encode_record
from .v8_owned_endpoints import WORKER_ID, _RTX, _V100

SCHEMA = "dulche-independent-joint-workflow-v1"
PROTOCOL = "checkpoint-yield-two-owned-ranks-v1"
LEGACY_RECIPE_SHA256 = "46e27a27b97a40998cddaa0de681eacb407415a07d3fc84981584f3ed3f6c65b"
DATASET_MANIFEST_SHA256 = "3f298927998ce8b00c67c9c1fa487ee134def70a9fee142a0704c3737745f616"
_SHA = re.compile(r"[0-9a-f]{64}")
_RUNTIME_FIELDS = {"logical_rank", "physical_lane", "kind", "gpu_uuid", "runtime_sha256", "bundle_sha256"}
_SETTINGS = {"method": "sft", "segment": "upper", "seq_len": 2048, "grad_accum": 8, "learning_rate": 0.0001,
             "max_steps": 400, "checkpoint_every": 1, "checkpoint_grace_seconds": 90, "seed": 20261123,
             "gradient_checkpointing": True}
_GPU_ROWS = ((0, 1, "legacy_vm", _V100), (1, 0, "native", _RTX))


@dataclass(frozen=True, slots=True)
class JointRegistration:
    digest: str
    dataset_manifest_sha256: str
    original_recipe_sha256: str
    pager_sha256: str
    cutoff_sha256: str
    tokenizer_sha256: str
    rank_manifest_sha256: tuple
    runtime_bindings: tuple


def _pin(value):
    return type(value) is str and bool(_SHA.fullmatch(value))


def verify_joint_registration(document, *, trusted_record_sha256):
    """Verify exact role mapping, existing data/settings and distinct source pins."""
    expected = {"schema", "purpose", "id", "protocol", "source_recipe_sha256", "dataset_manifest_sha256",
                "pager_sha256", "cutoff_sha256", "tokenizer_sha256", "rank_manifest_sha256", "settings", "ranks"}
    if (type(document) is not dict or document.keys() != expected or not _pin(trusted_record_sha256)
            or document.get("schema") != SCHEMA or document.get("purpose") != "real_training"
            or document.get("id") != WORKER_ID or document.get("protocol") != PROTOCOL
            or document.get("source_recipe_sha256") != LEGACY_RECIPE_SHA256
            or document.get("dataset_manifest_sha256") != DATASET_MANIFEST_SHA256
            or document.get("settings") != _SETTINGS
            or any(type(document["settings"][k]) is not type(v) for k, v in _SETTINGS.items())
            or any(not _pin(document[name]) for name in ("pager_sha256", "cutoff_sha256", "tokenizer_sha256"))):
        raise CustodyError("Unsupported, unpinned or changed joint v8 registration.")
    pins = document["rank_manifest_sha256"]
    lanes = document["ranks"]
    if (type(pins) is not list or len(pins) != 2 or any(not _pin(p) for p in pins) or pins[0] == pins[1]
            or type(lanes) is not list or len(lanes) != 2):
        raise CustodyError("The joint v8 workflow lacks both publishers' rank pins.")
    for lane, registered in zip(lanes, _GPU_ROWS):
        if (type(lane) is not dict or lane.keys() != _RUNTIME_FIELDS
                or type(lane["logical_rank"]) is not int or type(lane["physical_lane"]) is not int
                or tuple(lane[k] for k in ("logical_rank", "physical_lane", "kind", "gpu_uuid")) != registered
                or not _pin(lane["runtime_sha256"]) or not _pin(lane["bundle_sha256"])):
            raise CustodyError("No exact native RTX tail and V100 guest early registration.")
    digest = hashlib.sha256(_encode_record(document)).hexdigest()
    if digest != trusted_record_sha256:
        raise CustodyError("Joint registration does not match the trusted installed approval.")
    bindings = tuple((*row, lane["runtime_sha256"], lane["bundle_sha256"])
                     for row, lane in zip(_GPU_ROWS, lanes))
    return JointRegistration(digest, document["dataset_manifest_sha256"], document["source_recipe_sha256"],
                             document["pager_sha256"], document["cutoff_sha256"],
                             document["tokenizer_sha256"], tuple(pins), bindings)


@dataclass(frozen=True, slots=True)
class RankProcessReceipt:
    attempt_id: str
    logical_rank: int
    host_id: str
    ownership_id: str
    gpu_uuid: str
    runtime_sha256: str
    bundle_sha256: str
    pid: int
    start_ticks: int
    cgroup: str
    cuda_capability: tuple[int, int]
    visible_cuda_count: int


def verify_live_joint_processes(registration, attempt_id, receipts, *, observe_owned_process):
    """Bind each CUDA worker to independent host/guest owner observations.

    A host unit's SSH or wrapper PID isn't valid evidence for the actual guest
    process. Call the original owner on each machine to compare live PID start
    ticks/cgroup, process artifact and native CUDA driver identity. This hook
    runs before sending any training bytes; it is not a thermal permit.
    """
    from .artifacts import canonical_id

    if (type(registration) is not JointRegistration or canonical_id(attempt_id) == "00000000-0000-0000-0000-000000000000"
            or type(receipts) is not tuple or len(receipts) != 2 or not callable(observe_owned_process)):
        raise CustodyError("Missing independent native and guest process owners.")
    for bind, receipt, capability in zip(registration.runtime_bindings, receipts, ((7, 0), (12, 0))):
        rank, _, _, gpu, runtime, bundle = bind
        if (type(receipt) is not RankProcessReceipt or receipt.attempt_id != attempt_id
                or type(receipt.logical_rank) is not int or receipt.logical_rank != rank
                or receipt.gpu_uuid != gpu or receipt.runtime_sha256 != runtime or receipt.bundle_sha256 != bundle
                or not isinstance(receipt.host_id, str) or not receipt.host_id
                or not isinstance(receipt.ownership_id, str) or not receipt.ownership_id
                or type(receipt.pid) is not int or receipt.pid < 2
                or type(receipt.start_ticks) is not int or receipt.start_ticks <= 0
                or type(receipt.cgroup) is not str or not receipt.cgroup.startswith("/")
                or receipt.cgroup == "/" or ".." in receipt.cgroup.split("/")
                or receipt.cuda_capability != capability
                or type(receipt.visible_cuda_count) is not int or receipt.visible_cuda_count != 1):
            raise CustodyError("A joint training rank does not match its registered process and CUDA identity.")
    if (receipts[0].host_id == receipts[1].host_id or receipts[0].ownership_id == receipts[1].ownership_id
            or any(observe_owned_process(receipt) is not True for receipt in receipts)):
        raise CustodyError("Both independent owned GPU processes were not observed.")
    return receipts
