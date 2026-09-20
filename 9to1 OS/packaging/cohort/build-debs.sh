#!/usr/bin/env bash
set -euo pipefail

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd -P)
OUT=${1:-"$ROOT/artifacts/cohort"}
VERSION=${VERSION:-0.1.0}
ARCH=${ARCH:-amd64}
mkdir -p "$OUT"
rm -rf "$OUT/work"
mkdir -p "$OUT/work"

require() { command -v "$1" >/dev/null 2>&1 || { echo "missing command: $1" >&2; exit 1; }; }
require dpkg-deb
require sha256sum

write_control() {
  local dir=$1 name=$2 depends=$3 description=$4
  mkdir -p "$dir/DEBIAN"
  cat > "$dir/DEBIAN/control" <<EOF
Package: $name
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: $depends
Maintainer: HavenOS
Description: $description
EOF
}

build_data() {
  local dir="$OUT/work/havenos-data"
  mkdir -p "$dir/usr/lib/havenos/data" "$dir/usr/bin"
  cp -a "$ROOT/apps/Data/." "$dir/usr/lib/havenos/data/"
  cat > "$dir/usr/bin/haven-data-calc-worker" <<'EOF'
#!/bin/sh
set -eu
exec python3 /usr/lib/havenos/data/workers/calc_worker.py "$@"
EOF
  chmod 0755 "$dir/usr/bin/haven-data-calc-worker"
  write_control "$dir" havenos-data "python3, python3-uno, libreoffice-calc-nogui" \
    "HavenOS Data Calc and DuckDB worker sources"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-data_${VERSION}_${ARCH}.deb" >/dev/null
}

build_present() {
  local dir="$OUT/work/havenos-present"
  mkdir -p "$dir/usr/lib/havenos/present-engine" "$dir/usr/bin"
  test -n "${PRESENT_BUILD_DIR:-}" || { echo "PRESENT_BUILD_DIR is required" >&2; exit 1; }
  test -x "$PRESENT_BUILD_DIR/cakeos-present-worker" || { echo "Present worker binary is missing" >&2; exit 1; }
  cp "$PRESENT_BUILD_DIR/cakeos-present-worker" "$dir/usr/lib/havenos/present-engine/"
  cp -a "$ROOT/apps/present-engine/README.md" "$dir/usr/lib/havenos/present-engine/"
  cat > "$dir/usr/bin/cakeos-present-worker" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present-engine/cakeos-present-worker "$@"
EOF
  chmod 0755 "$dir/usr/bin/cakeos-present-worker"
  write_control "$dir" havenos-present "libreoffice-impress-nogui, libstdc++6" \
    "HavenOS Present worker source and protocol entrypoint"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-present_${VERSION}_${ARCH}.deb" >/dev/null
}

build_wine() {
  local dir="$OUT/work/havenos-wine-compat"
  mkdir -p "$dir/usr/lib/havenos/compatibility" "$dir/usr/lib/systemd/user"
  cp -a "$ROOT/compatibility/wine" "$dir/usr/lib/havenos/compatibility/"
  sed -e 's|@PYTHON@|/usr/bin/python3|g' \
      -e 's|@SOURCE_ROOT@|/usr/lib/havenos/compatibility|g' \
      -e 's|@DOC_ROOT@|/usr/share/doc/havenos-wine-compat|g' \
      "$ROOT/compatibility/wine/systemd/haven-compatd.service.in" \
      > "$dir/usr/lib/systemd/user/haven-compatd.service"
  write_control "$dir" havenos-wine-compat "python3, bubblewrap, systemd" \
    "HavenOS per-user Wine compatibility broker"
  dpkg-deb --build --root-owner-group "$dir" "$OUT/havenos-wine-compat_${VERSION}_${ARCH}.deb" >/dev/null
}

build_data
build_present
build_wine

for deb in "$OUT"/*.deb; do
  sha256sum "$deb"
done > "$OUT/SHA256SUMS"
: > "$OUT/package-control.txt"
for deb in "$OUT"/*.deb; do
  dpkg-deb --info "$deb" >> "$OUT/package-control.txt"
done
cat > "$OUT/cohort.json" <<EOF
{
  "version": "$VERSION",
  "architecture": "$ARCH",
  "packages": [
    {"name": "havenos-data", "artifact": "$(basename "$OUT/havenos-data_${VERSION}_${ARCH}.deb")", "depends": ["python3", "python3-uno", "libreoffice-calc-nogui"]},
    {"name": "havenos-present", "artifact": "$(basename "$OUT/havenos-present_${VERSION}_${ARCH}.deb")", "depends": ["libreoffice-impress-nogui", "libstdc++6"]},
    {"name": "havenos-wine-compat", "artifact": "$(basename "$OUT/havenos-wine-compat_${VERSION}_${ARCH}.deb")", "depends": ["python3", "bubblewrap", "systemd"]}
  ]
}
EOF
