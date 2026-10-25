"""Bind an owner-requested development TEST build; never mark a release READY.

Consumes the existing build/package/smoke receipts and actual files. No network,
credential handling, upload, install or native execution. This is metadata, not
an independent semantic review of source completeness or smoke assertions.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess

from installer_delivery import BASELINE

SCHEMA = "9to1.development-test-build/v1"


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def identity(path):
    path = Path(path)
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode), "Retained input must be a regular file")
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        after = os.fstat(stream.fileno())
    require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
            (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns), "Retained input changed")
    return {"bytes": after.st_size, "sha256": digest}


def load(path):
    require(identity(path)["bytes"] <= 2 * 1024 * 1024, "Oversized receipt")
    value = json.loads(Path(path).read_text())
    require(isinstance(value, dict), "Receipt must be an object")
    return value


def retained(path):
    return {"path": str(Path(path).absolute()), **identity(path)}


def make_manifest(package_receipt, build_receipt, smoke_receipt, scope_evidence,
                  native_receipt, source_receipt, drive_id, folder_id, limitations,
                  browser_source=None, engine_source=None):
    package, build, smoke, scope, native, source = [load(p) for p in
        (package_receipt, build_receipt, smoke_receipt, scope_evidence, native_receipt, source_receipt)]
    revision, patch = package.get("source_head"), package.get("source_patch_sha256")
    require(isinstance(revision, str) and re.fullmatch(r'[0-9a-f]{40}', revision) and
            isinstance(patch, str) and re.fullmatch(r'[0-9a-f]{64}', patch), "Exact source/patch identity required")
    require(package.get("package_status") == "ASSEMBLED_NOT_NATIVE_ACCEPTED" and
            package.get("architecture") == "arm64", "Expected existing ARM package receipt")
    actual_package = Path(package["package"])
    require(identity(actual_package) == {k: package[k] for k in ("bytes", "sha256")}, "Actual DEB differs from build receipt")
    fields = subprocess.check_output(["dpkg-deb", "--field", str(actual_package), "Package", "Version", "Architecture"], text=True, stderr=subprocess.PIPE)
    metadata = dict(line.split(": ", 1) for line in fields.splitlines())
    require(metadata.get("Architecture") == "arm64" and metadata.get("Package") == "9to1-preview", "Actual package name/architecture mismatch")
    require(build.get("status") == "PASS_BUILD_AND_PACKAGE_ONLY_NOT_NATIVE_APP_ACCEPTED" and
            build.get("source_head") == revision and build.get("source_patch_sha256") == patch and
            build.get("package") == package, "Build/package receipt source or result mismatch")
    stages = build.get("stages")
    require(isinstance(stages, list) and stages and all(stage.get("exit_code") == 0 for stage in stages), "Missing or failed normal build stage")
    require(smoke.get("source_revision") == revision and smoke.get("result") == "PASS" and
            smoke.get("exit_code") == 0 and isinstance(smoke.get("command"), list) and smoke["command"],
            "Successful exact-source basic owning smoke receipt required")
    tests = smoke.get("tests", {})
    require(type(tests.get("total")) is int and tests["total"] > 0 and tests.get("passed") == tests["total"] and
            tests.get("failed") == 0 and tests.get("skipped") == 0, "Basic smoke must contain actual passed tests")
    require(scope.get("source_revision") == revision and scope.get("status") == "ALL_REQUESTED_BOARDS_UI_APPLIED" and
            isinstance(scope.get("requirements"), list) and scope["requirements"] and
            isinstance(scope.get("reviewer"), str) and scope["reviewer"].strip(),
            "Root must retain its final all-thread Boards UI source review; no completeness inferred from a package")
    require(native.get("schema") == "9to1.rnote-native-stage/v1" and native.get("source_head") == revision and native.get("release_ready") is False and
            native.get("status") == "STAGED_STATICALLY_VERIFIED_NOT_RUNTIME_ACCEPTED", "Wrong native staging input")
    native_ref = build.get("rnote_native_stage", {})
    require(native_ref.get("sha256") == identity(native_receipt)["sha256"], "Native receipt is not the actual product build input")
    native_proof = package.get("rnote_native", {})
    require(native_proof and native_proof.get("status") == "PACKAGE_NATIVE_BYTES_AND_DEPENDENCIES_VERIFIED_NOT_EXECUTED" and
            native_proof.get("dependencies") == native.get("inspection", {}).get("dependencies") and
            native_proof.get("members", {}).get("opt/9to1-preview/libcakeos_canvas_rnote_poc.so") == native.get("inspection", {}).get("library"),
            "Package lacks its verified actual native drawing payload")
    native_source = native.get("corresponding_source", {})
    require(native_source.get("identity") == identity(native_source.get("path", "")), "Missing native corresponding-source/licence archive")
    require(source.get("source_commit") == revision, "Corresponding source belongs to another revision")
    source_file = Path(source_receipt).parent / source["archive"]
    require(source_file.parent.resolve() == Path(source_receipt).parent.resolve() and
            identity(source_file) == {k: source[k] for k in ("bytes", "sha256")}, "Missing/changed product corresponding source")
    supporting = {"product_source": retained(source_file), "rnote_source": retained(native_source["path"])}
    for label, required, path in (("browser_source", bool(package.get("bundled_browser")), browser_source),
                                  ("engine_source", bool(package.get("inference")), engine_source)):
        require(not required or path is not None, "Bundled component requires retained " + label)
        if path is not None:
            supporting[label] = retained(path)
    require(isinstance(drive_id, str) and drive_id.strip() and isinstance(folder_id, str) and folder_id.strip(), "Expected existing private destination IDs required")
    require(isinstance(limitations, list) and limitations and all(isinstance(v, str) and v.strip() for v in limitations), "Explicit remaining limitations required")
    return {"schema": SCHEMA, "created_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "status": "TEST_BUILD_NOT_ACCEPTED", "release_ready": False,
            "source_revision": revision, "source_patch_sha256": patch,
            "package": {"file_name": actual_package.name, **metadata, **retained(actual_package)},
            "inputs": {name: retained(path) for name, path in
                       (("package_receipt", package_receipt), ("build_receipt", build_receipt),
                        ("basic_smoke", smoke_receipt), ("all_boards_ui_review", scope_evidence),
                        ("rnote_receipt", native_receipt), ("source_receipt", source_receipt))},
            "supporting_files": supporting, "known_limitations": limitations,
            "acceptance": {"full_independent": "PAUSED", "ARM_application_runtime": "NOT RUN by manifest",
                           "Ubuntu26_GNOME": "NOT RUN", "installation": "NOT RUN", "normal_close": "NOT RUN by manifest"},
            "private_delivery": {"provider": "existing_owner_only_onedrive", "drive_id": drive_id,
                "parent_folder_id": folder_id, "preserve_startup_baseline": BASELINE, "status": "NOT RUN", "laptop_receipt": "NOT RUN",
                "requirements": ["Recheck exact existing folder identity and effective owner-only permissions immediately before upload",
                                 "Use supported file input/upload-session API with conflict=fail; no public artifact or sharing route",
                                 "Verify returned item/parent/bytes, effective permissions and authenticated raw SHA256 readback",
                                 "Retain every supporting source/licence file together with this package",
                                 "Actual Ubuntu26 ARM laptop must verify path/bytes/SHA/time in a new private destination and preserve original baseline",
                                 "Read-only coordinator status does not grant laptop build/transfer/invocation; require actual supported local execution"]}}


def write_new(path, value):
    path = Path(path)
    require(path.is_absolute() and not path.parent.is_symlink(), "Choose an absolute private output path")
    repository = Path(__file__).resolve().parents[2]
    require(repository not in path.resolve().parents, "Private manifest must remain outside the source repository")
    parent = path.parent.stat()
    require(parent.st_uid == os.getuid() and parent.st_mode & 0o077 == 0, "Output directory must be owner-only")
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("package-receipt", "build-receipt", "smoke-receipt", "scope-evidence", "native-receipt", "source-receipt", "out"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--drive-id", required=True)
    parser.add_argument("--folder-id", required=True)
    parser.add_argument("--limitation", action="append", required=True)
    parser.add_argument("--browser-source", type=Path)
    parser.add_argument("--engine-source", type=Path)
    args = vars(parser.parse_args())
    output = args.pop("out")
    args["limitations"] = args.pop("limitation")
    value = make_manifest(**args)
    write_new(output, value)
    print(json.dumps({"status": value["status"], "manifest": retained(output), "release_ready": False,
                      "private_delivery": "NOT RUN", "native_acceptance": "NOT RUN"}))


if __name__ == "__main__":
    main()
