#!/usr/bin/env bash
# Build a separate candidate; never install or write physical media.
set -euo pipefail
work=$1
base_config=$2
[[ ! -e $work && -f $base_config ]]
[[ $(uname -m) == aarch64 ]]
revision=6221fc7393c8cd04554df24c6853f50d61d6ab03
release=7.2.7-9to1-book4edge16-next
git init "$work"
git -C "$work" remote add origin https://github.com/n1c0la84/linux-book4-edge.git
git -C "$work" fetch --depth 1 origin "$revision"
git -C "$work" checkout --detach FETCH_HEAD
[[ $(git -C "$work" rev-parse HEAD) == "$revision" ]]
# Kconfig parses this include even with EVDI disabled. Use the exact gitlink
# pinned by the kernel tree; none of these unrelated custom drivers is built.
git -C "$work" submodule update --init --depth 1 drivers/custom/evdi
cp "$base_config" "$work/.config"
"$work/scripts/config" --file "$work/.config" \
  --disable DEBUG_INFO --disable DEBUG_INFO_DWARF5 \
  --disable DEBUG_INFO_DWARF_TOOLCHAIN_DEFAULT --enable DEBUG_INFO_NONE \
  --disable DEBUG_INFO_BTF --set-str SYSTEM_TRUSTED_KEYS '' \
  --set-str SYSTEM_REVOCATION_KEYS '' --disable LOCALVERSION_AUTO \
  --module SND_SOC_X1E80100 --disable SND_SOC_WSA883X --disable SND_SOC_WSA884X \
  --module EC_SAMSUNG_GALAXYBOOK --module TYPEC_SAMSUNG_EMUEC \
  --module VIDEO_QCOM_CAMSS --module I2C_QCOM_CCI --module VIDEO_OV02C10 \
  --module ARM_SCMI_CPUFREQ --module SM_VIDEOCC_8550
for option in AYN AYANEO BMI260 XONE FANATEC LG4FF TMFF2 T150 RAZER S76 S76IO DRM_EVDI V4L2LOOP; do
  "$work/scripts/config" --file "$work/.config" --disable "$option"
done
make -C "$work" ARCH=arm64 olddefconfig
for option in SECURITY_APPARMOR BLK_DEV_INITRD RD_GZIP RD_ZSTD SQUASHFS_ZSTD; do
  grep -q "^CONFIG_${option}=y$" "$work/.config"
done
for option in SND_SOC_X1E80100 EC_SAMSUNG_GALAXYBOOK TYPEC_SAMSUNG_EMUEC VIDEO_QCOM_CAMSS I2C_QCOM_CCI VIDEO_OV02C10; do
  grep -q "^CONFIG_${option}=m$" "$work/.config"
done
# Headphones and microphones can be tested without energising the internal
# speaker amplifiers. A software volume cap is not smart-amp protection.
dts=arch/arm64/boot/dts/qcom/x1e84100-samsung-galaxy-book4-edge-16.dts
cat >> "$work/$dts" <<'DTS'

&{/sound} {
    status = "okay";
    audio-routing = "IN1_HPHL", "HPHL_OUT",
                    "IN2_HPHR", "HPHR_OUT",
                    "AMIC2", "MIC BIAS2",
                    "VA DMIC0", "MIC BIAS3",
                    "VA DMIC1", "MIC BIAS3",
                    "VA DMIC2", "MIC BIAS1",
                    "VA DMIC3", "MIC BIAS1",
                    "TX SWR_INPUT1", "ADC2_OUTPUT";
    /delete-node/ wsa-dai-link;
};
&swr0 { status = "disabled"; };
&swr3 { status = "disabled"; };
DTS
# Fail on the small board configuration before compiling thousands of modules.
make -C "$work" ARCH=arm64 LOCALVERSION=-9to1-book4edge16-next \
  qcom/x1e84100-samsung-galaxy-book4-edge-16.dtb
make -C "$work" ARCH=arm64 -j"$(nproc)" \
  LOCALVERSION=-9to1-book4edge16-next KDEB_PKGVERSION=7.2.7-1 bindeb-pkg
make -C "$work" ARCH=arm64 LOCALVERSION=-9to1-book4edge16-next \
  qcom/x1e84100-samsung-galaxy-book4-edge-16.dtb
test "$(cat "$work/include/config/kernel.release")" = "$release"
mkdir -p "$work/output"
cp "$work/arch/arm64/boot/Image" "$work/output/"
cp "$work/arch/arm64/boot/dts/qcom/$(basename "$dts" .dts).dtb" "$work/output/"
cp "$work/.config" "$work/output/kernel.config"
cp "$work/include/config/kernel.release" "$work/output/kernel.release"
git -C "$work" diff > "$work/output/candidate.patch"
find "$(dirname "$work")" -maxdepth 1 -name '*7.2.7*arm64.deb' -exec cp {} "$work/output/" \;
printf '%s\n' "$revision" > "$work/output/source.commit"
(cd "$work/output" && sha256sum ./* > SHA256SUMS)
