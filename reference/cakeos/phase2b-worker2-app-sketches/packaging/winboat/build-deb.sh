#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${CAKEOS_WINBOAT_VERSION:-0.1.0}"
out="$root/artifacts/packages"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required" >&2; exit 2; }

# WinBoat runtime - this is a meta-package that depends on the actual upstream WinBoat
# The actual WinBoat runtime should be provided as a pre-built artifact
winboat_runtime="${WINBOAT_RUNTIME_PATH:-}"
if [[ -z "$winboat_runtime" || ! -d "$winboat_runtime" ]]; then
  echo "WINBOAT_RUNTIME_PATH must be set to the upstream WinBoat runtime directory" >&2
  exit 2
fi

source_date_epoch="${SOURCE_DATE_EPOCH:-$(git -C "$root" show -s --format=%ct HEAD)}"
[[ "$source_date_epoch" =~ ^[0-9]+$ ]] || { echo "Invalid SOURCE_DATE_EPOCH: $source_date_epoch" >&2; exit 2; }
export SOURCE_DATE_EPOCH="$source_date_epoch"

mkdir -p "$stage/DEBIAN" "$stage/usr/lib/havenos/winboat" "$stage/usr/bin" "$out"

# Copy WinBoat runtime
cp -R "$winboat_runtime/." "$stage/usr/lib/havenos/winboat/"

cat > "$stage/usr/bin/haven-winboat" <<'EOF'
#!/bin/sh
set -eu
exec /usr/lib/havenos/winboat/winboat "$@"
EOF
chmod 0755 "$stage/usr/bin/haven-winboat"

cat > "$stage/DEBIAN/control" <<EOF
Package: havenos-winboat
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Depends: qemu-system-x86, qemu-kvm, docker.io | podman, freerdp2-x11, libvirt-daemon-system, bridge-utils, iptables
Maintainer: HavenOS Platform <platform@havenos.invalid>
Description: HavenOS WinBoat runtime (Windows container/VM + FreeRDP RemoteApp)
  Actual upstream WinBoat runtime for running Windows applications via KVM container/VM
  with FreeRDP RemoteApp integration. Distinct from Wine - provides full Windows Guest OS.
EOF

find "$stage" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
package="$out/havenos-winboat_${version}_amd64.deb"
dpkg-deb --build --root-owner-group "$stage" "$package"
dpkg-deb --info "$package"
sha256sum "$package" | tee "$package.sha256"