#!/usr/bin/env python3
"""Verify and optionally extract a sealed Files WebAssembly candidate offline.

This is artifact custody, not Google OAuth, live Files acceptance or deployment.
No network, credentials or production endpoints are used by this program.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import zipfile
import zlib

MAX_PUBLIC = 128 * 1024 * 1024
MAX_MANIFEST = 8 * 1024 * 1024
MAX_SEAL = 64 * 1024
MAX_MEMBERS = 2048
REQUIRED = {"index.html", "main.js", "_framework/dotnet.js", "_framework/avalonia.js", "_framework/storage.js"}
OUTER = {"seal.json", "publish-manifest.json", "wwwroot.zip"}
COMMIT = re.compile(r"[0-9a-f]{40}\Z")
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
PRIVATE_DIRS = {".git", ".nuget", "node_modules", "obj", "fixture-data", "profiles"}
PRIVATE_SUFFIXES = {".nupkg", ".pfx", ".p12", ".pem", ".key"}


class RejectedCandidate(ValueError):
    """An artifact failed the fail-closed distribution gate."""


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise RejectedCandidate(reason)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def safe_name(name: str) -> bool:
    if not isinstance(name, str) or not name or "\\" in name or "\0" in name or ":" in name:
        return False
    path = PurePosixPath(name)
    if path.is_absolute() or path.as_posix() != name or any(part in {"", ".", ".."} for part in name.split("/")):
        return False
    if len(name) > 512 or any(ord(ch) < 32 or ord(ch) == 127 for ch in name):
        return False
    lower = name.lower()
    parts = lower.split("/")
    return not (any(part in PRIVATE_DIRS for part in parts) or
                any(PurePosixPath(part).suffix in PRIVATE_SUFFIXES or part.startswith(".env") for part in parts))


def verify_members(archive: zipfile.ZipFile, *, max_members: int, max_total: int, outer: bool = False) -> dict[str, zipfile.ZipInfo]:
    members = archive.infolist()
    require(0 < len(members) <= max_members, "Archive member count outside bounds")
    names: dict[str, zipfile.ZipInfo] = {}
    total = 0
    for entry in members:
        name = entry.filename
        require(safe_name(name), f"Unsafe or private archive path: {name!r}")
        require(not entry.is_dir(), f"Directory entry not permitted: {name!r}")
        require((entry.flag_bits & 1) == 0, f"Encrypted archive member: {name!r}")
        mode = entry.external_attr >> 16
        require(stat.S_IFMT(mode) in {0, stat.S_IFREG}, f"Nonregular archive member: {name!r}")
        require(name not in names, f"Duplicate archive member: {name!r}")
        require(0 <= entry.file_size <= max_total - total, "Archive exceeds uncompressed size ceiling")
        total += entry.file_size
        names[name] = entry
    if outer:
        require(set(names) == OUTER, "Outer GitHub artifact must contain exactly the three canonical seal files")
        require(names["seal.json"].file_size <= MAX_SEAL, "Oversized seal")
        require(names["publish-manifest.json"].file_size <= MAX_MANIFEST, "Oversized manifest")
        require(names["wwwroot.zip"].file_size <= MAX_PUBLIC, "Oversized public ZIP")
    return names


def digest_stream(stream) -> tuple[str, int]:
    digest = hashlib.sha256()
    total = 0
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
        total += len(block)
    return digest.hexdigest(), total


def verify(outer_path: Path, expected_commit: str, *, extract: Path | None = None) -> dict:
    require(bool(COMMIT.fullmatch(expected_commit)), "Expected source revision must be a full lowercase 40-hex Git SHA")
    require(outer_path.is_file() and not outer_path.is_symlink(), "Candidate ZIP must be a regular file")
    require(0 < outer_path.stat().st_size <= MAX_PUBLIC + MAX_MANIFEST + MAX_SEAL + (1024 * 1024),
            "Candidate ZIP size outside bounds")
    try:
        with zipfile.ZipFile(outer_path) as outer:
            om = verify_members(outer, max_members=3, max_total=MAX_PUBLIC + MAX_MANIFEST + MAX_SEAL, outer=True)
            seal_bytes = outer.read(om["seal.json"])
            manifest_bytes = outer.read(om["publish-manifest.json"])
            try:
                seal = json.loads(seal_bytes)
                receipt = json.loads(manifest_bytes)
            except (UnicodeError, ValueError) as e:
                raise RejectedCandidate("Seal or manifest JSON could not be parsed") from e
            require(isinstance(seal, dict) and isinstance(receipt, dict), "Invalid seal or manifest root")
            for key in ("receiptSha256", "zipSha256"):
                require(isinstance(seal.get(key), str) and DIGEST.fullmatch(seal[key]), f"Invalid seal digest: {key}")
            require(seal.get("sourceCommit") == expected_commit, "Seal source differs from requested source")
            require(receipt.get("sourceCommit") == expected_commit and receipt.get("sourceCommitAfter") == expected_commit,
                    "Manifest source custody differs from requested source")
            require(receipt.get("exitCode") == 0 and not receipt.get("trackedChangesAfter") and
                    not receipt.get("addedSourceInputs") and not receipt.get("removedSourceInputs") and
                    not receipt.get("changedSourceInputs"), "The source was not clean across the published build")
            require(sha256(manifest_bytes) == seal["receiptSha256"], "Manifest SHA-256 differs from sealed receipt")
            zip_bytes = outer.read(om["wwwroot.zip"])
            require(len(zip_bytes) == seal.get("zipBytes") and sha256(zip_bytes) == seal["zipSha256"],
                    "Public ZIP size or SHA-256 differs from seal")
            rows = receipt.get("publishFiles")
            require(isinstance(rows, list) and 0 < len(rows) <= MAX_MEMBERS, "Manifest has no bounded public files")
            require(seal.get("fileCount") == receipt.get("fileCount") == len(rows), "File count custody mismatch")
            expected: dict[str, tuple[int, str]] = {}
            total = 0
            for row in rows:
                require(isinstance(row, dict), "Manifest row must be an object")
                name, size, digest = row.get("path"), row.get("bytes"), row.get("sha256")
                require(safe_name(name), f"Unsafe manifest file path: {name!r}")
                require(name not in expected, f"Duplicate manifest file: {name}")
                require(type(size) is int and 0 <= size <= MAX_PUBLIC - total, "Invalid file size or aggregate ceiling")
                require(isinstance(digest, str) and DIGEST.fullmatch(digest), f"Invalid file digest: {name}")
                expected[name] = size, digest
                total += size
            require(receipt.get("totalBytes") == seal.get("totalBytes") == total,
                    "Public file byte totals differ from seal and receipt")
            require(REQUIRED.issubset(expected), "Browser bootstrap is incomplete")
            require(any(n.startswith("_framework/") and "dotnet.native" in n and n.endswith(".wasm") for n in expected),
                    "Actual runtime WebAssembly binary is missing")
            with zipfile.ZipFile(io.BytesIO(zip_bytes)) as bundle:
                entries = verify_members(bundle, max_members=MAX_MEMBERS, max_total=MAX_PUBLIC)
                require(set(entries) == set(expected), "ZIP members differ from the full published-file manifest")
                for name, entry in entries.items():
                    size, digest = expected[name]
                    require(entry.file_size == size, f"Public file size differs: {name}")
                    with bundle.open(entry) as stream:
                        observed, length = digest_stream(stream)
                    require(length == size and observed == digest, f"Public file contents differ: {name}")
                if extract is not None:
                    safely_extract(bundle, expected, extract)
    except (zipfile.BadZipFile, zlib.error, RuntimeError, OSError, EOFError, UnicodeError) as error:
        raise RejectedCandidate(f"Candidate archive could not be safely verified: {type(error).__name__}") from error
    return {
        "status": "PASS", "sourceCommit": expected_commit, "publicFilesVerified": len(expected),
        "publicBytesVerified": total, "publicZipSha256": seal["zipSha256"],
        "manifestSha256": seal["receiptSha256"],
        "extractedTo": str(extract) if extract is not None else None,
        "artifactIntegrityVerified": True,
        "browserStartupVerifiedByThisTool": False,
        "googleOAuthVerifiedByThisTool": False,
        "liveDriveAccessVerifiedByThisTool": False,
        "productionDeploymentVerifiedByThisTool": False,
        "readyToUseAsRealDrive": False,
    }


def safely_extract(bundle: zipfile.ZipFile, expected: dict[str, tuple[int, str]], destination: Path) -> None:
    parent = destination.parent
    require(parent.is_dir() and not parent.is_symlink(), "Extraction parent must exist and not be a symlink")
    # mkdir(exist_ok=False) is exclusive; a check-then-rename would be able to
    # replace an empty directory another process created after the check.
    try:
        destination.mkdir(mode=0o700, exist_ok=False)
    except FileExistsError as error:
        raise RejectedCandidate("Extraction destination already exists") from error
    try:
        for name, (size, digest) in expected.items():
            target = destination.joinpath(*name.split("/"))
            target.parent.mkdir(parents=True, exist_ok=True)
            with bundle.open(name) as stream, target.open("xb") as output:
                for block in iter(lambda: stream.read(1024 * 1024), b""):
                    output.write(block)
            require(target.stat().st_size == size and file_sha256(target) == digest,
                    f"Extracted file differs after write: {name}")
    except BaseException:
        # Only our exclusive directory was created. Never remove a destination
        # that existed before invocation, even when extraction fails.
        shutil.rmtree(destination)
        raise


def file_sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return digest_stream(stream)[0]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("artifact", type=Path, help="GitHub Actions files-web-candidate ZIP")
    parser.add_argument("--expected-commit", required=True, help="40-character remote main commit of the compiled candidate")
    parser.add_argument("--extract", type=Path, help="Fresh destination for verified WWWROOT files; never overwrites")
    args = parser.parse_args(argv)
    try:
        result = verify(args.artifact, args.expected_commit, extract=args.extract)
    except RejectedCandidate as error:
        print(json.dumps({"status": "FAIL", "reason": str(error), "readyToUseAsRealDrive": False}), file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
