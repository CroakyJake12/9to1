#!/usr/bin/env python3
"""Prepare a candidate Strata source lock for an upstream upgrade.

This is a BOUNDED, build-free tool: it does not compile, link, run inference, install
packages or download models. It reads an already-fetched upstream source archive (the exact
public tar.gz of a tag) and emits a `strata-source-lock.json` candidate in the same schema
as the maintained `NativeStrata/strata-source-lock.json`.

Source-lock modes:
  generate --archive A --origin O --commit C --tree T --out out.json
      Builds a candidate lock from the archive. gitBlobSha is computed locally from file
      contents (git blob SHA-1), so no GitHub API is required.
  verify --archive A --lock L.json
      Verifies the archive against a lock exactly like `build-package.py` does
      (size, per-row bytes, gitBlobSha, sha256, closure). Useful to validate both the
      current maintained lock and any candidate.

An additional explicit prepare-host-cache --source-root S --out NEW_DIR mode
prepares a small opt-in selected-source patch packet from the checked-in prep profile.
It never changes the provided upstream, selected build profile, source lock or engine pin.

No provided upstream body is modified; this tool produces metadata or a separate patch packet. The caller still must
rework the native bridge against the chosen upstream API and run the real CMake/CUDA build
in a separately allocated build slot before promotion.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import stat
import sys
import tarfile


def git_blob_sha(body: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(body)).encode("ascii") + b"\0" + body).hexdigest()


def validated_lock_rows(lock: dict) -> dict:
    """Keep source-lock rows one-to-one; never hide an earlier row behind a later path."""
    expected = {}
    for row in lock["rows"]:
        path = row["path"]
        if path in expected:
            raise ValueError("Duplicate source-lock path: " + path)
        expected[path] = row
    count = lock["blobCount"]
    if type(count) is not int or count != len(expected):
        raise ValueError("Invalid source-lock blobCount: expected %d unique rows" % len(expected))
    return expected


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
    rows = validated_lock_rows(lock)
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
    expected = validated_lock_rows(lock)
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


# Experimental PREPARATION profile only. This does not select or promote a
# build-package profile, candidate engine/source lock, or production setting.
HOST_CACHE_PROFILE = "host-cache-v0.2.3.1"
HOST_CACHE_PROFILE_SHA256 = "61d29875c6e88941e3c8b3bf937713ab8f444a12f69442350cfafac2d454d30b"
HOST_CACHE_ENGINE = "src/qwen35/q36_engine.cu"
HOST_CACHE_OUTPUT_LIMIT = 64 * 1024


def read_regular_source(path: pathlib.Path) -> bytes:
    # O_NOFOLLOW enforces the FINAL-component rule in the open itself. Parent
    # components are not confined and this is not whole-checkout authority.
    nofollow = getattr(os, "O_NOFOLLOW", None)
    nonblock = getattr(os, "O_NONBLOCK", None)
    if not isinstance(nofollow, int) or not nofollow or not isinstance(nonblock, int) or not nonblock:
        raise ValueError("platform cannot enforce O_NOFOLLOW|O_NONBLOCK input reads")
    flags = os.O_RDONLY | nofollow | nonblock | getattr(os, "O_CLOEXEC", 0)
    try:
        descriptor = os.open(path, flags)
    except OSError as failure:
        raise ValueError(f"final-link-safe input open refused: {path}: {failure.strerror}") from failure
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"regular input file required: {path}")
        if before.st_size > 350000:
            raise ValueError(f"input exceeds small-source bound: {path}")
        chunks = []
        remaining = 350001
        while remaining:
            chunk = os.read(descriptor, remaining)
            if not chunk:
                break
            chunks.append(chunk)
            remaining -= len(chunk)
        after = os.fstat(descriptor)
        # Exclude access time: this very read may update it. This detects
        # observable changes while the descriptor is held, not a filesystem
        # snapshot or protection from writers that can forge metadata.
        def identity(info):
            return (info.st_dev, info.st_ino, info.st_mode, info.st_nlink,
                    info.st_uid, info.st_gid, info.st_size,
                    info.st_mtime_ns, info.st_ctime_ns)
        if identity(before) != identity(after):
            raise ValueError(f"input descriptor metadata changed during read: {path}")
        data = b"".join(chunks)
        if len(data) > 350000:
            raise ValueError(f"input grew past small-source bound: {path}")
        if len(data) != before.st_size:
            raise ValueError(f"input size inconsistent during descriptor read: {path}")
        return data
    finally:
        os.close(descriptor)


def strict_host_cache_patch(base: bytes, patch: bytes) -> bytes:
    """Apply only this one-file unified diff, with exact positions and no fuzz."""
    lines = patch.splitlines(keepends=True)
    headers = [b"--- a/src/qwen35/q36_engine.cu\n",
               b"+++ b/src/qwen35/q36_engine.cu\n"]
    if lines[:2] != headers:
        raise ValueError("patch must affect exactly the reviewed q36 engine path")
    original = base.splitlines(keepends=True)
    output: list[bytes] = []
    cursor = 0
    at = 2
    hunks = 0
    pattern = rb"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@[^\n]*\n"
    while at < len(lines):
        match = re.fullmatch(pattern, lines[at])
        if not match:
            raise ValueError("malformed or additional-file patch hunk")
        old_start, old_count, new_start, new_count = (
            int(value) if value is not None else 1 for value in match.groups())
        old_pos = old_start - 1
        if old_pos < cursor or old_pos > len(original):
            raise ValueError("overlapping or out-of-range patch hunk")
        output.extend(original[cursor:old_pos])
        cursor = old_pos
        if len(output) != new_start - 1:
            raise ValueError("new hunk location mismatch")
        consumed = produced = 0
        at += 1
        while at < len(lines) and not lines[at].startswith(b"@@ "):
            line = lines[at]
            if not line or line[:1] not in (b" ", b"+", b"-"):
                raise ValueError("unsupported patch line")
            if line[:1] != b"+":
                if cursor >= len(original) or original[cursor] != line[1:]:
                    raise ValueError("patch preimage/context mismatch")
                cursor += 1
                consumed += 1
            if line[:1] != b"-":
                output.append(line[1:])
                produced += 1
            at += 1
        if consumed != old_count or produced != new_count:
            raise ValueError("patch hunk line count mismatch")
        hunks += 1
    if not hunks:
        raise ValueError("empty patch")
    output.extend(original[cursor:])
    return b"".join(output)


def load_host_cache_profile():
    assets = pathlib.Path(__file__).resolve().parent / "prep" / HOST_CACHE_PROFILE
    body = read_regular_source(assets / "profile.json")
    if hashlib.sha256(body).hexdigest() != HOST_CACHE_PROFILE_SHA256:
        raise ValueError("Frozen host-cache preparation profile changed")
    profile = json.loads(body)
    if (profile["schema"] != "strata-host-cache-preparation/v1"
            or profile["scope"] != "SELECTED_SOURCE_FILES_ONLY_NOT_FULL_ARCHIVE_LOCK"
            or profile["enginePath"] != HOST_CACHE_ENGINE):
        raise ValueError("Invalid selected-source host-cache profile")
    return assets, profile


def verify_selected_host_cache_source(source_root, rows):
    """Verify only supplied selected rows; never claim a complete checkout/lock."""
    verified = {}
    for relative, row in rows.items():
        body = read_regular_source(source_root / relative)
        if (len(body) != row["bytes"] or git_blob_sha(body) != row["gitBlobSha"]
                or hashlib.sha256(body).hexdigest() != row["sha256"]):
            raise ValueError("Selected host-cache source identity mismatch: " + relative)
        verified[relative] = body
    return verified


def host_cache_payload(assets, profile):
    payload = {}
    for row in profile["payload"]:
        relative = pathlib.PurePosixPath(row["path"])
        if (relative.is_absolute() or ".." in relative.parts or not relative.parts
                or row["path"] in payload):
            raise ValueError("Unsafe or duplicate host-cache payload path")
        body = read_regular_source(assets / row["asset"])
        if len(body) != row["bytes"] or hashlib.sha256(body).hexdigest() != row["sha256"]:
            raise ValueError("Frozen host-cache payload changed: " + row["asset"])
        payload[row["path"]] = body
    return payload


def emit_host_cache_packet(payload, output, source_root):
    """Write only a fresh, small sibling/private packet; never write upstream."""
    # Resolve parent aliases before the overlap check. This is an output-path
    # check, not a claim that the provided source is a whole verified checkout.
    source = source_root.resolve(strict=True)
    if not source.is_dir():
        raise ValueError("Host-cache source root must be a directory")
    output = output.absolute()
    parent = output.parent.resolve(strict=True)
    output = parent / output.name
    if output == source or source in output.parents:
        raise ValueError("Host-cache output must be outside the provided upstream tree")
    if sum(map(len, payload.values())) > HOST_CACHE_OUTPUT_LIMIT:
        raise ValueError("Host-cache output exceeds the 64 KiB preparation limit")
    nofollow, directory = getattr(os, "O_NOFOLLOW", None), getattr(os, "O_DIRECTORY", None)
    if (not nofollow or not directory or os.open not in os.supports_dir_fd
            or os.mkdir not in os.supports_dir_fd):
        raise ValueError("Platform cannot enforce exclusive no-follow preparation output")
    # Hold the resolved parent before mkdir. A parent renamed/replaced by a
    # symlink cannot redirect the new packet through a later pathname lookup.
    parent_fd = os.open(parent, os.O_RDONLY | directory | nofollow | getattr(os, "O_CLOEXEC", 0))
    try:
        # Atomic mkdir refuses every existing final entry, including dangling links.
        os.mkdir(output.name, 0o700, dir_fd=parent_fd)
        root_fd = os.open(output.name, os.O_RDONLY | directory | nofollow
                          | getattr(os, "O_CLOEXEC", 0), dir_fd=parent_fd)
        try:
            for relative, body in payload.items():
                parts = pathlib.PurePosixPath(relative).parts
                if not parts or pathlib.PurePosixPath(relative).is_absolute() or ".." in parts:
                    raise ValueError("Unsafe host-cache output path")
                at = os.dup(root_fd)
                try:
                    for part in parts[:-1]:
                        try:
                            os.mkdir(part, 0o700, dir_fd=at)
                        except FileExistsError:
                            pass
                        child = os.open(part, os.O_RDONLY | directory | nofollow, dir_fd=at)
                        os.close(at)
                        at = child
                    fd = os.open(parts[-1], os.O_WRONLY | os.O_CREAT | os.O_EXCL | nofollow
                                 | getattr(os, "O_CLOEXEC", 0), 0o600, dir_fd=at)
                    try:
                        view = memoryview(body)
                        while view:
                            written = os.write(fd, view)
                            if written <= 0:
                                raise OSError("Short host-cache output write")
                            view = view[written:]
                    finally:
                        os.close(fd)
                finally:
                    os.close(at)
        finally:
            os.close(root_fd)
        visible, held = os.stat(parent, follow_symlinks=False), os.fstat(parent_fd)
        if (visible.st_dev, visible.st_ino, visible.st_mode) != (held.st_dev, held.st_ino, held.st_mode):
            raise ValueError("Host-cache output parent changed during preparation; partial packet retained")
    finally:
        os.close(parent_fd)
    return output


def prepare_host_cache(args):
    assets, profile = load_host_cache_profile()
    rows = validated_lock_rows(profile)
    sources = verify_selected_host_cache_source(args.source_root, rows)
    payload = host_cache_payload(assets, profile)
    patched = strict_host_cache_patch(sources[HOST_CACHE_ENGINE], payload["q36_engine.integration.patch"])
    if hashlib.sha256(patched).hexdigest() != profile["postimageSha256"]:
        raise ValueError("Host-cache patched engine differs from the frozen postimage")
    receipt = {
        "schema": "strata-host-cache-preparation-receipt/v1",
        "status": "SELECTED_SOURCE_APPLICABLE_NOT_ENGINE_COMPILED",
        "scope": profile["scope"],
        "origin": profile["origin"], "release": profile["release"],
        "referenceCommit": profile["commit"], "referenceTree": profile["gitCommitTree"],
        "profileSha256": HOST_CACHE_PROFILE_SHA256,
        "selectedSourceFiles": len(rows),
        "enginePreimageSha256": hashlib.sha256(sources[HOST_CACHE_ENGINE]).hexdigest(),
        "enginePostimageSha256": hashlib.sha256(patched).hexdigest(),
        "payload": {name: {"bytes": len(body), "sha256": hashlib.sha256(body).hexdigest()}
                    for name, body in payload.items()},
        "providedUpstreamModified": False, "candidatePinChanged": False,
        "limits": profile["limits"],
    }
    payload["preparation-receipt.json"] = (json.dumps(receipt, indent=2) + "\n").encode()
    output = emit_host_cache_packet(payload, args.out, args.source_root)
    print("opt-in host-cache preparation written: %s" % output)
    print("selected files only; no whole-checkout, engine build, CUDA fit or speed result")
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

    hc = sub.add_parser("prepare-host-cache", help="Prepare the opt-in selected-source q36 host-cache patch; no build or pin promotion.")
    hc.add_argument("--source-root", type=pathlib.Path, required=True,
                    help="Explicit already-acquired upstream directory; only profile-selected files are verified.")
    hc.add_argument("--out", type=pathlib.Path, required=True,
                    help="New isolated output directory outside upstream; existing entries are never overwritten.")
    hc.set_defaults(func=prepare_host_cache)

    args = parser.parse_args()
    try:
        return args.func(args)
    except Exception as error:  # noqa: BLE001 - CLI boundary
        print("error: %s" % error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
