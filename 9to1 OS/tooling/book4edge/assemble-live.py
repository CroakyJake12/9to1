#!/usr/bin/env python3
"""Build a private, untested Book4 Edge live-USB candidate on Windows or Linux.

Requires Python 3.12 and zstandard. Never writes disks or changes the source ISO.
The input tree must already be copied from an authenticated Ubuntu ARM64 ISO.
"""
import argparse
import gzip
import hashlib
import io
import json
import shutil
import stat
import tarfile
from pathlib import Path, PurePosixPath

RELEASE = "6.19.0-9to1-book4edge16"
FW_PATH = "usr/lib/firmware/updates/qcom/x1e80100/SAMSUNG/galaxy-book4-edge"
FIRMWARE = ("adsp_dtbs.elf", "adspr.jsn", "adsps.jsn", "adspua.jsn",
            "cdsp_dtbs.elf", "cdspr.jsn", "qcadsp8380.mbn", "qccdsp8380.mbn",
            "qcdxkmsuc8380.mbn")
KEYBOARD_RULE = ('SUBSYSTEM=="input", ATTRS{name}=="hid-over-i2c 0CF2:9050 Keyboard", '
                 'ENV{ID_INPUT}="1", ENV{ID_INPUT_KEYBOARD}="1", ENV{ID_INPUT_KEY}="1", '
                 'ENV{ID_INPUT_TABLET}:="", ENV{ID_INPUT_TABLET_PAD}:=""\n')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def safe_name(name):
    p = PurePosixPath(name)
    if p.is_absolute() or ".." in p.parts or "\\" in name or "\0" in name:
        raise ValueError(f"Unsafe archive path: {name}")
    return str(p)


def read_cpio(data, start=0):
    """Read one newc archive, including its hardlink fields, without extraction."""
    entries, pos = {}, start
    while data[pos:pos + 6] == b"070701":
        fields = [int(data[pos + 6 + i * 8:pos + 14 + i * 8], 16) for i in range(13)]
        size, namesize = fields[6], fields[11]
        if namesize < 1 or pos + 110 + namesize > len(data):
            raise ValueError("Truncated cpio header")
        name = data[pos + 110:pos + 110 + namesize - 1].decode()
        body = (pos + 110 + namesize + 3) & ~3
        if body + size > len(data):
            raise ValueError("Truncated cpio data")
        pos = (body + size + 3) & ~3
        if name == "TRAILER!!!":
            return entries, pos
        entries[safe_name(name)] = (fields[1], data[body:body + size])
    raise ValueError("Expected newc cpio archive")


def cpio(entries):
    result = bytearray()
    records = list(entries.items()) + [("TRAILER!!!", (0, b""))]
    for ino, (name, (mode, data)) in enumerate(records, 1):
        safe_name(name)
        encoded = name.encode() + b"\0"
        fields = (ino, mode, 0, 0, 1, 0, len(data), 0, 0, 0, 0, len(encoded), 0)
        result.extend(b"070701" + b"".join(f"{n:08x}".encode() for n in fields))
        result.extend(encoded)
        result.extend(b"\0" * (-len(result) % 4))
        result.extend(data)
        result.extend(b"\0" * (-len(result) % 4))
    return bytes(result)


def original_initrd_entries(data):
    import zstandard
    entries, pos = read_cpio(data)
    while pos < len(data) and data[pos] == 0:
        pos += 1
    if data[pos:pos + 4] != b"\x28\xb5\x2f\xfd":
        raise ValueError("Expected the authenticated Ubuntu initrd's zstd main archive")
    with zstandard.ZstdDecompressor().stream_reader(io.BytesIO(data[pos:])) as stream:
        main = stream.read(1024 * 1024 * 1024 + 1)
    if len(main) > 1024 * 1024 * 1024:
        raise ValueError("Unexpectedly large main initramfs")
    entries.update(read_cpio(main)[0])
    return entries


def deb_members(data):
    if not data.startswith(b"!<arch>\n"):
        raise ValueError("Not a Debian ar archive")
    pos, found = 8, {}
    while pos < len(data):
        header = data[pos:pos + 60]
        if len(header) != 60 or header[58:] != b"`\n":
            raise ValueError("Invalid ar member")
        name = header[:16].decode().strip().rstrip("/")
        size = int(header[48:58].decode().strip())
        pos += 60
        if pos + size > len(data):
            raise ValueError("Truncated ar member")
        found[name] = data[pos:pos + size]
        pos += size + size % 2
    if found.get("debian-binary") != b"2.0\n":
        raise ValueError("Unexpected Debian archive version")
    return found


def tar_members(name, data):
    if name.endswith(".zst"):
        import zstandard
        with zstandard.ZstdDecompressor().stream_reader(io.BytesIO(data)) as stream:
            data = stream.read()
    return tarfile.open(fileobj=io.BytesIO(data), mode="r:*")


def module_entries(deb):
    members = deb_members(deb)
    data_names = [n for n in members if n.startswith("data.tar")]
    if len(data_names) != 1:
        raise ValueError("Expected one Debian payload")
    name = data_names[0]
    entries = {}
    with tar_members(name, members[name]) as archive:
        for member in archive:
            name = safe_name(member.name)
            if name == "lib/modules" or name.startswith("lib/modules/"):
                name = "usr/" + name
            prefix = "usr/lib/modules/" + RELEASE
            if name != prefix and not name.startswith(prefix + "/"):
                continue
            if member.isdir():
                entries[name] = (stat.S_IFDIR | member.mode, b"")
            elif member.isfile():
                entries[name] = (stat.S_IFREG | member.mode, archive.extractfile(member).read())
            elif member.issym():
                target = PurePosixPath(member.linkname)
                if target.is_absolute() or ".." in target.parts:
                    raise ValueError(f"External module symlink: {name}")
                entries[name] = (stat.S_IFLNK | member.mode, member.linkname.encode())
            else:
                raise ValueError(f"Unexpected module entry: {name}")
    for required in ("modules.dep", "modules.dep.bin", "modules.alias.bin", "modules.builtin"):
        if f"usr/lib/modules/{RELEASE}/{required}" not in entries:
            raise ValueError(f"Package lacks matching {required}")
    if not any("/kernel/drivers/ufs/" in n and ".ko" in n for n in entries):
        raise ValueError("Package has no UFS driver modules")
    return entries


def add_parents(entries):
    for name in list(entries):
        for parent in PurePosixPath(name).parents:
            if str(parent) != ".":
                entries.setdefault(str(parent), (stat.S_IFDIR | 0o755, b""))
    return dict(sorted(entries.items(), key=lambda item: (item[0].count("/"), item[0])))


def verify_kernel_bundle(directory):
    for line in (directory / "SHA256SUMS").read_text().splitlines():
        digest, name = line.split(maxsplit=1)
        name = safe_name(name.lstrip("*"))
        if sha((directory / name).read_bytes()) != digest:
            raise ValueError(f"Kernel artifact hash mismatch: {name}")
    if (directory / "kernel.release").read_text().strip() != RELEASE:
        raise ValueError("Kernel release does not match this assembly recipe")
    image = (directory / "Image").read_bytes()
    if image[56:60] != b"ARM\x64":
        raise ValueError("Expected an uncompressed ARM64 Image")
    config = (directory / "kernel.config").read_text()
    for flag in ("CONFIG_BLK_DEV_INITRD=y", "CONFIG_RD_GZIP=y", "CONFIG_RD_ZSTD=y",
                 "CONFIG_SQUASHFS_ZSTD=y", "CONFIG_SECURITY_APPARMOR=y"):
        if flag not in config.splitlines():
            raise ValueError(f"Kernel missing required support: {flag}")
    return image


def assemble(tree, bundle, firmware):
    image = verify_kernel_bundle(bundle)
    kernel_deb = bundle / f"linux-image-{RELEASE}_6.19.0-1_arm64.deb"
    entries = module_entries(kernel_deb.read_bytes())
    source_initrd = (tree / "casper/initrd").read_bytes()
    original = original_initrd_entries(source_initrd)
    manifest = json.loads((firmware / "manifest.json").read_text(encoding="utf-8-sig"))
    chosen = {}
    for name in FIRMWARE:
        candidates = [f for f in manifest["Files"] if f["Name"] == name]
        if not candidates:
            raise ValueError(f"Private firmware export lacks {name}")
        selected = max(candidates, key=lambda f: f["LastWriteTimeUtc"])
        data = (firmware / safe_name(selected["File"])).read_bytes()
        if sha(data) != selected["SHA256"]:
            raise ValueError(f"Private firmware hash mismatch: {name}")
        entries[f"{FW_PATH}/{name}"] = (stat.S_IFREG | 0o644, data)
        chosen[name] = selected

    builtin = entries[f"usr/lib/modules/{RELEASE}/modules.builtin"][1].decode().splitlines()
    available = {PurePosixPath(n).name.split(".ko")[0].replace("-", "_")
                 for n in list(entries) + builtin if ".ko" in n}
    old_modules = original["conf/modules"][1].decode().splitlines()
    requested = old_modules + ["ufs_qcom", "ufshcd_pltfrm", "phy_qcom_qmp_ufs", "sd_mod", "xhci_plat_hcd", "dwc3", "squashfs", "overlay", "vfat", "nls_ascii", "nls_cp437"]
    modules = []
    for line in requested:
        name = line.strip().split(" ")[0].replace("-", "_")
        if name in available and name not in modules and not name.startswith(("snd_", "soundwire")):
            modules.append(name)
    if "ufs_qcom" not in modules:
        raise ValueError("UFS controller driver not available")
    entries["conf/modules"] = (stat.S_IFREG | 0o644, ("\n".join(modules) + "\n").encode())
    entries["etc/udev/rules.d/99-book4edge-keyboard.rules"] = (stat.S_IFREG | 0o644, KEYBOARD_RULE.encode())
    hook = f'''#!/bin/sh
case "$1" in prereqs) exit 0 ;; esac
set -eu
test "$(uname -r)" = "{RELEASE}"
# /root is the casper RAM overlay, never an internal disk mount.
test -d /root/usr
mkdir -p /root/usr/lib/modules /root/{FW_PATH}
cp -a /usr/lib/modules/{RELEASE} /root/usr/lib/modules/
cp -a /{FW_PATH}/. /root/{FW_PATH}/
mkdir -p /root/etc/modprobe.d /root/etc/udev/rules.d /root/var/log
cp /etc/udev/rules.d/99-book4edge-keyboard.rules /root/etc/udev/rules.d/
printf '%s\\n' 'blacklist snd_soc_x1e80100' > /root/etc/modprobe.d/9to1-first-boot-speakers.conf
printf '%s\\n' 'Experimental Book4 Edge 16 live hardware test. Kernel/modules/firmware matched. Speakers disabled. Installation and internal partitions have not been prepared.' > /root/var/log/9to1-live-candidate.txt
'''
    hook_name = "scripts/casper-bottom/98book4edge-modules"
    entries[hook_name] = (stat.S_IFREG | 0o755, hook.encode())
    order = original["scripts/casper-bottom/ORDER"][1].decode()
    marker = '/scripts/casper-bottom/99casperboot "$@"'
    if order.count(marker) != 1:
        raise ValueError("Unexpected casper hook order")
    order = order.replace(marker, f'/{hook_name} "$@"\n[ -e /conf/param.conf ] && . /conf/param.conf\n' + marker)
    entries["scripts/casper-bottom/ORDER"] = (stat.S_IFREG | 0o755, order.encode())
    overlay = cpio(add_parents(entries))
    roundtrip, _ = read_cpio(overlay)
    if roundtrip != add_parents(entries):
        raise ValueError("Initramfs overlay failed round-trip validation")
    dest = tree / "book4edge"
    dest.mkdir(exist_ok=True)
    dtbs = list(bundle.glob("*book4*16*.dtb"))
    if len(dtbs) != 1:
        raise ValueError("Expected exactly one 16-inch Samsung device tree")
    (dest / "Image").write_bytes(image)
    shutil.copy2(dtbs[0], dest / "book4edge16.dtb")
    combined = source_initrd + b"\0" * (-len(source_initrd) % 4) + gzip.compress(overlay, compresslevel=6, mtime=0)
    (dest / "initrd").write_bytes(combined)
    stock_cfg = tree / "boot/grub/grub.cfg"
    backup = tree / "boot/grub/grub-stock.cfg"
    if not backup.exists():
        shutil.copy2(stock_cfg, backup)
    stock_cfg.write_text('''set timeout=30
set default=0
loadfont unicode
if [ "$lockdown" != "y" ]; then
    cutmem 0x8800000000 0x8fffffffff
fi
menuentry "Samsung Book4 Edge 16 - TRY Ubuntu (hardware test)" {
    set gfxpayload=keep
    devicetree /book4edge/book4edge16.dtb
    linux /book4edge/Image boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth console=tty0 ---
    initrd /book4edge/initrd
}
menuentry "UEFI firmware settings" {
    fwsetup
}
menuentry "Power off" {
    halt
}
''', encoding="utf-8", newline="\n")
    # The stock casper checksum list covers files we changed; regenerate it.
    checksums = []
    for p in sorted(tree.rglob("*")):
        if p.is_file() and p.name != "md5sum.txt":
            with p.open("rb") as f:
                digest = hashlib.file_digest(f, "md5").hexdigest()
            checksums.append(f"{digest}  ./{p.relative_to(tree).as_posix()}")
    (tree / "md5sum.txt").write_text("\n".join(checksums) + "\n", encoding="utf-8", newline="\n")
    result = {"kernelRelease": RELEASE, "hardwareTested": False, "purpose": "live hardware test only", "firmwareFiles": chosen,
              "moduleFiles": sum(".ko" in n for n in entries), "forcedModules": modules,
              "stockInitrdSHA256": sha(source_initrd), "initrdSHA256": sha(combined), "imageSHA256": sha(image),
              "initrdBytes": len(combined), "overlayBytes": len(overlay)}
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tree", required=True, type=Path)
    parser.add_argument("--kernel", required=True, type=Path)
    parser.add_argument("--firmware", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    result = assemble(args.tree, args.kernel, args.firmware)
    args.report.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({k: v for k, v in result.items() if k != "firmwareFiles"}, indent=2))
