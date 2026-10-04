#!/usr/bin/env python3
"""Validate Haven llama.cpp backend evidence and derive probe candidates."""
from __future__ import annotations

import argparse
import json
import pathlib
from typing import Any

EVIDENCE_ORDER = ("inspected", "built", "tested", "runtimeProven", "benchmarked")


class BackendMatrixError(RuntimeError):
    pass


def load_matrix(path: pathlib.Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise BackendMatrixError("backend matrix must be a JSON object")
    validate_matrix(value)
    return value


def validate_matrix(matrix: dict[str, Any]) -> None:
    if matrix.get("schemaVersion") != 1:
        raise BackendMatrixError("unsupported backend matrix schema")
    if matrix.get("policy", {}).get("automaticGpuEnablement") is not False:
        raise BackendMatrixError("automatic GPU enablement must remain disabled until runtime evidence exists")
    backends = matrix.get("backends")
    if not isinstance(backends, list) or not backends:
        raise BackendMatrixError("backends must be a non-empty list")
    seen: set[str] = set()
    for backend in backends:
        if not isinstance(backend, dict):
            raise BackendMatrixError("backend entry must be an object")
        backend_id = str(backend.get("id", ""))
        if not backend_id or backend_id in seen:
            raise BackendMatrixError(f"invalid or duplicate backend id: {backend_id}")
        seen.add(backend_id)
        evidence = backend.get("evidence")
        if not isinstance(evidence, dict):
            raise BackendMatrixError(f"{backend_id}: missing evidence")
        values: dict[str, bool] = {}
        for key in EVIDENCE_ORDER:
            item = evidence.get(key)
            if not isinstance(item, dict) or not isinstance(item.get("value"), bool):
                raise BackendMatrixError(f"{backend_id}: invalid evidence state {key}")
            values[key] = item["value"]
            if item["value"] and not str(item.get("scope", "")).strip():
                raise BackendMatrixError(f"{backend_id}: true evidence state {key} needs scope")
            if item["value"] and not str(item.get("reference", "")).strip():
                raise BackendMatrixError(f"{backend_id}: true evidence state {key} needs a reference")
        if values["built"] and not values["inspected"]:
            raise BackendMatrixError(f"{backend_id}: built cannot precede inspected")
        if values["tested"] and not values["built"]:
            raise BackendMatrixError(f"{backend_id}: tested cannot precede built")
        if values["runtimeProven"] and not values["tested"]:
            raise BackendMatrixError(f"{backend_id}: runtimeProven requires tested")
        if values["benchmarked"] and not values["runtimeProven"]:
            raise BackendMatrixError(f"{backend_id}: benchmarked requires runtimeProven")
    if matrix.get("approvedVm", {}).get("hardwareInspection", {}).get("status") not in {"not-run", "observed"}:
        raise BackendMatrixError("approved VM hardware inspection status is invalid")


def candidate_backends(probe: dict[str, Any], matrix: dict[str, Any]) -> list[dict[str, Any]]:
    """Return hardware candidates only; this never promotes evidence status."""
    vendors = {str(item.get("vendorName", "")).lower() for item in probe.get("gpus", []) if isinstance(item, dict)}
    is_linux = str(probe.get("platform", {}).get("system", "")).lower() == "linux"
    result: list[dict[str, Any]] = []
    for backend in matrix["backends"]:
        backend_id = backend["id"]
        detected = False
        reason = "no matching observed hardware"
        if backend_id == "cpu":
            detected = is_linux
            reason = "Linux CPU baseline" if detected else "non-Linux probe"
        elif backend_id == "cuda":
            detected = "nvidia" in vendors
            reason = "NVIDIA DRM device observed" if detected else reason
        elif backend_id == "hip":
            detected = "amd" in vendors
            reason = "AMD DRM device observed" if detected else reason
        elif backend_id == "vulkan":
            detected = bool(vendors)
            reason = "DRM GPU observed; Vulkan runtime/toolchain still requires validation" if detected else reason
        elif backend_id == "sycl":
            detected = "intel" in vendors
            reason = "Intel DRM device observed" if detected else reason
        elif backend_id == "opencl":
            detected = bool(vendors)
            reason = "GPU observed; OpenCL runtime still requires validation" if detected else reason
        elif backend_id == "openvino":
            detected = False
            reason = "OpenVINO candidacy requires explicit runtime/device inspection; Linux presence alone is insufficient"
        result.append({
            "id": backend_id,
            "hardwareCandidate": detected,
            "reason": reason,
            "runtimeProven": backend["evidence"]["runtimeProven"]["value"],
        })
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("matrix", type=pathlib.Path)
    parser.add_argument("--probe", type=pathlib.Path)
    args = parser.parse_args(argv)
    matrix = load_matrix(args.matrix)
    output: dict[str, Any] = {"valid": True, "backends": matrix["backends"]}
    if args.probe:
        probe = json.loads(args.probe.read_text(encoding="utf-8"))
        output["candidates"] = candidate_backends(probe, matrix)
    print(json.dumps(output, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
