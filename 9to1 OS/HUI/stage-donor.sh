#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lock="$root/havenos.lock"
dest="$root/HUI/vendor/Haven.UI"

command -v git >/dev/null || { echo "git is required" >&2; exit 2; }
command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }
[[ -f "$lock" ]] || { echo "Missing havenos.lock" >&2; exit 2; }

repo="$(jq -r '.components.HUI.migrationDonor.repository' "$lock")"
rev="$(jq -r '.components.HUI.migrationDonor.revision' "$lock")"
path="$(jq -r '.components.HUI.migrationDonor.sourcePath' "$lock")"

[[ "$repo" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || { echo "Invalid HUI donor repository" >&2; exit 2; }
[[ "$rev" =~ ^[0-9a-f]{40}$ ]] || { echo "HUI donor revision must be a full commit SHA" >&2; exit 2; }
[[ -n "$path" && "$path" != null && "$path" != /* && "$path" != *..* ]] || { echo "Invalid HUI donor source path" >&2; exit 2; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

git -C "$tmp" init -q
git -C "$tmp" remote add origin "https://github.com/$repo.git"
git -C "$tmp" fetch -q --depth 1 origin "$rev"
git -C "$tmp" checkout -q --detach FETCH_HEAD
actual="$(git -C "$tmp" rev-parse HEAD)"
[[ "$actual" == "$rev" ]] || { echo "Fetched donor SHA $actual does not match lock $rev" >&2; exit 1; }
[[ -f "$tmp/$path/Haven.UI.csproj" ]] || { echo "Pinned donor does not contain $path/Haven.UI.csproj" >&2; exit 1; }

rm -rf "$dest"
mkdir -p "$dest"
cp -R "$tmp/$path/." "$dest/"

# The pinned donor currently contains tracked build-output trees such as obj-hui.
# Generated output is not source and must never cross the CakeOS migration boundary.
find "$dest" -type d \( -name obj -o -name 'obj-*' -o -name bin -o -name 'bin-*' \) -prune -exec rm -rf {} +
if find "$dest" -type d \( -name obj -o -name 'obj-*' -o -name bin -o -name 'bin-*' \) -print -quit | grep -q .; then
  echo "Generated build-output directory remained after donor staging" >&2
  exit 1
fi

printf '%s\n' "$rev" > "$root/HUI/vendor/.donor-revision"

echo "Staged source-only $repo@$rev:$path into HUI/vendor/Haven.UI"