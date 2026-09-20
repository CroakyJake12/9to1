#!/usr/bin/env bash
set -euo pipefail

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
OUT=${1:-"$ROOT/artifacts/cohort"}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

for deb in "$OUT"/*.deb; do
  name=$(dpkg-deb --field "$deb" Package)
  root="$TMP/$name"
  mkdir -p "$root"
  dpkg-deb --extract "$deb" "$root"
  case "$name" in
    havenos-data) test -x "$root/usr/bin/haven-data-calc-worker"; python3 -m py_compile "$root/usr/lib/havenos/data/workers/calc_worker.py" ;;
    havenos-present) test -x "$root/usr/bin/cakeos-present-worker"; test -x "$root/usr/lib/havenos/present-engine/cakeos-present-worker"; test -f "$root/usr/lib/havenos/present-engine/README.md" ;;
    havenos-wine-compat) test -f "$root/usr/lib/systemd/user/haven-compatd.service"; ! grep -Eq '@(PYTHON|SOURCE_ROOT|DOC_ROOT)@' "$root/usr/lib/systemd/user/haven-compatd.service"; python3 -m compileall -q "$root/usr/lib/havenos/compatibility/wine/haven_compat" ;;
  esac
done
sha256sum --check "$OUT/SHA256SUMS"
echo "HavenOS cohort package extraction and entrypoint smokes passed."
