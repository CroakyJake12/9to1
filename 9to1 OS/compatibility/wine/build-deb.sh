#!/usr/bin/env bash
set -euo pipefail
source_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
output=${1:-"$source_dir/../../../artifacts/packages"}
mkdir -p "$output"
output=$(cd -- "$output" && pwd)
staging=$(mktemp -d)
trap 'rm -rf -- "$staging"' EXIT
mkdir -p "$staging/usr/lib/9to1/compatibility/wine" "$staging/usr/bin" \
  "$staging/usr/share/applications" "$staging/usr/share/doc/9to1-windows-compat" "$staging/DEBIAN"
cp -R "$source_dir/haven_compat" "$staging/usr/lib/9to1/compatibility/wine/"
python3 - "$staging" <<'PY'
from pathlib import Path
import shutil, sys
for path in Path(sys.argv[1]).rglob('__pycache__'):
    shutil.rmtree(path)
PY
cat > "$staging/usr/bin/9to1-open-exe" <<'EOF'
#!/bin/sh
export PYTHONPATH=/usr/lib/9to1
exec /usr/bin/python3 -m compatibility.wine.haven_compat.chooser "$@"
EOF
cat > "$staging/usr/bin/9to1-compat" <<'EOF'
#!/bin/sh
export PYTHONPATH=/usr/lib/9to1
exec /usr/bin/python3 -m compatibility.wine.haven_compat.cli "$@"
EOF
cat > "$staging/DEBIAN/control" <<'EOF'
Package: 9to1-windows-compat
Version: 0.1.0
Architecture: all
Maintainer: 9to1 Project
Depends: python3 (>= 3.10), python3-gi, gir1.2-gtk-4.0, bubblewrap, xdg-utils, systemd
Recommends: freerdp3-x11
Section: utils
Priority: optional
Description: 9to1 Windows executable chooser and managed Wine broker
 Experimental desktop integration for native Wine and ARM64 Hangover/FEX.
 WinBoat connections use an explicitly configured, already running Windows guest.
EOF
install -m 0644 "$source_dir/desktop/org.ninetone.ExeChooser.desktop" "$staging/usr/share/applications/"
install -m 0644 "$source_dir/../../../LICENSE" "$staging/usr/share/doc/9to1-windows-compat/copyright"
install -m 0644 "$source_dir/EXE-CHOOSER.md" "$staging/usr/share/doc/9to1-windows-compat/README.md"
chmod -R u=rwX,go=rX "$staging"
chmod 0755 "$staging/usr/bin/9to1-open-exe" "$staging/usr/bin/9to1-compat"
dpkg-deb --root-owner-group --build "$staging" "$output/9to1-windows-compat_0.1.0_all.deb"
