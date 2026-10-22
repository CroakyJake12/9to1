#!/usr/bin/env bash
# Thunder shell-fallback failure-JSON fixture (bounded, no network/SDK/apt/native).
#
# Sources the ACTUAL production driver `.github/scripts/thunder-cuda-compile.sh` (main-guarded, so the
# build/network main is never executed) and proves the EXIT-trap shell-only fallback (used when Python
# is unavailable) emits a genuinely parseable JSON failure record:
#   - python3 is hidden from PATH so the `thunder_on_exit` shell fallback path is taken;
#   - a stage value containing a double-quote and backslash round-trips correctly (JSON escaping);
#   - the original nonzero exit code is preserved and `pythonUnavailable` is recorded.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DIR/../../.." && pwd)"
source "$REPO_ROOT/.github/scripts/thunder-cuda-compile.sh"

tmp="$(mktemp -d)"
original_path="$PATH"
trap 'export PATH="$original_path"; rm -rf "$tmp"' EXIT

# Minimal PATH with ONLY mkdir (symlinked) and no python3 anywhere.
bindir="$tmp/bin"
mkdir -p "$bindir"
ln -s /usr/bin/mkdir "$bindir/mkdir"
export PATH="$bindir"

REPO="$tmp/repo"
EVIDENCE="$tmp/ev"
STAGE='boot"strap\python'   # exercises quote + backslash escaping
LOG="$tmp/ev/thunder-cuda-compile.log"
mkdir -p "$REPO" "$EVIDENCE"

# Force a nonzero exit status (errexit temporarily relaxed; the failing subshell sets $? without the
# `set -e` builtin resetting it) then run the EXIT-trap failure path.
set +e
( exit 1 )
thunder_on_exit
set -e

# Restore PATH and parse the record with python (available again).
export PATH="$original_path"
f="$EVIDENCE/thunder-cuda-compile-failure.json"
[ -s "$f" ] || { echo "FAIL: failure json not written"; exit 1; }
python3 - "$f" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
assert d["workflow"] == "haven-chat-model-ci.yml"
assert d["job"] == "thunder-cuda-compile"
assert d["stage"] == 'boot"strap\\python'          # escaped round-trip
assert d["exitCode"] == 1                           # original nonzero preserved
assert d["success"] is False
assert d["pythonUnavailable"] is True
assert d["compileOnly"]["gpu"] == "NOT RUN"
assert isinstance(d["log"], str) and d["log"].endswith("thunder-cuda-compile.log")
PY

# --- Scenario B: an interpreter PRESENT but lacking json must also take the shell fallback (the
# Python writer would fail); we must not emit malformed JSON or lose the original rc. -------------
fakepy="$tmp/fakepy"
cat > "$fakepy" <<'PY'
#!/usr/bin/env bash
echo "ModuleNotFoundError: No module named 'json'" >&2
exit 1
PY
chmod +x "$fakepy"
export THUNDER_PYTHON_BIN="$fakepy"
export PATH="$original_path"   # restore normal tools; fakepy is chosen explicitly

EVIDENCE2="$tmp/ev2"; REPO2="$tmp/repo2"; LOG2="$tmp/ev2/log.txt"
mkdir -p "$EVIDENCE2" "$REPO2"
EVIDENCE="$EVIDENCE2"; REPO="$REPO2"; LOG="$LOG2"; STAGE='preflight'

set +e
( exit 3 )
thunder_on_exit
set -e

f2="$EVIDENCE2/thunder-cuda-compile-failure.json"
[ -s "$f2" ] || { echo "FAIL: fallback json not written for json-missing interpreter"; exit 1; }
python3 - "$f2" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
assert d["exitCode"] == 3, d
assert d["pythonUnavailable"] is True, d
assert d["stage"] == "preflight", d
assert d["compileOnly"]["gpu"] == "NOT RUN", d
PY
echo "OK: present-but-json-missing writer fell back to valid JSON without losing rc"

echo "THUNDER-SHELL-FAILURE-JSON-FIXTURE-OK"
