#!/usr/bin/env python3
"""Local custody checks only; no product execution or acceptance decision."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

PENDING = "CUSTODY_INTEGRITY_OK_PENDING_MANUAL_REVIEW"
SPEC_IDS = {
    "development": "1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg",
    "postrelease": "1yZCIP-ogTPLBfqcnc5EFMsL2FO5DoLYBTbGGPTk7Aks",
}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def value(record, key):
    require(isinstance(record, dict), f"Expected object for {key}")
    result = record.get(key)
    require(isinstance(result, str) and bool(result.strip()), f"Missing {key}")
    lowered = result.strip().lower()
    require(lowered not in {"todo", "tbd", "unknown", "placeholder", "n/a", "pending"}
            and not lowered.startswith(("replace_", "replace with", "<")),
            f"Placeholder {key}")
    return result


def rows(record, key):
    require(isinstance(record, dict), f"Expected object for {key}")
    result = record.get(key)
    require(isinstance(result, list) and bool(result), f"Missing nonempty {key}")
    require(all(isinstance(item, dict) for item in result), f"Invalid {key}")
    return result


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def checked_file(record, base):
    path = Path(value(record, "path"))
    path = (base / path).resolve() if not path.is_absolute() else path.resolve()
    expected = value(record, "sha256")
    require(re.fullmatch(r"[0-9a-f]{64}", expected) and expected != "0" * 64,
            "Invalid sha256")
    require(path.is_file() and path.stat().st_size > 0, f"Missing file: {path}")
    require(digest(path) == expected, f"File hash mismatch: {path}")
    return path


def git(checkout, *args, binary=False, allow_absent=False):
    result = subprocess.run(["git", "-c", "core.fsmonitor=false", "-C", str(checkout), *args],
                            env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"},
                            capture_output=True, check=False)
    require(result.returncode == 0 or (allow_absent and result.returncode == 1),
            f"Git source check failed: {' '.join(args[:2])}")
    return result.stdout if binary else result.stdout.decode().strip()


def check_git_read_safety(checkout, seen=None):
    seen = set() if seen is None else seen
    require(checkout not in seen, "Cyclic source submodule checkout")
    seen.add(checkout)
    filters = git(checkout, "config", "--name-only", "--get-regexp",
                  r"^filter\..*\.(clean|process)$", allow_absent=True)
    require(not filters, "Configured external Git clean/process filters require a filter-free custody checkout")
    index_entries = git(checkout, "ls-files", "-v", "-z", binary=True).split(b"\0")
    require(all(not entry[:1].islower() and entry[:1] != b"S" for entry in index_entries if entry),
            "Source index flags hide checkout changes (assume-unchanged or skip-worktree)")
    entries = git(checkout, "ls-files", "--stage", "-z", binary=True).split(b"\0")
    for entry in entries:
        if not entry.startswith(b"160000 "):
            continue
        submodule = (checkout / os.fsdecode(entry.split(b"\t", 1)[1])).resolve()
        require(submodule.is_relative_to(checkout), "Submodule checkout outside source")
        if (submodule / ".git").exists():
            check_git_read_safety(submodule, seen)


def check_source(source):
    checkout = Path(value(source, "checkout"))
    require(checkout.is_absolute(), "Source checkout must be an absolute path")
    checkout = checkout.resolve()
    revision = value(source, "revision")
    require(re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", revision),
            "Source revision must be full exact hexadecimal commit")
    require(git(checkout, "cat-file", "-t", revision) == "commit", "Source is not a commit")
    require(git(checkout, "rev-parse", "HEAD") == revision, "Source checkout revision drift")
    require(git(checkout, "remote", "get-url", "origin") == value(source, "repository"),
            "Source repository mismatch")
    check_git_read_safety(checkout)
    require(not git(checkout, "status", "--porcelain", "--untracked-files=all", "--ignore-submodules=none"),
            "Source checkout is dirty")
    return checkout


def pinned_source_file(record, checkout, revision):
    path = checked_file(record, checkout)
    require(path.is_relative_to(checkout), "Source file outside checkout")
    relative = path.relative_to(checkout).as_posix()
    committed = git(checkout, "show", f"{revision}:{relative}", binary=True)
    require(hashlib.sha256(committed).hexdigest() == record["sha256"],
            f"Source file differs from commit: {relative}")


def citations(record, specs):
    authoritative = False
    for item in rows(record, "spec_citations"):
        identity = value(item, "spec_id")
        require(identity in specs, "Unknown scope specification")
        authoritative = authoritative or identity in SPEC_IDS
        value(item, "locator")
    require(authoritative, "Scope row lacks an authoritative specification citation")


def inspect(manifest_path):
    base = manifest_path.resolve().parent
    candidate = json.loads(manifest_path.read_text())
    require(isinstance(candidate, dict), "Manifest must be a JSON object")
    require(candidate.get("schema_version") == 1, "Unsupported schema_version")
    candidate_id = value(candidate, "candidate_id")
    value(candidate, "delivery_index_entry")
    source = candidate["source"]
    checkout = check_source(source)
    for item in rows(source, "source_files"):
        pinned_source_file(item, checkout, source["revision"])

    specs = {}
    for item in rows(candidate, "specs"):
        identity = value(item, "id")
        require(identity not in specs, "Duplicate specification")
        value(item, "revision")
        checked_file(item, base)
        revision_path = checked_file(item["revision_evidence"], base)
        revision_record = json.loads(revision_path.read_text())
        require(isinstance(revision_record, dict), "Specification revision evidence must be an object")
        require(revision_record.get("documentId") == value(item, "document_id")
                and revision_record.get("revisionId") == item["revision"],
                "Specification revision evidence mismatch")
        specs[identity] = value(item, "document_id")
    for identity, document_id in SPEC_IDS.items():
        require(specs.get(identity) == document_id, f"Missing authoritative {identity} specification")

    lock_roles = set()
    for item in rows(candidate, "locks"):
        lock_roles.add(value(item, "role"))
        pinned_source_file(item, checkout, source["revision"])
    require({"dependencies", "donors"} <= lock_roles, "Missing dependency/donor locks")

    scope = candidate["scope"]
    platforms = {}
    for item in rows(scope, "platform_matrix"):
        identity = value(item, "id")
        require(identity not in platforms, "Duplicate platform")
        require(item.get("required") in (True, False) and isinstance(item["required"], bool),
                "Platform required must be boolean")
        require(value(item, "kind") in {"native", "web"}, "Platform kind must be native or web")
        citations(item, specs)
        if not item["required"]:
            value(item, "exemption_reason")
        platforms[identity] = item
    required = {key for key, item in platforms.items() if item["required"]}
    require(bool(required), "No required platform")
    claims = {}
    for item in rows(scope, "claims"):
        identity = value(item, "id")
        require(identity not in claims, "Duplicate claim")
        value(item, "description")
        citations(item, specs)
        targets = item.get("platforms")
        require(isinstance(targets, list) and bool(targets)
                and all(isinstance(p, str) and p in required for p in targets),
                "Claim targets unknown or exempt platform")
        claims[identity] = set(targets)
    require(set().union(*claims.values()) == required, "Required platform lacks scoped claim")

    artifacts = {}
    for item in rows(candidate, "artifacts"):
        identity = value(item, "id")
        platform = value(item, "platform")
        require(identity not in artifacts, "Duplicate artifact")
        require(platform in required, "Artifact platform mismatch")
        require(item.get("source_revision") == source["revision"]
                and item.get("source_repository") == source["repository"],
                "Artifact source mismatch")
        checked_file(item, base)
        artifacts[identity] = item
    require({item["platform"] for item in artifacts.values()} == required,
            "Missing packaged artifact for required platform")

    smoke = candidate["smoke"]
    report_path = checked_file(smoke, base)
    report = json.loads(report_path.read_text())
    require(isinstance(report, dict), "Smoke report must be a JSON object")
    require(report.get("report_id") == value(smoke, "report_id"), "Smoke report identity mismatch")
    require(report.get("issuer") == value(smoke, "issuer"), "Smoke report issuer mismatch")
    require(report.get("issuer_role") == "Smoke Tester" and report.get("independent") is True,
            "Independent Smoke Tester declaration missing")
    require(report.get("candidate_id") == candidate_id, "Smoke candidate mismatch")
    require(report.get("source_revision") == source["revision"]
            and report.get("source_repository") == source["repository"], "Smoke source mismatch")
    tested = {}
    for item in rows(report, "tested_artifacts"):
        identity = value(item, "id")
        require(identity not in tested and identity in artifacts, "Smoke artifact identity mismatch")
        artifact = artifacts[identity]
        require(item.get("sha256") == artifact["sha256"], "Smoke artifact hash mismatch")
        require(item.get("platform") == artifact["platform"], "Smoke platform mismatch")
        tested[identity] = item
    require(set(tested) == set(artifacts), "Artifact absent from smoke report")
    environments = {}
    for item in rows(report, "environments"):
        platform = value(item, "platform")
        require(platform in required and platform not in environments, "Smoke environment platform mismatch")
        for key in ("host", "os", "os_version", "architecture", "runtime", "session_id"):
            value(item, key)
        environments[platform] = item
    require(set(environments) == required, "Missing smoke platform environment")
    covered = set()
    for item in rows(report, "main_workflows"):
        value(item, "id")
        platform = value(item, "platform")
        require(platform in required, "Workflow platform mismatch")
        require(item.get("executed") is True and item.get("result") == "PASS",
                "Smoke main workflow was not executed with reported PASS")
        targets = item.get("claim_ids")
        require(isinstance(targets, list) and bool(targets), "Missing workflow claim_ids")
        for identity in targets:
            require(isinstance(identity, str) and identity in claims
                    and platform in claims[identity], "Workflow scope mismatch")
            covered.add((identity, platform))
        checked_file(item["evidence"], report_path.parent)
    require(covered == {(identity, p) for identity, targets in claims.items() for p in targets},
            "Missing main workflow evidence for scoped claim/platform")

    for platform in required:
        if platforms[platform]["kind"] != "web":
            continue
        bundles = [item for item in artifacts.values() if item["platform"] == platform]
        require(len(bundles) == 1, "Web platform requires one exact deployed bundle artifact")
        deployment = platforms[platform]["deployment"]
        for key in ("deployment_id", "url", "access_boundary", "session_id"):
            value(deployment, key)
        require(deployment.get("immutable") is True, "Immutable web deployment declaration missing")
        require(deployment["deployment_id"].lower() not in {"latest", "current", "main", "production"},
                "Mutable web deployment alias")
        require(deployment.get("bundle_sha256") == bundles[0]["sha256"], "Web bundle hash mismatch")
        require(deployment["session_id"] == environments[platform]["session_id"], "Web session mismatch")
        access_path = checked_file(deployment["access_evidence"], base)
        access = json.loads(access_path.read_text())
        require(isinstance(access, dict), "Web access evidence must be a JSON object")
        for key in ("deployment_id", "url", "bundle_sha256", "access_boundary", "session_id"):
            require(access.get(key) == deployment[key], f"Web access evidence {key} mismatch")
        require(access.get("source_revision") == source["revision"]
                and access.get("source_repository") == source["repository"], "Web access source mismatch")
        require(access.get("executed") is True and access.get("boundary_reached") is True,
                "Web boundary/session access was not observed")
        checked_file(access["observation"], access_path.parent)

    check_source(source)
    return {"status": PENDING, "candidate_id": candidate_id,
            "manual_review_required": True,
            "limitations": "Authenticate issuer, scope and reported smoke assertions independently; "
                           "local hashes and declarations do not prove runtime behavior or acceptance."}


def validate(manifest_path):
    try:
        return inspect(Path(manifest_path))
    except (ValueError, KeyError, TypeError, OSError) as error:
        return {"status": "CUSTODY_INTEGRITY_REJECTED", "errors": [str(error)],
                "manual_review_required": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    result = validate(parser.parse_args().manifest)
    print(json.dumps(result, indent=2))
    return 0 if result["status"] == PENDING else 1


if __name__ == "__main__":
    raise SystemExit(main())
