#!/usr/bin/env bash
set -euo pipefail
[[ $# -ge 1 && $# -le 2 ]] || { echo 'Usage: build-hardware.sh NEW-WORK-DIRECTORY [--kernel]' >&2; exit 2; }
work=$1
mode=${2:---dtb}
[[ $mode == --dtb || $mode == --kernel ]] || exit 2
[[ ! -e $work ]] || { echo 'Use a new build directory.' >&2; exit 1; }
profile=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/profile.json
for tool in git make gcc flex bison python3; do command -v "$tool" >/dev/null || { echo "Missing build tool: $tool" >&2; exit 1; }; done
cross=()
if [[ $(uname -m) != aarch64 ]]; then
  command -v aarch64-linux-gnu-gcc >/dev/null || { echo 'Install gcc-aarch64-linux-gnu first.' >&2; exit 1; }
  cross=(CROSS_COMPILE=aarch64-linux-gnu-)
fi
readarray -t fields < <(python3 - "$profile" <<'PY'
import json,sys
p=json.load(open(sys.argv[1]))['kernel']
for key in ('repository','commit','dts'): print(p[key])
PY
)
git init "$work"
git -C "$work" remote add origin "${fields[0]}"
git -C "$work" fetch --depth 1 origin "${fields[1]}"
git -C "$work" checkout --detach FETCH_HEAD
[[ $(git -C "$work" rev-parse HEAD) == "${fields[1]}" ]]
dtb=$(basename -- "${fields[2]}" .dts).dtb
config=defconfig
if [[ $mode == --kernel ]]; then config=ubuntu_x1e_defconfig; fi
make -C "$work" ARCH=arm64 "${cross[@]}" "$config"
if [[ $mode == --kernel ]]; then
  # Use the pinned tree's Ubuntu configuration, including AppArmor and Casper
  # filesystem support. Debug symbols and unavailable distro signing keys are
  # unsuitable for this unsigned, locally built experimental kernel.
  "$work/scripts/config" --file "$work/.config" \
    --disable DEBUG_INFO --disable DEBUG_INFO_DWARF5 \
    --disable DEBUG_INFO_DWARF_TOOLCHAIN_DEFAULT --enable DEBUG_INFO_NONE \
    --disable DEBUG_INFO_BTF --set-str SYSTEM_TRUSTED_KEYS '' \
    --set-str SYSTEM_REVOCATION_KEYS '' --disable LOCALVERSION_AUTO \
    --disable SND_SOC_X1E80100
  make -C "$work" ARCH=arm64 "${cross[@]}" olddefconfig
  # The upstream speaker path lacks verified protection. Disable the card in
  # this first-boot candidate as well as omitting its driver; USB audio remains.
  printf '\n/ { sound { status = "disabled"; }; };\n' >> "$work/${fields[2]}"
fi
make -C "$work" ARCH=arm64 "${cross[@]}" -j"$(nproc)" "qcom/$dtb"
mkdir -p "$work/output"
cp "$profile" "$work/output/profile.json"
cp "$work/arch/arm64/boot/dts/qcom/$dtb" "$work/output/"
if [[ $mode == --kernel ]]; then
  # Image, modules and DTB must be from the same source/configuration.
  make -C "$work" ARCH=arm64 "${cross[@]}" -j"${JOBS:-$(nproc)}" \
    LOCALVERSION=-9to1-book4edge16 KDEB_PKGVERSION=6.19.0-1 bindeb-pkg
  cp "$work/.config" "$work/output/kernel.config"
  cp "$work/arch/arm64/boot/Image" "$work/output/Image"
  cp "$work/include/config/kernel.release" "$work/output/kernel.release"
  git -C "$work" diff -- "${fields[2]}" > "$work/output/first-boot-speakers-disabled.patch"
  find "$(dirname "$work")" -maxdepth 1 -name '*6.19.0*arm64.deb' -exec cp {} "$work/output/" \;
fi
(cd "$work/output" && sha256sum ./* > SHA256SUMS)
echo "Experimental build in $work. No bootloader or device has been modified."
