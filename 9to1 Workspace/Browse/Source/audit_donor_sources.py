#!/usr/bin/env python3
"""Inventory committed donor declarations versus actual Git tree entries, not runtime parity."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

from verify_firefox_source import git


def audit(root: Path) -> dict:
    records = git(root, "ls-tree", "-r", "-z", "HEAD").split("\0")
    entries = {}
    for record in records:
        if record:
            metadata, path = record.split("\t", 1)
            mode, kind, sha = metadata.split()
            entries[path] = {"mode": mode, "type": kind, "sha": sha}
    raw = git(root, "config", "--blob", "HEAD:.gitmodules", "-z", "--get-regexp",
              r"^submodule\..*\.path$")
    donors = []
    for item in raw.split("\0"):
        if not item:
            continue
        key, path = item.split("\n", 1)
        entry = entries.get(path)
        children = [name for name in entries if name.startswith(path + "/")]
        if entry and entry["mode"] == "160000":
            state = "pinned_gitlink"
        elif entry:
            state = "file_instead_of_donor_directory"
        elif children:
            state = "tracked_directory_requires_native_source_audit"
        else:
            state = "declared_but_untracked"
        donors.append({"path": path, "state": state, "entry": entry,
                       "tracked_entries_below_path": len(children)})
    declared = {item["path"] for item in donors}
    return {
        "parent_commit": git(root, "rev-parse", "HEAD").strip(),
        "scope": "Committed .gitmodules declarations only; not an exhaustive comparison to the product spec",
        "declared_count": len(donors),
        "pinned_gitlink_count": sum(item["state"] == "pinned_gitlink" for item in donors),
        "declared_but_untracked_count": sum(item["state"] == "declared_but_untracked" for item in donors),
        "donors": donors,
        "undeclared_gitlinks": {path: entry["sha"] for path, entry in entries.items()
                                if entry["mode"] == "160000" and path not in declared},
        "runtime_integration": "not assessed",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path)
    args = parser.parse_args()
    try:
        text = json.dumps(audit(Path(__file__).resolve().parents[3]), indent=2) + "\n"
        if args.evidence:
            args.evidence.parent.mkdir(parents=True, exist_ok=True)
            args.evidence.write_text(text, encoding="utf-8")
        print(text, end="")
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"Donor source inventory FAILED: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
