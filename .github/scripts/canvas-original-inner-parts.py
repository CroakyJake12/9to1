#!/usr/bin/env python3
"""Retain only exact ORIGINAL public Canvas ZIP bytes; no native additions."""
import argparse
import hashlib
import json
import os
import re
import shutil
import stat
import zipfile
from pathlib import Path, PurePosixPath

ZIP_NAME = "canvas-win-x64-unaccepted.zip"
ZIP_SIZE = 101349818
ZIP_SHA = "dd24a1082fa6ac571068d3b0bcf941bbb497c4d7f07a67eb78752a15f711ef0a"
MANIFEST_SHA = "5544406d1f09ee0a7aab41237b9bbc463ef2109033d5116abfbd85c28df0182a"
PRODUCER = "e2effba426353978b11fc1c0f2fc8e3e533318e4"
PART_SIZE = 19 * 1024 * 1024
CHUNK_SIZE = 1024 * 1024


def require(condition, label):
    if not condition:
        raise ValueError(label)


def one(root, name):
    paths = [p for p in root.rglob(name) if p.is_file() and not p.is_symlink()]
    require(len(paths) == 1, "Expected one exact original input")
    return paths[0]


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while data := source.read(CHUNK_SIZE):
            digest.update(data)
    return digest.hexdigest()


def safe_name(name):
    segments = PurePosixPath(name).parts
    require(name and not name.startswith("/") and ":" not in name and "\\" not in name, "Unsafe original archive name")
    require(all(p not in (".", "..") and not p.endswith((" ", ".")) for p in segments), "Unsafe original archive segment")
    require(all(not re.search(r'[<>"|?*\x00-\x1f]', p) for p in segments), "Invalid Windows archive name")
    require(all(p.split(".")[0].upper() not in {"CON", "PRN", "AUX", "NUL", *("COM" + str(i) for i in range(1, 10)), *("LPT" + str(i) for i in range(1, 10))} for p in segments), "Reserved Windows archive name")


def run(args):
    output = args.output.resolve()
    require(not output.exists(), "Fresh output directory required")
    require(not output.is_relative_to(Path(__file__).resolve().parents[2]), "Output must be outside source")
    for source_root in (args.package_input.resolve(), args.observation_input.resolve()):
        require(not output.is_relative_to(source_root), "Output must be outside immutable input")
    require(shutil.disk_usage(output.parent).free >= 2 * 1024**3, "Assigned runner output capacity prerequisite")
    original = one(args.package_input, ZIP_NAME)
    manifest_path = one(args.observation_input, "result.json")
    require(original.stat().st_size == ZIP_SIZE and file_hash(original) == ZIP_SHA, "Original inner ZIP mismatch")
    manifest_bytes = manifest_path.read_bytes()
    require(hashlib.sha256(manifest_bytes).hexdigest() == MANIFEST_SHA, "Original manifest mismatch")
    manifest = json.loads(manifest_bytes.decode("utf-8-sig"))
    require(manifest["workflowCommit"] == PRODUCER and manifest["target"] == "canvas", "Original managed source mismatch")
    require(manifest["package"] == {"file": ZIP_NAME, "bytes": ZIP_SIZE, "sha256": ZIP_SHA}, "Original manifest package mismatch")
    rows = manifest["files"]
    require(len(rows) == 418 and len({r["path"].casefold() for r in rows}) == 418, "Original 418-casefold manifest required")
    expected = {row["path"]: row for row in rows}
    with zipfile.ZipFile(original) as archive:
        entries = archive.infolist()
        files = [e for e in entries if not e.is_dir()]
        require(len(files) == 418 and {e.filename for e in files} == set(expected), "Exact original ZIP member set required")
        require(len({e.filename.casefold() for e in entries}) == len(entries), "Casefold archive collision refused")
        for entry in entries:
            safe_name(entry.filename.rstrip("/") if entry.is_dir() else entry.filename)
            require(not entry.flag_bits & 1 and stat.S_IFMT(entry.external_attr >> 16) != stat.S_IFLNK and not entry.external_attr & 1024, "Unsafe ZIP link/encryption refused")
        for entry in files:
            row = expected[entry.filename]
            require(entry.file_size == row["bytes"], "Original member size mismatch")
            digest = hashlib.sha256()
            count = 0
            with archive.open(entry) as member:
                while data := member.read(CHUNK_SIZE):
                    count += len(data)
                    require(count <= row["bytes"], "Original member bound exceeded")
                    digest.update(data)
            require(count == row["bytes"] and digest.hexdigest() == row["sha256"], "Original member CRC/body hash mismatch")
    output.mkdir()
    (output / "original-result.json").write_bytes(manifest_bytes)
    parts = []
    whole = hashlib.sha256()
    consumed = 0
    with original.open("rb") as source:
        while consumed < ZIP_SIZE:
            target = output / f"original-canvas.zip.part-{len(parts)+1:02d}"
            digest = hashlib.sha256()
            size = 0
            with target.open("xb") as destination:
                while size < PART_SIZE and consumed < ZIP_SIZE:
                    require(shutil.disk_usage(output).free >= 1024**3, "Runner retention capacity refused")
                    data = source.read(min(CHUNK_SIZE, PART_SIZE-size, ZIP_SIZE-consumed))
                    require(bool(data), "Original ZIP truncated during retention")
                    destination.write(data)
                    digest.update(data)
                    whole.update(data)
                    size += len(data)
                    consumed += len(data)
            require(target.stat().st_size == size and file_hash(target) == digest.hexdigest(), "Physical original part mismatch")
            parts.append({"order": len(parts)+1, "file": target.name, "offset": consumed-size, "bytes": size, "sha256": digest.hexdigest()})
        require(source.read(1) == b"", "Original ZIP changed during retention")
    require(consumed == ZIP_SIZE and whole.hexdigest() == ZIP_SHA and file_hash(original) == ZIP_SHA, "Original final byte stream mismatch")
    index = {"schemaVersion": 1, "scope": "EXACT_ORIGINAL_PUBLIC_CANVAS_INNER_ZIP_PARTS_ONLY_UNACCEPTED", "originalRun": 37203799923,
             "originalArtifact": 11304710012, "originalManagedProducer": PRODUCER, "originalPackage": manifest["package"],
             "originalManifestSHA256": MANIFEST_SHA, "all418OriginalMemberCRCAndHashesVerified": True, "parts": parts,
             "noRebuildExtractionCanvasReplayNativeKitOrAugmentation": True}
    (output / "original-parts-index.json").write_text(json.dumps(index, indent=2) + "\n")
    with open(os.environ["GITHUB_OUTPUT"], "a") as github_output:
        github_output.write("parts_ready=true\n")
    print(json.dumps({"originalFilesVerified": 418, "originalParts": len(parts), "originalZIPBytes": consumed, "nativeInputs": 0}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package-input", type=Path, required=True)
    parser.add_argument("--observation-input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    try:
        run(parser.parse_args())
    except Exception as exc:
        print(json.dumps({"status": "ORIGINAL_RETENTION_REFUSED", "exceptionTypeOnly": type(exc).__name__, "rawExternalText": "WITHHELD"}))
        raise SystemExit(1)
