#!/usr/bin/env python3
"""Stage a pinned Ubuntu 26.04 ARM64 Hangover runtime without installing packages."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import tarfile
import tempfile
from pathlib import Path

NAME = "hangover_11.16_ubuntu2604_resolute_arm64.tar"
URL = "https://github.com/AndreRH/hangover/releases/download/hangover-11.16/" + NAME
SHA256 = "bdf7d91d67367a3dc6e856be3c6ab16e8d3b9049e7cd197844df20a7e4b45c52"
RUNTIME = "hangover-11.16-arm64"
PACKAGES = ("hangover-wine_11.16~resolute_arm64.deb", "hangover-libwow64fex_11.16_arm64.deb",
            "hangover-libarm64ecfex_11.16_arm64.deb", "hangover-wowbox64_11.16_arm64.deb")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--runtime-root", type=Path, default=Path(os.environ.get(
        "XDG_DATA_HOME", Path.home() / ".local/share")) / "haven/compat/wine/runtimes")
    args = parser.parse_args()
    args.cache.mkdir(parents=True, exist_ok=True)
    archive = args.cache / NAME
    if not archive.exists():
        partial = archive.with_suffix(".partial")
        subprocess.run(["curl", "--fail", "--location", "--retry", "3", "--output", str(partial), URL], check=True)
        partial.replace(archive)
    digest = hashlib.sha256()
    with archive.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    if digest.hexdigest() != SHA256:
        raise ValueError("Hangover archive checksum mismatch")
    args.runtime_root.mkdir(parents=True, exist_ok=True)
    destination = args.runtime_root / RUNTIME
    if destination.exists():
        raise ValueError(f"Runtime already exists; refusing to replace it: {destination}")
    with tempfile.TemporaryDirectory(dir=args.runtime_root, prefix=".hangover-") as temporary:
        staging = Path(temporary)
        package_dir = args.cache / "packages"
        package_dir.mkdir(exist_ok=True)
        with tarfile.open(archive) as source:
            for name in PACKAGES:
                member = source.getmember(name)
                if not member.isfile():
                    raise ValueError(f"Unexpected archive member: {name}")
                with source.extractfile(member) as incoming, (package_dir / name).open("wb") as outgoing:
                    shutil.copyfileobj(incoming, outgoing)
                subprocess.run(["dpkg-deb", "--extract", str(package_dir / name), str(staging)], check=True)
        root = staging / "usr"
        metadata = {"schemaVersion": 1, "hostArchitecture": "arm64",
                    "windowsArchitectures": ["x86", "x86_64", "arm64", "arm64ec"],
                    "translation": "hangover-fex", "sourceUrl": URL, "sourceSha256": SHA256,
                    "ubuntuVersion": "26.04", "runtimeTested": False}
        (root / "runtime.json").write_text(json.dumps(metadata, indent=2) + "\n")
        # Import the broker validator without executing ARM64 code on the build PC.
        from haven_compat.runtime import runtime_metadata
        runtime_metadata(root, machine="aarch64")
        root.rename(destination)
    print(f"Staged {destination}. Install the matching package dependencies on Ubuntu ARM64 before launch.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
