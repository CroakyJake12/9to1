"""Stage the frozen V100 rank in host custody without mounting host storage."""

import base64
from dataclasses import asdict, dataclass
import hashlib
import math
import re

from .artifacts import CustodyError, _encode_record, write_record
from .rank_checkpoint import RankStageReceipt, TwoRankCheckpoint
from .rank_grad_clip import ClipDecision, ClipPlan, decide_global_clip
from .rank_sft import RankSftUpdate, _claim_path, _claim_record

_SHA256 = re.compile(r"[0-9a-f]{64}")
_MAX_RNG_BYTES = 64 * 1024


@dataclass(frozen=True, slots=True)
class FrozenV100Receipt:
    attempt_id: str
    step: int
    ownership_id: str
    completed_microbatches: int
    transcript_sha256: str
    cuda_rng_sha256: str
    cpu_rng_sha256: str


def stage_frozen_v100(checkpoint, plan, decision, *, receipt, cuda_rng, cpu_rng, grad_accum,
                      authenticate_completion):
    """A host owner stages sealed guest evidence, never a guest-side optimizer.

    Authentication must consult live, independently maintained input and
    activation/gradient transcript state from the exact pinned guest process.
    Matching digests alone are not an authentication decision. Only the
    normal joint checkpoint commit, with real rank-1 evidence, reports a step.
    """
    if (type(checkpoint) is not TwoRankCheckpoint or type(plan) is not ClipPlan
            or type(decision) is not ClipDecision or type(receipt) is not FrozenV100Receipt
            or plan.attempt_id != checkpoint.attempt.attempt_id
            or plan.step != checkpoint.step or plan.owners != checkpoint.owners
            or plan.names(0) != () or receipt.attempt_id != plan.attempt_id
            or type(receipt.step) is not int or receipt.step != plan.step
            or receipt.ownership_id != plan.owners[0]
            or type(grad_accum) is not int or not 0 < grad_accum <= 4096
            or type(receipt.completed_microbatches) is not int
            or receipt.completed_microbatches != grad_accum
            or type(receipt.transcript_sha256) is not str
            or not _SHA256.fullmatch(receipt.transcript_sha256)
            or type(cuda_rng) is not bytes or not 0 < len(cuda_rng) <= _MAX_RNG_BYTES
            or receipt.cuda_rng_sha256 != hashlib.sha256(cuda_rng).hexdigest()
            or type(cpu_rng) is not bytes or not 0 < len(cpu_rng) <= _MAX_RNG_BYTES
            or receipt.cpu_rng_sha256 != hashlib.sha256(cpu_rng).hexdigest()
            or not callable(authenticate_completion)):
        raise CustodyError("The frozen V100 checkpoint has invalid custody or evidence.")
    claim = _claim_path(checkpoint, plan, 0)
    if claim.exists() or checkpoint.claim.exists():
        raise CustodyError("The frozen V100 step was already claimed.")
    checkpoint._validate_base()
    if (not math.isfinite(decision.norm) or decision.norm <= 0
            or decide_global_clip(plan, decision.receipts) != decision
            or authenticate_completion(receipt, cuda_rng, cpu_rng) is not True):
        raise CustodyError("The frozen V100 completion or global norm is not authenticated.")
    write_record(claim, _claim_record(plan, 0, decision))
    payload = _encode_record({"schema": 1, "format": "v8-frozen-v100-rng-v1",
                              "evidence": asdict(receipt),
                              "cuda_rng_base64": base64.b64encode(cuda_rng).decode("ascii"),
                              "cpu_rng_base64": base64.b64encode(cpu_rng).decode("ascii")})
    artifact = checkpoint.store.publish_bytes(
        checkpoint.attempt, f"v8-frozen-v100-step-{plan.step}.json", "segment_checkpoint",
        payload, inputs=(checkpoint.base,))
    return RankSftUpdate(RankStageReceipt(checkpoint.attempt, checkpoint.step, checkpoint.base,
                             0, checkpoint.owners[0], artifact), None, 0.0)
