#!/usr/bin/env bash
set -euo pipefail

# Master build script for all HavenOS Phase 2B apps
# Run this on the Linux builder (Ubuntu 26.04) with all build dependencies installed

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
VERSION=${VERSION:-0.1.0}
ARCH=${ARCH:-amd64}

echo "=== HavenOS Phase 2B App Build ==="
echo "Root: $ROOT"
echo "Version: $VERSION"
echo "Architecture: $ARCH"

# Check required commands
for cmd in dotnet cmake make pkg-config dpkg-deb git python3 cargo; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "Missing required command: $cmd" >&2; exit 1; }
done

# Build present-engine (native, used by Write and Present)
echo "=== Building present-engine ==="
PRESENT_BUILD_DIR="$ROOT/artifacts/present-engine/build"
mkdir -p "$PRESENT_BUILD_DIR"
cd "$ROOT/apps/present-engine"
cmake -B "$PRESENT_BUILD_DIR" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=/usr
cmake --build "$PRESENT_BUILD_DIR" -j"$(nproc)"
test -x "$PRESENT_BUILD_DIR/cakeos-present-worker" || { echo "present-engine build failed" >&2; exit 1; }
echo "present-engine built at $PRESENT_BUILD_DIR"

# Build all .NET apps
echo "=== Building .NET apps ==="

# Write app
echo "--- Building Write app ---"
WRITE_BUILD_DIR="$ROOT/artifacts/write/publish"
mkdir -p "$WRITE_BUILD_DIR"
dotnet publish "$ROOT/apps/Write/App/HavenOS.Write.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$WRITE_BUILD_DIR"
dotnet publish "$ROOT/apps/Write/Hui/HavenOS.Write.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$WRITE_BUILD_DIR"
test -x "$WRITE_BUILD_DIR/HavenOS.Write.App" || { echo "Write app build failed" >&2; exit 1; }

# Present app
echo "--- Building Present app ---"
PRESENT_APP_BUILD_DIR="$ROOT/artifacts/present-app/publish"
mkdir -p "$PRESENT_APP_BUILD_DIR"
dotnet publish "$ROOT/apps/Present/App/HavenOS.Present.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$PRESENT_APP_BUILD_DIR"
dotnet publish "$ROOT/apps/Present/Hui/HavenOS.Present.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$PRESENT_APP_BUILD_DIR"
test -x "$PRESENT_APP_BUILD_DIR/HavenOS.Present.App" || { echo "Present app build failed" >&2; exit 1; }

# Plan app
echo "--- Building Plan app ---"
PLAN_BUILD_DIR="$ROOT/artifacts/plan/publish"
mkdir -p "$PLAN_BUILD_DIR"
dotnet publish "$ROOT/apps/Plan/App/HavenOS.Plan.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$PLAN_BUILD_DIR"
dotnet publish "$ROOT/apps/Plan/Hui/HavenOS.Plan.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$PLAN_BUILD_DIR"
test -x "$PLAN_BUILD_DIR/HavenOS.Plan.App" || { echo "Plan app build failed" >&2; exit 1; }

# Terminal app
echo "--- Building Terminal app ---"
TERMINAL_BUILD_DIR="$ROOT/artifacts/terminal/publish"
mkdir -p "$TERMINAL_BUILD_DIR"
dotnet publish "$ROOT/apps/Terminal/App/HavenOS.Terminal.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$TERMINAL_BUILD_DIR"
dotnet publish "$ROOT/apps/Terminal/Hui/HavenOS.Terminal.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$TERMINAL_BUILD_DIR"
test -x "$TERMINAL_BUILD_DIR/HavenOS.Terminal.App" || { echo "Terminal app build failed" >&2; exit 1; }

# Dev app
echo "--- Building Dev app ---"
DEV_BUILD_DIR="$ROOT/artifacts/dev/publish"
mkdir -p "$DEV_BUILD_DIR"
dotnet publish "$ROOT/apps/Dev/App/HavenOS.Dev.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$DEV_BUILD_DIR"
dotnet publish "$ROOT/apps/Dev/Hui/HavenOS.Dev.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$DEV_BUILD_DIR"
test -x "$DEV_BUILD_DIR/HavenOS.Dev.App" || { echo "Dev app build failed" >&2; exit 1; }

# Wave app
echo "--- Building Wave app ---"
WAVE_BUILD_DIR="$ROOT/artifacts/wave/publish"
mkdir -p "$WAVE_BUILD_DIR"
dotnet publish "$ROOT/apps/Wave/App/HavenOS.Wave.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$WAVE_BUILD_DIR"
if [[ -f "$ROOT/apps/Wave/Hui/HavenOS.Wave.Hui.csproj" ]]; then
  dotnet publish "$ROOT/apps/Wave/Hui/HavenOS.Wave.Hui.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$WAVE_BUILD_DIR"
fi
test -x "$WAVE_BUILD_DIR/HavenOS.Wave.App" || { echo "Wave app build failed" >&2; exit 1; }

# Wine app
echo "--- Building Wine app ---"
WINE_BUILD_DIR="$ROOT/artifacts/wine/publish"
mkdir -p "$WINE_BUILD_DIR"
if [[ -f "$ROOT/apps/Wine/App/HavenOS.Wine.App.csproj" ]]; then
  dotnet publish "$ROOT/apps/Wine/App/HavenOS.Wine.App.csproj" \
    -c Release -r linux-x64 --self-contained true \
    -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$WINE_BUILD_DIR"
fi
dotnet publish "$ROOT/apps/Wine/Hui/HavenOS.Wine.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$WINE_BUILD_DIR"
# Wine app might not have App binary, that's OK

# Build Data app
echo "--- Building Data app ---"
DATA_BUILD_DIR="$ROOT/artifacts/data/publish"
mkdir -p "$DATA_BUILD_DIR"
dotnet publish "$ROOT/apps/Data/App/HavenOS.Data.App.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$DATA_BUILD_DIR"
dotnet publish "$ROOT/apps/Data/Hui/HavenOS.Data.Hui.csproj" \
  -c Release -r linux-x64 --self-contained true \
  -p:ContinuousIntegrationBuild=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$DATA_BUILD_DIR"
test -x "$DATA_BUILD_DIR/HavenOS.Data.App" || { echo "Data app build failed" >&2; exit 1; }

# Build Canvas/RNote (Rust)
echo "=== Building Canvas/RNote ==="
cd "$ROOT/apps/canvas/rnote-poc"
cargo build --release
CANVAS_PUBLISH_DIR="$ROOT/artifacts/canvas-rnote/publish"
mkdir -p "$CANVAS_PUBLISH_DIR"
cp "target/release/cakeos-canvas-rnote" "$CANVAS_PUBLISH_DIR/"

echo "=== All apps built successfully ==="
echo "Build artifacts:"
echo "  PRESENT_BUILD_DIR=$PRESENT_BUILD_DIR"
echo "  WRITE_BUILD_DIR=$WRITE_BUILD_DIR"
echo "  PRESENT_APP_BUILD_DIR=$PRESENT_APP_BUILD_DIR"
echo "  PLAN_BUILD_DIR=$PLAN_BUILD_DIR"
echo "  TERMINAL_BUILD_DIR=$TERMINAL_BUILD_DIR"
echo "  DEV_BUILD_DIR=$DEV_BUILD_DIR"
echo "  WAVE_BUILD_DIR=$WAVE_BUILD_DIR"
echo "  WINE_BUILD_DIR=$WINE_BUILD_DIR"
echo "  DATA_BUILD_DIR=$DATA_BUILD_DIR"
echo "  CANVAS_PUBLISH_DIR=$CANVAS_PUBLISH_DIR"

# Export for cohort build
cat > "$ROOT/artifacts/build-env.sh" <<EOF
export PRESENT_BUILD_DIR="$PRESENT_BUILD_DIR"
export WRITE_BUILD_DIR="$WRITE_BUILD_DIR"
export PRESENT_APP_BUILD_DIR="$PRESENT_APP_BUILD_DIR"
export PLAN_BUILD_DIR="$PLAN_BUILD_DIR"
export TERMINAL_BUILD_DIR="$TERMINAL_BUILD_DIR"
export DEV_BUILD_DIR="$DEV_BUILD_DIR"
export WAVE_BUILD_DIR="$WAVE_BUILD_DIR"
export WINE_BUILD_DIR="$WINE_BUILD_DIR"
export DATA_BUILD_DIR="$DATA_BUILD_DIR"
export CANVAS_PUBLISH_DIR="$CANVAS_PUBLISH_DIR"
EOF

echo "Build environment exported to $ROOT/artifacts/build-env.sh"