"""Strict lineage admission and split integrity for training datasets."""

from __future__ import annotations

from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path
import re
from typing import Mapping

from .artifacts import CustodyError, _copy, _open_regular, checked_path, relative_parts

_SPLITS = {"train", "validation", "final_held_out"}
_ROLES = {"examples", "labels", "rewards", "corrections", "filtering_signals"}
_REAL_ORIGINS = {"independent_licensed", "user_provided", "local_model"}
_TEST_ONLY = "software_test_only"
_PURPOSES = {"real_training", _TEST_ONLY}
_SHA = re.compile(r"[0-9a-f]{64}")
_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}")
_NO_OPENAI = re.compile(r"open[\s_-]*ai|chat[\s_-]*gpt|(?:^|[ /:])gpt[-_\s]", re.I)
_MAX_FILES = 4096
_MAX_SAMPLES = 250_000


@dataclass(frozen=True, slots=True)
class Lineage:
    role: str
    origin: str
    producer: str
    evidence: str
    license_ref: str | None = None
    model_id: str | None = None


@dataclass(frozen=True, slots=True)
class Sample:
    id: str
    sha256: str


@dataclass(frozen=True, slots=True)
class DatasetFile:
    id: str
    split: str
    path: str
    sha256: str
    size_bytes: int
    lineage: tuple[Lineage, ...]
    samples: tuple[Sample, ...]


@dataclass(frozen=True, slots=True)
class DatasetManifest:
    purpose: str
    files: tuple[DatasetFile, ...]

    def as_record(self) -> dict:
        return {"schema": 1, **asdict(self)}

    @property
    def sha256(self) -> str:
        data = json.dumps(self.as_record(), sort_keys=True, ensure_ascii=True).encode("ascii")
        return hashlib.sha256(data).hexdigest()


def _strict_fields(value, required: set[str], optional: set[str] | None = None) -> Mapping:
    optional = optional or set()
    if not isinstance(value, Mapping) or not required <= value.keys() <= required | optional:
        raise CustodyError("Dataset provenance has missing or unrecognized fields.")
    return value


def _text(value, limit: int = 256) -> str:
    if type(value) is not str or not value.strip() or len(value) > limit or any(ord(c) < 32 for c in value):
        raise CustodyError("Dataset provenance requires bounded, non-empty text.")
    return value


def _identifier(value) -> str:
    if not _ID.fullmatch(_text(value, 128)):
        raise CustodyError("The dataset or sample identity is invalid.")
    return value


def _digest(value) -> str:
    if type(value) is not str or not _SHA.fullmatch(value):
        raise CustodyError("Dataset provenance requires a lowercase SHA-256 digest.")
    return value


def _lineage(value, purpose: str) -> Lineage:
    item = _strict_fields(value, {"role", "origin", "producer", "evidence"}, {"license_ref", "model_id"})
    role, origin = _text(item["role"]), _text(item["origin"])
    producer, evidence = _text(item["producer"]), _text(item["evidence"], 2048)
    license_ref, model_id = item.get("license_ref"), item.get("model_id")
    if role not in _ROLES:
        raise CustodyError("This training contribution has an unsupported lineage role.")
    if _NO_OPENAI.search(producer) or (model_id is not None and _NO_OPENAI.search(_text(model_id))):
        raise CustodyError("OpenAI-generated training contributions are excluded.")
    if origin not in _REAL_ORIGINS and not (purpose == _TEST_ONLY and origin == _TEST_ONLY):
        raise CustodyError("This training contribution has an unauthorized origin.")
    if origin in {"independent_licensed", "local_model"}:
        license_ref = _text(license_ref, 2048)
    elif license_ref is not None:
        license_ref = _text(license_ref, 2048)
    if origin == "local_model":
        model_id = _text(model_id)
    return Lineage(role, origin, producer, evidence, license_ref, model_id)


def validate_dataset_manifest(value, *, purpose: str = "real_training") -> DatasetManifest:
    """Validate a complete explicit lineage assertion, independent of its bytes.

    Evidence references must come from trusted admission. The parser can reject
    prohibited declarations and overlap, but cannot prove a source's honesty.
    """
    item = _strict_fields(value, {"schema", "purpose", "files"})
    if type(item["schema"]) is not int or item["schema"] != 1 or purpose not in _PURPOSES or item["purpose"] != purpose:
        raise CustodyError("Dataset purpose or schema does not match this execution boundary.")
    raw_files = item["files"]
    if not isinstance(raw_files, (list, tuple)) or not (3 <= len(raw_files) <= _MAX_FILES):
        raise CustodyError("Dataset file records are missing or exceed the manifest limit.")
    splits: set[str] = set()
    file_ids: set[str] = set()
    paths: set[str] = set()
    sample_ids: set[str] = set()
    file_hashes: dict[str, str] = {}
    sample_hashes: dict[str, str] = {}
    files: list[DatasetFile] = []
    for raw in raw_files:
        row = _strict_fields(raw, {"id", "split", "path", "sha256", "size_bytes", "lineage", "samples"})
        file_id, split = _identifier(row["id"]), _text(row["split"])
        path = "/".join(relative_parts(row["path"]))
        sha, size = _digest(row["sha256"]), row["size_bytes"]
        if split not in _SPLITS or type(size) is not int or size <= 0:
            raise CustodyError("A dataset file must belong to a known split and have a positive byte size.")
        if file_id.casefold() in file_ids or path.casefold() in paths:
            raise CustodyError("Dataset file IDs and paths must be unique.")
        if sha in file_hashes and file_hashes[sha] != split:
            raise CustodyError("A dataset file's content appears in more than one split.")
        file_ids.add(file_id.casefold())
        paths.add(path.casefold())
        file_hashes[sha] = split
        splits.add(split)
        lineage = row["lineage"]
        if not isinstance(lineage, (list, tuple)) or not (1 <= len(lineage) <= 24):
            raise CustodyError("Dataset provenance requires bounded lineage for each file.")
        traces = tuple(_lineage(trace, purpose) for trace in lineage)
        if not any(trace.role == "examples" for trace in traces):
            raise CustodyError("Training examples require their own provenance.")
        samples = row["samples"]
        if not isinstance(samples, (list, tuple)) or not samples or len(sample_ids) + len(samples) > _MAX_SAMPLES:
            raise CustodyError("Dataset samples are missing or exceed the manifest limit.")
        verified_samples = []
        for raw_sample in samples:
            sample = _strict_fields(raw_sample, {"id", "sha256"})
            sample_id, sample_hash = _identifier(sample["id"]), _digest(sample["sha256"])
            if sample_id.casefold() in sample_ids:
                raise CustodyError("Dataset sample IDs must be unique across all splits.")
            if sample_hash in sample_hashes and sample_hashes[sample_hash] != split:
                raise CustodyError("A dataset sample's content appears in more than one split.")
            sample_ids.add(sample_id.casefold())
            sample_hashes[sample_hash] = split
            verified_samples.append(Sample(sample_id, sample_hash))
        files.append(DatasetFile(file_id, split, path, sha, size, traces, tuple(verified_samples)))
    if splits != _SPLITS:
        raise CustodyError("Train, validation and final held-out datasets are all required.")
    return DatasetManifest(purpose, tuple(files))


def verify_dataset_files(manifest: DatasetManifest, root: Path | str) -> tuple[Path, ...]:
    """Validate real bytes beneath an explicitly admitted root, without mutating them."""
    path = Path(root)
    checked_path(path, directory=True)
    verified = []
    # Re-admit the record before file I/O even if a caller constructed a data class.
    manifest = validate_dataset_manifest(manifest.as_record(), purpose=manifest.purpose)
    for entry in manifest.files:
        source = path.joinpath(*relative_parts(entry.path))
        with _open_regular(source) as content:
            if _copy(content, None, entry.size_bytes) != (entry.sha256, entry.size_bytes):
                raise CustodyError("Dataset bytes do not match their immutable manifest.")
        verified.append(source)
    return tuple(verified)
