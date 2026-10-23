#!/usr/bin/env python3
"""Local evidence/coordination adjunct. Does not launch workers or replace the PC ledger."""
import argparse
import contextlib
import datetime as dt
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import signal
import stat
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

SCHEMA = 1
STATUSES = {"COMPLETED", "VERIFIED", "CHECKPOINT", "WORKING", "STALLED", "BLOCKED"}
MANDATORY = (
    "cui-conformance", "donor-licences", "visual-category", "packaged-workflows",
    "privacy-security-accessibility", "platform-performance", "cross-app", "independent-acceptance",
)
QUALITY = ("Usability", "Product fit", "Functionality", "Integration", "Separation and authenticity", "User fit", "Refinement")


class Refusal(Exception):
    pass


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def encoded(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n").encode()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(Path(path).read_bytes())


def atomic(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(prefix="." + path.name + ".", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(encoded(value)); stream.flush(); os.fsync(stream.fileno())
        os.replace(temp, path)
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try: os.fsync(parent)
        finally: os.close(parent)
    finally:
        if os.path.exists(temp): os.unlink(temp)


def initialize(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with Path(str(path) + ".lock").open("a+b") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if path.exists(): raise Refusal("Existing checkpoint preserved; reread/reconcile instead of initialising over it")
        atomic(path, value)


def git(repo, *args):
    return subprocess.check_output(["git", "--no-optional-locks", "-C", str(repo), *args], stderr=subprocess.PIPE)


def safe_relative(name):
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or not path.parts or not name or "\\" in name or str(path) != name:
        raise Refusal("Expected a repository-relative POSIX path: " + name)
    return path


def file_identity(path):
    path = Path(path)
    if path.is_symlink():
        return {"kind": "symlink", "target": os.readlink(path)}
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK | getattr(os, "O_NOATIME", 0) | os.O_NOFOLLOW)
    except PermissionError:
        fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    with os.fdopen(fd, "rb") as stream:
        st = os.fstat(stream.fileno())
        if not stat.S_ISREG(st.st_mode): raise Refusal("Nonregular evidence/source input: " + str(path))
        h = hashlib.file_digest(stream, "sha256").hexdigest()
        after = os.fstat(stream.fileno())
        if (st.st_dev, st.st_ino, st.st_size, st.st_mtime_ns, st.st_ctime_ns) != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns):
            raise Refusal("Input changed while hashing: " + str(path))
    return {"kind": "file", "sha256": h, "bytes": st.st_size, "mode": st.st_mode & 0o777}


def tree(repo):
    repo = Path(repo).resolve()
    names = git(repo, "ls-files", "-z", "--cached", "--others", "--exclude-standard").decode().split("\0")
    staged = {}
    for entry in git(repo, "ls-files", "--stage", "-z").decode().split("\0"):
        if not entry: continue
        metadata, name = entry.split("\t", 1)
        mode, revision, stage = metadata.split()
        if stage != "0": raise Refusal("Unresolved integration conflict: " + name)
        staged[name] = (mode, revision)
    skipped = {line[2:] for line in git(repo, "ls-files", "-t", "-z").decode().split("\0") if line.startswith("S ")}
    result = {}
    for name in sorted(set(filter(None, names))):
        safe_relative(name)
        path = repo / name
        # Sparse checkout and gitlinks are not a complete candidate. Never hash their omission as success.
        if not path.exists() and not path.is_symlink():
            if name in skipped: raise Refusal("Missing sparse-checkout input: " + name)
            if staged.get(name, (None,))[0] == "160000": raise Refusal("Missing pinned donor worktree: " + name)
            result[name] = {"kind": "deleted"}
            continue
        if path.is_dir() and not path.is_symlink():
            mode, revision = staged.get(name, (None, None))
            if mode != "160000": raise Refusal("Untracked directory is not a file input: " + name)
            actual_root = Path(git(path, "rev-parse", "--show-toplevel").decode().strip()).resolve()
            actual_revision = git(path, "rev-parse", "HEAD").decode().strip()
            if actual_root != path or actual_revision != revision: raise Refusal("Uninitialised/mismatched pinned donor: " + name)
            result[name] = {"kind": "gitlink", "revision": revision}
            for child, identity in tree(path).items(): result[name + "/" + child] = identity
            continue
        result[name] = file_identity(path)
    return result


def manifest(repo, files=None):
    repo = Path(repo).resolve()
    if files is None:
        files = tree(repo)
    clean = not git(repo, "status", "--porcelain=v1", "--untracked-files=normal", "--ignore-submodules=none").strip()
    payload = {"schema": SCHEMA, "source": files, "revision": git(repo, "rev-parse", "HEAD").decode().strip(), "worktree_clean": clean}
    return {**payload, "identity": digest(encoded(payload))}


def snapshot(repo, destination, accepted=None, required=None):
    candidate = manifest(repo)
    fixes = {record["id"]: record for record in (accepted or [])}
    missing = set(required or {}) - set(fixes)
    if missing:
        raise Refusal("Previously accepted fixes missing: " + ", ".join(sorted(missing)))
    for key, fix in fixes.items():
        if not fix.get("reviewer") or not fix.get("evidence") or not fix.get("files"):
            raise Refusal("Accepted fix needs independent reviewer/evidence/content identities: " + key)
        old = (required or {}).get(key)
        if old and old != fix and fix.get("replaces") != digest(encoded(old)):
            raise Refusal("Changed accepted fix needs explicit reviewed predecessor: " + key)
        for path, identity in fix["files"].items():
            if candidate["source"].get(path) != identity:
                raise Refusal("Accepted fix content changed/disappeared: " + key + ": " + path)
    candidate["accepted_fixes"] = fixes
    # Bind fix lineage too; never overwrite an existing snapshot/RC.
    candidate["identity"] = digest(encoded({"schema": SCHEMA, "source": candidate["source"], "revision": candidate["revision"], "worktree_clean": candidate["worktree_clean"], "accepted_fixes": candidate["accepted_fixes"]}))
    path = Path(destination)
    path.parent.mkdir(parents=True, exist_ok=True)
    try:
        with path.open("xb") as stream: stream.write(encoded(candidate)); stream.flush(); os.fsync(stream.fileno())
    except FileExistsError:
        if read(path) != candidate: raise Refusal("Immutable candidate already exists with different content")
    return candidate


def validate_candidate(candidate, repo=None):
    payload = {"schema": SCHEMA, "source": candidate["source"], "revision": candidate["revision"], "worktree_clean": candidate["worktree_clean"], "accepted_fixes": candidate.get("accepted_fixes", {})}
    if candidate.get("identity") != digest(encoded(payload)):
        raise Refusal("Candidate/fix-lineage identity mismatch")
    if repo:
        # Includes all tracked + ordinary untracked inputs: adding an unlisted input invalidates reuse.
        current = manifest(repo)
        if current["source"] != candidate["source"] or current["revision"] != candidate["revision"] or current["worktree_clean"] != candidate["worktree_clean"]:
            raise Refusal("Candidate source/revision changed (including new/deleted inputs)")


def initial_state(repo, root_checkpoint):
    checkpoint = Path(root_checkpoint).resolve()
    if not checkpoint.is_file(): raise Refusal("Existing ROOT-LIVE checkpoint required; do not initialise replacement programme state")
    return {"schema": SCHEMA, "generation": 0, "updated_at": now(), "projection_of": root_checkpoint,
            "repository": str(Path(repo).resolve()), "observed_revision": git(repo, "rev-parse", "HEAD").decode().strip(),
            "verified_revision": None, "development_candidates": {}, "execution_custody": {}, "release_candidate": None,
            "workers": {}, "tasks": {}, "pending_changes": {}, "blockers": {}, "evidence": {}, "artifacts": {},
            "recovery_assets": {"root-checkpoint": {"path": str(checkpoint), **file_identity(checkpoint)}},
            "outstanding_acceptance": list(MANDATORY), "next_actions": [],
            "authority": "PC milestone ledger remains authoritative; this is a cloud ROOT-LIVE checkpoint projection, not a worker supervisor."}


def validate_state(state):
    if state.get("schema") != SCHEMA: raise Refusal("Unsupported ledger schema")
    for field in ("workers", "tasks", "pending_changes", "blockers", "evidence", "artifacts", "recovery_assets", "development_candidates"):
        if not isinstance(state.get(field), dict): raise Refusal("Ledger field must be a named object: " + field)
    if not isinstance(state.get("execution_custody", {}), dict): raise Refusal("Execution custody must be a named object")
    for task in state.get("tasks", {}).values():
        if task.get("status") not in STATUSES: raise Refusal("Invalid task status")
        if task["status"] == "VERIFIED":
            refs = task.get("evidence", [])
            if not refs or any(x not in state["evidence"] for x in refs): raise Refusal("VERIFIED task needs evidence references")
            if any(state["evidence"][x].get("result") != "PASS" for x in refs): raise Refusal("VERIFIED task has nonpassing evidence")
            source = task.get("source_revision")
            if not source or task.get("verified_revision") != source: raise Refusal("VERIFIED task requires the exact source/verified revision")
            for ref in refs:
                evidence = state["evidence"][ref]
                if evidence.get("source_revision") != source or (task.get("candidate") and evidence.get("candidate") != task["candidate"]):
                    raise Refusal("VERIFIED task evidence belongs to a different source/candidate")
                if (not evidence.get("reviewer") or not evidence.get("implementer") or evidence["reviewer"] == evidence["implementer"]
                        or evidence["reviewer"] in ("NOT RUN", "PENDING", "UNASSIGNED") or not evidence.get("steps")):
                    raise Refusal("VERIFIED task requires an independent reviewer and executed steps")
                artifact = evidence.get("artifact", {})
                try:
                    if artifact.get("kind") != "file" or file_identity(artifact["path"]) != {k: v for k, v in artifact.items() if k != "path"}:
                        raise Refusal("VERIFIED task evidence artifact changed or is not a retained regular file")
                except (OSError, KeyError) as error: raise Refusal("VERIFIED task evidence artifact unavailable") from error
            if task.get("stage") == "release" and not release_ready(state)["ready"]: raise Refusal("Release task still has mandatory outstanding gates")
    if state.get("verified_revision") is not None:
        matching = [t for t in state.get("tasks", {}).values() if t.get("status") == "VERIFIED" and t.get("verified_revision") == state["verified_revision"]]
        if not matching: raise Refusal("Verified revision needs a VERIFIED task with passing evidence; observed HEAD alone is insufficient")


@contextlib.contextmanager
def transaction(path, expected):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with Path(str(path) + ".lock").open("a+b") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        value = read(path)
        if value["generation"] != expected: raise Refusal("Stale generation; reread/reconcile rather than overwriting")
        yield value
        validate_state(value)
        value["generation"] += 1; value["updated_at"] = now()
        atomic(path, value)


def scope_overlap(a, b):
    def root(x):
        if x.endswith("/**"):
            prefix = x[:-3]
            safe_relative(prefix)
            if any(c in prefix for c in "*?["): raise Refusal("Only a trailing /** wildcard is supported")
            return prefix, True
        safe_relative(x)
        if any(c in x for c in "*?["): raise Refusal("Scopes permit only exact paths or a trailing /**")
        return x.rstrip("/"), False
    a, at = root(a); b, bt = root(b)
    return a == b or (at and b.startswith(a + "/")) or (bt and a.startswith(b + "/"))


def claim(state, worker, paths, role):
    if not paths: raise Refusal("An explicit nonempty ownership scope is required")
    for path in paths: scope_overlap(path, path)
    for name, other in state["workers"].items():
        if name == worker or other.get("status") != "WORKING": continue
        if any(scope_overlap(a, b) for a in paths for b in other["paths"]):
            raise Refusal("Ownership overlaps active worker: " + name)
    state["workers"][worker] = {"role": role, "paths": paths, "status": "WORKING", "observed_at": now(),
                                "liveness": "UNCONFIRMED: reconcile with supported live session API after restart"}


def pin_rc(state, candidate, path):
    validate_candidate(candidate)
    if not candidate.get("worktree_clean"): raise Refusal("Release candidate requires committed clean source; retain dirty snapshots as development checkpoints")
    if read(path) != candidate: raise Refusal("RC path does not contain the reviewed candidate")
    if state.get("release_candidate"):
        raise Refusal("RC already pinned; create a traceable successor after retaining the prior candidate")
    state["release_candidate"] = {"identity": candidate["identity"], "path": str(Path(path).resolve()),
                                   "manifest": file_identity(path), "revision": candidate["revision"], "accepted_fixes": candidate["accepted_fixes"], "pinned_at": now()}


def advance_rc(state, candidate, path, patch):
    old = state.get("release_candidate")
    if not old: raise Refusal("No existing RC to amend")
    previous = read(old["path"])
    validate_candidate(previous); validate_candidate(candidate)
    if previous["identity"] != old["identity"] or file_identity(old["path"]) != old["manifest"]:
        raise Refusal("Existing RC manifest changed")
    if not patch.get("reviewer") or patch.get("reviewer") == patch.get("implementer") or not patch.get("reason"):
        raise Refusal("RC fix requires an independent reviewer and reason")
    if patch.get("source_candidate") != old["identity"] or patch.get("target_candidate") != candidate["identity"]:
        raise Refusal("RC patch must bind the exact previous and successor candidates")
    changed = {p for p in set(previous["source"]) | set(candidate["source"]) if previous["source"].get(p) != candidate["source"].get(p)}
    if not changed or changed != set(patch.get("files", [])): raise Refusal("RC patch scope differs from actual source changes")
    if not patch.get("id") or not patch.get("backport_targets"): raise Refusal("RC fix needs an ID and active-development backport targets")
    if patch["id"] in state["pending_changes"]: raise Refusal("RC patch ID already retained; use a new traceable successor ID")
    if set(previous["accepted_fixes"]) - set(candidate["accepted_fixes"]): raise Refusal("Successor drops accepted fixes")
    for key, fix in previous["accepted_fixes"].items():
        replacement = candidate["accepted_fixes"][key]
        if replacement != fix and replacement.get("replaces") != digest(encoded(fix)):
            raise Refusal("Successor silently rewrites accepted fix: " + key)
    state.setdefault("release_history", []).append({**old, "superseded_by": candidate["identity"], "reviewed_patch": patch, "at": now()})
    state["release_candidate"] = None
    pin_rc(state, candidate, path)
    state["pending_changes"][patch["id"]] = {"kind": "rc-backport", "status": "CHECKPOINT", "patch": patch,
                                              "expected_files": {p: candidate["source"].get(p) for p in changed}, "verified_targets": {}}


def verify_backport(state, patch_id, target, repo):
    pending = state["pending_changes"][patch_id]
    if target not in pending["patch"]["backport_targets"]: raise Refusal("Target is not declared in the reviewed patch")
    check_backport_files(pending, repo)
    pending["verified_targets"][target] = {"at": now(), "revision": git(repo, "rev-parse", "HEAD").decode().strip(), "repo": str(Path(repo).resolve())}
    if set(pending["patch"]["backport_targets"]) <= set(pending["verified_targets"]): pending["status"] = "VERIFIED"


def check_backport_files(pending, repo):
    for name, identity in pending["expected_files"].items():
        safe_relative(name)
        path = Path(repo) / name
        if identity is None or identity.get("kind") == "deleted":
            if path.exists() or path.is_symlink(): raise Refusal("Deleted RC input still exists in backport: " + name)
        elif identity.get("kind") == "gitlink":
            if git(path, "rev-parse", "HEAD").decode().strip() != identity["revision"]: raise Refusal("Backport donor pin differs: " + name)
        elif file_identity(path) != identity: raise Refusal("Backport file differs from reviewed RC fix: " + name)


def backports_current(change):
    targets = set(change.get("patch", {}).get("backport_targets", []))
    verified = change.get("verified_targets", {})
    if change.get("status") != "VERIFIED" or not targets or not targets <= set(verified) or not change.get("expected_files"): return False
    try:
        for target in targets:
            if not verified[target].get("revision"): return False
            check_backport_files(change, verified[target]["repo"])
    except (OSError, KeyError, Refusal, subprocess.CalledProcessError): return False
    return True


def recovery_reference(record, content=False):
    required = {"path", "kind", "bytes", "mode", "sha256"}
    if not isinstance(record, dict) or set(record) != required:
        raise Refusal("Recovery asset requires exactly path/kind/bytes/mode/sha256")
    if not isinstance(record["path"], str) or not record["path"] or "\0" in record["path"] or not Path(record["path"]).is_absolute():
        raise Refusal("Recovery asset path must be an absolute retained file path")
    if (record["kind"] != "file" or type(record["bytes"]) is not int or record["bytes"] < 0
            or type(record["mode"]) is not int or not 0 <= record["mode"] <= 0o777
            or not isinstance(record["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", record["sha256"])):
        raise Refusal("Recovery asset has invalid regular-file identity fields")
    if content:
        try:
            if file_identity(record["path"]) != {k: v for k, v in record.items() if k != "path"}:
                raise Refusal("Recovery asset content identity changed")
        except OSError as error: raise Refusal("Recovery asset unavailable: " + str(error)) from error


def recovery_locator(record):
    if (not isinstance(record, dict) or not isinstance(record.get("path"), str)
            or not record["path"] or "\0" in record["path"] or not Path(record["path"]).is_absolute()
            or not isinstance(record.get("sha256"), str) or not re.fullmatch(r"[0-9a-f]{64}", record["sha256"])):
        raise Refusal("Newest ROOT-LIVE observation requires an absolute path and SHA256 record")


def checkpoint_recovery_preflight(state, patch):
    if not ({"recovery_assets", "last_root_checkpoint_observed"} & patch.keys()): return
    incoming = patch.get("recovery_assets", {})
    if not isinstance(incoming, dict): raise Refusal("Recovery assets must be named file identity records")
    for asset in incoming.values(): recovery_reference(asset, content=True)
    assets = {**state["recovery_assets"], **incoming}
    observed = state.get("last_root_checkpoint_observed")
    if "last_root_checkpoint_observed" in patch:
        recovery_locator(patch["last_root_checkpoint_observed"])
        observed = {**(observed if isinstance(observed, dict) else {}), **patch["last_root_checkpoint_observed"]}
    # Both may be absent in a legacy projection. An unrelated retained asset update
    # does not rehash historical files, while a new current pair must agree atomically.
    if "last_root_checkpoint_observed" in state or "last_root_checkpoint_observed" in patch or "latest-root-checkpoint" in assets:
        recovery_locator(observed)
        latest = assets.get("latest-root-checkpoint")
        recovery_reference(latest, content="last_root_checkpoint_observed" in patch and "latest-root-checkpoint" not in incoming)
        if observed["path"] != latest["path"] or observed["sha256"] != latest["sha256"]:
            raise Refusal("Newest ROOT-LIVE observation and retained asset must match in the same checkpoint")


def apply_checkpoint(state, patch):
    forbidden = set(patch) & {"schema", "generation", "release_candidate", "release_history", "workers", "authority", "projection_of"}
    if forbidden: raise Refusal("Protected checkpoint fields: " + ", ".join(sorted(forbidden)))
    if "pending_changes" in patch:
        incoming = patch["pending_changes"]
        if not isinstance(incoming, dict): raise Refusal("Pending changes must be named records")
        for key, value in incoming.items():
            if not isinstance(value, dict): raise Refusal("Pending change must be a named record: " + key)
            if state["pending_changes"].get(key, {}).get("kind") == "rc-backport" or value.get("kind") == "rc-backport":
                raise Refusal("RC backport records require advance-rc/verify-backport, never a generic checkpoint")
    checkpoint_recovery_preflight(state, patch)
    checkpoint_execution_preflight(state, patch)
    for key, value in patch.items():
        if isinstance(value, dict) and isinstance(state.get(key), dict): state[key].update(value)
        else: state[key] = value


def trx(path):
    root = ET.parse(path).getroot()
    counters = root.find(".//{*}Counters")
    if counters is None: raise Refusal("TRX missing actual test counters")
    total, executed, passed, failed = (int(counters.get(x, "0")) for x in ("total", "executed", "passed", "failed"))
    if min(total, executed, passed, failed) < 0 or passed + failed > executed or executed > total:
        raise Refusal("Invalid TRX counters")
    results = root.findall(".//{*}UnitTestResult")
    result_passed = sum(r.get("outcome") == "Passed" for r in results)
    result_failed = sum(r.get("outcome") == "Failed" for r in results)
    consistent = len(results) == total and result_passed == passed and result_failed == failed
    fatal = any(int(counters.get(key, "0")) for key in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "inProgress", "pending"))
    return {"total": total, "executed": executed, "passed": passed, "failed": failed,
            "result_rows_match_counters": consistent,
            "result": "PASS" if consistent and not fatal and total > 0 and total == executed == passed and failed == 0 else "FAIL"}


def environment_inputs(path):
    record = read(path)
    if not record.get("reviewer") or not record.get("files"):
        raise Refusal("Reusable environment needs reviewed toolchain/dependency closure, not only a version label")
    for name, identity in record["files"].items():
        if file_identity(name) != identity: raise Refusal("Toolchain/dependency input changed: " + name)
    return record


def provider_allowed(command):
    retired = re.compile(r"(^|[/=])(mercury(?:-[0-9]+)?|inception(?:labs)?)([/=]|$)", re.I)
    for index, arg in enumerate(command):
        model_option = index and command[index - 1] in ("--model", "-m", "--provider")
        if ("api.inceptionlabs.ai" in arg.lower() or "run-isolated-mercury-worker" in Path(arg).name.lower()
                or (model_option and retired.search(arg)) or (arg.startswith(("--model=", "--provider=")) and retired.search(arg))):
            raise Refusal("Mercury/Inception is revoked. Use the verified Beam or Codex mechanism.")


def process_identity(pid):
    try:
        fields = Path(f"/proc/{pid}/stat").read_text().rpartition(") ")[2].split()
        return {"pid": pid, "pgid": int(fields[2]), "session": int(fields[3]), "start_ticks": int(fields[19]), "state": fields[0]}
    except (OSError, ValueError, IndexError): return None


def stop_owned(process, identity):
    def still_owned():
        current = process_identity(process.pid)
        return (process.poll() is None and current and identity and current["start_ticks"] == identity["start_ticks"]
                and current["pgid"] == current["session"] == process.pid)
    if not still_owned(): raise Refusal("Process ownership changed/ended; reconcile group before sending signals")
    os.killpg(process.pid, signal.SIGTERM)
    try: process.wait(timeout=2)
    except subprocess.TimeoutExpired:
        if not still_owned(): raise Refusal("Cannot prove ownership for escalation")
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=2)


def remaining_group(identity):
    if not identity: return []
    members = []
    for path in Path("/proc").iterdir():
        if not path.name.isdigit(): continue
        current = process_identity(int(path.name))
        if (current and current["pgid"] == current["session"] == identity["pid"]
                and current["start_ticks"] >= identity["start_ticks"] and current["state"] != "Z"):
            members.append(current)
    return members


def run_check(repo, candidate, check_id, layer, command, output, trx_path=None, kind="test", environment=None, timeout_seconds=300):
    if layer not in (1, 2, 3, 4): raise Refusal("Unknown verification layer")
    validate_candidate(candidate, repo)
    provider_allowed(command)
    environment_record = environment_inputs(environment) if environment else None
    if not 0 < timeout_seconds <= 86400: raise Refusal("Explicit bounded timeout required (0 < seconds <= 86400)")
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    log = output / "process.log"
    started = time.monotonic()
    receipt = {"schema": SCHEMA, "check_id": check_id, "candidate": candidate["identity"], "layer": layer,
               "source_revision": candidate["revision"],
               "command": command, "cwd": str(Path(repo).resolve()), "started_at": now(),
               "platform": {"system": sys.platform, "machine": os.uname().machine}, "result": "NOT RUN",
               "classification": "UNTRIAGED", "kind": kind}
    receipt["environment"] = environment_record
    receipt["timeout_seconds"] = timeout_seconds
    atomic(output / "receipt.json", receipt)
    # Never inherit a stale TRX as current execution evidence.
    if trx_path and Path(trx_path).exists(): raise Refusal("TRX destination already exists; retain it and use a fresh output path")
    try:
        environment_values = os.environ.copy()
        environment_values["PWD"] = str(Path(repo).resolve())
        with log.open("xb") as stream:
            process = subprocess.Popen(command, cwd=repo, env=environment_values, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
            identity = process_identity(process.pid)
            receipt.update(process=identity, expected_cwd=environment_values["PWD"], pwd=environment_values["PWD"])
            atomic(output / "receipt.json", receipt)
            try: process.wait(timeout=timeout_seconds)
            except subprocess.TimeoutExpired:
                receipt.update(timed_out=True, reason="Bounded execution timed out; raw failure remains untriaged")
                stop_owned(process, identity)
        completed_successfully = process.returncode == 0 and not receipt.get("timed_out")
        receipt.update(exit_code=process.returncode, result="PROCESS_SUCCEEDED" if completed_successfully else "FAIL")
        for _ in range(20):
            group = remaining_group(identity)
            if not group: break
            time.sleep(0.01)
        receipt["remaining_group"] = group
        if trx_path and Path(trx_path).exists():
            receipt["tests"] = trx(trx_path)
            receipt["test_artifact"] = {"path": str(Path(trx_path).resolve()), **file_identity(trx_path)}
            if receipt["test_artifact"]["kind"] != "file": raise Refusal("Test evidence must be a retained regular file")
            if completed_successfully: receipt["result"] = receipt["tests"]["result"]
        if kind == "test" and receipt["result"] == "PROCESS_SUCCEEDED":
            receipt["result"] = "UNVERIFIED"  # exit zero alone never proves tests actually executed
        if group:
            receipt.update(result="BLOCKED", classification="INFRASTRUCTURE", reason="Live process-group members remain; preserve custody and reconcile, do not infer terminal ownership from leader exit")
        try: validate_candidate(candidate, repo)
        except Refusal as error: receipt.update(result="INVALIDATED", classification="INFRASTRUCTURE", reason=str(error))
        if environment:
            try: environment_inputs(environment)
            except Refusal as error: receipt.update(result="INVALIDATED", classification="INFRASTRUCTURE", reason=str(error))
    except (OSError, Refusal, ET.ParseError, ValueError) as error:
        receipt.update(result="BLOCKED", classification="INFRASTRUCTURE", reason=str(error))
    finally:
        receipt.update(ended_at=now(), duration_seconds=round(time.monotonic() - started, 6))
        if log.exists(): receipt["log"] = {"path": str(log), **file_identity(log)}
        atomic(output / "receipt.json", receipt)
    return receipt


def reusable(receipt, candidate, command, platform):
    validate_candidate(candidate)
    if receipt.get("timed_out"): return False  # also reject historically misclassified receipts
    if receipt.get("candidate") != candidate["identity"]: return False
    if receipt.get("command") != command or receipt.get("platform") != platform or receipt.get("result") != "PASS": return False
    environment = receipt.get("environment")
    if not environment or not environment.get("reviewer") or not environment.get("files"): return False
    for name, identity in environment["files"].items():
        try:
            if file_identity(name) != identity: return False
        except OSError: return False
    for field in ("log", "test_artifact"):
        evidence = receipt.get(field)
        if not evidence: return False
        try:
            if file_identity(evidence["path"]) != {k: v for k, v in evidence.items() if k != "path"}: return False
        except OSError: return False
    return True


def release_ready(state):
    rc = state.get("release_candidate")
    missing = []
    if not rc: return {"ready": False, "missing": ["immutable release candidate"]}
    try:
        candidate = read(rc["path"])
        validate_candidate(candidate)
        if candidate["identity"] != rc["identity"] or file_identity(rc["path"]) != rc["manifest"]:
            return {"ready": False, "missing": ["release candidate manifest changed"]}
    except (KeyError, OSError, ValueError, Refusal):
        return {"ready": False, "missing": ["release candidate manifest unavailable or invalid"]}
    for gate in MANDATORY:
        records = [e for e in state["evidence"].values() if e.get("gate") == gate and e.get("candidate") == rc["identity"]
                   and e.get("result") == "PASS" and e.get("layer") == 4]
        valid = []
        for e in records:
            if not e.get("reviewer") or e.get("reviewer") == e.get("implementer"): continue
            if not e.get("artifact") or not e.get("platform") or not e.get("steps"): continue
            try:
                p = e["artifact"]["path"]
                if e["artifact"].get("kind") != "file": continue
                if file_identity(p) != {k: v for k, v in e["artifact"].items() if k != "path"}: continue
            except (KeyError, OSError): continue
            if gate == "independent-acceptance" and set(e.get("quality", [])) != set(QUALITY): continue
            valid.append(e)
        if not valid: missing.append(gate)
    for key, change in state.get("pending_changes", {}).items():
        if change.get("kind") == "rc-backport" and not backports_current(change): missing.append("backport:" + key)
    # Readiness is an evidence gate, never inferred from green build steps or a stored label.
    return {"ready": not missing, "candidate": rc["identity"], "missing": missing}


def absolute_path(value, label):
    if not isinstance(value, str) or not value or "\0" in value or not Path(value).is_absolute():
        raise Refusal(label + " requires an absolute path")
    return Path(value)


def revision_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value):
        raise Refusal("Execution revision must be a full Git object ID")
    return value


def execution_files(root, files):
    if not isinstance(files, dict) or not files: raise Refusal("Execution source needs named file identities")
    for name, identity in files.items():
        if not isinstance(name, str): raise Refusal("Execution source path must be a string")
        safe_relative(name)
        if not isinstance(identity, dict) or "path" in identity: raise Refusal("Invalid execution file identity")
        recovery_reference({"path": str(root / name), **identity}, content=True)


def export_files(source, revision):
    asset = source["manifest"]
    recovery_reference(asset)
    # One bounded no-follow read supplies both the parsed bytes and their receipt.
    fd = os.open(asset["path"], os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode): raise Refusal("Export manifest must be a regular file")
        if before.st_size != asset["bytes"]: raise Refusal("Export manifest size differs from its receipt")
        raw = stream.read(asset["bytes"] + 1)
        after = os.fstat(stream.fileno())
        identity = {"kind": "file", "bytes": len(raw), "sha256": digest(raw), "mode": before.st_mode & 0o777}
        stable = ("st_dev", "st_ino", "st_size", "st_mtime_ns", "st_ctime_ns")
        if (identity != {k: v for k, v in asset.items() if k != "path"}
                or any(getattr(before, field) != getattr(after, field) for field in stable)):
            raise Refusal("Export manifest changed/unavailable")
    manifest = json.loads(raw)
    if not isinstance(manifest, dict): raise Refusal("Export manifest must be a JSON object")
    if (manifest.get("schema") != "9to1.exact-source-export/v1" or manifest.get("root") != source["path"]
            or manifest.get("origin_git") != source["git"]["path"]
            or manifest.get("source_revision") != source["git"]["revision"]
            or manifest.get("candidate_revision", manifest.get("source_revision")) != revision):
        raise Refusal("Export manifest root/origin/base/candidate binding differs")
    entries = manifest.get("files")
    if not isinstance(entries, list) or not entries: raise Refusal("Export manifest needs actual file entries")
    files = {}
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) != {"path", "bytes", "mode", "sha256"}: raise Refusal("Invalid export file entry")
        name = entry["path"]
        if not isinstance(name, str) or name in files: raise Refusal("Duplicate/invalid export file path")
        files[name] = {"kind": "file", **{k: v for k, v in entry.items() if k != "path"}}
    return files


def _execution_json(path, label, expected=None):
    """Parse the same stable no-follow regular-file bytes that were checked."""
    path = absolute_path(str(path), label)
    flags = os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW
    try: fd = os.open(path, flags | getattr(os, "O_NOATIME", 0))
    except PermissionError: fd = os.open(path, flags)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        if not stat.S_ISREG(before.st_mode) or before.st_size > 16 * 1024 * 1024:
            raise Refusal(label + " must be a bounded regular JSON file")
        if expected is not None and before.st_size != expected["bytes"]:
            raise Refusal(label + " identity changed")
        raw = stream.read(before.st_size + 1); after = os.fstat(stream.fileno())
        identity = {"kind": "file", "bytes": len(raw), "mode": before.st_mode & 0o777, "sha256": digest(raw)}
        stable = ("st_dev", "st_ino", "st_size", "st_mtime_ns", "st_ctime_ns")
        if (len(raw) != before.st_size or any(getattr(before, key) != getattr(after, key) for key in stable)
                or expected is not None and identity != expected):
            raise Refusal(label + " identity changed while reading")
    value = json.loads(raw)
    if not isinstance(value, dict): raise Refusal(label + " must be a JSON object")
    return value


def _execution_admission_scope(state, execution):
    if state.get("schema") != SCHEMA or type(state.get("generation")) is not int or state["generation"] < 0:
        raise Refusal("Current execution ledger schema/generation is invalid")
    fields = ("development_candidates", "execution_custody", "recovery_assets")
    if any(not isinstance(state.get(key), dict) for key in fields):
        raise Refusal("Current execution candidate/custody/Root admission is missing")
    return {"candidate": state["development_candidates"].get(execution),
            "custody": state["execution_custody"].get(execution),
            "Root": state["recovery_assets"].get("latest-root-checkpoint"),
            "observed_Root": state.get("last_root_checkpoint_observed")}


def execution_admission(state_path, execution, source_path, revision, manifest_path, manifest_sha256, runner_path):
    """Match existing coordinator admission immediately before launch. Never grant or launch.

    This intentionally checks manifest metadata/pins, not all source files: the
    admitted owning runner must keep its full source, resources and process guards.
    """
    if not isinstance(execution, str) or not execution: raise Refusal("A named current execution admission is required")
    state = _execution_json(state_path, "Current execution ledger")
    scope = _execution_admission_scope(state, execution)
    candidate, record = scope["candidate"], scope["custody"]
    revision = revision_id(revision)
    if not isinstance(candidate, dict) or not isinstance(record, dict) or candidate.get("revision") != revision or record.get("revision") != revision:
        raise Refusal("Current candidate revision does not admit the requested execution")
    if set(record) != {"revision", "owner", "source", "build_dependencies"} or not isinstance(record["owner"], str) or not record["owner"].strip():
        raise Refusal("Current execution custody has no exact owner/source admission")
    source = record["source"]
    if not isinstance(source, dict) or set(source) != {"kind", "path", "git", "manifest"} or source.get("kind") != "source-export":
        raise Refusal("Execution admission requires the current source-export custody")
    root = absolute_path(str(source_path), "Requested execution source")
    if source.get("path") != str(root) or candidate.get("worktree") != str(root) or not root.is_dir():
        raise Refusal("Current candidate source path differs or is unavailable")
    origin = source["git"]
    if not isinstance(origin, dict) or set(origin) != {"path", "common_directory", "revision", "required_revisions"}:
        raise Refusal("Execution export Git metadata is invalid")
    absolute_path(origin["path"], "Export origin"); absolute_path(origin["common_directory"], "Export object store")
    base = revision_id(origin["revision"])
    if candidate.get("base_revision", base) != base or not isinstance(origin["required_revisions"], list):
        raise Refusal("Current candidate export base differs")
    for value in origin["required_revisions"]: revision_id(value)
    asset = source["manifest"]; recovery_reference(asset)
    if (candidate.get("manifest") != asset or asset["path"] != str(absolute_path(str(manifest_path), "Requested execution manifest"))
            or asset["sha256"] != manifest_sha256):
        raise Refusal("Current candidate/export manifest pin differs from the requested execution")
    dependencies = record["build_dependencies"]
    runner = dependencies.get("actual-guarded-sdk-runner") if isinstance(dependencies, dict) else None
    if not isinstance(runner, dict) or set(runner) != {"path", "identity", "recovery", "next_action"}:
        raise Refusal("Current guarded runner admission is missing or invalid")
    if runner["recovery"] not in ("restore", "rebuild") or not isinstance(runner["next_action"], str) or not runner["next_action"].strip():
        raise Refusal("Current guarded runner admission has invalid recovery metadata")
    if not isinstance(runner["identity"], dict) or set(runner["identity"]) != {"kind", "bytes", "mode", "sha256"}:
        raise Refusal("Current guarded runner admission requires its exact file identity")
    if runner["path"] != str(absolute_path(str(runner_path), "Requested guarded runner")):
        raise Refusal("Current guarded runner path differs from the requested execution")
    runner_pin = {"path": runner["path"], **runner["identity"]}; recovery_reference(runner_pin)
    latest, observed = scope["Root"], scope["observed_Root"]
    try:
        recovery_reference(latest); recovery_locator(observed)
        if (observed["path"] != latest["path"] or observed["sha256"] != latest["sha256"] or
                any(key in observed and observed[key] != latest[key] for key in ("kind", "bytes", "mode"))):
            raise Refusal("Root observation and current retained proof must match")
        proof = _execution_json(latest["path"], "Root execution admission proof", {key: value for key, value in latest.items() if key != "path"})
    except (OSError, ValueError, TypeError, KeyError, Refusal) as error:
        raise Refusal("Root admission proof unavailable/changed: " + str(error)) from error
    admitted = proof.get("sdk_admission")
    generation = proof.get("expected_next_generation")
    if (proof.get("schema") != "9to1.root-live/v1" or proof.get("candidate") != revision or proof.get("source_manifest") != asset
            or type(generation) is not int or not 0 < generation <= state["generation"]):
        raise Refusal("Root proof candidate/manifest/generation was not adopted by the current ledger")
    if not isinstance(admitted, dict) or admitted.get("runner") != runner_pin:
        raise Refusal("Root SDK admission does not bind this exact guarded runner")
    try:
        # Same existing parser binds root/origin/base/candidate and validates the
        # actual pinned manifest bytes. Do not call execution_files/tree here.
        files = export_files(source, revision)
        for name, identity in files.items():
            safe_relative(name); recovery_reference({"path": str(root / name), **identity})
    except (OSError, ValueError, TypeError, KeyError, Refusal) as error:
        raise Refusal("Execution manifest unavailable/changed: " + str(error)) from error
    for label, pin in (("Root proof", latest), ("Execution manifest", asset), ("Guarded runner", runner_pin)):
        try: recovery_reference(pin, content=True)
        except (OSError, Refusal) as error: raise Refusal(label + " identity changed: " + str(error)) from error
    current = _execution_json(state_path, "Current execution ledger")
    if _execution_admission_scope(current, execution) != scope or current["generation"] < state["generation"]:
        raise Refusal("Current execution admission changed during validation; reread before any launch")
    return {"result": "PASS", "classification": "INFRASTRUCTURE", "execution": execution, "revision": revision,
            "generation": current["generation"], "source_path": str(root), "source_manifest": asset, "runner": runner_pin,
            "Root_checkpoint": latest, "admission_created": False, "process_launched": False, "source_files_revalidated": False,
            "scope": "Existing current coordinator pins matched only. Invoke immediately before the exact guarded runner; keep its full source/resource/process checks. No launch, ownership, allocation, acceptance or release authority is created."}


def execution_record(record, candidate, *, publication=False):
    """Check observed execution inputs; never infer acceptance from their availability."""
    if not isinstance(record, dict) or set(record) != {"revision", "owner", "source", "build_dependencies"}:
        raise Refusal("Execution custody requires revision/owner/source/build_dependencies")
    revision = revision_id(record["revision"])
    if not isinstance(candidate, dict) or candidate.get("revision") != revision:
        raise Refusal("Execution custody must match its named development candidate revision")
    if not isinstance(record["owner"], str) or not record["owner"].strip(): raise Refusal("Execution custody needs an actual owner")
    source = record["source"]
    if not isinstance(source, dict) or source.get("kind") not in ("git-worktree", "source-export"):
        raise Refusal("Execution source kind must be git-worktree or source-export")
    expected = {"kind", "path", "git", "files" if source["kind"] == "git-worktree" else "manifest"}
    if set(source) != expected: raise Refusal("Execution source fields differ from its kind")
    root = absolute_path(source["path"], "Execution source")
    if candidate.get("worktree") is not None and candidate["worktree"] != str(root):
        raise Refusal("Execution source path differs from named candidate worktree")
    origin = source["git"]
    if not isinstance(origin, dict) or set(origin) != {"path", "common_directory", "revision", "required_revisions"}:
        raise Refusal("Execution Git custody requires path/common_directory/revision/required_revisions")
    repository = absolute_path(origin["path"], "Execution Git repository")
    common = absolute_path(origin["common_directory"], "Execution common Git directory")
    base = revision_id(origin["revision"])
    if not isinstance(origin["required_revisions"], list): raise Refusal("Required Git revisions must be a list")
    required = {revision, base, *(revision_id(x) for x in origin["required_revisions"])}
    if candidate.get("base_revision") is not None: required.add(revision_id(candidate["base_revision"]))
    source_problems, build_problems, publication_problems, actions = [], [], [], []
    try:
        if not root.is_dir(): raise Refusal("Execution source directory unavailable: " + str(root))
        actual_common = Path(git(repository, "rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip()).resolve()
        if actual_common != common.resolve(): raise Refusal("Execution common Git directory changed")
        if source["kind"] == "git-worktree":
            if repository != root or base != revision: raise Refusal("Git worktree custody must bind its own repository and HEAD")
            actual_root = Path(git(root, "rev-parse", "--show-toplevel").decode().strip()).resolve()
            if actual_root != root.resolve() or git(root, "rev-parse", "HEAD").decode().strip() != revision:
                raise Refusal("Execution worktree root/HEAD changed")
        for required_revision in sorted(required):
            if git(repository, "cat-file", "-t", required_revision).decode().strip() != "commit": raise Refusal("Execution revision is not a commit")
            closure = git_closure(repository, required_revision)
            if closure["result"] != "PASS": raise Refusal("; ".join(closure["problems"]))
        files = source["files"] if source["kind"] == "git-worktree" else export_files(source, revision)
        execution_files(root, files)
    except (OSError, subprocess.CalledProcessError, Refusal, ValueError, TypeError, KeyError) as error:
        source_problems.append("Execution source unavailable/changed: " + str(error))
        actions.append("Restore exact declared source/object closure, or publish an explicitly reviewed successor custody record")
    dependencies = record["build_dependencies"]
    if not isinstance(dependencies, dict): raise Refusal("Build dependencies must be named prerequisite records")
    for name, dependency in dependencies.items():
        if not isinstance(name, str) or not isinstance(dependency, dict) or set(dependency) != {"path", "identity", "recovery", "next_action"}:
            raise Refusal("Build prerequisite requires path/identity/recovery/next_action")
        path = absolute_path(dependency["path"], "Build prerequisite")
        if dependency["recovery"] not in ("rebuild", "restore") or not isinstance(dependency["next_action"], str) or not dependency["next_action"].strip():
            raise Refusal("Build prerequisite needs rebuild/restore and an executable next action")
        if dependency["identity"] is None:
            if publication and os.path.lexists(path): raise Refusal("Existing build prerequisite needs its actual identity")
            build_problems.append("Build prerequisite identity unrecorded: " + name)
            actions.append(dependency["next_action"])
            continue
        try:
            if not isinstance(dependency["identity"], dict) or "path" in dependency["identity"]: raise Refusal("Invalid build identity")
            recovery_reference({"path": str(path), **dependency["identity"]}, content=True)
        except (OSError, Refusal) as error:
            build_problems.append("Build prerequisite unavailable/changed: " + name + ": " + str(error))
            publication_problems.append(build_problems[-1])
            actions.append(dependency["next_action"])
    if publication and (source_problems or publication_problems):
        raise Refusal("; ".join(source_problems + publication_problems))
    problems = source_problems + build_problems
    return {"result": "BLOCKED" if problems else "PASS", "classification": "INFRASTRUCTURE", "owner": record["owner"],
            "revision": revision, "source_path": str(root), "source_status": "BLOCKED" if source_problems else "PASS",
            "build_status": "BLOCKED" if build_problems else ("PASS" if dependencies else "UNRECORDED"),
            "problems": problems, "next_actions": actions,
            "scope": "Declared inputs only; omitted build prerequisites, live workers, compilation and native acceptance are not verified"}


def checkpoint_execution_preflight(state, patch):
    if not ({"execution_custody", "development_candidates"} & patch.keys()): return
    incoming = patch.get("execution_custody", {})
    candidates = patch.get("development_candidates", {})
    if not isinstance(incoming, dict) or not isinstance(candidates, dict): raise Refusal("Execution custody/candidates must be named objects")
    merged_candidates = {**state["development_candidates"], **candidates}
    merged_custody = {**state.get("execution_custody", {}), **incoming}
    for name in incoming.keys() | candidates.keys():
        if name in merged_custody:
            execution_record(merged_custody[name], merged_candidates.get(name), publication=True)
        elif isinstance(merged_candidates.get(name), dict) and "worktree" in merged_candidates[name]:
            # New active path declarations need actual structured custody; historical
            # revision/status-only records stay compatible, without becoming ready.
            raise Refusal("Active candidate worktree requires execution custody in the same checkpoint: " + name)


def execution_checks(state, selected=None):
    candidates, custody = state["development_candidates"], state.get("execution_custody", {})
    names = {selected} if selected else set(candidates) | set(custody)
    checks = {}
    for name in sorted(names):
        try:
            if name not in candidates: raise Refusal("Unknown named development candidate: " + name)
            if name in custody:
                checks[name] = execution_record(custody[name], candidates[name])
                continue
            candidate = candidates[name]
            problems = []
            if not isinstance(candidate, dict): raise Refusal("Invalid legacy candidate record")
            if "worktree" in candidate:
                root = absolute_path(candidate["worktree"], "Legacy candidate worktree")
                revision = revision_id(candidate.get("revision"))
                if not root.is_dir() or Path(git(root, "rev-parse", "--show-toplevel").decode().strip()).resolve() != root.resolve():
                    raise Refusal("Legacy candidate worktree unavailable: " + str(root))
                if git(root, "rev-parse", "HEAD").decode().strip() != revision: raise Refusal("Legacy candidate HEAD changed")
                closure = git_closure(root, revision)
                problems.extend(closure["problems"])
            checks[name] = {"result": "BLOCKED" if problems else "UNRECORDED", "problems": problems,
                            "next_actions": ["Coordinator must publish actual structured source and build prerequisite custody"],
                            "scope": "Legacy metadata is not execution custody or readiness"}
        except (Refusal, OSError, subprocess.CalledProcessError, ValueError, TypeError, KeyError) as error:
            checks[name] = {"result": "BLOCKED", "classification": "INFRASTRUCTURE", "problems": [str(error)],
                            "next_actions": ["Restore/reconcile named candidate source; retain historical evidence separately"]}
    blocked = any(c["result"] == "BLOCKED" for c in checks.values())
    complete = bool(checks) and all(c["result"] == "PASS" and c.get("build_status") == "PASS" for c in checks.values())
    return {"result": "BLOCKED" if blocked else ("DECLARED_INPUTS_AVAILABLE" if complete else "UNRECORDED"),
            "declared_inputs_available": complete, "candidates": checks,
            "native_acceptance": "NOT RUN", "liveness": "NOT CHECKED"}


def git_closure(repo, revision="HEAD"):
    problems = []
    alternate_file = Path(git(repo, "rev-parse", "--git-path", "objects/info/alternates").decode().strip())
    if not alternate_file.is_absolute(): alternate_file = Path(repo) / alternate_file
    if alternate_file.exists():
        for line in alternate_file.read_text().splitlines():
            alternate = Path(line)
            if not alternate.is_absolute(): alternate = alternate_file.parent.parent / alternate
            if not alternate.is_dir(): problems.append("Missing Git object alternate: " + str(alternate))
    try:
        listing = git(repo, "rev-list", "--objects", "--missing=print", revision).decode().splitlines()
        missing = [line for line in listing if line.startswith("?")]
        problems.extend("Missing reachable Git object: " + x[1:] for x in missing[:20])
    except subprocess.CalledProcessError as error:
        problems.append("Cannot enumerate " + revision + " object closure: " + error.stderr.decode(errors="replace")[:1000])
    return {"check": "git-object-closure", "result": "BLOCKED" if problems else "PASS", "classification": "INFRASTRUCTURE", "problems": problems,
            "limitation": "Selected revision reachability only; separately retain working source manifests and external prerequisites."}


def donor_checks(repo, registry):
    checks = []
    for name, donor in registry.get("donors", {}).items():
        if not any((Path(repo) / p).exists() for p in donor.get("when_any", [])): continue
        problems = []
        try:
            entry = git(repo, "ls-files", "--stage", "--", donor["path"]).decode().strip().split()
            if len(entry) < 2 or entry[:2] != ["160000", donor["revision"]]: problems.append("Pinned donor Gitlink differs or is absent")
        except subprocess.CalledProcessError as error: problems.append("Cannot read donor pin: " + str(error))
        for path, expected in donor["files"].items():
            safe_relative(path)
            try:
                if file_identity(Path(repo) / path) != expected: problems.append("Changed pinned donor input: " + path)
            except OSError: problems.append("Missing pinned donor input: " + path)
        checks.append({"check": "donor-source-closure:" + name, "result": "BLOCKED" if problems else "PASS", "classification": "INFRASTRUCTURE",
                       "revision": donor["revision"], "checked_files": len(donor["files"]), "problems": problems})
    return checks


def preflight(repo, candidate=None, donors=None, state=None, execution=None):
    if execution:
        if candidate is not None: raise Refusal("Use a named execution custody record or --candidate, not both")
        if state is None: raise Refusal("Named execution preflight requires --state")
        declared = execution_checks(state, execution)
        check_execution_context(state, repo, execution)
        return {"at": now(), "ready": declared["declared_inputs_available"], "execution_custody": declared,
                "scope": "Named declared source/build inputs only; toolchain execution and native acceptance NOT RUN"}
    checks = []
    for relative in ("AGENTS.md", "global.json", "NuGet.Config", "docs/specification/9to1-development-specification-v2.md"):
        checks.append({"check": relative, "result": "PASS" if (Path(repo) / relative).is_file() else "BLOCKED", "classification": "INFRASTRUCTURE"})
    checks.append(git_closure(repo))
    donor_registry = Path(__file__).with_name("donor-inputs.json")
    if donors is None and donor_registry.exists(): donors = read(donor_registry)
    checks.extend(donor_checks(repo, donors or {}))
    if candidate:
        try: validate_candidate(candidate, repo); result, reason = "PASS", None
        except Refusal as error: result, reason = "BLOCKED", str(error)
        checks.append({"check": "candidate inputs", "result": result, "reason": reason, "classification": "INFRASTRUCTURE"})
    vfs = os.statvfs(repo)
    checks.append({"check": "free-space", "available_bytes": vfs.f_bavail * vfs.f_frsize,
                   "result": "OBSERVED", "note": "Use the owning build/package peak-space requirement; no invented universal fit claim."})
    execution_result = execution_checks(state) if state is not None else None
    return {"at": now(), "checks": checks,
            "execution_custody": execution_result,
            "ready": all(c["result"] != "BLOCKED" for c in checks) and (execution_result is None or execution_result["declared_inputs_available"]),
            "scope": "Generic source/configuration/Git/donor preflight only; build and native readiness are not inferred.",
            "native_environment": "NOT RUN", "toolchain": "NOT RUN: use platform-specific existing preflight"}


def plan(changed, registry, release=False):
    selected, unknown = set(), []
    for path in changed:
        safe_relative(path)
        matches = [key for key, entry in registry["checks"].items() if any(scope_overlap(path, scope) for scope in entry["paths"])]
        selected.update(matches)
        if not matches: unknown.append(path)
    if unknown: selected.update(registry["checks"])  # uncovered dependency fails conservatively to the complete registered set
    todo = list(selected)
    while todo:
        name = todo.pop()
        for dep in registry["checks"][name].get("depends_on", []):
            if dep not in registry["checks"]: raise Refusal("Unknown check dependency: " + dep)
            if dep not in selected: selected.add(dep); todo.append(dep)
    if release: selected.update(registry["checks"])
    return {"checks": {k: registry["checks"][k] for k in sorted(selected)}, "unknown_paths": unknown,
            "mandatory_release_gates": list(MANDATORY) if release else [],
            "selection_limit": "Registered checks are dependency selectors, not an exhaustive product acceptance manifest. L4 is never waived."}


def recovery_checkpoint(state):
    """Use the coordinator's pinned observation, never a filename/time heuristic."""
    assets = state["recovery_assets"]
    observed = state.get("last_root_checkpoint_observed")
    latest = assets.get("latest-root-checkpoint")
    if "last_root_checkpoint_observed" not in state and "latest-root-checkpoint" not in assets:
        asset = assets.get("root-checkpoint")
        source = "initial projection checkpoint (legacy state without a newer observation)"
    else:
        if not isinstance(observed, dict) or not isinstance(latest, dict):
            raise Refusal("Newest ROOT-LIVE observation and retained latest-root-checkpoint asset are both required")
        if (observed.get("path") != latest.get("path") or observed.get("sha256") != latest.get("sha256")):
            raise Refusal("Newest ROOT-LIVE observation does not match its retained asset identity")
        asset = latest
        source = "last_root_checkpoint_observed"
    if not isinstance(asset, dict) or asset.get("kind") != "file":
        raise Refusal("Selected ROOT-LIVE checkpoint must be a retained regular file")
    if file_identity(asset["path"]) != {k: v for k, v in asset.items() if k != "path"}:
        raise Refusal("Selected ROOT-LIVE checkpoint content identity changed")
    return {"path": asset["path"], "sha256": asset["sha256"], "selection": source,
            "recorded_in_generation": state["generation"], "ledger_updated_at": state["updated_at"]}


def check_execution_context(state, repo, execution):
    custody = state.get("execution_custody", {})
    if not isinstance(custody, dict): raise Refusal("Execution custody must be a named object")
    record = custody.get(execution)
    if not isinstance(record, dict): raise Refusal("Named execution requires structured custody")
    source = record.get("source")
    if not isinstance(source, dict) or source.get("kind") not in ("git-worktree", "source-export"):
        raise Refusal("Named execution requires a structured source of a supported kind")
    origin = source.get("git")
    if not isinstance(origin, dict): raise Refusal("Named execution requires structured Git custody")
    permitted = [absolute_path(state.get("repository"), "Original repository"),
                 absolute_path(source.get("path"), "Named execution source"),
                 absolute_path(origin.get("path"), "Named execution Git repository")]
    requested = os.fspath(repo) if isinstance(repo, (str, os.PathLike)) else None
    if absolute_path(requested, "Requested repository").resolve() not in {p.resolve() for p in permitted}:
        raise Refusal("Requested repository is unrelated to named execution custody")


def recovery(state, repo, execution=None):
    # The projection's original repository and a named execution source are separate.
    canonical_repo = state["repository"] if execution else repo
    revision = None
    rc = state.get("release_candidate")
    discrepancies = []
    try: validate_state(state)
    except (Refusal, KeyError, TypeError) as error: discrepancies.append("Ledger evidence/schema validation failed: " + str(error))
    try:
        if execution: check_execution_context(state, repo, execution)
        elif Path(repo).resolve() != Path(state["repository"]).resolve():
            discrepancies.append("Repository path differs from original projection; select explicit named execution custody")
        revision = git(canonical_repo, "rev-parse", "HEAD").decode().strip()
        if revision != state.get("observed_revision"): discrepancies.append("Repository HEAD differs from checkpoint")
        object_closure = git_closure(canonical_repo)
    except (OSError, Refusal, KeyError, subprocess.CalledProcessError) as error:
        object_closure = {"result": "BLOCKED", "classification": "INFRASTRUCTURE", "problems": ["Original repository unavailable: " + str(error)]}
    declared = execution_checks(state, execution)
    for name, check in declared["candidates"].items():
        if check["result"] == "BLOCKED": discrepancies.extend(name + ": " + p for p in check["problems"])
    if rc:
        try:
            candidate = read(rc["path"])
            validate_candidate(candidate)
            if candidate["identity"] != rc["identity"] or file_identity(rc["path"]) != rc["manifest"]:
                raise Refusal("Pinned RC content identity changed")
        except (OSError, KeyError, ValueError, Refusal) as error: discrepancies.append("RC manifest unavailable/changed: " + str(error))
    if object_closure["result"] != "PASS": discrepancies.extend(object_closure["problems"])
    for key, asset in state.get("recovery_assets", {}).items():
        try:
            if not isinstance(asset, dict): raise Refusal("required recovery asset must be an identity record")
            if asset.get("kind") != "file": raise Refusal("required recovery assets must be retained regular files, not symlink targets")
            if file_identity(asset["path"]) != {k: v for k, v in asset.items() if k != "path"}: raise Refusal("content identity changed")
        except (OSError, KeyError, TypeError, Refusal) as error: discrepancies.append("Recovery asset unavailable/changed: " + key + ": " + str(error))
    checkpoint = None
    try: checkpoint = recovery_checkpoint(state)
    except (OSError, KeyError, TypeError, Refusal) as error:
        discrepancies.append("ROOT-LIVE selection unavailable/changed: " + str(error))
    return {"schema": SCHEMA, "recovered_at": now(), "generation": state["generation"], "actual_revision": revision,
            "read_first": ["AGENTS.md", "docs/development/workflow.md", "docs/specification/9to1-development-specification-v2.md"] + ([checkpoint["path"]] if checkpoint else []),
            "root_checkpoint": checkpoint, "projection_origin": state["projection_of"],
            "workers": state["workers"], "tasks": state["tasks"], "blockers": state["blockers"],
            "outstanding_acceptance": state["outstanding_acceptance"], "release_readiness": release_ready(state),
            "next_actions": state["next_actions"], "discrepancies": discrepancies, "recovery_status": "BLOCKED" if discrepancies else "CHECKPOINT",
            "git_object_closure": object_closure, "canonical_repository": state["repository"],
            "requested_repository": str(repo), "selected_execution": execution, "execution_custody": declared,
            "execution_next_actions": {name: c["next_actions"] for name, c in declared["candidates"].items()},
            "liveness": "NOT CHECKED: inspect supported live agent/worker APIs; historical PIDs are not live custody",
            "native_testing": "NOT RUN by recovery", "provider_policy": "Mercury revoked: Beam and Codex only; no paid fallback"}


def dashboard(state):
    lines = ["# Development checkpoint projection", "", f"Generation {state['generation']} · updated {state['updated_at']}", "",
             "PC coordinator remains authoritative. Engineering, integration and release acceptance are separate.", "",
             "| Task | Owner | Stage | Status | Last verified revision | Blockers | Next action |", "|---|---|---|---|---|---|---|"]
    for key, task in sorted(state["tasks"].items()):
        values = [key, task.get("owner", "UNASSIGNED"), task.get("stage", "development"), task["status"], task.get("verified_revision", "NOT VERIFIED"), ", ".join(task.get("dependencies", [])), task.get("next_action", "UNRECORDED")]
        lines.append("| " + " | ".join(str(x).replace("|", "\\|").replace("\n", " ") for x in values) + " |")
    lines += ["", "Release: " + ("evidence gates met" if release_ready(state)["ready"] else "BLOCKED — " + ", ".join(release_ready(state)["missing"])), ""]
    return "\n".join(lines)


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--state", type=Path)
    sub = p.add_subparsers(dest="action", required=True)
    q = sub.add_parser("init"); q.add_argument("--repo", required=True); q.add_argument("--root-checkpoint", required=True)
    q = sub.add_parser("checkpoint"); q.add_argument("--expect", type=int, required=True); q.add_argument("--patch", required=True)
    q = sub.add_parser("claim"); q.add_argument("--expect", type=int, required=True); q.add_argument("--worker", required=True); q.add_argument("--role", required=True); q.add_argument("paths", nargs="+")
    q = sub.add_parser("release"); q.add_argument("--expect", type=int, required=True); q.add_argument("--worker", required=True)
    q = sub.add_parser("snapshot"); q.add_argument("--repo", required=True); q.add_argument("--out", required=True); q.add_argument("--accepted-fix", action="append", default=[]); q.add_argument("--previous-candidate")
    q = sub.add_parser("pin-rc"); q.add_argument("--expect", type=int, required=True); q.add_argument("--candidate", required=True)
    q = sub.add_parser("advance-rc"); q.add_argument("--expect", type=int, required=True); q.add_argument("--candidate", required=True); q.add_argument("--patch", required=True)
    q = sub.add_parser("verify-backport"); q.add_argument("--expect", type=int, required=True); q.add_argument("--patch-id", required=True); q.add_argument("--target", required=True); q.add_argument("--repo", required=True)
    q = sub.add_parser("preflight"); q.add_argument("--repo", required=True); q.add_argument("--candidate"); q.add_argument("--execution")
    q = sub.add_parser("copy-budget"); q.add_argument("--manifest", required=True); q.add_argument("--sha256", required=True)
    q = sub.add_parser("execution-admission"); q.add_argument("--execution", required=True); q.add_argument("--source", required=True); q.add_argument("--revision", required=True); q.add_argument("--manifest", required=True); q.add_argument("--manifest-sha256", required=True); q.add_argument("--runner", required=True)
    q = sub.add_parser("plan"); q.add_argument("--changed", required=True); q.add_argument("--registry", default=str(Path(__file__).with_name("checks.json"))); q.add_argument("--release", action="store_true")
    q = sub.add_parser("run"); q.add_argument("--repo", required=True); q.add_argument("--candidate", required=True); q.add_argument("--check", required=True); q.add_argument("--layer", type=int, required=True); q.add_argument("--out", required=True); q.add_argument("--trx"); q.add_argument("--environment-manifest"); q.add_argument("--timeout-seconds", type=float, default=300); q.add_argument("--kind", choices=["test", "build"], default="test"); q.add_argument("command", nargs=argparse.REMAINDER)
    q = sub.add_parser("recover"); q.add_argument("--repo", required=True); q.add_argument("--execution")
    sub.add_parser("dashboard"); sub.add_parser("release-status")
    a = p.parse_args(argv)
    try:
        if a.action == "init":
            if not a.state: raise Refusal("--state required")
            value = initial_state(a.repo, a.root_checkpoint)
            initialize(a.state, value)
        elif a.action == "snapshot":
            old = read(a.previous_candidate)["accepted_fixes"] if a.previous_candidate else []
            value = snapshot(a.repo, a.out, [read(f) for f in a.accepted_fix], old)
        elif a.action == "preflight": value = preflight(a.repo, read(a.candidate) if a.candidate else None, state=read(a.state) if a.state else None, execution=a.execution)
        elif a.action == "execution-admission":
            if not a.state: raise Refusal("--state required for current execution admission")
            value = execution_admission(a.state, a.execution, a.source, a.revision, a.manifest, a.manifest_sha256, a.runner)
        elif a.action == "copy-budget":
            from copy_budget import assess
            value = assess(a.manifest, a.sha256)
        elif a.action == "plan": value = plan(read(a.changed), read(a.registry), a.release)
        elif a.action == "run":
            cmd = a.command[1:] if a.command[:1] == ["--"] else a.command
            if not cmd: raise Refusal("Explicit command required")
            value = run_check(a.repo, read(a.candidate), a.check, a.layer, cmd, a.out, a.trx, a.kind, a.environment_manifest, a.timeout_seconds)
        elif a.action in ("checkpoint", "claim", "release", "pin-rc", "advance-rc", "verify-backport"):
            if not a.state: raise Refusal("--state required")
            with transaction(a.state, a.expect) as state:
                if a.action == "checkpoint":
                    apply_checkpoint(state, read(a.patch))
                elif a.action == "claim": claim(state, a.worker, a.paths, a.role)
                elif a.action == "release": state["workers"][a.worker].update(status="CHECKPOINT", released_at=now())
                elif a.action == "advance-rc": advance_rc(state, read(a.candidate), a.candidate, read(a.patch))
                elif a.action == "verify-backport": verify_backport(state, a.patch_id, a.target, a.repo)
                else: pin_rc(state, read(a.candidate), a.candidate)
            value = read(a.state)
        else:
            if not a.state: raise Refusal("--state required")
            state = read(a.state)
            if a.action == "dashboard": validate_state(state); print(dashboard(state)); return 0
            if a.action == "release-status": validate_state(state)
            value = recovery(state, a.repo, a.execution) if a.action == "recover" else release_ready(state)
        print(json.dumps(value, indent=2, ensure_ascii=False))
        if a.action == "run" and value["result"] not in ("PASS", "PROCESS_SUCCEEDED"): return 1
        if a.action == "preflight" and not value["ready"]: return 2
        if a.action == "copy-budget" and not value["known_copy_budget_fits"]: return 2
        if a.action == "recover" and value["recovery_status"] == "BLOCKED": return 2
        if a.action == "release-status" and not value["ready"]: return 1
        return 0
    except (Refusal, OSError, KeyError, ValueError, ET.ParseError, subprocess.CalledProcessError) as error:
        print(json.dumps({"result": "BLOCKED", "classification": "INFRASTRUCTURE", "reason": str(error)})); return 2


if __name__ == "__main__":
    sys.exit(main())
