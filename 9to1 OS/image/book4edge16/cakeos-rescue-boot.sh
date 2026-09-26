#!/bin/sh
# HavenOS rescue boot helper: report network state and collect diagnostics.
# Runs as root via cakeos-rescue-net.service. It does not set credentials,
# enable remote login, or touch device firmware/internal storage.
set -eu

echo "CakeOS rescue: waiting for network..." > /dev/console
for _ in $(seq 1 30); do
    if ip -4 route show default 2>/dev/null | grep -q .; then
        break
    fi
    sleep 2
done

echo "CakeOS rescue IP addresses:" > /dev/console
ip -4 -brief addr show >> /dev/console 2>&1 || true
echo "" > /dev/console
echo "Network inspection complete. This helper does not configure remote login." > /dev/console

if [ -x /usr/libexec/cakeos/cakeos-diagnostics ]; then
    /usr/libexec/cakeos/cakeos-diagnostics >> /dev/console 2>&1 || true
fi
