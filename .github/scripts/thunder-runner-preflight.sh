#!/usr/bin/env bash
# Thunder runner-side resource preflight (lane 6) + container bounds.
#
# Runs on the GitHub runner BEFORE docker. Reads ACTUAL host capacity (memory/CPU/disk) AND the
# enclosing cgroup limits (memory.max/current, cpu.max, pids.max/current), derives conservative
# container bounds (--memory/--cpus/--pids-limit) that FIT the actual runner/cgroup capacity (never
# assumes public/private runners share the same RAM), fails clearly BEFORE compiling if inadequate,
# records the measured limits as structured metadata (not raw env), and publishes the bounds via
# $GITHUB_OUTPUT for the docker run step.
set -euo pipefail

mkdir -p work/pc-worker/thunder-cuda-compile-evidence

# On failure python writes to stderr and exits non-zero (set -e aborts before eval). On success stdout
# carries ONLY the three safe integer assignments.
RUNNER_BOUNDS="$(python3 - <<'PY'
import json, os, pathlib, shutil, sys

ev = pathlib.Path("work") / "pc-worker" / "thunder-cuda-compile-evidence"
ev.mkdir(parents=True, exist_ok=True)

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

def mem_available_mib():
    for line in open("/proc/meminfo"):
        if line.startswith("MemAvailable:"):
            return int(line.split()[1]) // 1024
    return None

avail = mem_available_mib()
ncpu = os.cpu_count() or 1

# Enclosing cgroup limits (cgroup v2).
mem_max = parse_limit(read_cgroup("/sys/fs/cgroup/memory.max"))
mem_current = parse_limit(read_cgroup("/sys/fs/cgroup/memory.current"))
cpu_max_raw = read_cgroup("/sys/fs/cgroup/cpu.max")
pids_max = parse_limit(read_cgroup("/sys/fs/cgroup/pids.max"))
pids_current = parse_limit(read_cgroup("/sys/fs/cgroup/pids.current"))

# Effective memory headroom (MiB) = min(/proc MemAvailable, numeric cgroup max - current).
eff_avail = avail
if mem_max is not None and mem_current is not None:
    cg_headroom_mib = (mem_max - mem_current) // (1024 * 1024)
    if eff_avail is None or cg_headroom_mib < eff_avail:
        eff_avail = cg_headroom_mib

# CPU quota/period from cpu.max ("<quota> <period>").
cpu_quota = None
cpu_period = None
if cpu_max_raw:
    parts = cpu_max_raw.split()
    if len(parts) == 2:
        cpu_quota = parse_limit(parts[0])
        cpu_period = parse_limit(parts[1])

# Effective CPU capacity = min(host CPU, cgroup quota/period when finite). Never exceed the host or
# the enclosing cgroup; do NOT clamp a fractional/quota value upward.
if cpu_quota is not None and cpu_period:
    eff_cpu = min(float(ncpu), cpu_quota / float(cpu_period))
else:
    eff_cpu = float(ncpu)

# PID headroom = pids.max - pids.current when both known.
pids_headroom = None
if pids_max is not None and pids_current is not None:
    pids_headroom = max(0, pids_max - pids_current)

disk = shutil.disk_usage(str(pathlib.Path(".").resolve()))
free_mib = disk.free // (1024 * 1024)

# Conservative caps (upper bounds) per lane requirement.
CPU_CAP = 2
MEM_CAP_MIB = 6144       # <= 6 GiB
PIDS_CAP = 512           # <= 512

RESERVE_MIB = 1024       # memory headroom
PID_RESERVE = 64         # pid headroom
MIN_BUILD_MEM_MIB = 4096  # minimum build memory that must fit within effective headroom
MIN_BUILD_CPU = 1.0
MIN_PIDS_BOUND = 64

# Memory bound derived DOWNWARD: at most effective headroom minus reserve; never floor upward past it.
if eff_avail is not None:
    mem_bound = min(MEM_CAP_MIB, max(0, int(eff_avail - RESERVE_MIB)))
else:
    mem_bound = None

# CPU bound: at most the conservative cap and the effective (host+cgroup) capacity.
cpus_bound = min(CPU_CAP, max(1, int(eff_cpu)))

# PID bound: capped downward to headroom minus reserve; never exceed headroom.
if pids_headroom is not None:
    pids_bound = min(PIDS_CAP, pids_headroom - PID_RESERVE)
else:
    pids_bound = PIDS_CAP

rec = {
    "availableMemoryMiB": avail,
    "effectiveMemoryMiB": eff_avail,
    "cgroupMemoryMaxMiB": None if mem_max is None else mem_max // (1024 * 1024),
    "cgroupMemoryCurrentMiB": None if mem_current is None else mem_current // (1024 * 1024),
    "cgroupCpuMax": cpu_max_raw,
    "hostCPU": ncpu,
    "effectiveCPUs": round(eff_cpu, 3),
    "pidsHeadroom": pids_headroom,
    "diskFreeMiB": free_mib,
    "recommendedContainerMemoryMiB": mem_bound,
    "recommendedContainerCPUs": cpus_bound,
    "recommendedPidsLimit": pids_bound,
}
(ev / "preflight-runner.json").write_text(json.dumps(rec, indent=2) + "\n")

# Fail clearly before compiling if inadequate (never oversubscribe beyond actual effective capacity).
if eff_avail is None or mem_bound < MIN_BUILD_MEM_MIB:
    print("FAIL_RESOURCE: runner effective memory too low: %s MiB (reserved %s MiB)" % (eff_avail, RESERVE_MIB), file=sys.stderr)
    sys.exit(1)
if free_mib < 20480:
    print("FAIL_RESOURCE: runner disk free too low: %s MiB" % free_mib, file=sys.stderr)
    sys.exit(1)
if eff_cpu < MIN_BUILD_CPU:
    print("FAIL_RESOURCE: runner effective CPU too low: %s" % eff_cpu, file=sys.stderr)
    sys.exit(1)
if pids_bound < MIN_PIDS_BOUND:
    print("FAIL_RESOURCE: runner pid headroom too low: %s (reserved %s)" % (pids_headroom, PID_RESERVE), file=sys.stderr)
    sys.exit(1)

print("MEMORY_MB=%d" % mem_bound)
print("CPUS=%d" % cpus_bound)
print("PIDS_LIMIT=%d" % pids_bound)
PY
)"
eval "$RUNNER_BOUNDS"

echo "memory_mb=$MEMORY_MB" >> "${GITHUB_OUTPUT:-/dev/null}"
echo "cpus=$CPUS" >> "${GITHUB_OUTPUT:-/dev/null}"
echo "pids_limit=$PIDS_LIMIT" >> "${GITHUB_OUTPUT:-/dev/null}"
echo "thunder runner preflight OK: memory=${MEMORY_MB}MiB cpus=${CPUS} pids=${PIDS_LIMIT}"
