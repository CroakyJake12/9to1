"""Immutable experiment records and verified copies on a private filesystem."""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import stat
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import BinaryIO, Mapping
from uuid import UUID, uuid4

_REPARSE = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
_SHA256 = re.compile(r"[0-9a-f]{64}")
_NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,119}")
_RESERVED = {"AUX", "CON", "NUL", "PRN", *(f"COM{i}" for i in range(1, 10)),
             *(f"LPT{i}" for i in range(1, 10))}
_JSON_LIMIT = 1024 * 1024
_CHUNK = 1024 * 1024
_KINDS = {
    "original", "dataset", "dataset_manifest", "config", "script", "environment",
    "log", "checkpoint", "segment_checkpoint", "optimizer_state", "adapter",
    "edited_weights", "quantization", "evaluation", "benchmark", "model", "evidence",
}
_OUTCOMES = {"passed", "failed", "paused", "cancelled", "interrupted"}


class CustodyError(ValueError):
    """An experiment record cannot be safely admitted or verified."""


@dataclass(frozen=True, slots=True)
class AttemptRef:
    run_id: str
    attempt_id: str


@dataclass(frozen=True, slots=True)
class ArtifactRef:
    run_id: str
    attempt_id: str
    artifact_id: str
    sha256: str
    size_bytes: int

    @property
    def attempt(self) -> AttemptRef:
        return AttemptRef(self.run_id, self.attempt_id)


@dataclass(frozen=True, slots=True)
class AttemptVerification:
    artifacts: tuple[ArtifactRef, ...]
    incomplete_artifact_ids: tuple[str, ...]


def canonical_id(value: str) -> str:
    try:
        if type(value) is str and str(UUID(value)) == value:
            return value
    except (ValueError, AttributeError):
        pass
    raise CustodyError("An exact canonical UUID is required.")


def relative_parts(value: str) -> tuple[str, ...]:
    if type(value) is not str or len(value) > 240:
        raise CustodyError("Expected a bounded relative name.")
    parts = tuple(value.split("/"))
    if any(
        not _NAME.fullmatch(part) or part.endswith(".")
        or part.split(".", 1)[0].upper() in _RESERVED for part in parts
    ):
        raise CustodyError("Unsafe relative experiment path.")
    return parts


def checked_path(path: Path, *, directory: bool) -> os.stat_result:
    """Check every component without resolving away symbolic links or junctions."""
    if not path.is_absolute() or ".." in path.parts:
        raise CustodyError("An absolute, non-traversing path is required.")
    for component in (*reversed(path.parents), path):
        info = component.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & _REPARSE:
            raise CustodyError("Experiment paths cannot traverse links or reparse points.")
        if component != path or directory:
            if not stat.S_ISDIR(info.st_mode):
                raise CustodyError("An experiment directory is not a directory.")
        elif not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
            raise CustodyError("An experiment file must be a regular file without aliases.")
    return info


def _open_regular(path: Path) -> BinaryIO:
    inspected = checked_path(path, directory=False)
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    fd = os.open(path, flags)
    try:
        opened = os.fstat(fd)
        checked_path(path, directory=False)
        if not os.path.samestat(inspected, opened) or not stat.S_ISREG(opened.st_mode) or opened.st_nlink != 1:
            raise CustodyError("The experiment file changed before it could be opened.")
        return os.fdopen(fd, "rb")
    except BaseException:
        os.close(fd)
        raise


def _fsync_directory(path: Path) -> None:
    if os.name != "nt":
        fd = os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(fd)
        finally:
            os.close(fd)


def create_directory(path: Path, *, exclusive: bool = False) -> None:
    checked_path(path.parent, directory=True)
    path.mkdir(mode=0o700, exist_ok=not exclusive)
    checked_path(path, directory=True)
    _fsync_directory(path.parent)


def _copy(source: BinaryIO, output: BinaryIO | None, max_bytes: int | None) -> tuple[str, int]:
    if max_bytes is not None and (type(max_bytes) is not int or max_bytes < 0):
        raise CustodyError("An artifact byte limit must be a nonnegative integer.")
    digest, count = hashlib.sha256(), 0
    while True:
        chunk = source.read(_CHUNK)
        if not chunk:
            break
        if not isinstance(chunk, bytes):
            raise CustodyError("Artifact content must be bytes.")
        count += len(chunk)
        if max_bytes is not None and count > max_bytes:
            raise CustodyError("The copy would exceed its artifact byte allowance.")
        digest.update(chunk)
        if output is not None:
            output.write(chunk)
    return digest.hexdigest(), count


def _write_atomic(
    target: Path, source: BinaryIO, *, max_bytes: int | None = None, expected: tuple[str, int] | None = None,
) -> tuple[str, int]:
    checked_path(target.parent, directory=True)
    stage = target.with_name(f".{target.name}.{uuid4().hex}.partial")
    mode = os.O_WRONLY | os.O_EXCL | os.O_CREAT | getattr(os, "O_BINARY", 0)
    with os.fdopen(os.open(stage, mode, 0o600), "wb") as output:
        try:
            result = _copy(source, output, max_bytes)
        finally:
            output.flush()
            os.fsync(output.fileno())
    if expected is not None and result != expected:
        raise CustodyError("The copied bytes do not match the recorded artifact.")
    checked_path(stage, directory=False)
    # A sibling hard link publishes the completed inode in one operation and
    # fails if the final name exists. A failed or interrupted stage is retained.
    os.link(stage, target)
    stage.unlink()
    _fsync_directory(target.parent)
    return result


def _encode_record(values: Mapping) -> bytes:
    return (json.dumps(values, allow_nan=False, sort_keys=True, ensure_ascii=True) + "\n").encode("ascii")


def write_record(path: Path, values: Mapping) -> None:
    _write_atomic(path, io.BytesIO(_encode_record(values)), max_bytes=_JSON_LIMIT)


def _read_record(path: Path, expected_sha256: str | None = None) -> dict:
    with _open_regular(path) as content:
        data = content.read(_JSON_LIMIT + 1)
    if len(data) > _JSON_LIMIT:
        raise CustodyError("Experiment record is too large.")
    if expected_sha256 is not None and hashlib.sha256(data).hexdigest() != expected_sha256:
        raise CustodyError("The artifact manifest failed its integrity check.")
    try:
        result = json.loads(data)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise CustodyError("Experiment record is malformed.") from error
    if type(result) is not dict or result.get("schema") != 1:
        raise CustodyError("Experiment record schema is unsupported.")
    return result


def _now() -> str:
    return datetime.now(timezone.utc).isoformat()


class ArtifactStore:
    """A private, caller-owned store on a filesystem supporting hard links."""

    def __init__(self, root: Path | str):
        self.root = Path(root)
        create_directory(self.root)
        create_directory(self.root / "runs")

    def _run(self, run_id: str) -> Path:
        path = self.root / "runs" / canonical_id(run_id)
        if _read_record(path / "run.json").get("run_id") != run_id:
            raise CustodyError("The run record does not match its path.")
        return path

    def attempt_path(self, attempt: AttemptRef) -> Path:
        path = self._run(attempt.run_id) / "attempts" / canonical_id(attempt.attempt_id)
        record = _read_record(path / "attempt.json")
        if (record.get("run_id"), record.get("attempt_id")) != (attempt.run_id, attempt.attempt_id):
            raise CustodyError("The attempt record does not match its path.")
        return path

    def create_run(self, objective: str, plan: Mapping) -> str:
        if not isinstance(objective, str) or not objective.strip():
            raise CustodyError("An experiment objective is required.")
        run_id = str(uuid4())
        run = self.root / "runs" / run_id
        create_directory(run, exclusive=True)
        create_directory(run / "attempts", exclusive=True)
        write_record(run / "run.json", {
            "schema": 1, "run_id": run_id, "created_at": _now(), "objective": objective, "plan": dict(plan),
        })
        return run_id

    def begin_attempt(
        self, run_id: str, configuration: Mapping, *, resume_from: tuple[ArtifactRef, ...] = (),
    ) -> AttemptRef:
        run = self._run(run_id)
        for reference in resume_from:
            if reference.run_id != run_id:
                raise CustodyError("A resumed attempt must belong to the original run.")
            self.verify(reference)
        attempt = AttemptRef(run_id, str(uuid4()))
        directory = run / "attempts" / attempt.attempt_id
        create_directory(directory, exclusive=True)
        for child in ("artifacts", "work", "restored", "control"):
            create_directory(directory / child, exclusive=True)
        write_record(directory / "attempt.json", {
            "schema": 1, **asdict(attempt), "created_at": _now(), "configuration": dict(configuration),
            "resume_from": [asdict(reference) for reference in resume_from],
        })
        return attempt

    def publish_bytes(self, attempt: AttemptRef, name: str, kind: str, data: bytes, **options) -> ArtifactRef:
        if not isinstance(data, bytes):
            raise CustodyError("Artifact content must be bytes.")
        return self.publish(attempt, name, kind, io.BytesIO(data), **options)

    def publish_file(
        self, attempt: AttemptRef, name: str, kind: str, relative_work_file: str, **options,
    ) -> ArtifactRef:
        work_file = self.attempt_path(attempt) / "work"
        work_file = work_file.joinpath(*relative_parts(relative_work_file))
        with _open_regular(work_file) as source:
            return self.publish(attempt, name, kind, source, **options)

    def publish(
        self, attempt: AttemptRef, name: str, kind: str, source: BinaryIO, *,
        max_bytes: int | None = None, inputs: tuple[ArtifactRef, ...] = (),
    ) -> ArtifactRef:
        relative_parts(name)
        if kind not in _KINDS:
            raise CustodyError("Unknown experiment artifact kind.")
        for item in inputs:
            self._payload(item)
        base = self.attempt_path(attempt) / "artifacts"
        artifact_id = str(uuid4())
        directory = base / artifact_id
        create_directory(directory, exclusive=True)
        fields = {
            "schema": 1, **asdict(attempt), "artifact_id": artifact_id,
            "name": name, "kind": kind, "inputs": [asdict(item) for item in inputs], "created_at": _now(),
        }
        write_record(directory / "intent.json", fields)
        sha, size = _write_atomic(directory / "payload", source, max_bytes=max_bytes)
        reference = ArtifactRef(attempt.run_id, attempt.attempt_id, artifact_id, sha, size)
        manifest = {**fields, **asdict(reference)}
        write_record(directory / "manifest.json", manifest)
        write_record(directory / "seal.json", {"schema": 1, "sha256": hashlib.sha256(_encode_record(manifest)).hexdigest()})
        return reference

    def _artifact_directory(self, attempt: AttemptRef, artifact_id: str) -> Path:
        return self.attempt_path(attempt) / "artifacts" / canonical_id(artifact_id)

    def _manifest_ref(self, attempt: AttemptRef, artifact_id: str) -> ArtifactRef:
        directory = self._artifact_directory(attempt, artifact_id)
        seal = _read_record(directory / "seal.json").get("sha256")
        if not isinstance(seal, str) or not _SHA256.fullmatch(seal):
            raise CustodyError("The artifact manifest seal is invalid.")
        record = _read_record(directory / "manifest.json", seal)
        if ((record.get("run_id"), record.get("attempt_id"), record.get("artifact_id"))
                != (attempt.run_id, attempt.attempt_id, artifact_id)):
            raise CustodyError("The artifact manifest does not match its path.")
        sha, size = record.get("sha256"), record.get("size_bytes")
        if not isinstance(sha, str) or not _SHA256.fullmatch(sha) or type(size) is not int or size < 0:
            raise CustodyError("The artifact digest or size is invalid.")
        return ArtifactRef(attempt.run_id, attempt.attempt_id, artifact_id, sha, size)

    def _payload(self, reference: ArtifactRef) -> Path:
        actual = self._manifest_ref(reference.attempt, reference.artifact_id)
        if reference != actual:
            raise CustodyError("The artifact reference does not match its immutable manifest.")
        return self._artifact_directory(reference.attempt, reference.artifact_id) / "payload"

    def verify(self, reference: ArtifactRef) -> Path:
        payload = self._payload(reference)
        with _open_regular(payload) as source:
            actual = _copy(source, None, reference.size_bytes)
        if actual != (reference.sha256, reference.size_bytes):
            raise CustodyError("The stored artifact failed its integrity check.")
        return payload

    def verify_attempt(self, attempt: AttemptRef) -> AttemptVerification:
        directory = self.attempt_path(attempt) / "artifacts"
        checked_path(directory, directory=True)
        references, incomplete = [], []
        for item in sorted(directory.iterdir()):
            checked_path(item, directory=True)
            try:
                reference = self._manifest_ref(attempt, item.name)
            except FileNotFoundError:
                canonical_id(item.name)
                incomplete.append(item.name)
                continue
            self.verify(reference)
            references.append(reference)
        return AttemptVerification(tuple(references), tuple(incomplete))

    def restore(self, reference: ArtifactRef, receiving_attempt: AttemptRef, name: str) -> Path:
        parts = relative_parts(name)
        directory = self.attempt_path(receiving_attempt) / "restored"
        checked_path(directory, directory=True)
        for part in parts[:-1]:
            directory /= part
            create_directory(directory)
        target = directory / parts[-1]
        with _open_regular(self._payload(reference)) as source:
            _write_atomic(target, source, max_bytes=reference.size_bytes,
                          expected=(reference.sha256, reference.size_bytes))
        return target

    def record_outcome(self, attempt: AttemptRef, status: str, details: Mapping) -> None:
        if status not in _OUTCOMES:
            raise CustodyError("An attempt outcome must be a supported terminal state.")
        write_record(self.attempt_path(attempt) / "outcome.json", {
            "schema": 1, **asdict(attempt), "finished_at": _now(), "status": status, "details": dict(details),
        })
