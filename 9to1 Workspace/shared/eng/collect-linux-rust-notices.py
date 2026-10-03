#!/usr/bin/env python3
"""Collect verified cached Rust notices for a Cargo.lock dependency superset.

This is source evidence, not a claim that all locked crates were linked, that
native/system notices are complete, or that source-offer obligations are met.
No network, Cargo invocation or source mutation is performed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import tomllib
import tarfile


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verified_file(root, relative, archive):
    path = root / relative
    if path.is_symlink() or not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Crate evidence escapes its source directory: " + relative)
    data = path.read_bytes()
    if len(data) > 2 * 1024 * 1024:
        raise ValueError("Crate evidence exceeds the bounded text size: " + relative)
    with tarfile.open(archive, "r:gz") as packed:
        member = packed.getmember(root.name + "/" + relative)
        if not member.isfile() or member.size > 2 * 1024 * 1024:
            raise ValueError("Invalid archive evidence member: " + relative)
        original = packed.extractfile(member).read()
        if original != data:
            raise ValueError("Cached evidence differs from the checksum-pinned crate archive: " + relative)
    return data


def collect(locks, registry):
    packages = {}
    for lock in locks:
        for package in tomllib.loads(lock.read_text())["package"]:
            key = (package["name"], package["version"], package.get("source", ""))
            previous = packages.get(key)
            if previous and previous.get("checksum") != package.get("checksum"):
                raise ValueError("Conflicting locked package checksum: " + str(key))
            packages[key] = package
    entries, texts = [], {}
    for (name, version, source), package in sorted(packages.items()):
        if not re.fullmatch(r"[A-Za-z0-9_.+-]+", name) or not re.fullmatch(r"[A-Za-z0-9_.+-]+", version):
            raise ValueError("Unsafe package locator")
        entry = {"name": name, "version": version, "source": source, "texts": []}
        entries.append(entry)
        checksum = package.get("checksum")
        if not source.startswith("registry+") or not checksum:
            entry["status"] = "owning-source-license-required"
            continue
        candidates = list(registry.glob("*/" + name + "-" + version))
        matching = []
        for candidate in candidates:
            if candidate.is_symlink() or not candidate.resolve().is_relative_to(registry.resolve()):
                raise ValueError("Registry package escapes its cache")
            archive = registry.parent / "cache" / candidate.parent.name / (name + "-" + version + ".crate")
            if not archive.is_file():
                continue
            if archive.is_symlink() or archive.stat().st_size > 64 * 1024 * 1024:
                raise ValueError("Invalid or oversized crate archive")
            if sha(archive.read_bytes()) == checksum:
                matching.append((candidate, archive))
        if not matching:
            entry["status"] = "matching-cached-source-or-pinned-archive-missing"
            continue
        root, archive = matching[0]
        metadata = tomllib.loads(verified_file(root, "Cargo.toml", archive).decode())
        declaration = metadata.get("package", {})
        if declaration.get("name") != name or str(declaration.get("version")) != version:
            raise ValueError("Cached manifest identity does not match the lock")
        entry.update(checksum=checksum, sourceDirectory=str(root), license=declaration.get("license"))
        paths = {p.name for p in root.iterdir() if p.is_file() and
                 re.match(r"^(license|copying|notice|copyright)([._-]|$)", p.name, re.I)}
        if declaration.get("license-file"):
            paths.add(declaration["license-file"])
        for relative in sorted(paths):
            data = verified_file(root, relative, archive)
            digest = sha(data)
            destination = "texts/" + digest + ".txt"
            texts[destination] = data
            entry["texts"].append({"source": relative, "path": destination, "sha256": digest, "bytes": len(data)})
        entry["status"] = "available-verified-text" if paths else "license-text-missing"
    return entries, texts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cargo-lock", type=Path, action="append", required=True)
    parser.add_argument("--registry-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--require-complete", action="store_true")
    args = parser.parse_args()
    if args.output.exists() or args.output.is_symlink():
        parser.error("Use a new output directory; retained evidence is never overwritten")
    entries, texts = collect(args.cargo_lock, args.registry_source)
    report = {"scope": "Cargo.lock dependency superset; not proof of linked/native/system/source-offer closure",
              "locks": [{"path": str(p), "sha256": sha(p.read_bytes())} for p in args.cargo_lock],
              "entries": entries}
    args.output.mkdir(parents=True)
    for relative, data in texts.items():
        destination = args.output / relative
        destination.parent.mkdir(exist_ok=True)
        destination.write_bytes(data)
    (args.output / "inventory.json").write_text(json.dumps(report, indent=2) + "\n")
    missing = sum(e["status"] != "available-verified-text" for e in entries)
    print(json.dumps({"packages": len(entries), "uniqueTexts": len(texts), "unresolved": missing}))
    return 2 if args.require_complete and missing else 0


if __name__ == "__main__":
    raise SystemExit(main())
