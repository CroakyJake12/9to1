#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_WAVE_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }
command -v cmake >/dev/null || { echo "cmake is required" >&2; exit 2; }
command -v make >/dev/null || { echo "make is required" >&2; exit 2; }
command -v pkg-config >/dev/null || { echo "pkg-config is required" >&2; exit 2; }

# Build Wave .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Wave/App/HavenOS.Wave.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Wave Hui .NET app (if exists)
hui_csproj="$root/apps/Wave/Hui/HavenOS.Wave.Hui.csproj"
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
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/wave" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/wave/"
if [[ -d "$stage/publish-hui" ]]; then
  cp -R "$stage/publish-hui/." "$stage/usr/lib/havenos/wave/"
fi

cat > "$stage/usr/bin/haven-wave" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wave/HavenOS.Wave.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-wave"

if [[ -f "$stage/publish-hui/HavenOS.Wave.Hui" ]]; then
  cat > "$stage/usr/bin/haven-wave-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/wave/HavenOS.Wave.Hui "$@"
EOF
  chmod 0755 "$stage/usr/bin/haven-wave-hui"
fi

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-wave
Version: $version
Section: sound
Priority: optional
Architecture: amd64
Depends: libc6, libstdc++6, libgcc-s1, gstreamer1.0-plugins-base, gstreamer1.0-plugins-good, gstreamer1.0-plugins-bad, gstreamer1.0-libav, libges-1.0-0
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Wave application (GStreamer + GES)
 Audio/video editing application using GStreamer and GStreamer Editing Services.
 Includes both the core engine and HUI frontend.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-wave_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"