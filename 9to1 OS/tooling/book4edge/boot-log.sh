#!/bin/sh
# Live boot diagnostics. Never mount any partition except the configured USB.
set -u
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
umask 077
case " $(cat /proc/cmdline) " in *' book4edge.log=1 '*) ;; *) exit 0 ;; esac
test -f /etc/book4edge-log-target || exit 0
. /etc/book4edge-log-target

mountpoint=/run/book4edge-log-usb
phase=initramfs
test "${1:-}" != --userspace || phase=userspace
lockdir=/run/book4edge-log-$phase.lock
mkdir "$lockdir" 2>/dev/null || exit 0
out=
mounted=no

snapshot() {
    test -n "$out" || return 0
    dmesg > "$out/kernel.log.new" 2>&1 && mv "$out/kernel.log.new" "$out/kernel.log"
    if test ! -f "$out/kernel-first.log" && test -f "$out/kernel.log"; then
        cp "$out/kernel.log" "$out/kernel-first.log"
    fi
    for source in /run/initramfs/initramfs.debug /casper.log /root/var/log/casper.log /var/log/casper.log /run/book4edge-root-init.log /root/var/log/book4edge-internal-readonly.txt /var/log/book4edge-internal-readonly.txt; do
        test -f "$source" || continue
        name=${source##*/}
        tail -c 4194304 "$source" > "$out/$name.new" 2>&1 && mv "$out/$name.new" "$out/$name"
    done
    if test "$phase" = userspace && command -v journalctl >/dev/null 2>&1; then
        journalctl -b --no-pager -n 2000 > "$out/journal.log.new" 2>&1 && mv "$out/journal.log.new" "$out/journal.log"
        journalctl -b --no-pager -p warning -n 2000 > "$out/warnings.log.new" 2>&1 && mv "$out/warnings.log.new" "$out/warnings.log"
        journalctl -b --no-pager -n 2000 -u gdm.service -u gdm3.service -u display-manager.service > "$out/display-manager.log.new" 2>&1 && mv "$out/display-manager.log.new" "$out/display-manager.log"
    fi
    if test "$phase" = userspace && command -v timeout >/dev/null 2>&1; then
        timeout 3 systemctl --no-pager list-jobs > "$out/systemd-jobs.txt" 2>&1 || true
        # Read only standard kernel attributes; no EC commands or register scans.
        {
            for supply in /sys/class/power_supply/*; do
                test -d "$supply" || continue
                printf '\n%s\n' "$supply"
                timeout 2 cat "$supply/uevent" 2>&1 || true
            done
        } > "$out/power-supplies.txt.new"
        mv "$out/power-supplies.txt.new" "$out/power-supplies.txt"
        cat /proc/bus/input/devices > "$out/input-devices.txt" 2>&1 || true
        if command -v upower >/dev/null 2>&1; then
            timeout 3 upower --dump > "$out/upower.txt.new" 2>&1 && mv "$out/upower.txt.new" "$out/upower.txt"
        fi
        for result in /run/user/[0-9]*/book4edge-hardware-check.json; do
            test -f "$result" && test ! -L "$result" || continue
            head -c 65536 "$result" > "$out/user-hardware-check.json.new" && mv "$out/user-hardware-check.json.new" "$out/user-hardware-check.json"
        done
        if test "$((count % 6))" -eq 0; then
            {
                printf '\n=== PCI drivers ===\n'; timeout 3 lspci -nnk
                printf '\n=== USB devices ===\n'; timeout 3 lsusb
                printf '\n=== Network status ===\n'; timeout 3 nmcli device status
                printf '\n=== Radio state ===\n'; timeout 3 rfkill list
                printf '\n=== Bluetooth configuration ===\n'; timeout 3 btmgmt info
                printf '\n=== ALSA cards ===\n'; cat /proc/asound/cards
                printf '\n=== Playback devices ===\n'; timeout 3 aplay -l
                printf '\n=== Capture devices ===\n'; timeout 3 arecord -l
                printf '\n=== Storage protection ===\n'; lsblk -o NAME,MODEL,SIZE,RO,TYPE,MOUNTPOINTS
                printf '\n=== Camera devices ===\n'; ls -l /dev/video* /dev/media* 2>/dev/null
                printf '\n=== Deferred drivers ===\n'; cat /sys/kernel/debug/devices_deferred 2>/dev/null
                printf '\n=== Graphics drivers ===\n'; ls -l /sys/class/drm/card*/device/driver 2>/dev/null
                printf '\n=== Brightness and keyboard lights ===\n'; ls /sys/class/backlight /sys/class/leds 2>/dev/null
                printf '\n=== CPU frequency drivers ===\n'; cat /sys/devices/system/cpu/cpufreq/policy*/scaling_driver 2>/dev/null
            } > "$out/hardware-status.txt.new" 2>&1
            mv "$out/hardware-status.txt.new" "$out/hardware-status.txt"
        fi
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
    rmdir "$lockdir" 2>/dev/null || true
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
        cat /proc/mounts > "$out/mounts.txt"
        printf '%s\n' "$phase reached; USB identity matched; this is not a hardware pass" > "$out/logger-started.txt"
        count=0
        while test "$count" -lt 720; do
            snapshot
            sleep 5
            count=$((count + 1))
        done
        exit 0
    fi
    sleep 2
    attempt=$((attempt + 1))
done
