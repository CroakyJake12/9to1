"""Configured versus observed engine placement, with no new inference or model allocations."""

from __future__ import annotations

import json
import math
import re
import subprocess
import threading
import time
from functools import lru_cache
from pathlib import Path

from engine_profiles import CPU, GPU_RTX, GPU_V100, PRIMARY, V100, INKLING, INKLING_POOL

CACHE = {}
LOCK = threading.Lock()
INVOCATION = re.compile(r"[0-9a-f]{32}\Z")
MULTIPART = re.compile(r"-\d{5}-of-\d{5}\.gguf$", re.I)
LOG_PREFIX = r"^\s*(?:\d+(?:\.\d+){2,4}\s+[A-Z]\s+)?"
BUFFER = re.compile(
    LOG_PREFIX
    + r"(?:load_tensors|llama_kv_cache|llama_memory_recurrent|sched_reserve):\s+(CPU(?:_Mapped)?|RPC0\[127\.0\.0\.1:19110\])\s+(model|KV|RS|compute) buffer size\s*=\s*([0-9]+(?:\.[0-9]+)?) MiB\s*$"
)
LAYERS = re.compile(
    LOG_PREFIX + r"load_tensors:\s+offloaded (\d+)/(\d+) layers to GPU\s*$"
)


def number(x):
    return x if type(x) in (int, float) and math.isfinite(x) and x >= 0 else None


def cached(key, ttl, get):
    now = time.monotonic()
    with LOCK:
        old = CACHE.get(key)
        if old and old[0] > now:
            return old[1]
    try:
        value = get()
    except (OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        value = None
    with LOCK:
        if len(CACHE) >= 128:
            expired = [k for k, v in CACHE.items() if v[0] <= now]
            for k in expired:
                CACHE.pop(k, None)
            if len(CACHE) >= 128:
                CACHE.pop(next(iter(CACHE)))
        CACHE[key] = (now + (ttl if value else min(ttl, 2)), value)
    return value


@lru_cache(maxsize=32)
def estimate(path, size, mtime, layers, context, kv):
    from gguf_placement import layer_budget

    return layer_budget(Path(path), layers, context, kv)


def configured_placement(model, settings=None, plan=None):
    """Describe only a supported adapter/configuration. Never infer placement from a model's display name."""
    settings = settings or {}
    mid = model.get("id")
    gpu = settings.get("gpu") or model.get("gpu")
    layers = settings.get("gpu_layers")
    ceiling = settings.get("vram_ceiling_mib")
    if mid == INKLING_POOL:
        kind = "pooled_gpu_layers"
    elif mid in (PRIMARY, INKLING) and gpu == GPU_RTX:
        kind = "native_strata_expert_cache"
    elif mid == V100 and gpu == GPU_V100:
        kind = "gpu_layer_offload"
    elif (mid == CPU or str(mid).startswith("local-")) and gpu == "CPU":
        kind = "cpu"
    elif str(mid).startswith("local-") and gpu == GPU_V100:
        kind = (
            "partial_gpu_layers"
            if type(layers) is int
            and layers > 0
            and type(ceiling) is int
            and ceiling > 0
            else "gpu_layer_offload"
        )
    else:
        kind = "unknown"
    out = {
        "kind": kind,
        "evidence": "configured" if kind != "unknown" else "unknown",
        "gpu": gpu,
        "source": "registered engine settings" if kind != "unknown" else None,
        "sampled_at": None,
    }
    if mid == INKLING_POOL:
        out.update(physical_devices=["rtx5080", "v100"], display_reserved_bytes=1_400_000_000,
                   hard_reservation_supported=False)
    path = model.get("path")
    size = number(model.get("bytes"))
    if not size and isinstance(path, str) and not MULTIPART.search(path):
        try:
            size = Path(path).stat().st_size
        except OSError:
            pass
    if size is not None:
        out["artifact_bytes"] = size
    if kind == "partial_gpu_layers":
        out.update(gpu_layers=layers, vram_ceiling_mib=ceiling)
        if not plan and isinstance(path, str):
            try:
                st = Path(path).stat()
                plan = estimate(
                    path,
                    st.st_size,
                    st.st_mtime_ns,
                    layers,
                    settings.get("context", model.get("context", 32768)),
                    settings.get("kv_type", "q8_0"),
                )
            except (OSError, ValueError, KeyError, TypeError):
                plan = None
        if plan:
            for source, target in (
                ("estimated_cpu_weight_mib", "estimated_cpu_weight_mib"),
                ("weight_upper_mib", "estimated_gpu_weight_mib"),
                ("estimated_vram_mib", "estimated_total_vram_mib"),
                ("total_layer_groups", "gguf_layer_groups"),
            ):
                n = number(plan.get(source))
                if n is not None:
                    out[target] = n
    return out


def native_observation(requested, model_id, metrics):
    engine = (metrics or {}).get("engine")
    if not isinstance(engine, dict) or engine.get("model") != model_id:
        return None
    if not all(
        number(engine.get(k)) is not None
        for k in ("arena_mib", "expert_cache_mib", "expert_slots")
    ):
        return None
    out = {
        **requested,
        "evidence": "observed",
        "source": "authenticated Strata /metrics.engine",
        "sampled_at": time.time(),
        "host_arena_mib": engine["arena_mib"],
        "expert_cache_mib": engine["expert_cache_mib"],
        "expert_slots": engine["expert_slots"],
        "kv_resident": number(engine.get("kv_resident")),
    }
    if isinstance(engine.get("drafting"), str):
        out["drafting"] = engine["drafting"][:64]
    return out


def journal_buffers(records, invocation):
    """Parse only exact active systemd invocation receipts; newer repeat lines replace earlier values."""
    if not isinstance(invocation, str) or not INVOCATION.fullmatch(invocation):
        return {}
    buffers = {}
    out = {}
    last = None
    relevant = [r for r in records if r.get("_SYSTEMD_INVOCATION_ID") == invocation]
    relevant.sort(key=lambda r: int(r.get("__REALTIME_TIMESTAMP", 0)))
    for row in relevant:
        msg = row.get("MESSAGE")
        if not isinstance(msg, str):
            continue
        match = BUFFER.search(msg)
        if match:
            device, kind, size = match.groups()
            size = number(float(size))
            if size is None:
                continue
            buffers[(device, kind)] = size
        else:
            match = LAYERS.search(msg)
            if not match:
                continue
            n, total = map(int, match.groups())
            if not 0 <= n <= total:
                continue
            out.update(offloaded_layers=n, total_offload_layers=total)
        last = int(row.get("__REALTIME_TIMESTAMP") or 0) / 1e6
    for group, prefix in (
        (("CPU", "CPU_Mapped"), "cpu"),
        (("RPC0[127.0.0.1:19110]",), "gpu"),
    ):
        for kind, key in (
            ("model", "weight_buffer_mib"),
            ("KV", "kv_buffer_mib"),
            ("RS", "recurrent_buffer_mib"),
            ("compute", "compute_buffer_mib"),
        ):
            vals = [n for (d, k), n in buffers.items() if k == kind and d in group]
            if vals:
                out[prefix + "_" + key] = round(sum(vals), 3)
    if last:
        out["buffer_reported_at"] = last
    return out


def load_receipts(invocation):
    if not isinstance(invocation, str) or not INVOCATION.fullmatch(invocation):
        return {}
    p = subprocess.run(
        [
            "journalctl",
            "--user",
            "_SYSTEMD_INVOCATION_ID=" + invocation,
            "--grep",
            "model buffer size|KV buffer size|RS buffer size|compute buffer size|offloaded [0-9]+/[0-9]+ layers",
            "--no-pager",
            "--lines=64",
            "--output=json",
            "--output-fields=MESSAGE,_SYSTEMD_INVOCATION_ID",
        ],
        timeout=2,
        capture_output=True,
        text=True,
        check=False,
    )
    if p.returncode:
        return {}
    rows = [json.loads(line) for line in p.stdout.splitlines() if line.strip()]
    return journal_buffers(rows, invocation)


def model_placements(spec, actual, unit, rec, connection, read_json):
    """Called after the serving adapter proves process, listener and model identity."""
    requested = configured_placement(
        spec, actual.get("settings"), rec.get("placement_plan")
    )
    observation = None
    if (
        actual.get("state") == "loaded"
        and connection
        and requested["kind"] != "unknown"
    ):
        port, key, _owner, pid = connection
        if spec.get("id") == INKLING_POOL:
            runtime = actual.get("pool_runtime") or {}
            if runtime.get("phase") == "running" and runtime.get("dual_allocations_observed") is True:
                observation = {**requested, "evidence": "observed",
                               "source": "UUID-pinned host and guest per-process NVML allocations",
                               "sampled_at": runtime.get("observed_at"),
                               "devices": (runtime.get("budget") or {}).get("devices", []),
                               "budget": runtime.get("budget"),
                               "evidence_note": "Both GPUs have attributable allocations; this does not by itself prove correlated compute or output quality."}
        elif spec.get("id") in (PRIMARY, INKLING):
            cache_key = (
                "strata",
                spec["id"],
                port,
                pid,
                unit.get("InvocationID"),
                actual.get("context"),
            )

            def inspect_native():
                return native_observation(
                    requested,
                    spec["id"],
                    read_json(port, "/metrics", key=key, timeout=1),
                )

            observation = cached(cache_key, 3, inspect_native)
        else:
            invocation = unit.get("InvocationID")
            facts = (
                cached(("gguf", invocation), 120, lambda: load_receipts(invocation))
                if isinstance(invocation, str) and INVOCATION.fullmatch(invocation)
                else {}
            ) or {}
            measured = number(actual.get("observed_model_vram_mib"))
            if facts or measured is not None:
                observation = {
                    **requested,
                    **facts,
                    "evidence": "observed",
                    "sampled_at": time.time(),
                    "source": "active llama.cpp allocation log"
                    if facts
                    else "dedicated V100 allocation delta",
                }
                if measured is not None:
                    observation["observed_model_vram_mib"] = measured
                    observation["vram_measurement_source"] = (
                        "dedicated V100 use minus pre-load baseline"
                    )
                if facts:
                    observation["invocation_id"] = invocation
    return {
        "placement_requested": requested,
        "placement_observation": observation,
        "placement": observation or requested,
    }
