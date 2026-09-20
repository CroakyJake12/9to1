#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
lock="$root/havenos.lock"
packages="$root/platform/ubuntu/packages/havenos-desktop.list"

command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }

rev="$(jq -r '.components.HUI.migrationDonor.revision' "$lock")"
[[ "$rev" =~ ^[0-9a-f]{40}$ ]] || { echo "HUI donor revision is not a full SHA" >&2; exit 1; }

if grep -Eq '^\*\*\* |^#!/bin/sh$|^lb config ' "$packages"; then
  echo "Ubuntu package manifest still contains patch/script residue" >&2
  exit 1
fi

expected=(ubuntu-desktop-minimal gnome-shell mutter gdm3 network-manager pipewire wireplumber systemd live-build debootstrap dpkg-dev)
for package in "${expected[@]}"; do
  grep -Fxq "$package" "$packages" || { echo "Missing desktop package: $package" >&2; exit 1; }
done

if [[ -d "$root/HUI/vendor/Haven.UI" ]]; then
  if find "$root/HUI/vendor/Haven.UI" -type d \( -name obj -o -name 'obj-*' -o -name bin -o -name 'bin-*' \) -print -quit | grep -q .; then
    echo "Staged HUI core contains generated build-output directories" >&2
    exit 1
  fi

  # Documentation and comments may name Avalonia while describing the boundary.
  # Reject only compile-time dependencies/usages in the platform-neutral HUI core.
  if grep -RInE --include='*.cs' '(^|[[:space:]])using[[:space:]]+Avalonia([.;]|$)|Avalonia\.' "$root/HUI/vendor/Haven.UI" | grep -q .; then
    echo "Staged HUI core contains an Avalonia C# dependency" >&2
    exit 1
  fi
  if grep -RInE --include='*.csproj' '<(PackageReference|ProjectReference|Reference)[^>]*Include="[^"]*Avalonia[^"]*"' "$root/HUI/vendor/Haven.UI" | grep -q .; then
    echo "Staged HUI core contains an Avalonia project/package reference" >&2
    exit 1
  fi
fi

echo "HUI preview provenance and package-manifest validation passed."
