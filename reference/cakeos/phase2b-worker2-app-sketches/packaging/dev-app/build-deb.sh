#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_DEV_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }

# Build Dev .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Dev/App/HavenOS.Dev.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Dev Hui .NET app
hui_publish_dir="$stage/publish-hui"
mkdir -p "$hui_publish_dir"
dotnet publish "$root/apps/Dev/Hui/HavenOS.Dev.Hui.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$hui_publish_dir"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/dev" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/dev/"
cp -R "$hui_publish_dir/." "$stage/usr/lib/havenos/dev/"

cat > "$stage/usr/bin/haven-dev" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/dev/HavenOS.Dev.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-dev"

cat > "$stage/usr/bin/haven-dev-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/dev/HavenOS.Dev.Hui "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-dev-hui"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-dev
Version: $version
Section: devel
Priority: optional
Architecture: amd64
Depends: libc6, libstdc++6, libgcc-s1, codium
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Dev application (VSCodium/Code-OSS integration)
 Development environment integration with VSCodium/Code-OSS.
 Includes extension APIs, IPC, commands, shared workspace, and CakeUI shell.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-dev_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"