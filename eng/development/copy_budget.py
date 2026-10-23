"""Read-only admission of a verified, incomplete set of owning build copies.

This is a budget for listed complete assets, not an MSBuild evaluator or a native
readiness check. A fit never authorizes SDK launch: unlisted/custom operations,
scratch, runtime/platform closure and the owning execution guard remain required.
Missing/stale assets reserve their whole rounded size. Possible hardlink fallback
never spends the reserve; only an existing verified identical output needs no copy.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys


RESERVE_BYTES = 256 * 1024 * 1024
MAX_MANIFEST_BYTES = 2 * 1024 * 1024
MAX_ROWS = 10000
SCHEMAS = {"9to1.copy-budget/v1",
           "9to1.failed-native17-fresh-output-duplicate-candidates/v1"}
IDENTITY_FIELDS = ("bytes", "allocated_bytes", "mode", "device", "inode",
                   "nlink", "mtime_ns", "ctime_ns")


class Refusal(ValueError):
    """The frozen inputs cannot support even the declared copy budget."""


def _integer(value, label, minimum=0):
    if type(value) is not int or value < minimum:
        raise Refusal(f"{label} must be an integer >= {minimum}")
    return value


def _sha(value):
    if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
        raise Refusal("Expected lowercase SHA256")
    return value


def _path(value):
    if not isinstance(value, str) or not value or "\0" in value:
        raise Refusal("Expected an absolute path")
    path = Path(value)
    if not path.is_absolute() or value.startswith("//") or ".." in path.parts:
        raise Refusal("Relative/traversing paths are refused")
    return path


def _inspect(path, missing_leaf=False, directory=False):
    """Refuse symlinks in every component, including a dangling output link."""
    current = Path(path.anchor)
    info = current.lstat()
    if len(path.parts) == 1 and not directory:
        raise Refusal(f"Non-regular file: {path}")
    for index, part in enumerate(path.parts[1:]):
        current /= part
        leaf = index == len(path.parts) - 2
        try:
            info = current.lstat()
        except FileNotFoundError:
            if leaf and missing_leaf:
                return None
            raise Refusal(f"Missing input/output parent: {current}")
        if stat.S_ISLNK(info.st_mode):
            raise Refusal(f"Symlink refused: {current}")
        if not leaf and not stat.S_ISDIR(info.st_mode):
            raise Refusal(f"Non-directory parent: {current}")
        if leaf and not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
            raise Refusal(f"Non-regular file: {current}")
    return info


def _identity(info):
    if not hasattr(info, "st_blocks"):
        raise Refusal("Allocated-block identity is unavailable on this platform")
    return dict(zip(IDENTITY_FIELDS, (
        info.st_size, info.st_blocks * 512, stat.S_IMODE(info.st_mode),
        info.st_dev, info.st_ino, info.st_nlink, info.st_mtime_ns,
        info.st_ctime_ns)))


def _read_verified(path, info, expected_sha, collect=False):
    # O_NOFOLLOW closes the leaf symlink race; fstat binds the actual opened file.
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    descriptor = os.open(path, flags)
    digest = hashlib.sha256()
    chunks = []
    with os.fdopen(descriptor, "rb") as stream:
        if _identity(os.fstat(stream.fileno())) != _identity(info):
            raise Refusal(f"Input changed before read: {path}")
        remaining = info.st_size
        while remaining:
            chunk = stream.read(min(1024 * 1024, remaining))
            if not chunk:
                break
            remaining -= len(chunk)
            digest.update(chunk)
            if collect:
                chunks.append(chunk)
        if remaining or stream.read(1):
            raise Refusal(f"Input length changed during read: {path}")
        if _identity(os.fstat(stream.fileno())) != _identity(info):
            raise Refusal(f"Input changed during read: {path}")
    if _identity(_inspect(path)) != _identity(info):
        raise Refusal(f"Input changed after read: {path}")
    if expected_sha is not None and digest.hexdigest() != expected_sha:
        raise Refusal(f"SHA256 mismatch: {path}")
    return b"".join(chunks) if collect else digest.hexdigest()


def _unique_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise Refusal(f"Duplicate JSON key: {key}")
        value[key] = item
    return value


def _invalid_constant(value):
    raise Refusal(f"Invalid JSON constant: {value}")


def _record(value, origin=False):
    if not isinstance(value, dict):
        raise Refusal("Expected file identity record")
    path = _path(value.get("path"))
    _integer(value.get("bytes"), "bytes")
    _sha(value.get("sha256"))
    for key in IDENTITY_FIELDS:
        if origin or key in value:
            _integer(value.get(key), key, 1 if key in ("inode", "nlink") else 0)
    if "mode" in value and value["mode"] > 0o7777:
        raise Refusal("Invalid mode")
    return path


def assess(manifest_path, expected_manifest_sha256):
    """Return a known-copy budget only; never execute, copy, link or remove files."""
    if not hasattr(os, "statvfs"):
        raise Refusal("Filesystem allocation/free-space observation is unavailable")
    manifest_path = _path(str(manifest_path))
    expected_manifest_sha256 = _sha(expected_manifest_sha256)
    manifest_info = _inspect(manifest_path)
    if manifest_info.st_size > MAX_MANIFEST_BYTES:
        raise Refusal("Copy manifest is too large")
    data = _read_verified(manifest_path, manifest_info, expected_manifest_sha256, True)
    manifest = json.loads(data, object_pairs_hook=_unique_object,
                          parse_constant=_invalid_constant)
    if (not isinstance(manifest, dict) or not isinstance(manifest.get("schema"), str)
            or manifest["schema"] not in SCHEMAS):
        raise Refusal("Unknown copy manifest schema")
    if (manifest.get("copy_kind", "regular_file") != "regular_file" or
            any(key in manifest for key in ("custom_copy", "copy_target", "copy_semantics"))):
        raise Refusal("Unknown/custom copy operation")
    rows = manifest.get("rows")
    if not isinstance(rows, list) or not rows or len(rows) > MAX_ROWS:
        raise Refusal("Expected a bounded nonempty copy list")
    if "candidate_files" in manifest and _integer(manifest["candidate_files"], "candidate_files") != len(rows):
        raise Refusal("Manifest row count mismatch")

    groups = {}
    seen_outputs = set()
    origins = []
    parents = {}
    output_observations = []
    counts = dict(missing=0, stale=0, identical=0, existing_hardlink=0)

    def filesystem(parent):
        parent_info = _inspect(parent, directory=True)
        parents.setdefault(parent, (parent_info.st_dev, parent_info.st_ino))
        values = os.statvfs(parent)
        unit = _integer(values.f_frsize or values.f_bsize, "allocation unit", 1)
        available = _integer(values.f_bavail, "available blocks") * unit
        device = parent_info.st_dev
        group = groups.setdefault(device, {
            "device": device, "observed_at_path": str(parent),
            "allocation_unit_bytes": unit, "available_bytes": available,
            "known_copy_allocation_bytes": 0,
            "possible_hardlink_savings_bytes_not_credited": 0})
        if group["allocation_unit_bytes"] != unit:
            raise Refusal("Conflicting allocation units for one device")
        group["available_bytes"] = min(group["available_bytes"], available)
        return group

    for row in rows:
        if not isinstance(row, dict) or row.get("copy_kind", "regular_file") != "regular_file":
            raise Refusal("Unknown/custom copy operation")
        if any(key in row for key in ("custom_copy", "copy_target", "copy_semantics")):
            raise Refusal("Unknown/custom copy operation")
        hardlinks = row.get("hardlinks_if_possible", False)
        if type(hardlinks) is not bool:
            raise Refusal("Hardlink eligibility must be boolean")
        candidate = row.get("candidate")
        origin = row.get("package_origin")
        output_path = _record(candidate)
        origin_path = _record(origin, True)
        if output_path == origin_path or output_path in seen_outputs:
            raise Refusal("Same/duplicate output pathname")
        seen_outputs.add(output_path)
        if (candidate["bytes"], candidate["sha256"]) != (origin["bytes"], origin["sha256"]):
            raise Refusal("Output and pinned origin contents disagree")

        original_info = _inspect(origin_path)
        if _identity(original_info) != {key: origin[key] for key in IDENTITY_FIELDS}:
            raise Refusal(f"Stale pinned origin identity: {origin_path}")
        _read_verified(origin_path, original_info, origin["sha256"])
        origins.append((origin_path, original_info))
        filesystem(origin_path.parent)  # Preserve the floor on input mounts too.

        output_info = _inspect(output_path, True)
        group = filesystem(output_path.parent)
        if output_info is not None and output_info.st_dev != group["device"]:
            raise Refusal("Output file is on an unexpected mount")
        output_observations.append((output_path, output_info))
        equal = False
        if output_info is not None and output_info.st_size == candidate["bytes"]:
            actual_hash = _read_verified(output_path, output_info, None)
            equal = (actual_hash == candidate["sha256"] and
                     stat.S_IMODE(output_info.st_mode) == candidate.get("mode", origin["mode"]))
        if equal:
            counts["identical"] += 1
            if (output_info.st_dev, output_info.st_ino) == (original_info.st_dev, original_info.st_ino):
                counts["existing_hardlink"] += 1
            continue

        counts["missing" if output_info is None else "stale"] += 1
        unit = group["allocation_unit_bytes"]
        allocation = ((candidate["bytes"] + unit - 1) // unit) * unit
        group["known_copy_allocation_bytes"] += allocation
        if hardlinks and original_info.st_dev == group["device"]:
            # Eligibility does not guarantee link success. The real Copy may fall
            # back, so keep the complete physical copy in the admission budget.
            group["possible_hardlink_savings_bytes_not_credited"] += allocation

    if seen_outputs.intersection(path for path, _ in origins):
        raise Refusal("Output pathname is also a pinned input")
    # A changed destination/origin invalidates the observation, rather than being
    # treated as free space or a silently reusable cache item.
    for path, info in origins + output_observations:
        current = _inspect(path, info is None)
        if (current is None) != (info is None) or (info is not None and _identity(current) != _identity(info)):
            raise Refusal(f"File changed during assessment: {path}")
    _read_verified(manifest_path, manifest_info, expected_manifest_sha256)
    for parent, identity in parents.items():
        info = _inspect(parent, directory=True)
        if (info.st_dev, info.st_ino) != identity:
            raise Refusal(f"Filesystem parent changed: {parent}")
    for group in groups.values():
        # Reobserve available space after hashing all inputs.
        values = os.statvfs(group["observed_at_path"])
        if (values.f_frsize or values.f_bsize) != group["allocation_unit_bytes"]:
            raise Refusal("Filesystem allocation unit changed")
        available = _integer(values.f_bavail, "available blocks") * group["allocation_unit_bytes"]
        group["available_bytes"] = min(group["available_bytes"], available)
        group["reserve_bytes"] = RESERVE_BYTES
        group["required_free_bytes"] = RESERVE_BYTES + group["known_copy_allocation_bytes"]
        group["shortfall_bytes"] = max(0, group["required_free_bytes"] - group["available_bytes"])
    fits = all(group["shortfall_bytes"] == 0 for group in groups.values())
    return {
        "schema": "9to1.copy-budget-result/v1",
        "status": "KNOWN_COPY_BUDGET_FITS_NOT_LAUNCH_ADMISSION" if fits else "KNOWN_COPY_BUDGET_DENIED",
        "manifest_path": str(manifest_path), "manifest_sha256": expected_manifest_sha256,
        "known_copy_budget_fits": fits, "sdk_launch_admitted": False,
        "reserve_bytes": RESERVE_BYTES, "copy_rows": len(rows), "counts": counts,
        "known_copy_allocation_bytes": sum(g["known_copy_allocation_bytes"] for g in groups.values()),
        "filesystems": sorted(groups.values(), key=lambda group: group["device"]),
        "scope": "Conservative budget for listed complete assets only; lower-bound scope, not full copy/scratch/native closure.",
        "unproven": ["unlisted/conditional/custom target work", "remaining build scratch and peak usage",
                     "actual native load/platform closure", "owning guarded SDK admission"]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest")
    parser.add_argument("--sha256", required=True)
    args = parser.parse_args(argv)
    try:
        result = assess(args.manifest, args.sha256)
    except (OSError, ValueError) as error:
        print(json.dumps({"status": "COPY_BUDGET_INPUT_REFUSED", "reason": str(error),
                          "sdk_launch_admitted": False}))
        return 2
    print(json.dumps(result, indent=2))
    return 0 if result["known_copy_budget_fits"] else 2


if __name__ == "__main__":
    sys.exit(main())
