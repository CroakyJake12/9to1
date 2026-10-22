#!/usr/bin/env python3
"""Verify an accepted private ARM artifact on the owner's laptop; never install it."""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import selectors
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import zipfile

REPOSITORY = "CroakyJake12/9to1"
REPOSITORY_ID = 1378313608
ROLES = {"package", "corresponding-source-and-evidence", "browser-sidecar", "upstream-engine-source"}
GATES = {"cui-conformance", "donor-licences", "visual-category", "packaged-workflows", "privacy-security-accessibility", "platform-performance", "cross-app", "independent-acceptance"}
QUALITY = {"Usability", "Product fit", "Functionality", "Integration", "Separation and authenticity", "User fit", "Refinement"}
BASELINE = {"file_name": "9to1-preview_0.1.0~internal.native16.20261021.39571247743.1+icu78.1_arm64.deb", "bytes": 62010524, "sha256": "91f302640f912d0ef86da2ce9d07809ca8b3f082c72a668dae9b9b1b56399fcf"}
MARGIN = 256 * 1024 * 1024
CHUNK = 1024 * 1024


class Blocked(Exception):
    pass


def require(condition, reason):
    if not condition:
        raise Blocked(reason)


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def identity(path):
    path = Path(path)
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode), "Required payload is not a regular file")
        h = hashlib.sha256()
        for chunk in iter(lambda: stream.read(CHUNK), b""):
            h.update(chunk)
        after = os.fstat(stream.fileno())
    require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) == (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns), "Payload changed during verification")
    return {"bytes": after.st_size, "sha256": h.hexdigest()}


def read_regular(path, maximum):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode) and before.st_size <= maximum, "Required metadata must be a bounded regular file")
        raw = stream.read(maximum + 1)
        after = os.fstat(stream.fileno())
    fields = ("st_dev", "st_ino", "st_size", "st_mtime_ns", "st_ctime_ns")
    require(len(raw) == before.st_size and all(getattr(before, key) == getattr(after, key) for key in fields), "Metadata changed while reading")
    return raw


def check_identity(actual, expected, label):
    require(all(actual.get(k) == expected.get(k) for k in ("bytes", "sha256")), label + " bytes/SHA256 mismatch")


def expected_identity(value):
    require(isinstance(value, dict) and type(value.get("bytes")) is int and value["bytes"] > 0, "Positive exact payload byte count required")
    require(isinstance(value.get("sha256"), str) and re.fullmatch(r"[0-9a-f]{64}", value["sha256"]), "Exact SHA256 required")


def member_name(value):
    require(isinstance(value, str) and value and "\\" not in value and not value.startswith("/"), "Unsafe artifact member")
    require(all(p not in ("", ".", "..") for p in value.split("/")), "Unsafe artifact member")
    return value


def validate_manifest(manifest):
    require(isinstance(manifest, dict), "Delivery manifest must be an object")
    require(manifest.get("schema") == "9to1.installer-delivery/v1", "Unsupported delivery manifest")
    require(manifest.get("repository") == REPOSITORY, "Only the existing private 9to1 repository is supported")
    require(re.fullmatch(r"[0-9a-f]{40}", manifest.get("source_revision", "")), "Exact source revision required")
    package = manifest.get("package", {})
    expected_identity(package)
    member_name(package.get("member"))
    require(Path(package["member"]).name == package.get("file_name") and package["file_name"].endswith("_arm64.deb"), "Exact ARM64 package basename required")
    require(package["file_name"] != BASELINE["file_name"], "The startup-only baseline must never be replaced")
    require(package.get("architecture") == "arm64" and package.get("name") and package.get("version"), "Exact DEB identity required")
    require(re.fullmatch(r"[0-9a-f]{64}", manifest.get("source_patch_sha256", "")), "Exact source patch identity required")
    acceptance = manifest.get("acceptance", {})
    require(isinstance(acceptance, dict), "Independent acceptance must be an object")
    require(acceptance.get("status") == "READY", "Root has not declared this exact package READY")
    require(acceptance.get("source_revision") == manifest["source_revision"] and acceptance.get("source_patch_sha256") == manifest["source_patch_sha256"] and acceptance.get("package_sha256") == package["sha256"], "Acceptance belongs to another source/package")
    require(acceptance.get("coordinator") and acceptance.get("reviewer") and acceptance.get("implementer") and acceptance["reviewer"] != acceptance["implementer"], "Independent acceptance identities required")
    for key, required in (("mandatory_gates", GATES), ("quality", QUALITY)):
        proof = acceptance.get(key, {})
        require(isinstance(proof, dict), "Mandatory acceptance must use named gate maps: " + key)
        require(required <= proof.keys() and all(proof[k] == "PASS" for k in required), "Mandatory acceptance incomplete: " + key)
    evidence_paths = [acceptance.get(k) for k in ("evidence_member", "evidence_file") if acceptance.get(k)]
    require(len(evidence_paths) == 1, "One retained independent acceptance member or adjacent file required")
    member_name(evidence_paths[0])
    expected_identity(acceptance.get("evidence", {}))
    artifacts = manifest.get("artifacts", [])
    require(isinstance(artifacts, list) and artifacts, "Pinned authenticated artifact set required")
    ids, roles = set(), set()
    for artifact in artifacts:
        expected_identity(artifact)
        require(type(artifact.get("id")) is int and artifact["id"] > 0 and artifact["id"] not in ids, "Unique real artifact IDs required")
        require(type(artifact.get("run_id")) is int and artifact["run_id"] > 0 and artifact.get("name"), "Exact artifact run/name required")
        require(re.fullmatch(r"[0-9a-f]{40}", artifact.get("workflow_head_sha", "")), "Exact artifact workflow revision required")
        require(isinstance(artifact.get("roles"), list) and set(artifact["roles"]) <= ROLES, "Unknown artifact role")
        roles.update(artifact["roles"])
        ids.add(artifact["id"])
    require(ROLES <= roles, "Package, corresponding source/evidence, browser sidecar and engine source must remain available together")
    require(sum("package" in a["roles"] for a in artifacts) == 1, "One exact package artifact required")
    require(sum("corresponding-source-and-evidence" in a["roles"] for a in artifacts) == 1, "One exact acceptance-evidence artifact required")
    require(manifest.get("target") == {"distribution": "ubuntu", "version": "26.04", "architecture": "arm64", "gnome_acceptance": "NOT RUN"}, "Target Ubuntu26 ARM64 and honest untested GNOME status required")
    return manifest


def load_manifest(path):
    require(not Path(path).is_symlink(), "Manifest must be a retained regular file")
    raw = read_regular(path, 1024 * 1024)
    manifest = validate_manifest(json.loads(raw))
    proof = None
    if manifest["acceptance"].get("evidence_file"):
        evidence = Path(path).parent / manifest["acceptance"]["evidence_file"]
        proof = read_regular(evidence, 1024 * 1024)
        check_identity({"bytes": len(proof), "sha256": hashlib.sha256(proof).hexdigest()}, manifest["acceptance"]["evidence"], "Retained independent acceptance file")
        validate_acceptance_bytes(manifest, proof)
    return manifest, hashlib.sha256(raw).hexdigest(), proof


def host_preflight():
    require(platform.machine().lower() in ("aarch64", "arm64"), "Run receiving commands on the actual ARM64 laptop, not the cloud x86 host")
    release = platform.freedesktop_os_release()
    require(release.get("ID") == "ubuntu" and release.get("VERSION_ID") == "26.04", "Receiving target must be the intended Ubuntu26.04 system")
    require(shutil.which("dpkg-deb"), "dpkg-deb is required for actual package metadata inspection")
    return {"machine": platform.machine(), "distribution": release["ID"], "version": release["VERSION_ID"], "gnome_acceptance": "NOT RUN"}


def baseline_preflight(path):
    path = Path(path)
    require(path.name == BASELINE["file_name"], "Choose the recorded startup-only baseline; do not replace it")
    actual = identity(path)
    check_identity(actual, BASELINE, "Preserved startup-only baseline")
    return {"path": str(path.absolute()), **actual}


def gh_environment():
    env = os.environ.copy()
    env.pop("GH_DEBUG", None)
    env.update(GH_PROMPT_DISABLED="1", GH_NO_UPDATE_NOTIFIER="1")
    return env


class GitHub:
    def metadata(self, endpoint):
        result = subprocess.run(["gh", "api", "--hostname", "github.com", endpoint], stdin=subprocess.DEVNULL, capture_output=True, timeout=60, env=gh_environment())
        require(result.returncode == 0, "Authenticated gh API read failed; confirm existing repository access with gh auth status")
        return json.loads(result.stdout)

    def download(self, artifact, destination):
        """Bounded streaming on the receiving laptop; never used as a cloud-tool size bypass."""
        endpoint = f"repos/{REPOSITORY}/actions/artifacts/{artifact['id']}/zip"
        with tempfile.TemporaryFile() as errors:
            process = subprocess.Popen(["gh", "api", "--hostname", "github.com", endpoint], stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=errors, env=gh_environment(), start_new_session=True)
            selector = selectors.DefaultSelector()
            selector.register(process.stdout, selectors.EVENT_READ)
            size, h, deadline = 0, hashlib.sha256(), time.monotonic() + 1800
            try:
                with Path(destination).open("xb") as output:
                    os.chmod(destination, 0o600)
                    while selector.get_map():
                        require(time.monotonic() < deadline, "Artifact download timed out; no delivery receipt issued")
                        for key, _ in selector.select(timeout=1):
                            data = os.read(key.fd, CHUNK)
                            if not data:
                                selector.unregister(key.fileobj)
                                continue
                            size += len(data)
                            require(size <= artifact["bytes"], "Download exceeds pinned archive size")
                            output.write(data)
                            h.update(data)
                    require(process.wait(timeout=30) == 0, "Authenticated artifact download failed")
                    output.flush()
                    os.fsync(output.fileno())
                check_identity({"bytes": size, "sha256": h.hexdigest()}, artifact, "Downloaded archive")
            finally:
                selector.close()
                process.stdout.close()
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=30)


def remote_preflight(manifest, client):
    repo = client.metadata("repos/" + REPOSITORY)
    require(repo.get("private") is True and repo.get("id") == REPOSITORY_ID and repo.get("full_name") == REPOSITORY, "Authenticated access to the existing private repository was not verified")
    for expected in manifest["artifacts"]:
        actual = client.metadata(f"repos/{REPOSITORY}/actions/artifacts/{expected['id']}")
        require(actual.get("expired") is False, "Authenticated artifact is expired/unavailable")
        expiry = dt.datetime.fromisoformat(actual.get("expires_at", "").replace("Z", "+00:00"))
        require(expiry.tzinfo is not None and expiry > dt.datetime.now(dt.timezone.utc), "Artifact retention has expired")
        require(actual.get("id") == expected["id"] and actual.get("name") == expected["name"] and actual.get("size_in_bytes") == expected["bytes"] and actual.get("digest") == "sha256:" + expected["sha256"], "Live artifact identity differs from accepted manifest")
        run = actual.get("workflow_run", {})
        require(run.get("id") == expected["run_id"] and run.get("head_sha") == expected["workflow_head_sha"] and run.get("repository_id") == REPOSITORY_ID, "Artifact workflow provenance differs from pinned identity")
    return {"repository": REPOSITORY, "repository_id": REPOSITORY_ID, "private": True, "authentication": "Existing gh credentials verified by actual private-repository and artifact reads"}


def prepare_destination(manifest, destination, baseline):
    destination = Path(destination).absolute()
    require(destination.is_dir() and not destination.is_symlink(), "Choose an existing private receiving directory")
    require(destination.stat().st_uid == os.getuid() and not destination.stat().st_mode & 0o077, "Receiving directory must be owned by this user with mode0700")
    require(destination.resolve() != Path(baseline).resolve().parent, "Use a separate new candidate directory; preserve the old baseline directory")
    required = sum(a["bytes"] for a in manifest["artifacts"]) + manifest["package"]["bytes"] + MARGIN
    require(shutil.disk_usage(destination).free >= required, "Insufficient receiving disk capacity for all archives, DEB and256MiB reserve")
    names = [a["id"] for a in manifest["artifacts"]]
    require(len(names) == len(set(names)), "Archive destination collision")
    return destination, required


def zip_member(archive, member, maximum):
    infos = [i for i in archive.infolist() if i.filename == member]
    require(len(infos) == 1, "Required archive member absent/duplicated: " + member)
    info = infos[0]
    require(not info.is_dir() and not stat.S_ISLNK(info.external_attr >> 16) and info.file_size <= maximum and not info.flag_bits & 1, "Invalid required archive member: " + member)
    return info


def read_json_member(path, member):
    with zipfile.ZipFile(path) as archive:
        info = zip_member(archive, member_name(member), 1024 * 1024)
        value = json.loads(archive.read(info))
        require(isinstance(value, dict), "Required JSON archive member must be an object")
        return value


def inspect_package(manifest, archives, stage):
    artifact = next(a for a in manifest["artifacts"] if "package" in a["roles"])
    path = archives[artifact["id"]]
    expected = manifest["package"]
    build = read_json_member(path, "package-receipt.json")
    require(build.get("source_head") == manifest["source_revision"] and build.get("source_patch_sha256") == manifest["source_patch_sha256"] and build.get("architecture") == "arm64" and PurePosixPath(build.get("package", "")).name == expected["file_name"], "Package build provenance does not match accepted source")
    check_identity(build, expected, "Package build receipt")
    output = Path(stage) / expected["file_name"]
    with zipfile.ZipFile(path) as archive:
        checksum = archive.read(zip_member(archive, "SHA256SUMS", 65536)).decode()
        require(checksum.strip() == expected["sha256"] + "  " + expected["file_name"], "Package checksum file differs")
        info = zip_member(archive, expected["member"], expected["bytes"])
        require(info.file_size == expected["bytes"], "Inner package size differs")
        with archive.open(info) as source, output.open("xb") as target:
            os.chmod(output, 0o600)
            shutil.copyfileobj(source, target, CHUNK)
            target.flush()
            os.fsync(target.fileno())
    check_identity(identity(output), expected, "Actual extracted DEB")
    fields = subprocess.run(["dpkg-deb", "--field", str(output), "Package", "Architecture", "Version"], capture_output=True, text=True, timeout=60)
    require(fields.returncode == 0, "Actual dpkg-deb package inspection failed")
    actual = dict(line.split(": ", 1) for line in fields.stdout.strip().splitlines())
    require(actual == {"Package": expected["name"], "Architecture": "arm64", "Version": expected["version"]}, "Actual DEB package/version/architecture differs")
    return output


def acceptance_evidence(manifest, archives, local_proof=None):
    artifact = next(a for a in manifest["artifacts"] if "corresponding-source-and-evidence" in a["roles"])
    expected = manifest["acceptance"]
    if expected.get("evidence_file"):
        require(local_proof is not None, "Adjacent accepted proof was not loaded")
        proof = local_proof
    else:
        with zipfile.ZipFile(archives[artifact["id"]]) as archive:
            proof = archive.read(zip_member(archive, expected["evidence_member"], 1024 * 1024))
    validate_acceptance_bytes(manifest, proof)


def validate_acceptance_bytes(manifest, proof):
    expected = manifest["acceptance"]
    check_identity({"bytes": len(proof), "sha256": hashlib.sha256(proof).hexdigest()}, expected["evidence"], "Independent acceptance evidence")
    verdict = json.loads(proof)
    require(isinstance(verdict, dict), "Independent verdict must be an object")
    require(verdict.get("status") == "READY" and verdict.get("source_revision") == manifest["source_revision"] and verdict.get("source_patch_sha256") == manifest["source_patch_sha256"] and verdict.get("package_sha256") == manifest["package"]["sha256"] and verdict.get("reviewer") == expected["reviewer"] and verdict.get("implementer") == expected["implementer"], "Retained independent verdict is not this READY candidate")
    require(verdict.get("mandatory_gates") == expected["mandatory_gates"] and verdict.get("quality") == expected["quality"], "Retained acceptance gate proof differs")


def publish_file(source, target):
    target = Path(target)
    if target.exists() or target.is_symlink():
        require(not target.is_symlink(), "Existing receiving target is a symlink")
        check_identity(identity(target), identity(source), "Existing target; never overwrite different bytes")
        return
    os.link(source, target, follow_symlinks=False)  # Atomic, same filesystem, refuses overwrite.


def receive(manifest, manifest_sha, destination, baseline, local_archives=None, client=None, local_proof=None):
    host = host_preflight()
    preserved = baseline_preflight(baseline)
    destination, required = prepare_destination(manifest, destination, baseline)
    auth = remote_preflight(manifest, client or GitHub()) if local_archives is None else {"authentication": "NOT CHECKED: owner-supplied local archives; pinned provider digests verified"}
    with tempfile.TemporaryDirectory(prefix=".9to1-receive-", dir=destination) as temporary:
        stage, archives = Path(temporary), {}
        for artifact in manifest["artifacts"]:
            staged = stage / ("artifact-" + str(artifact["id"]) + ".zip")
            if local_archives is None:
                (client or GitHub()).download(artifact, staged)
            else:
                source = Path(local_archives) / staged.name
                check_identity(identity(source), artifact, "Owner-downloaded archive")
                with source.open("rb") as incoming, staged.open("xb") as outgoing:
                    os.chmod(staged, 0o600)
                    shutil.copyfileobj(incoming, outgoing, CHUNK)
                    outgoing.flush()
                    os.fsync(outgoing.fileno())
            check_identity(identity(staged), artifact, "Staged archive")
            archives[artifact["id"]] = staged
        acceptance_evidence(manifest, archives, local_proof)
        package = inspect_package(manifest, archives, stage)
        check_identity(identity(baseline), preserved, "Startup-only baseline after staging")
        # Nothing becomes visible as a candidate until every required byte/proof passes.
        for source in [*archives.values(), package]:
            publish_file(source, destination / source.name)
        final = destination / package.name
        check_identity(identity(final), manifest["package"], "Final laptop DEB")
        check_identity(identity(baseline), preserved, "Startup-only baseline after delivery")
        receipt = {"schema": "9to1.installer-delivery-receipt/v1", "at": now(), "status": "DELIVERED_BYTES_VERIFIED", "manifest_sha256": manifest_sha, "source_revision": manifest["source_revision"], "package": {"path": str(final), **identity(final)}, "archives": [{"path": str(destination / p.name), **identity(destination / p.name)} for p in archives.values()], "preserved_startup_baseline": preserved, "host": host, "source_access": auth, "capacity_required_bytes": required, "installation": "NOT RUN", "GNOME_acceptance": "NOT RUN", "application_launch_chat_browser_close": "NOT RUN by delivery", "release_acceptance": "Unchanged from independently authorised input; delivery adds no native acceptance"}
        report = stage / (package.name + ".delivery.json")
        with report.open("xb") as stream:
            os.chmod(report, 0o600)
            stream.write((json.dumps(receipt, indent=2) + "\n").encode())
            stream.flush()
            os.fsync(stream.fileno())
        # A retry verifies existing bytes; use a separate immutable timestamped receipt.
        report_target = destination / (package.name + ".delivery-" + str(time.time_ns()) + ".json")
        publish_file(report, report_target)
        fd = os.open(destination, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(fd)
        finally:
            os.close(fd)
        return {**receipt, "receipt_path": str(report_target)}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("preflight", "receive", "verify-local"))
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--destination", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--archives", type=Path)
    args = parser.parse_args(argv)
    try:
        manifest, sha, local_proof = load_manifest(args.manifest)
        if args.command == "preflight":
            host = host_preflight()
            baseline = baseline_preflight(args.baseline)
            _, required = prepare_destination(manifest, args.destination, args.baseline)
            result = {"status": "PREFLIGHT_ONLY", "source_access": remote_preflight(manifest, GitHub()), "host": host, "baseline": baseline, "capacity_required_bytes": required, "delivery": "NOT RUN"}
        else:
            require(args.command != "verify-local" or args.archives is not None, "--archives required for verify-local")
            require(args.command != "receive" or args.archives is None, "receive uses authenticated GitHub, not a local archive override")
            result = receive(manifest, sha, args.destination, args.baseline, args.archives if args.command == "verify-local" else None, local_proof=local_proof)
        print(json.dumps(result, indent=2))
        return 0
    except (Blocked, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile, subprocess.SubprocessError, KeyboardInterrupt) as error:
        receipt = {"at": now(), "status": "BLOCKED", "reason": str(error) or "Interrupted", "delivery": "NOT CONFIRMED", "installation": "NOT RUN", "GNOME_acceptance": "NOT RUN"}
        destination = args.destination
        try:
            if destination.is_dir() and not destination.is_symlink() and destination.stat().st_uid == os.getuid() and not destination.stat().st_mode & 0o077:
                failure = destination / ("delivery-BLOCKED-" + str(time.time_ns()) + ".json")
                fd = os.open(failure, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
                with os.fdopen(fd, "w") as stream:
                    json.dump(receipt, stream, indent=2)
                    stream.write("\n")
                    stream.flush()
                    os.fsync(stream.fileno())
                receipt["failure_receipt_path"] = str(failure.absolute())
        except OSError:
            pass
        print(json.dumps(receipt))
        return 2


if __name__ == "__main__":
    sys.exit(main())
