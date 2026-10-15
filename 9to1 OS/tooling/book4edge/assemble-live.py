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
import re
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
    if p.is_absolute() or ".." in p.parts or "\\" in name or "\0" in name or ":" in name:
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


def logging_entries(original, target):
    """Opt-in USB logging; private identity is supplied by the local inventory."""
    uuid = str(target['partuuid']).lower().strip('{}')
    serial = str(target['serialPrefix']).lower()
    size = int(target['diskBytes'])
    if not re.fullmatch(r'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}', uuid):
        raise ValueError('Invalid log partition UUID')
    if not re.fullmatch(r'[0-9a-f]{20,64}', serial) or size <= 0 or size % 512:
        raise ValueError('Invalid log USB identity')
    config = f"PARTUUID='{uuid}'\nSERIAL_PREFIX='{serial}'\nDISK_SECTORS='{size // 512}'\n"
    entries = {
        'etc/book4edge-log-target': (stat.S_IFREG | 0o600, config.encode()),
        'usr/sbin/book4edge-boot-log': (stat.S_IFREG | 0o755, Path(__file__).with_name('boot-log.sh').read_bytes()),
    }
    entries['usr/sbin/book4edge-init'] = (stat.S_IFREG | 0o755, b'''#!/bin/sh
# This wrapper remains PID 1 and execs the real init after starting file capture.
printf '%s\\n' 'Reached live root; starting logger before systemd' > /run/book4edge-root-init.log
/usr/sbin/book4edge-boot-log --userspace </dev/null >>/run/book4edge-root-init.log 2>&1 &
exec /sbin/init "$@" >>/run/book4edge-root-init.log 2>&1
''')
    start = b'''#!/bin/sh
case "$1" in prereqs) exit 0 ;; esac
/usr/sbin/book4edge-boot-log </dev/null >/run/book4edge-logger-errors.txt 2>&1 &
exit 0
'''
    stop = b'''#!/bin/sh
case "$1" in prereqs) exit 0 ;; esac
test -f /run/book4edge-log.pid || exit 0
pid=$(cat /run/book4edge-log.pid)
case "$pid" in ''|*[!0-9]*) exit 0 ;; esac
grep -q '/usr/sbin/book4edge-boot-log' "/proc/$pid/cmdline" 2>/dev/null || exit 0
kill -TERM "$pid" 2>/dev/null || true
count=0
while kill -0 "$pid" 2>/dev/null && test "$count" -lt 15; do
    sleep 1
    count=$((count + 1))
done
exit 0
'''
    for phase, name, script in [('init-top', '00book4edge-log', start), ('init-bottom', '00book4edge-log-stop', stop)]:
        script_path = f'scripts/{phase}/{name}'
        entries[script_path] = (stat.S_IFREG | 0o755, script)
        order_path = f'scripts/{phase}/ORDER'
        entries[order_path] = (stat.S_IFREG | 0o755, f'/{script_path} "$@"\n[ -e /conf/param.conf ] && . /conf/param.conf\n'.encode() + original[order_path][1])
    entries['etc/systemd/system/book4edge-boot-log.service'] = (stat.S_IFREG | 0o644, b'''[Unit]
Description=Save this live boot's diagnostics to the approved USB
DefaultDependencies=no
After=systemd-udevd.service
Before=sysinit.target
ConditionKernelCommandLine=book4edge.log=1
[Service]
Type=simple
ExecStart=/usr/sbin/book4edge-boot-log --userspace
TimeoutStopSec=20
[Install]
WantedBy=sysinit.target
''')
    return entries


def assemble(tree, bundle, firmware, linux_firmware=None, log_target=None, display_dtb=None, battery_module=None):
    image = verify_kernel_bundle(bundle)
    kernel_deb = bundle / f"linux-image-{RELEASE}_6.19.0-1_arm64.deb"
    entries = module_entries(kernel_deb.read_bytes())
    source_initrd = (tree / "casper/initrd").read_bytes()
    original = original_initrd_entries(source_initrd)
    if log_target is not None:
        entries.update(logging_entries(original, log_target))
    battery_report = None
    if battery_module is not None:
        if log_target is None or display_dtb is None:
            raise ValueError('Battery testing requires the verified display and USB logging')
        battery_report = json.loads(battery_module.with_name('battery-test.json').read_text())
        data = battery_module.read_bytes()
        source = Path(__file__).with_name('battery-test') / 'book4_ec.c'
        if (battery_report['kernelRelease'] != RELEASE or
                battery_report['moduleSHA256'] != sha(data) or
                battery_report['sourceSHA256'] != sha(source.read_text().encode()) or
                not data.startswith(b'\x7fELF\x02\x01') or data[18:20] != b'\xb7\0'):
            raise ValueError('Battery module provenance, source or architecture mismatch')
        entries['usr/lib/book4edge/book4_ec.ko'] = (stat.S_IFREG | 0o644, data)
        entries['usr/sbin/book4edge-battery-test'] = (stat.S_IFREG | 0o755,
            source.with_name('start-battery-test.sh').read_text().encode())
        entries['etc/systemd/system/book4edge-battery-test.service'] = (stat.S_IFREG | 0o644, b'''[Unit]
Description=Opt-in Samsung battery reporting test
After=systemd-udev-trigger.service
ConditionKernelCommandLine=book4edge.battery=1
[Service]
Type=oneshot
ExecStart=/usr/sbin/book4edge-battery-test
RemainAfterExit=yes
[Install]
WantedBy=multi-user.target
''')
    runtime_firmware = []
    if linux_firmware is not None:
        runtime_manifest = json.loads((linux_firmware / "manifest.json").read_text(encoding="utf-8-sig"))
        for record in runtime_manifest["firmware"]:
            name = safe_name(record["file"])
            if not (name.startswith(("qca/", "ath12k/WCN7850/hw2.0/")) or name == "qcom/gen70500_sqe.fw"):
                raise ValueError(f"Unexpected runtime firmware path: {name}")
            data = (linux_firmware / name).read_bytes()
            if sha(data) != record["sha256"]:
                raise ValueError(f"Runtime firmware hash mismatch: {name}")
            entries[f"usr/lib/firmware/updates/{name}"] = (stat.S_IFREG | 0o644, data)
            runtime_firmware.append(record)
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
    disk_rule = 'SUBSYSTEM=="block", ATTRS{model}=="THGJFJT2T85BAT0A*", ENV{UDISKS_IGNORE}="1"\n'
    entries["etc/udev/rules.d/99-book4edge-internal-test.rules"] = (stat.S_IFREG | 0o644, disk_rule.encode())
    hook = f'''#!/bin/sh
case "$1" in prereqs) exit 0 ;; esac
set -eu
test "$(uname -r)" = "{RELEASE}"
# Protect the specifically inventoried internal UFS disk for this live test.
# This changes only the running kernel's read-only flag, never on-disk metadata.
for sysdisk in /sys/class/block/sd*; do
    test -f "$sysdisk/device/model" || continue
    model=$(tr -d '[:space:]' < "$sysdisk/device/model")
    test "$model" = THGJFJT2T85BAT0A || continue
    disk=${{sysdisk##*/}}
    blockdev --setro "/dev/$disk"
    test "$(blockdev --getro "/dev/$disk")" = 1
    for syspart in "$sysdisk"/"$disk"*; do
        test -f "$syspart/partition" || continue
        part=${{syspart##*/}}
        blockdev --setro "/dev/$part"
        test "$(blockdev --getro "/dev/$part")" = 1
    done
    printf '%s\\n' "$disk protected read-only for live testing" >> /run/book4edge-internal-readonly
done
# /root is the casper RAM overlay, never an internal disk mount.
test -d /root/usr
mkdir -p /root/usr/lib/modules /root/{FW_PATH}
cp -a /usr/lib/modules/{RELEASE} /root/usr/lib/modules/
cp -a /usr/lib/firmware/updates/. /root/usr/lib/firmware/updates/
mkdir -p /root/etc/modprobe.d /root/etc/udev/rules.d /root/var/log
cp /etc/udev/rules.d/99-book4edge-keyboard.rules /root/etc/udev/rules.d/
cp /etc/udev/rules.d/99-book4edge-internal-test.rules /root/etc/udev/rules.d/
if test -f /etc/book4edge-log-target; then
    mkdir -p /root/usr/sbin /root/etc/systemd/system/sysinit.target.wants
    cp /etc/book4edge-log-target /root/etc/
    cp /usr/sbin/book4edge-boot-log /root/usr/sbin/
    cp /usr/sbin/book4edge-init /root/usr/sbin/
    cp /etc/systemd/system/book4edge-boot-log.service /root/etc/systemd/system/
    ln -sf ../book4edge-boot-log.service /root/etc/systemd/system/sysinit.target.wants/book4edge-boot-log.service
fi
if test -f /usr/lib/book4edge/book4_ec.ko; then
    mkdir -p /root/usr/lib/book4edge /root/usr/sbin /root/etc/systemd/system/multi-user.target.wants
    cp /usr/lib/book4edge/book4_ec.ko /root/usr/lib/book4edge/
    cp /usr/sbin/book4edge-battery-test /root/usr/sbin/
    cp /etc/systemd/system/book4edge-battery-test.service /root/etc/systemd/system/
    ln -sf ../book4edge-battery-test.service /root/etc/systemd/system/multi-user.target.wants/book4edge-battery-test.service
fi
if test -f /run/book4edge-internal-readonly; then
    cp /run/book4edge-internal-readonly /root/var/log/book4edge-internal-readonly.txt
fi
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
    display_hash = None
    if display_dtb is not None:
        if log_target is None:
            raise ValueError('Native display testing requires automatic USB logging')
        display_report = json.loads(display_dtb.with_name('display-test.json').read_text())
        display_hash = sha(display_dtb.read_bytes())
        if display_report['originalDTBSHA256'] != sha(dtbs[0].read_bytes()) or display_report['displayDTBSHA256'] != display_hash:
            raise ValueError('Display test does not match the verified original DTB')
        shutil.copy2(display_dtb, dest / 'book4edge16-gpu.dtb')
    combined = source_initrd + b"\0" * (-len(source_initrd) % 4) + gzip.compress(overlay, compresslevel=6, mtime=0)
    (dest / "initrd").write_bytes(combined)
    stock_cfg = tree / "boot/grub/grub.cfg"
    backup = tree / "boot/grub/grub-stock.cfg"
    if not backup.exists():
        shutil.copy2(stock_cfg, backup)
    # ISO copies on Windows retain the read-only attribute on these output files.
    stock_cfg.chmod(stock_cfg.stat().st_mode | stat.S_IWUSR)
    stock_cfg.write_text(grub_config(log_target is not None, display_dtb is not None, battery_module is not None), encoding="utf-8", newline="\n")
    # The stock casper checksum list covers files we changed; regenerate it.
    checksums = []
    for p in sorted(tree.rglob("*")):
        if p.is_file() and p.name != "md5sum.txt":
            with p.open("rb") as f:
                digest = hashlib.file_digest(f, "md5").hexdigest()
            checksums.append(f"{digest}  ./{p.relative_to(tree).as_posix()}")
    checksum_file = tree / "md5sum.txt"
    if checksum_file.exists():
        checksum_file.chmod(checksum_file.stat().st_mode | stat.S_IWUSR)
    checksum_file.write_text("\n".join(checksums) + "\n", encoding="utf-8", newline="\n")
    result = {"kernelRelease": RELEASE, "hardwareTested": False, "purpose": "live hardware test only", "firmwareFiles": chosen,
              "runtimeFirmware": runtime_firmware,
              "moduleFiles": sum(".ko" in n for n in entries), "forcedModules": modules,
              "stockInitrdSHA256": sha(source_initrd), "initrdSHA256": sha(combined), "imageSHA256": sha(image),
              "dtbSHA256": sha((dest / "book4edge16.dtb").read_bytes()),
              "grubSHA256": sha(stock_cfg.read_bytes()),
              "internalDiskReadOnlyRule": "KIOXIA THGJFJT2T85BAT0A; verify lsblk RO before testing",
              "usbBootLoggingConfigured": log_target is not None,
              "displayTestDTBSHA256": display_hash,
              "batteryTestModule": battery_report,
              "initrdBytes": len(combined), "overlayBytes": len(overlay)}
    return result


def grub_config(enable_usb_logging=False, native_display=False, battery_test=False):
    # Keep diagnostics separate: reduced graphics is not a hardware-support pass.
    config = '''set timeout=-1
set default=0
loadfont unicode
if [ "$lockdown" = "y" ]; then
    echo "This experimental Samsung test cannot run while GRUB is locked down."
    sleep --interruptible 30
    halt
fi
insmod mmap
if cutmem 0x8800000000 0x8fffffffff; then
    echo "Samsung memory reservation applied."
else
    echo "Required memory reservation failed; stopping the test boot."
    sleep --interruptible 30
    halt
fi
insmod fdt
menuentry "Book4 Edge - SLOW DIAGNOSTIC log capture (select this first)" {
    set gfxpayload=keep
    echo "[1/4] Loading the Samsung 16-inch device tree..."
    if devicetree /book4edge/book4edge16.dtb; then
        echo "[2/4] Loading the matched Linux kernel..."
        if linux /book4edge/Image boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth cma=256M console=tty0 acpi=off earlycon=efifb keep_bootcon boot_delay=200 loglevel=7 printk.time=1 log_buf_len=4M panic=0 fbcon=nodefer nomodeset module_blacklist=msm noplymouth plymouth.enable=0 systemd.show_status=1 systemd.unit=multi-user.target ---; then
            echo "[3/4] Loading the matching initramfs (about 305 MB)..."
            if initrd /book4edge/initrd; then
                echo "[4/4] Starting Linux in 5 seconds. Record the screen now."
                echo "This diagnostic deliberately slows kernel messages. Allow several minutes while text changes."
                sleep --interruptible 5
                boot
            fi
        fi
    fi
    echo "A boot command returned or failed. Photograph the messages above."
    sleep --interruptible 300
}
menuentry "Book4 Edge - previous DIAGNOSTIC text boot" {
    set gfxpayload=keep
    echo "[1/4] Loading the Samsung 16-inch device tree..."
    if devicetree /book4edge/book4edge16.dtb; then
        echo "[2/4] Loading the matched Linux kernel..."
        if linux /book4edge/Image boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth cma=256M console=tty0 acpi=off earlycon=efifb loglevel=7 ignore_loglevel initcall_debug printk.time=1 log_buf_len=4M panic=0 fbcon=nodefer nomodeset module_blacklist=msm noplymouth plymouth.enable=0 systemd.show_status=1 systemd.unit=multi-user.target ---; then
            echo "[3/4] Loading the matching initramfs (about 305 MB)..."
            if initrd /book4edge/initrd; then
                echo "[4/4] Starting Linux in 5 seconds. Film the next messages if it hangs."
                sleep --interruptible 5
                boot
            fi
        fi
    fi
    echo "A boot command returned or failed. Photograph the messages above."
    sleep --interruptible 300
}
menuentry "Samsung Book4 Edge 16 - TRY Ubuntu (hardware test)" {
    set gfxpayload=keep
    devicetree /book4edge/book4edge16.dtb
    linux /book4edge/Image boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth cma=256M console=tty0 ---
    initrd /book4edge/initrd
}
menuentry "UEFI firmware settings" {
    fwsetup
}
menuentry "Power off" {
    halt
}
'''
    # initramfs-tools treats panic=0 as an instruction to halt on its own errors.
    # Omitting it preserves the diagnostic shell and time to flush USB logs.
    config = config.replace(' panic=0', '')
    if enable_usb_logging:
        auto_entry = '''menuentry "Book4 Edge - SAVE BOOT LOGS TO USB (select this first)" {
    set gfxpayload=keep
    echo "Loading Samsung device tree, kernel and automatic USB logger..."
    if devicetree /book4edge/book4edge16.dtb; then
        if linux /book4edge/Image boot=casper nopersistent clk_ignore_unused pd_ignore_unused arm64.nopauth cma=256M console=tty0 acpi=off loglevel=7 printk.time=1 log_buf_len=4M fbcon=nodefer nomodeset module_blacklist=msm noplymouth plymouth.enable=0 systemd.show_status=1 systemd.unit=multi-user.target systemd.log_target=kmsg systemd.log_level=info udev.log_level=info init=/usr/sbin/book4edge-init book4edge.log=1 debug ---; then
            if initrd /book4edge/initrd; then
                echo "Boot logs will be saved to BOOK4E_DATA/boot-logs once the USB is available."
                sleep --interruptible 5
                boot
            fi
        fi
    fi
    echo "A boot command returned or failed before automatic logging."
    sleep --interruptible 300
}
'''
        if native_display:
            display_entry = auto_entry.replace('SAVE BOOT LOGS TO USB', 'NATIVE DISPLAY + USB LOGS')
            display_entry = display_entry.replace('/book4edge/book4edge16.dtb', '/book4edge/book4edge16-gpu.dtb')
            display_entry = display_entry.replace(' nomodeset module_blacklist=msm', '')
            display_entry = display_entry.replace(' systemd.unit=multi-user.target', '')
            auto_entry = auto_entry.replace(' (select this first)', ' (firmware display fallback)')
            if battery_test:
                battery_entry = display_entry.replace('NATIVE DISPLAY + USB LOGS', 'TOUCHSCREEN + BATTERY TEST')
                battery_entry = battery_entry.replace(' book4edge.log=1', ' module_blacklist=qcom_battmgr book4edge.battery=1 book4edge.log=1')
                display_entry = battery_entry + display_entry.replace(' (select this first)', ' (without battery test)')
            auto_entry = display_entry + auto_entry
        config = config.replace('insmod fdt\n', 'insmod fdt\n' + auto_entry, 1)
    return config


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tree", required=True, type=Path)
    parser.add_argument("--kernel", required=True, type=Path)
    parser.add_argument("--firmware", required=True, type=Path)
    parser.add_argument("--linux-firmware", type=Path, help="Hash-manifested Qualcomm runtime files for early driver probing")
    parser.add_argument("--log-target", type=Path, help="Private JSON identifying the approved exFAT USB data partition for boot logs")
    parser.add_argument("--display-dtb", type=Path, help="Separately verified native-display DTB and adjacent display-test.json")
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    result = assemble(args.tree, args.kernel, args.firmware, args.linux_firmware,
                      json.loads(args.log_target.read_text(encoding='utf-8-sig')) if args.log_target else None,
                      args.display_dtb)
    args.report.write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({k: v for k, v in result.items() if k not in ("firmwareFiles", "runtimeFirmware")}, indent=2))
