#!/bin/sh
# CakeOS rescue boot helper: show IP on console, collect diagnostics.
# Runs as root via cakeos-rescue-net.service. Never touches device firmware
# or internal storage; live session only.
set -eu

# The live-session user is created at boot by casper, so its rescue password
# can only be set here (chpasswd fails at image build time: no such user yet).
if id ubuntu >/dev/null 2>&1; then
    echo 'ubuntu:cakeos-rescue' | chpasswd || true
fi

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
echo "SSH is running. Log in as ubuntu over the network." > /dev/console

if [ -x /usr/libexec/cakeos/cakeos-diagnostics ]; then
    /usr/libexec/cakeos/cakeos-diagnostics >> /dev/console 2>&1 || true
fi
