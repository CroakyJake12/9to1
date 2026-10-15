#!/usr/bin/env bash
# Build modules and a matching DTB; no physical device is accessed.
set -euo pipefail
work=$1
headers=$2
output=$3
recipe=$(cd "$(dirname "$0")" && pwd)
revision=6221fc7393c8cd04554df24c6853f50d61d6ab03
release=7.2.7-9to1-book4edge16-next
[[ ! -e $work && ! -e $output && $(uname -m) == aarch64 ]]
git init "$work"
git -C "$work" remote add origin https://github.com/n1c0la84/linux-book4-edge.git
git -C "$work" sparse-checkout init --cone
git -C "$work" sparse-checkout set arch/arm64/boot/dts/qcom include/dt-bindings sound/soc/qcom sound/soc/codecs drivers/media/i2c
git -C "$work" fetch --filter=blob:none --depth 1 origin "$revision"
git -C "$work" checkout --detach FETCH_HEAD
[[ $(git -C "$work" rev-parse HEAD) == "$revision" ]]
python3 "$recipe/patch-media-modules.py" "$work"
mkdir -p "$work/media-modules/qdsp6" "$output"
cp "$work/sound/soc/qcom/x1e80100.c" "$work/sound/soc/codecs/wsa883x.c" \
   "$work/drivers/media/i2c/ov02c10.c" "$work/media-modules/"
cp "$work/sound/soc/qcom/"*.h "$work/media-modules/"
cp "$work/sound/soc/qcom/qdsp6/"*.h "$work/media-modules/qdsp6/"
cat > "$work/media-modules/Makefile" <<'MAKE'
obj-m += snd-soc-wsa883x.o snd-soc-x1e80100.o ov02c10.o
snd-soc-wsa883x-y := wsa883x.o
snd-soc-x1e80100-y := x1e80100.o
MAKE
make -C "$headers" M="$work/media-modules" -j"$(nproc)" modules
for module in snd-soc-wsa883x snd-soc-x1e80100 ov02c10; do
    test "$(modinfo -F vermagic "$work/media-modules/$module.ko" | cut -d' ' -f1)" = "$release"
    readelf -h "$work/media-modules/$module.ko" | grep -q 'Machine:.*AArch64'
    cp "$work/media-modules/$module.ko" "$output/"
done
dts=$work/arch/arm64/boot/dts/qcom/x1e84100-samsung-galaxy-book4-edge-16.dts
# Keep the original two address-2 speaker routes. No tweeter route is added.
printf '\n&{/sound} { status = "okay"; };\n' >> "$dts"
cat "$recipe/np960xma-usb-test.dtsi" "$recipe/np960xma-camera-test.dtsi" >> "$dts"
gcc -E -nostdinc -undef -D__DTS__ -x assembler-with-cpp \
    -I "$work/include" -I "$work/arch/arm64/boot/dts" "$dts" > "$output/expanded.dts"
dtc -I dts -O dtb -o "$output/book4edge16-media.dtb" "$output/expanded.dts"
test "$(fdtget "$output/book4edge16-media.dtb" / model)" = 'Samsung Galaxy Book4 Edge (16 inch)'
test "$(fdtget "$output/book4edge16-media.dtb" /soc@0/cci@ac15000/i2c-bus@0/camera@36 compatible)" = ovti,ov02c10
test "$(fdtget "$output/book4edge16-media.dtb" /sound status)" = okay
git -C "$work" diff > "$output/candidate.patch"
printf '%s\n' "$revision" > "$output/source.commit"
printf '%s\n' "$release" > "$output/kernel.release"
(cd "$output" && sha256sum ./* > SHA256SUMS)
