#!/bin/sh
set -eu

# Package the HavenOS shell treatment, theme and defaults.
# Inputs (read-only, owned by other workers -- do NOT move them here):
#   platform/haven-shell/extension/metadata.json (uuid haven-shell@havenos, GNOME Shell 50/51 compatible)
#   platform/haven-shell/extension/extension.js
#   platform/haven-shell/extension/stylesheet.css
#   platform/haven-shell/session/haven-wayland.desktop (installed as haven.desktop)
#   HUI/theme/tokens.json
#   HUI/theme/haven-theme.css
#   HUI/shell/haven-shell-primitive.css
#   branding/haven-wallpaper.svg
# Install layout produced:
#   /usr/share/gnome-shell/extensions/haven-shell@havenos/{metadata.json,extension.js,stylesheet.css}
#   /usr/share/wayland-sessions/haven.desktop
#   /usr/share/havenos/theme/{tokens.json,haven-theme.css,haven-shell-primitive.css}
#   /usr/share/backgrounds/haven/haven-wallpaper.svg
#   /usr/share/glib-2.0/schemas/90-haven-shell.gschema.override
#   /usr/share/doc/havenos-shell/source-provenance
# NOTE: gsettings overrides take effect once glib-compile-schemas runs on the
# target (normally via the libglib2.0 trigger on the Linux builder/image).
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

ext_src="$root/platform/haven-shell/extension"
session_src="$root/platform/haven-shell/session"
theme_src="$root/HUI/theme"
primitive_src="$root/HUI/shell/haven-shell-primitive.css"
wallpaper_src="$root/branding/haven-wallpaper.svg"
[ -f "$ext_src/metadata.json" ] || die "Missing extension source: $ext_src/metadata.json"
[ -f "$ext_src/extension.js" ] || die "Missing extension source: $ext_src/extension.js"
[ -f "$ext_src/stylesheet.css" ] || die "Missing extension source: $ext_src/stylesheet.css"
[ -f "$session_src/haven-wayland.desktop" ] || die "Missing session source: $session_src/haven-wayland.desktop"
[ -f "$theme_src/tokens.json" ] || die "Missing theme source: $theme_src/tokens.json"
[ -f "$theme_src/haven-theme.css" ] || die "Missing theme source: $theme_src/haven-theme.css"
[ -f "$primitive_src" ] || die "Missing shell primitive source: $primitive_src"
[ -f "$wallpaper_src" ] || die "Missing wallpaper source: $wallpaper_src"

work="$(mktemp -d "${TMPDIR:-/tmp}/havenos-shell-package.XXXXXX")"
package_root="$work/havenos-shell"
package="$output_dir/havenos-shell_${version}_${arch}.deb"
trap 'rm -rf "$work"' EXIT HUP INT TERM

mkdir -p \
  "$package_root/DEBIAN" \
  "$package_root/usr/share/doc/havenos-shell" \
  "$package_root/usr/share/gnome-shell/extensions/haven-shell@havenos" \
  "$package_root/usr/share/wayland-sessions" \
  "$package_root/usr/share/havenos/theme" \
  "$package_root/usr/share/backgrounds/haven" \
  "$package_root/usr/share/glib-2.0/schemas"
chmod 0755 "$package_root/DEBIAN"
chmod 0755 \
  "$package_root/usr" \
  "$package_root/usr/share" \
  "$package_root/usr/share/doc" \
  "$package_root/usr/share/doc/havenos-shell" \
  "$package_root/usr/share/gnome-shell" \
  "$package_root/usr/share/gnome-shell/extensions" \
  "$package_root/usr/share/gnome-shell/extensions/haven-shell@havenos" \
  "$package_root/usr/share/wayland-sessions" \
  "$package_root/usr/share/havenos" \
  "$package_root/usr/share/havenos/theme" \
  "$package_root/usr/share/backgrounds" \
  "$package_root/usr/share/backgrounds/haven" \
  "$package_root/usr/share/glib-2.0" \
  "$package_root/usr/share/glib-2.0/schemas"

cp "$ext_src/metadata.json" "$package_root/usr/share/gnome-shell/extensions/haven-shell@havenos/metadata.json"
cp "$ext_src/extension.js" "$package_root/usr/share/gnome-shell/extensions/haven-shell@havenos/extension.js"
cp "$ext_src/stylesheet.css" "$package_root/usr/share/gnome-shell/extensions/haven-shell@havenos/stylesheet.css"
cp "$session_src/haven-wayland.desktop" "$package_root/usr/share/wayland-sessions/haven.desktop"
cp "$theme_src/tokens.json" "$package_root/usr/share/havenos/theme/tokens.json"
cp "$theme_src/haven-theme.css" "$package_root/usr/share/havenos/theme/haven-theme.css"
cp "$primitive_src" "$package_root/usr/share/havenos/theme/haven-shell-primitive.css"
cp "$wallpaper_src" "$package_root/usr/share/backgrounds/haven/haven-wallpaper.svg"

cat > "$package_root/DEBIAN/control" <<EOF
Package: havenos-shell
Version: $version
Section: gnome
Priority: optional
Architecture: $arch
Maintainer: HavenOS Platform <platform@havenos.invalid>
Depends: gnome-shell (>= 50), gnome-session-bin | gnome-session, gsettings-desktop-schemas
Description: HavenOS GNOME shell treatment, session, theme and defaults
 HavenOS system shell: Haven top-bar extension, Haven session entries,
 HUI theme tokens, Haven wallpaper, and gsettings defaults that enable
 the extension with the Haven wallpaper and Haven session label.
EOF

cat > "$package_root/usr/share/glib-2.0/schemas/90-haven-shell.gschema.override" <<'EOF'
# HavenOS shell defaults.
# Generated by packaging/build-havenos-shell-deb.sh from:
#   platform/haven-shell/extension/metadata.json (uuid haven-shell@havenos)
#   branding/haven-wallpaper.svg
#   platform/haven-shell/session/haven-wayland.desktop (GDM label; launches
#   Ubuntu's validated session target)
# Do not hand-edit the installed copy; rebuild the havenos-shell package.

[org.gnome.shell]
enabled-extensions=['haven-shell@havenos']

[org.gnome.desktop.background]
picture-uri='file:///usr/share/backgrounds/haven/haven-wallpaper.svg'
picture-uri-dark='file:///usr/share/backgrounds/haven/haven-wallpaper.svg'

[org.gnome.desktop.screensaver]
picture-uri='file:///usr/share/backgrounds/haven/haven-wallpaper.svg'

[org.gnome.desktop.session]
session-name='Haven'
EOF

cat > "$package_root/usr/share/doc/havenos-shell/source-provenance" <<EOF
HavenOS package: havenos-shell
Source revision: $source_revision
Package version: $version
Package architecture: $arch
EOF

find "$package_root/usr/share" -type f -exec chmod 0644 {} \;
chmod 0644 "$package_root/DEBIAN/control"

mkdir -p "$output_dir"
dpkg-deb --root-owner-group --build "$package_root" "$package" >/dev/null
dpkg-deb --field "$package" Package Version Architecture >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/gnome-shell/extensions/haven-shell@havenos/metadata.json' >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/wayland-sessions/haven.desktop' >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/glib-2.0/schemas/90-haven-shell.gschema.override' >/dev/null
printf 'Created %s\n' "$package"
