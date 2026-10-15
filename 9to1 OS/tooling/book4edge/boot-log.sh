#!/bin/sh
# Initramfs-only diagnostics. Never mount any partition except the configured USB.
set -u
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
umask 077
case " $(cat /proc/cmdline) " in *' book4edge.log=1 '*) ;; *) exit 0 ;; esac
test -f /etc/book4edge-log-target || exit 0
. /etc/book4edge-log-target

mountpoint=/run/book4edge-log-usb
phase=initramfs
test "${1:-}" != --userspace || phase=userspace
out=
mounted=no

snapshot() {
    test -n "$out" || return 0
    dmesg > "$out/kernel.log.new" 2>&1 && mv "$out/kernel.log.new" "$out/kernel.log"
    for source in /run/initramfs/initramfs.debug /casper.log /root/var/log/casper.log; do
        test -f "$source" || continue
        name=${source##*/}
        tail -c 4194304 "$source" > "$out/$name.new" 2>&1 && mv "$out/$name.new" "$out/$name"
    done
    if test "$phase" = userspace && command -v journalctl >/dev/null 2>&1; then
        journalctl -b --no-pager -n 2000 > "$out/journal.log.new" 2>&1 && mv "$out/journal.log.new" "$out/journal.log"
    fi
    cat /proc/uptime > "$out/last-snapshot-uptime.txt"
    sync
}

finish() {
    trap - TERM INT EXIT
    if test "$mounted" = yes; then
        snapshot
        test -z "$out" || printf '%s\n' "$phase logger stopped; this alone does not prove successful desktop boot" > "$out/logger-finished.txt"
        sync
        umount "$mountpoint" || true
    fi
    exit 0
}
trap finish TERM INT EXIT

# Require USB ancestry, the configured SanDisk serial prefix, exact disk size,
# partition 2 and its GPT partition UUID before even trying a writable mount.
find_target() {
    for sysdisk in /sys/class/block/sd*; do
        test -f "$sysdisk/size" && test ! -f "$sysdisk/partition" || continue
        test "$(cat "$sysdisk/size")" = "$DISK_SECTORS" || continue
        ancestor=$(readlink -f "$sysdisk/device") || continue
        usb_match=no
        while test "$ancestor" != / && test -n "$ancestor"; do
            if test -f "$ancestor/idVendor" && test -f "$ancestor/serial"; then
                vendor=$(cat "$ancestor/idVendor")
                serial=$(tr '[:upper:]' '[:lower:]' < "$ancestor/serial")
                case "$serial" in "$SERIAL_PREFIX"*) test "$vendor" = 0781 && usb_match=yes ;; esac
                break
            fi
            ancestor=${ancestor%/*}
        done
        test "$usb_match" = yes || continue
        disk=${sysdisk##*/}
        part=/dev/${disk}2
        test -b "$part" || continue
        uuid=$(blkid -s PARTUUID -o value "$part" 2>/dev/null) || continue
        test "$uuid" = "$PARTUUID" || continue
        kind=$(blkid -s TYPE -o value "$part" 2>/dev/null) || continue
        test "$kind" = exfat || continue
        printf '%s\n' "$part"
        return 0
    done
    return 1
}

printf '%s\n' "$$" > /run/book4edge-log.pid
mkdir -p "$mountpoint"
modprobe exfat 2>/dev/null || true
attempt=0
while test "$attempt" -lt 120; do
    part=$(find_target) || part=
    if test -n "$part" && mount -t exfat -o rw,sync,nosuid,nodev,noexec "$part" "$mountpoint"; then
        mounted=yes
        bootid=$(cat /proc/sys/kernel/random/boot_id)
        case "$bootid" in ''|*[!a-f0-9-]*) exit 0 ;; esac
        out=$mountpoint/boot-logs/$bootid/$phase
        mkdir -p "$out" || exit 0
        cat /proc/cmdline > "$out/cmdline.txt"
        uname -a > "$out/kernel-version.txt"
        printf '%s\n' "$phase reached; USB identity matched; this is not a hardware pass" > "$out/logger-started.txt"
        count=0
        while test "$count" -lt 180; do
            snapshot
            sleep 5
            count=$((count + 1))
        done
        exit 0
    fi
    sleep 2
    attempt=$((attempt + 1))
done
