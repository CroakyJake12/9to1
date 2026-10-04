#!/usr/bin/env bash
set -euo pipefail

# Verify and extract already-downloaded immutable packages. This helper never
# builds, fetches, installs, enables services, or mutates the model store.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
lock="$root/packaging/llamacpp/cohort-artifact.lock.json"
out="${HAVEN_COHORT_STAGE_DIR:-$root/artifacts/cohort-stage}"
llama_deb="${HAVEN_LLAMA_DEB:-}"
hui_deb="${HAVEN_HUI_DEB:-}"

command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }
command -v sha256sum >/dev/null || { echo "sha256sum is required" >&2; exit 2; }
command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }
[[ -f "$lock" ]] || { echo "Missing cohort lock: $lock" >&2; exit 2; }
[[ -n "$llama_deb" && -f "$llama_deb" ]] || { echo "HAVEN_LLAMA_DEB must name an existing package" >&2; exit 2; }
[[ -n "$hui_deb" && -f "$hui_deb" ]] || { echo "HAVEN_HUI_DEB must name an existing package" >&2; exit 2; }

verify_package() {
  local id="$1"
  local package="$2"
  local expected filename architecture
  expected="$(jq -r --arg id "$id" '.artifacts[] | select(.id == $id) | .package.sha256' "$lock")"
  filename="$(jq -r --arg id "$id" '.artifacts[] | select(.id == $id) | .package.filename' "$lock")"
  architecture="$(jq -r --arg id "$id" '.artifacts[] | select(.id == $id) | .package.architecture' "$lock")"
  [[ "$(basename "$package")" == "$filename" ]] || { echo "$id filename mismatch" >&2; exit 1; }
  printf '%s  %s\n' "$expected" "$package" | sha256sum -c -
  local expected_package
  expected_package="$(jq -r --arg id "$id" 'if $id == "llamacpp-runtime" then "haven-llamacpp-runtime" else "haven-hui-preview" end' "$lock")"
  [[ "$(dpkg-deb --field "$package" Package)" == "$expected_package" ]] || {
    echo "$id package name mismatch" >&2
    exit 1
  }
  [[ "$(dpkg-deb --field "$package" Architecture)" == "$architecture" ]] || { echo "$id architecture mismatch" >&2; exit 1; }
}

verify_package "llamacpp-runtime" "$llama_deb"
verify_package "hui-linux-graphical-preview" "$hui_deb"

rm -rf "$out"
mkdir -p "$out/llamacpp-runtime" "$out/hui-linux-graphical-preview"
dpkg-deb --extract "$llama_deb" "$out/llamacpp-runtime"
dpkg-deb --extract "$hui_deb" "$out/hui-linux-graphical-preview"

test -x "$out/llamacpp-runtime/usr/bin/haven-modelctl"
test -x "$out/hui-linux-graphical-preview/usr/bin/cakeos-hui-preview"
test -x "$out/hui-linux-graphical-preview/usr/lib/cakeos/hui-preview/cakeos-hui-linux-preview"
test ! -e "$out/hui-linux-graphical-preview/usr/lib/cakeos/hui-preview/libcoreclrtraceptprovider.so"
test ! -d "$out/llamacpp-runtime/usr/share/haven/models"
test ! -d "$out/hui-linux-graphical-preview/models"

printf 'Verified cohort packages staged at %s\n' "$out"
