#!/usr/bin/env bash
# Static Candidate ISO validation (handover section 28).
# Usage: validate-iso-static.sh <iso-path> <report-md>
# Every check is explicit; nothing is silently skipped.
# Exit nonzero if any check FAILs.
set -euo pipefail

ISO="${1:?ISO path required}"
REPORT="${2:?report path required}"
WORK="$(dirname "$REPORT")"
LISTING="$WORK/iso-listing.txt"
LIVELIST="$WORK/live-layer-listing.txt"
GRUB="$WORK/grub.cfg"

pass=0
fail=0
declare -a rows

check() { # check <name> <command...>
  local name="$1"; shift
  if "$@" >/dev/null 2>&1; then
    rows+=("| $name | PASS | |")
    pass=$((pass + 1))
  else
    rows+=("| $name | FAIL | see log |")
    fail=$((fail + 1))
    echo "CHECK-FAIL: $name" >&2
  fi
}

check_msg() { # check_msg <name> <expected:PASS|FAIL> <detail>
  if [ "$2" = "PASS" ]; then
    rows+=("| $1 | PASS | $3 |")
    pass=$((pass + 1))
  else
    rows+=("| $1 | FAIL | $3 |")
    fail=$((fail + 1))
    echo "CHECK-FAIL: $1 ($3)" >&2
  fi
}

{
  echo "# Book4Edge16 static ISO validation"
  echo
  echo "- ISO: \`$ISO\`"
  echo "- Date (UTC): $(date -u +%FT%TZ)"
} > "$REPORT"

check "ISO file readable" test -r "$ISO"
SIZE=$(stat -c%s "$ISO" 2>/dev/null || stat -f%z "$ISO")
[ "$SIZE" -gt 1000000000 ] && check_msg "ISO size > 1GiB ($SIZE bytes)" PASS "" \
  || check_msg "ISO size > 1GiB ($SIZE bytes)" FAIL "too small"
SHA=$(sha256sum "$ISO" | awk '{print $1}')
echo "- SHA256: \`$SHA\`" >> "$REPORT"
echo "$SHA  $(basename "$ISO")" > "$WORK/iso.sha256"

xorriso -indev "$ISO" -find / -maxdepth 4 2>/dev/null | sort > "$LISTING" || \
  xorriso -osirrox on -indev "$ISO" -find / 2>/dev/null | sort > "$LISTING" || true
check "ISO listing produced" test -s "$LISTING"

LOWER="$WORK/iso-listing-lower.txt"
tr '[:upper:]' '[:lower:]' < "$LISTING" > "$LOWER"

check "ARM64 EFI loader present (BOOTAA64.EFI)" grep -q 'boot/bootaa64\.efi' "$LOWER"
check "GRUB EFI module dir present" grep -q 'boot/grub' "$LOWER"

xorriso -osirrox on -indev "$ISO" -extract /boot/grub/grub.cfg "$GRUB" 2>/dev/null || true
check "GRUB config extractable" test -s "$GRUB"
if [ -s "$GRUB" ]; then
  check "GRUB has CakeOS Emergency entry" grep -q 'CakeOS Emergency' "$GRUB"
  check "GRUB linux line references /casper/vmlinuz" grep -q 'linux /casper/vmlinuz' "$GRUB"
  check "GRUB initrd line references /casper/initrd" grep -q 'initrd /casper/initrd' "$GRUB"
fi

check "kernel /casper/vmlinuz in ISO" grep -q 'casper/vmlinuz' "$LOWER"
check "initrd /casper/initrd in ISO" grep -q 'casper/initrd' "$LOWER"

DTB_PATH="casper/dtbs/qcom/x1e80100-samsung-galaxy-book4-edge.dtb"
if grep -q "$DTB_PATH" "$LOWER"; then
  check_msg "Book4 Edge DTB exact path" PASS ""
else
  check_msg "Book4 Edge DTB exact path" FAIL "missing $DTB_PATH"
fi
# No x1e84100 naming confusion anywhere in the image listing.
if grep -q 'x1e84100' "$LOWER"; then
  check_msg "no x1e84100 boot references" FAIL "$(grep -c 'x1e84100' "$LOWER") hits"
else
  check_msg "no x1e84100 boot references" PASS ""
fi

check "layered Casper base (minimal.squashfs)" grep -q 'casper/minimal\.squashfs' "$LOWER"
check "layered Casper middle (minimal.standard.squashfs)" grep -q 'casper/minimal\.standard\.squashfs' "$LOWER"
check "modified CakeOS live layer (minimal.standard.live.squashfs)" grep -q 'casper/minimal\.standard\.live\.squashfs' "$LOWER"
check "md5sum.txt present" grep -q 'md5sum\.txt' "$LOWER"

# Extract the CakeOS live layer and list (not fully unpack) for content proofs.
LIVE_SQ="$WORK/live.squashfs"
xorriso -osirrox on -indev "$ISO" -extract /casper/minimal.standard.live.squashfs "$LIVE_SQ" 2>/dev/null || true
if [ -s "$LIVE_SQ" ]; then
  check_msg "live layer extractable" PASS ""
  unsquashfs -l "$LIVE_SQ" > "$LIVELIST" 2>/dev/null || true
  LL="$WORK/live-layer-listing-lower.txt"
  tr '[:upper:]' '[:lower:]' < "$LIVELIST" > "$LL"
  check "cakeos-diagnostics in live layer" grep -q 'cakeos-diagnostics' "$LL"
  check "Canvas Rnote native lib in live layer" grep -q 'libcakeos_canvas' "$LL"
  check "Boards package content in live layer" grep -q 'boards' "$LL"
  check "rescue SSH service in live layer" grep -q 'cakeos-rescue-net' "$LL"
  check "evtest in live layer" grep -q 'evtest' "$LL"
  check "libinput tooling in live layer" grep -q 'libinput' "$LL"
  check "v4l/camera tooling in live layer" grep -q 'v4l' "$LL"
  check "ssh server in live layer" grep -q 'usr/sbin/sshd' "$LL"
else
  check_msg "live layer extractable" FAIL "extraction produced nothing"
fi

{
  echo
  echo "## Results"
  echo
  echo "| check | result | detail |"
  echo "|---|---|---|"
  printf '%s\n' "${rows[@]}"
  echo
  echo "- PASS: $pass, FAIL: $fail"
  if [ "$fail" -eq 0 ]; then
    echo "- VERDICT: STATIC-VALIDATION-GREEN (static only; not hardware proof)"
  else
    echo "- VERDICT: STATIC-VALIDATION-RED"
  fi
} >> "$REPORT"

cat "$REPORT"
[ "$fail" -eq 0 ]
