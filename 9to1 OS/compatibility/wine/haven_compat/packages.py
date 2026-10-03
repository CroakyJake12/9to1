"""Read-only foreign package inspection. Extensions never establish format or trust."""
from __future__ import annotations

import hashlib
import os
import re
import stat
import struct
import uuid
import zipfile
from dataclasses import asdict, dataclass
from pathlib import Path
from xml.etree import ElementTree


class PackageError(ValueError):
    pass


@dataclass(frozen=True)
class CompatibilityPackage:
    identity: str
    identity_source: str
    format: str
    architectures: tuple[str, ...]
    path: str
    size_bytes: int
    sha256: str
    trust: str = "unverified"

    def as_dict(self) -> dict:
        return asdict(self)


def inspect_package(path: Path) -> CompatibilityPackage:
    try:
        resolved = path.resolve(strict=True)
        fd = os.open(resolved, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
        with os.fdopen(fd, "rb") as handle:
            before = handle.fileno()
            info = os.fstat(before)
            if not stat.S_ISREG(info.st_mode):
                raise PackageError("foreign package must be a regular file")
            signature = handle.read(512)
            handle.seek(0)
            digest = hashlib.file_digest(handle, "sha256").hexdigest()
            handle.seek(0)
            if signature.startswith(b"MZ"):
                kind, architectures, identity = _pe(handle, signature, info.st_size)
            elif signature.startswith(bytes.fromhex("d0cf11e0a1b11ae1")):
                kind, architectures, identity = _msi(handle, signature, info.st_size)
            elif signature.startswith(b"PK\x03\x04"):
                kind, architectures, identity = _apk(handle)
            else:
                raise PackageError("unrecognised package signature; no backend will be selected")
            after = os.fstat(before)
            if (info.st_size, info.st_mtime_ns, info.st_ctime_ns) != (after.st_size, after.st_mtime_ns, after.st_ctime_ns):
                raise PackageError("package changed during inspection; inspect it again")
    except PackageError:
        raise
    except (OSError, ValueError, struct.error, zipfile.BadZipFile, ElementTree.ParseError) as exc:
        raise PackageError("package cannot be inspected safely") from exc
    # A digest is a package identity, not a claim of publisher/application identity.
    return CompatibilityPackage(identity or f"windows-package:sha256:{digest}",
        "android-manifest" if identity else "package-content-sha256", kind,
        architectures, str(resolved), info.st_size, digest)


def _pe(handle, signature: bytes, size: int):
    if len(signature) < 64:
        raise PackageError("truncated DOS executable header")
    offset = struct.unpack_from("<I", signature, 60)[0]
    if offset < 64 or offset + 24 > size:
        raise PackageError("invalid Windows executable header offset")
    handle.seek(offset)
    header = handle.read(24)
    if header[:4] != b"PE\x00\x00":
        raise PackageError("DOS executable is not a supported Windows PE package")
    machine, sections, _, _, _, optional_size, flags = struct.unpack_from("<HHIIIHH", header, 4)
    if not 1 <= sections <= 96 or optional_size < 2 or offset + 24 + optional_size + sections * 40 > size:
        raise PackageError("invalid or truncated Windows PE structure")
    if not flags & 0x0002 or flags & 0x2000:
        raise PackageError("PE image is not an executable application (DLLs are not installers)")
    architecture = {0x014c: "x86", 0x8664: "x86_64", 0xaa64: "arm64"}.get(machine)
    if architecture is None:
        raise PackageError("unsupported Windows executable architecture")
    magic = struct.unpack("<H", handle.read(2))[0]
    if magic != (0x10b if architecture == "x86" else 0x20b):
        raise PackageError("PE architecture and optional header disagree")
    return "windows-exe", (architecture,), None


def _msi(handle, header: bytes, size: int):
    if len(header) != 512 or header[28:30] != b"\xfe\xff":
        raise PackageError("invalid compound package header")
    major, shift = struct.unpack_from("<H", header, 26)[0], struct.unpack_from("<H", header, 30)[0]
    if (major, shift) not in {(3, 9), (4, 12)}:
        raise PackageError("unsupported compound package version")
    sector_size = 1 << shift
    directory = struct.unpack_from("<I", header, 48)[0]
    offset = (directory + 1) * sector_size
    if offset + 128 > size:
        raise PackageError("compound package root directory is outside the file")
    handle.seek(offset)
    entry = handle.read(128)
    # The MSI database root storage CLSID distinguishes it from Office/OLE files.
    if entry[66] != 5 or uuid.UUID(bytes_le=entry[80:96]) != uuid.UUID("000c1084-0000-0000-c000-000000000046"):
        raise PackageError("compound file is not a Windows Installer database")
    # Architecture must be resolved from MSI SummaryInformation before installation.
    return "windows-msi", (), None


def _apk(handle):
    with zipfile.ZipFile(handle) as archive:
        manifests = [entry for entry in archive.infolist() if entry.filename == "AndroidManifest.xml"]
        if len(manifests) != 1:
            raise PackageError("APK requires exactly one Android manifest")
        manifest = manifests[0]
        if manifest.file_size > 2 * 1024 * 1024 or manifest.flag_bits & 1:
            raise PackageError("APK manifest is too large or encrypted")
        raw = archive.read(manifest)
        package = _android_package(raw)
        architectures = sorted({entry.filename.split("/")[1]
            for entry in archive.infolist()
            if entry.filename.startswith("lib/") and len(entry.filename.split("/")) == 3
            and entry.filename.endswith(".so")})
    return "android-apk", tuple(architectures), "android:" + package


def _android_package(raw: bytes) -> str:
    if raw.lstrip().startswith(b"<"):
        # Reject DTDs/entities; no external XML resource is needed for package identity.
        if b"<!DOCTYPE" in raw.upper() or b"<!ENTITY" in raw.upper():
            raise PackageError("APK XML declarations are unsupported")
        root = ElementTree.fromstring(raw)
        if root.tag != "manifest":
            raise PackageError("APK XML root is not manifest")
        package = root.get("package")
    else:
        package = _binary_android_package(raw)
    if not isinstance(package, str) or len(package) > 255 or not re.fullmatch(r"[A-Za-z_][A-Za-z_0-9]*(?:\.[A-Za-z_][A-Za-z_0-9]*)+", package):
        raise PackageError("APK does not declare a valid stable Android package identity")
    return package


def _binary_android_package(raw: bytes) -> str | None:
    if len(raw) < 8 or struct.unpack_from("<HHI", raw) != (3, 8, len(raw)):
        raise PackageError("invalid binary Android XML header")
    strings: list[str] = []
    position = 8
    while position < len(raw):
        if position + 8 > len(raw):
            raise PackageError("truncated binary Android XML chunk")
        kind, header_size, size = struct.unpack_from("<HHI", raw, position)
        if header_size < 8 or size < header_size or position + size > len(raw):
            raise PackageError("invalid binary Android XML chunk bounds")
        chunk = raw[position:position + size]
        if kind == 1:
            if header_size < 28:
                raise PackageError("invalid Android string pool")
            count, _, flags, start, _ = struct.unpack_from("<IIIII", chunk, 8)
            if count > 65536 or header_size + count * 4 > size or start < header_size + count * 4 or start > size:
                raise PackageError("invalid Android string offsets")
            strings = [_pool_string(chunk, start + struct.unpack_from("<I", chunk, header_size + index * 4)[0], bool(flags & 0x100)) for index in range(count)]
        elif kind == 0x0102:
            if header_size != 16 or size < 36:
                raise PackageError("invalid Android start element")
            name, attr_start, attr_size, attr_count = struct.unpack_from("<IHHH", chunk, 20)
            if name >= len(strings):
                raise PackageError("invalid Android element name")
            if strings[name] == "manifest":
                if attr_start < 20 or attr_size < 20 or 16 + attr_start + attr_size * attr_count > size:
                    raise PackageError("invalid Android manifest attributes")
                packages = []
                for index in range(attr_count):
                    offset = 16 + attr_start + index * attr_size
                    namespace, attr_name, value, typed_size, _, value_type, typed_value = struct.unpack_from("<IIIHBBI", chunk, offset)
                    if attr_name >= len(strings) or typed_size != 8:
                        raise PackageError("invalid Android attribute")
                    if namespace == 0xffffffff and strings[attr_name] == "package":
                        string_index = value if value != 0xffffffff else typed_value if value_type == 3 else 0xffffffff
                        if string_index >= len(strings):
                            raise PackageError("invalid Android package string")
                        packages.append(strings[string_index])
                if len(packages) != 1:
                    raise PackageError("APK manifest package identity is absent or ambiguous")
                return packages[0]
            raise PackageError("first Android XML element must be manifest")
        position += size
    return None


def _pool_string(chunk: bytes, offset: int, utf8: bool) -> str:
    def length(position: int):
        if utf8:
            first = chunk[position]
            return (((first & 0x7f) << 8) | chunk[position + 1], position + 2) if first & 0x80 else (first, position + 1)
        first = struct.unpack_from("<H", chunk, position)[0]
        return (((first & 0x7fff) << 16) | struct.unpack_from("<H", chunk, position + 2)[0], position + 4) if first & 0x8000 else (first, position + 2)
    try:
        count, offset = length(offset)
        if utf8:
            count, offset = length(offset)
        byte_count = count if utf8 else count * 2
        terminator_size = 1 if utf8 else 2
        if offset + byte_count + terminator_size > len(chunk) or any(chunk[offset + byte_count:offset + byte_count + terminator_size]):
            raise PackageError("invalid Android string termination")
        return chunk[offset:offset + byte_count].decode("utf-8" if utf8 else "utf-16-le")
    except (IndexError, UnicodeError, struct.error) as exc:
        raise PackageError("invalid Android string pool data") from exc
