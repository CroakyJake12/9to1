#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_BOARDS_VERSION:-0.1.0}"
publish="${CAKEOS_BOARDS_PUBLISH:-$root/artifacts/boards/publish}"
deb_arch="${CAKEOS_DEB_ARCH:-amd64}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }

# The authoritative branch contains libraries and a Flutter proof harness, not
# a launchable Boards process. A builder must provide that process explicitly.
[[ -x "$publish/cakeos-boards" ]] || { echo "Expected Linux Boards host at $publish/cakeos-boards; refusing to package libraries or proof harness" >&2; exit 2; }

command -v cargo >/dev/null || { echo "cargo is required for RNote build" >&2; exit 2; }

# Stage the shared Rnote native library at the publish root so
# DllImport("cakeos_canvas_rnote_poc") resolves at runtime.
echo "Building Boards shared Rnote native library from source..."
cargo build --release --manifest-path "$root/../9to1 Workspace/Canvas/rnote-poc/Cargo.toml"
native_lib="$root/../9to1 Workspace/Canvas/rnote-poc/target/release/libcakeos_canvas_rnote_poc.so"
[[ -f "$native_lib" ]] || { echo "Expected Rnote native library at $native_lib" >&2; exit 2; }
cp "$native_lib" "$publish/"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/boards" "$stage/usr/bin" "$out"
cp -R "$publish/." "$stage/usr/lib/cakeos/boards/"

cat > "$stage/usr/bin/cakeos-boards" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/boards/cakeos-boards "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-boards"

cat > "$stage/DEBIAN/control" <<EOF
Package: cakeos-boards
Version: $version
Section: office
Priority: optional
Architecture: $deb_arch
Depends: libc6, libgcc-s1, libstdc++6, zlib1g
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS Boards Linux component
  HUI Boards component with AppFlowy integration at its isolated proof boundary.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/cakeos-boards_${version}_${deb_arch}.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"