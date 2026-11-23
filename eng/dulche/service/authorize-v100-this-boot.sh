#!/bin/sh
# Install the reviewed helper in volatile root-owned storage, then launch only the control socket.
# The account owner runs this from their ordinary terminal. sudo prompts locally if required.
set -eu
[ "$(id -u)" -ge 1000 ] || { echo 'Run from the ordinary desktop account.' >&2; exit 1; }
base=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd)
account=$(id -un)
sha=c693af764b81198ee58ada09a9ca8c20eab301e45853b858653ea857dd1914d8
src="$base/v100_authority.py"
target=/run/dulche-v100/authority.py
/usr/bin/python3 -I -S -B "$src" --inspect
printf '%s  %s\n' "$sha" "$src" | /usr/bin/sha256sum --check --status || { echo 'Source digest mismatch.' >&2; exit 1; }
if /usr/bin/systemctl is-active --quiet dulche-v100-authority.service; then
    echo 'The per-boot V100 authority is already running.'
    exit 0
fi
/usr/bin/sudo /usr/bin/install -d -o root -g root -m 0755 /run/dulche-v100
/usr/bin/sudo /usr/bin/install -o root -g root -m 0555 -- "$src" "$target"
printf '%s  %s\n' "$sha" "$target" | /usr/bin/sha256sum --check --status || { echo 'Installed digest mismatch.' >&2; exit 1; }
/usr/bin/sudo /usr/bin/systemd-run --unit=dulche-v100-authority --collect \
    -p Type=exec -p Restart=no -p NoNewPrivileges=yes -p PrivateTmp=yes \
    -p ProtectHome=read-only -p ProtectSystem=strict -p ReadWritePaths=/run/dulche-v100 \
    -p UMask=0077 '-p' 'RestrictAddressFamilies=AF_UNIX AF_NETLINK' \
    /usr/bin/python3 -I -S -B "$target" --serve-account "$account"
echo 'Temporary authority installed for this boot. No GPU has been attached.'
