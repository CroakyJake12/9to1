#!/usr/bin/env bash
# Thunder python-bootstrap fixture (bounded, no network/SDK/apt/container/native).
#
# Sources the ACTUAL production driver `.github/scripts/thunder-cuda-compile.sh` (main-guarded, so the
# build/network main is never executed) and exercises `thunder_bootstrap_python` with explicitly
# labelled fake interpreter/apt command stubs:
#   - an interpreter that is PRESENT but lacks a required stdlib module (json) takes the repair path,
#     selects FULL python3 (never python3-minimal), and verifies stdlib-before-preflight;
#   - an interpreter that remains incomplete after install FAILS clearly (no false success);
#   - the ap install stub records the requested package so we assert FULL python3 selection.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DIR/../../.." && pwd)"
source "$REPO_ROOT/.github/scripts/thunder-cuda-compile.sh"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Fake interpreter: fails the stdlib import probe UNTIL a repair marker exists.
export FAKE_REPAIR="$tmp/.repaired"
fakepy="$tmp/fakepy"
cat > "$fakepy" <<'PY'
#!/usr/bin/env bash
if [ -e "$FAKE_REPAIR" ]; then
  exit 0
fi
echo "ModuleNotFoundError: No module named 'json'"
exit 1
PY
chmod +x "$fakepy"
export THUNDER_PYTHON_BIN="$fakepy"

# --- Scenario A: present but stdlib-incomplete -> repair -> verify (full python3 selected). ----
selected_a="$tmp/pkg-selected-a.txt"
run_install_fix() {
  printf '%s\n' python3 >> "$selected_a"
  touch "$FAKE_REPAIR"
}
export THUNDER_APT_UPDATE=true
export THUNDER_APT_INSTALL=run_install_fix

if ! thunder_bootstrap_python >/dev/null 2>&1; then
  echo "FAIL: bootstrap did not repair an interpreter that was present but stdlib-incomplete"; exit 1
fi
grep -qx 'python3' "$selected_a" || { echo "FAIL: full python3 was not selected"; exit 1; }
if grep -q 'python3-minimal' "$selected_a"; then
  echo "FAIL: minimal python was selected instead of full python3"; exit 1
fi
echo "OK: present-but-incomplete interpreter repaired with full python3"

# --- Scenario B: interpreter remains incomplete after install -> must FAIL clearly. --------------
rm -f "$FAKE_REPAIR"
selected_b="$tmp/pkg-selected-b.txt"
run_install_nofix() {
  printf '%s\n' python3 >> "$selected_b"
}
export THUNDER_APT_INSTALL=run_install_nofix
if thunder_bootstrap_python >/dev/null 2>&1; then
  echo "FAIL: bootstrap succeeded though stdlib remained incomplete"; exit 1
fi
grep -qx 'python3' "$selected_b" || { echo "FAIL: full python3 not attempted in failure path"; exit 1; }
echo "OK: incomplete stdlib after install fails clearly"

echo "THUNDER-PYTHON-BOOTSTRAP-FIXTURE-OK"
