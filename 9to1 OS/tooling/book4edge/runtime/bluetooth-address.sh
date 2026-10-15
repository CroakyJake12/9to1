#!/bin/bash
# Use this machine's existing public address only when the UART radio needs it.
# No pairing keys are imported and no firmware is flashed.
set -eu
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
grep -a -q 'samsung,galaxy-book4-edge-16' /proc/device-tree/compatible
test -f /etc/book4edge/bluetooth-address
address=$(tr -d '\r\n' < /etc/book4edge/bluetooth-address)
[[ $address =~ ^([0-9A-F]{2}:){5}[0-9A-F]{2}$ ]]
for ((attempt=0; attempt<60; attempt++)); do
    if test -d /sys/class/bluetooth/hci0; then
        device=$(readlink -f /sys/class/bluetooth/hci0/device)
        case "$device" in *serial*|*uart*|*bluetooth*) ;; *) exit 1 ;; esac
        current=$(timeout 3 btmgmt --index 0 info 2>/dev/null || true)
        if grep -qi "addr $address" <<< "$current"; then
            echo 'Existing Bluetooth address already configured.'
            exit 0
        fi
        config=$(timeout 3 btmgmt --index 0 config 2>/dev/null || true)
        if grep -q 'missing options: public-address' <<< "$config"; then
            timeout 5 btmgmt --index 0 public-addr "$address" >/dev/null
            echo 'Restored this laptop original Bluetooth radio address.'
            exit 0
        fi
        if grep -q 'addr ' <<< "$current" && ! grep -q 'addr 00:00:00:00:00:00' <<< "$current"; then
            echo 'Radio already has an address; leaving it unchanged.'
            exit 0
        fi
    fi
    sleep 1
done
echo 'Bluetooth address setup did not complete; inspect the saved boot log.' >&2
exit 1
