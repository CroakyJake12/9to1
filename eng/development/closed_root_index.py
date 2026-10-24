"""Prepare a recoverable archive of one explicitly closed Root composer index.

Root calls this only after successful commit/ref and COMPOSITION persistence,
never from error/finally cleanup. This tool never unlinks an index or adopts a
receipt. Root separately reviews/adopts the proof and rechecks custody before
reclaiming the duplicate. Exact binary recovery requires the verified gzip;
read-tree would recreate entries, not the original stat/cache bytes.

Only private, self-contained SHA-1 Git index versions 2/3 are supported. Git is
read-only, bounded, has no optional locks/lazy fetch/replace objects, and always
uses the explicitly supplied alternative index. No SDK or network is invoked.
"""

import argparse
import datetime
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import selectors
import stat
import struct
import subprocess
import sys
import time

import native_source_identity
import workflow

Refusal = workflow.Refusal
MAX_INDEX_BYTES = 8 * 1024 * 1024
MAX_GIT_BYTES = 16 * 1024 * 1024
MAX_ENTRIES = 100000
GIT_SECONDS = 30
INDEX_NAME = re.compile(r"9to1-root-compose[1-9][0-9]{0,5}\.index")
FIELDS = ("bytes", "allocated_bytes", "mode", "device", "inode", "nlink", "uid", "gid", "mtime_ns", "ctime_ns")


def _absolute(value):
    path = Path(value)
    if not path.is_absolute() or str(value).startswith("//") or ".." in path.parts:
        raise Refusal("An absolute non-traversing path is required")
    return path


def _identity(info):
    return dict(zip(FIELDS, (info.st_size, info.st_blocks * 512,
        stat.S_IMODE(info.st_mode), info.st_dev, info.st_ino, info.st_nlink,
        info.st_uid, info.st_gid, info.st_mtime_ns, info.st_ctime_ns)))


class _Parent:
    """Pin existing directory components without following symlinks."""
    def __init__(self, path):
        self.path = _absolute(path)
        self.fd = os.open(self.path.anchor, os.O_RDONLY | os.O_DIRECTORY)
        try:
            for part in self.path.parts[1:-1]:
                child = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                                dir_fd=self.fd)
                os.close(self.fd)
                self.fd = child
            self.directory = os.fstat(self.fd)
        except BaseException:
            self.close()
            raise

    def close(self):
        if self.fd is not None:
            os.close(self.fd)
            self.fd = None

    def current(self):
        other = _Parent(self.path)
        try:
            if (other.directory.st_dev, other.directory.st_ino) != (self.directory.st_dev, self.directory.st_ino):
                raise Refusal("A parent directory changed")
        finally:
            other.close()

    def fresh(self):
        self.current()
        try:
            os.stat(self.path.name, dir_fd=self.fd, follow_symlinks=False)
        except FileNotFoundError:
            return
        raise Refusal("Existing output preserved; choose a fresh path")


def _regular(parent):
    parent.current()
    info = os.stat(parent.path.name, dir_fd=parent.fd, follow_symlinks=False)
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1:
        raise Refusal("Expected a regular exclusive file (nlink=1)")
    if info.st_uid != os.geteuid() or stat.S_IMODE(info.st_mode) & 0o077:
        raise Refusal("Expected a caller-owned private file")
    return _identity(info)


def _unlocked(parent):
    try:
        os.stat(parent.path.name + ".lock", dir_fd=parent.fd, follow_symlinks=False)
    except FileNotFoundError:
        return
    raise Refusal("Index lock exists; preserve the index")


def _read_index(parent, expected):
    _unlocked(parent)
    before = _regular(parent)
    if before["bytes"] > MAX_INDEX_BYTES:
        raise Refusal("Index exceeds the bounded 8 MiB limit")
    fd = os.open(parent.path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK,
                 dir_fd=parent.fd)
    with os.fdopen(fd, "rb") as stream:
        if _identity(os.fstat(stream.fileno())) != before:
            raise Refusal("Index changed before read")
        raw = stream.read(MAX_INDEX_BYTES + 1)
        if _identity(os.fstat(stream.fileno())) != before:
            raise Refusal("Index changed during read")
    if len(raw) != before["bytes"] or _regular(parent) != before:
        raise Refusal("Index changed after read")
    _unlocked(parent)
    digest = hashlib.sha256(raw).hexdigest()
    if digest != expected:
        raise Refusal("Index SHA256 mismatch")
    return dict(path=str(parent.path), sha256=digest, **before), raw


def _unchanged(parent, record):
    _unlocked(parent)
    if _regular(parent) != {key: record[key] for key in FIELDS}:
        raise Refusal("Index identity changed")
    _unlocked(parent)


def _outside_git(path, protected):
    # Also reject unrelated/nested worktrees and ordinary bare Git directories.
    for directory in path.parents:
        if directory.name == ".git" or os.path.lexists(directory / ".git"):
            raise Refusal("Path must be outside Git repositories/worktrees")
        if ((directory / "HEAD").is_file() and (directory / "objects").is_dir()
                and (directory / "refs").is_dir()):
            raise Refusal("Path must be outside a bare Git directory")
    if any(path == root or root in path.parents for root in protected):
        raise Refusal("Path must be outside repository, worktrees and Git directories")


def _git(repo, index, *arguments):
    environment = native_source_identity.git_environment()
    for name in list(environment):
        if name.startswith(("GIT_TRACE", "GIT_TEST_")):
            environment.pop(name)
    environment.update(GIT_INDEX_FILE=str(index), GIT_OPTIONAL_LOCKS="0")
    process = subprocess.Popen(["git", "--no-optional-locks", "-c", "core.fsmonitor=false",
        "-c", "core.untrackedCache=false", "-C", str(repo), *arguments],
        env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    selector = selectors.DefaultSelector()
    selector.register(process.stdout, selectors.EVENT_READ, "out")
    selector.register(process.stderr, selectors.EVENT_READ, "err")
    chunks = {"out": [], "err": []}
    length, deadline = 0, time.monotonic() + GIT_SECONDS
    try:
        while selector.get_map():
            if time.monotonic() >= deadline:
                raise Refusal("Read-only Git exceeded its time bound")
            for key, _ in selector.select(min(0.1, max(0, deadline - time.monotonic()))):
                chunk = os.read(key.fd, 65536)
                if not chunk:
                    selector.unregister(key.fileobj)
                    continue
                length += len(chunk)
                if length > MAX_GIT_BYTES:
                    raise Refusal("Read-only Git exceeded its output bound")
                chunks[key.data].append(chunk)
        if process.wait(timeout=max(0.001, deadline - time.monotonic())):
            raise Refusal("Read-only Git failed: " + b"".join(chunks["err"])[-1000:].decode(errors="replace"))
        return b"".join(chunks["out"])
    finally:
        selector.close()
        process.stdout.close()
        process.stderr.close()
        if process.poll() is None:
            process.kill()
            process.wait()


def _protected(repo, index):
    # Opening a sentinel pins/rejects symlink components of the repository path.
    parent = _Parent(repo / "sentinel")
    parent.close()
    roots = {repo}
    for argument in ("--absolute-git-dir", "--git-common-dir"):
        value = _git(repo, index, "rev-parse", "--path-format=absolute", argument)
        roots.add(_absolute(os.fsdecode(value.strip())))
    for row in _git(repo, index, "worktree", "list", "--porcelain", "-z").split(b"\0"):
        if row.startswith(b"worktree "):
            roots.add(_absolute(os.fsdecode(row[9:])))
    return roots


def _index_format(raw):
    if len(raw) < 32 or raw[:4] != b"DIRC":
        raise Refusal("Invalid Git index header")
    version, count = struct.unpack(">II", raw[4:12])
    if version not in (2, 3) or count > MAX_ENTRIES:
        raise Refusal("Only bounded SHA-1 index versions 2/3 are supported")
    if hashlib.sha1(raw[:-20]).digest() != raw[-20:]:
        raise Refusal("Git index checksum mismatch")
    offset, end = 12, len(raw) - 20
    try:
        for _ in range(count):
            start = offset
            if offset + 62 > end:
                raise ValueError()
            flags = struct.unpack(">H", raw[offset + 60:offset + 62])[0]
            offset += 62
            if flags & 0x4000:
                if version != 3:
                    raise ValueError()
                offset += 2
            terminator = raw.index(b"\0", offset, end)
            offset = start + ((terminator + 1 - start + 7) // 8) * 8
        extensions = []
        while offset < end:
            if offset + 8 > end:
                raise ValueError()
            signature = raw[offset:offset + 4]
            size = struct.unpack(">I", raw[offset + 4:offset + 8])[0]
            offset += 8 + size
            if signature == b"link":
                raise Refusal("Split index is not a self-contained cache")
            extensions.append(signature.decode("ascii"))
        if offset != end:
            raise ValueError()
    except (ValueError, struct.error, UnicodeError) as error:
        raise Refusal("Malformed Git index entries/extensions") from error
    return dict(version=version, entries=count, extensions=extensions,
                checksum="SHA1 verified", split_index=False)


def _entries(data, staged):
    result = {}
    for row in data.split(b"\0"):
        if not row:
            continue
        try:
            metadata, path = row.split(b"\t", 1)
            mode, middle, last = metadata.split(b" ")
        except ValueError as error:
            raise Refusal("Malformed Git entry output") from error
        if (not path or path.startswith(b"/") or any(p in (b"", b".", b"..") for p in path.split(b"/"))
                or path in result):
            raise Refusal("Unsafe or duplicate Git entry path")
        if staged:
            oid = middle
            if last != b"0":
                raise Refusal("Unmerged index entries are not closed")
        else:
            if middle not in (b"blob", b"commit"):
                raise Refusal("Unexpected recursive tree entry type")
            oid = last
        if mode not in (b"100644", b"100755", b"120000", b"160000") or not re.fullmatch(rb"[0-9a-f]{40}", oid):
            raise Refusal("Unsupported Git entry mode/object")
        result[path] = (mode, oid)
        if len(result) > MAX_ENTRIES:
            raise Refusal("Git entry bound exceeded")
    return result


def _semantic_proof(repo, index, revision, tree, count):
    if _git(repo, index, "rev-parse", "--verify", revision + "^{commit}").strip() != revision.encode():
        raise Refusal("Revision must identify the commit itself")
    if _git(repo, index, "rev-parse", "--verify", revision + "^{tree}").strip() != tree.encode():
        raise Refusal("Committed tree differs from the expected tree")
    staged = _entries(_git(repo, index, "ls-files", "--stage", "-z"), True)
    committed = _entries(_git(repo, index, "ls-tree", "-r", "-z", "--full-tree", revision), False)
    if len(staged) != count or staged != committed:
        raise Refusal("Every stage-0 mode/object/path must match the committed tree")
    digest = hashlib.sha256()
    for path, (mode, oid) in sorted(staged.items()):
        digest.update(mode + b" " + oid + b" 0\t" + path + b"\0")
    return dict(revision=revision, tree=tree, stage0_entries=len(staged),
                unmerged_entries=0, exact_committed_entries=True,
                normalized_entries_sha256=digest.hexdigest())


def _new_file(parent):
    parent.current()
    fd = os.open(parent.path.name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                 0o600, dir_fd=parent.fd)
    os.fchmod(fd, 0o600)
    return fd


def _verify_archive(parent, expected, length):
    before = _regular(parent)
    if before["bytes"] > length + 65536:
        raise Refusal("Compressed archive exceeds its bounded size")
    fd = os.open(parent.path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK,
                 dir_fd=parent.fd)
    compressed = hashlib.sha256()
    with os.fdopen(fd, "rb") as stream:
        if _identity(os.fstat(stream.fileno())) != before:
            raise Refusal("Archive changed before verification")
        compressed_length = 0
        while chunk := stream.read(65536):
            compressed_length += len(chunk)
            if compressed_length > length + 65536:
                raise Refusal("Archive grew beyond its bounded size")
            compressed.update(chunk)
        if compressed_length != before["bytes"]:
            raise Refusal("Compressed archive length changed")
        stream.seek(0)
        actual, digest = 0, hashlib.sha256()
        with gzip.GzipFile(fileobj=stream, mode="rb") as zipped:
            while chunk := zipped.read(min(65536, length - actual + 1)):
                actual += len(chunk)
                if actual > length:
                    raise Refusal("Decompressed archive exceeds the original length")
                digest.update(chunk)
        if actual != length or digest.hexdigest() != expected:
            raise Refusal("Whole decompressed index SHA256/length mismatch")
        if _identity(os.fstat(stream.fileno())) != before:
            raise Refusal("Archive changed during verification")
    if _regular(parent) != before:
        raise Refusal("Archive identity changed")
    return dict(path=str(parent.path), sha256=compressed.hexdigest(), **before)


def _verify_receipt(parent, identity, payload):
    if _regular(parent) != identity:
        raise Refusal("Receipt identity changed")
    fd = os.open(parent.path.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK,
                 dir_fd=parent.fd)
    with os.fdopen(fd, "rb") as stream:
        if _identity(os.fstat(stream.fileno())) != identity:
            raise Refusal("Receipt changed before verification")
        actual = stream.read(len(payload) + 1)
        if actual != payload or _identity(os.fstat(stream.fileno())) != identity:
            raise Refusal("Receipt body/identity changed")
    if _regular(parent) != identity:
        raise Refusal("Receipt identity changed after verification")


def prepare(repo, index, expected_sha256, revision, tree, archive, receipt, *, closed=False):
    """Write only fresh archive/receipt; preserve every raw index on all outcomes."""
    repo, index, archive, receipt = map(_absolute, (repo, index, archive, receipt))
    if closed is not True or INDEX_NAME.fullmatch(index.name) is None:
        raise Refusal("An explicitly closed 9to1-root-composeNN.index is required")
    if len({index, archive, receipt}) != 3:
        raise Refusal("Index, archive and receipt paths must be distinct")
    if not isinstance(expected_sha256, str) or not re.fullmatch(r"[0-9a-f]{64}", expected_sha256):
        raise Refusal("Expected lowercase index SHA256")
    if any(not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{40}", value) for value in (revision, tree)):
        raise Refusal("Exact lowercase SHA-1 committed revision/tree required")
    parents = []
    try:
        for path in (index, archive, receipt):
            _outside_git(path, set())
            parents.append(_Parent(path))
        original, raw = _read_index(parents[0], expected_sha256)
        format_proof = _index_format(raw)
        protected = _protected(repo, index)
        for path in (index, archive, receipt):
            _outside_git(path, protected)
        parents[1].fresh()
        parents[2].fresh()
        proof = _semantic_proof(repo, index, revision, tree, format_proof["entries"])
        _unchanged(parents[0], original)
        # The footer is closed before fsync; errors retain the new archive too.
        for path in (index, archive, receipt):
            _outside_git(path, protected)
        with os.fdopen(_new_file(parents[1]), "wb") as stream:
            with gzip.GzipFile(filename="", fileobj=stream, mode="wb", compresslevel=9, mtime=0) as zipped:
                for offset in range(0, len(raw), 65536):
                    zipped.write(raw[offset:offset + 65536])
            stream.flush()
            os.fsync(stream.fileno())
        os.fsync(parents[1].fd)
        archived = _verify_archive(parents[1], original["sha256"], original["bytes"])
        _unchanged(parents[0], original)
        # Re-read input body as well as identity after all Git/archive operations.
        again, _ = _read_index(parents[0], expected_sha256)
        if again != original:
            raise Refusal("Original index changed before receipt")
        value = dict(schema="9to1.closed-root-index-archive/v1",
            status="PREPARED_NO_UNLINK", prepared_at=datetime.datetime.now(datetime.timezone.utc).isoformat(),
            original=original, archive=archived, committed=proof, binary_format=format_proof,
            closed_asserted_by_caller=True, adopted=False, raw_removed=False,
            sdk_launch_admitted=False,
            restore="Verify archive SHA and whole decompressed raw SHA/length; restore only to an exclusive absent exact alternative-index path with original mode/mtime. Never overwrite any repository/worktree/current index. New inode identity must be recorded.",
            next_action="Root reviews and adopts this exact receipt/archive before separately rechecking identity/custody and reclaiming the duplicate. This prepare-only tool never unlinks or changes refs/source/ledger.")
        for path in (index, archive, receipt):
            _outside_git(path, protected)
        payload = workflow.encoded(value)
        with os.fdopen(_new_file(parents[2]), "wb") as stream:
            stream.write(payload)
            stream.flush()
            os.fsync(stream.fileno())
            receipt_identity = _identity(os.fstat(stream.fileno()))
        os.fsync(parents[2].fd)
        _unchanged(parents[0], original)
        for parent in parents:
            parent.current()
        for path in (index, archive, receipt):
            _outside_git(path, protected)
        _verify_receipt(parents[2], receipt_identity, payload)
        if _regular(parents[1]) != {key: archived[key] for key in FIELDS}:
            raise Refusal("Archive changed before completion")
        return value
    finally:
        for parent in parents:
            parent.close()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--index", type=Path, required=True)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--revision", required=True)
    parser.add_argument("--tree", required=True)
    parser.add_argument("--archive", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--closed", action="store_true", required=True,
                        help="Caller asserts this Root alternative index is closed, never active")
    args = parser.parse_args(argv)
    try:
        value = prepare(args.repo, args.index, args.expected_sha256, args.revision,
                        args.tree, args.archive, args.receipt, closed=args.closed)
        print(json.dumps(dict(status=value["status"], archive=str(args.archive),
                              receipt=str(args.receipt), raw_removed=False,
                              sdk_launch_admitted=False)))
        return 0
    except (Refusal, OSError, ValueError, EOFError, subprocess.SubprocessError) as error:
        print(json.dumps(dict(status="REFUSED_NO_UNLINK", reason=str(error))), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
