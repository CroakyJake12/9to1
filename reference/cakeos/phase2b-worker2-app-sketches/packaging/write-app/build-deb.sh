#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_WRITE_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }

# Build Write .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Write/App/HavenOS.Write.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Write Hui .NET app
hui_publish_dir="$stage/publish-hui"
mkdir -p "$hui_publish_dir"
dotnet publish "$root/apps/Write/Hui/HavenOS.Write.Hui.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$hui_publish_dir"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/write" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/write/"
cp -R "$hui_publish_dir/." "$stage/usr/lib/havenos/write/"

cat > "$stage/usr/bin/haven-write" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/write/HavenOS.Write.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-write"

cat > "$stage/usr/bin/haven-write-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/write/HavenOS.Write.Hui "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-write-hui"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-write
Version: $version
Section: office
Priority: optional
Architecture: amd64
Depends: havenos-present-engine, libreoffice-writer-nogui, libc6, libstdc++6, libgcc-s1
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Write application (LibreOffice Writer + LibreOfficeKit)
 Writer application using LibreOfficeKit for document editing.
 Includes both the core engine and HUI frontend.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-write_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"