"""Write-once worker artifacts and artifact-bound operator verification receipts."""

import hashlib
import json
import os
import tempfile
from pathlib import Path

from state import Fault


def record_artifact(state_dir, swarm_id, task):
    digest = hashlib.sha256(task["deliverable"].encode("utf-8")).hexdigest()
    value = {
        "schema_version": 1,
        "swarm_id": swarm_id,
        "task_id": task["id"],
        "role": task["role"],
        "acceptance": task["acceptance"],
        "deliverable": task["deliverable"],
        "artifact_sha256": digest,
    }
    root = Path(state_dir) / "swarm-artifacts"
    path = root / swarm_id
    for directory in (root, path):
        directory.mkdir(mode=0o700, exist_ok=True)
        if (
            directory.is_symlink()
            or not directory.is_dir()
            or directory.stat().st_uid != os.getuid()
        ):
            raise Fault(409, "Invalid swarm artifact directory")
    target = path / (task["id"] + ".json")
    data = (json.dumps(value, sort_keys=True, ensure_ascii=False) + "\n").encode()
    with tempfile.NamedTemporaryFile(dir=path, delete=False) as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
        temporary = Path(f.name)
    try:
        try:
            os.link(temporary, target)
        except FileExistsError:
            if (
                target.is_symlink()
                or target.stat().st_size != len(data)
                or target.read_bytes() != data
            ):
                raise Fault(
                    409,
                    "An existing immutable worker artifact differs; operator review required",
                )
    finally:
        temporary.unlink(missing_ok=True)
    task["artifact_sha256"] = digest
    task["artifact_path"] = str(target)


def verified_receipt(state_dir, swarm_id, task, recipe, receipts):
    if not recipe:
        return None
    for row in reversed(receipts):
        if (
            row.get("recipe") != recipe
            or row.get("exit_code") != 0
            or not row.get("log")
        ):
            continue
        f = Path(row["log"])
        if (
            f.is_symlink()
            or f.parent.resolve() != (Path(state_dir) / "logs").resolve()
            or not f.is_file()
        ):
            continue
        with f.open("rb") as stream:
            stream.seek(max(0, f.stat().st_size - 32768))
            lines = stream.read(32768).decode("utf-8", errors="replace").splitlines()
        for line in reversed(lines):
            try:
                value = json.loads(line)
            except ValueError:
                continue
            if not isinstance(value, dict) or value.get("verified") is not True:
                continue
            if (
                value.get("swarm_id") != swarm_id
                or value.get("task_id") != task["id"]
                or value.get("artifact_sha256") != task.get("artifact_sha256")
            ):
                continue
            evidence = value.get("evidence")
            if isinstance(evidence, str) and 0 < len(evidence.strip()) <= 4096:
                return {
                    "recipe": recipe,
                    "artifact_sha256": value["artifact_sha256"],
                    "evidence": evidence,
                    "log": str(f),
                }
    return None
