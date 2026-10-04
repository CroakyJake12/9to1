"""Bind the current ordinary Mail five-project graph before any runner TLS change."""
import hashlib
import json
import os
from pathlib import Path
import subprocess


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args])


def verify_current_cut(root, manifest_path, manifest_sha, expected_commit):
    root = Path(root).resolve()
    manifest = root / manifest_path
    if manifest_path != ".github/validation/team-c-mail72-trusted-greenmail.json":
        raise RuntimeError("Unexpected current Mail catalog path")
    if manifest.is_symlink() or digest(manifest.read_bytes()) != manifest_sha:
        raise RuntimeError("Current Mail catalog bytes differ")
    if git(root, "rev-parse", "HEAD").decode().strip() != expected_commit or os.environ.get("GITHUB_SHA") != expected_commit:
        raise RuntimeError("Exact selected workflow source differs")
    cut = json.loads(manifest.read_bytes())
    subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor", cut["sourceBasis"], expected_commit], check=True)
    changed = set(git(root, "diff", "--name-only", "-z", cut["sourceBasis"], expected_commit).decode().strip("\0").split("\0")) - {""}
    if not changed <= set(cut["harnessPaths"]):
        raise RuntimeError("Undeclared production or policy change after reviewed Mail source")
    if git(root, "status", "--porcelain"):
        raise RuntimeError("Current source checkout has changes")
    tree = {}
    for entry in git(root, "ls-tree", "-rz", "HEAD").split(b"\0"):
        if entry:
            meta, path = entry.split(b"\t", 1)
            mode, kind, blob = meta.decode().split()
            tree[path.decode()] = (mode, kind, blob)
    seen = set()
    rows = []
    for pin in cut["sourcePins"]:
        relative = Path(pin["path"])
        if relative.is_absolute() or ".." in relative.parts or pin["path"] in seen:
            raise RuntimeError("Invalid or duplicate current source path")
        seen.add(pin["path"])
        physical = root / relative
        if any((root / Path(*relative.parts[:i])).is_symlink() for i in range(1, len(relative.parts) + 1)) or not physical.is_file() or not physical.resolve().is_relative_to(root):
            raise RuntimeError("Current source body is absent or linked")
        data = physical.read_bytes()
        blob = hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()
        if tree.get(pin["path"]) != (pin["mode"], "blob", pin["gitBlob"]) or blob != pin["gitBlob"] or len(data) != pin["bytes"] or digest(data) != pin["sha256"]:
            raise RuntimeError("Whole current Mail source body differs")
        rows.append(pin)
    projects = cut["ordinaryProjects"]
    if len(projects) != len(set(projects)) or len(projects) != 5 or not set(projects) <= seen:
        raise RuntimeError("Actual five-project Mail closure is incomplete")
    for project in projects:
        import xml.etree.ElementTree as ET
        for ref in ET.parse(root / project).iter("ProjectReference"):
            value = ref.attrib["Include"].replace("\\", "/")
            if "$(" in value or ";" in value:
                raise RuntimeError("Unreviewed dynamic project reference")
            target = ((root / project).parent / value).resolve()
            if not target.is_relative_to(root) or target.relative_to(root).as_posix() not in projects:
                raise RuntimeError("Current Mail project reference falls outside closure")
    if cut["requiredBuildGitlinks"]:
        raise RuntimeError("This managed Mail graph has no source-gitlink build dependency")
    return cut, rows


def require_disposable_runner():
    if os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted" or os.environ.get("RUNNER_OS") != "Linux" or not hasattr(os, "pidfd_open") or not Path("/proc/self/stat").is_file():
        raise RuntimeError("Reviewed disposable GitHub-hosted Linux runner required")


def owned_output(root):
    raw = os.environ.get("TEAM_C_MAIL_OUTPUT", "")
    runner = os.environ.get("RUNNER_TEMP", "")
    if not raw or not runner:
        raise RuntimeError("Owned disposable runner output is missing")
    output, runner = Path(raw).resolve(), Path(runner).resolve()
    if output == runner or not output.is_relative_to(runner) or output.is_relative_to(Path(root).resolve()):
        raise RuntimeError("Output must be an owned child of runner temp outside source")
    return output


def collect_public(output, source_commit, secret, fixture_summary):
    """Copy only declared original diagnostics after exact secret/key refusal."""
    output = Path(output)
    raw = output / "diagnostics"
    public = output / "public-diagnostics"
    secrets = (secret,) if isinstance(secret, str) else tuple(secret)
    if public.exists() or not secrets or any(not isinstance(value, str) or not value for value in secrets) or raw.is_symlink() or not raw.is_dir():
        raise RuntimeError("Fresh declared public diagnostics required")
    public_signatures = {"mail-owned-public-ca.pem", "mail-owned-public-server.pem", "mail-owned-public-crl.pem", "mail-owned-public-crl.der"}
    exact = {"normal-mail72-result.json", "source-before.json", "source-after.json", "commands.json", "restore-assets.json", "runtime-before.json", "runtime-after.json", "evaluated-inputs.json", "whole-discovery.json", "whole-trx-readback.json", "mail-actual-strict-tls-readiness.json", "mail-maintained-server-producer.json", "mail-java-version.txt", "mail-original-public-crl-distribution.json", "mail-maintained-release-tag.json"} | public_signatures
    logs = {"sdk-version.log", "restore.log", "build.log", "whole-discovery.log", "whole-unfiltered-mail72.log"}
    rows = []
    bodies = []
    for path in sorted(raw.rglob("*")):
        if path.is_symlink():
            raise RuntimeError("Linked diagnostic refused")
        if not path.is_file():
            continue
        relative = path.relative_to(raw).as_posix()
        allowed = relative in exact or relative in logs or (path.parent == raw and path.name.startswith("evaluated-project-") and path.suffix in (".json", ".log")) or relative == "test-results/mail72.trx" or (len(Path(relative).parts) == 2 and Path(relative).parts[0] in {"mail-original-java-session", "mail-original-caller-session", "mail-original-crl-session"} and path.suffix == ".json")
        if not allowed:
            continue
        data = path.read_bytes()
        if any(value.encode() in data for value in secrets) or any(marker in data for marker in (b"-----BEGIN PRIVATE KEY-----", b"-----BEGIN RSA PRIVATE KEY-----", b"-----BEGIN ENCRYPTED PRIVATE KEY-----")):
            raise RuntimeError("Private credential or key bytes refuse public diagnostics")
        rows.append({"path": relative, "bytes": len(data), "sha256": digest(data)})
        bodies.append((path, relative, data))
    if fixture_summary.get("fullScopedPass") and not public_signatures <= {row["path"] for row in rows}:
        raise RuntimeError("Full scoped pass requires original public certificate and CRL signature bodies")
    if sum(row["bytes"] for row in rows) > 24 * 1024 * 1024:
        raise RuntimeError("Declared public original diagnostic budget exceeded")
    public.mkdir(mode=0o700)
    for path, relative, data in bodies:
        target = public / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        if digest(target.read_bytes()) != digest(data):
            raise RuntimeError("Original public copy differs")
    (public / "fixture-final-summary.json").write_text(json.dumps(fixture_summary, indent=2) + "\n")
    (public / "artifact-index.json").write_text(json.dumps({"sourceCommit": source_commit, "runId": os.environ.get("GITHUB_RUN_ID"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"), "originalFiles": rows, "generatedSummary": "fixture-final-summary.json", "credentialAndPrivateKeyRefusalPassed": True, "privateServerLogsKeysCacheFixtureStateExcluded": True, "qualification": "Original scoped managed Mail72 diagnostics only; no graphical native/Home/package/Windows/full release acceptance."}, indent=2) + "\n")
    return public
