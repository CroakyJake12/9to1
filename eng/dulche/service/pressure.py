"""Sample host pressure without allocating resources or changing display GPU settings."""

from pathlib import Path
import os
import shutil
import subprocess
import time


def fields(path):
    return {
        k.rstrip(":"): int(v)
        for k, v, *_ in map(str.split, Path(path).read_text().splitlines())
    }


def stall(kind):
    try:
        line = Path("/proc/pressure", kind).read_text().splitlines()[0]
        return float(dict(x.split("=") for x in line.split()[1:])["avg10"])
    except (OSError, IndexError, ValueError, KeyError):
        return None


class HostSampler:
    def __init__(self, directory):
        self.directory = directory
        self.previous = None

    def sample(self):
        mem = fields("/proc/meminfo")
        vm = fields("/proc/vmstat")
        cpu = list(
            map(int, Path("/proc/stat").read_text().splitlines()[0].split()[1:9])
        )
        now = time.monotonic()
        counters = (now, sum(cpu), cpu[3] + cpu[4], vm["pswpin"], vm["pswpout"])
        result = {
            "available_ram_mib": mem["MemAvailable"] // 1024,
            "total_ram_mib": mem["MemTotal"] // 1024,
            "swap_used_mib": (mem["SwapTotal"] - mem["SwapFree"]) // 1024,
            "disk_free_bytes": shutil.disk_usage(self.directory).free,
            "memory_stall_pct": stall("memory"),
            "io_stall_pct": stall("io"),
            "cpu_stall_pct": stall("cpu"),
            "sample_at": time.time(),
            "cpu_busy_pct": None,
            "swap_mib_s": None,
            "desktop_roundtrip_ms": None,
        }
        if self.previous:
            then, total, idle, si, so = self.previous
            result["cpu_busy_pct"] = round(
                100 * (1 - (counters[2] - idle) / max(1, counters[1] - total)), 1
            )
            result["swap_mib_s"] = round(
                (counters[3] + counters[4] - si - so)
                * os.sysconf("SC_PAGE_SIZE")
                / 2**20
                / max(0.01, now - then),
                2,
            )
        self.previous = counters
        if os.environ.get("DISPLAY") and shutil.which("xprop"):
            tick = time.monotonic()
            try:
                probe = subprocess.run(
                    ["xprop", "-root", "_NET_ACTIVE_WINDOW"],
                    capture_output=True,
                    timeout=0.75,
                )
                if probe.returncode == 0:
                    result["desktop_roundtrip_ms"] = round(
                        1000 * (time.monotonic() - tick), 2
                    )
            except subprocess.TimeoutExpired:
                result["desktop_roundtrip_ms"] = 750.0
        return result
