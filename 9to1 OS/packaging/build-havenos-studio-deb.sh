#!/bin/sh
set -eu

# Package the independently published HavenOS Studio tool for the image builder.
root="$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)"
publish_dir="${HAVENOS_PUBLISH_DIR:-$root/artifacts/publish/havenos-studio}"
output_dir="${HAVENOS_DEB_DIR:-$root/artifacts/packages}"
source_revision="${HAVENOS_SOURCE_REVISION:-}"
version="${HAVENOS_PACKAGE_VERSION:-0.1.0}"

die() {
  printf '%s\n' "$*" >&2
  exit 2
}

command -v dpkg-deb >/dev/null 2>&1 || die 'dpkg-deb is required on the Linux builder.'
[ -d "$publish_dir" ] || die "Published HavenOS Studio output is missing: $publish_dir"
[ -n "$source_revision" ] || die 'HAVENOS_SOURCE_REVISION is required for package provenance.'
case "$source_revision" in
  *[!a-fA-F0-9]*) die 'HAVENOS_SOURCE_REVISION must be a hexadecimal Git revision.' ;;
esac
case "$version" in
  ''|*[!0-9A-Za-z.+:~_-]*) die 'HAVENOS_PACKAGE_VERSION contains unsupported Debian version characters.' ;;
esac

work="$(mktemp -d "${TMPDIR:-/tmp}/havenos-studio-package.XXXXXX")"
package_root="$work/havenos-studio"
package="$output_dir/havenos-studio_${version}_amd64.deb"
trap 'rm -rf "$work"' EXIT HUP INT TERM

mkdir -p \
  "$package_root/DEBIAN" \
  "$package_root/usr/bin" \
  "$package_root/usr/lib/havenos/studio" \
  "$package_root/usr/share/applications" \
  "$package_root/usr/share/doc/havenos-studio" \
  "$package_root/usr/share/havenos"
chmod 0755 "$package_root/DEBIAN"
chmod 0755 \
  "$package_root/usr" \
  "$package_root/usr/bin" \
  "$package_root/usr/lib" \
  "$package_root/usr/lib/havenos" \
  "$package_root/usr/lib/havenos/studio" \
  "$package_root/usr/share" \
  "$package_root/usr/share/applications" \
  "$package_root/usr/share/doc" \
  "$package_root/usr/share/doc/havenos-studio" \
  "$package_root/usr/share/havenos"
cp -a "$publish_dir/." "$package_root/usr/lib/havenos/studio/"
find "$package_root/usr/lib/havenos/studio" -type f -exec chmod 0644 {} \;
chmod 0755 "$package_root/usr/lib/havenos/studio/HavenOS.Studio"
ln -s /usr/lib/havenos/studio/HavenOS.Studio "$package_root/usr/bin/havenos-studio"

cat > "$package_root/DEBIAN/control" <<EOF
Package: havenos-studio
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: HavenOS Platform <platform@havenos.invalid>
Depends: libc6, libgcc-s1, libstdc++6, zlib1g
Description: HavenOS workspace and source validation tool
 HavenOS Studio reports the provenance and layout of an installed HavenOS
 workspace. It is not required for the desktop session to start.
EOF

cat > "$package_root/usr/share/havenos/source-provenance" <<EOF
HavenOS package: havenos-studio
Source revision: $source_revision
Package version: $version
EOF

cat > "$package_root/usr/share/havenos/os-release" <<EOF
NAME="HavenOS"
ID=havenos
PRETTY_NAME="HavenOS"
HAVENOS_SOURCE_REVISION="$source_revision"
EOF

cat > "$package_root/usr/share/applications/havenos-studio.desktop" <<'EOF'
[Desktop Entry]
Name=HavenOS Studio
Comment=Inspect HavenOS source provenance
Exec=/usr/bin/havenos-studio
Terminal=true
Type=Application
Categories=Development;
EOF
chmod 0644 \
  "$package_root/usr/share/applications/havenos-studio.desktop" \
  "$package_root/usr/share/havenos/os-release" \
  "$package_root/usr/share/havenos/source-provenance"

mkdir -p "$output_dir"
dpkg-deb --root-owner-group --build "$package_root" "$package" >/dev/null
dpkg-deb --field "$package" Package Version Architecture >/dev/null
dpkg-deb --contents "$package" | grep -F '/usr/share/havenos/os-release' >/dev/null
printf 'Created %s\n' "$package"
