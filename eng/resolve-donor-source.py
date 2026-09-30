#!/usr/bin/env python3
"""Resolve an app's declared donor to actual pinned source, including portable shared bindings.

This prints one actual source directory for build/consumer tooling. It does not
claim assets, recursive dependencies, compilation, or runtime parity are complete.
"""
import argparse
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

def resolve(app: str, donor: str) -> Path:
    registry = json.loads((ROOT / "eng/donor-sources.json").read_text(encoding="utf-8"))
    if registry.get("schemaVersion") != 1:
        raise ValueError("Unsupported donor registry schema")
    matches = [b for b in registry["bindings"] if b["app"] == app and b["donor"] == donor]
    if len(matches) != 1:
        raise ValueError("Expected one canonical app/donor binding")
    binding = matches[0]
    record = registry["upstreams"][donor]
    primary = binding.get("sharedSourcePath", binding["path"])
    if primary != binding["path"]:
        envelope = json.loads((ROOT / binding["path"] / "source.binding.json").read_text(encoding="utf-8"))
        expected = {"schemaVersion": 1, "donor": donor, "canonicalSourcePath": primary,
                    "commit": record["commit"], "sourceRepository": record.get("sourceRepository", record.get("fork", record["repository"]))}
        if envelope != expected or not any(b["path"] == primary and b["donor"] == donor and not b.get("sharedSourcePath") for b in registry["bindings"]):
            raise ValueError("Invalid portable shared source binding")
    source = (ROOT / primary).resolve(strict=True)
    if not source.is_dir() or not source.is_relative_to(ROOT):
        raise ValueError("Source must resolve inside this repository")
    if donor != "files":
        commit = subprocess.check_output(["git", "-C", str(source), "rev-parse", "HEAD"], text=True).strip()
        if commit != record["commit"]:
            raise ValueError("Source HEAD differs from the selected immutable commit")
    if any(not (source / name).is_file() for name in record["licenseFiles"]):
        raise ValueError("Source licence/notice is missing")
    return source

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", required=True)
    parser.add_argument("--donor", required=True)
    arguments = parser.parse_args()
    try:
        print(resolve(arguments.app, arguments.donor))
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Donor source unavailable: {error}\n")
