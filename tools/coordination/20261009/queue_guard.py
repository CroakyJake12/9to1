"""Read-only queue checks. Structural evidence checks are NOT product verification.

Python 3.11+. Never writes Git refs, source files, leases, or external resources.
A reviewer must authenticate receipts and inspect the actual product separately.
"""
from __future__ import annotations
import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

STATUSES = {"WORKING", "STALLED", "BLOCKED", "CHECKPOINT", "COMPLETED", "VERIFIED"}
GATES = ("implementation", "integration", "build", "package", "smoke", "acceptance")
RESULTS = {"NOT_RUN", "UNKNOWN", "PASS", "FAIL", "BLOCKED"}
SHA = re.compile(r"[0-9a-f]{64}\Z")


def canonical_path(value: str) -> str:
    """Reject ambiguous paths; case-fold for a conservative cross-platform lease."""
    if not isinstance(value, str) or not value or value != value.strip():
        raise ValueError("empty or whitespace-padded path")
    if "\\" in value or any(c in value for c in ":*?[]") or any(ord(c) < 32 for c in value):
        raise ValueError("backslash, drive, wildcard or null in path")
    value = value.removesuffix("/")
    parts = value.split("/")
    if any(not p or p in {".", ".."} or p.endswith((".", " ")) for p in parts):
        raise ValueError("absolute, traversal or ambiguous path")
    return "/".join(parts).casefold()


def overlap(a: str, b: str) -> bool:
    return a == b or a.startswith(b + "/") or b.startswith(a + "/")


def validate(queue: dict[str, Any]) -> list[str]:
    """Return all detected errors; a valid ledger does not mean a valid release."""
    errors: list[str] = []
    workers = queue.get("workers", {})
    tasks = queue.get("tasks", [])
    if not isinstance(workers, dict) or not isinstance(tasks, list):
        return ["workers must be an object and tasks an array"]
    ids: dict[str, dict[str, Any]] = {}
    leases: list[tuple[str, str]] = []
    for task in tasks:
        if not isinstance(task, dict) or not isinstance(task.get("id"), str):
            errors.append("task requires a string id")
            continue
        tid = task["id"]
        if tid in ids:
            errors.append(f"{tid}: duplicate task id")
        ids[tid] = task
        owner = task.get("owner")
        if not isinstance(owner, str) or owner not in workers:
            errors.append(f"{tid}: unknown owner")
        status = task.get("status")
        if status is not None and status not in STATUSES:
            errors.append(f"{tid}: invalid execution status")
        if not task.get("source_refs") or not task.get("acceptance"):
            errors.append(f"{tid}: missing source references or acceptance criteria")
        if not task.get("next_checkpoint") or "effort" not in task:
            errors.append(f"{tid}: missing checkpoint/effort (unknown is allowed)")
        claim = task.get("claim", "PROPOSED")
        if claim not in {"PROPOSED", "ACKNOWLEDGED", "RELEASED"}:
            errors.append(f"{tid}: invalid claim state")
        paths = task.get("write_paths", [])
        if not isinstance(paths, list):
            errors.append(f"{tid}: write_paths must be a list")
            paths = []
        if paths and claim != "ACKNOWLEDGED":
            errors.append(f"{tid}: write lease without acknowledged claim")
        if claim == "ACKNOWLEDGED" and (not task.get("ack_url") or not task.get("branch")):
            errors.append(f"{tid}: acknowledged claim needs receipt and branch")
        for raw in paths:
            try:
                p = canonical_path(raw)
                for other, op in leases:
                    if other != tid and overlap(p, op):
                        errors.append(f"{tid}: overlapping write lease with {other}: {p}")
                leases.append((tid, p))
            except ValueError as exc:
                errors.append(f"{tid}: unsafe path: {exc}")
        gates = task.get("gates", {})
        if not isinstance(gates, dict):
            errors.append(f"{tid}: gates must be an object")
            gates = {}
        candidate = task.get("candidate_manifest_sha256")
        receipts = task.get("receipts", {})
        if not isinstance(receipts, dict):
            errors.append(f"{tid}: receipts must be an object")
            receipts = {}
        for gate in GATES:
            result = gates.get(gate, "NOT_RUN")
            if result not in RESULTS:
                errors.append(f"{tid}: invalid {gate} result")
            if result != "PASS":
                continue
            receipt = receipts.get(gate, {})
            if not isinstance(receipt, dict):
                receipt = {}
            if not isinstance(candidate, str) or not SHA.fullmatch(candidate):
                errors.append(f"{tid}: PASS requires exact candidate manifest SHA256")
            if receipt.get("candidate_manifest_sha256") != candidate or not receipt.get("url"):
                errors.append(f"{tid}: {gate} receipt missing or for another candidate")
            if gate in {"package", "smoke", "acceptance"}:
                package = receipt.get("package_sha256", "")
                if not isinstance(package, str) or not SHA.fullmatch(package):
                    errors.append(f"{tid}: {gate} requires a real artifact hash")
                expected = receipts.get("package", {})
                if not isinstance(expected, dict) or expected.get("package_sha256") != package:
                    errors.append(f"{tid}: {gate} package does not match packaged artifact")
            if gate in {"smoke", "acceptance"}:
                reviewer = receipt.get("reviewer")
                if not isinstance(reviewer, str) or reviewer not in workers or reviewer == owner:
                    errors.append(f"{tid}: {gate} requires an independent registered reviewer")
                if receipt.get("kind") != "PRODUCT_RUNTIME":
                    errors.append(f"{tid}: {gate} cannot use fixture or source-only evidence")
                if any(gates.get(prereq) != "PASS" for prereq in ("integration", "build", "package")):
                    errors.append(f"{tid}: {gate} lacks integrated/build/package prerequisites")
            if gate == "acceptance" and (gates.get("smoke") != "PASS" or receipt.get("full_scope") is not True):
                errors.append(f"{tid}: full acceptance requires smoke and full-scope receipt")
        if status in {"COMPLETED", "VERIFIED"} and gates.get("implementation") != "PASS":
            errors.append(f"{tid}: completed task lacks completed implementation")
        if status == "VERIFIED" and any(gates.get(g) != "PASS" for g in GATES):
            errors.append(f"{tid}: VERIFIED requires all separate gates")
    # Implementation dependencies form a DAG; acceptance dependencies are separate.
    graph: dict[str, list[str]] = {}
    for tid, task in ids.items():
        deps = task.get("depends_on", [])
        if not isinstance(deps, list) or any(not isinstance(d, str) for d in deps):
            errors.append(f"{tid}: dependencies must be string IDs")
            deps = []
        for dep in deps:
            if dep not in ids:
                errors.append(f"{tid}: unknown dependency {dep}")
        graph[tid] = [d for d in deps if d in ids]
    remaining = {k: set(v) for k, v in graph.items()}
    while remaining:
        ready = {k for k, v in remaining.items() if not v}
        if not ready:
            errors.append("dependency cycle: " + ", ".join(sorted(remaining)))
            break
        remaining = {k: v - ready for k, v in remaining.items() if k not in ready}
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("queue", type=Path)
    args = parser.parse_args()
    try:
        queue = json.loads(args.queue.read_text(encoding="utf-8"))
        if not isinstance(queue, dict):
            raise ValueError("queue must be an object")
        errors = validate(queue)
    except (OSError, ValueError, TypeError) as exc:
        print(f"INVALID INPUT: {exc}", file=sys.stderr)
        return 2
    for error in errors:
        print(error, file=sys.stderr)
    if errors:
        return 1
    print(f"STRUCTURALLY VALID: {len(queue.get('tasks', []))} tasks. Product acceptance is NOT implied.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
