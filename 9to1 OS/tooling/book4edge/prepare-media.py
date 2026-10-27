#!/usr/bin/env python3
"""Download an authenticated Ubuntu ARM64 desktop ISO. Never writes disks."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

RELEASES = "https://cdimage.ubuntu.com/ubuntu/releases/"
SIGNER = "843938DF228D22F7B3742BC0D94AA3F0EFE21092"
ISO_PATTERN = re.compile(r"ubuntu-(\d{2}\.\d{2}(?:\.\d+)?)-desktop-arm64\.iso")


def fetch(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def stable_images(checksums: str) -> dict[str, str]:
    images = {}
    for line in checksums.splitlines():
        match = re.fullmatch(r"([a-f0-9]{64}) [ *](.+)", line)
        if match and ISO_PATTERN.fullmatch(match[2]):
            images[match[2]] = match[1]
    return images


def version(name: str) -> tuple[int, ...]:
    return tuple(map(int, ISO_PATTERN.fullmatch(name)[1].split(".")))


def resolve(release: str | None = None) -> tuple[str, str, str, bytes, bytes]:
    if release:
        if not re.fullmatch(r"\d{2}\.\d{2}(?:\.\d+)?", release):
            raise ValueError("release must be a stable numeric version, e.g. 26.04.1")
        candidates = [release]
    else:
        index = fetch(RELEASES).decode("utf-8")
        candidates = sorted(set(re.findall(r'href="(\d{2}\.\d{2}(?:\.\d+)?)/"', index)),
                            key=lambda s: tuple(map(int, s.split("."))), reverse=True)
    for candidate in candidates:
        base = RELEASES + candidate + "/release/"
        try:
            sums = fetch(base + "SHA256SUMS")
        except urllib.error.HTTPError as exc:
            if exc.code == 404:
                continue  # Scheduled releases and prereleases are not stable images.
            raise
        images = stable_images(sums.decode("utf-8"))
        if images:
            name = max(images, key=version)
            return base, name, images[name], sums, fetch(base + "SHA256SUMS.gpg")
    raise RuntimeError("No published stable ARM64 desktop image found")


def authenticate(directory: Path) -> None:
    if not shutil.which("gpg"):
        raise RuntimeError("Install gnupg first; checksum signature verification is required")
    with tempfile.TemporaryDirectory(prefix="ubuntu-signing-") as temporary:
        key = Path(temporary) / "ubuntu.asc"
        key.write_bytes(fetch("https://keyserver.ubuntu.com/pks/lookup?op=get&search=0x" + SIGNER))
        command = ["gpg", "--batch", "--homedir", temporary]
        subprocess.run(command + ["--import", str(key)], check=True, capture_output=True)
        result = subprocess.run(command + ["--status-fd", "1", "--verify",
                                str(directory / "SHA256SUMS.gpg"), str(directory / "SHA256SUMS")],
                                check=True, capture_output=True, text=True)
        valid = [line.split() for line in result.stdout.splitlines()
                 if line.startswith("[GNUPG:] VALIDSIG ")]
        if not any(fields[2] == SIGNER or fields[-1] == SIGNER for fields in valid):
            raise RuntimeError("Checksum signature is not from the pinned Ubuntu image signing key")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--release", help="Default: newest published stable ARM64 desktop release")
    parser.add_argument("--resolve-only", action="store_true", help="Verify metadata without downloading the ISO")
    args = parser.parse_args()
    base, name, digest, sums, signature = resolve(args.release)
    directory = args.output.resolve() / name.removesuffix(".iso")
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "SHA256SUMS").write_bytes(sums)
    (directory / "SHA256SUMS.gpg").write_bytes(signature)
    authenticate(directory)
    provenance = {"checkedAt": datetime.now(timezone.utc).isoformat(), "url": base + name,
                  "sha256": digest, "signer": SIGNER, "signatureVerified": True,
                  "isoVerified": False, "hardwareTested": False,
                  "target": "Samsung Galaxy Book4 Edge 16-inch Snapdragon X Elite"}
    print(json.dumps(provenance, indent=2), flush=True)
    if not args.resolve_only:
        target = directory / name
        if not target.exists() or sha256(target) != digest:
            partial = target.with_suffix(".iso.partial")
            # curl preserves proxy/CA configuration, resumes partial transfers, and never invokes a shell.
            subprocess.run(["curl", "--fail", "--location", "--retry", "3", "--connect-timeout", "30",
                            "--continue-at", "-", "--output", str(partial), base + name], check=True)
            if sha256(partial) != digest:
                partial.unlink()
                raise RuntimeError("Downloaded ISO checksum mismatch; partial file removed")
            partial.replace(target)
        provenance["isoVerified"] = True
        provenance["localPath"] = str(target)
    (directory / "provenance.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(f"Prepared: {directory}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
