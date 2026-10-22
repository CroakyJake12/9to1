#!/usr/bin/env python3
"""Recover the existing ARM engine's exact bytes from retained immutable inputs.

This only repacks data on a disposable Actions runner. It does not install a
package, execute ARM code, download another model or change any runtime asset.
"""
from pathlib import Path
import argparse
import datetime
import gzip
import hashlib
import io
import json
import os
import runpy
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

HERE = Path(__file__).resolve().parent
RETENTION = runpy.run_path(str(HERE / "retain-immutable-artifacts.py"))
SPEC = "9to1 Workspace/Home/Source/Dulche/bundle/arm64-runtime-bundle-spec.json"
SPEC_SHA = "34c54539260061a1e9de1f2298f5b97a24ec73d21489e80ccd2439235c62c756"
DEB_NAME = "9to1-preview_0.1.0~lesson.preview.arm64.20261021.39698883180.1_arm64.deb"
DEB_BYTES = 689730252
DEB_SHA = "1dd9ebb78634cc5a7a7e64cb626f6180036900ca6f09d96e3b9c64e5d623c4a5"
SOURCE_NAME = "llama.cpp-5266f24da75dc449bd56cbed7addb9c8e4a6a73e-source.tar.gz"
SOURCE_BYTES = 37286769
SOURCE_SHA = "8d9637a726a8de93e9037b8ea938f463ae6e0bd825177173f3a2be4c52cc6f00"
INNER_NAME = "llamacpp-b10809-linux-arm64-bookworm.tar.gz"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def copy_verified(stream, destination, sha, size):
    count, actual = 0, hashlib.sha256()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with destination.open("xb") as target:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            count += len(chunk)
            if count > size:
                raise ValueError("Input exceeds its pinned byte count")
            actual.update(chunk)
            target.write(chunk)
    if count != size or actual.hexdigest() != sha:
        raise ValueError("Input byte identity mismatch: " + destination.name)


def download_artifact(artifact_id, directory):
    name, size, sha = RETENTION["ARTIFACTS"][artifact_id]
    opener = urllib.request.build_opener(RETENTION["SafeRedirect"]())
    api_path = "/actions/artifacts/" + str(artifact_id)
    with opener.open(RETENTION["request"](api_path), timeout=60) as response:
        metadata = json.load(response)
    RETENTION["validate_metadata"](metadata, artifact_id)
    target = directory / (str(artifact_id) + ".zip")
    with opener.open(RETENTION["request"](api_path + "/zip"), timeout=120) as response:
        copy_verified(response, target, sha, size)
    return target, metadata


def extract_exact_zip_member(archive, filename, target, sha, size):
    with zipfile.ZipFile(archive) as z:
        matches = [member for member in z.infolist() if Path(member.filename).name == filename]
        if len(matches) != 1:
            raise ValueError("Expected one exact original archive member: " + filename)
        member = matches[0]
        if member.file_size != size or member.is_dir() or Path(member.filename).is_absolute() \
                or ".." in Path(member.filename).parts or "\\" in member.filename:
            raise ValueError("Unsafe or mismatched original ZIP member")
        with z.open(member) as stream:
            copy_verified(stream, target, sha, size)


def recover_assets(archive, destination, spec):
    expected = {"usr/lib/haven/" + asset["installPath"]: asset for asset in spec["assets"]}
    wanted = set(expected)
    recovered = []
    for member in archive:
        name = member.name[2:] if member.name.startswith("./") else member.name
        if name not in wanted:
            continue
        if name not in expected:
            raise ValueError("Duplicate pinned runtime asset in original package")
        asset = expected.pop(name)
        path = Path(asset["path"])
        if not member.isfile() or path.is_absolute() or ".." in path.parts \
                or member.size != asset["expectedSize"]:
            raise ValueError("Wrong recovered runtime member kind or size")
        with archive.extractfile(member) as stream:
            target = destination / path
            copy_verified(stream, target, asset["expectedSha256"], asset["expectedSize"])
        target.chmod(0o755 if path.as_posix() == "bin/llama-server" else 0o644)
        recovered.append({"path": path.as_posix(), "bytes": target.stat().st_size,
                          "sha256": digest(target), "original_install_path": name})
    if expected:
        raise ValueError("Original package is missing pinned runtime assets: " + str(sorted(expected)))
    return recovered


def deterministic_inner(source, destination):
    with destination.open("xb") as raw, gzip.GzipFile(filename="", fileobj=raw, mode="wb", mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w|", format=tarfile.USTAR_FORMAT) as archive:
            for path in sorted(source.rglob("*")):
                if path.is_symlink():
                    raise ValueError("Recovered inner contains an unexpected entry")
                if path.is_dir():
                    continue
                if not path.is_file():
                    raise ValueError("Recovered inner contains an unexpected entry")
                member = tarfile.TarInfo(path.relative_to(source).as_posix())
                member.size, member.mtime = path.stat().st_size, 0
                member.uid = member.gid = 0
                member.mode = 0o755 if member.name == "bin/llama-server" else 0o644
                with path.open("rb") as stream:
                    archive.addfile(member, stream)


def recover(output):
    if os.environ.get("GITHUB_REPOSITORY") != RETENTION["REPOSITORY"] or os.environ.get("GITHUB_ACTIONS") != "true":
        raise ValueError("Only the same-repository disposable Actions environment is supported")
    if output.exists() or output.is_symlink():
        raise ValueError("Recovery needs a fresh private output directory")
    spec_file = Path(SPEC)
    if digest(spec_file) != SPEC_SHA:
        raise ValueError("Original unchanged runtime bundle specification identity mismatch")
    spec = json.loads(spec_file.read_text())
    output.mkdir(parents=True)
    if shutil.disk_usage(output).free < 2 * 1024**3:
        raise ValueError("Insufficient scratch space for bounded original archives")
    scratch, provider = output / "scratch", output / "provider"
    scratch.mkdir(); provider.mkdir()
    package_zip, package_metadata = download_artifact(12393340411, scratch)
    source_zip, source_metadata = download_artifact(12393058751, scratch)
    deb = scratch / DEB_NAME
    extract_exact_zip_member(package_zip, DEB_NAME, deb, DEB_SHA, DEB_BYTES)
    extract_exact_zip_member(source_zip, SOURCE_NAME, provider / SOURCE_NAME, SOURCE_SHA, SOURCE_BYTES)
    payload = scratch / "recovered-assets"
    with (output / "deb-read.stderr.log").open("w") as errors:
        process = subprocess.Popen(["dpkg-deb", "--fsys-tarfile", str(deb)], stdout=subprocess.PIPE, stderr=errors)
        try:
            with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
                assets = recover_assets(archive, payload, spec)
            if process.wait(timeout=120) != 0:
                raise RuntimeError("Reading the original package payload failed")
        finally:
            process.stdout.close()
            if process.poll() is None:
                process.terminate(); process.wait(timeout=10)
    inner = provider / INNER_NAME
    deterministic_inner(payload, inner)
    shutil.copyfile(payload / "evidence/engine-closure.json", provider / "engine-closure.json")
    receipt = {"schema": "9to1.exact-arm-provider-recovery/v1", "status": "CHECKPOINT_DATA_IDENTITY_ONLY",
               "at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
               "original_failed_application_run": RETENTION["RUN"], "original_package_source_head": RETENTION["HEAD"],
               "original_package": {"name": DEB_NAME, "bytes": DEB_BYTES, "sha256": DEB_SHA},
               "original_artifact_metadata": [package_metadata, source_metadata], "unchanged_bundle_spec_sha256": SPEC_SHA,
               "assets": assets, "inner_name": INNER_NAME, "inner_sha256": digest(inner),
               "source_archive_sha256": SOURCE_SHA, "new_runtime_assets": 0,
               "engine_or_model_execution": "NOT RUN", "native_application_acceptance": "NOT RUN",
               "release_ready": False}
    (provider / "provider-recovery.json").write_text(json.dumps(receipt, indent=2) + "\n")
    rows = [digest(path) + "  " + path.name for path in sorted(provider.iterdir()) if path.is_file()]
    (provider / "SHA256SUMS").write_text("\n".join(rows) + "\n")
    print(json.dumps(receipt, indent=2))
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    recover(parser.parse_args().output)
