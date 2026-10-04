#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_IMAGES_VERSION:-0.1.0}"
backend_publish="${CAKEOS_IMAGES_BACKEND_PUBLISH:-$root/artifacts/images-backend/publish}"
frontend_publish="${CAKEOS_IMAGES_FRONTEND_PUBLISH:-$root/artifacts/images-frontend/publish}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([+~.-][A-Za-z0-9.+~-]+)?$ ]] ||
  { echo "Invalid Debian version: $version" >&2; exit 2; }

# Build backend package
if [[ -f "$backend_publish/libcakeos_images_backend.so" ]]; then
    mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/images-backend" "$out"
    cp "$backend_publish/libcakeos_images_backend.so" "$stage/usr/lib/cakeos/images-backend/"
    
    cat > "$stage/DEBIAN/control" <<EOF
Package: cakeos-images-backend
Version: $version
Section: graphics
Priority: optional
Architecture: amd64
Depends: libc6, libgcc-s1, libstdc++6, libglib2.0-0, libcairo2, liblcms2-2, zlib1g
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS Images glycin backend
 Glycin-based image decoding, rendering, metadata, and color management backend.
EOF

    source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
    [[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
    export SOURCE_DATE_EPOCH="$source_date_epoch"

    find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
    package="$out/cakeos-images-backend_${version}_amd64.deb"
    dpkg-deb --build --root-owner-group "$stage" "$package"
    dpkg-deb --info "$package"
    sha256sum "$package" | tee "$package.sha256"
    rm -rf "$stage"/*
fi

# Build frontend package
if [[ -x "$frontend_publish/cakeos-images" ]]; then
    mkdir -p "$stage/DEBIAN" "$stage/usr/lib/cakeos/images" "$stage/usr/bin" "$out"
    cp -R "$frontend_publish/." "$stage/usr/lib/cakeos/images/"
    
    cat > "$stage/usr/bin/cakeos-images" <<'EOF'
#!/bin/sh
exec /usr/lib/cakeos/images/cakeos-images "$@"
EOF
    chmod 0755 "$stage/usr/bin/cakeos-images"

    cat > "$stage/DEBIAN/control" <<EOF
Package: cakeos-images
Version: $version
Section: graphics
Priority: optional
Architecture: amd64
Depends: cakeos-images-backend (>= $version), libc6, libgcc-s1, libstdc++6, libglib2.0-0, libcairo2, libskia-sharp
Maintainer: CakeOS Platform <noreply@cakeos.local>
Description: CakeOS Images frontend
 CakeUI-native image viewer with AI Vision integration, annotations, and Windows interop.
EOF

    source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
    [[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
    export SOURCE_DATE_EPOCH="$source_date_epoch"

    find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
    package="$out/cakeos-images_${version}_amd64.deb"
    dpkg-deb --build --root-owner-group "$stage" "$package"
    dpkg-deb --info "$package"
    sha256sum "$package" | tee "$package.sha256"
fi