#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
bash "$root/HUI/stage-donor.sh"

rm -rf "$root/artifacts/hui-preview/publish"
mkdir -p "$root/artifacts/hui-preview/publish"

dotnet publish "$root/HUI/Preview/CakeOS.HuiPreview.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:InvariantGlobalization=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -p:ContinuousIntegrationBuild=true \
  -o "$root/artifacts/hui-preview/publish"

preview="$root/artifacts/hui-preview/publish/cakeos-hui-preview"
[[ -x "$preview" ]] || { echo "Self-contained HUI preview executable was not produced" >&2; exit 1; }

"$preview" > "$root/artifacts/hui-preview/render-summary.json"

echo "Built self-contained HUI preview and wrote artifacts/hui-preview/render-summary.json"
