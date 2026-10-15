#!/usr/bin/env bash
set -euo pipefail
case ${1:---plan} in
  --plan) apply=0 ;;
  --apply) apply=1 ;;
  *) echo 'Usage: post-install.sh [--plan|--apply]' >&2; exit 2 ;;
esac
packages=(linux-firmware libgl1-mesa-dri mesa-vulkan-drivers mesa-utils vulkan-tools
  power-profiles-daemon python3 python3-gi gir1.2-gtk-4.0 bubblewrap xdg-utils
  freerdp3-x11 mokutil efibootmgr)
printf 'Ubuntu ARM64 packages: %s\n' "${packages[*]}"
printf '%s\n' 'Retain the working kernel and its matching device tree.' \
  'Extract Qualcomm firmware from your own Windows installation using qcom-firmware-extract when available.' \
  'Use the balanced power profile; test suspend, GPU and audio before enabling additional tuning.'
if (( ! apply )); then exit 0; fi
[[ $(id -u) -eq 0 ]] || { echo 'Run --apply with sudo on the Galaxy Book.' >&2; exit 1; }
[[ $(dpkg --print-architecture) == arm64 ]] || { echo 'Requires the ARM64 Galaxy Book, not the 5080 PC.' >&2; exit 1; }
. /etc/os-release
[[ $ID == ubuntu && $VERSION_ID == 26.04 ]] || { echo 'This package set is verified against Ubuntu 26.04 only.' >&2; exit 1; }
model=$(tr '\0' ' ' < /sys/firmware/devicetree/base/model)
[[ ${model,,} == *'samsung'* && ${model,,} == *'book4'* && ${model,,} == *'16'* ]] || {
  echo "Expected a Book4 Edge 16-inch device tree; found: $model" >&2; exit 1;
}
apt-get update
apt-get install -y "${packages[@]}"
if apt-cache show qcom-firmware-extract >/dev/null 2>&1; then
  apt-get install -y qcom-firmware-extract
else
  echo 'qcom-firmware-extract unavailable in configured repositories. See README; firmware extraction is still required.' >&2
fi
# No PPA, bootloader, kernel command line, audio amplifier or power-limit changes.
