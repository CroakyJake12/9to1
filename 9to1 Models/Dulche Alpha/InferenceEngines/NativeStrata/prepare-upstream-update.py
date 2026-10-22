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


def member_body(archive: pathlib.Path, relative: str) -> bytes:
    """Return the raw body of one regular member of the archive (by relative path)."""
    with tarfile.open(archive, "r:gz") as source:
        for member in source.getmembers():
            if member.isdir() or not member.isfile():
                continue
            name = pathlib.PurePosixPath(member.name)
            if len(name.parts) < 2:
                continue
            rel = pathlib.PurePosixPath(*name.parts[1:]).as_posix()
            if rel == relative:
                original = source.extractfile(member)
                if original is None:
                    raise ValueError("Missing source body: " + relative)
                with original:
                    return original.read()
    raise ValueError("Member not found in archive: " + relative)


def explained_eol_normalization(archive: pathlib.Path, path: str, tree_blob_sha: str) -> bool:
    """Verify the ONLY allowed EOL export transform: CRLF->LF normalization of the exported body makes
    its git blob hash equal the authenticated tree blob. A pathname is never exempt by itself."""
    try:
        body = member_body(archive, path)
    except Exception:
        return False
    return git_blob_sha(body.replace(b"\r\n", b"\n")) == tree_blob_sha


def extract_rows(archive: pathlib.Path):
    """Yield (relative_path, bytes, sha256, git_blob_sha) for every regular source file."""
    expected_root = None
    rows = []
    seen = set()
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
            if relative in seen:
                raise ValueError("Duplicate archive member: " + relative)
            seen.add(relative)
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
    # Provenance guard: the archive is keyed by a commit and a tree that MUST be
    # distinct Git object SHAs. A lock that copies the commit as the tree is a
    # provenance error (the two object types are never the same identifier).
    if not args.commit or not args.tree:
        raise ValueError("Both --commit and --tree are required and must be distinct Git object SHAs.")
    if args.commit == args.tree:
        raise ValueError("Provenance error: --commit and --tree must differ (a commit SHA is never its tree SHA).")
    if len(args.commit) != 40 or len(args.tree) != 40 or not all(c in "0123456789abcdef" for c in args.commit + args.tree):
        raise ValueError("Provenance error: commit/tree must be 40-hex SHA-1 identifiers.")
    archive = args.archive.read_bytes()
    rows = extract_rows(args.archive)
    lock = {
        "origin": args.origin,
        "commit": args.commit,
        "gitCommitTree": args.tree,
        "archiveUrl": args.archive_url or "",
        "lockSchema": 2,
        "provenance": {
            "commit": args.commit,
            "tree": args.tree,
            "note": "Authenticated tag->commit->tree chain must be resolved independently and recorded; a commit is never its own tree.",
        },
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
    # Optional authenticated-tree cross-check: verifies the archive's local blob
    # hashes against the upstream tree blob SHAs, honouring EOL export exemption
    # lists (e.g. .gitattributes `eol=crlf` paths that differ between the working
    # tree export and the committed blob).
    if getattr(args, "verified_tree_blobs", None) is not None:
        tree = json.loads(args.verified_tree_blobs.read_text())
        # Header binding: the authenticated tree supplied MUST be the one selected as the lock's tree,
        # never a different tree that merely happens to be attached to the argument.
        if tree.get("treeSha") != args.tree:
            raise ValueError("Tree cross-check header mismatch: authenticated tree %s != selected tree %s"
                             % (tree.get("treeSha"), args.tree))
        blobs = tree.get("blobs") or {}
        missing = [r["path"] for r in lock["rows"] if r["path"] not in blobs]
        extra = [p for p in blobs if p not in {r["path"] for r in lock["rows"]}]
        mismatch = [r["path"] for r in lock["rows"] if r["path"] in blobs and blobs[r["path"]] != r["gitBlobSha"]]
        # No blanket path exemption: every mismatch must be explained by recomputing the actual
        # normalized (CRLF->LF) body blob hash against the authenticated tree. Anything else is
        # unexplained and fails closed.
        explained = [p for p in mismatch if explained_eol_normalization(args.archive, p, blobs[p])]
        unexplained = [p for p in mismatch if p not in explained]
        if missing or extra or unexplained:
            raise ValueError(
                "Tree cross-check failed: missing=%d extra=%d unexplained-mismatch=%d"
                % (len(missing), len(extra), len(unexplained)))
        lock["treeCrossCheck"] = {
            "treeSha": args.tree,
            "blobMatches": len(lock["rows"]) - len(mismatch),
            "missingFromTree": missing,
            "extraInTree": extra,
            "eolExportNormalized": sorted(explained),
        }
        # Stamp the INDEPENDENTLY authenticated canonical GitHub tree blob SHA onto each explained
        # EOL-normalized row; the raw archive blob stays pinned as gitBlobSha.
        by_path = {r["path"]: r for r in lock["rows"]}
        for p in explained:
            by_path[p]["treeBlobSha"] = blobs[p]
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(lock, indent=2) + "\n")
    print("candidate lock written: %s" % args.out)
    print("origin=%s commit=%s tree=%s" % (args.origin, args.commit, args.tree))
    print("archive=%d bytes sha256=%s" % (len(archive), lock["archive"]["sha256"]))
    print("blobs=%d sourceBytes=%d" % (len(rows), lock["sourceBytes"]))
    return 0


def verify_tree(args) -> int:
    """Cross-check a lock's rows against an authenticated Git tree blob map.

    Every mismatch must be explained by recomputing the actual normalized (CRLF->LF) body blob hash
    against the authenticated tree, and the authenticated tree must equal the lock's tree. There is
    no blanket path exemption.
    """
    lock = json.loads(args.lock.read_text())
    tree = json.loads(args.tree_blobs.read_text())
    tree_sha = tree.get("treeSha")
    if tree_sha != lock.get("gitCommitTree"):
        print("tree cross-check FAILED: tree header %s != lock tree %s" % (tree_sha, lock.get("gitCommitTree")),
              file=sys.stderr)
        return 2
    blobs = tree.get("blobs") or {}
    rows = {r["path"]: r for r in lock["rows"]}
    missing = [p for p in rows if p not in blobs]
    extra = [p for p in blobs if p not in rows]
    mismatch = [p for p in rows if p in blobs and blobs[p] != rows[p]["gitBlobSha"]]
    explained = [p for p in mismatch if explained_eol_normalization(args.archive, p, blobs[p])]
    unexplained = [p for p in mismatch if p not in explained]
    print("tree=%s lockRows=%d treeBlobs=%d" % (tree_sha, len(rows), len(blobs)))
    print("missingFromTree=%d extraInTree=%d mismatch=%d explainedEOL=%d unexplained=%d"
          % (len(missing), len(extra), len(mismatch), len(explained), len(unexplained)))
    if missing or extra or unexplained:
        print("tree cross-check FAILED", file=sys.stderr)
        return 2
    print("tree cross-check OK")
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
    g.add_argument("--verified-tree-blobs", type=pathlib.Path, default=None,
                  help="Authenticated tree blob map JSON (path->git blob SHA) to cross-check the archive against.")
    g.add_argument("--out", type=pathlib.Path, required=True)
    g.set_defaults(func=generate)

    v = sub.add_parser("verify")
    v.add_argument("--archive", type=pathlib.Path, required=True)
    v.add_argument("--lock", type=pathlib.Path, required=True)
    v.set_defaults(func=verify)

    vt = sub.add_parser("verify-tree")
    vt.add_argument("--lock", type=pathlib.Path, required=True)
    vt.add_argument("--tree-blobs", type=pathlib.Path, required=True)
    vt.add_argument("--archive", type=pathlib.Path, required=True,
                    help="The donor source archive (used to recompute normalized EOL blob hashes).")
    vt.set_defaults(func=verify_tree)

    args = parser.parse_args()
    try:
        return args.func(args)
    except Exception as error:  # noqa: BLE001 - CLI boundary
        print("error: %s" % error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
