#!/usr/bin/env python3
"""Materialize only the two reviewed Avalonia build gitlinks from local Git objects."""
import hashlib
import os
from pathlib import Path
import subprocess
import time

PINS = {
    "framework/CUI/vendor/Avalonia/external/XamlX": "009d4815470cf4bf71d1adbb633a5d81dcb2bb52",
    "framework/CUI/vendor/Avalonia/external/Avalonia.DBus": "864a05282841bf04006890f04d11d60d1a046aa9",
}


def _git(cwd, *args, timeout=45):
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GIT_ALLOW_PROTOCOL="file",
               GIT_OPTIONAL_LOCKS="0")
    result = subprocess.run(["git", *args], cwd=str(cwd), env=env,
                            capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError("Build dependency Git operation failed: " +
                           result.stderr[-1200:].strip())
    return result.stdout.strip()


def _clean_at(path, expected):
    if Path(_git(path, "rev-parse", "--show-toplevel")).resolve() != path.resolve():
        raise RuntimeError("Dependency is not its own Git checkout: " + str(path))
    actual = _git(path, "rev-parse", "HEAD")
    if actual != expected:
        raise RuntimeError("Dependency revision differs; preserve existing checkout: " + str(path))
    if _git(path, "status", "--porcelain", "--untracked-files=normal"):
        raise RuntimeError("Dependency has existing changes; preserve them: " + str(path))


def ensure(worktree, repo):
    """Return verified dependency receipts; never fetch, repin or alter existing work."""
    worktree, repo = Path(worktree).resolve(), Path(repo).resolve()
    if worktree == repo:
        raise RuntimeError("Dependency preparation requires an isolated worktree")
    for directory in (worktree, repo):
        if Path(_git(directory, "rev-parse", "--show-toplevel")).resolve() != directory:
            raise RuntimeError("Expected repository root: " + str(directory))
    common = Path(_git(repo, "rev-parse", "--git-common-dir"))
    if not common.is_absolute():
        common = repo / common
    config = common.resolve() / "config"
    config_before = hashlib.sha256(config.read_bytes()).hexdigest()
    receipt = {"at": time.time(), "worktree": str(worktree), "dependencies": []}
    for relative, reviewed_pin in PINS.items():
        tree = _git(worktree, "ls-tree", "HEAD", "--", relative)
        fields = tree.split(None, 3)
        if len(fields) != 4 or fields[:2] != ["160000", "commit"] or fields[3] != relative:
            raise RuntimeError("Expected tracked dependency gitlink: " + relative)
        pin = fields[2]
        if pin != reviewed_pin:
            raise RuntimeError("Dependency pin changed; review required: " + relative)
        source, target = repo / relative, worktree / relative
        if source.is_symlink() or target.is_symlink():
            raise RuntimeError("Dependency path cannot be a symlink: " + relative)
        _clean_at(source, pin)
        state = "already_ready"
        if not (target / ".git").exists():
            if target.exists() and (not target.is_dir() or any(target.iterdir())):
                raise RuntimeError("Unexpected dependency contents; preserve them: " + str(target))
            target.parent.mkdir(parents=True, exist_ok=True)
            # Both modules are already registered in the shared canonical config.
            # Omitting --init prevents any registration writes to that shared config.
            _git(worktree,
                 "-c", "protocol.file.allow=always",
                 "-c", "submodule." + relative + ".active=true",
                 "-c", "submodule." + relative + ".url=" + str(source),
                 "submodule", "update", "--no-fetch", "--", relative)
            state = "initialized"
        _clean_at(target, pin)
        if _git(worktree, "diff", "--name-only", "HEAD", "--", relative):
            raise RuntimeError("Dependency gitlink changed: " + relative)
        receipt["dependencies"].append({"path": relative, "commit": pin, "state": state})
    config_after = hashlib.sha256(config.read_bytes()).hexdigest()
    if config_after != config_before:
        raise RuntimeError("Shared Git config changed during dependency preparation; inspect concurrent activity")
    receipt["canonical_config_unchanged"] = True
    receipt["canonical_config_sha256"] = config_after
    return receipt
