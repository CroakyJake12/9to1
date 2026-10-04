#!/bin/sh
set -eu

DEB=${1:-}
if [ -z "$DEB" ]; then
  echo 'Usage: verify-installable-deb.sh <package.deb>' >&2
  exit 64
fi
if [ ! -f "$DEB" ] || [ -L "$DEB" ]; then
  echo 'Package must be a regular .deb file, not a symlink.' >&2
  exit 65
fi
if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo 'dpkg-deb is required to inspect the package.' >&2
  exit 69
fi

PACKAGE=$(dpkg-deb --field "$DEB" Package)
ARCH=$(dpkg-deb --field "$DEB" Architecture)
if [ "$PACKAGE" != 'haven-llamacpp-runtime' ]; then
  echo "Unexpected package name: $PACKAGE" >&2
  exit 66
fi
if [ "$ARCH" != 'amd64' ]; then
  echo "Unexpected package architecture: $ARCH" >&2
  exit 67
fi

CONTROL_DIR=$(mktemp -d)
cleanup() { rm -rf "$CONTROL_DIR"; }
trap cleanup EXIT HUP INT TERM

dpkg-deb --control "$DEB" "$CONTROL_DIR"
for name in preinst postinst prerm postrm config triggers; do
  if [ -e "$CONTROL_DIR/$name" ] || [ -L "$CONTROL_DIR/$name" ]; then
    echo "Package contains forbidden maintainer/control script: $name" >&2
    exit 68
  fi
done

CONTENTS=$(dpkg-deb --contents "$DEB")
ROOT_MODE=$(printf '%s\n' "$CONTENTS" | awk '$NF == "./" { print $1; exit }')
if [ "$ROOT_MODE" != 'drwxr-xr-x' ]; then
  echo "Package root directory must be mode 0755; got ${ROOT_MODE:-missing}" >&2
  exit 72
fi
if printf '%s\n' "$CONTENTS" | grep -E '/models/|\.gguf($| )' >/dev/null; then
  echo 'Package contains forbidden model data.' >&2
  exit 70
fi
if printf '%s\n' "$CONTENTS" | grep -E '/systemd/user(-preset)?/|/systemd/user/' | grep -E '(^|/)(default|graphical|basic)\.target\.wants/|\.preset($| )' >/dev/null; then
  echo 'Package contains a user-service auto-enable/preset payload.' >&2
  exit 71
fi

exit 0
