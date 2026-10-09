#!/usr/bin/env python3
"""Transport original sealed public ZIP bytes; never extract or rebuild assets."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import stat
import zipfile

PART_BYTES = 19 * 1024 * 1024
MAX_PARTS = 2
ARTIFACT_BODY_LIMIT = 20 * 1024 * 1024 - 64 * 1024
PUBLIC_LIMIT = 128 * 1024 * 1024
META_LIMIT = 1024 * 1024
FILES = {"wwwroot.zip", "publish-manifest.json", "seal.json"}
PART_FILES = {"wwwroot.zip.part", "publish-manifest.json", "seal.json", "parts-manifest.json"}


def require(value, message):
    if not value:
        raise ValueError(message)


def sha_bytes(data):
    return hashlib.sha256(data).hexdigest()


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def canonical(path):
    path = Path(path).absolute()
    require(path == path.resolve(), "Canonical nonsymlink path required")
    return path


def regular(path):
    require(not path.is_symlink() and stat.S_ISREG(path.lstat().st_mode), "Regular public file required")
    return path


def bounded_metadata(path):
    regular(path)
    require(path.stat().st_size <= META_LIMIT, "Public metadata exceeds bound")
    return path.read_bytes()


def valid_hash(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def valid_name(value):
    return (isinstance(value, str) and bool(value) and not value.startswith("/")
            and "\\" not in value and "\0" not in value
            and all(part not in ("", ".", "..") for part in value.split("/")))


def public_name(value):
    if not valid_name(value):
        return False
    lower = value.lower()
    return (not any(part in {".git", ".nuget", "node_modules", "obj", "fixture-data", "profiles"}
                    for part in lower.split("/"))
            and Path(lower).suffix not in {".nupkg", ".pfx", ".p12", ".pem", ".key"}
            and not Path(lower).name.startswith(".env"))


def metadata(receipt_raw, seal_raw, expected_commit):
    require(re.fullmatch(r"[0-9a-f]{40}", expected_commit) is not None, "Exact source commit required")
    receipt, seal = json.loads(receipt_raw), json.loads(seal_raw)
    require(receipt["sourceCommit"] == receipt["sourceCommitAfter"] == seal["sourceCommit"] == expected_commit,
            "Publisher source commit differs")
    require(receipt["exitCode"] == 0 and seal["receiptSha256"] == sha_bytes(receipt_raw), "Publisher receipt differs")
    rows = receipt["publishFiles"]
    require(isinstance(rows, list) and len(rows) > 0, "Complete public inventory required")
    names = set()
    for row in rows:
        require(public_name(row["path"]) and row["path"] not in names and valid_hash(row["sha256"])
                and type(row["bytes"]) is int and row["bytes"] >= 0, "Invalid public inventory row")
        names.add(row["path"])
    require(receipt["fileCount"] == seal["fileCount"] == len(rows), "Public file count differs")
    require(receipt["totalBytes"] == seal["totalBytes"] == sum(row["bytes"] for row in rows)
            and 0 < seal["totalBytes"] <= PUBLIC_LIMIT, "Public body size differs")
    require(type(seal["zipBytes"]) is int and 0 < seal["zipBytes"] <= PART_BYTES * MAX_PARTS
            and valid_hash(seal["zipSha256"]), "Original ZIP exceeds two-part transport capacity or invalid seal")
    return receipt, seal


def verify_archive(path, receipt, seal):
    regular(path)
    require(path.stat().st_size == seal["zipBytes"] and digest(path) == seal["zipSha256"], "Original ZIP size/hash differs")
    expected = {row["path"]: row for row in receipt["publishFiles"]}
    with zipfile.ZipFile(path) as archive:
        members = archive.infolist()
        require(len(members) == len(expected) and {item.filename for item in members} == set(expected),
                "ZIP inventory has missing/extra/duplicate members")
        for item in members:
            mode = stat.S_IFMT(item.external_attr >> 16)
            require(valid_name(item.filename) and not item.is_dir() and mode in (0, stat.S_IFREG)
                    and not item.flag_bits & 1, "Unsafe public ZIP member")
            row = expected[item.filename]
            require(item.file_size == row["bytes"], "ZIP member size differs")
            result = hashlib.sha256()
            size = 0
            with archive.open(item) as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                    size += len(chunk)
                    require(size <= row["bytes"], "ZIP member exceeds declared size")
                    result.update(chunk)
            require(size == row["bytes"] and result.hexdigest() == row["sha256"], "ZIP full body/CRC/hash differs")


def verify_parts(root, expected_commit, expected_index_sha256):
    root = canonical(root)
    require(valid_hash(expected_index_sha256), "Independent index hash required")
    index_raw = bounded_metadata(root / "part01/parts-manifest.json")
    require(sha_bytes(index_raw) == expected_index_sha256, "Transport index hash differs")
    index = json.loads(index_raw)
    rows = index["parts"]
    require(type(index["partBytesLimit"]) is int and 0 < index["partBytesLimit"] <= PART_BYTES
            and 1 <= len(rows) <= MAX_PARTS, "Part capacity differs")
    folders = {"part%02d" % number for number in range(1, len(rows) + 1)}
    require({path.name for path in root.iterdir()} == folders, "Missing/extra part directories")
    whole = hashlib.sha256()
    total = 0
    originals = None
    for number, row in enumerate(rows, 1):
        folder = canonical(root / ("part%02d" % number))
        require(folder.is_dir() and {path.name for path in folder.iterdir()} == PART_FILES, "Incomplete/extra part files")
        raw = bounded_metadata(folder / "parts-manifest.json")
        receipt_raw = bounded_metadata(folder / "publish-manifest.json")
        seal_raw = bounded_metadata(folder / "seal.json")
        require(raw == index_raw, "Part index copies differ")
        if originals is None:
            originals = (receipt_raw, seal_raw)
        require(originals == (receipt_raw, seal_raw), "Original metadata copies differ")
        receipt, seal = metadata(receipt_raw, seal_raw, expected_commit)
        require(index["sourceCommit"] == expected_commit and index["originalReceiptSha256"] == sha_bytes(receipt_raw)
                and index["originalSealSha256"] == sha_bytes(seal_raw)
                and index["archiveSha256"] == seal["zipSha256"] and index["archiveBytes"] == seal["zipBytes"],
                "Original publisher tuple differs")
        require(row["part"] == number and row["path"] == "%s/wwwroot.zip.part" % folder.name
                and type(row["bytes"]) is int and 0 < row["bytes"] <= index["partBytesLimit"]
                and valid_hash(row["sha256"]), "Ordered part identity/size differs")
        part = regular(folder / "wwwroot.zip.part")
        require(part.stat().st_size == row["bytes"] and digest(part) == row["sha256"], "Part size/hash differs")
        require(sum(regular(path).stat().st_size for path in folder.iterdir()) < ARTIFACT_BODY_LIMIT,
                "Part artifact payload exceeds bounded overhead reserve")
        with part.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                whole.update(chunk)
                total += len(chunk)
    require(total == index["archiveBytes"] and whole.hexdigest() == index["archiveSha256"], "Reassembly size/hash differs")
    return index


def split_publication(publication, output, expected_commit, part_bytes=PART_BYTES):
    publication, output = canonical(publication), canonical(output)
    require(publication.is_dir() and {path.name for path in publication.iterdir()} == FILES,
            "Exact three original public producer files required")
    require(not output.exists() and not output.is_relative_to(publication)
            and not publication.is_relative_to(output), "Fresh disjoint output required")
    require(type(part_bytes) is int and 0 < part_bytes <= PART_BYTES, "Part byte bound differs")
    receipt_raw = bounded_metadata(publication / "publish-manifest.json")
    seal_raw = bounded_metadata(publication / "seal.json")
    receipt, seal = metadata(receipt_raw, seal_raw, expected_commit)
    require(seal["zipBytes"] <= part_bytes * MAX_PARTS, "Original ZIP exceeds configured two-part capacity")
    archive = publication / "wwwroot.zip"
    verify_archive(archive, receipt, seal)
    output.mkdir()
    try:
        rows = []
        with archive.open("rb") as stream:
            for number in range(1, MAX_PARTS + 1):
                data = stream.read(part_bytes)
                if not data:
                    break
                folder = output / ("part%02d" % number)
                folder.mkdir()
                (folder / "wwwroot.zip.part").write_bytes(data)
                rows.append({"part": number, "path": "%s/wwwroot.zip.part" % folder.name,
                             "bytes": len(data), "sha256": sha_bytes(data)})
            require(not stream.read(1), "Original ZIP has untransported bytes")
        index = {"sourceCommit": expected_commit, "archiveBytes": seal["zipBytes"], "archiveSha256": seal["zipSha256"],
                 "originalReceiptSha256": sha_bytes(receipt_raw), "originalSealSha256": sha_bytes(seal_raw),
                 "partBytesLimit": part_bytes, "parts": rows,
                 "reassembly": "Concatenate parts in numeric order; require declared whole size/SHA before ZIP use.",
                 "scope": "Exact original public archive transport only; no rebuilt assets or runtime acceptance."}
        index_raw = (json.dumps(index, indent=2) + "\n").encode()
        for row in rows:
            folder = output / ("part%02d" % row["part"])
            (folder / "publish-manifest.json").write_bytes(receipt_raw)
            (folder / "seal.json").write_bytes(seal_raw)
            (folder / "parts-manifest.json").write_bytes(index_raw)
        verify_parts(output, expected_commit, sha_bytes(index_raw))
        require(bounded_metadata(publication / "publish-manifest.json") == receipt_raw
                and bounded_metadata(publication / "seal.json") == seal_raw, "Original metadata changed during transport")
        verify_archive(archive, receipt, seal)
        return index, sha_bytes(index_raw)
    except Exception:
        shutil.rmtree(output)
        raise


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--publication", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expected-commit", required=True)
    parser.add_argument("--github-output", type=Path)
    args = parser.parse_args()
    index, index_sha = split_publication(args.publication, args.output, args.expected_commit)
    if args.github_output:
        with args.github_output.open("a") as stream:
            stream.write("part_count=%d\n" % len(index["parts"]))
    print(json.dumps({"sourceCommit": args.expected_commit, "archiveBytes": index["archiveBytes"],
                      "archiveSha256": index["archiveSha256"], "indexSha256": index_sha,
                      "originalReceiptSha256": index["originalReceiptSha256"],
                      "originalSealSha256": index["originalSealSha256"],
                      "partCount": len(index["parts"]), "runtimeAccepted": False}))


if __name__ == "__main__":
    main()
