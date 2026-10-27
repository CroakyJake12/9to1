"""Apply exact reviewed Rnote derivative patches to Cargo's locked vendor source.

Only source-file operations and the system patch utility; no build/network/native
execution. Patch/hash/checksum receipts distinguish the derivative from upstream.
"""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import tempfile
import tomllib

REVISION = "8cac558fa73ee63d958cec05124ea2fdbd7d608d"
CRATE = Path("9to1 Workspace/Canvas/rnote-poc")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def identity(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), "Patch input must be regular: " + str(path))
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"bytes": path.stat().st_size, "sha256": digest}


def relative(name):
    path = PurePosixPath(name)
    require(isinstance(name, str) and name and not path.is_absolute() and ".." not in path.parts
            and str(path) == name, "Unsafe patch target")
    return path


def apply_vendor_patches(root, vendor):
    root, vendor = Path(root), Path(vendor)
    require(vendor.is_dir() and not vendor.is_symlink(), "Expected real Cargo vendor directory")
    lock = tomllib.loads((root / CRATE / "Cargo.lock").read_text())
    expected_source = "git+https://github.com/flxzt/rnote?rev=" + REVISION + "#" + REVISION
    for name in ("rnote-engine", "rnote-compose"):
        rows = [row for row in lock["package"] if row["name"] == name]
        require(len(rows) == 1 and rows[0].get("source") == expected_source, "Wrong locked Rnote source: " + name)
    folder = root / CRATE / "patches"
    manifest = json.loads((folder / "source-patches.json").read_text())
    require(manifest.get("schema") == "9to1.rnote-reviewed-source-patches/v1" and
            manifest.get("upstream_revision") == REVISION and manifest.get("package") == "rnote-engine" and
            manifest.get("package_version") == "0.14.2", "Wrong derivative source manifest")
    candidates = []
    for item in vendor.iterdir():
        if item.is_dir() and not item.is_symlink() and (item / "Cargo.toml").is_file():
            package = tomllib.loads((item / "Cargo.toml").read_text()).get("package", {})
            if package.get("name") == "rnote-engine" and package.get("version") == "0.14.2":
                candidates.append(item)
    require(len(candidates) == 1, "Expected exactly one locked rnote-engine vendor package")
    engine = candidates[0]
    checksum_path = engine / ".cargo-checksum.json"
    original_checksum = identity(checksum_path)
    checksum = json.loads(checksum_path.read_text())
    require(checksum.get("package") is None and isinstance(checksum.get("files"), dict), "Expected a git-source Cargo checksum manifest")
    steps = manifest.get("patches")
    require(isinstance(steps, list) and steps, "No reviewed derivative patches")
    targets = set()
    for step in steps:
        name = str(relative(step["file"]))
        require("/" not in name and identity(folder / name) == {k: step[k] for k in ("bytes", "sha256")}, "Patch bytes differ from manifest")
        names = [str(relative(row["path"])) for row in step["files"]]
        require(names and len(names) == len(set(names)), "Duplicate or empty patch file set")
        text = (folder / name).read_text()
        before = re.findall(r'^--- a/crates/rnote-engine/([^\t\n ]+)$', text, re.M)
        after = re.findall(r'^\+\+\+ b/crates/rnote-engine/([^\t\n ]+)$', text, re.M)
        # Reject unrecognised extra file headers, creations/deletions and path tricks.
        require(before == after == names and len(re.findall(r'^--- ', text, re.M)) == len(names)
                and len(re.findall(r'^\+\+\+ ', text, re.M)) == len(names), "Patch target set differs from reviewed manifest")
        targets.update(names)
    for name in targets:
        path = engine / name
        require(not any((engine.joinpath(*Path(name).parts[:i])).is_symlink() for i in range(1, len(Path(name).parts) + 1)), "Vendor patch path contains a symlink")
        actual = identity(path)
        require(checksum["files"].get(name) == actual["sha256"], "Original Cargo vendor checksum mismatch")
    receipts = []
    with tempfile.TemporaryDirectory(prefix="rnote-patch-", dir=vendor) as temporary:
        scratch = Path(temporary)
        for name in targets:
            path = scratch / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((engine / name).read_bytes())
        for step in steps:
            for row in step["files"]:
                require(identity(scratch / row["path"]) == row["before"], "Derivative patch preimage mismatch: " + row["path"])
            result = subprocess.run(["patch", "--batch", "--forward", "--fuzz=0", "--no-backup-if-mismatch", "-p3", "-i", str((folder / step["file"]).resolve())],
                                    cwd=scratch, capture_output=True, text=True, timeout=30)
            require(result.returncode == 0, "Exact native source patch refused: " + result.stdout[-2000:] + result.stderr[-2000:])
            require({p.relative_to(scratch).as_posix() for p in scratch.rglob("*") if p.is_file()} == targets, "Patch created undeclared files")
            for row in step["files"]:
                require(identity(scratch / row["path"]) == row["after"], "Derivative patch postimage mismatch: " + row["path"])
            receipts.append({"file": step["file"], "patch": identity(folder / step["file"]), "files": step["files"]})
        # Commit only after ALL patch steps/preimages/postimages passed in isolation.
        # A crash during publication leaves invalid vendor checksums; Cargo refuses.
        for name in sorted(targets):
            path = engine / name
            temp = path.with_name(path.name + ".9to1-patched")
            with temp.open("xb") as stream:
                stream.write((scratch / name).read_bytes())
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temp, path)
            checksum["files"][name] = identity(path)["sha256"]
    checksum_path.write_text(json.dumps(checksum, sort_keys=True) + "\n")
    return {"schema": "9to1.rnote-vendored-derivative/v1", "status": "PATCHED_LOCKED_DONOR_SOURCE_NOT_RUNTIME_ACCEPTED",
            "upstream_revision": REVISION, "vendor_package": engine.name, "patch_manifest": identity(folder / "source-patches.json"),
            "patches": receipts, "cargo_checksum_before": original_checksum, "cargo_checksum_after": identity(checksum_path),
            "native_build_and_execution": "NOT RUN by patch application"}
