#!/usr/bin/env python3
"""Explicit-assumption RAM/VRAM planning estimates for llama.cpp models."""
from __future__ import annotations

import argparse
import json
import math
import pathlib
from typing import Any

SCALAR_BYTES = {"f32": 4, "f16": 2, "bf16": 2}


class BudgetError(RuntimeError):
    pass


def transformer_kv_payload_bytes(*, layers: int, context: int, kv_heads: int, head_dim: int, k_type: str, v_type: str) -> int:
    for name, value in {"layers": layers, "context": context, "kv_heads": kv_heads, "head_dim": head_dim}.items():
        if value <= 0:
            raise BudgetError(f"{name} must be positive")
    if k_type not in SCALAR_BYTES or v_type not in SCALAR_BYTES:
        raise BudgetError("KV estimator supports f32, f16 and bf16 scalar storage only")
    elements_per_cache = layers * context * kv_heads * head_dim
    return elements_per_cache * (SCALAR_BYTES[k_type] + SCALAR_BYTES[v_type])


def estimate_budget(
    *,
    weights_bytes: int,
    kv_bytes: int = 0,
    gpu_offload_fraction: float = 0.0,
    kv_location: str = "cpu",
    runtime_overhead_fraction: float = 0.15,
    safety_fraction: float = 0.10,
) -> dict[str, Any]:
    if weights_bytes <= 0 or kv_bytes < 0:
        raise BudgetError("weights must be positive and KV bytes non-negative")
    if not 0.0 <= gpu_offload_fraction <= 1.0:
        raise BudgetError("gpu_offload_fraction must be between 0 and 1")
    if kv_location not in {"cpu", "gpu"}:
        raise BudgetError("kv_location must be cpu or gpu")
    if runtime_overhead_fraction < 0 or safety_fraction < 0:
        raise BudgetError("overhead and safety fractions cannot be negative")

    gpu_weights = math.ceil(weights_bytes * gpu_offload_fraction)
    cpu_weights = weights_bytes - gpu_weights
    cpu_floor = cpu_weights + (kv_bytes if kv_location == "cpu" else 0)
    gpu_floor = gpu_weights + (kv_bytes if kv_location == "gpu" else 0)

    def recommended(floor: int) -> tuple[int, int, int]:
        overhead = math.ceil(floor * runtime_overhead_fraction)
        subtotal = floor + overhead
        safety = math.ceil(subtotal * safety_fraction)
        return overhead, safety, subtotal + safety

    cpu_overhead, cpu_safety, cpu_recommended = recommended(cpu_floor)
    gpu_overhead, gpu_safety, gpu_recommended = recommended(gpu_floor)
    return {
        "schemaVersion": 1,
        "method": "planning-estimate-not-runtime-measurement",
        "weightsBytes": weights_bytes,
        "kvPayloadBytes": kv_bytes,
        "gpuOffloadFraction": gpu_offload_fraction,
        "kvLocation": kv_location,
        "cpu": {
            "planningFloorBytes": cpu_floor,
            "runtimeOverheadBytes": cpu_overhead,
            "safetyBytes": cpu_safety,
            "recommendedBudgetBytes": cpu_recommended,
        },
        "gpu": {
            "planningFloorBytes": gpu_floor,
            "runtimeOverheadBytes": gpu_overhead,
            "safetyBytes": gpu_safety,
            "recommendedBudgetBytes": gpu_recommended,
        },
        "assumptions": [
            "Weight offload fraction is a coarse byte split, not a layer-aware llama.cpp allocator simulation.",
            "KV payload formula applies to standard transformer K/V geometry and excludes allocator padding/metadata.",
            "Runtime overhead and safety margins are policy estimates, not benchmark evidence.",
            "File size is used as the weight-byte planning input; actual resident memory may differ due to mmap/offload behavior."
        ],
    }


def _weights_from_args(args: argparse.Namespace) -> int:
    if args.model_file is not None:
        if not args.model_file.is_file():
            raise BudgetError("model file does not exist or is not regular")
        return args.model_file.stat().st_size
    if args.weights_bytes is None:
        raise BudgetError("provide --model-file or --weights-bytes")
    return args.weights_bytes


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Estimate RAM/VRAM planning budgets with explicit assumptions")
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--model-file", type=pathlib.Path)
    source.add_argument("--weights-bytes", type=int)
    parser.add_argument("--layers", type=int)
    parser.add_argument("--context", type=int)
    parser.add_argument("--kv-heads", type=int)
    parser.add_argument("--head-dim", type=int)
    parser.add_argument("--k-type", choices=sorted(SCALAR_BYTES), default="f16")
    parser.add_argument("--v-type", choices=sorted(SCALAR_BYTES), default="f16")
    parser.add_argument("--gpu-offload-fraction", type=float, default=0.0)
    parser.add_argument("--kv-location", choices=("cpu", "gpu"), default="cpu")
    parser.add_argument("--runtime-overhead", type=float, default=0.15)
    parser.add_argument("--safety", type=float, default=0.10)
    args = parser.parse_args(argv)

    geometry = (args.layers, args.context, args.kv_heads, args.head_dim)
    if any(value is not None for value in geometry) and not all(value is not None for value in geometry):
        raise SystemExit("KV geometry requires --layers, --context, --kv-heads and --head-dim together")
    kv_bytes = 0
    if all(value is not None for value in geometry):
        kv_bytes = transformer_kv_payload_bytes(
            layers=args.layers, context=args.context, kv_heads=args.kv_heads, head_dim=args.head_dim,
            k_type=args.k_type, v_type=args.v_type,
        )
    result = estimate_budget(
        weights_bytes=_weights_from_args(args), kv_bytes=kv_bytes,
        gpu_offload_fraction=args.gpu_offload_fraction, kv_location=args.kv_location,
        runtime_overhead_fraction=args.runtime_overhead, safety_fraction=args.safety,
    )
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
