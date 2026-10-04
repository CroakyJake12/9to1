#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_PRESENT_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v cmake >/dev/null || { echo "cmake is required" >&2; exit 2; }
command -v make >/dev/null || { echo "make is required" >&2; exit 2; }
command -v pkg-config >/dev/null || { echo "pkg-config is required" >&2; exit 2; }

# Build present-engine native library
build_dir="$stage/build"
mkdir -p "$build_dir"
cd "$root/apps/present-engine"
cmake -B "$build_dir" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=/usr
cmake --build "$build_dir" -j"$(nproc)"
cmake --install "$build_dir" --prefix "$stage/usr"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/present-engine" "$stage/usr/bin" "$out"

# Copy the built worker binary
cp "$build_dir/cakeos-present-worker" "$stage/usr/lib/havenos/present-engine/"
cp -a "$root/apps/present-engine/README.md" "$stage/usr/lib/havenos/present-engine/"

cat > "$stage/usr/bin/cakeos-present-worker" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present-engine/cakeos-present-worker "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-present-worker"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-present-engine
Version: $version
Section: libs
Priority: optional
Architecture: amd64
Depends: libreoffice-impress-nogui, libstdc++6, libc6, libglib2.0-0
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Present Engine native worker (LibreOfficeKit)
 LibreOfficeKit-based document engine for Writer and Present apps.
 Provides native worker binary for LibreOffice Writer/Impress integration.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-present-engine_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"