#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_WINE_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }

# Build Wine .NET app (if exists)
app_csproj="$root/apps/Wine/App/HavenOS.Wine.App.csproj"
if [[ -f "$app_csproj" ]]; then
  publish_dir="$stage/publish"
  mkdir -p "$publish_dir"
  dotnet publish "$app_csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:ContinuousIntegrationBuild=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$publish_dir"
fi

# Build Wine Hui .NET app
hui_csproj="$root/apps/Wine/Hui/HavenOS.Wine.Hui.csproj"
if [[ -f "$hui_csproj" ]]; then
  hui_publish_dir="$stage/publish-hui"
  mkdir -p "$hui_publish_dir"
  dotnet publish "$hui_csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:ContinuousIntegrationBuild=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$hui_publish_dir"
fi

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/wine" "$stage/usr/bin" "$out"

# Copy published files
if [[ -d "$stage/publish" ]]; then
  cp -R "$stage/publish/." "$stage/usr/lib/havenos/wine/"
fi
if [[ -d "$stage/publish-hui" ]]; then
  cp -R "$stage/publish-hui/." "$stage/usr/lib/havenos/wine/"
fi

if [[ -f "$stage/publish/HavenOS.Wine.App" ]]; then
  cat > "$stage/usr/bin/haven-wine" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wine/HavenOS.Wine.App "$@"
EOF
  chmod 0755 "$stage/usr/bin/haven-wine"
fi

if [[ -f "$stage/publish-hui/HavenOS.Wine.Hui" ]]; then
  cat > "$stage/usr/bin/haven-wine-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wine/HavenOS.Wine.Hui "$@"
EOF
  chmod 0755 "$stage/usr/bin/haven-wine-hui"
fi

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-wine
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Depends: wine64, wine32:i386, libc6, libstdc++6, libgcc-s1
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Wine integration
 Wine integration for running Windows applications on HavenOS.
 Includes HUI frontend for Wine management.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-wine_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"