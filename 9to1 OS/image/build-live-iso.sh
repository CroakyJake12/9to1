#!/usr/bin/env bash
set -euo pipefail

# Run only on a dedicated Ubuntu/Debian Linux builder with live-build installed.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
config="$root/platform/ubuntu/image/live-build"
packages="${HAVENOS_DEB_DIR:-$root/artifacts/packages}"
output="$root/artifacts/HavenOS-$(jq -r '.ubuntu.release' "$root/havenos.lock")-amd64.iso"

command -v lb >/dev/null || { echo "live-build (lb) is required on the Linux builder." >&2; exit 2; }
command -v jq >/dev/null || { echo "jq is required to read havenos.lock." >&2; exit 2; }
[[ -f "$root/havenos.lock" ]] || { echo "Missing provenance lock." >&2; exit 2; }
[[ -d "$packages" ]] || { echo "Haven package directory is missing: $packages" >&2; exit 2; }
compgen -G "$packages/*.deb" >/dev/null || { echo "Refusing to compose an image without a real Haven .deb package." >&2; exit 2; }

mkdir -p "$config/config/packages.chroot" "$root/artifacts"
cp -f "$packages"/*.deb "$config/config/packages.chroot/"
cd "$config"
./auto/config
lb build
[[ -f live-image-amd64.hybrid.iso ]] || { echo "live-build completed without an ISO." >&2; exit 1; }
cp -f live-image-amd64.hybrid.iso "$output"
sha256sum "$output" | tee "$output.sha256"
echo "Created $output"
