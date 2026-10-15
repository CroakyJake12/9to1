"""Architecture metadata for managed native Wine and Hangover/FEX runtimes."""
from __future__ import annotations

import json
import platform
import struct
from pathlib import Path


class RuntimeError(ValueError):
    pass


def host_arch(machine: str | None = None) -> str:
    value = (machine or platform.machine()).lower()
    return {"aarch64": "arm64", "arm64": "arm64", "amd64": "x86_64", "x86_64": "x86_64"}.get(value, value)


def pe_arch(path: Path) -> str:
    """Read a PE header without loading or executing the selected program."""
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise RuntimeError("Select a Windows PE executable (.exe)")
        offset = struct.unpack_from("<I", header, 60)[0]
        if offset < 64 or offset > path.stat().st_size - 24:
            raise RuntimeError("Invalid Windows executable header")
        stream.seek(offset)
        pe = stream.read(24)
    if pe[:4] != b"PE\0\0":
        raise RuntimeError("DOS-only executables are not supported")
    machine = struct.unpack_from("<H", pe, 4)[0]
    architectures = {0x14C: "x86", 0x8664: "x86_64", 0xAA64: "arm64", 0xA641: "arm64ec"}
    if machine not in architectures:
        raise RuntimeError(f"Unsupported Windows machine type: 0x{machine:04x}")
    if struct.unpack_from("<H", pe, 22)[0] & 0x2000:
        raise RuntimeError("Select an application, not a DLL")
    return architectures[machine]


def runtime_metadata(root: Path, machine: str | None = None) -> dict:
    architecture = host_arch(machine)
    metadata = root / "runtime.json"
    if not metadata.exists():
        if architecture != "x86_64":
            raise RuntimeError("ARM64 needs a managed Hangover/FEX runtime with runtime.json")
        return {"hostArchitecture": "x86_64", "windowsArchitectures": ["x86", "x86_64"], "translation": "native"}
    try:
        value = json.loads(metadata.read_text())
    except (OSError, ValueError) as exc:
        raise RuntimeError("Cannot read managed Wine runtime.json") from exc
    if not isinstance(value, dict) or value.get("schemaVersion") != 1:
        raise RuntimeError("Unsupported Wine runtime metadata")
    if value.get("hostArchitecture") != architecture:
        raise RuntimeError("Wine runtime architecture does not match this Linux host")
    supported = value.get("windowsArchitectures")
    if not isinstance(supported, list) or not supported or any(
        item not in {"x86", "x86_64", "arm64", "arm64ec"} for item in supported
    ):
        raise RuntimeError("Invalid Windows architectures in runtime.json")
    translation = value.get("translation")
    if translation not in {"native", "hangover-fex"}:
        raise RuntimeError("Unsupported Wine translation backend")
    binary = root / "bin" / "wine"
    with binary.open("rb") as stream:
        elf = stream.read(20)
    expected = {"arm64": 183, "x86_64": 62}.get(architecture)
    if (len(elf) != 20 or elf[:4] != b"\x7fELF" or elf[4:6] != b"\x02\x01"
            or struct.unpack_from("<H", elf, 18)[0] != expected):
        raise RuntimeError("Managed Wine loader must be a native 64-bit Linux ELF binary")
    if architecture == "arm64" and set(supported) & {"x86", "x86_64", "arm64ec"}:
        if translation != "hangover-fex":
            raise RuntimeError("x86/x64 Windows applications on ARM64 require Hangover/FEX")
        dlls = root / "lib" / "wine" / "aarch64-windows"
        for dll in ("libarm64ecfex.dll", "libwow64fex.dll"):
            target = dlls / dll
            if not target.is_file() or not target.resolve().is_relative_to(root.resolve()):
                raise RuntimeError(f"Managed Hangover runtime is missing {dll}")
    return value


def runtime_environment(metadata: dict) -> dict[str, str]:
    if metadata.get("translation") == "hangover-fex":
        return {"HODLL": "libwow64fex.dll", "HODLL64": "libarm64ecfex.dll"}
    return {}
