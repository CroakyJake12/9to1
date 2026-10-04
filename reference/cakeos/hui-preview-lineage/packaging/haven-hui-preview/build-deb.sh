#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_HUI_PREVIEW_VERSION:-0.1.0}"
publish="$root/artifacts/hui-linux-host/publish"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+~.-][A-Za-z0-9.+~-]+)?$ ]] || { echo "Invalid Debian preview version: $version" >&2; exit 2; }
[[ -x "$publish/cakeos-hui-linux-preview" ]] || { echo "Build graphical HUI Linux host before packaging" >&2; exit 2; }
[[ ! -e "$publish/libcoreclrtraceptprovider.so" ]] || { echo "Optional obsolete LTTng provider must not be packaged" >&2; exit 2; }

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

mkdir -p \
  "$stage/DEBIAN" \
  "$stage/usr/lib/cakeos/hui-preview" \
  "$stage/usr/bin" \
  "$stage/usr/share/applications" \
  "$out"
cp -R "$publish/." "$stage/usr/lib/cakeos/hui-preview/"
cat > "$stage/usr/bin/cakeos-hui-preview" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/hui-preview/cakeos-hui-linux-preview "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-hui-preview"

cat > "$stage/usr/share/applications/cakeos-hui-preview.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=CakeOS HUI Preview
Comment=Preview the CakeOS HUI Linux shell surface
Exec=cakeos-hui-preview
Terminal=false
Categories=Utility;
StartupNotify=true
EOF

cat > "$stage/DEBIAN/control" <<EOF
Package: haven-hui-preview
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Depends: libc6, libgcc-s1, libstdc++6, zlib1g
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS HUI graphical Linux preview
 Self-contained graphical preview package for validating the pinned HUI core on Linux.
 This package is not a desktop session and does not replace GNOME Shell or Mutter.
EOF

# Normalize filesystem timestamps so repeated package builds from the same
# source commit and publish payload produce byte-identical Debian archives.
find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +

package="$out/haven-hui-preview_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"
echo "Created $package from SOURCE_DATE_EPOCH=$SOURCE_DATE_EPOCH"
