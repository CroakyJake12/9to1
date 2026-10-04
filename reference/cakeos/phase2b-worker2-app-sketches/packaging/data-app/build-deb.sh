#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_DATA_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
command -v dotnet >/dev/null || { echo "dotnet is required" >&2; exit 2; }
command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 2; }

# Build Data .NET app
publish_dir="$stage/publish"
mkdir -p "$publish_dir"
dotnet publish "$root/apps/Data/App/HavenOS.Data.App.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$publish_dir"

# Build Data Hui .NET app
hui_publish_dir="$stage/publish-hui"
mkdir -p "$hui_publish_dir"
dotnet publish "$root/apps/Data/Hui/HavenOS.Data.Hui.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:ContinuousIntegrationBuild=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$hui_publish_dir"

# Copy worker scripts
mkdir -p "$stage/usr/lib/havenos/data/workers"
cp "$root/apps/Data/workers/calc_worker.py" "$stage/usr/lib/havenos/data/workers/"
cp "$root/apps/Data/workers/duckdb_worker.py" "$stage/usr/lib/havenos/data/workers/"

# Create package structure
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/data" "$stage/usr/bin" "$out"

# Copy published files
cp -R "$publish_dir/." "$stage/usr/lib/havenos/data/"
cp -R "$hui_publish_dir/." "$stage/usr/lib/havenos/data/"

cat > "$stage/usr/bin/haven-data" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/data/HavenOS.Data.App "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-data"

cat > "$stage/usr/bin/haven-data-hui" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/data/HavenOS.Data.Hui "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-data-hui"

cat > "$stage/usr/bin/haven-data-calc-worker" <<'EOF'
#!/bin/sh
set -eu
exec python3 /usr/lib/havenos/data/workers/calc_worker.py "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-data-calc-worker"

cat > "$stage/usr/bin/haven-data-duckdb-worker" <<'EOF'
#!/bin/sh
set -eu
exec python3 /usr/lib/havenos/data/workers/duckdb_worker.py "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-data-duckdb-worker"

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-data
Version: $version
Section: office
Priority: optional
Architecture: amd64
Depends: libc6, libstdc++6, libgcc-s1, python3, python3-duckdb, python3-uno, libreoffice-calc-nogui
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS Data application (Calc + DuckDB)
 Spreadsheet and database application using LibreOffice Calc and DuckDB.
 Includes both the core engine, HUI frontend, and Python workers.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-data_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"