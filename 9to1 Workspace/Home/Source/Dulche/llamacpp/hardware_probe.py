#!/usr/bin/env python3
"""Read-only Linux hardware probe for Haven local inference planning.

The probe reads procfs/sysfs/device-node metadata and checks executable presence.
It does not install packages, load drivers, run vendor utilities, or enable a GPU.
"""
from __future__ import annotations

import argparse
import glob
import json
import os
import pathlib
import platform
import shutil
from typing import Any

GPU_VENDORS = {"0x10de": "nvidia", "0x1002": "amd", "0x8086": "intel"}
RELEVANT_CPU_FLAGS = {
    "sse4_2", "avx", "avx2", "avx_vnni", "avx512f", "avx512_vnni", "fma", "f16c",
    "asimd", "neon", "dotprod", "i8mm", "sve", "sve2",
}
TOOLS = (
    "nvidia-smi", "nvcc", "rocminfo", "hipcc", "vulkaninfo", "glslc",
    "sycl-ls", "icpx", "dpcpp", "clinfo",
)


def read_text(path: pathlib.Path) -> str | None:
    try:
        return path.read_text(encoding="utf-8", errors="replace").strip()
    except (OSError, UnicodeError):
        return None


def read_int(path: pathlib.Path) -> int | None:
    value = read_text(path)
    if value is None:
        return None
    try:
        return int(value, 0)
    except ValueError:
        return None


def parse_meminfo(text: str) -> dict[str, int]:
    result: dict[str, int] = {}
    for line in text.splitlines():
        if ":" not in line:
            continue
        key, raw = line.split(":", 1)
        parts = raw.strip().split()
        if not parts:
            continue
        try:
            value = int(parts[0])
        except ValueError:
            continue
        if len(parts) > 1 and parts[1].lower() == "kb":
            value *= 1024
        result[key] = value
    return result


def parse_cgroup_limit(value: str | None) -> int | None:
    if value is None or value == "max":
        return None
    try:
        number = int(value)
    except ValueError:
        return None
    return number if number >= 0 else None


def cpu_flags(cpuinfo: str) -> list[str]:
    flags: set[str] = set()
    for line in cpuinfo.splitlines():
        lower = line.lower()
        if lower.startswith("flags") or lower.startswith("features"):
            _, _, raw = line.partition(":")
            flags.update(raw.strip().lower().split())
    return sorted(flags & RELEVANT_CPU_FLAGS)


def _driver_name(device: pathlib.Path) -> str | None:
    driver = device / "driver"
    try:
        return driver.resolve(strict=True).name
    except OSError:
        return None


def probe_gpus(drm_root: pathlib.Path = pathlib.Path("/sys/class/drm")) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    if not drm_root.exists():
        return result
    for card in sorted(drm_root.glob("card*")):
        suffix = card.name.removeprefix("card")
        if not suffix.isdigit():
            continue
        device = card / "device"
        vendor_raw = read_text(device / "vendor")
        device_raw = read_text(device / "device")
        if vendor_raw is None:
            continue
        vendor_key = vendor_raw.lower()
        result.append({
            "drmCard": card.name,
            "vendorId": vendor_key,
            "vendorName": GPU_VENDORS.get(vendor_key, "unknown"),
            "deviceId": device_raw.lower() if device_raw else None,
            "driver": _driver_name(device),
            "vramBytes": read_int(device / "mem_info_vram_total"),
        })
    return result


def probe_device_nodes() -> list[dict[str, Any]]:
    paths = sorted(set(glob.glob("/dev/dri/renderD*") + glob.glob("/dev/nvidia[0-9]*")))
    return [{"path": path, "readable": os.access(path, os.R_OK), "writable": os.access(path, os.W_OK)} for path in paths]


def probe() -> dict[str, Any]:
    meminfo = parse_meminfo(read_text(pathlib.Path("/proc/meminfo")) or "")
    cpuinfo = read_text(pathlib.Path("/proc/cpuinfo")) or ""
    cgroup_max = parse_cgroup_limit(read_text(pathlib.Path("/sys/fs/cgroup/memory.max")))
    cgroup_current = read_int(pathlib.Path("/sys/fs/cgroup/memory.current"))
    product = read_text(pathlib.Path("/sys/class/dmi/id/product_name"))
    vendor = read_text(pathlib.Path("/sys/class/dmi/id/sys_vendor"))
    return {
        "schemaVersion": 1,
        "probeMode": "read-only-no-vendor-tools-executed",
        "platform": {
            "system": platform.system(),
            "machine": platform.machine(),
            "kernel": platform.release(),
        },
        "cpu": {
            "logicalCpus": os.cpu_count(),
            "relevantFlags": cpu_flags(cpuinfo),
            "hypervisorFlagObserved": " hypervisor " in f" {cpuinfo.lower()} ",
        },
        "memory": {
            "totalBytes": meminfo.get("MemTotal"),
            "availableBytes": meminfo.get("MemAvailable"),
            "cgroupMaxBytes": cgroup_max,
            "cgroupCurrentBytes": cgroup_current,
        },
        "virtualization": {"systemVendor": vendor, "productName": product},
        "gpus": probe_gpus(),
        "deviceNodes": probe_device_nodes(),
        "tooling": {tool: shutil.which(tool) for tool in TOOLS},
        "claims": {
            "hardwareObservedOnly": True,
            "backendSupportInferred": False,
            "runtimeProven": False,
        },
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Read-only local inference hardware probe")
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args(argv)
    value = probe()
    text = json.dumps(value, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    else:
        print(text, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
