#!/usr/bin/env python3
"""Launch the *existing* privately built Haven Linux x64 app with an explicit profile.

This verifies bytes, it does NOT perform Google consent or accept Drive operations.
The candidate must have been produced by native_files_source_candidate.py.
"""
from __future__ import annotations

import argparse
from contextlib import ExitStack
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tarfile

ARCHIVE = "haven-linux-linux-x64.tar.gz"
NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}\Z")
SHA = re.compile(r"[a-f0-9]{40}\Z")
DIGEST = re.compile(r"[a-f0-9]{64}\Z")
MAX_ARCHIVE = 1024 * 1024 * 1024
MAX_UNPACKED = 4 * 1024 * 1024 * 1024


class LaunchError(Exception):
    """Only constant, path-free error messages are shown on standard output."""


def _same(a, b):
    return (a.st_dev, a.st_ino, a.st_mode, a.st_size, a.st_mtime_ns, a.st_ctime_ns) == (b.st_dev, b.st_ino, b.st_mode, b.st_size, b.st_mtime_ns, b.st_ctime_ns)


def _private_dir(path: Path) -> int:
    """Anchor every ancestor from /; refuse intermediate or final symlinks."""
    if not path.is_absolute() or any(part in ("", ".", "..") for part in path.parts[1:]):
        raise LaunchError("Select an absolute directory without traversal")
    fd = os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
    try:
        for part in path.parts[1:]:
            new = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=fd)
            os.close(fd)
            fd = new
        s = os.fstat(fd)
        if s.st_uid != os.geteuid() or stat.S_IMODE(s.st_mode) != 0o700:
            raise LaunchError("Selected directory must be user-owned and mode 0700")
        return fd
    except BaseException:
        os.close(fd)
        raise


def _dir(parent: int, name: str) -> int:
    if not NAME.fullmatch(name):
        raise LaunchError("Unsafe candidate component")
    fd = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
    s = os.fstat(fd)
    if s.st_uid != os.geteuid() or stat.S_IMODE(s.st_mode) != 0o700:
        os.close(fd)
        raise LaunchError("Candidate and profile directories must be private")
    return fd


def _file(parent: int, name: str, *, max_bytes: int = MAX_ARCHIVE) -> int:
    if not NAME.fullmatch(name):
        raise LaunchError("Unsafe candidate component")
    fd = os.open(name, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=parent)
    s = os.fstat(fd)
    if not stat.S_ISREG(s.st_mode) or s.st_uid != os.geteuid() or s.st_size > max_bytes or s.st_nlink != 1:
        os.close(fd)
        raise LaunchError("Candidate contains unsafe file metadata")
    return fd


def _read_receipt(parent: int) -> dict:
    fd = _file(parent, "receipt.json", max_bytes=16384)
    with os.fdopen(fd, "rb") as f:
        before = os.fstat(f.fileno())
        if stat.S_IMODE(before.st_mode) != 0o600:
            raise LaunchError("Candidate receipt must be mode 0600")
        data = f.read(16385)
        if not _same(before, os.fstat(f.fileno())) or len(data) > 16384:
            raise LaunchError("Candidate receipt changed during inspection")
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise LaunchError("Candidate receipt contains duplicate keys")
            result[key] = value
        return result
    try:
        receipt = json.loads(data, object_pairs_hook=pairs)
    except (ValueError, UnicodeError) as exc:
        raise LaunchError("Candidate receipt is invalid") from exc
    if not isinstance(receipt, dict):
        raise LaunchError("Candidate receipt is invalid")
    return receipt


def _tree_index(dirfd: int) -> dict[str, str]:
    result = {}
    def walk(fd: int, prefix: str):
        first = os.fstat(fd)
        with os.scandir(fd) as it:
            entries = sorted([(e.name, e.stat(follow_symlinks=False)) for e in it])
        for name, initial in entries:
            if not NAME.fullmatch(name):
                raise LaunchError("Extracted runtime contains unsafe entries")
            key = prefix + name
            if stat.S_ISDIR(initial.st_mode):
                child = _dir(fd, name)
                try:
                    if not _same(initial, os.fstat(child)):
                        raise LaunchError("Extracted runtime changed during inspection")
                    walk(child, key + "/")
                finally:
                    os.close(child)
            elif stat.S_ISREG(initial.st_mode):
                child = _file(fd, name, max_bytes=MAX_UNPACKED)
                with os.fdopen(child, "rb") as f:
                    if not _same(initial, os.fstat(f.fileno())):
                        raise LaunchError("Extracted runtime changed during inspection")
                    h = hashlib.sha256()
                    for chunk in iter(lambda: f.read(1024 * 1024), b""):
                        h.update(chunk)
                    if not _same(initial, os.fstat(f.fileno())):
                        raise LaunchError("Extracted runtime changed during inspection")
                    result[key] = h.hexdigest()
            else:
                raise LaunchError("Extracted runtime contains a link or special file")
            if not _same(initial, os.stat(name, dir_fd=fd, follow_symlinks=False)):
                raise LaunchError("Extracted runtime entry was replaced")
        if not _same(first, os.fstat(fd)):
            raise LaunchError("Extracted runtime directory was modified")
    walk(dirfd, "")
    return result


def _directory_index(rootfd: int) -> set[str]:
    names = set()
    def walk(fd, prefix):
        with os.scandir(fd) as entries:
            children = [(e.name, e.stat(follow_symlinks=False)) for e in entries]
        for name, before in children:
            if not NAME.fullmatch(name):
                raise LaunchError("Extracted runtime contains unsafe entries")
            if not stat.S_ISDIR(before.st_mode):
                continue
            nextfd = _dir(fd, name)
            try:
                if not _same(before, os.fstat(nextfd)):
                    raise LaunchError("Extracted runtime directory changed")
                key = prefix + name
                names.add(key)
                walk(nextfd, key + "/")
                if not _same(before, os.stat(name, dir_fd=fd, follow_symlinks=False)):
                    raise LaunchError("Extracted runtime directory was replaced")
            finally:
                os.close(nextfd)
    walk(rootfd, "")
    return names


def _archive_index(fd: int, expected: str) -> tuple[dict[str, str], set[str]]:
    before = os.fstat(fd)
    digest = hashlib.sha256()
    with os.fdopen(os.dup(fd), "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            digest.update(chunk)
    if not _same(before, os.fstat(fd)) or digest.hexdigest() != expected:
        raise LaunchError("Candidate archive checksum or identity is invalid")
    result = {}
    archived_dirs = set()
    total = 0
    # dup() shares the open-file description and offset with the digest pass.
    os.lseek(fd, 0, os.SEEK_SET)
    with os.fdopen(os.dup(fd), "rb") as source:
        try:
            with tarfile.open(fileobj=source, mode="r:gz") as archive:
                count = 0
                for member in archive:
                    count += 1
                    if count > 50000:
                        raise LaunchError("Candidate archive contains too many entries")
                    raw = member.name.removeprefix("./").rstrip("/")
                    if raw in ("", ".") and member.isdir():
                        continue
                    parts = raw.split("/")
                    if any(not NAME.fullmatch(p) for p in parts):
                        raise LaunchError("Candidate archive contains unsafe member")
                    if member.isdir():
                        if raw in archived_dirs:
                            raise LaunchError("Candidate archive contains duplicate directories")
                        archived_dirs.add(raw)
                        continue
                    if not member.isfile() or member.issparse() or raw in result:
                        raise LaunchError("Candidate archive contains links or duplicate members")
                    total += member.size
                    if member.size < 0 or total > MAX_UNPACKED:
                        raise LaunchError("Candidate archive exceeds size limits")
                    stream = archive.extractfile(member)
                    if stream is None:
                        raise LaunchError("Candidate archive member cannot be read")
                    with stream:
                        h = hashlib.sha256()
                        for block in iter(lambda: stream.read(1024 * 1024), b""):
                            h.update(block)
                        result[raw] = h.hexdigest()
        except (OSError, tarfile.TarError, EOFError) as exc:
            raise LaunchError("Candidate archive is invalid") from exc
    if not _same(before, os.fstat(fd)):
        raise LaunchError("Candidate archive changed during verification")
    if any(parent not in archived_dirs for name in result
           for parent in ("/".join(name.split("/")[:depth])
                          for depth in range(1, len(name.split("/"))))):
        raise LaunchError("Candidate archive omits a runtime directory")
    if not {"Haven", "Haven.dll", "Haven.runtimeconfig.json"}.issubset(result):
        raise LaunchError("Candidate archive is missing Haven runtime")
    return result, archived_dirs


def _profile(path: Path, create: bool, stack: ExitStack) -> int:
    if not NAME.fullmatch(path.name) or not path.is_absolute():
        raise LaunchError("Use an explicit absolute safe-named owner profile")
    parent = _private_dir(path.parent)
    stack.callback(os.close, parent)
    if create:
        os.mkdir(path.name, mode=0o700, dir_fd=parent)  # no clobber
    root = _dir(parent, path.name)
    stack.callback(os.close, root)
    try:
        fcntl.flock(root, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError as exc:
        raise LaunchError("Owner profile is already used by this launcher") from exc
    for name in ("config", "data", "cache", "state", "haven-data"):
        try:
            os.mkdir(name, mode=0o700, dir_fd=root)
        except FileExistsError:
            pass
        checked = _dir(root, name)
        os.close(checked)
    return root


def _environment(profile: Path) -> dict[str, str]:
    allowed = {"PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "DISPLAY",
               "XAUTHORITY", "WAYLAND_DISPLAY", "XDG_RUNTIME_DIR", "XDG_SESSION_TYPE",
               "DBUS_SESSION_BUS_ADDRESS", "DOTNET_ROOT", "DOTNET_ROOT_X64",
               "SSL_CERT_FILE", "SSL_CERT_DIR", "GDK_BACKEND", "LD_LIBRARY_PATH", "TZ"}
    env = {k: v for k, v in os.environ.items() if k in allowed}
    env.update({"HOME": str(profile), "DOTNET_CLI_HOME": str(profile),
                "XDG_CONFIG_HOME": str(profile / "config"),
                "XDG_DATA_HOME": str(profile / "data"), "XDG_CACHE_HOME": str(profile / "cache"),
                "XDG_STATE_HOME": str(profile / "state"),
                "HAVEN_DATA_DIR": str(profile / "haven-data"),
                "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
    return env


def launch(candidate: Path, profile: Path, revision: str, *, create_profile: bool,
           dotnet: str = "dotnet", runner=subprocess.run) -> dict:
    if not sys.platform.startswith("linux"):
        raise LaunchError("Native Files candidate requires Linux")
    if not SHA.fullmatch(revision):
        raise LaunchError("Expected revision must be a complete lowercase Git SHA")
    if candidate == profile or candidate in profile.parents or profile in candidate.parents:
        raise LaunchError("Candidate and persistent profile must be separate")
    if not candidate.is_absolute() or not NAME.fullmatch(candidate.name):
        raise LaunchError("Candidate path must be absolute and safe-named")
    with ExitStack() as stack:
        cpfd = _private_dir(candidate.parent)
        stack.callback(os.close, cpfd)
        cfd = _dir(cpfd, candidate.name)
        stack.callback(os.close, cfd)
        receipt = _read_receipt(cfd)
        archive_path = candidate / "artifacts" / ARCHIVE
        exe = candidate / "installed" / "Haven"
        if (receipt.get("schema") != "9to1.native-files-source-candidate/v1" or
            receipt.get("status") != "BUILT_NOT_DRIVE_ACCEPTED" or
            receipt.get("source_revision") != revision or
            receipt.get("target") != "linux-x64" or
            receipt.get("archive") != str(archive_path) or
            receipt.get("launch_executable") != str(exe) or
            receipt.get("oauth_checks") != "NOT_RUN" or
            receipt.get("real_drive_checks") != "NOT_RUN"):
            raise LaunchError("Candidate receipt does not match the selected build")
        expected = receipt.get("archive_sha256")
        if not isinstance(expected, str) or not DIGEST.fullmatch(expected):
            raise LaunchError("Candidate receipt lacks valid archive digest")
        afd = _dir(cfd, "artifacts")
        stack.callback(os.close, afd)
        archive_fd = _file(afd, ARCHIVE)
        stack.callback(os.close, archive_fd)
        installed = _dir(cfd, "installed")
        stack.callback(os.close, installed)
        hostfd = _file(installed, "Haven")
        stack.callback(os.close, hostfd)
        host = os.fstat(hostfd)
        if not host.st_mode & stat.S_IXUSR or host.st_mode & 0o022:
            raise LaunchError("Haven apphost must be owner-executable and not group-writable")
        header = os.read(hostfd, 20)
        if not (len(header) == 20 and header[:5] == b"\x7fELF\x02" and header[18:20] == b"\x3e\x00"):
            raise LaunchError("Haven apphost is not a Linux x64 executable")
        expected_files, expected_dirs = _archive_index(archive_fd, expected)
        if _tree_index(installed) != expected_files or _directory_index(installed) != expected_dirs:
            raise LaunchError("Extracted Haven runtime differs from verified archive")
        if not _same(host, os.fstat(hostfd)) or not _same(host, os.stat("Haven", dir_fd=installed, follow_symlinks=False)):
            raise LaunchError("Haven apphost changed during verification")
        pfd = _profile(profile, create_profile, stack)
        profile_stat = os.fstat(pfd)
        env = _environment(profile)
        try:
            checked = runner([dotnet, "--list-runtimes"], env=env, capture_output=True,
                             text=True, timeout=15, check=False)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise LaunchError("Compatible .NET runtime could not be checked") from exc
        if checked.returncode != 0 or not re.search(r"^Microsoft\.NETCore\.App 10\.0\.[0-9]+\s", checked.stdout, re.M):
            raise LaunchError("A .NET 10 desktop runtime is required")
        if not _same(profile_stat, os.fstat(pfd)) or not _same(host, os.stat("Haven", dir_fd=installed, follow_symlinks=False)):
            raise LaunchError("Profile or Haven runtime changed before launch")
        # Original apphost, not a new wrapper UI, with no inherited Drive secrets.
        try:
            completed = runner([str(exe)], cwd=exe.parent, env=env, check=False)
        except OSError as exc:
            raise LaunchError("Existing Haven application could not start") from exc
        return {"status": "EXITED" if completed.returncode == 0 else "APPLICATION_EXIT_NONZERO",
                "source_revision": revision, "archive_sha256": expected,
                "profile": "OWNER_SELECTED_PERSISTENT", "profile_preserved": True,
                "oauth_acceptance": "NOT_RUN", "real_drive_acceptance": "NOT_RUN"}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--expect-revision", required=True)
    parser.add_argument("--create-profile", action="store_true")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args(argv)
    try:
        result = launch(args.candidate, args.profile, args.expect_revision,
                        create_profile=args.create_profile, dotnet=args.dotnet)
        print(json.dumps(result, sort_keys=True))
        return 0 if result["status"] == "EXITED" else 2
    except (LaunchError, OSError, ValueError, TypeError) as exc:
        # Fixed diagnostics: never expose arbitrary OS error/path/secret strings.
        reason = str(exc) if isinstance(exc, LaunchError) else "Private launch safety check failed"
        print(json.dumps({"status": "BLOCKED", "reason": reason,
                          "oauth_acceptance": "NOT_RUN", "real_drive_acceptance": "NOT_RUN"}, sort_keys=True))
        return 2


if __name__ == "__main__":
    sys.exit(main())
