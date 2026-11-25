"""Immutable, data-only checkpoints and bound evidence for ALORA sessions."""

from dataclasses import KW_ONLY, asdict, dataclass
import json
from pathlib import Path
import sys

from custody import ArtifactRef, ArtifactStore, AttemptRef, CustodyError
from custody.artifacts import _encode_record, _read_record
from custody.snapshot import load_snapshot, publish_snapshot

_EVIDENCE_BYTES = 1024 * 1024
_PURPOSES = {"software_test_only", "real_training"}


@dataclass(frozen=True, slots=True)
class CheckpointPublication:
    checkpoint: ArtifactRef
    evidence: ArtifactRef


def _count(value) -> bool:
    return type(value) is int and 0 <= value <= sys.maxsize


def _record_bytes(record: dict) -> bytes:
    try:
        encoded = _encode_record(record)
    except (ValueError, TypeError, RecursionError, OverflowError) as error:
        raise CustodyError("Checkpoint evidence and identity must be finite JSON.") from error
    if len(encoded) > _EVIDENCE_BYTES:
        raise CustodyError("Checkpoint evidence exceeds its byte allowance.")
    return encoded


@dataclass(frozen=True, slots=True)
class CheckpointPublisher:
    store: ArtifactStore
    attempt: AttemptRef
    _: KW_ONLY
    purpose: str
    max_bytes: int

    def __post_init__(self):
        if (type(self.store) is not ArtifactStore or type(self.attempt) is not AttemptRef
                or type(self.purpose) is not str or self.purpose not in _PURPOSES
                or not _count(self.max_bytes) or self.max_bytes == 0):
            raise CustodyError("ALORA publication requires a custody attempt, purpose and byte limit.")
        configuration = _read_record(
            self.store.attempt_path(self.attempt) / "attempt.json"
        ).get("configuration")
        if type(configuration) is not dict or configuration.get("purpose") != self.purpose:
            raise CustodyError("The checkpoint purpose differs from its custody attempt.")

    def _validate(self, snapshot: dict, record: dict) -> tuple[str, int]:
        import torch

        if (type(snapshot) is not dict or type(record) is not dict
                or any(type(item.get("schema")) is not int or item["schema"] != 1
                       or item.get("purpose") != self.purpose for item in (snapshot, record))
                or snapshot.get("kind") != "alora-training"
                or not _count(snapshot.get("updates")) or not _count(record.get("updates"))
                or snapshot["updates"] != record["updates"]
                or type(snapshot.get("failed")) is not bool
                or type(snapshot.get("identity")) is not dict
                or type(record.get("identity")) is not dict
                or type(snapshot.get("optimizer")) is not dict
                or type(snapshot.get("adapters")) is not dict or not snapshot["adapters"]
                or ("cursor" in snapshot and not _count(snapshot["cursor"]))):
            raise CustodyError("The ALORA checkpoint and evidence do not match their schema.")
        if _record_bytes(snapshot["identity"]) != _record_bytes(record["identity"]):
            raise CustodyError("The checkpoint identity differs from its evidence.")
        _record_bytes(record)

        for name, value in snapshot["adapters"].items():
            if (type(name) is not str or not name
                    or not isinstance(value, torch.Tensor)
                    or value.device.type != "cpu" or value.layout != torch.strided):
                raise CustodyError("ALORA adapters must be named dense CPU tensors.")
            if not snapshot["failed"] and not torch.isfinite(value).all().item():
                raise CustodyError("A non-failed checkpoint cannot contain non-finite adapters.")
        rng = snapshot.get("torch_cpu_rng")
        if (not isinstance(rng, torch.Tensor) or rng.device.type != "cpu"
                or rng.layout != torch.strided or rng.dtype != torch.uint8
                or rng.dim() != 1 or rng.numel() == 0):
            raise CustodyError("An ALORA checkpoint requires a CPU RNG-state byte tensor.")

        status, updates = record.get("status"), snapshot["updates"]
        if ((status == "initial" and not snapshot["failed"] and updates == 0)
                or (status == "completed" and not snapshot["failed"] and updates > 0)
                or (status == "failed" and snapshot["failed"])):
            return status, updates
        raise CustodyError("The checkpoint does not match its recorded update outcome.")

    def _verify(self, reference: ArtifactRef) -> Path:
        if type(reference) is not ArtifactRef or reference.attempt != self.attempt:
            raise CustodyError("The checkpoint publication belongs to a different custody attempt.")
        return self.store.verify(reference)

    def publish(self, snapshot: dict, record: dict) -> CheckpointPublication:
        """Seal a recoverable checkpoint, then evidence binding its exact bytes."""
        status, updates = self._validate(snapshot, record)
        frozen_record = json.loads(_record_bytes(record))
        checkpoint = publish_snapshot(
            self.store, self.attempt, f"alora-{status}-update-{updates}", snapshot,
            max_bytes=self.max_bytes, kind="segment_checkpoint",
        )
        self._verify(checkpoint)
        payload = _record_bytes({
            "schema": 1, "kind": "alora-checkpoint-evidence", "purpose": self.purpose,
            "status": status, "updates": updates, "attempt": asdict(self.attempt),
            "checkpoint": asdict(checkpoint), "record": frozen_record,
        })
        evidence = self.store.publish_bytes(
            self.attempt, f"alora-{status}-update-{updates}-evidence.json",
            "evidence", payload, inputs=(checkpoint,), max_bytes=_EVIDENCE_BYTES,
        )
        publication = CheckpointPublication(checkpoint, evidence)
        # Verify publication through the same path used for resume and delivery.
        self._verified(publication)
        return publication

    def _verified(self, publication: CheckpointPublication) -> tuple[dict, Path]:
        if type(publication) is not CheckpointPublication:
            raise CustodyError("Expected an ALORA checkpoint publication.")
        checkpoint_path = self._verify(publication.checkpoint)
        evidence_path = self._verify(publication.evidence)
        checkpoint_manifest = _read_record(checkpoint_path.parent / "manifest.json")
        evidence_manifest = _read_record(evidence_path.parent / "manifest.json")
        evidence = _read_record(evidence_path)
        if (checkpoint_manifest.get("kind") != "segment_checkpoint"
                or evidence_manifest.get("kind") != "evidence"
                or evidence_manifest.get("inputs") != [asdict(publication.checkpoint)]
                or type(evidence.get("schema")) is not int or evidence["schema"] != 1
                or evidence.get("kind") != "alora-checkpoint-evidence"
                or evidence.get("purpose") != self.purpose
                or evidence.get("attempt") != asdict(self.attempt)
                or evidence.get("checkpoint") != asdict(publication.checkpoint)):
            raise CustodyError("The checkpoint and immutable evidence are not bound together.")
        snapshot = load_snapshot(self.store, publication.checkpoint, max_bytes=self.max_bytes)
        status, updates = self._validate(snapshot, evidence.get("record"))
        if (evidence.get("status") != status or not _count(evidence.get("updates"))
                or evidence["updates"] != updates):
            raise CustodyError("The checkpoint evidence outcome is inconsistent.")
        self._verify(publication.checkpoint)
        self._verify(publication.evidence)
        return snapshot, checkpoint_path

    def load(self, publication: CheckpointPublication) -> dict:
        """Verify both immutable artifacts before decoding and admitting a resume."""
        return self._verified(publication)[0]

    def checkpoint_path(self, publication: CheckpointPublication) -> Path:
        """Return the verified checkpoint file for TrainingControl delivery."""
        return self._verified(publication)[1]
