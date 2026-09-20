#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_CANVAS_RNOTE_VERSION:-0.1.0}"
publish="${CAKEOS_CANVAS_RNOTE_PUBLISH:-$root/artifacts/canvas-rnote/publish}"
deb_arch="${CAKEOS_DEB_ARCH:-amd64}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v cargo >/dev/null || { echo "cargo is required for RNote build" >&2; exit 2; }

# Build RNote from source if publish dir doesn't exist
if [[ ! -x "$publish/cakeos-canvas-rnote" ]]; then
  echo "Building RNote from source..."
  cd "$root/../9to1 Workspace/Canvas/rnote-poc"
  cargo build --release
  mkdir -p "$publish"
  cp "target/release/cakeos-canvas-rnote" "$publish/"
fi

[[ -x "$publish/cakeos-canvas-rnote" ]] || { echo "Expected Linux Canvas/Rnote host at $publish/cakeos-canvas-rnote" >&2; exit 2; }

# Stage the Rnote native shared library alongside the managed host so
# DllImport("cakeos_canvas_rnote_poc") resolves at runtime.
echo "Building Canvas Rnote native library from source..."
cargo build --release --manifest-path "$root/../9to1 Workspace/Canvas/rnote-poc/Cargo.toml"
native_lib="$root/../9to1 Workspace/Canvas/rnote-poc/target/release/libcakeos_canvas_rnote_poc.so"
[[ -f "$native_lib" ]] || { echo "Expected Rnote native library at $native_lib" >&2; exit 2; }
cp "$native_lib" "$publish/"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/canvas-rnote" "$stage/usr/bin" "$out"
cp -R "$publish/." "$stage/usr/lib/cakeos/canvas-rnote/"

cat > "$stage/usr/bin/cakeos-canvas-rnote" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/canvas-rnote/cakeos-canvas-rnote "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-canvas-rnote"

cat > "$stage/DEBIAN/control" <<EOF
Package: cakeos-canvas-rnote
Version: $version
Section: graphics
Priority: optional
Architecture: $deb_arch
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libgtk-4-1, libadwaita-1-0
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS Canvas/Rnote Linux component
  Renderer-neutral Canvas/Rnote component using the pinned Rnote core.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/cakeos-canvas-rnote_${version}_${deb_arch}.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"