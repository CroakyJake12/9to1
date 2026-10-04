#!/bin/sh
set -eu

# Package the Haven Welcome launcher for the image builder.
# Inputs (read-only, owned by other workers -- do NOT move them here):
#   apps/haven-welcome/haven-welcome (POSIX sh launcher)
#   apps/haven-welcome/haven-welcome.desktop
#   apps/haven-welcome/icons/haven-welcome.svg
# Install layout produced:
#   /usr/bin/haven-welcome (0755)
#   /usr/share/applications/haven-welcome.desktop (0644)
#   /usr/share/icons/hicolor/scalable/apps/haven-welcome.svg (0644)
#   /usr/share/doc/haven-welcome/source-provenance (0644)
root="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
output_dir="${HAVENOS_DEB_DIR:-$root/artifacts/packages}"
source_revision="${HAVENOS_SOURCE_REVISION:-}"
version="${HAVENOS_PACKAGE_VERSION:-0.1.0}"
arch="${HAVENOS_PACKAGE_ARCH:-all}"

die() {
  printf '%s\n' "$*" >&2
  exit 2
}

command -v dpkg-deb >/dev/null 2>&1 || die 'dpkg-deb is required on the Linux builder.'
[ -n "$source_revision" ] || die 'HAVENOS_SOURCE_REVISION is required for package provenance.'
case "$source_revision" in
  *[!a-fA-F0-9]*) die 'HAVENOS_SOURCE_REVISION must be a hexadecimal Git revision.' ;;
esac
case "$version" in
  ''|*[!0-9A-Za-z.+:~_-]*) die 'HAVENOS_PACKAGE_VERSION contains unsupported Debian version characters.' ;;
esac
case "$arch" in
  amd64|all) ;;
  *) die 'HAVENOS_PACKAGE_ARCH must be amd64 or all.' ;;
esac

app_src="$root/apps/haven-welcome"
[ -f "$app_src/haven-welcome" ] || die "Missing welcome source: $app_src/haven-welcome"
[ -f "$app_src/haven-welcome.desktop" ] || die "Missing welcome source: $app_src/haven-welcome.desktop"
[ -f "$app_src/icons/haven-welcome.svg" ] || die "Missing welcome source: $app_src/icons/haven-welcome.svg"

work="$(mktemp -d "${TMPDIR:-/tmp}/haven-welcome-package.XXXXXX")"
package_root="$work/haven-welcome"
package="$output_dir/haven-welcome_${version}_${arch}.deb"
trap 'rm -rf "$work"' EXIT HUP INT TERM

mkdir -p \
  "$package_root/DEBIAN" \
  "$package_root/usr/bin" \
  "$package_root/usr/share/applications" \
  "$package_root/usr/share/icons/hicolor/scalable/apps" \
  "$package_root/usr/share/doc/haven-welcome"
chmod 0755 "$package_root/DEBIAN"
chmod 0755 \
  "$package_root/usr" \
  "$package_root/usr/bin" \
  "$package_root/usr/share" \
  "$package_root/usr/share/applications" \
  "$package_root/usr/share/icons" \
  "$package_root/usr/share/icons/hicolor" \
  "$package_root/usr/share/icons/hicolor/scalable" \
  "$package_root/usr/share/icons/hicolor/scalable/apps" \
  "$package_root/usr/share/doc" \
  "$package_root/usr/share/doc/haven-welcome"

cp "$app_src/haven-welcome" "$package_root/usr/bin/haven-welcome"
cp "$app_src/haven-welcome.desktop" "$package_root/usr/share/applications/haven-welcome.desktop"
cp "$app_src/icons/haven-welcome.svg" "$package_root/usr/share/icons/hicolor/scalable/apps/haven-welcome.svg"

cat > "$package_root/DEBIAN/control" <<EOF
Package: haven-welcome
Version: $version
Section: utils
Priority: optional
Architecture: $arch
Maintainer: HavenOS Platform <platform@havenos.invalid>
Recommends: zenity
Description: HavenOS first-run welcome launcher
 Tiny Haven-native welcome surface. Prints the Spaces-home starting
 point; its optional graphical card uses an existing dialog helper
 and never requires network or privileges.
EOF

cat > "$package_root/usr/share/doc/haven-welcome/source-provenance" <<EOF
HavenOS package: haven-welcome
Source revision: $source_revision
Package version: $version
Package architecture: $arch
EOF

find "$package_root/usr/share" -type f -exec chmod 0644 {} \;
chmod 0755 "$package_root/usr/bin/haven-welcome"
chmod 0644 "$package_root/DEBIAN/control"

mkdir -p "$output_dir"
dpkg-deb --root-owner-group --build "$package_root" "$package" >/dev/null
dpkg-deb --field "$package" Package Version Architecture >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/bin/haven-welcome' >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/applications/haven-welcome.desktop' >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/icons/hicolor/scalable/apps/haven-welcome.svg' >/dev/null
printf 'Created %s\n' "$package"
