#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_PRESENT_APP_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }

# Build Present .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Present/App/HavenOS.Present.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Present Hui .NET app
hui_publish_dir="$stage/publish-hui"
mkdir -p "$hui_publish_dir"
dotnet publish "$root/apps/Present/Hui/HavenOS.Present.Hui.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$hui_publish_dir"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/present" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/present/"
cp -R "$hui_publish_dir/." "$stage/usr/lib/havenos/present/"

cat > "$stage/usr/bin/haven-present" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present/HavenOS.Present.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-present"

cat > "$stage/usr/bin/haven-present-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/present/HavenOS.Present.Hui "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-present-hui"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-present
Version: $version
Section: office
Priority: optional
Architecture: amd64
Depends: havenos-present-engine, libreoffice-impress-nogui, libc6, libstdc++6, libgcc-s1
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Present application (LibreOffice Impress + LibreOfficeKit)
 Present application using LibreOfficeKit for presentation editing.
 Includes both the core engine and HUI frontend.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-present_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"