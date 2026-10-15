#!/bin/sh
# Opt-in live RAM overlay only. No bus scans, EC-space writes or firmware updates.
set -eu
case " $(cat /proc/cmdline) " in *' book4edge.battery=1 '*) ;; *) exit 0 ;; esac
test "$(uname -r)" = 6.19.0-9to1-book4edge16
test "$(cat /sys/class/dmi/id/board_name)" = NP960XMA-KB1UK
test -f /var/log/book4edge-internal-readonly.txt
grep -q 'protected read-only' /var/log/book4edge-internal-readonly.txt
bus=
attempt=0
while test -z "$bus" && test "$attempt" -lt 30; do
    for candidate in /sys/bus/i2c/devices/i2c-*; do
        for link in "$candidate/of_node" "$candidate/device/of_node"; do
            node=$(readlink -f "$link") || continue
            case "$node" in
                */soc@0/geniqup@bc0000/i2c@b80000)
                    found=${candidate##*/}
                    test -z "$bus" || test "$bus" = "$found"
                    bus=$found
                    ;;
            esac
        done
    done
    test -n "$bus" && break
    attempt=$((attempt + 1))
    sleep 1
done
test -n "$bus" || { echo 'Expected keyboard/EC I2C controller not found'; exit 1; }
test -d "/sys/bus/i2c/devices/${bus#i2c-}-0005"
attempt=0
while ! grep -q '0CF2:9050' /proc/bus/input/devices; do
    attempt=$((attempt + 1))
    test "$attempt" -lt 30 || { echo 'Expected Samsung keyboard identity absent'; exit 1; }
    sleep 1
done
device=/sys/bus/i2c/devices/${bus#i2c-}-0064
test ! -e "$device" || { echo 'EC address already owned; refusing to rebind'; exit 1; }
insmod /usr/lib/book4edge/book4_ec.ko
printf 'book4-ec 0x64\n' > "/sys/bus/i2c/devices/$bus/new_device"
test -L "$device/driver" && test -d /sys/class/power_supply/book4-battery || {
    echo 'Battery probe did not produce validated readings'
    exit 1
}
echo 'Samsung battery readout active; physical percentage and charger tests still required'
cat /sys/class/power_supply/book4-battery/uevent
