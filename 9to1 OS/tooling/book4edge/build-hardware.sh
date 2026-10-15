#!/usr/bin/env bash
set -euo pipefail
[[ $# -ge 1 && $# -le 2 ]] || { echo 'Usage: build-hardware.sh NEW-WORK-DIRECTORY [--kernel]' >&2; exit 2; }
work=$1
mode=${2:---dtb}
[[ $mode == --dtb || $mode == --kernel ]] || exit 2
[[ ! -e $work ]] || { echo 'Use a new build directory.' >&2; exit 1; }
profile=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/profile.json
for tool in git make gcc flex bison; do command -v "$tool" >/dev/null || { echo "Missing build tool: $tool" >&2; exit 1; }; done
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
make -C "$work" ARCH=arm64 "${cross[@]}" defconfig
make -C "$work" ARCH=arm64 "${cross[@]}" -j"$(nproc)" "qcom/$dtb"
mkdir -p "$work/output"
cp "$profile" "$work/output/profile.json"
cp "$work/arch/arm64/boot/dts/qcom/$dtb" "$work/output/"
if [[ $mode == --kernel ]]; then
  # Image, modules and DTB must be from the same source/configuration.
  make -C "$work" ARCH=arm64 "${cross[@]}" -j"$(nproc)" LOCALVERSION=-9to1-book4edge16 bindeb-pkg
fi
sha256sum "$work/output/$dtb" > "$work/output/DTB.sha256"
echo "Experimental build in $work. No bootloader or device has been modified."
