#!/usr/bin/env python3
"""Create a reproducible unsigned inventory for a completed native publish tree.

This is build evidence, never installation authority. A trusted installer must
authenticate the publisher/package, issue its own protected receipt, and bind
the running OS process before Home can grant service access.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import tempfile


def stable_digest(path):
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError("Publish inventory requires regular files")
        digest = hashlib.sha256()
        with os.fdopen(descriptor, "rb", closefd=False) as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        after = os.fstat(descriptor)
        identity = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
        if identity(before) != identity(after):
            raise ValueError("Publish file changed during inventory")
        return before.st_size, digest.hexdigest(), identity(before)
    finally:
        os.close(descriptor)


def inventory(root, app_id, version, entry_point):
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,127}", app_id):
        raise ValueError("A stable application identifier is required")
    if not version or len(version) > 128:
        raise ValueError("An explicit package version is required")
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Publish root must be an actual directory")
    root = root.resolve()
    entry = Path(entry_point)
    if entry.is_absolute() or ".." in entry.parts or not entry.parts:
        raise ValueError("Entry point must be a relative package file")
    entries = []
    directory_snapshots = []
    file_snapshots = []
    for directory, directories, files in os.walk(root, followlinks=False):
        directory_path = Path(directory)
        before = directory_path.lstat()
        if not stat.S_ISDIR(before.st_mode) or not directory_path.resolve().is_relative_to(root):
            raise ValueError("Publish directory identity changed or escaped its root")
        directory_snapshots.append((directory_path, before))
        for name in directories + files:
            if Path(directory, name).is_symlink():
                raise ValueError("Publish package contains a symlink; materialize and verify it before packaging")
        for name in sorted(files):
            path = Path(directory, name)
            size, digest, snapshot = stable_digest(path)
            file_snapshots.append((path, snapshot))
            entries.append({"path": path.relative_to(root).as_posix(), "size": size, "sha256": digest})
    for path, snapshot in file_snapshots:
        after = path.lstat()
        identity = (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)
        if not stat.S_ISREG(after.st_mode) or identity != snapshot:
            raise ValueError("Publish file changed before inventory completed")
    for path, before in directory_snapshots:
        after = path.lstat()
        identity = lambda value: (value.st_dev, value.st_ino, value.st_mode, value.st_mtime_ns, value.st_ctime_ns)
        if identity(before) != identity(after) or not path.resolve().is_relative_to(root):
            raise ValueError("Publish directory changed during inventory")
    entries.sort(key=lambda item: item["path"])
    if entry.as_posix() not in {item["path"] for item in entries}:
        raise ValueError("Entry point is absent from the publish inventory")
    payload = {"schemaVersion": 1, "authority": "UnsignedBuildInventory", "appId": app_id,
               "packageVersion": version, "entryPoint": entry.as_posix(), "files": entries}
    canonical = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()
    return {**payload, "inventorySha256": hashlib.sha256(canonical).hexdigest(), "publisherSignature": None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish-root", type=Path, required=True)
    parser.add_argument("--app-id", required=True)
    parser.add_argument("--package-version", required=True)
    parser.add_argument("--entry-point", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.absolute()
    root = args.publish_root.resolve()
    if output.is_relative_to(root):
        parser.error("Inventory output must be outside the publish tree to avoid self-inclusion")
    try:
        result = inventory(args.publish_root, args.app_id, args.package_version, args.entry_point)
        output.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary = tempfile.mkstemp(prefix=".native-inventory-", dir=output.parent)
        try:
            with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
                json.dump(result, stream, sort_keys=True, indent=2)
                stream.write("\n")
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, output)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)
    except (OSError, ValueError) as error:
        parser.exit(1, "Native package inventory failed: " + str(error) + "\n")


if __name__ == "__main__":
    main()
