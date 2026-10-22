#!/usr/bin/env python3
"""Reuse only a byte-verified package for the exact same candidate and runner inputs.

This cache saves compilation/packaging work; all installed-package/native gates
still execute. A cache hit never constitutes product or release acceptance.
"""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os


def identity(environment):
    keys = ("GITHUB_SHA", "NATIVE_DOTNET_VERSION", "PIPELINE_ENVIRONMENT_SHA")
    values = {key: environment[key] for key in keys}
    if len(values["GITHUB_SHA"]) != 40 or len(values["PIPELINE_ENVIRONMENT_SHA"]) != 64:
        raise ValueError("Immutable source and environment fingerprints are required")
    return values


def validate_package(directory, expected_head):
    for parent in [directory, *directory.parents]:
        if parent.is_symlink():
            raise ValueError("Package cache path must not contain symlinks")
    packages = list(directory.glob("*.deb"))
    if len(packages) != 1:
        raise ValueError("Expected exactly one immutable package")
    package, receipt_path = packages[0], directory / "package-receipt.json"
    if package.is_symlink() or receipt_path.is_symlink() or not package.is_file() or not receipt_path.is_file():
        raise ValueError("Package cache inputs must be regular files")
    receipt = json.loads(receipt_path.read_text())
    if receipt.get("source_head") != expected_head or receipt.get("architecture") != "arm64":
        raise ValueError("Cached package belongs to a different source or platform")
    with package.open("rb") as stream:
        sha = hashlib.file_digest(stream, "sha256").hexdigest()
    if sha != receipt.get("sha256") or package.stat().st_size != receipt.get("bytes"):
        raise ValueError("Cached package byte identity differs from its original receipt")
    return package, sha


def operate(directory, mode, environment):
    inputs = identity(environment)
    package, sha = validate_package(directory, inputs["GITHUB_SHA"])
    metadata_path = directory / "immutable-cache-origin.json"
    if mode == "record":
        if metadata_path.exists() or metadata_path.is_symlink():
            raise ValueError("Refuse overwriting an immutable package cache origin")
        origin = {"schema": "9to1.immutable-arm-package-cache/v1", "inputs": inputs,
                  "producer_run": environment["GITHUB_RUN_ID"], "package_sha256": sha,
                  "scope": "BUILD_PACKAGE_AND_INSTALL_ONLY; no native acceptance inferred"}
        metadata_path.write_text(json.dumps(origin, indent=2) + "\n")
    else:
        if metadata_path.is_symlink():
            raise ValueError("Cache origin must be a regular file")
        origin = json.loads(metadata_path.read_text())
        if origin.get("schema") != "9to1.immutable-arm-package-cache/v1" or origin.get("inputs") != inputs or origin.get("package_sha256") != sha:
            raise ValueError("Cached package provenance or dependency inputs differ")
        with Path(environment["GITHUB_ENV"]).open("a") as stream:
            for key, value in {"PREVIEW_DEB": str(package), "PREVIEW_PACKAGE_DIR": str(directory)}.items():
                if "\n" in value or "\r" in value:
                    raise ValueError("Invalid package cache path")
                stream.write(key + "=" + value + "\n")
    result = {"at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "status": "CHECKPOINT", "mode": mode, "origin": origin,
              "package": str(package), "sha256": sha, "bytes": package.stat().st_size,
              "native_application_acceptance": "NOT RUN", "owner_laptop": "NOT RUN",
              "release_ready": False}
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--mode", choices=("record", "restore"), required=True)
    args = parser.parse_args()
    result = operate(args.directory, args.mode, os.environ)
    output = Path("evidence/immutable-package-cache-" + args.mode + ".json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2) + "\n")
