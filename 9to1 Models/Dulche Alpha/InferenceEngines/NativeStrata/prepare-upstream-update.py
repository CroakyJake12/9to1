#!/usr/bin/env python3
"""Prepare a candidate Strata source lock for an upstream upgrade.

This is a BOUNDED, build-free tool: it does not compile, link, run inference, install
packages or download models. It reads an already-fetched upstream source archive (the exact
public tar.gz of a tag) and emits a `strata-source-lock.json` candidate in the same schema
as the maintained `NativeStrata/strata-source-lock.json`.

It supports two modes:
  generate --archive A --origin O --commit C --tree T --out out.json
      Builds a candidate lock from the archive. gitBlobSha is computed locally from file
      contents (git blob SHA-1), so no GitHub API is required.
  verify --archive A --lock L.json
      Verifies the archive against a lock exactly like `build-package.py` does
      (size, per-row bytes, gitBlobSha, sha256, closure). Useful to validate both the
      current maintained lock and any candidate.

No upstream body is modified; this tool only produces pin metadata. The caller still must
rework the native bridge against the chosen upstream API and run the real CMake/CUDA build
in a separately allocated build slot before promotion.
"""
import argparse
import hashlib
import json
import pathlib
import sys
import tarfile


def git_blob_sha(body: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(body)).encode("ascii") + b"\0" + body).hexdigest()


def extract_rows(archive: pathlib.Path):
    """Yield (relative_path, bytes, sha256, git_blob_sha) for every regular source file."""
    expected_root = None
    rows = []
    with tarfile.open(archive, "r:gz") as source:
        for member in source.getmembers():
            name = pathlib.PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts or not name.parts:
                raise ValueError("Unsafe archive path")
            if member.isdir():
                continue
            if not member.isfile() or len(name.parts) < 2:
                raise ValueError("Only regular source files are admitted")
            root = name.parts[0]
            if expected_root is None:
                expected_root = root
            elif root != expected_root:
                raise ValueError("Archive must have exactly one top-level root directory")
            relative = pathlib.PurePosixPath(*name.parts[1:]).as_posix()
            original = source.extractfile(member)
            if original is None:
                raise ValueError("Missing source body: " + relative)
            with original:
                body = original.read(member.size + 1)
            if len(body) != member.size:
                raise ValueError("Truncated source body: " + relative)
            rows.append((relative, len(body), hashlib.sha256(body).hexdigest(), git_blob_sha(body)))
    if expected_root is None:
        raise ValueError("Archive is empty")
    rows.sort(key=lambda r: r[0])
    return rows


def generate(args) -> int:
    archive = args.archive.read_bytes()
    rows = extract_rows(args.archive)
    lock = {
        "origin": args.origin,
        "commit": args.commit,
        "gitCommitTree": args.tree,
        "archiveUrl": args.archive_url or "",
        "archive": {
            "path": None,
            "bytes": len(archive),
            "sha256": hashlib.sha256(archive).hexdigest(),
        },
        "blobCount": len(rows),
        "sourceBytes": sum(r[1] for r in rows),
        "rows": [
            {"path": r[0], "bytes": r[1], "gitBlobSha": r[3], "sha256": r[2]}
            for r in rows
        ],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(lock, indent=2) + "\n")
    print("candidate lock written: %s" % args.out)
    print("origin=%s commit=%s tree=%s" % (args.origin, args.commit, args.tree))
    print("archive=%d bytes sha256=%s" % (len(archive), lock["archive"]["sha256"]))
    print("blobs=%d sourceBytes=%d" % (len(rows), lock["sourceBytes"]))
    return 0


def verify(args) -> int:
    lock = json.loads(args.lock.read_text())
    archive = args.archive.read_bytes()
    if len(archive) != lock["archive"]["bytes"]:
        raise ValueError("Archive byte-count mismatch")
    if hashlib.sha256(archive).hexdigest() != lock["archive"]["sha256"]:
        raise ValueError("Archive SHA256 mismatch")
    expected = {row["path"]: row for row in lock["rows"]}
    verified = {}
    with tarfile.open(args.archive, "r:gz") as source:
        roots = set()
        for member in source.getmembers():
            name = pathlib.PurePosixPath(member.name)
            if name.is_absolute() or ".." in name.parts or not name.parts:
                raise ValueError("Unsafe archive path")
            roots.add(name.parts[0])
            if member.isdir():
                continue
            if not member.isfile() or len(name.parts) < 2:
                raise ValueError("Only pinned regular source files are admitted")
            relative = pathlib.PurePosixPath(*name.parts[1:]).as_posix()
            row = expected.get(relative)
            if row is None or relative in verified or member.size != row["bytes"]:
                raise ValueError("Unexpected source member or size: " + relative)
            original = source.extractfile(member)
            if original is None:
                raise ValueError("Missing source body: " + relative)
            with original:
                body = original.read(row["bytes"] + 1)
            blob = git_blob_sha(body)
            if len(body) != row["bytes"] or blob != row["gitBlobSha"] or \
                    hashlib.sha256(body).hexdigest() != row["sha256"]:
                raise ValueError("Pinned source body mismatch: " + relative)
            verified[relative] = body
        if len(roots) != 1 or set(verified) != set(expected) or len(verified) != lock["blobCount"]:
            raise ValueError("Incomplete immutable donor closure")
    print("verify OK: %d blobs match %s" % (len(verified), args.lock))
    print("origin=%s commit=%s tree=%s" % (lock.get("origin"), lock.get("commit"), lock.get("gitCommitTree")))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="mode", required=True)

    g = sub.add_parser("generate")
    g.add_argument("--archive", type=pathlib.Path, required=True)
    g.add_argument("--origin", required=True)
    g.add_argument("--commit", required=True)
    g.add_argument("--tree", required=True)
    g.add_argument("--archive-url", default="")
    g.add_argument("--out", type=pathlib.Path, required=True)
    g.set_defaults(func=generate)

    v = sub.add_parser("verify")
    v.add_argument("--archive", type=pathlib.Path, required=True)
    v.add_argument("--lock", type=pathlib.Path, required=True)
    v.set_defaults(func=verify)

    args = parser.parse_args()
    try:
        return args.func(args)
    except Exception as error:  # noqa: BLE001 - CLI boundary
        print("error: %s" % error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
