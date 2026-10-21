#!/usr/bin/env bash
# Thunder install-closure inventory fixture (bounded, no network/SDK/native/GPU).
#
# Sources the ACTUAL production driver `.github/scripts/thunder-cuda-compile.sh` (main-guarded: only
# its functions are imported, the build/network main is never executed) and exercises
# `thunder_inventory_install` against clearly-labelled NON-EXECUTED file-metadata trees (tiny empty
# placeholder files; never run or claimed as artifacts):
#   - a complete named closure (engine, worker, 4 helper/lock files, licenses, receipt) succeeds and
#     writes a sorted path/size/SHA-256 inventory;
#   - a missing notice/receipt fails;
#   - a model-like file (.gguf) fails;
#   - a symlink escape fails.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DIR/../../.." && pwd)"
source "$REPO_ROOT/.github/scripts/thunder-cuda-compile.sh"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Build a complete (but non-executed) install closure tree.
make_complete() {
  local install="$1"
  mkdir -p "$install/runtimes/linux-x64/engine" \
           "$install/runtimes/linux-x64/native" \
           "$install/licenses"
  : > "$install/runtimes/linux-x64/engine/strata"
  : > "$install/runtimes/linux-x64/engine/libstrata_kq.so"
  : > "$install/runtimes/linux-x64/native/dulche-strata-candidate-worker"
  : > "$install/runtimes/linux-x64/native/strata_tokenizer_helper.py"
  : > "$install/runtimes/linux-x64/native/strata_tokenizer.py"
  : > "$install/runtimes/linux-x64/native/gguf_reader.py"
  : > "$install/runtimes/linux-x64/native/strata-runtime-dependencies.json"
  : > "$install/licenses/MIT-STRATA.txt"
  : > "$install/licenses/MIT-GGML.txt"
  : > "$install/strata-build-receipt.json"
}

# 1. Complete closure -> success + sorted inventory.
install_a="$tmp/install-a"
make_complete "$install_a"
out_a="$tmp/inventory-a.json"
if ! thunder_inventory_install "$install_a" "$out_a" >/dev/null 2>&1; then
  echo "FAIL: complete closure inventory returned nonzero"; exit 1
fi
if [ ! -s "$out_a" ]; then
  echo "FAIL: inventory output not written"; exit 1
fi
python3 - "$out_a" <<'PY'
import json, sys
items = json.load(open(sys.argv[1]))
paths = [it["path"] for it in items]
assert len(items) == 10, "expected 10 named closure files, got %d" % len(items)
assert paths == sorted(paths), "inventory not sorted"
assert all({"bytes", "sha256"} <= set(it) for it in items), "missing size/sha metadata"
for rel in ("runtimes/linux-x64/engine/strata",
            "runtimes/linux-x64/engine/libstrata_kq.so",
            "runtimes/linux-x64/native/dulche-strata-candidate-worker",
            "runtimes/linux-x64/native/strata_tokenizer_helper.py",
            "runtimes/linux-x64/native/strata_tokenizer.py",
            "runtimes/linux-x64/native/gguf_reader.py",
            "runtimes/linux-x64/native/strata-runtime-dependencies.json",
            "licenses/MIT-STRATA.txt", "licenses/MIT-GGML.txt",
            "strata-build-receipt.json"):
    assert rel in paths, "missing %s" % rel
PY
echo "OK: complete closure inventory (10 named files)"

# 2. Missing notice/receipt -> fail.
install_b="$tmp/install-b"
make_complete "$install_b"
rm -f "$install_b/licenses/MIT-GGML.txt" "$install_b/strata-build-receipt.json"
out_b="$tmp/inventory-b.json"
if thunder_inventory_install "$install_b" "$out_b" >/dev/null 2>&1; then
  echo "FAIL: missing notice/receipt accepted"; exit 1
fi
# 3. Model-like file -> fail.
install_c="$tmp/install-c"
make_complete "$install_c"
: > "$install_c/private.bin"
out_c="$tmp/inventory-c.json"
if thunder_inventory_install "$install_c" "$out_c" >/dev/null 2>&1; then
  echo "FAIL: model-like file accepted"; exit 1
fi
# 4. Symlink escape -> fail.
install_d="$tmp/install-d"
make_complete "$install_d"
ln -s /etc/hostname "$install_d/escape"
out_d="$tmp/inventory-d.json"
if thunder_inventory_install "$install_d" "$out_d" >/dev/null 2>&1; then
  echo "FAIL: symlink escape accepted"; exit 1
fi

# 5. Missing K-quant DSO -> fail independently of notices/receipt.
install_e="$tmp/install-e"
make_complete "$install_e"
rm -f "$install_e/runtimes/linux-x64/engine/libstrata_kq.so"
if thunder_inventory_install "$install_e" "$tmp/inventory-e.json" >/dev/null 2>&1; then
  echo "FAIL: missing K-quant DSO accepted"; exit 1
fi

echo "THUNDER-INVENTORY-FIXTURE-OK"
