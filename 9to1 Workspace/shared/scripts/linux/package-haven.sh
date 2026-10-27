#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <published-linux-runtime> <output-directory> [version]" >&2
    exit 2
}

[[ $# -ge 2 && $# -le 3 ]] || usage
runtime_dir=$(realpath "$1")
output_dir=$(realpath -m "$2")
version="${3:-0.1.0}"
entrypoint="$runtime_dir/Haven"

[[ -d "$runtime_dir" ]] || { echo "Published runtime directory is missing: $runtime_dir" >&2; exit 1; }
[[ -x "$entrypoint" ]] || { echo "Linux Haven entrypoint is missing or not executable: $entrypoint" >&2; exit 1; }
for tool in dpkg dpkg-deb python3; do
    command -v "$tool" >/dev/null 2>&1 || { echo "Ubuntu packaging requires $tool." >&2; exit 1; }
done
[[ "$version" =~ ^[0-9][a-zA-Z0-9.+:~\-]*$ ]] && dpkg --validate-version "$version" || {
    echo "Invalid Debian package version: $version" >&2; exit 1;
}
[[ "$output_dir" != "$runtime_dir" && "$output_dir" != "$runtime_dir/"* ]] || {
    echo "Package output must be outside the published runtime." >&2; exit 1;
}

# This packager owns the ordinary, non-single-file linux-x64 publish layout.
# Never mislabel a framework-dependent, foreign-architecture or unbound apphost.
python3 - "$runtime_dir" <<'VALIDATE'
import json
import pathlib
import struct
import sys

root = pathlib.Path(sys.argv[1])
def require(condition, message):
    if not condition:
        raise SystemExit("Invalid Haven publish: " + message)

host = root / "Haven"
require(not host.is_symlink(), "Haven apphost must be a regular file")
data = host.read_bytes()
require(len(data) >= 64 and data[:6] == b"\x7fELF\x02\x01" and
        struct.unpack_from("<H", data, 18)[0] == 62 and
        struct.unpack_from("<H", data, 16)[0] in (2, 3), "Haven must be an x86-64 Linux ELF apphost")
require(b"Haven.dll\0" in data, "apphost must be bound to Haven.dll")
for name in ("Haven.dll", "Haven.deps.json", "Haven.runtimeconfig.json", "libcoreclr.so",
             "libhostfxr.so", "libhostpolicy.so", "System.Private.CoreLib.dll"):
    path = root / name
    require(path.is_file() and not path.is_symlink() and path.stat().st_size > 0,
            "missing self-contained runtime file: " + name)
try:
    options = json.loads((root / "Haven.runtimeconfig.json").read_text())["runtimeOptions"]
    deps = json.loads((root / "Haven.deps.json").read_text())
    require(not options.get("framework") and not options.get("frameworks"),
            "framework-dependent runtime configuration is unsupported; publish --self-contained true")
    require(any(item.get("name") == "Microsoft.NETCore.App" for item in options.get("includedFrameworks", [])),
            "self-contained .NET runtime declaration is missing")
    require(deps["runtimeTarget"]["name"].endswith("/linux-x64"), "dependency target must be linux-x64")
except (AttributeError, KeyError, TypeError, ValueError) as error:
    raise SystemExit("Invalid Haven publish metadata: " + str(error)) from error
VALIDATE

mkdir -p "$output_dir"
package="$output_dir/haven_${version}_amd64.deb"
[[ ! -e "$package" && ! -L "$package" ]] || { echo "Refusing to overwrite existing package: $package" >&2; exit 1; }
stage=$(mktemp -d "$output_dir/.haven-package-stage-${version}.XXXXXX")
mkdir -p "$stage/DEBIAN" "$stage/usr/lib/haven" "$stage/usr/bin" "$stage/usr/share/applications"
cp -a "$runtime_dir"/. "$stage/usr/lib/haven/"

cat > "$stage/DEBIAN/control" <<CONTROL
Package: haven
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Haven
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libfontconfig1, libfreetype6, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libice6, libsm6, libgl1, libegl1, libdbus-1-3, libgomp1, libasound2t64 | libasound2
Description: Haven desktop workspace
CONTROL

cat > "$stage/usr/bin/haven" <<'LAUNCHER'
#!/usr/bin/env bash
set -u
runtime=/usr/lib/haven/Haven
if [[ ! -x "$runtime" ]]; then
    echo "Haven runtime unavailable; the launcher is leaving the session unchanged." >&2
    exit 127
fi
exec "$runtime" "$@"
LAUNCHER
chmod 0755 "$stage/usr/bin/haven"

cat > "$stage/usr/share/applications/haven.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Haven
Comment=Open the Haven desktop workspace
TryExec=haven
Exec=haven %U
Terminal=false
Categories=Utility;Office;
DESKTOP

# Publish directories remain private under the caller's umask, while installed
# application/control files must be readable and executables traversable.
chmod -R a+rX "$stage"

# Build privately, then publish without overwriting a concurrently created file.
package_temporary=$(mktemp "$output_dir/.haven-package-XXXXXX.deb")
trap 'rm -f -- "$package_temporary"' EXIT
dpkg-deb --build --root-owner-group "$stage" "$package_temporary"
ln -- "$package_temporary" "$package"
rm -- "$package_temporary"
trap - EXIT
echo "Created $package"
echo "Staging retained for review at $stage"
