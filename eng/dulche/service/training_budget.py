"""Workload-independent, measured compute admission. GPU memory and utilization are distinct."""

from __future__ import annotations

import collections
import os
import shutil
import subprocess
import time
from pathlib import Path

GiB = 1024**3
MiB = 1024**2
TARGET = 10.0


def host_sample(directory):
    mem = {
        s.split()[0].strip(":"): int(s.split()[1]) * 1024
        for s in Path("/proc/meminfo").read_text().splitlines()
    }
    vm = {
        s.split()[0]: int(s.split()[1])
        for s in Path("/proc/vmstat").read_text().splitlines()
    }
    psi = Path("/proc/pressure/memory").read_text().splitlines()
    full = next(l for l in psi if l.startswith("full "))
    return {
        "time": time.time(),
        "available_ram_bytes": mem["MemAvailable"],
        "swap_free_bytes": mem["SwapFree"],
        "swapped_pages": vm["pswpin"] + vm["pswpout"],
        "oom_kills": vm.get("oom_kill"),
        "available_disk_bytes": shutil.disk_usage(directory).free,
        "full_psi_10s": float(full.split()[1].split("=")[1]),
    }


def gpu_sample(uuid, owned_pids):
    command = [
        "nvidia-smi",
        "--query-gpu=index,uuid,memory.free,memory.used,temperature.gpu,utilization.gpu",
        "--format=csv,noheader,nounits",
    ]
    p = subprocess.run(command, check=True, capture_output=True, text=True, timeout=4)
    rows = [[c.strip() for c in line.split(",")] for line in p.stdout.splitlines()]
    device = next((r for r in rows if len(r) == 6 and r[1] == uuid), None)
    if not device:
        raise ValueError(
            "Requested GPU is not visible to this worker's driver environment"
        )
    if not all(v.isdigit() for v in [device[0], *device[2:]]):
        raise ValueError("Incomplete physical GPU measurements")
    samples = subprocess.run(
        ["nvidia-smi", "pmon", "-i", device[0], "-c", "1", "-s", "um"],
        check=True,
        capture_output=True,
        text=True,
        timeout=5,
    ).stdout
    return {
        "uuid": uuid,
        "free_vram_mib": int(device[2]),
        "used_vram_mib": int(device[3]),
        "temperature_c": int(device[4]),
        "total_sm_pct": int(device[5]),
        **parse_process_samples(samples, owned_pids, int(device[5])),
    }


def parse_process_samples(text, owned_pids, total_sm_pct):
    headers = next(
        (
            s.lstrip("# ").split()
            for s in text.splitlines()
            if s.startswith("#") and "pid" in s.split() and "sm" in s.split()
        ),
        [],
    )
    if not all(k in headers for k in ("pid", "sm", "fb")):
        raise ValueError("Per-process SM/VRAM monitoring is unsupported")
    pid_i, sm_i, mem_i = [headers.index(k) for k in ("pid", "sm", "fb")]
    own = other = used = 0
    count = 0
    seen = set()
    unknown_other = False
    unknown_worker = False
    for line in text.splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        fields = line.split()
        count += 1
        if len(fields) <= max(pid_i, sm_i, mem_i):
            raise ValueError("Incomplete process GPU measurement")
        if fields[pid_i] == "-":
            continue
        pid = int(fields[pid_i])
        if pid in owned_pids:
            if not fields[mem_i].isdigit():
                raise ValueError("Training GPU memory measurement is unavailable")
            used += int(fields[mem_i])
            seen.add(pid)
            if fields[sm_i].isdigit():
                own += int(fields[sm_i])
            else:
                unknown_worker = True
        elif fields[sm_i].isdigit():
            other += int(fields[sm_i])
        else:
            unknown_other = True
    if not count:
        raise ValueError("No process telemetry cycles were returned")
    # NVSMI renders both idle and unsupported fields as "-". The *measured physical total* is a
    # conservative upper bound for either unknown group; never treat a dash as a measured zero.
    return {
        "worker_sm_pct": total_sm_pct if unknown_worker else min(100, own),
        "other_sm_pct": max(other, total_sm_pct) if unknown_other else min(100, other),
        "worker_measurement": "total-gpu-upper-bound"
        if unknown_worker
        else "attributed",
        "other_measurement": "total-gpu-upper-bound" if unknown_other else "attributed",
        "worker_vram_mib": used,
        "visible_gpu_pids": sorted(seen),
    }


def group_counters(group):
    root = Path("/sys/fs/cgroup")
    if not group.startswith("/user.slice/"):
        raise ValueError("Worker cgroup is unavailable")
    cg = root / group.lstrip("/")
    cpu = dict(s.split() for s in (cg / "cpu.stat").read_text().splitlines())
    events = dict(s.split() for s in (cg / "memory.events").read_text().splitlines())
    pids = {
        int(pid)
        for path in cg.rglob("cgroup.procs")
        for pid in path.read_text().split()
    }
    return {
        "cpu_usec": int(cpu["usage_usec"]),
        "memory_bytes": int((cg / "memory.current").read_text()),
        "cgroup_oom_kills": int(events["oom_kill"]),
        "pids": pids,
    }


class ComputeBudget:
    def __init__(self):
        self.series = collections.deque()
        self.last = None
        self.sum_pct_seconds = 0.0
        self.seconds = 0.0
        self.origin = time.monotonic()

    def assess(self, limits, current, cgroup, gpu, now=None):
        now = time.monotonic() if now is None else now
        previous = self.last
        self.last = (now, dict(current), dict(cgroup))
        if (
            current["available_disk_bytes"]
            < 50 * GiB + limits["checkpoint_reserve_bytes"]
        ):
            return False, "storage-floor", None
        if current["available_ram_bytes"] < limits["min_free_ram_mib"] * MiB:
            return False, "host-ram-pressure", None
        if current["full_psi_10s"] > 2:
            return False, "host-memory-stalls", None
        if (
            cgroup["memory_bytes"] > limits["ram_mib"] * MiB
            or cgroup["cgroup_oom_kills"]
        ):
            return False, "worker-memory-budget", None
        if gpu and (
            gpu["temperature_c"] >= limits["max_gpu_temperature_c"]
            or gpu["other_sm_pct"] > 5
        ):
            return False, "gpu-temperature-or-competing-work", None
        if gpu and (
            gpu["worker_vram_mib"] > limits["vram_mib"]
            or gpu["free_vram_mib"] + gpu["worker_vram_mib"] < limits["vram_mib"]
        ):
            return False, "gpu-allocation-pressure", None
        if not previous:
            return False, "measuring", None
        dt = now - previous[0]
        if dt <= 0 or dt > 10:
            return False, "measurement-gap", None
        paging = (
            (current["swapped_pages"] - previous[1]["swapped_pages"])
            * os.sysconf("SC_PAGE_SIZE")
            / dt
        )
        if paging > 8 * MiB:
            return False, "swap-activity", None
        if gpu:
            measured = float(gpu["worker_sm_pct"])
        else:
            cores = max(os.cpu_count() or 1, 1)
            measured = (
                100
                * max(0, cgroup["cpu_usec"] - previous[2]["cpu_usec"])
                / (dt * 1e6 * cores)
            )
        self.sum_pct_seconds += measured * dt
        self.seconds += dt
        self.series.append((now, measured, dt))
        while self.series and self.series[0][0] < now - 60:
            self.series.popleft()
        recent = sum(v * t for _, v, t in self.series) / max(
            0.001, sum(t for _, _, t in self.series)
        )
        if self.seconds >= 15 and recent > TARGET + 3:
            return False, "measured-compute-overrun", measured
        # CPUQuota enforces aggregate host CPU time independently of this feedback. GPU
        # workers additionally consume expiring permits. A process pause alone holds VRAM.
        if gpu:
            permitted = (now - self.origin) % 10.0 < 1.0 and recent <= TARGET + 1
            return (
                permitted,
                "gpu-duty-window" if permitted else "budget-idle",
                measured,
            )
        return True, "cpu-quota", measured

    @property
    def mean(self):
        return self.sum_pct_seconds / self.seconds if self.seconds else None
