"""Atomic checkpoint visibility for two privately staged optimizer participants.

The coordinator does not own tensors or roll back optimizer side effects. Rank
owners must stage out of place, or discard their live processes and restore the
last committed checkpoint after any failed or uncertain step.
"""
from dataclasses import asdict, dataclass

from .artifacts import (
    ArtifactRef, ArtifactStore, AttemptRef, CustodyError, _encode_record, _read_record, write_record,
)


@dataclass(frozen=True, slots=True)
class RankStageReceipt:
    """An authenticated owner supplies this typed receipt after staging privately."""
    attempt: AttemptRef
    step: int
    base: ArtifactRef
    rank: int
    ownership_id: str
    fragment: ArtifactRef


class TwoRankCheckpoint:
    """One trusted coordinator publishes one verified result for a step.

    Rank fragments and incomplete publications are evidence only. Only the
    fixed commit record makes the result resumable. A fixed prepublication
    claim survives process loss and prevents replay through fresh instances.
    """

    def __init__(self, store: ArtifactStore, attempt: AttemptRef, step: int,
                 base: ArtifactRef, owners: tuple[str, str]):
        if (type(step) is not int or not 0 < step <= 2**63 - 1
                or type(owners) is not tuple or len(owners) != 2
                or any(type(o) is not str or not o or len(o) > 128 for o in owners)
                or owners[0] == owners[1] or type(base) is not ArtifactRef
                or base.run_id != attempt.run_id):
            raise CustodyError("Invalid two-rank checkpoint binding.")
        self.store, self.attempt, self.step, self.base, self.owners = store, attempt, step, base, owners
        control = store.attempt_path(attempt) / "control"
        self.control = control
        self.claim = control / f"rank-step-{step}.claimed.json"
        self.commit_record = control / f"rank-step-{step}.committed.json"

    def _result(self, receipts: tuple[RankStageReceipt, RankStageReceipt]) -> dict:
        if type(receipts) is not tuple or len(receipts) != 2:
            raise CustodyError("Exactly two ordered rank stage receipts are required.")
        for rank, (receipt, owner) in enumerate(zip(receipts, self.owners)):
            if (type(receipt) is not RankStageReceipt or receipt.attempt != self.attempt
                    or type(receipt.step) is not int or receipt.step != self.step
                    or receipt.base != self.base or type(receipt.rank) is not int or receipt.rank != rank
                    or receipt.ownership_id != owner or type(receipt.fragment) is not ArtifactRef
                    or receipt.fragment.attempt != self.attempt or receipt.fragment == self.base
                    or receipt.fragment.size_bytes <= 0):
                raise CustodyError("A rank receipt does not match this attempt, step, or owner.")
        if receipts[0].fragment.artifact_id == receipts[1].fragment.artifact_id:
            raise CustodyError("Each rank requires its own checkpoint fragment.")
        return {"schema": 1, "format": "two-rank-checkpoint-v1", "attempt": asdict(self.attempt),
                "step": self.step, "base": asdict(self.base), "ranks": [asdict(r) for r in receipts]}

    def _verify_artifact(self, ref: ArtifactRef, kinds: tuple[str, ...],
                         inputs: tuple[ArtifactRef, ...] | None = None):
        payload = self.store.verify(ref)
        # Read the same private, sealed manifest as ArtifactStore._manifest_ref;
        # ArtifactRef itself intentionally does not carry the artifact kind.
        seal = _read_record(payload.parent / "seal.json")
        manifest = _read_record(payload.parent / "manifest.json", seal["sha256"])
        if manifest.get("kind") not in kinds:
            raise CustodyError("Unexpected artifact kind in two-rank checkpoint lineage.")
        if inputs is not None and manifest.get("inputs") != [asdict(item) for item in inputs]:
            raise CustodyError("Unexpected artifact inputs in two-rank checkpoint lineage.")
        return payload, manifest

    def _validate_base(self):
        if self.step == 1:
            attempt_record = _read_record(self.control.parent / "attempt.json")
            if attempt_record.get("resume_from") != [asdict(self.base)]:
                raise CustodyError("The step-one base must be the sole declared resume checkpoint.")
            payload, manifest = self._verify_artifact(self.base, ("checkpoint", "segment_checkpoint", "evidence"))
            if manifest["kind"] == "evidence":
                try:
                    record = _read_record(payload, self.base.sha256)
                    if (record.get("format") != "two-rank-checkpoint-v1"
                            or self.base.attempt == self.attempt):
                        raise CustodyError("The joint resume base must come from a separate committed attempt.")
                    source = TwoRankCheckpoint(
                        self.store, self.base.attempt, record["step"], ArtifactRef(**record["base"]),
                        tuple(row["ownership_id"] for row in record["ranks"]),
                    )
                    if source.committed() != self.base:
                        raise CustodyError("The joint resume base does not have its exact committed pointer.")
                except (TypeError, KeyError) as error:
                    raise CustodyError("Invalid joint resume base.") from error
            return
        try:
            previous = ArtifactRef(**_read_record(self.control / f"rank-step-{self.step - 1}.committed.json")["result"])
        except (FileNotFoundError, TypeError, KeyError) as error:
            raise CustodyError("The immediately preceding step is not committed.") from error
        if previous != self.base or previous.attempt != self.attempt:
            raise CustodyError("This step does not extend the immediately preceding checkpoint.")
        payload, _ = self._verify_artifact(self.base, ("evidence",))
        previous_record = _read_record(payload, self.base.sha256)
        if (previous_record.get("format") != "two-rank-checkpoint-v1"
                or previous_record.get("attempt") != asdict(self.attempt)
                or previous_record.get("step") != self.step - 1
                or previous_record != _read_record(self.control / f"rank-step-{self.step - 1}.claimed.json")):
            raise CustodyError("The predecessor checkpoint does not match its committed step.")

    def _verify_fragments(self, fragments: tuple[ArtifactRef, ...]):
        for ref in fragments:
            self._verify_artifact(ref, ("segment_checkpoint",), (self.base,))

    def commit(self, receipts: tuple[RankStageReceipt, RankStageReceipt]) -> ArtifactRef:
        """Publish only after both fragments verify; never retry after the claim.

        A pre-claim refusal writes no result, but does not authorize replaying an
        optimizer step. Owners alone know whether a candidate was safely staged
        or whether a fresh attempt restoring the previous committed base is required.
        """
        # Early refusal is diagnostic; exclusive write_record remains the race boundary.
        if self.claim.exists():
            raise CustodyError("This step was already claimed; do not replay it.")
        self._validate_base()
        record = self._result(receipts)
        fragments = tuple(receipt.fragment for receipt in receipts)
        self._verify_fragments(fragments)
        try:
            write_record(self.claim, record)
        except FileExistsError as error:
            raise CustodyError("This step was already claimed; do not replay it.") from error
        except Exception as error:
            raise CustodyError("Checkpoint claim is uncertain; do not replay this attempt.") from error
        try:
            result = self.store.publish_bytes(self.attempt, f"rank-step-{self.step}.json", "evidence",
                                              _encode_record(record), inputs=(self.base, *fragments))
            self._verify_artifact(result, ("evidence",), (self.base, *fragments))
            write_record(self.commit_record, {"schema": 1, "result": asdict(result)})
            if self.committed() != result:
                raise CustodyError("The committed checkpoint could not be read back.")
        except Exception as error:
            raise CustodyError("Checkpoint outcome is uncertain; verify the commit record or restore the prior base.") from error
        return result

    def committed(self) -> ArtifactRef | None:
        """Verify the one committed result and its linked base and rank fragments."""
        try:
            published = _read_record(self.commit_record)
        except FileNotFoundError:
            return None
        try:
            result = ArtifactRef(**published["result"])
            claim = _read_record(self.claim)
            ranks = []
            for row in claim["ranks"]:
                data = dict(row)
                data.update(attempt=AttemptRef(**row["attempt"]), base=ArtifactRef(**row["base"]),
                            fragment=ArtifactRef(**row["fragment"]))
                ranks.append(RankStageReceipt(**data))
            receipts = tuple(ranks)
            if result.attempt != self.attempt or claim != self._result(receipts):
                raise CustodyError("The published checkpoint does not match the bound step.")
            fragments = tuple(receipt.fragment for receipt in receipts)
            payload, _ = self._verify_artifact(result, ("evidence",), (self.base, *fragments))
            if _read_record(payload, result.sha256) != claim:
                raise CustodyError("The published checkpoint differs from its claim.")
            self._validate_base()
            self._verify_fragments(fragments)
        except (KeyError, TypeError) as error:
            raise CustodyError("Invalid two-rank checkpoint record.") from error
        return result
