#!/usr/bin/env bash
# Thunder pass-21 compile-only CUDA CI driver (lane 6).
#
# Runs INSIDE the pinned nvidia/cuda:13.0.2-devel-ubuntu24.04 container with the repository checkout
# bind-mounted at $REPO. It acquires + verifies the exact candidate source archive and the full
# llama.cpp/ggml checkout, builds the candidate relay engine through the EXISTING NativeStrata
# CMake + build-package.py entrypoint, and assembles the isolated install closure + compile evidence.
# Compile-only: GPU/driver/model are NOT run here.
#
# Env injected by the workflow (all values are owner/pinned constants, never user-typed):
#   REPO           absolute path of the checkout inside the container
#   CANDIDATE_LOCK relative path (from REPO) of the candidate lock
#   ARCHIVE_URL     pinned immutable COMMIT archive URL
#   ARCHIVE_SHA256  pinned archive SHA-256
#   GGML_REPO       pinned llama.cpp repository
#   GGML_COMMIT     pinned llama.cpp commit
#   GGML_TREE       pinned llama.cpp tree
#   CUDA_IMAGE      pinned CUDA image reference (for recording)
#
# The build/install dirs must be ABSENT before the builder runs (build-package.py refuses to take
# ownership of existing output). No git global config / identity is set; only repo-scoped
# safe.directory is used where required.

set -euo pipefail

# ------------------------------------------------------------------------------------------------
# Path preparation (pure function, main-guarded so it can be sourced by a bounded shell fixture).
# Creates ONLY the shared WORK/EVIDENCE/dependency-parent dirs. Build/install must be fresh/absent;
# existing output is never reused.
# ------------------------------------------------------------------------------------------------
thunder_prepare_paths() {
  local work="$1" install="$2" evidence="$3" deps_parent="$4"
  mkdir -p "$work" "$evidence" "$deps_parent"
  # build-work lives at $work/build-work; the builder requires it absent.
  if [ -e "$work/build-work" ]; then
    echo "FATAL: build-work path already exists (builder refuses reuse): $work/build-work" >&2
    return 1
  fi
  if [ -e "$install" ]; then
    echo "FATAL: install path already exists (builder refuses reuse): $install" >&2
    return 1
  fi
}

# ------------------------------------------------------------------------------------------------
# Install-closure inventory (pure function, main-guarded so it can be exercised by a bounded shell
# fixture). Verifies the complete named closure (engine, worker, 4 helper/lock files, licenses,
# receipt), rejects symlinks/path escape/model-like files, and writes a sorted path/size/SHA-256
# inventory. The fixture uses tiny NON-EXECUTED file-metadata files; no binary/runtime/GPU proof.
# ------------------------------------------------------------------------------------------------
thunder_inventory_install() {
  local install="$1" out_json="$2"
  python3 - "$install" "$out_json" <<'PY'
import hashlib, json, pathlib, sys

install = pathlib.Path(sys.argv[1]); out = pathlib.Path(sys.argv[2])
expected = [
    "runtimes/linux-x64/engine/strata",
    "runtimes/linux-x64/engine/libstrata_kq.so",
    "runtimes/linux-x64/native/dulche-strata-candidate-worker",
    "runtimes/linux-x64/native/strata_tokenizer_helper.py",
    "runtimes/linux-x64/native/strata_tokenizer.py",
    "runtimes/linux-x64/native/gguf_reader.py",
    "runtimes/linux-x64/native/strata-runtime-dependencies.json",
    "licenses/MIT-STRATA.txt",
    "licenses/MIT-GGML.txt",
    "strata-build-receipt.json",
]
missing = [e for e in expected if not (install / e).exists()]
if missing:
    print("FATAL: install closure missing: " + ", ".join(missing))
    sys.exit(1)

MODEL_EXTS = {".gguf", ".bin", ".safetensors", ".pt", ".onnx", ".ckpt", ".pdparams", ".pth"}
root_resolved = install.resolve()
items = []
for p in sorted(install.rglob("*")):
    if p.is_symlink():
        print("FATAL: symlink in install closure: " + str(p)); sys.exit(1)
    if p.is_file():
        r = p.resolve()
        if not str(r).startswith(str(root_resolved)):
            print("FATAL: path escape in install closure: " + str(p)); sys.exit(1)
        if p.suffix.lower() in MODEL_EXTS:
            print("FATAL: model-like file in install closure: " + str(p)); sys.exit(1)
        rel = p.relative_to(install).as_posix()
        items.append({"path": rel, "bytes": p.stat().st_size,
                      "sha256": hashlib.sha256(p.read_bytes()).hexdigest()})
out.write_text(json.dumps(items, indent=2) + "\n")
PY
}

# ------------------------------------------------------------------------------------------------
# POSIX shell JSON string escaper (used by the EXIT-trap shell fallback when Python is unavailable).
# Escapes backslash, double-quote, tab, LF, CR and wraps the result in double quotes so the emitted
# record is genuinely parseable JSON. Pure function; no python/network/apt.
# ------------------------------------------------------------------------------------------------
thunder_json_str() {
  local s="$1"
  s=${s//\\/\\\\}
  s=${s//\"/\\\"}
  s=${s//$'\t'/\\t}
  s=${s//$'\n'/\\n}
  s=${s//$'\r'/\\r}
  printf '"%s"' "$s"
}

# ------------------------------------------------------------------------------------------------
# Python bootstrap boundary (pure; main-guarded so a no-network fixture can call it with explicitly
# labelled fake apt/interpreter command stubs). The minimal NVIDIA devel image must not be assumed to
# include python3 or the full stdlib; presence of an interpreter is NOT enough. We select FULL python3
# (never python3-minimal) and verify the required stdlib imports BEFORE any structured preflight/build
# metadata is emitted. Env overrides for the fixture only (never set in production):
#   THUNDER_PYTHON_BIN   interpreter used for probing (default: python3)
#   THUNDER_APT_UPDATE    apt update command (default: apt-get update -qq)
#   THUNDER_APT_INSTALL   full-python install command (default: apt-get install -y -qq
#                         --no-install-recommends python3)
# ------------------------------------------------------------------------------------------------
thunder_python_usable() {
  local py="${THUNDER_PYTHON_BIN:-python3}"
  if ! command -v "$py" >/dev/null 2>&1; then
    echo "THUNDER-PYTHON-MISSING" >&2
    return 1
  fi
  if ! "$py" -c 'import json, hashlib, os, pathlib, re, shutil, struct, subprocess, sys, tarfile, ssl' >/dev/null 2>&1; then
    echo "THUNDER-PYTHON-STDLIB-INCOMPLETE" >&2
    return 1
  fi
  return 0
}

thunder_install_python_full() {
  local upd="${THUNDER_APT_UPDATE:-apt-get update -qq}"
  local inst="${THUNDER_APT_INSTALL:-apt-get install -y -qq --no-install-recommends python3}"
  $upd || true
  $inst
}

thunder_bootstrap_python() {
  if thunder_python_usable >/dev/null 2>&1; then
    echo "THUNDER-PYTHON-OK"
    return 0
  fi
  STAGE="${STAGE:-bootstrap-python-install}"
  thunder_install_python_full || return 1
  # Repair path: after full install, verify stdlib really is present (an interpreter alone is not
  # enough). Fail clearly if the full package still does not supply the required stdlib.
  thunder_python_usable || return 1
}

# ------------------------------------------------------------------------------------------------
# EXIT trap: preserves the original status and emits a bounded failure receipt/log for ANY nonzero
# stage, including explicit `exit 1` and failures early in bootstrap/apt/preflight (the trap is
# installed before those steps). Never masks failure; a successful run sets THUNDER_DONE before
# returning. Falls back to a shell JSON record if Python is unavailable.
# ------------------------------------------------------------------------------------------------
thunder_on_exit() {
  local rc=$?
  if [ "${THUNDER_DONE:-0}" = "1" ]; then
    return 0
  fi
  mkdir -p "$EVIDENCE" 2>/dev/null || true
  # Prefer the structured Python writer, but only when its own required stdlib (json/pathlib) is
  # present AND the writer actually succeeds; otherwise fall back to the shell-only JSON record. The
  # original nonzero exit status is preserved either way (never lost via the fallback).
  if thunder_python_usable >/dev/null 2>&1 && python3 - "$rc" "$STAGE" "$EVIDENCE" "$REPO" "$LOG" <<'PY'
import json, os, pathlib, sys
rc = int(sys.argv[1]); stage = sys.argv[2]; ev = pathlib.Path(sys.argv[3])
repo = sys.argv[4]; log = sys.argv[5]
ev.mkdir(parents=True, exist_ok=True)
rec = {
    "workflow": "haven-chat-model-ci.yml",
    "job": "thunder-cuda-compile",
    "stage": stage,
    "exitCode": rc,
    "success": False,
    "compileOnly": {"gpu": "NOT RUN", "driver": "NOT RUN", "model": "NOT RUN"},
    "cudaImage": os.environ.get("CUDA_IMAGE", ""),
    "log": str(log),
}
sv = pathlib.Path(repo) / "work" / "pc-worker" / "thunder-cuda-compile" / "build-work" / "source-verification.json"
if sv.exists():
    try:
        rec["builderSourceVerification"] = json.loads(sv.read_text())
    except Exception:
        rec["builderSourceVerificationNote"] = "unreadable"
(ev / "thunder-cuda-compile-failure.json").write_text(json.dumps(rec, indent=2) + "\n")
print("THUNDER-CUDA-COMPILE-FAILED stage=%s exit=%s" % (stage, rc))
PY
  then
    :
  else
    # Shell-only fallback: Python is unavailable OR its writer stdlib is missing/incomplete. Every
    # string value is emitted via thunder_json_str so the record is valid JSON.
    printf '{"workflow":%s,"job":%s,"stage":%s,"exitCode":%s,"success":false,"compileOnly":{"gpu":%s,"driver":%s,"model":%s},"pythonUnavailable":true,"log":%s}\n' \
      "$(thunder_json_str "haven-chat-model-ci.yml")" \
      "$(thunder_json_str "thunder-cuda-compile")" \
      "$(thunder_json_str "$STAGE")" "$rc" \
      "$(thunder_json_str "NOT RUN")" "$(thunder_json_str "NOT RUN")" "$(thunder_json_str "NOT RUN")" \
      "$(thunder_json_str "$LOG")" \
      > "$EVIDENCE/thunder-cuda-compile-failure.json" 2>/dev/null || true
    echo "THUNDER-CUDA-COMPILE-FAILED stage=$STAGE exit=$rc (python writer unavailable)"
  fi
}

# ------------------------------------------------------------------------------------------------
# Container resource preflight: read ACTUAL /proc memory + enclosing cgroup limits (memory/cpu/pids)
# + df before compiling, fail clearly if inadequate, and preserve the measured limits as structured
# metadata (never raw env). Runs AFTER Python bootstrap (python3 is guaranteed present).
# ------------------------------------------------------------------------------------------------
thunder_resource_preflight() {
  python3 - "$EVIDENCE" <<'PY'
import json, pathlib, shutil, sys

ev = pathlib.Path(sys.argv[1])

def read_cgroup(name):
    try:
        return pathlib.Path(name).read_text().strip()
    except Exception:
        return None

def parse_limit(text):
    if text is None:
        return None
    text = text.strip()
    if text in ("", "max"):
        return None
    try:
        return int(text)
    except ValueError:
        return None

def mem_available_bytes():
    for line in open("/proc/meminfo"):
        if line.startswith("MemAvailable:"):
            return int(line.split()[1]) * 1024
    return None

avail = mem_available_bytes()
mem_max = parse_limit(read_cgroup("/sys/fs/cgroup/memory.max"))
mem_current = parse_limit(read_cgroup("/sys/fs/cgroup/memory.current"))
cpu_max = read_cgroup("/sys/fs/cgroup/cpu.max")      # "quota period"
pids_max = parse_limit(read_cgroup("/sys/fs/cgroup/pids.max"))
pids_current = parse_limit(read_cgroup("/sys/fs/cgroup/pids.current"))
disk = shutil.disk_usage(str(ev))

# Effective memory headroom = min(/proc MemAvailable, numeric cgroup max - current).
effective_mem = avail
if mem_max is not None and mem_current is not None:
    cg_headroom = mem_max - mem_current
    if effective_mem is None or cg_headroom < effective_mem:
        effective_mem = cg_headroom

rec = {
    "availableMemoryBytes": avail,
    "effectiveMemoryBytes": effective_mem,
    "cgroupMemoryMax": mem_max,
    "cgroupMemoryCurrent": mem_current,
    "cgroupCpuMax": cpu_max,
    "cgroupPidsMax": pids_max,
    "cgroupPidsCurrent": pids_current,
    "diskFreeBytes": disk.free,
    "diskTotalBytes": disk.total,
}
(ev / "resource-limits.json").write_text(json.dumps(rec, indent=2) + "\n")

need_mem = 4 * 1024 ** 3   # 4 GiB
need_disk = 8 * 1024 ** 3   # 8 GiB free for build/artifact
if effective_mem is None or effective_mem < need_mem:
    print("FATAL: container effective memory too low for compile", flush=True)
    sys.exit(1)
if disk.free < need_disk:
    print("FATAL: container disk free too low for build/artifact", flush=True)
    sys.exit(1)
PY
}

thunder_main() {
  # Required env (owner constants; never user-typed).
  : "${REPO:?}" "${CANDIDATE_LOCK:?}" "${ARCHIVE_URL:?}" "${ARCHIVE_SHA256:?}" \
    "${GGML_REPO:?}" "${GGML_COMMIT:?}" "${GGML_TREE:?}" "${CUDA_IMAGE:?}"

  WORK="$REPO/work/pc-worker/thunder-cuda-compile"
  BUILD_WORK="$WORK/build-work"
  INSTALL="$WORK/install"
  EVIDENCE="$REPO/work/pc-worker/thunder-cuda-compile-evidence"
  GGML_PARENT="$WORK/ggml-checkout"
  mkdir -p "$EVIDENCE"
  LOG="$EVIDENCE/thunder-cuda-compile.log"

  # Capture all stdout/stderr to the log as well as the console (bounded; no secrets/raw env).
  exec > >(tee -a "$LOG") 2>&1

  export THUNDER_DONE=0
  trap thunder_on_exit EXIT

  # Bootstrap Python under the EXIT/failure-log path first. The minimal NVIDIA devel image must not be
  # assumed to include python3 or the full stdlib. We install FULL python3 (never python3-minimal) and
  # verify the required stdlib imports BEFORE emitting any structured metadata. If the full install or
  # its stdlib verification fails, the EXIT trap records the failure (with the shell fallback if the
  # Python writer is unusable).
  STAGE="bootstrap-python"
  thunder_bootstrap_python || exit 1

  STAGE="start"
  python3 - "$EVIDENCE" "$CUDA_IMAGE" <<'PY'
import json, pathlib, sys
ev = pathlib.Path(sys.argv[1]); ev.mkdir(parents=True, exist_ok=True)
(ev / "thunder-cuda-compile-start.json").write_text(json.dumps({
    "workflow": "haven-chat-model-ci.yml", "job": "thunder-cuda-compile",
    "stage": "start", "cudaImage": sys.argv[2],
    "compileOnly": {"gpu": "NOT RUN", "driver": "NOT RUN", "model": "NOT RUN"},
}, indent=2) + "\n")
PY

  STAGE="preflight"
  thunder_resource_preflight

  # The base devel image ships nvcc + CUDA headers but not the full build/network toolchain.
  STAGE="apt"
  apt-get update -qq
  apt-get install -y -qq --no-install-recommends \
    build-essential git curl ca-certificates python3 cmake ninja-build

  STAGE="paths"
  thunder_prepare_paths "$WORK" "$INSTALL" "$EVIDENCE" "$GGML_PARENT"

  cd "$REPO"   # no global git config/identity; no commits are made

  STAGE="archive"
  ARCHIVE="$WORK/strata-v0.2.2.tar.gz"
  if [ ! -s "$ARCHIVE" ]; then
    curl -fL --retry 3 -o "$ARCHIVE" "$ARCHIVE_URL"
  fi
  ACTUAL_SHA=$(sha256sum "$ARCHIVE" | awk '{print $1}')
  ACTUAL_BYTES=$(stat -c%s "$ARCHIVE")
  if [ "$ACTUAL_SHA" != "$ARCHIVE_SHA256" ]; then
    echo "FATAL: archive SHA mismatch: $ACTUAL_SHA != $ARCHIVE_SHA256"
    exit 1
  fi

  STAGE="ggml"
  GGML="$GGML_PARENT/llama.cpp"
  if [ ! -d "$GGML/.git" ]; then
    git clone --filter=blob:none --no-checkout "$GGML_REPO" "$GGML"
  fi
  # Repo-scoped safe.directory only (never global configuration).
  local safe_arg="safe.directory=$(realpath "$GGML")"
  git -C "$GGML" -c "$safe_arg" fetch --depth 1 origin "$GGML_COMMIT"
  git -C "$GGML" -c "$safe_arg" checkout --detach "$GGML_COMMIT"
  HEAD_COMMIT=$(git -C "$GGML" -c "$safe_arg" rev-parse HEAD)
  HEAD_TREE=$(git -C "$GGML" -c "$safe_arg" rev-parse 'HEAD^{tree}')
  if [ "$HEAD_COMMIT" != "$GGML_COMMIT" ] || [ "$HEAD_TREE" != "$GGML_TREE" ]; then
    echo "FATAL: ggml checkout HEAD/tree mismatch: $HEAD_COMMIT/$HEAD_TREE"
    exit 1
  fi
  if [ -n "$(git -C "$GGML" -c "$safe_arg" status --porcelain)" ]; then
    echo "FATAL: ggml checkout is not clean"
    exit 1
  fi

  STAGE="build"
  PY="9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata"
  python3 "$PY/build-package.py" \
    --profile candidate \
    --lock "$REPO/$CANDIDATE_LOCK" \
    --archive "$ARCHIVE" \
    --work "$BUILD_WORK" \
    --install "$INSTALL" \
    --ggml-dir "$GGML" \
    --cmake-define CMAKE_CUDA_ARCHITECTURES=120 \
    --cmake-define STRATA_PORTABLE=ON \
    --cmake-define GGML_NATIVE=OFF \
    --build

  STAGE="evidence"
  ENGINE="$INSTALL/runtimes/linux-x64/engine/strata"
  WORKER="$INSTALL/runtimes/linux-x64/native/dulche-strata-candidate-worker"
  HELPER="$INSTALL/runtimes/linux-x64/native/strata_tokenizer_helper.py"
  for bin in "$ENGINE" "$WORKER"; do
    if [ ! -x "$bin" ]; then
      echo "FATAL: expected real binary missing: $bin"
      exit 1
    fi
  done

  # Verify the actual toolkit is >= 13.0 (explicit SM120 needs CUDA 13.0+ per the upstream pin).
  NVCC_RELEASE=$(nvcc --version | tail -1)
  NVCC_MAJOR=$(nvcc --version | sed -n 's/.*release \([0-9]*\)\.\([0-9]*\).*/\1/p')
  if [ "${NVCC_MAJOR:-0}" -lt 13 ]; then
    echo "FATAL: nvcc < 13.0: $NVCC_RELEASE"
    exit 1
  fi

  # Model-free upstream version metadata (--version/--families are before any GPU call per the
  # verified source; no model load/hello/GPU-ready execution).
  ENGINE_VERSION="$(cd "$(dirname "$ENGINE")" && ./strata --version 2>&1 || true)"
  ENGINE_FAMILIES="$(cd "$(dirname "$ENGINE")" && ./strata --families 2>&1 || true)"

  # Assemble the isolated install closure: attach upstream MIT/ggml notices, then inventory it via the
  # pure, fixture-testable function above.
  STAGE="closure"
  mkdir -p "$INSTALL/licenses"
  cp "$BUILD_WORK/upstream/LICENSE" "$INSTALL/licenses/MIT-STRATA.txt"
  cp "$GGML/LICENSE" "$INSTALL/licenses/MIT-GGML.txt"
  thunder_inventory_install "$INSTALL" "$EVIDENCE/install-closure-inventory.json"

  python3 - "$EVIDENCE" "$CUDA_IMAGE" "$ACTUAL_SHA" "$ACTUAL_BYTES" \
    "$HEAD_COMMIT" "$HEAD_TREE" "$ENGINE" "$WORKER" "$HELPER" "$NVCC_RELEASE" \
    "$ENGINE_VERSION" "$ENGINE_FAMILIES" <<'PY'
import hashlib, json, pathlib, subprocess, sys
ev = pathlib.Path(sys.argv[1])
def sha(p):
    return hashlib.sha256(pathlib.Path(p).read_bytes()).hexdigest()
def first_stdout(cmd):
    return subprocess.run(cmd, capture_output=True, text=True).stdout.strip()
rec = {
    "workflow": "haven-chat-model-ci.yml",
    "job": "thunder-cuda-compile",
    "cudaImage": sys.argv[2],
    "success": True,
    "compileOnly": {"gpu": "NOT RUN", "driver": "NOT RUN", "model": "NOT RUN"},
    "archiveSha256": sys.argv[3],
    "archiveBytes": sys.argv[4],
    "ggmlCommit": sys.argv[5],
    "ggmlTree": sys.argv[6],
    "engineSha256": sha(sys.argv[7]),
    "engineBytes": pathlib.Path(sys.argv[7]).stat().st_size,
    "workerSha256": sha(sys.argv[8]),
    "workerBytes": pathlib.Path(sys.argv[8]).stat().st_size,
    "helperSha256": sha(sys.argv[9]),
    "nvcc": sys.argv[10],
    "engineVersion": sys.argv[11],
    "engineFamilies": sys.argv[12],
    "cmake": first_stdout(["cmake", "--version"]).splitlines()[0] if first_stdout(["cmake", "--version"]) else None,
    "ninja": first_stdout(["ninja", "--version"]),
}
(ev / "thunder-cuda-compile-evidence.json").write_text(json.dumps(rec, indent=2) + "\n")
PY

  THUNDER_DONE=1
  echo "THUNDER-CUDA-COMPILE-OK"
  echo "Evidence: $EVIDENCE/thunder-cuda-compile-evidence.json"
}

# Main guard: only run when executed, so a shell fixture can source this file and call the pure
# functions (e.g. thunder_prepare_paths) without performing any build/network work.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
  thunder_main "$@"
fi
