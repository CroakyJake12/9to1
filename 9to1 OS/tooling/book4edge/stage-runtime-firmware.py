#!/usr/bin/env python3
"""Stage early Qualcomm runtime files from an authenticated Ubuntu live tree.

Requires PySquashfsImage 0.9.0 and zstandard. This reads files and creates a new
output directory; it never installs drivers, flashes devices or writes disks.
Supply a sparse checkout of official linux-firmware release 20251111 for the
Book4-tested c5 Wi-Fi fallback. The two upstream blob identities are pinned.
"""
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

UPSTREAM = "https://gitlab.com/kernel-firmware/linux-firmware"
COMMIT = "6fc940781a013ad837ed8fea326d2b897467bbc3"
BLOBS = {"amss.bin": "1dcfedb6218353de6ba02e0fb9da5bd9e787d125",
         "m3.bin": "55e5c18f812eaa87bc8fe5c75f083a86f2ab86d0"}


def stage(tree, firmware_source, output):
    from PySquashfsImage import SquashFsImage
    import zstandard
    if output.exists():
        raise ValueError("Use a new output directory")
    image_path = tree / "casper/minimal.squashfs"
    if not image_path.is_file():
        raise ValueError("Expected the authenticated Ubuntu live tree")
    fallback = {}
    for name, expected in BLOBS.items():
        data = (firmware_source / "ath12k/WCN7850/hw2.0" / name).read_bytes()
        actual = hashlib.sha1(f"blob {len(data)}\0".encode() + data).hexdigest()
        if actual != expected:
            raise ValueError(f"Upstream firmware blob mismatch: {name}")
        fallback[name] = data
    version = re.search(rb"QC_IMAGE_VERSION_STRING=([ -~]+)", fallback["amss.bin"])
    if not version or b"c5-00302" not in version[1]:
        raise ValueError("Expected c5-00302 Wi-Fi runtime")
    for filename in ("LICENSE.QualcommAtheros_ath10k", "WHENCE"):
        if not (firmware_source / filename).is_file():
            raise ValueError(f"Missing licence information: {filename}")
    output.mkdir(parents=True)
    rows = {}

    def save(name, data, source, blob=None):
        # All paths are selected below; nevertheless reject archive traversal.
        if Path(name).is_absolute() or ".." in Path(name).parts or ":" in name or "\\" in name:
            raise ValueError(f"Unsafe firmware path: {name}")
        target = output / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        rows[name] = {"file": name, "bytes": len(data),
                      "sha256": hashlib.sha256(data).hexdigest(), "source": source}
        if blob:
            rows[name]["gitBlob"] = blob

    with SquashFsImage.from_file(str(image_path)) as image:
        for item in image:
            if item.is_dir or item.is_symlink:
                continue
            p = item.path
            if not (p.startswith("/usr/lib/firmware/qca/") or
                    p.startswith("/usr/lib/firmware/ath12k/WCN7850/hw2.0/") or
                    p == "/usr/lib/firmware/qcom/gen70500_sqe.fw.zst" or
                    p == "/usr/lib/firmware/qcom/gen70500_sqe.fw"):
                continue
            data = item.read_bytes()
            name = p.removeprefix("/usr/lib/firmware/")
            if name.endswith(".zst"):
                data = zstandard.ZstdDecompressor().decompress(data)
                name = name[:-4]
            save(name, data, "Authenticated Ubuntu live image: minimal.squashfs" + p)
    for name, data in fallback.items():
        relative = "ath12k/WCN7850/hw2.0/" + name
        save(relative, data, f"{UPSTREAM}/-/blob/{COMMIT}/{relative}", BLOBS[name])
    for required in ("ath12k/WCN7850/hw2.0/board-2.bin", "qca/hmtbtfw20.tlv", "qca/hmtnv20.bin"):
        if required not in rows:
            raise ValueError(f"Ubuntu image lacks required runtime firmware: {required}")
    for filename in ("LICENSE.QualcommAtheros_ath10k", "WHENCE"):
        shutil.copy2(firmware_source / filename, output / filename)
    manifest = {"purpose": "Runtime files for private live hardware test; not firmware flashing",
                "wifiVersion": version[1].decode(), "wifiTestedOnThisLaptop": False,
                "boardData": "Original Ubuntu board-2.bin retained; no guessed fallback board.bin or board-ID remapping",
                "firmware": list(rows.values())}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    return {"files": len(rows), "bytes": sum(row["bytes"] for row in rows.values()),
            "wifiVersion": manifest["wifiVersion"]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tree", required=True, type=Path)
    parser.add_argument("--firmware-source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    print(json.dumps(stage(args.tree, args.firmware_source, args.output), indent=2))
