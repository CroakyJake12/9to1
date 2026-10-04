#!/usr/bin/env bash
set -euo pipefail

package_name='haven-hui-preview'
package_version='0.1.0+git2a578502c319'
package_file="haven-hui-preview_${package_version}_amd64.deb"
expected_sha256='104f8ff3929b8b0e1c90673804d7cfbe79a31ea5c2763dc040246a7ead53575a'
mode="${1:-probe}"
explicit_package="${2:-}"

case "$mode" in
  probe|install|rollback) ;;
  *) echo "Usage: $0 [probe|install|rollback] [package.deb]" >&2; exit 2 ;;
esac

if [[ "$(uname -s)" != Linux || "$(uname -m)" != x86_64 ]]; then
  echo 'Gate 4 requires the approved x86_64 Linux VM.' >&2
  exit 3
fi

if [[ -z "${XDG_SESSION_ID:-}" || -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]]; then
  echo 'Gate 4 must run inside a real logged-in graphical user session, not from GDM or an isolated root shell.' >&2
  exit 4
fi

current_desktop="${XDG_CURRENT_DESKTOP:-${DESKTOP_SESSION:-}}"
if [[ "$current_desktop" != *GNOME* && "$current_desktop" != *gnome* && "$current_desktop" != *ubuntu* ]]; then
  echo "Gate 4 requires stock GNOME/Ubuntu session context; got '${current_desktop:-unknown}'." >&2
  exit 5
fi

shell_ping() {
  command -v gdbus >/dev/null || { echo 'gdbus is required to prove GNOME Shell remains alive.' >&2; return 1; }
  gdbus call --session \
    --dest org.gnome.Shell \
    --object-path /org/gnome/Shell \
    --method org.freedesktop.DBus.Peer.Ping >/dev/null
}

package_version_of() {
  dpkg-query -W -f='${Version}' "$1" 2>/dev/null || true
}

capture_platform_versions() {
  local destination="$1"
  {
    printf 'gnome-shell='; package_version_of gnome-shell; printf '\n'
    printf 'mutter='; package_version_of mutter; printf '\n'
    printf 'gdm3='; package_version_of gdm3; printf '\n'
  } > "$destination"
}

find_package() {
  if [[ -n "$explicit_package" ]]; then
    printf '%s\n' "$explicit_package"
    return
  fi

  local source target
  while read -r source target; do
    [[ -n "$source" && -n "$target" ]] || continue
    if [[ "$source" == 'havenos-transfer' || "$source" == *'havenos-transfer'* ]]; then
      if [[ -f "$target/$package_file" ]]; then
        printf '%s\n' "$target/$package_file"
        return
      fi
    fi
  done < <(findmnt -rn -t vboxsf -o SOURCE,TARGET 2>/dev/null || true)

  for candidate in \
    "/media/sf_havenos-transfer/$package_file" \
    "/media/havenos-transfer/$package_file" \
    "/mnt/havenos-transfer/$package_file"; do
    if [[ -f "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return
    fi
  done

  return 1
}

export_evidence() {
  local package_path="$1"
  local evidence="$2"
  local transfer_mount
  transfer_mount="$(findmnt -rn -T "$package_path" -t vboxsf -o TARGET 2>/dev/null | head -n 1 || true)"
  if [[ -z "$transfer_mount" ]]; then
    echo 'Gate 4 evidence export failed: accepted package is not on a VirtualBox shared-folder mount.' >&2
    return 1
  fi
  if [[ ! -w "$transfer_mount" ]]; then
    echo "Gate 4 evidence export failed: shared-folder mount is not writable: $transfer_mount" >&2
    return 1
  fi

  local export_root="$transfer_mount/cakeos-gate4-evidence"
  local export_dir="$export_root/$(basename "$evidence")"
  mkdir -p "$export_dir"
  printf '%s\n' "$export_dir" > "$evidence/export-path.txt"
  cp -a "$evidence/." "$export_dir/"
  sync "$export_dir" 2>/dev/null || true
  printf '%s\n' "$export_dir"
}

if [[ "$mode" == rollback ]]; then
  shell_ping
  if dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null | grep -Fqx 'install ok installed'; then
    sudo dpkg --remove "$package_name"
  fi
  if dpkg-query -W -f='${Status}' "$package_name" 2>/dev/null | grep -Fqx 'install ok installed'; then
    echo 'Rollback failed: preview package is still installed.' >&2
    exit 20
  fi
  shell_ping
  echo 'Gate 4 rollback passed: preview package absent and GNOME Shell still responsive.'
  exit 0
fi

package_path="$(find_package)" || {
  echo "Could not locate $package_file in the existing havenos-transfer share; pass an explicit package path as argument 2." >&2
  exit 6
}
package_path="$(readlink -f "$package_path")"
[[ -f "$package_path" ]] || { echo "Package path is not a file: $package_path" >&2; exit 7; }

actual_sha256="$(sha256sum "$package_path" | awk '{print $1}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
  echo "Gate 4 package SHA-256 mismatch: $actual_sha256" >&2
  exit 8
fi

if [[ "$(dpkg-deb --field "$package_path" Package)" != "$package_name" || \
      "$(dpkg-deb --field "$package_path" Version)" != "$package_version" || \
      "$(dpkg-deb --field "$package_path" Architecture)" != amd64 ]]; then
  echo 'Gate 4 package metadata does not match the accepted artifact.' >&2
  exit 9
fi

shell_ping

evidence_root="${XDG_STATE_HOME:-$HOME/.local/state}/cakeos/hui-preview-gate4"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
evidence="$evidence_root/$timestamp-$mode"
mkdir -p "$evidence"
printf '%s  %s\n' "$actual_sha256" "$package_path" > "$evidence/package.sha256"
printf '%s\n' "$package_version" > "$evidence/expected-version.txt"
loginctl show-session "$XDG_SESSION_ID" -p Name -p State -p Type -p Class -p Remote -p Desktop > "$evidence/session.txt"
capture_platform_versions "$evidence/platform-before.txt"

run_preview() {
  local executable="$1"
  local log="$2"
  CAKEOS_HUI_PREVIEW_SELF_TEST=1 \
  CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS=5000 \
    "$executable" >"$log" 2>&1
}

if [[ "$mode" == probe ]]; then
  probe_root="$(mktemp -d)"
  trap 'rm -rf "$probe_root"' EXIT
  dpkg-deb -x "$package_path" "$probe_root"
  executable="$probe_root/usr/lib/cakeos/hui-preview/cakeos-hui-linux-preview"
  [[ -x "$executable" ]] || { echo 'Accepted package does not contain the graphical executable.' >&2; exit 10; }
  [[ ! -e "$probe_root/usr/lib/cakeos/hui-preview/libcoreclrtraceptprovider.so" ]] || {
    echo 'Accepted package unexpectedly contains the pruned obsolete LTTng provider.' >&2
    exit 11
  }
  run_preview "$executable" "$evidence/preview.log"
  shell_ping
  capture_platform_versions "$evidence/platform-after.txt"
  cmp "$evidence/platform-before.txt" "$evidence/platform-after.txt"
  printf 'probe=passed\nruntime_self_test=passed\nplatform_invariant=passed\npackage_sha256=%s\n' \
    "$actual_sha256" > "$evidence/result.txt"
  exported="$(export_evidence "$package_path" "$evidence")"
  echo "Gate 4 rootless probe passed. Local evidence: $evidence"
  echo "Exported evidence: $exported"
  exit 0
fi

if [[ "${EUID}" -eq 0 ]]; then
  installer=()
else
  command -v sudo >/dev/null || { echo 'sudo is required for the install acceptance step.' >&2; exit 12; }
  installer=(sudo)
fi

"${installer[@]}" dpkg --install "$package_path"

installed_version="$(package_version_of "$package_name")"
if [[ "$installed_version" != "$package_version" ]]; then
  echo "Installed package version mismatch: ${installed_version:-missing}" >&2
  exit 13
fi

if dpkg -V "$package_name" | grep -q .; then
  echo 'Installed package verification reported modified/missing files.' >&2
  dpkg -V "$package_name" >&2 || true
  exit 14
fi

[[ -x /usr/bin/cakeos-hui-preview ]] || { echo 'Installed launcher is missing.' >&2; exit 15; }
[[ -x /usr/lib/cakeos/hui-preview/cakeos-hui-linux-preview ]] || { echo 'Installed graphical executable is missing.' >&2; exit 16; }
[[ ! -e /usr/lib/cakeos/hui-preview/libcoreclrtraceptprovider.so ]] || {
  echo 'Installed payload unexpectedly contains the pruned obsolete LTTng provider.' >&2
  exit 17
}

grep -Fqx 'Name=CakeOS HUI Preview' /usr/share/applications/cakeos-hui-preview.desktop
grep -Fqx 'Exec=cakeos-hui-preview' /usr/share/applications/cakeos-hui-preview.desktop
run_preview /usr/bin/cakeos-hui-preview "$evidence/preview.log"
shell_ping
capture_platform_versions "$evidence/platform-after.txt"
cmp "$evidence/platform-before.txt" "$evidence/platform-after.txt"
printf 'install=passed\nruntime_self_test=passed\nplatform_invariant=passed\npackage_sha256=%s\ninstalled_version=%s\n' \
  "$actual_sha256" "$installed_version" > "$evidence/result.txt"
exported="$(export_evidence "$package_path" "$evidence")"

echo "Gate 4 install/runtime checks passed. Local evidence: $evidence"
echo "Exported evidence: $exported"
echo "Rollback command: $0 rollback"
