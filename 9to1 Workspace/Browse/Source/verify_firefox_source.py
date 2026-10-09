#!/usr/bin/env python3
"""Verify the complete, pinned Firefox donor checkout. Does not claim runtime parity."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from datetime import datetime, timezone

SOURCE_PATH = "9to1 Workspace/Browse/Source/FirefoxGecko"
SOURCE_URL = "https://github.com/mozilla-firefox/firefox.git"
REQUIRED_FILES = (
    "LICENSE", "mach", "moz.build", "browser/app/nsBrowserApp.cpp",
    "toolkit/components/places/nsNavBookmarks.cpp",
    "toolkit/components/downloads/Downloads.sys.mjs",
    "dom/moz.build", "layout/moz.build",
)


def git(root: Path, *args: str) -> str:
    result = subprocess.run(
        ["git", "-C", str(root), *args], check=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        encoding="utf-8", errors="surrogateescape", timeout=180,
    )
    return result.stdout


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def verify(root: Path, lock: dict) -> dict:
    root = root.resolve()
    require(lock.get("schema") == 1, "Unsupported source lock schema")
    require(lock.get("path") == SOURCE_PATH, "Unexpected donor path")
    require(lock.get("url") == SOURCE_URL, "Unexpected donor repository")
    for key in ("commit", "tree"):
        require(bool(re.fullmatch(r"[0-9a-f]{40}", str(lock.get(key, "")))),
                f"Invalid locked {key}")
    source = root / SOURCE_PATH
    require(source.resolve().is_relative_to(root), "Donor path escapes the checkout")
    entry = git(root, "ls-tree", "-z", "HEAD", "--", SOURCE_PATH).rstrip("\0")
    expected = f"160000 commit {lock['commit']}\t{SOURCE_PATH}"
    require(entry == expected, "Missing or mismatched pinned Firefox gitlink")
    url = git(root, "config", "-f", ".gitmodules", "--get",
              f"submodule.{SOURCE_PATH}.url").strip()
    require(url == SOURCE_URL, "Submodule URL does not match the source lock")
    require((source / ".git").exists(), "Firefox source has not been checked out")
    actual_root = Path(git(source, "rev-parse", "--show-toplevel").strip()).resolve()
    require(actual_root == source.resolve(), "Donor is not an independent Git checkout")
    require(git(source, "rev-parse", "HEAD").strip() == lock["commit"],
            "Checked-out donor commit does not match the pin")
    require(git(source, "rev-parse", "HEAD^{tree}").strip() == lock["tree"],
            "Checked-out donor tree does not match upstream")
    flags = git(source, "ls-files", "-v", "-z").split("\0")
    require(not any(item.startswith(("S ", "s ")) for item in flags),
            "Sparse/skip-worktree checkout is not a complete donor import")
    require(not any(item and item[0].islower() for item in flags),
            "Assume-unchanged index entries can hide modifications")
    require(not git(source, "status", "--porcelain", "--untracked-files=no").strip(),
            "Donor has modified, staged, or missing tracked files")
    tracked = [p for p in git(source, "ls-files", "-z").split("\0") if p]
    require(bool(tracked), "Donor contains no tracked source files")
    missing = [p for p in tracked if not os.path.lexists(source / p)]
    require(not missing, f"Incomplete source checkout: {missing[:5]}")
    hashes = {}
    tracked_set = set(tracked)
    for relative in REQUIRED_FILES:
        require(relative in tracked_set, f"Native donor file is not tracked: {relative}")
        path = source / relative
        require(path.is_file(), f"Missing native donor file: {relative}")
        hashes[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    return {
        "checked_at_utc": datetime.now(timezone.utc).isoformat(),
        "source_url": SOURCE_URL, "source_commit": lock["commit"],
        "source_tree": lock["tree"], "parent_commit": git(root, "rev-parse", "HEAD").strip(),
        "tracked_files_present": len(tracked), "full_source_checkout_verified": True,
        "native_source_sha256": hashes,
        "browser_build": "not assessed", "cakeui_runtime_integration": "not assessed",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, help="Write the source verification JSON")
    args = parser.parse_args()
    try:
        location = Path(__file__).resolve()
        lock = json.loads(location.with_name("firefox-source.lock.json").read_text(encoding="utf-8"))
        evidence = verify(location.parents[3], lock)
        text = json.dumps(evidence, indent=2) + "\n"
        if args.evidence:
            args.evidence.parent.mkdir(parents=True, exist_ok=True)
            args.evidence.write_text(text, encoding="utf-8")
        print(text, end="")
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"Firefox source verification FAILED: {error}", file=sys.stderr)
        if isinstance(error, subprocess.CalledProcessError) and error.stderr:
            print(error.stderr, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
