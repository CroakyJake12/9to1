"""Bounded Python/Studio wire types."""

from dataclasses import asdict
import json
import re

from custody.artifacts import ArtifactRef, AttemptRef, CustodyError, canonical_id

SCHEMA = 1
JOB = "cpu_self_check"
PURPOSE = "software_test_only"
REQUEST_BYTES = 32 * 1024
RESPONSE_BYTES = 32 * 1024
CHECKPOINT_BYTES = 100_000
SHA256 = re.compile(r"[a-f0-9]{64}")


class WorkerError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def _invalid():
    return WorkerError("invalid_request", "The Studio request does not match worker API schema 1.")


def _pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON member.")
        result[key] = value
    return result


def _reject_constant(_):
    raise ValueError("Non-finite JSON number.")


def decode(data: bytes) -> dict:
    value = json.loads(data.decode("utf-8"), object_pairs_hook=_pairs, parse_constant=_reject_constant)
    if type(value) is not dict:
        raise ValueError("JSON object required.")
    return value


def attempt_ref(value: dict) -> AttemptRef:
    if type(value) is not dict or value.keys() != {"run_id", "attempt_id"}:
        raise _invalid()
    try:
        return AttemptRef(canonical_id(value["run_id"]), canonical_id(value["attempt_id"]))
    except CustodyError as error:
        raise _invalid() from error


def checkpoint_ref(value: dict) -> ArtifactRef:
    fields = {"run_id", "attempt_id", "artifact_id", "sha256", "size_bytes"}
    if type(value) is not dict or value.keys() != fields:
        raise _invalid()
    attempt_ref({key: value[key] for key in ("run_id", "attempt_id")})
    try:
        canonical_id(value["artifact_id"])
    except CustodyError as error:
        raise _invalid() from error
    digest, size = value["sha256"], value["size_bytes"]
    if type(digest) is not str or not SHA256.fullmatch(digest) or type(size) is not int or not 1 <= size <= CHECKPOINT_BYTES:
        raise _invalid()
    return ArtifactRef(**value)


def read_request(stream) -> tuple[str, object]:
    data = stream.read(REQUEST_BYTES + 1)
    if len(data) > REQUEST_BYTES:
        raise WorkerError("request_too_large", "The Studio request exceeds the worker input limit.")
    try:
        request = decode(data)
    except (ValueError, RecursionError) as error:
        raise _invalid() from error
    schema = request.pop("schema", None)
    command = request.pop("command", None)
    if type(schema) is not int or schema != SCHEMA:
        raise _invalid()
    if command in ("status", "launch") and not request:
        return command, None
    if command in ("status", "pause"):
        return command, attempt_ref(request)
    if command == "list" and request.keys() <= {"limit", "after"}:
        limit = request.get("limit", 20)
        if type(limit) is not int or not 1 <= limit <= 50:
            raise _invalid()
        return command, (limit, attempt_ref(request["after"]) if "after" in request else None)
    if command == "resume" and request.keys() == {"checkpoint"}:
        return command, checkpoint_ref(request["checkpoint"])
    raise _invalid()


def attempt_record(reference: AttemptRef, status: str, *, reason=None, updates=None,
                   artifacts=(), resume=None) -> dict:
    return {**asdict(reference), "job": JOB, "purpose": PURPOSE, "status": status, "reason": reason,
            "completed_updates": updates, "artifacts": [asdict(ref) for ref in artifacts],
            "resume_checkpoint": asdict(resume) if resume is not None else None}


def encode_event(event, **fields) -> bytes:
    data = json.dumps({"schema": SCHEMA, "event": event, **fields}, separators=(",", ":"), allow_nan=False)
    return data.encode("utf-8")


def result_page(references, limit, resolve):
    attempts = []
    page = {"attempts": attempts, "next": None}
    for idx, reference in enumerate(references[:limit]):
        cursor = asdict(reference) if idx + 1 < len(references) else None
        attempts.append(resolve(reference))
        candidate = {"attempts": attempts, "next": cursor}
        if len(encode_event("result", result=candidate)) > RESPONSE_BYTES:
            attempts.pop()
            if not attempts:
                raise WorkerError("response_too_large", "A retained attempt exceeds the worker output limit.")
            break
        page = candidate
    return page
