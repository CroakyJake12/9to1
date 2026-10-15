#!/usr/bin/env bash
# Compile a separate 16-inch USB wiring candidate; never install it.
set -euo pipefail
work=$1
output=$2
recipe=$(cd "$(dirname "$0")" && pwd)
revision=6221fc7393c8cd04554df24c6853f50d61d6ab03
[[ ! -e $work && ! -e $output ]]
git init "$work"
git -C "$work" remote add origin https://github.com/n1c0la84/linux-book4-edge.git
git -C "$work" fetch --depth 1 origin "$revision"
git -C "$work" checkout --detach FETCH_HEAD
[[ $(git -C "$work" rev-parse HEAD) == "$revision" ]]
dts=$work/arch/arm64/boot/dts/qcom/x1e84100-samsung-galaxy-book4-edge-16.dts
# Apply precisely the same headphones-only sound changes as the kernel build.
python3 - "$recipe/build-next-hardware.sh" "$dts" <<'PY'
import sys
from pathlib import Path
script=Path(sys.argv[1]).read_text()
patch=script.split("<<'DTS'\n",1)[1].split('\nDTS\n',1)[0]
with Path(sys.argv[2]).open('a') as stream:
    stream.write(patch+'\n')
PY
cat "$recipe/np960xma-usb-test.dtsi" >> "$dts"
mkdir -p "$output"
gcc -E -nostdinc -undef -D__DTS__ -x assembler-with-cpp \
  -I "$work/include" -I "$work/arch/arm64/boot/dts" "$dts" > "$output/expanded.dts"
dtc -I dts -O dtb -o "$output/book4edge16-usb-test.dtb" "$output/expanded.dts"
test "$(fdtget "$output/book4edge16-usb-test.dtb" / model)" = 'Samsung Galaxy Book4 Edge (16 inch)'
test "$(fdtget "$output/book4edge16-usb-test.dtb" /soc@0/geniqup@bc0000/i2c@b98000 status)" = okay
test "$(fdtget -t x "$output/book4edge16-usb-test.dtb" /soc@0/geniqup@bc0000/i2c@b98000/redriver@4f reg)" = 4f
test "$(fdtget "$output/book4edge16-usb-test.dtb" /sound status)" = okay
! fdtget -l "$output/book4edge16-usb-test.dtb" /sound | grep -q '^wsa-dai-link$'
git -C "$work" diff > "$output/candidate.patch"
printf '%s\n' "$revision" > "$output/source.commit"
(cd "$output" && sha256sum ./* > SHA256SUMS)
