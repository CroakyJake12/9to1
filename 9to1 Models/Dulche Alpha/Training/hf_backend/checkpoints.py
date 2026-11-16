"""Verified immutable publication for HF session checkpoints."""

from dataclasses import KW_ONLY, asdict, dataclass
import json
import re
import sys

from custody import ArtifactRef, ArtifactStore, AttemptRef, CustodyError
from custody.artifacts import _encode_record, _open_regular, _read_record
from custody.snapshot import publish_snapshot

_EVIDENCE_BYTES = 1024 * 1024
_DIGEST = re.compile(r"[a-f0-9]{64}")
_PURPOSES = {"software_test_only", "real_training"}


@dataclass(frozen=True, slots=True)
class CheckpointPublication:
    checkpoint: ArtifactRef
    evidence: ArtifactRef


def _update(value) -> bool:
    return type(value) is int and 0 <= value <= sys.maxsize


def _digest(value) -> bool:
    return type(value) is str and _DIGEST.fullmatch(value) is not None


@dataclass(frozen=True, slots=True)
class VerifiedCheckpointPublisher:
    store: ArtifactStore
    attempt: AttemptRef
    _: KW_ONLY
    purpose: str
    max_bytes: int

    def __post_init__(self):
        if (type(self.store) is not ArtifactStore or type(self.attempt) is not AttemptRef
                or type(self.purpose) is not str or self.purpose not in _PURPOSES
                or not _update(self.max_bytes) or self.max_bytes == 0):
            raise CustodyError("Verified checkpoint publication requires a real custody attempt and bounded purpose.")
        path = self.store.attempt_path(self.attempt)
        config = _read_record(path / "attempt.json").get("configuration")
        if type(config) is not dict or config.get("purpose", self.purpose) != self.purpose:
            raise CustodyError("The checkpoint publisher purpose differs from the custody attempt.")

    def _validate(self, snapshot: dict, record: dict) -> tuple[str, int]:
        if (type(snapshot) is not dict or type(record) is not dict
                or any(type(item.get("schema")) is not int or item["schema"] != 1
                       or item.get("purpose") != self.purpose for item in (snapshot, record))
                or snapshot.get("kind") != "hf-local-session"
                or not _update(snapshot.get("updates"))
                or type(snapshot.get("failed")) is not bool
                or type(snapshot.get("model")) is not dict or not snapshot["model"]
                or type(snapshot.get("optimizer")) is not dict):
            raise CustodyError("The checkpoint and update record do not match the admitted HF schema and purpose.")

        manifest = snapshot.get("dataset_manifest_sha256")
        if (not _digest(manifest) or record.get("dataset_manifest_sha256") != manifest
                or not _digest(record.get("dataset_file_sha256"))):
            raise CustodyError("The checkpoint does not match the recorded dataset lineage.")

        completed = snapshot["updates"]
        status = record.get("status", "completed")
        if status == "completed" and not snapshot["failed"]:
            if completed > 0 and _update(record.get("update")) and record["update"] == completed:
                return status, completed
        if status == "failed" and snapshot["failed"]:
            attempted = record.get("attempted_update")
            phase = record.get("phase")
            if (type(phase) is str and 0 < len(phase) <= 64 and _update(attempted)
                    and attempted > 0 and attempted in (completed, completed + 1)
                    and _update(record.get("completed_updates")) and record["completed_updates"] == completed):
                return status, attempted
        raise CustodyError("The checkpoint does not match the recorded update outcome.")

    def _verify(self, reference: ArtifactRef):
        if type(reference) is not ArtifactRef or reference.attempt != self.attempt:
            raise CustodyError("The publisher returned an artifact outside its custody attempt.")
        return self.store.verify(reference)

    def publish(self, snapshot: dict, record: dict) -> CheckpointPublication:
        """Seal the candidate first, then bind and verify its update evidence."""
        status, update = self._validate(snapshot, record)
        try:
            encoded_record = _encode_record(record)
        except (ValueError, TypeError, RecursionError) as exc:
            raise CustodyError("The checkpoint update record must be finite JSON.") from exc
        if len(encoded_record) > _EVIDENCE_BYTES:
            raise CustodyError("The checkpoint update record exceeds its byte allowance.")
        frozen_record = json.loads(encoded_record)

        checkpoint = publish_snapshot(
            self.store, self.attempt, f"{status}-update-{update}", snapshot,
            max_bytes=self.max_bytes, kind="segment_checkpoint",
        )
        self._verify(checkpoint)
        payload = _encode_record({
            "schema": 1, "purpose": self.purpose, "status": status,
            "checkpoint": asdict(checkpoint), "record": frozen_record,
        })
        evidence = self.store.publish_bytes(
            self.attempt, f"{status}-update-{update}-evidence.json", "evidence", payload,
            inputs=(checkpoint,), max_bytes=_EVIDENCE_BYTES,
        )
        path = self._verify(evidence)
        with _open_regular(path) as stream:
            if stream.read(_EVIDENCE_BYTES + 1) != payload:
                raise CustodyError("The published checkpoint evidence does not match its update record.")
        self._verify(checkpoint)
        return CheckpointPublication(checkpoint, evidence)
