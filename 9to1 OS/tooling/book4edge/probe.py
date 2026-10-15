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


def power_supplies():
    fields = ("type", "status", "online", "present", "capacity", "voltage_now",
              "current_now", "power_now", "energy_now", "energy_full", "charge_now",
              "charge_full", "manufacturer", "model_name")
    return {p.name: {name: text(p / name) for name in fields if (p / name).exists()}
            for p in Path("/sys/class/power_supply").glob("*")}


def temperatures():
    return {p.name: {"type": text(p / "type"), "millidegreesC": text(p / "temp")}
            for p in Path("/sys/class/thermal").glob("thermal_zone*")}


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
        "opengl": command(["glxinfo", "-B"]),
        "wifi": command(["nmcli", "-t", "-f", "DEVICE,TYPE,STATE", "device"]),
        "radioBlocks": command(["rfkill", "list"]),
        "pciDrivers": command(["lspci", "-nnk"]),
        "usbDevices": command(["lsusb"]),
        "usbDrivers": command(["lsusb", "-t"]),
        "inputDevices": text("/proc/bus/input/devices"),
        "libinput": command(["libinput", "list-devices"]),
        "bluetooth": command(["bluetoothctl", "show"]),
        "audioDevices": command(["wpctl", "status"]),
        "audioPlaybackHardware": command(["aplay", "-l"]),
        "audioCaptureHardware": command(["arecord", "-l"]),
        "cameraDevices": command(["v4l2-ctl", "--list-devices"]),
        "fingerprintDevices": command(["gdbus", "call", "--system", "--dest", "net.reactivated.Fprint",
                                       "--object-path", "/net/reactivated/Fprint/Manager", "--method",
                                       "net.reactivated.Fprint.Manager.GetDevices"]),
        "powerSupplies": power_supplies(),
        "temperatures": temperatures(),
        "sleepModes": {"state": text("/sys/power/state"), "memory": text("/sys/power/mem_sleep")},
        "displayConnectors": {p.name: text(p / "status") for p in Path("/sys/class/drm").glob("card*-*")
                              if (p / "status").exists()},
        "driverPackages": command(["dpkg-query", "-W", "libfprint-2-2", "fprintd", "libgl1-mesa-dri",
                                    "mesa-vulkan-drivers", "bluez", "alsa-ucm-conf", "libcamera*"]),
        "kernelWarnings": command(["journalctl", "-b", "-k", "--no-pager", "-p", "warning", "-n", "300"]),
        "kvm": {"exists": Path("/dev/kvm").exists(), "readWrite": os.access("/dev/kvm", os.R_OK | os.W_OK)},
        "warning": "Read-only inventory; no audio playback, enrollment, suspend, firmware flashing or boot changes. Device detection is not proof of functional hardware support."
    }, indent=2))
