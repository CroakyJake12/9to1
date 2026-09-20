#!/usr/bin/env bash
set -euo pipefail

# Read-only acceptance harness for the first CakeOS Wine compatibility slice.
# This script does not install packages, enable services, modify the VM, create
# Wine prefixes, or launch Windows applications. It renders the packaging unit
# into a temporary directory, validates it, and runs the broker's read-only
# prerequisite audit.

usage() {
  cat <<'EOF'
Usage: acceptance.sh [--source-root PATH] [--runtime-root PATH]

Runs read-only host/session acceptance checks for the CakeOS Wine compatibility
slice. The script never installs packages, enables services, or launches Wine.

Options:
  --source-root PATH   Repository/install root containing compatibility/.
                       Defaults to the repository root inferred from this file.
  --runtime-root PATH  Managed Wine runtime directory passed to the audit.
                       Defaults to the broker's normal XDG data location.
  -h, --help           Show this help text.
EOF
}

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

note() {
  printf 'INFO: %s\n' "$*"
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command is unavailable: $1"
}

SOURCE_ROOT=""
RUNTIME_ROOT=""
while (($#)); do
  case "$1" in
    --source-root)
      (($# >= 2)) || fail '--source-root requires a path'
      SOURCE_ROOT=$2
      shift 2
      ;;
    --runtime-root)
      (($# >= 2)) || fail '--runtime-root requires a path'
      RUNTIME_ROOT=$2
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

[[ "$(uname -s)" == "Linux" ]] || fail 'acceptance harness requires Linux'
[[ "$(id -u)" -ne 0 ]] || fail 'acceptance harness must run as an unprivileged desktop user'

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
if [[ -z "$SOURCE_ROOT" ]]; then
  SOURCE_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd -P)
else
  SOURCE_ROOT=$(CDPATH= cd -- "$SOURCE_ROOT" && pwd -P)
fi

[[ -d "$SOURCE_ROOT/compatibility/wine/haven_compat" ]] || fail 'source root does not contain the compatibility broker'
[[ -f "$SOURCE_ROOT/compatibility/wine/systemd/haven-compatd.service.in" ]] || fail 'systemd service template is missing'

require_command python3
require_command systemd-analyze
require_command systemctl
require_command systemd-run
require_command journalctl
require_command env

[[ -n "${XDG_RUNTIME_DIR:-}" ]] || fail 'XDG_RUNTIME_DIR is not set'
[[ -d "$XDG_RUNTIME_DIR" ]] || fail 'XDG_RUNTIME_DIR does not exist'
[[ -O "$XDG_RUNTIME_DIR" ]] || fail 'XDG_RUNTIME_DIR is not owned by the current user'

systemctl --user show-environment >/dev/null 2>&1 || fail 'per-user systemd manager is not reachable'

TEMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/cakeos-wine-acceptance.XXXXXX")
cleanup() {
  rm -rf -- "$TEMP_DIR"
}
trap cleanup EXIT HUP INT TERM

TEMPLATE="$SOURCE_ROOT/compatibility/wine/systemd/haven-compatd.service.in"
RENDERED="$TEMP_DIR/haven-compatd.service"
PYTHON_PATH=$(command -v python3)
DOC_ROOT="$SOURCE_ROOT"

# Render only into the private temporary directory. Do not install or enable it.
sed \
  -e "s|@PYTHON@|$PYTHON_PATH|g" \
  -e "s|@SOURCE_ROOT@|$SOURCE_ROOT|g" \
  -e "s|@DOC_ROOT@|$DOC_ROOT|g" \
  "$TEMPLATE" > "$RENDERED"

if grep -Eq '@(PYTHON|SOURCE_ROOT|DOC_ROOT)@' "$RENDERED"; then
  fail 'rendered service still contains unresolved packaging placeholders'
fi

systemd-analyze --user verify "$RENDERED"
note 'systemd user service template verified'

AUDIT_ARGS=(audit)
if [[ -n "$RUNTIME_ROOT" ]]; then
  RUNTIME_ROOT=$(CDPATH= cd -- "$RUNTIME_ROOT" && pwd -P)
  AUDIT_ARGS+=(--runtime-root "$RUNTIME_ROOT")
fi

AUDIT_JSON="$TEMP_DIR/audit.json"
PYTHONPATH="$SOURCE_ROOT${PYTHONPATH:+:$PYTHONPATH}" \
  python3 -m compatibility.wine.haven_compat.cli "${AUDIT_ARGS[@]}" > "$AUDIT_JSON"

python3 - "$AUDIT_JSON" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
with path.open('r', encoding='utf-8') as handle:
    facts = json.load(handle)

preflight = facts.get('preflight', {})
wine = preflight.get('wineSlice1', {})
future = preflight.get('winboatFuture', {})

print('Wine slice 1 prerequisites present:', bool(wine.get('prerequisitesPresent')))
missing = wine.get('missing', [])
print('Wine missing:', ', '.join(missing) if missing else 'none')
print('WinBoat future prerequisites present:', bool(future.get('prerequisitesPresent')))

# A failed Wine preflight is evidence, not a reason to mutate the machine.
# Return a dedicated code so callers can distinguish prerequisite absence from
# a broken acceptance harness.
if not wine.get('prerequisitesPresent'):
    raise SystemExit(20)
PY

note 'Wine slice 1 read-only prerequisite gate passed'
note 'No packages, services, VM settings, prefixes, or Windows applications were changed'
