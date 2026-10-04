#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_PLAN_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }
command -v cmake >/dev/null || { echo "cmake is required" >&2; exit 2; }
command -v make >/dev/null || { echo "make is required" >&2; exit 2; }
command -v pkg-config >/dev/null || { echo "pkg-config is required" >&2; exit 2; }

# Build plan-engine native library
build_dir="$stage/build-plan-engine"
mkdir -p "$build_dir"
cd "$root/apps/Plan"
# Note: Plan engine native code would be in a separate directory
# For now, we'll build the .NET app which uses P/Invoke to libical/EDS

# Build Plan .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Plan/App/HavenOS.Plan.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Plan Hui .NET app
hui_publish_dir="$stage/publish-hui"
mkdir -p "$hui_publish_dir"
dotnet publish "$root/apps/Plan/Hui/HavenOS.Plan.Hui.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$hui_publish_dir"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/plan" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/plan/"
cp -R "$hui_publish_dir/." "$stage/usr/lib/havenos/plan/"

cat > "$stage/usr/bin/haven-plan" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/plan/HavenOS.Plan.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-plan"

cat > "$stage/usr/bin/haven-plan-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/plan/HavenOS.Plan.Hui "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-plan-hui"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-plan
Version: $version
Section: office
Priority: optional
Architecture: amd64
Depends: libc6, libstdc++6, libgcc-s1, libical3, libecal-2.0-1, libedataserver-1.2-26
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Plan application (Evolution Data Server + libical)
 Calendar and tasks application using Evolution Data Server and libical.
 Includes both the core engine and HUI frontend.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-plan_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"