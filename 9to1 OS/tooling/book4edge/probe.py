#!/usr/bin/env python3
"""Read-only Book4 Edge boot, hardware and compatibility inventory."""
import json
import os
import platform
import shutil
import subprocess
from pathlib import Path


def text(path):
    try:
        return Path(path).read_text().replace("\0", "\n").strip()
    except (OSError, UnicodeError):
        return None


def command(argv):
    if not shutil.which(argv[0]):
        return {"available": False}
    try:
        result = subprocess.run(argv, capture_output=True, text=True, timeout=15, check=False)
        return {"exitCode": result.returncode, "stdout": result.stdout.strip(), "stderr": result.stderr.strip()}
    except (OSError, subprocess.TimeoutExpired) as exc:
        return {"error": str(exc)}


if __name__ == "__main__":
    print(json.dumps({
        "architecture": platform.machine(), "kernel": platform.release(),
        "model": text("/sys/firmware/devicetree/base/model"),
        "compatible": text("/sys/firmware/devicetree/base/compatible"),
        "dmiProduct": text("/sys/class/dmi/id/product_name"),
        "uefi": Path("/sys/firmware/efi").is_dir(),
        "secureBoot": command(["mokutil", "--sb-state"]),
        "disks": command(["lsblk", "--json", "-o", "NAME,TYPE,SIZE,FSTYPE,LABEL,MOUNTPOINTS,PARTTYPE"]),
        "bootEntries": command(["efibootmgr"]),
        "graphics": command(["vulkaninfo", "--summary"]),
        "wifi": command(["nmcli", "-t", "-f", "DEVICE,TYPE,STATE", "device"]),
        "kvm": {"exists": Path("/dev/kvm").exists(), "readWrite": os.access("/dev/kvm", os.R_OK | os.W_OK)},
        "warning": "Inventory only. A generic ARM64 VM test does not validate Book4 Edge hardware."
    }, indent=2))
