"""Project each participant's requested placement separately from current engine observations."""

import math

from state import Fault

LABELS = {
    "pooled_gpu_layers": (
        "pooled_cuda_rpc",
        "llama.cpp · one model across RTX 5080 and V100",
    ),
    "native_strata_expert_cache": (
        "strata_ram_experts",
        "Native Strata · host RAM expert arena and GPU expert cache",
    ),
    "partial_gpu_layers": (
        "llama_cpu_gpu_layers",
        "llama.cpp · selected GPU layers and remaining CPU layers",
    ),
    "gpu_layer_offload": (
        "llama_gpu",
        "llama.cpp · GPU placement requested; exact layer split requires runtime evidence",
    ),
    "cpu": ("llama_cpu_weights", "llama.cpp · CPU layers in host RAM"),
    "unknown": ("unknown", "The engine has no verified placement description"),
}


def number(v):
    return v if type(v) in (int, float) and math.isfinite(v) and v >= 0 else None


def configured(model, settings):
    engine = str(settings.get("engine") or model.get("engine") or "")
    gpu = settings.get("gpu") or model.get("gpu")
    layers = number(settings.get("gpu_layers"))
    arch = model.get("architecture")
    native = model.get("id") in (
        "unsloth-ud-iq4_xs",
        "qwen3.8-flash-next-unsloth-ud-iq4_xs",
    ) or arch in ("qwen4exp", "qwen35moe", "inkling2_loop", "hy4")
    if model.get("id") == "inkling-2-mini-pooled-5080-v100":
        kind = "pooled_gpu_layers"
    elif "strata" in engine.lower() and native:
        kind = "native_strata_expert_cache"
    elif "llama" in engine.lower() or "lightning" in engine.lower():
        kind = (
            "cpu"
            if gpu == "CPU"
            else ("partial_gpu_layers" if layers is not None else "gpu_layer_offload")
        )
    else:
        kind = "unknown"
    plan = settings.get("placement_requested") or settings.get("placement_plan") or {}
    current = model.get("placement_requested") or {}
    if (
        not plan
        and isinstance(current, dict)
        and current.get("gpu") == gpu
        and current.get("gpu_layers") == layers
    ):
        plan = current
    if not isinstance(plan, dict):
        plan = {}
    kind = plan.get("kind") if plan.get("kind") in LABELS else kind
    mode, label = LABELS[kind]
    result = {
        "mode": mode,
        "kind": kind,
        "label": label,
        "source": plan.get("source") or "validated model and selected profile",
        "evidence": plan.get("evidence") or "configuration",
        "engine": engine,
        "gpu": gpu,
        "architecture": arch,
        "quantization": settings.get("quantization") or model.get("quantization"),
        "gpu_layers": layers,
        "vram_ceiling_mib": number(settings.get("vram_ceiling_mib")),
        "estimated_cpu_weight_mib": None,
        "estimated_gpu_weight_mib": None,
        "artifact_bytes": number(model.get("bytes")),
    }
    if kind == "pooled_gpu_layers":
        result["physical_devices"] = ["rtx5080", "v100"]
    for key in (
        "estimated_cpu_weight_mib",
        "estimated_gpu_weight_mib",
        "artifact_bytes",
        "gpu_layers",
        "vram_ceiling_mib",
    ):
        v = number(plan.get(key))
        if v is not None:
            result[key] = v
    if (
        kind == "cpu"
        and result["estimated_cpu_weight_mib"] is None
        and result["artifact_bytes"] is not None
    ):
        result["estimated_cpu_weight_mib"] = result["artifact_bytes"] / 2**20
    return result


def participant_placement(ctx, p, name):
    mid = p["model"]
    try:
        row = ctx.model(mid)
    except (Fault, KeyError, ValueError):
        row = {}
    requested = p.get("placement") or configured(row, p)
    live = row.get("state") == "loaded"
    active_profile = row.get("active_profile_id", row.get("profile_id"))
    observed = {
        "state": row.get("state", "unavailable"),
        "source": None,
        "evidence": None,
        "kind": None,
        "profile_matches": bool(
            live
            and active_profile == p.get("profile")
            and row.get("engine") == p.get("engine")
            and row.get("gpu") == p.get("gpu")
        ),
        "active_profile": active_profile if live else None,
        "engine": row.get("engine") if live else None,
        "gpu": row.get("gpu") if live else None,
        "model_vram_mib": None,
        "host_expert_arena_mib": None,
        "gpu_expert_cache_mib": None,
        "expert_slots": None,
        "kv_resident_tokens": None,
        "cpu_weight_buffer_mib": None,
        "gpu_weight_buffer_mib": None,
        "offloaded_layers": None,
        "total_offload_layers": None,
        "drafting": None,
        "as_of_utc": None,
        "sampled_at": None,
        "evidence_note": "No model-specific allocation measurement is available.",
    }
    placed = row.get("placement_observation") or {}
    if live and placed.get("kind") == "pooled_gpu_layers" and placed.get("evidence") == "observed":
        observed.update({key: placed.get(key) for key in
                         ("kind", "source", "evidence", "sampled_at", "devices", "budget", "evidence_note")})
    if live and number(row.get("observed_model_vram_mib")) is not None:
        observed.update(
            model_vram_mib=row["observed_model_vram_mib"],
            source="V100 allocation above pre-load baseline",
            evidence_note="Includes runtime allocations alongside weights.",
        )
    if live and isinstance(placed, dict) and placed.get("model_id", mid) == mid:
        numeric = {
            "model_vram_mib": "observed_model_vram_mib",
            "host_expert_arena_mib": "host_arena_mib",
            "gpu_expert_cache_mib": "expert_cache_mib",
            "kv_resident_tokens": "kv_resident",
            "expert_slots": "expert_slots",
            "cpu_weight_buffer_mib": "cpu_weight_buffer_mib",
            "gpu_weight_buffer_mib": "gpu_weight_buffer_mib",
            "offloaded_layers": "offloaded_layers",
            "total_offload_layers": "total_offload_layers",
        }
        found = False
        for dest, source in numeric.items():
            val = number(placed.get(source))
            if val is not None:
                observed[dest] = val
                found = True
        if found:
            for name_ in (
                "kind",
                "source",
                "evidence",
                "drafting",
                "as_of_utc",
                "sampled_at",
            ):
                observed[name_] = placed.get(name_)
            observed["evidence_note"] = (
                placed.get("evidence_note")
                or "An expert arena is distinct from all model weights. CPU-mapped weight buffers do not prove locked physical RAM. Total GPU allocation includes KV and compute."
            )
    return {
        "participant": name,
        "model": mid,
        "profile": p.get("profile"),
        "context": p.get("context"),
        "reasoning": p.get("reasoning", "default"),
        "configured": requested,
        "observed": observed,
    }
