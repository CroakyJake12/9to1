#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_BOARDS_VERSION:-0.1.0}"
publish="$root/artifacts/hui-linux-host/publish"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+~.-][A-Za-z0-9.+~-]+)?$ ]] || { echo "Invalid Debian version: $version" >&2; exit 2; }
[[ -x "$publish/cakeos-hui-linux-preview" ]] || { echo "Build the HUI Linux host before packaging" >&2; exit 2; }
[[ -f "$publish/Boards.dll" ]] || { echo "Build the Boards root provider before packaging" >&2; exit 2; }

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/boards" "$stage/usr/bin" "$stage/usr/share/applications" "$out"
cp -R "$publish/." "$stage/usr/lib/cakeos/boards/"
cat > "$stage/usr/bin/cakeos-boards" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/boards/cakeos-hui-linux-preview \
  --hui-root-provider-assembly /usr/lib/cakeos/boards/Boards.dll \
  --hui-root-provider-type Haven.Applications.Boards.BoardsRootProvider "$@"
EOF
chmod 0755 "$stage/usr/bin/cakeos-boards"
cat > "$stage/usr/share/applications/cakeos-boards.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=CakeOS Boards
Comment=Local-first Haven Boards through the CakeOS HUI Linux host
Exec=cakeos-boards
Terminal=false
Categories=Office;ProjectManagement;
EOF

cat > "$stage/DEBIAN/control" <<EOF
Package: haven-boards
Version: $version
Section: office
Priority: optional
Architecture: amd64
Depends: libc6, libgcc-s1, libstdc++6, zlib1g
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS Boards HUI Linux application
 Local-first Haven Boards using the generic CakeOS HUI Linux host.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/haven-boards_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"
