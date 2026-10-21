#!/usr/bin/env python3
"""Thunder artifact audit — static dependency + closure + receipt validation (SOURCE-ONLY).

This is a BOUNDED, build-free audit of a prior CI32 Thunder CUDA compile/install closure. It never
executes the engine/relay/DSO, never runs ldd/CUDA/nvidia-smi, never touches a GPU or driver, never
downloads models, and never promotes or claims runtime readiness.

It uses ONLY the standard library plus the `readelf` static reader (binutils). It runs on the GitHub
ubuntu-24.04 runner. The prior artifacts (evidence + install-closure) are fetched into fresh scoped
runner folders by the official `actions/download-artifact@v4` action using the validated artifact ids;
this script never downloads or extracts them itself and never writes the 43 MiB install archive to the
local worker.

Two modes (both read inputs from environment):
  --preflight   strictly validate inputs + GitHub metadata (same repo/run/head, run SUCCESS, artifacts
                not expired, exact names/ids) + runner disk/memory/artifact-size budget BEFORE any fetch.
  --audit       validate the ALREADY-fetched folders (13-file closure + canonical paths + symlink
                rejection + size caps + receipt/source-lock pin binding + evidence cross-check + static
                ELF inspection of engine/worker/KQ) and write a bounded structured report (pass/fail).

No unsafe GitHub input is interpolated into shell; inputs arrive via environment and are strictly
validated here (ASCII decimal IDs, 40-hex SHA) before any API use. The action token is used read-only
and is never printed or dumped.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import urllib.error
import urllib.request
from typing import Dict, List, Optional

# ------------------------------------------------------------------ verified candidate pin (v0.2.2)
CANDIDATE_COMMIT = "011c9e600a93bbc9fd451c563501b5831c32bbca"
CANDIDATE_TREE = "539755e6d6b799bd75a47225befc786c436f6d8b"
CANDIDATE_ARCHIVE_SHA256 = "ccfffeaabfb410b734b95d202576b98b3d2f2d28080a69ea8a3c49268d95c93a"
CANDIDATE_PROFILE = "candidate"
CANDIDATE_ENGINE_VERSION = "strata 0.1.47"
# The checked-out immutable candidate lock (prep/strata-v0.2.2-lock.candidate.json). The installed
# closure's `strata-source-lock.json` must hash to EXACTLY this value, and the receipt lockSha256
# must match it too (independent evidence, not self-declared).
CANDIDATE_LOCK_SHA256 = "9952dc3f5bb79c3cd44e831b94039ba564fdc600e21a0317dcecc3e110eeaebf"

GGML_COMMIT = "3cf03257f219afbe7334045ff7c6a06ac68c627d"
GGML_TREE = "d255198f04f9b8349f1dff24501d513f83cbfeda"

# Fixed MIT notice hashes/bytes (independent install-closure inventory), required of BOTH duplicate
# notices (upstream + ggml copies). Not self-declared inventory alone.
MIT_STRATA_SHA256 = "949b9b0aada7476006469e58ee4ffb0e0f68c2d9137e90a356757b61a09d37f0"
MIT_STRATA_BYTES = 1093
MIT_GGML_SHA256 = "94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d"
MIT_GGML_BYTES = 1078

INSTALL_CAP_BYTES = 512 * 1024 * 1024      # 512 MiB install-closure cap (uncompressed cumulative)
EVIDENCE_CAP_BYTES = 4 * 1024 * 1024       # 4 MiB evidence cap
MIN_PREFLIGHT_MEM_BYTES = 1024 * 1024 * 1024  # 1 GiB effective memory gate
PREFLIGHT_LOG_SLACK_BYTES = 256 * 1024 * 1024  # slack for logs + extraction overhead
MAX_API_RESPONSE_BYTES = 1024 * 1024        # 1 MiB bounded GitHub API JSON response
CHUNK = 1024 * 1024                         # streaming hash chunk
READELF_TIMEOUT_S = 30
READELF_OUTPUT_CAP = 1024 * 1024            # 1 MiB cap on readelf stdout/stderr

HEX40_RE = re.compile(r"^[0-9a-fA-F]{40}$")   # git commit SHA (short 40-hex)
HEX64_RE = re.compile(r"^[0-9a-fA-F]{64}$")   # sha256 hex digest
REPO_RE = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
DECIMAL_RE = re.compile(r"^[0-9]+$")

# The EXACT 13-file install closure (canonical POSIX-relative paths). No extra/missing/renamed files.
EXPECTED_CLOSURE_PATHS = [
    "licenses/MIT-GGML.txt",
    "licenses/MIT-STRATA.txt",
    "licenses/strata/LICENSE",
    "licenses/strata/ggml/LICENSE",
    "runtimes/linux-x64/engine/libstrata_kq.so",
    "runtimes/linux-x64/engine/strata",
    "runtimes/linux-x64/native/dulche-strata-candidate-worker",
    "runtimes/linux-x64/native/gguf_reader.py",
    "runtimes/linux-x64/native/strata-runtime-dependencies.json",
    "runtimes/linux-x64/native/strata_tokenizer.py",
    "runtimes/linux-x64/native/strata_tokenizer_helper.py",
    "strata-build-receipt.json",
    "strata-source-lock.json",
]
EXPECTED_CLOSURE_SET = set(EXPECTED_CLOSURE_PATHS)
EXPECTED_ROW_COUNT = len(EXPECTED_CLOSURE_PATHS)

# The three static ELF binaries to inspect (paths relative to the install closure root).
ELF_FILES = {
    "engine": pathlib.Path("runtimes/linux-x64/engine/strata"),
    "worker": pathlib.Path("runtimes/linux-x64/native/dulche-strata-candidate-worker"),
    "kq": pathlib.Path("runtimes/linux-x64/engine/libstrata_kq.so"),
}

MIT_NOTICE_PATHS = [
    "licenses/MIT-STRATA.txt",
    "licenses/MIT-GGML.txt",
    "licenses/strata/LICENSE",
    "licenses/strata/ggml/LICENSE",
]

REQUIRED_EVIDENCE_FILES = {
    "install-closure-inventory.json",
    "thunder-cuda-compile-evidence.json",
    "preflight-runner.json",
    "resource-limits.json",
}


class AuditError(Exception):
    """A fail-closed audit rejection."""


# ------------------------------------------------------------------ injectable I/O (tests)
class _StripAuthRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Redirect handler that NEVER forwards the Authorization header to a new URL."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if code in (301, 302, 303, 307, 308):
            newheaders = dict(req.headers)
            newheaders.pop("Authorization", None)
            return urllib.request.Request(
                newurl, headers=newheaders, origin_req_host=req.origin_req_host,
                unverifiable=True, method=req.get_method())
        return None


def _make_opener():
    return urllib.request.build_opener(_StripAuthRedirectHandler())


def _http_json(path: str, token: str = "", base: str = "https://api.github.com",
               opener=None) -> dict:
    """GET a bounded JSON object from the GitHub API path (read-only, no token on redirect)."""
    url = base.rstrip("/") + "/" + path.lstrip("/")
    headers = {"User-Agent": "9to1-thunder-artifact-audit", "Accept": "application/vnd.github+json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(url, headers=headers)
    if opener is None:
        opener = _make_opener()
    try:
        with opener.open(req, timeout=30) as resp:
            data = resp.read(MAX_API_RESPONSE_BYTES + 1)
            if len(data) > MAX_API_RESPONSE_BYTES:
                raise AuditError("GitHub API response too large for %s" % path)
            return json.loads(data.decode("utf-8"))
    except urllib.error.HTTPError as e:
        # Never echo the response body or token.
        raise AuditError("GitHub API HTTP %d for %s" % (e.code, path)) from e
    except urllib.error.URLError as e:
        raise AuditError("GitHub API network error for %s: %s" % (path, e.reason)) from e
    except (TimeoutError, OSError) as e:
        raise AuditError("GitHub API timeout/OS error for %s: %s" % (path, e)) from e


def _gh(inp, path: str) -> dict:
    fn = inp.get("_http_json", _http_json)
    return fn(path, inp.get("token", ""))


def _disk_free(inp, path) -> int:
    fn = inp.get("_disk_free")
    return fn(path) if fn is not None else shutil.disk_usage(str(path)).free


def _avail_mem(inp) -> Optional[int]:
    fn = inp.get("_avail_mem")
    if fn is not None:
        return fn()
    try:
        return os.sysconf("SC_AVPHYS_PAGES") * os.sysconf("SC_PAGE_SIZE")
    except (ValueError, OSError, AttributeError):
        return None


def _default_elf_runner(binary: pathlib.Path):
    """Run the static `readelf` reader; returns (returncode, stdout, stderr)."""
    if shutil.which("readelf") is None:
        raise AuditError("readelf unavailable (binutils missing) — audit BLOCKED")
    try:
        cp = subprocess.run(["readelf", "-h", "-d", "-l", str(binary)],
                            capture_output=True, text=True, timeout=READELF_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        raise AuditError("readelf timed out on %s" % binary.name) from None
    if len(cp.stdout) > READELF_OUTPUT_CAP or len(cp.stderr) > READELF_OUTPUT_CAP:
        raise AuditError("readelf output exceeded cap on %s" % binary.name)
    return cp.returncode, cp.stdout, cp.stderr


# ------------------------------------------------------------------ input validation
def load_inputs(env=None) -> dict:
    env = env if env is not None else os.environ
    return {
        "run_id": (env.get("AUDIT_BUILD_RUN_ID") or "").strip(),
        "evidence_artifact_id": (env.get("AUDIT_EVIDENCE_ARTIFACT_ID") or "").strip(),
        "install_artifact_id": (env.get("AUDIT_INSTALL_ARTIFACT_ID") or "").strip(),
        "expected_head_sha": (env.get("AUDIT_EXPECTED_HEAD_SHA") or "").strip().lower(),
        "repo": (env.get("GITHUB_REPOSITORY") or "").strip(),
        "token": (env.get("GITHUB_TOKEN") or "").strip(),
    }


def validate_inputs(inp: dict) -> List[str]:
    issues = []
    for key in ("run_id", "evidence_artifact_id", "install_artifact_id"):
        v = inp[key]
        # ASCII decimal only, positive integer; booleans (non-str) rejected.
        if not isinstance(v, str) or not v.isascii() or not DECIMAL_RE.fullmatch(v) or int(v) <= 0:
            issues.append("%s must be a positive ASCII decimal integer" % key)
    if not HEX40_RE.fullmatch(inp.get("expected_head_sha", "")):
        issues.append("expected_head_sha must be a 40-hexSHA")
    if not isinstance(inp.get("repo"), str) or not REPO_RE.fullmatch(inp.get("repo", "")):
        issues.append("GITHUB_REPOSITORY must be 'owner/name' with ASCII owner/name")
    return issues


# ------------------------------------------------------------------ GitHub metadata verification
def _require_positive_int(value, what):
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise AuditError("malformed/untrusted GitHub metadata: %s is not a positive integer" % what)
    return value


def _artifact_ok(inp, aid, want_name, run_meta, run_repo_id, run_head_repo_id, cap) -> List[str]:
    issues = []
    path = "repos/%s/actions/artifacts/%s" % (inp["repo"], aid)
    a = _gh(inp, path)
    if not isinstance(a, dict):
        issues.append("artifact %s metadata is not an object" % aid)
        return issues
    # Bind the returned artifact.id to the requested decimal input (true positive int).
    try:
        if _require_positive_int(a.get("id"), "artifact id") != int(aid):
            issues.append("artifact %s returned id does not match requested id" % aid)
    except AuditError as e:
        issues.append(str(e))
    if a.get("name") != want_name:
        issues.append("artifact %s name mismatch: %r != %r" % (aid, a.get("name"), want_name))
    if a.get("expired") is not False:
        issues.append("artifact %s is expired" % aid)
    size = a.get("size_in_bytes")
    if isinstance(size, bool) or not isinstance(size, int) or size <= 0:
        issues.append("artifact %s missing/zero size" % aid)
    elif size > cap:
        issues.append("artifact %s size %d exceeds cap %d" % (aid, size, cap))
    wr = a.get("workflow_run")
    if not isinstance(wr, dict):
        issues.append("artifact %s missing workflow_run" % aid)
        return issues
    try:
        wr_id = _require_positive_int(wr.get("id"), "artifact run id")
    except AuditError as e:
        issues.append(str(e))
        return issues
    if wr_id != _require_positive_int(run_meta.get("id"), "run id"):
        issues.append("artifact %s is not from build run %s" % (aid, run_meta.get("id")))
    if str(wr.get("head_sha", "")).lower() != inp["expected_head_sha"]:
        issues.append("artifact %s head_sha does not match expected build head" % aid)
    # Bind actual run repository identity into artifact workflow_run repo ids.
    if wr.get("repository_id") != run_repo_id:
        issues.append("artifact %s repository_id does not match run repository" % aid)
    if wr.get("head_repository_id") != run_head_repo_id:
        issues.append("artifact %s head_repository_id does not match run head repository" % aid)
    return issues


def verify_metadata(inp) -> dict:
    """Prove same repo/run/head, run SUCCESS, artifacts present/not expired/names+ids exact under cap."""
    issues = []
    issues += validate_inputs(inp)
    if issues:
        raise AuditError("input validation failed: " + "; ".join(issues))
    run_path = "repos/%s/actions/runs/%s" % (inp["repo"], inp["run_id"])
    run = _gh(inp, run_path)
    if not isinstance(run, dict):
        raise AuditError("metadata verification failed: run metadata is not an object")
    try:
        if _require_positive_int(run.get("id"), "run id") != int(inp["run_id"]):
            issues.append("run returned id does not match requested run id")
    except AuditError as e:
        issues.append(str(e))
    if run.get("status") != "completed":
        issues.append("run %s is not completed" % inp["run_id"])
    if run.get("conclusion") != "success":
        issues.append("run %s conclusion is not success: %r" % (inp["run_id"], run.get("conclusion")))
    if str(run.get("head_sha", "")).lower() != inp["expected_head_sha"]:
        issues.append("run head_sha does not match expected build head")
    # Same source repo: run.repository.id must equal run.head_repository.id.
    run_repo_id = (run.get("repository") or {}).get("id")
    run_head_repo_id = (run.get("head_repository") or {}).get("id")
    try:
        run_repo_id = _require_positive_int(run_repo_id, "run repository id")
        run_head_repo_id = _require_positive_int(run_head_repo_id, "run head repository id")
    except AuditError as e:
        issues.append(str(e))
        run_repo_id = run_head_repo_id = None
    if run_repo_id is not None and run_head_repo_id is not None and run_repo_id != run_head_repo_id:
        issues.append("run repository/head_repository identity mismatch")
    issues += _artifact_ok(inp, inp["evidence_artifact_id"], "thunder-cuda-compile-evidence",
                           run, run_repo_id, run_head_repo_id, EVIDENCE_CAP_BYTES)
    issues += _artifact_ok(inp, inp["install_artifact_id"], "thunder-cuda-compile-install-closure",
                           run, run_repo_id, run_head_repo_id, INSTALL_CAP_BYTES)
    if issues:
        raise AuditError("metadata verification failed: " + "; ".join(issues))
    return run


# ------------------------------------------------------------------ preflight (disk/memory/budget)
def preflight(inp) -> dict:
    run = verify_metadata(inp)

    def size_of(aid):
        a = _gh(inp, "repos/%s/actions/artifacts/%s" % (inp["repo"], aid))
        s = a.get("size_in_bytes")
        return int(s) if isinstance(s, int) and s > 0 else 0
    install_zip = size_of(inp["install_artifact_id"])
    evidence_zip = size_of(inp["evidence_artifact_id"])
    if install_zip > INSTALL_CAP_BYTES:
        raise AuditError("install artifact exceeds cap")
    if evidence_zip > EVIDENCE_CAP_BYTES:
        raise AuditError("evidence artifact exceeds cap")
    # extraction budget: compressed artifact bytes + up to full uncompressed extraction (install
    # 512 MiB cap + evidence 4 MiB cap) + slack for logs.
    budget = (install_zip + INSTALL_CAP_BYTES) + (evidence_zip + EVIDENCE_CAP_BYTES) \
        + PREFLIGHT_LOG_SLACK_BYTES
    free = _disk_free(inp, os.getcwd())
    if free < budget:
        raise AuditError("insufficient runner disk: need >= %d bytes, free %d" % (budget, free))
    mem = _avail_mem(inp)
    if mem is not None and mem < MIN_PREFLIGHT_MEM_BYTES:
        raise AuditError("insufficient effective memory")
    return {"run": int(inp["run_id"]), "installZipBytes": install_zip, "evidenceZipBytes": evidence_zip,
            "budgetBytes": budget, "diskFreeBytes": free}


# ------------------------------------------------------------------ closure/inventory validation
def _file_sha_stream(p) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        while True:
            b = f.read(CHUNK)
            if not b:
                break
            h.update(b)
    return h.hexdigest()


def _relpath_issues(rel) -> List[str]:
    if not isinstance(rel, str) or rel == "":
        return ["inventory row without path"]
    if "\\" in rel:
        return ["non-canonical path (backslash): " + rel]
    if rel.startswith("/"):
        return ["absolute inventory path: " + rel]
    parts = rel.split("/")
    if any(s in ("", ".") for s in parts):
        return ["empty/`.` component in inventory path: " + rel]
    if ".." in parts:
        return ["parent traversal in inventory path: " + rel]
    return []


def _scan_closure_files(install_dir: pathlib.Path):
    """Return (issues, rel->Path map). Rejects symlink root/parent/members and path escape FIRST."""
    issues = []
    root = install_dir.resolve()
    if install_dir.is_symlink():
        return ["install root is a symlink"], {}
    if not install_dir.is_dir():
        return ["install dir missing: " + str(install_dir)], {}
    mapping = {}
    for p in install_dir.rglob("*"):
        if p.is_symlink():
            issues.append("symlink in closure: " + str(p.relative_to(install_dir)))
            continue
        if not p.is_file() and not p.is_dir():
            issues.append("non-regular path in closure: " + str(p))
            continue
        if p.is_file():
            try:
                rel = p.relative_to(install_dir).as_posix()
            except ValueError:
                issues.append("path outside install root: " + str(p))
                continue
            r = p.resolve()
            if not str(r).startswith(str(root)):
                issues.append("path escape in closure: " + rel)
                continue
            if _relpath_issues(rel):
                issues.append("non-canonical closure path: " + rel)
                continue
            mapping[rel] = p
    return issues, mapping


def validate_closure(install_dir: pathlib.Path, inventory: list, mit_pins=None) -> List[str]:
    """Structural + canonical + exact-13 + size-cap + streaming-hash validation. MIT pins default to
    the production constants; tests may inject explicit constants to isolate structure (production
    default is never weakened)."""
    if mit_pins is None:
        mit_pins = {
            "licenses/MIT-STRATA.txt": (MIT_STRATA_SHA256, MIT_STRATA_BYTES),
            "licenses/strata/LICENSE": (MIT_STRATA_SHA256, MIT_STRATA_BYTES),
            "licenses/MIT-GGML.txt": (MIT_GGML_SHA256, MIT_GGML_BYTES),
            "licenses/strata/ggml/LICENSE": (MIT_GGML_SHA256, MIT_GGML_BYTES),
        }
    issues, actual = _scan_closure_files(install_dir)
    if issues:
        return issues
    # Exact 13-file set (no extra/missing/duplicate).
    actual_set = set(actual)
    for rel in sorted(actual_set - EXPECTED_CLOSURE_SET):
        issues.append("extra file not in expected closure: " + rel)
    for rel in sorted(EXPECTED_CLOSURE_SET - actual_set):
        issues.append("missing expected closure file: " + rel)
    # Cumulative uncompressed size cap BEFORE hashing any file; stop immediately if exceeded.
    cumulative = sum(p.stat().st_size for p in actual.values())
    if cumulative > INSTALL_CAP_BYTES:
        issues.append("cumulative closure bytes %d exceed cap %d" % (cumulative, INSTALL_CAP_BYTES))
        return issues
    if len(actual) != EXPECTED_ROW_COUNT:
        issues.append("closure file count %d != expected %d" % (len(actual), EXPECTED_ROW_COUNT))

    # First pass: structurally validate every inventory row BEFORE using it as a key/hash source.
    # Malformed rows fail fail-closed and are never allowed to raise or be hashed.
    valid_rows = []
    seen = {}
    for i, row in enumerate(inventory):
        if not isinstance(row, dict):
            issues.append("inventory row %d is not an object" % i)
            continue
        rel = row.get("path")
        if not isinstance(rel, str):
            issues.append("inventory row %d lacks a string path" % i)
            continue
        pissues = _relpath_issues(rel)
        if pissues:
            issues += pissues
            continue
        if rel in seen:
            issues.append("duplicate inventory row: " + rel)
            continue
        seen[rel] = i
        b = row.get("bytes")
        if isinstance(b, bool) or not isinstance(b, int) or b <= 0:
            issues.append("inventory row %s bytes is not a positive integer" % rel)
            continue
        sh = row.get("sha256")
        if not isinstance(sh, str) or not HEX64_RE.fullmatch(sh):
            issues.append("inventory row %s sha256 is not 64-hex" % rel)
            continue
        valid_rows.append((rel, b, sh))

    # Exactly 13 rows, and the inventory path set must equal the expected 13-files set.
    inv_set = {rel for rel, _, _ in valid_rows}
    if len(inventory) != EXPECTED_ROW_COUNT:
        issues.append("inventory row count %d != expected %d" % (len(inventory), EXPECTED_ROW_COUNT))
    for rel in sorted(EXPECTED_CLOSURE_SET - inv_set):
        issues.append("inventory missing expected path: " + rel)
    for rel in sorted(inv_set - EXPECTED_CLOSURE_SET):
        issues.append("inventory has unexpected path: " + rel)

    # Second pass: verify size and streamed hash of each valid inventory row against disk.
    for rel, b, sh in valid_rows:
        f = actual.get(rel)
        if f is None:
            issues.append("inventory row missing on disk: " + rel)
            continue
        try:
            actual_bytes = f.stat().st_size
        except OSError:
            issues.append("cannot stat closure file: " + rel)
            continue
        if b != actual_bytes:
            issues.append("size mismatch for %s: %r != %r" % (rel, b, actual_bytes))
        try:
            actual_sha = _file_sha_stream(f)
        except OSError:
            issues.append("cannot hash closure file: " + rel)
            continue
        if sh != actual_sha:
            issues.append("hash mismatch for %s" % rel)

    # MIT notices: enforce fixed production hashes AND duplicate-pair equality on actual file bytes.
    for rel, (want_sha, want_bytes) in mit_pins.items():
        f = actual.get(rel)
        if f is None:
            issues.append("MIT notice missing: " + rel)
            continue
        try:
            if f.stat().st_size != want_bytes:
                issues.append("MIT notice size mismatch: " + rel)
        except OSError:
            issues.append("cannot stat MIT notice: " + rel)
            continue
        try:
            if _file_sha_stream(f) != want_sha:
                issues.append("MIT notice hash mismatch (fixed pin): " + rel)
        except OSError:
            issues.append("cannot hash MIT notice: " + rel)
    # duplicate-pair equality (Strata pair identical, GGML pair identical)
    if "licenses/MIT-STRATA.txt" in actual and "licenses/strata/LICENSE" in actual:
        if _file_sha_stream(actual["licenses/MIT-STRATA.txt"]) != \
           _file_sha_stream(actual["licenses/strata/LICENSE"]):
            issues.append("MIT Strata duplicate notices differ")
    if "licenses/MIT-GGML.txt" in actual and "licenses/strata/ggml/LICENSE" in actual:
        if _file_sha_stream(actual["licenses/MIT-GGML.txt"]) != \
           _file_sha_stream(actual["licenses/strata/ggml/LICENSE"]):
            issues.append("MIT GGML duplicate notices differ")
    return issues


# ------------------------------------------------------------------ receipt + source-lock pin binding
def validate_receipt(install_dir: pathlib.Path, inventory: list, candidate_lock: pathlib.Path) -> List[str]:
    issues = []
    receipt_path = install_dir / "strata-build-receipt.json"
    if not receipt_path.is_file():
        return ["strata-build-receipt.json missing from install closure"]
    try:
        rec = json.loads(receipt_path.read_text())
    except (OSError, ValueError) as e:
        return ["receipt unparseable: %s" % e]
    if not isinstance(rec, dict):
        return ["receipt is not a JSON object"]
    if rec.get("profile") != CANDIDATE_PROFILE:
        issues.append("receipt profile %r != candidate" % rec.get("profile"))
    # target must be the actual builder-overwritten ELF check dict (Linux x64), not an assumed string.
    tgt = rec.get("target")
    if not isinstance(tgt, dict):
        issues.append("receipt target is not the actual builder ELF check dict")
    else:
        if tgt.get("targetSystem") != "linux-x64":
            issues.append("receipt target targetSystem is not linux-x64")
        if tgt.get("machine") != 62 or tgt.get("elfClass") != 64:
            issues.append("receipt target is not the ELF64 x86_64 check dict")
    # Installed source-lock must equal the checked-out immutable candidate lock, and receipt lockSha256
    # must equal the same value.
    source_lock = install_dir / "strata-source-lock.json"
    if not source_lock.is_file():
        issues.append("strata-source-lock.json missing from install closure")
    else:
        try:
            sl_sha = _file_sha_stream(source_lock)
        except OSError:
            sl_sha = None
            issues.append("cannot hash strata-source-lock.json")
        if sl_sha != CANDIDATE_LOCK_SHA256:
            issues.append("installed source-lock hash does not equal immutable candidate lock")
    if rec.get("lockSha256") != CANDIDATE_LOCK_SHA256:
        issues.append("receipt lockSha256 does not equal immutable candidate lock")
    if not candidate_lock.is_file():
        issues.append("candidate lock path missing: " + str(candidate_lock))
    elif _file_sha_stream(candidate_lock) != CANDIDATE_LOCK_SHA256:
        issues.append("candidate lock file hash does not equal expected pin")
    if rec.get("commit") != CANDIDATE_COMMIT:
        issues.append("receipt commit does not match verified pin")
    if rec.get("tree") != CANDIDATE_TREE:
        issues.append("receipt tree does not match verified pin")
    if rec.get("archiveSha256") != CANDIDATE_ARCHIVE_SHA256:
        issues.append("receipt archiveSha256 does not match verified pin")

    inv_by_path = {r.get("path"): r for r in inventory}
    binary_fields = {
        "engine": ("runtimes/linux-x64/engine/strata", "engineSha256", "engineBytes"),
        "worker": ("runtimes/linux-x64/native/dulche-strata-candidate-worker", "workerSha256", "workerBytes"),
        "kq": ("runtimes/linux-x64/engine/libstrata_kq.so", "kqDsoSha256", "kqDsoBytes"),
    }
    for name, (rel, hkey, lenkey) in binary_fields.items():
        inv = inv_by_path.get(rel, {})
        if rec.get(hkey) != inv.get("sha256"):
            issues.append("receipt %s hash missing/mismatched with inventory" % name)
        if rec.get(lenkey) != inv.get("bytes"):
            issues.append("receipt %s length missing/mismatched with inventory" % name)
    # helper closure: each helper must carry bytes AND sha256 matching inventory; runtimeDepsLockSha256
    # must equal the runtime-dependencies inventory hash.
    hc = rec.get("helperClosure")
    if not isinstance(hc, dict):
        issues.append("receipt helperClosure missing")
        hc = {}
    for helper in ("strata_tokenizer_helper.py", "strata_tokenizer.py", "gguf_reader.py",
                   "strata-runtime-dependencies.json"):
        rel = "runtimes/linux-x64/native/" + helper
        inv = inv_by_path.get(rel, {})
        entry = hc.get(helper)
        if not isinstance(entry, dict) or entry.get("sha256") != inv.get("sha256") \
                or entry.get("bytes") != inv.get("bytes"):
            issues.append("receipt helperClosure missing/mismatch: " + helper)
    if rec.get("runtimeDepsLockSha256") != inv_by_path.get(
            "runtimes/linux-x64/native/strata-runtime-dependencies.json", {}).get("sha256"):
        issues.append("receipt runtimeDepsLockSha256 does not match runtime-dependencies inventory")
    return issues


# ------------------------------------------------------------------ evidence cross-check
def validate_evidence(evidence_dir: pathlib.Path, inventory: list) -> List[str]:
    issues = []
    ep = evidence_dir / "thunder-cuda-compile-evidence.json"
    if not ep.is_file():
        return ["thunder-cuda-compile-evidence.json missing"]
    try:
        ev = json.loads(ep.read_text())
    except (OSError, ValueError) as e:
        return ["evidence unparseable: %s" % e]
    if not isinstance(ev, dict):
        return ["evidence is not a JSON object"]
    if ev.get("success") is not True:
        issues.append("evidence success is not true")
    if ev.get("engineVersion") != CANDIDATE_ENGINE_VERSION:
        issues.append("evidence engineVersion does not equal 'strata 0.1.47'")
    if ev.get("archiveSha256") != CANDIDATE_ARCHIVE_SHA256:
        issues.append("evidence archiveSha256 does not match verified pin")
    if ev.get("ggmlCommit") != GGML_COMMIT:
        issues.append("evidence ggmlCommit does not match verified pin")
    if ev.get("ggmlTree") != GGML_TREE:
        issues.append("evidence ggmlTree does not match verified pin")
    inv_by_path = {r.get("path"): r for r in inventory}
    for key, rel, lenkey in (
            ("engineSha256", "runtimes/linux-x64/engine/strata", "engineBytes"),
            ("workerSha256", "runtimes/linux-x64/native/dulche-strata-candidate-worker", "workerBytes")):
        inv = inv_by_path.get(rel, {})
        if ev.get(key) != inv.get("sha256"):
            issues.append("evidence %s does not match closure inventory" % key)
        if ev.get(lenkey) != inv.get("bytes"):
            issues.append("evidence %s does not match closure inventory" % lenkey)
    if ev.get("helperSha256") != inv_by_path.get(
            "runtimes/linux-x64/native/strata_tokenizer_helper.py", {}).get("sha256"):
        issues.append("evidence helperSha256 does not match closure inventory")
    return issues


# ------------------------------------------------------------------ static ELF inspection
def _parse_elf(stdout: str) -> dict:
    info = {"class": None, "machine": None, "type": None, "interpreter": None,
            "needed": [], "rpath": None, "runpath": None, "malformed": False,
            "libcudart13Needed": False, "otherCudaSonames": []}
    lines = stdout.splitlines()
    for ln in lines:
        s = ln.strip()
        if "Class:" in ln:
            info["class"] = s.split(":", 1)[1].strip()
        elif "Machine:" in ln:
            info["machine"] = s.split(":", 1)[1].strip()
        elif "Type:" in ln and info["type"] is None:
            info["type"] = s.split(":", 1)[1].strip()
    # interpreter: strip the "Requesting program interpreter:" label, keep just the path
    for ln in lines:
        m = re.search(r"Requesting program interpreter:\s*([^\]]+)\]", ln)
        if m:
            info["interpreter"] = m.group(1).strip()
    for ln in lines:
        if "(NEEDED)" in ln:
            m = re.search(r"\[([^\]]+)\]", ln)
            if m:
                info["needed"].append(m.group(1).strip())
        elif "(RPATH)" in ln:
            m = re.search(r"\[([^\]]+)\]", ln)
            if m:
                info["rpath"] = m.group(1).strip()
        elif "(RUNPATH)" in ln:
            m = re.search(r"\[([^\]]+)\]", ln)
            if m:
                info["runpath"] = m.group(1).strip()
    info["libcudart13Needed"] = "libcudart.so.13" in info["needed"]
    info["otherCudaSonames"] = sorted(
        n for n in info["needed"]
        if n != "libcudart.so.13" and (n.startswith("libcudart") or n.startswith("libcuda")
                                       or n.startswith("libnv")))
    if info["class"] is None or info["machine"] is None:
        info["malformed"] = True
    return info


def inspect_elf(binary: pathlib.Path, runner=None) -> dict:
    runner = runner if runner is not None else _default_elf_runner
    before = _file_sha_stream(binary)
    rc, stdout, stderr = runner(binary)
    if rc != 0:
        raise AuditError("readelf failed (rc %d) on %s" % (rc, binary.name))
    after = _file_sha_stream(binary)
    if before != after:
        raise AuditError("binary hash changed during static readelf: %s" % binary.name)
    info = _parse_elf(stdout)
    if info["malformed"]:
        raise AuditError("malformed readelf metadata for %s" % binary.name)
    info["hashBefore"] = before
    info["hashAfter"] = after
    info["stable"] = before == after
    return info


def audit_elfs(install_dir: pathlib.Path, inventory: list, runner=None) -> tuple:
    inv_by_path = {r.get("path"): r for r in inventory}
    out = {}
    issues = []
    for name, rel in ELF_FILES.items():
        rel_str = rel.as_posix()
        bp = install_dir / rel_str
        if not bp.is_file():
            out[name] = {"error": "missing binary"}
            issues.append("ELF binary missing: " + name)
            continue
        inv = inv_by_path.get(rel_str, {})
        try:
            info = inspect_elf(bp, runner=runner)
        except AuditError as e:
            out[name] = {"error": str(e)}
            issues.append("ELF inspection failed for %s: %s" % (name, e))
            continue
        info["inventorySha256"] = inv.get("sha256")
        info["inventoryBytes"] = inv.get("bytes")
        info["hashMatchesInventory"] = (info["hashBefore"] == inv.get("sha256"))
        if not info["hashMatchesInventory"]:
            issues.append("ELF %s hash does not match inventory" % name)
        out[name] = info
    return out, issues


def _libcudart_present(elfs: dict) -> bool:
    return any(info.get("libcudart13Needed") for info in elfs.values())


def _other_cuda_sonames(elfs: dict) -> List[str]:
    names = set()
    for info in elfs.values():
        names.update(info.get("otherCudaSonames", []))
    return sorted(names)


# ------------------------------------------------------------------ top-level audit
def _run_audit(inp, install_dir, evidence_dir, candidate_lock, http_json=_http_json,
               readelf_runner=None, report_dir=None) -> dict:
    # Preserve an injected transport; only default when the caller did not provide one.
    inp.setdefault("_http_json", http_json)
    issues = []
    verify_metadata(inp)  # raises AuditError on failure
    for name in REQUIRED_EVIDENCE_FILES:
        if not (evidence_dir / name).is_file():
            issues.append("evidence file missing: " + name)
    inv_path = evidence_dir / "install-closure-inventory.json"
    if inv_path.is_file():
        try:
            inventory = json.loads(inv_path.read_text())
        except (OSError, ValueError):
            issues.append("inventory unparseable")
            inventory = []
        if not isinstance(inventory, list):
            issues.append("inventory is not a JSON array")
            inventory = []
    else:
        issues.append("install-closure-inventory.json missing")
        inventory = []
    issues += validate_closure(install_dir, inventory)
    issues += validate_receipt(install_dir, inventory, candidate_lock)
    issues += validate_evidence(evidence_dir, inventory)
    elfs, elf_issues = audit_elfs(install_dir, inventory, runner=readelf_runner)
    issues += elf_issues
    libcudart = _libcudart_present(elfs)
    other_cuda = _other_cuda_sonames(elfs)
    status = "fail" if issues else "pass"
    report = {
        "schema": "thunder-artifact-audit/v1",
        "status": status,
        "run": inp["run_id"],
        "expectedHeadSha": inp["expected_head_sha"],
        "repo": inp["repo"],
        "evidenceArtifactId": inp["evidence_artifact_id"],
        "installArtifactId": inp["install_artifact_id"],
        "closure": {"expectedRows": EXPECTED_ROW_COUNT, "inventoryRows": len(inventory), "issues": issues},
        "elf": elfs,
        "deploymentPrecondition": {
            "libcudart13Needed": libcudart,
            "otherCudaSonames": other_cuda,
            "note": "if NEEDED libcudart.so.13 is present, this is a reported deployment precondition; "
                     "never auto-apt/driver-change. Static linkage inventory only."},
        "constraints": {"gpu": "NOT RUN", "driver": "NOT RUN", "model": "NOT RUN",
                        "promotion": "NONE", "runtimeReadiness": "NOT CLAIMED",
                        "staticDependencyInventory": True},
    }
    if report_dir is not None:
        report_dir.mkdir(parents=True, exist_ok=True)
        (report_dir / "audit-report.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


# ------------------------------------------------------------------ bounded failure report
def _write_failure_report(report_dir, inp, stage, error):
    if report_dir is None:
        return
    try:
        report_dir.mkdir(parents=True, exist_ok=True)
    except OSError:
        return
    report = {
        "schema": "thunder-artifact-audit/v1",
        "status": "fail",
        "stage": stage,
        "errorRedacted": str(error)[:512],
        "run": inp.get("run_id"),
        "expectedHeadSha": inp.get("expected_head_sha"),
        "repo": inp.get("repo"),
        "constraints": {"gpu": "NOT RUN", "driver": "NOT RUN", "model": "NOT RUN",
                        "promotion": "NONE", "runtimeReadiness": "NOT CLAIMED"},
    }
    try:
        (report_dir / "audit-report.json").write_text(json.dumps(report, indent=2) + "\n")
    except OSError:
        pass


# ------------------------------------------------------------------ CLI
def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="thunder-artifact-audit.py", description=__doc__)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--preflight", action="store_true")
    g.add_argument("--audit", action="store_true")
    args = ap.parse_args(argv)

    inp = load_inputs()
    base = pathlib.Path(os.environ.get("GITHUB_WORKSPACE", os.getcwd()))
    report_dir = base / "work/pc-worker/thunder-artifact-audit-report"
    install_dir = base / "work/pc-worker/thunder-artifact-audit/install"
    evidence_dir = base / "work/pc-worker/thunder-artifact-audit/evidence"
    candidate_lock = base / os.environ.get(
        "CANDIDATE_LOCK",
        "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata/prep/strata-v0.2.2-lock.candidate.json")

    try:
        if args.preflight:
            rec = preflight(inp)
            print(json.dumps(rec, indent=2))
            return 0
        # audit: re-verify metadata/budget, then validate the artifacts ALREADY fetched into fresh
        # scoped folders by actions/download-artifact@v4 (the sole transport). No second fetch here.
        preflight(inp)
        rec = _run_audit(inp, install_dir, evidence_dir, candidate_lock, report_dir=report_dir)
        print(json.dumps({"status": rec["status"], "closureIssues": len(rec["closure"]["issues"]),
                          "elf": {k: v.get("machine") for k, v in rec["elf"].items()},
                          "libcudart13Needed": rec["deploymentPrecondition"]["libcudart13Needed"],
                          "otherCudaSonames": rec["deploymentPrecondition"]["otherCudaSonames"]},
                         indent=2))
        return 0 if rec["status"] == "pass" else 2
    except AuditError as e:
        _write_failure_report(report_dir, inp, "preflight" if args.preflight else "audit", e)
        print("error: %s" % e, file=sys.stderr)
        return 2
    except Exception as e:  # noqa: BLE001 - fail-closed structured report, never raw token/body
        _write_failure_report(report_dir, inp, "preflight" if args.preflight else "audit", e)
        print("error: audit failed closed: %s" % e, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
