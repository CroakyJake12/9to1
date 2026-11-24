"""Server-side joint-stop authorization for the v8 Flash training window.

Not a client/server launcher. Installed v8's scalar stop_proved cannot use
this proof until the authenticated server registers both owned authorities.
The training users/processes must not have either observation signing key.
"""
from dataclasses import asdict, dataclass
import hashlib
import hmac
import re
import secrets
from time import monotonic

from .artifacts import CustodyError, _encode_record, canonical_id
from .topology import TrainingLane

_HEX = re.compile(r"[0-9a-f]{64}")
_WINDOW_S = 5


@dataclass(frozen=True, slots=True)
class RegisteredRank:
    logical_rank: int
    lane: TrainingLane
    owner: str
    uid: int
    pid: int
    start_ticks: int
    cgroup: str


@dataclass(frozen=True, slots=True)
class RankExitReceipt:
    attempt_id: str
    nonce: str
    checkpoint_sha256: str
    rank: RegisteredRank
    original_process_exited: bool
    rank_cgroup_empty: bool
    gpu_compute_pids: tuple
    # GPU evidence must come from the lane owner after original process/cgroup
    # exit, independently of the job's wrapper or its own terminal receipt.
    fresh_guard_verified: bool
    mac: str = ""


def rank_exit_mac(receipt, key):
    evidence = asdict(receipt)
    del evidence["mac"]
    return hmac.digest(key, b"v8-independent-rank-stop-v1\x00" + _encode_record(
        {"schema": 1, **evidence}), "sha256").hex()


class JointFlashRelease:
    """One-shot server-side bridge; requires distinct registered stop authorities.

    On a signed request, start a bounded challenge before asking both original
    lane authorities for current evidence. The authority must use original
    process handles, owned cgroups and observed GPU processes, never sign
    claims from the worker or SSH wrapper. The host's existing memory/VRAM
    and thermal restoration gates remain authoritative and unchanged.
    """

    def __init__(self, *, attempt_id, ranks, authority_keys, clock=monotonic):
        if (canonical_id(attempt_id) == "00000000-0000-0000-0000-000000000000"
                or type(ranks) is not tuple or len(ranks) != 2 or type(authority_keys) is not tuple
                or len(authority_keys) != 2
                or any(type(k) is not bytes or len(k) < 32 for k in authority_keys)
                or hmac.compare_digest(*authority_keys)):
            raise CustodyError("Two distinct registered v8 stop authorities are required.")
        for logical, (record, (physical, kind)) in enumerate(zip(ranks, ((1, "legacy_vm"), (0, "native")))):
            if (type(record) is not RegisteredRank or type(record.logical_rank) is not int
                    or record.logical_rank != logical or type(record.lane) is not TrainingLane
                    or record.lane.rank != physical or record.lane.kind != kind
                    or not record.owner or type(record.uid) is not int or record.uid < 0
                    or type(record.pid) is not int or record.pid < 2
                    or type(record.start_ticks) is not int or record.start_ticks <= 0
                    or type(record.cgroup) is not str or not record.cgroup.startswith("/")
                    or ".." in record.cgroup.split("/") or record.cgroup == "/"):
                raise CustodyError("Joint v8 stop registration does not identify the exact owned rank.")
        if (ranks[0].owner == ranks[1].owner or ranks[0].lane.host_id == ranks[1].lane.host_id
                or ranks[0].lane.gpu_uuid == ranks[1].lane.gpu_uuid):
            raise CustodyError("Host and guest stop identities are not independent.")
        self._attempt, self._ranks, self._keys, self._clock = attempt_id, ranks, authority_keys, clock
        self._challenge = None

    def challenge(self, *, committed_checkpoint_sha256):
        if type(committed_checkpoint_sha256) is not str or not _HEX.fullmatch(committed_checkpoint_sha256):
            raise CustodyError("No verified joint recovery checkpoint.")
        self._challenge = (secrets.token_hex(32), committed_checkpoint_sha256, self._clock())
        return self._challenge[:2]

    def authorize(self, receipts):
        """Consume the challenge; any retry requires two newly sampled proofs."""
        challenge, self._challenge = self._challenge, None
        if (challenge is None or not 0 <= self._clock() - challenge[2] < _WINDOW_S
                or type(receipts) is not tuple or len(receipts) != 2):
            raise CustodyError("No fresh joint Flash release challenge or complete stop proof.")
        for record, receipt, key in zip(self._ranks, receipts, self._keys):
            if (type(receipt) is not RankExitReceipt or receipt.attempt_id != self._attempt
                    or receipt.nonce != challenge[0] or receipt.checkpoint_sha256 != challenge[1]
                    or receipt.rank != record or receipt.original_process_exited is not True
                    or receipt.rank_cgroup_empty is not True or receipt.gpu_compute_pids != ()
                    or receipt.fresh_guard_verified is not True or type(receipt.mac) is not str
                    or not _HEX.fullmatch(receipt.mac)
                    or not hmac.compare_digest(rank_exit_mac(receipt, key), receipt.mac)):
                raise CustodyError("Independent owned RTX and V100 stops are not both proven.")
        return challenge[1]
