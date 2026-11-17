#!/usr/bin/env bash
# One command: the genuine Files app (sealed WebAssembly) through the real
# Files Worker and Workerd against the disposable local Drive fixture, opened
# visibly on this machine. Synthetic-provider only; see the 17 November
# files-local-candidate document for evidence tiers and limits.
set -euo pipefail
cd "$(dirname "$0")/.."
[ -d node_modules ] || npm ci --no-audit --no-fund
WWW="${1:-/tmp/opencode/files-web-candidate-local/sdk-publish/wwwroot}"
if [ ! -f "$WWW/index.html" ]; then
  echo "Sealed bundle absent at $WWW." >&2
  echo "Build it first from the repository root (pinned SDK 10.0.401):" >&2
  echo "  DOTNET_ROOT=/tmp/files-dotnet-10.0.401 PATH=/tmp/files-dotnet-10.0.401:\$PATH \\" >&2
  echo "  python3 -B apps/Web/Tests/ci/run-ordinary-web-publish.py --expected-commit \"\$(git rev-parse HEAD)\" --output /tmp/opencode/files-web-candidate-local" >&2
  exit 1
fi
exec node dev/local-candidate.mjs --wwwroot "$WWW" --open
