#!/usr/bin/env bash
set -euo pipefail

# Smoke tests for HavenOS Phase 2B apps
# Run inside the HavenOS VM after package installation

ROOT=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
RESULTS_DIR="$ROOT/artifacts/smoke-results"
mkdir -p "$RESULTS_DIR"

log() { echo "[$(date -Iseconds)] $*"; }
fail() { log "FAIL: $*"; echo "$*" >> "$RESULTS_DIR/failures.log"; }
pass() { log "PASS: $*"; echo "$*" >> "$RESULTS_DIR/passes.log"; }

# Initialize results
> "$RESULTS_DIR/failures.log"
> "$RESULTS_DIR/passes.log"

# Test helper
run_test() {
  local name="$1"
  local cmd="$2"
  local expected_pattern="${3:-}"
  local timeout="${4:-30}"

  log "Testing: $name"
  if timeout "$timeout" bash -c "$cmd" >"$RESULTS_DIR/$name.stdout" 2>"$RESULTS_DIR/$name.stderr"; then
    if [[ -n "$expected_pattern" ]] && ! grep -q "$expected_pattern" "$RESULTS_DIR/$name.stdout"; then
      fail "$name: expected pattern '$expected_pattern' not found in output"
      cat "$RESULTS_DIR/$name.stdout"
      return 1
    fi
    pass "$name"
    return 0
  else
    local exit_code=$?
    fail "$name: command failed with exit code $exit_code"
    cat "$RESULTS_DIR/$name.stdout"
    cat "$RESULTS_DIR/$name.stderr"
    return 1
  fi
}

# 1. Test haven-welcome (already verified)
run_test "haven-welcome-version" "haven-welcome --version" "0.1.0"

# 2. Test havenos-shell (extension)
run_test "havenos-shell-extension" "gnome-extensions info haven-shell@havenos" "haven-shell@havenos"

# 3. Test havenos-studio
run_test "havenos-studio-version" "havenos-studio --version" "0.1.0"

# 4. Test havenos-data
run_test "havenos-data-version" "haven-data --version 2>&1 || true" "0.1.0"
run_test "havenos-data-calc-worker" "haven-data-calc-worker --help 2>&1 | head -5" ""
run_test "havenos-data-duckdb-worker" "haven-data-duckdb-worker --help 2>&1 | head -5" ""

# 5. Test havenos-present-engine
run_test "havenos-present-engine-worker" "cakeos-present-worker --version 2>&1 | head -3" ""

# 6. Test havenos-write
run_test "havenos-write-version" "haven-write --version 2>&1 || true" "0.1.0"
run_test "havenos-write-hui" "haven-write-hui --version 2>&1 || true" "0.1.0"

# 7. Test havenos-present
run_test "havenos-present-version" "haven-present --version 2>&1 || true" "0.1.0"
run_test "havenos-present-hui" "haven-present-hui --version 2>&1 || true" "0.1.0"

# 8. Test havenos-plan
run_test "havenos-plan-version" "haven-plan --version 2>&1 || true" "0.1.0"
run_test "havenos-plan-hui" "haven-plan-hui --version 2>&1 || true" "0.1.0"

# 9. Test havenos-terminal
run_test "havenos-terminal-version" "haven-terminal --version 2>&1 || true" "0.1.0"
run_test "havenos-terminal-hui" "haven-terminal-hui --version 2>&1 || true" "0.1.0"

# 10. Test havenos-dev
run_test "havenos-dev-version" "haven-dev --version 2>&1 || true" "0.1.0"
run_test "havenos-dev-hui" "haven-dev-hui --version 2>&1 || true" "0.1.0"

# 11. Test havenos-wave
run_test "havenos-wave-version" "haven-wave --version 2>&1 || true" "0.1.0"
run_test "havenos-wave-hui" "haven-wave-hui --version 2>&1 || true" "0.1.0"

# 12. Test havenos-wine
run_test "havenos-wine-version" "haven-wine --version 2>&1 || true" "0.1.0"
run_test "havenos-wine-hui" "haven-wine-hui --version 2>&1 || true" "0.1.0"

# 13. Test havenos-wine-compat
run_test "havenos-wine-compat-service" "systemctl --user list-unit-files | grep haven-compatd" "haven-compatd"

# 14. Test canvas-rnote
run_test "canvas-rnote-binary" "cakeos-canvas-rnote --version 2>&1 | head -3" ""

# 15. Test WinBoat (if available)
if command -v haven-winboat >/dev/null 2>&1; then
  run_test "havenos-winboat-version" "haven-winboat --version 2>&1 | head -3" ""
fi

# Summary
log "=== Smoke Test Summary ==="
pass_count=$(wc -l < "$RESULTS_DIR/passes.log")
fail_count=$(wc -l < "$RESULTS_DIR/failures.log")
log "Passed: $pass_count"
log "Failed: $fail_count"

if [[ $fail_count -gt 0 ]]; then
  log "FAILURES:"
  cat "$RESULTS_DIR/failures.log"
  exit 1
fi

log "All smoke tests passed!"
exit 0