"""Run a selected GGUF via the guarded V100 RPC worker, isolated from desktop CUDA."""

import fcntl
import os
from pathlib import Path
import signal
import subprocess
import sys
import threading
import time

from engine_profiles import dense_files, load_json, read_signature
from engines import _memory, _unit, _v100_guard


def run(record_path):
    if (
        record_path.is_symlink()
        or record_path.stat().st_uid != os.getuid()
        or record_path.stat().st_mode & 0o077
    ):
        raise ValueError("Unsafe engine registration")
    config = load_json(record_path)
    exe, _ = dense_files()
    model = read_signature(config["path"])
    keyfile = record_path.with_suffix(".key")
    args = [
        str(exe),
        "-m",
        str(model),
        "--rpc",
        "127.0.0.1:19110",
        "-dev",
        "RPC0",
        "-ngl",
        str(config.get("gpu_layers") or 99),
        "--fit",
        "off",
        "--cache-type-k",
        config["kv_type"],
        "--cache-type-v",
        config["kv_type"],
        "-c",
        str(config["context"]),
        "-np",
        "1",
        "-t",
        str(config["threads"]),
        "-b",
        "256",
        "-ub",
        "128",
        "--no-context-shift",
        "--cache-ram",
        "0",
        "--ctx-checkpoints",
        "0",
        "--poll",
        "0",
        "--jinja",
        "--no-webui",
        "--host",
        "127.0.0.1",
        "--port",
        str(config["port"]),
        "--api-key-file",
        str(keyfile),
        "--alias",
        config["alias"],
    ]
    draft = config.get("draft_tokens", 0)
    if draft:
        if config.get("alias") != "local-2c31f26c4d698422017d6c14" or draft not in (2,3,4):
            raise ValueError("MTP profile identity/length mismatch")
        args += ["--spec-type", "draft-mtp", "--spec-draft-n-max", str(draft),
                 "--spec-draft-type-k", config["kv_type"], "--spec-draft-type-v", config["kv_type"]]
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    stop = threading.Event()
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: stop.set())
    from pooled_runtime import claim_devices
    with claim_devices(["v100"], runtime_dir=runtime):
        if not _v100_guard() or _unit("astra-v100-inference.service").get(
            "ActiveState"
        ) in ("activating", "active"):
            raise RuntimeError("The V100 is unavailable or already allocated")
        host_floor_mib = 8192 if config.get("context", 0) > 32768 else 1536
        if _memory()["MemAvailable"] < max(3072, host_floor_mib) * 1024:
            raise RuntimeError("Insufficient host RAM to stream model weights")
        env = {
            **os.environ,
            "CUDA_VISIBLE_DEVICES": "",
            "LD_LIBRARY_PATH": os.pathsep.join(
                (str(exe.parent), str(Path.home() / ".cache/strata-cuda-12.9/lib"))
            ),
        }
        child = subprocess.Popen(args, stdin=subprocess.DEVNULL, env=env)
        poll_at = 0
        try:
            while child.poll() is None and not stop.wait(0.5):
                reading = _v100_guard()
                limit = config.get("vram_ceiling_mib")
                if (
                    limit
                    and reading
                    and reading["mem_used"] / 2**20 - config["baseline_v100_mib"]
                    > limit
                ):
                    raise RuntimeError(
                        "Observed V100 allocation exceeded the configured partial-residency limit"
                    )
                if not reading or _memory()["MemAvailable"] < host_floor_mib * 1024:
                    raise RuntimeError(
                        "Thermal telemetry, power or host memory guard closed admission"
                    )
                if time.monotonic() >= poll_at:
                    poll_at = time.monotonic() + 3
                    if _unit("astra-v100-inference.service").get("ActiveState") in (
                        "activating",
                        "active",
                    ):
                        raise RuntimeError(
                            "The preferred V100 engine is starting; releasing candidate allocations"
                        )
            return child.returncode or 0
        finally:
            if child.poll() is None:
                child.terminate()
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    child.kill()
                    child.wait()


if __name__ == "__main__":
    raise SystemExit(run(Path(sys.argv[1])))
