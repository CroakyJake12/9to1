#!/usr/bin/env python3
"""Build and privately stage the *existing* native Haven Files host from pinned Git source.

This owner-invoked command is not a downloader, OAuth flow, test fixture UI or
release publisher. It leaves a genuine source/publish/package identity receipt.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import stat
import subprocess
import sys

from native_files_package_admission import (
    AdmissionError, digest_archive, install_archive, open_archive,
    open_private_parent, same_child, same_parent_path,
)

PUBLISHER = Path("9to1 Workspace/shared/eng/publish-haven-linux.sh")
PROJECT = Path("9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj")
ARCHIVE = "haven-linux-linux-x64.tar.gz"
SHA_PATTERN = re.compile(r"^[a-f0-9]{40}$")
SDK_PATTERN = re.compile(r"^10\.0\.(\d{3})$")


class CandidateError(Exception):
    """Fixed, non-secret validation and build refusal."""


def _command(argv: list[str], *, env: dict[str, str] | None = None,
             cwd: Path | None = None, timeout: int = 90) -> str:
    try:
        completed = subprocess.run(argv, cwd=cwd, env=env, text=True,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   check=False, timeout=timeout)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise CandidateError("Required local command failed to start or timed out") from exc
    if completed.returncode != 0:
        raise CandidateError("Required local command returned nonzero status")
    return completed.stdout.strip()


def source_identity(repo: Path, expected_sha: str) -> str:
    if not SHA_PATTERN.fullmatch(expected_sha):
        raise CandidateError("Expected source revision must be a full lowercase Git SHA-1")
    root = _command(["git", "-C", str(repo), "rev-parse", "--show-toplevel"])
    if Path(root).resolve() != repo.resolve():
        raise CandidateError("Source path must be the repository root")
    head = _command(["git", "-C", str(repo), "rev-parse", "HEAD"])
    if head != expected_sha:
        raise CandidateError("Checked-out source does not match expected revision")
    if _command(["git", "-C", str(repo), "status", "--porcelain=v1", "--untracked-files=all"]):
        raise CandidateError("Source checkout contains uncommitted or untracked files")
    if not (repo / PUBLISHER).is_file() or not (repo / PROJECT).is_file():
        raise CandidateError("Original native Haven publisher or project is missing")
    try:
        sdk = json.loads((repo / "global.json").read_text(encoding="utf-8"))["sdk"]
    except (OSError, KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        raise CandidateError("Repository does not declare a valid SDK pin") from exc
    if sdk.get("version") != "10.0.301" or sdk.get("rollForward") != "latestFeature" or sdk.get("allowPrerelease") is not False:
        raise CandidateError("Repository SDK policy changed; review the build contract")
    return head


def check_sdk(dotnet: str, *, env: dict[str, str], repo: Path) -> str:
    version = _command([dotnet, "--version"], env=env, cwd=repo, timeout=30)
    match = SDK_PATTERN.fullmatch(version)
    if match is None or int(match.group(1)) < 301:
        raise CandidateError("Selected .NET SDK does not satisfy repository global.json")
    return version


def private_environment(candidate: Path, current: dict[str, str]) -> dict[str, str]:
    """Allowlist benign build configuration; never inherit app/account credentials."""
    allowed = {"PATH", "LANG", "LC_ALL", "LC_CTYPE", "TERM", "TMPDIR",
               "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy",
               "https_proxy", "no_proxy", "DOTNET_ROOT", "DOTNET_ROOT_X64",
               "SSL_CERT_FILE", "SSL_CERT_DIR", "SYSTEMROOT"}
    env = {k: v for k, v in current.items() if k in allowed}
    home = candidate / "build-home"
    for folder in (home, home / "config", home / "data", home / "cache", home / "state", candidate / "nuget"):
        folder.mkdir(mode=0o700, exist_ok=False)
    env.update({
        "HOME": str(home), "DOTNET_CLI_HOME": str(home),
        "XDG_CONFIG_HOME": str(home / "config"), "XDG_DATA_HOME": str(home / "data"),
        "XDG_CACHE_HOME": str(home / "cache"), "XDG_STATE_HOME": str(home / "state"),
        "NUGET_PACKAGES": str(candidate / "nuget"),
        "HAVEN_ARTIFACTS_DIR": str(candidate / "artifacts"),
        "HAVEN_LINUX_RID": "linux-x64", "HAVEN_CONFIGURATION": "Release",
        "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "MSBUILDDISABLENODEREUSE": "1",
    })
    return env


def _file_index(root: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for path, dirs, files in os.walk(root, followlinks=False):
        base = Path(path)
        for name in dirs:
            if not stat.S_ISDIR((base / name).lstat().st_mode):
                raise CandidateError("Native publish has an unexpected linked directory")
        for name in files:
            child = base / name
            if not stat.S_ISREG(child.lstat().st_mode):
                raise CandidateError("Native publish has a nonregular output")
            h = hashlib.sha256()
            with child.open("rb") as f:
                for block in iter(lambda: f.read(1024 * 1024), b""):
                    h.update(block)
            result[str(child.relative_to(root))] = h.hexdigest()
    return result


def make_private_candidate(destination: Path) -> tuple[int, int]:
    if not destination.is_absolute() or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,100}", destination.name):
        raise CandidateError("Candidate must be a new absolute safe-named directory")
    try:
        parent_fd = open_private_parent(destination.parent)
    except (OSError, AdmissionError) as exc:
        raise CandidateError("Candidate parent must be an existing user-private mode-0700 directory") from exc
    try:
        os.mkdir(destination.name, mode=0o700, dir_fd=parent_fd)
        root_fd = os.open(destination.name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent_fd)
        return parent_fd, root_fd
    except (OSError, AdmissionError) as exc:
        os.close(parent_fd)
        raise CandidateError("Candidate destination must be a new private named directory") from exc


def build_candidate(repo: Path, expected_sha: str, destination: Path, *, dotnet: str = "dotnet") -> dict[str, str]:
    repo = repo.absolute()
    revision = source_identity(repo, expected_sha)
    # Prevent generated binaries, build caches or receipts appearing in the source tree.
    if destination.resolve(strict=False).is_relative_to(repo.resolve()):
        raise CandidateError("Candidate output must be outside the source repository")
    parent_fd, root_fd = make_private_candidate(destination)
    try:
        if not same_child(parent_fd, destination.name, root_fd) or not same_parent_path(parent_fd, destination.parent):
            raise CandidateError("Candidate destination changed during setup")
        env = private_environment(destination, os.environ)
        sdk = check_sdk(dotnet, env=env, repo=repo)
        # Ensure no file was changed *while* preparing the private build environment.
        source_identity(repo, expected_sha)
        _command(["bash", str(repo / PUBLISHER)], cwd=repo, env=env, timeout=3300)
        source_identity(repo, expected_sha)
        archive = destination / "artifacts" / ARCHIVE
        publish = destination / "artifacts" / "publish"
        if not archive.is_file() or not publish.is_dir():
            raise CandidateError("Original native publisher did not produce a Haven archive")
        with open_archive(archive) as handle:
            sha = hashlib.file_digest(handle, "sha256").hexdigest()
        if digest_archive(archive, sha) != sha:
            raise CandidateError("Published archive digest could not be verified")
        result = install_archive(archive, sha, destination / "installed")
        if result.get("installed") is not True or _file_index(publish) != _file_index(destination / "installed"):
            raise CandidateError("Published bytes differ from the admitted extracted runtime")
        if not same_child(parent_fd, destination.name, root_fd) or not same_parent_path(parent_fd, destination.parent):
            raise CandidateError("Candidate destination changed during publishing")
        receipt = {
            "schema": "9to1.native-files-source-candidate/v1",
            "status": "BUILT_NOT_DRIVE_ACCEPTED",
            "source_revision": revision, "dotnet_sdk": sdk,
            "target": "linux-x64", "configuration": "Release",
            "archive_sha256": sha,
            "archive": str(archive),
            "launch_executable": str(destination / "installed" / "Haven"),
            "launch_command": f"cd {shlex.quote(str(destination / 'installed'))} && ./Haven",
            "publisher": str(PUBLISHER),
            "oauth_checks": "NOT_RUN", "real_drive_checks": "NOT_RUN",
            "graphical_launch": "NOT_RUN",
        }
        # No existing receipt may be overwritten, even if a stale build exists.
        fd = os.open("receipt.json", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o600, dir_fd=root_fd)
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            json.dump(receipt, f, sort_keys=True, indent=2)
            f.write("\n")
            f.flush()
            os.fsync(f.fileno())
        return receipt
    except (AdmissionError, OSError, ValueError) as exc:
        raise CandidateError("Native source candidate build or package admission failed") from exc
    finally:
        os.close(root_fd)
        os.close(parent_fd)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True, type=Path)
    parser.add_argument("--expect-revision", required=True)
    parser.add_argument("--candidate-dir", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet", help="Official compatible dotnet CLI executable")
    args = parser.parse_args(argv)
    try:
        result = build_candidate(args.repo, args.expect_revision, args.candidate_dir, dotnet=args.dotnet)
        print(json.dumps(result, sort_keys=True))
        return 0
    except CandidateError as exc:
        print(json.dumps({"status": "BLOCKED", "reason": str(exc)}, sort_keys=True))
        return 2


if __name__ == "__main__":
    sys.exit(main())
