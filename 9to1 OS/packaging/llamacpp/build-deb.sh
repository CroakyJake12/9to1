#!/bin/sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
BINARY=${LLAMA_SERVER_BINARY:-}
OUTPUT_DIR=${HAVEN_PACKAGE_OUTPUT_DIR:-}
MANIFEST="$SCRIPT_DIR/package-manifest.json"

if [ -z "$BINARY" ] || [ -z "$OUTPUT_DIR" ]; then
  echo 'Set LLAMA_SERVER_BINARY and HAVEN_PACKAGE_OUTPUT_DIR explicitly.' >&2
  exit 64
fi
case "$BINARY" in
  /*) ;;
  *) echo 'LLAMA_SERVER_BINARY must be an absolute path.' >&2; exit 65 ;;
esac
if [ ! -f "$BINARY" ] || [ ! -x "$BINARY" ] || [ -L "$BINARY" ]; then
  echo 'LLAMA_SERVER_BINARY must be a regular executable file, not a symlink.' >&2
  exit 66
fi
if ! command -v dpkg-deb >/dev/null 2>&1; then
  echo 'dpkg-deb is required to build the Debian package.' >&2
  exit 69
fi

PACKAGE=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["package"])' "$MANIFEST")
VERSION=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["version"])' "$MANIFEST")
ARCH=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["architecture"])' "$MANIFEST")
case "$PACKAGE:$VERSION:$ARCH" in
  *[!A-Za-z0-9.+:~_-]*) echo 'Package metadata contains unsafe characters.' >&2; exit 67 ;;
esac

VERSION_TEXT=$($BINARY --version 2>&1)
printf '%s\n' "$VERSION_TEXT" | grep -F '0.4.0' >/dev/null
printf '%s\n' "$VERSION_TEXT" | grep -F '5266f24' >/dev/null

mkdir -p "$OUTPUT_DIR"
STAGE=$(mktemp -d "$OUTPUT_DIR/.haven-llamacpp-stage.XXXXXX")
cleanup() { rm -rf "$STAGE"; }
trap cleanup EXIT HUP INT TERM
umask 022
# mktemp intentionally creates private 0700 directories. Debian archives also
# encode the staging root as './', so normalize it before package assembly.
chmod 0755 "$STAGE"

mkdir -p \
  "$STAGE/DEBIAN" \
  "$STAGE/usr/bin" \
  "$STAGE/usr/lib/haven/inference" \
  "$STAGE/usr/lib/haven/llama.cpp" \
  "$STAGE/usr/lib/systemd/user" \
  "$STAGE/usr/share/doc/$PACKAGE"

install -m 0755 "$REPO_ROOT/packaging/bin/haven-modelctl" "$STAGE/usr/bin/haven-modelctl"
for file in broker.py gguf.py model_lease.py modelctl.py backend_matrix.py hardware_probe.py budget.py benchmark.py; do
  install -m 0644 "$REPO_ROOT/runtime/llamacpp/$file" "$STAGE/usr/lib/haven/inference/$file"
done
install -m 0644 "$REPO_ROOT/runtime/llamacpp/provider-contract.json" "$STAGE/usr/lib/haven/inference/provider-contract.json"
install -m 0644 "$REPO_ROOT/runtime/llamacpp/backend-matrix.json" "$STAGE/usr/lib/haven/inference/backend-matrix.json"
install -m 0755 "$BINARY" "$STAGE/usr/lib/haven/llama.cpp/llama-server"
install -m 0644 "$REPO_ROOT/packaging/systemd/user/haven-inference-broker.service" "$STAGE/usr/lib/systemd/user/haven-inference-broker.service"
install -m 0644 "$REPO_ROOT/runtime/llamacpp/licenses/llama.cpp-LICENSE" "$STAGE/usr/share/doc/$PACKAGE/llama.cpp-LICENSE"
install -m 0644 "$REPO_ROOT/runtime/llamacpp/upstream.lock.json" "$STAGE/usr/share/doc/$PACKAGE/upstream.lock.json"

INSTALLED_SIZE=$(du -sk "$STAGE/usr" | awk '{print $1}')
cat > "$STAGE/DEBIAN/control" <<EOF
Package: $PACKAGE
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Maintainer: CakeOS Project
Depends: python3, libc6, libstdc++6, libgcc-s1, libgomp1
Installed-Size: $INSTALLED_SIZE
Description: local llama.cpp inference runtime for Haven/CakeOS
 Haven-owned Unix-socket inference broker and model lifecycle tools with a
 pinned llama.cpp CPU server. The package contains no model weights and does
 not enable the user service automatically.
EOF
chmod 0644 "$STAGE/DEBIAN/control"

DEB_NAME="${PACKAGE}_${VERSION}_${ARCH}.deb"
DEB_PATH="$OUTPUT_DIR/$DEB_NAME"
rm -f "$DEB_PATH" "$DEB_PATH.sha256"
dpkg-deb --root-owner-group --build "$STAGE" "$DEB_PATH" >/dev/null

test "$(dpkg-deb --field "$DEB_PATH" Package)" = "$PACKAGE"
test "$(dpkg-deb --field "$DEB_PATH" Version)" = "$VERSION"
test "$(dpkg-deb --field "$DEB_PATH" Architecture)" = "$ARCH"
if dpkg-deb --contents "$DEB_PATH" | grep -E '/models/|\.gguf($| )' >/dev/null; then
  echo 'Package unexpectedly contains a model path or GGUF.' >&2
  exit 68
fi
(
  cd "$OUTPUT_DIR"
  sha256sum "$DEB_NAME" > "$DEB_NAME.sha256"
)
printf '%s\n' "$DEB_PATH"
