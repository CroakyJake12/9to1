#!/usr/bin/env bash
# Thunder path-preparation fixture (bounded, no network/SDK/native).
#
# Sources the ACTUAL production driver `.github/scripts/thunder-cuda-compile.sh` (main-guarded: only
# its functions/constants are imported, the build/network main is never executed) and exercises
# `thunder_prepare_paths`:
#   - fresh shared WORK/EVIDENCE/dependency-parent dirs are created;
#   - build-work and install must remain ABSENT until the builder takes ownership;
#   - a pre-existing install OR build-work path is refused (never reused).
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DIR/../../.." && pwd)"
source "$REPO_ROOT/.github/scripts/thunder-cuda-compile.sh"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

work="$tmp/work"
evidence="$tmp/evidence"
deps="$tmp/deps"
install="$work/install"

# 1. Fresh paths -> success; shared dirs exist; build-work/install remain absent.
if thunder_prepare_paths "$work" "$install" "$evidence" "$deps" >/dev/null 2>&1; then
  { [ -d "$work" ] && [ -d "$evidence" ] && [ -d "$deps" ]; } || { echo "FAIL: shared dirs not created"; exit 1; }
  { [ ! -e "$work/build-work" ] && [ ! -e "$install" ]; } || { echo "FAIL: build-work/install created early"; exit 1; }
else
  echo "FAIL: fresh path preparation returned nonzero"; exit 1
fi

# 2. Pre-existing install -> refused.
mkdir -p "$install"
if thunder_prepare_paths "$work" "$install" "$evidence" "$deps" >/dev/null 2>&1; then
  echo "FAIL: pre-existing install accepted"; exit 1
fi

# 3. Pre-existing build-work -> refused.
rm -rf "$install"
mkdir -p "$work/build-work"
if thunder_prepare_paths "$work" "$install" "$evidence" "$deps" >/dev/null 2>&1; then
  echo "FAIL: pre-existing build-work accepted"; exit 1
fi

echo "THUNDER-PATH-PREP-FIXTURE-OK"
