"""Serve the dated model screen and quality-checked workstation measurements."""

import hashlib
import json
import math
import shutil
from collections import defaultdict
from datetime import UTC, datetime
from pathlib import Path
from statistics import median

GIB = 1024**3
EVIDENCE = Path(__file__).with_name("candidate-evidence.json")


def read_report(path):
    if not path.is_file() or path.stat().st_size > 16 * 1024**2:
        raise ValueError("missing or oversized benchmark report")
    with path.open("rb") as source:
        return json.load(source)


def finite(value):
    return type(value) in (int, float) and math.isfinite(value) and value >= 0


def metric_if_positive(value):
    return value if finite(value) and value > 0 else None


def observation(cell, aliases, source):
    """Recompute acceptance, counts and rates from tickets before admitting a cell."""
    if cell.get("status") != "completed" or cell.get("model") not in aliases:
        return None
    tickets = cell.get("tickets")
    wall = metric_if_positive(cell.get("wall_s"))
    if not isinstance(tickets, list) or not tickets or not wall:
        return None
    if len(tickets) != cell.get("returned_tickets") or len(tickets) != cell.get(
        "jobs_expected"
    ):
        return None
    if any(
        not isinstance(t, dict)
        or type(t.get("job")) is not int
        or not isinstance(t.get("task"), str)
        for t in tickets
    ):
        return None
    if len({t["job"] for t in tickets}) != len(tickets):
        return None
    passed = sum(
        t.get("complete") is True
        and t.get("correct") is True
        and t.get("context_qualified") is True
        and t.get("verified") is True
        and t.get("status") == 200
        and t.get("finish_reason") in ("stop", "tool_calls")
        for t in tickets
    )
    if passed != cell.get("verified"):
        return None
    completed = [t for t in tickets if t.get("complete") is True]
    if len(completed) != cell.get("completed_streams"):
        return None
    counted = bool(completed) and all(
        type(t.get("generated_tokens")) is int and t["generated_tokens"] >= 0
        for t in completed
    )
    counted = counted and not any(
        t.get("status") == 200 and t.get("complete") is not True for t in tickets
    )
    aggregate = (
        sum(t["generated_tokens"] for t in completed) / wall if counted else None
    )
    saved_rate = cell.get("aggregate_generated_tps")
    if aggregate is None:
        if saved_rate is not None:
            return None
    elif not finite(saved_rate) or not math.isclose(
        aggregate, saved_rate, rel_tol=1e-6
    ):
        return None
    tasks_h = 3600 * passed / wall
    if not finite(cell.get("verified_tasks_h")) or not math.isclose(
        tasks_h, cell["verified_tasks_h"], rel_tol=1e-6
    ):
        return None
    # Offer counts do not imply execution concurrency. A rejected request never passes.
    rejected = sum(t.get("status") == 429 for t in tickets)
    latencies = [t["ttft_s"] for t in completed if finite(t.get("ttft_s"))]
    telemetry = cell.get("telemetry") or {}
    gpu = cell.get("gpu")
    device = (telemetry.get("gpus") or {}).get(gpu) or {}
    cap = cell.get("context_budget", 0)
    if not finite(cap):
        return None
    taskset = sorted((t["job"], t["task"]) for t in tickets)
    cohort = [
        cap,
        cell.get("completion_budget"),
        cell.get("temperature"),
        cell.get("seed"),
        cell.get("corpus_sha256") if cap else "short",
        taskset,
    ]
    cohort_id = hashlib.sha256(
        json.dumps(cohort, separators=(",", ":")).encode()
    ).hexdigest()[:16]
    decode = [
        t["generation_tps_server"]
        for t in completed
        if metric_if_positive(t.get("generation_tps_server"))
    ]
    grade = passed == len(tickets)
    # This harness scores final, accepted stream outputs (including reasoning), without
    # recording a distinct count of answer-only tokens or proving simultaneous two-GPU capacity.
    return {
        "model": aliases[cell["model"]],
        "engine_model_id": cell["model"],
        "source": source,
        "cell": cell.get("id"),
        "run_utc": cell.get("start_utc"),
        "gpu": gpu,
        "slots": cell.get("slots"),
        "offered_clients": cell.get("offered_clients"),
        "context_budget": cap,
        "generation_budget": cell.get("completion_budget"),
        "cohort": cohort_id,
        "task_mix": [t[1] for t in taskset],
        "offered": len(tickets),
        "verified": passed,
        "http_429": rejected,
        "quality_gate": grade,
        "wall_s": wall,
        "verified_tasks_per_hour": tasks_h,
        "aggregate_generated_tps": aggregate,
        "median_decode_tps": median(decode) if decode else None,
        "median_ttft_s": median(latencies) if latencies else None,
        "peak_vram_mib": device.get("gpu_memory_mib_max")
        if finite(device.get("gpu_memory_mib_max"))
        else None,
        "validated_local_opus_parity": False,
        "sustained_independent_pair_gate": "unverified",
    }


def service_observation(run, aliases):
    """Use the independent daemon's own completed structured-check receipts."""
    model = run.get("model")
    if run.get("state") != "completed" or model not in aliases:
        return None
    tickets = run.get("tickets")
    wall = metric_if_positive(run.get("wall_s"))
    if (
        not isinstance(tickets, list)
        or not tickets
        or not wall
        or run.get("issued") != len(tickets)
    ):
        return None
    tasks = [t.get("task") for t in tickets if isinstance(t, dict)]
    expected = ["boundary", "contract"] * (len(tickets) // 2)
    if tasks != expected or not expected:
        return None
    passed = sum(t.get("correct") is True for t in tickets)
    if passed != run.get("passed") or run.get("quality_gate_passed") is not (
        passed == len(tickets)
    ):
        return None
    counted = all(
        type(t.get("accepted_output_tokens")) is int
        and t["accepted_output_tokens"] >= 0
        for t in tickets
    )
    aggregate = (
        sum(t["accepted_output_tokens"] for t in tickets) / wall if counted else None
    )
    recorded = run.get("accepted_output_tps")
    if aggregate is None:
        if recorded is not None:
            return None
    elif not finite(recorded) or not math.isclose(recorded, aggregate, rel_tol=1e-6):
        return None
    verified_h = 3600 * passed / wall
    if not finite(run.get("verified_tasks_per_hour")) or not math.isclose(
        verified_h, run["verified_tasks_per_hour"], rel_tol=1e-6
    ):
        return None
    cap = run.get("max_tokens")
    cohort = (
        "service-v1",
        tuple(tasks),
        cap if type(cap) is int and cap > 0 else run["id"],
    )
    latency = [t["first_token_s"] for t in tickets if finite(t.get("first_token_s"))]
    decodes = [
        t["server_timings"]["predicted_per_second"]
        for t in tickets
        if isinstance(t.get("server_timings"), dict)
        and metric_if_positive(t["server_timings"].get("predicted_per_second"))
    ]
    start = run.get("started_epoch")
    return {
        "model": aliases[model],
        "engine_model_id": model,
        "source": "Dulche benchmark scheduler",
        "cell": run["id"],
        "run_utc": datetime.fromtimestamp(start, UTC).isoformat()
        if finite(start) and start < 32_503_680_000
        else None,
        "profile": run.get("profile"),
        "engine": run.get("engine"),
        "gpu": run.get("gpu"),
        "slots": None,
        "offered_clients": 1,
        "context_budget": 0,
        "configured_context": run.get("context"),
        "generation_budget": cap,
        "cohort": hashlib.sha256(json.dumps(cohort).encode()).hexdigest()[:16],
        "task_mix": tasks,
        "offered": len(tickets),
        "verified": passed,
        "http_429": 0,
        "quality_gate": passed == len(tickets),
        "wall_s": wall,
        "verified_tasks_per_hour": verified_h,
        "aggregate_generated_tps": aggregate,
        "median_decode_tps": median(decodes) if decodes else None,
        "median_ttft_s": median(latency) if latency else None,
        "peak_vram_mib": None,
        "validated_local_opus_parity": False,
        "sustained_independent_pair_gate": "unverified",
    }


def candidate_report(
    state_dir, benchmark_files=(), *, service_runs=(), installed_models=()
):
    result = read_report(EVIDENCE)
    root = Path(state_dir)
    free = shutil.disk_usage(root if root.exists() else root.parent).free
    aliases = {
        name: row["id"]
        for row in result["items"]
        for name in [row["id"], *row["local_model_aliases"]]
    }
    # Associate an installed conversion only when the recorded Hub revision and selected
    # file match the evidence. A similarly named local file is insufficient.
    for local in installed_models:
        for candidate in result["items"]:
            for quant in candidate["quantizations"]:
                if (local.get("repo"), local.get("revision")) == (
                    quant["repo"],
                    quant["revision"],
                ) and any(
                    Path(f["path"]).name == Path(local.get("path", "")).name
                    for f in quant["files"]
                ):
                    aliases[local["id"]] = candidate["id"]
    all_rows = []
    seen = set()
    rejected_cells = 0
    invalid_reports = 0
    paths = [Path(x) for x in benchmark_files]
    directory = root / "benchmarks"
    if directory.is_dir():
        paths += sorted(directory.glob("*.json"))
    for path in dict.fromkeys(p.resolve() for p in paths):
        try:
            report = read_report(path)
            if (
                not isinstance(report, dict)
                or report.get("schema_version") != 1
                or not isinstance(report.get("cells"), list)
            ):
                invalid_reports += 1
                continue
            for cell in report["cells"]:
                if not isinstance(cell, dict):
                    rejected_cells += 1
                    continue
                row = observation(cell, aliases, path.name)
                if row is None:
                    rejected_cells += 1
                    continue
                key = (row["model"], row["cell"], row["run_utc"], row["cohort"])
                if key not in seen:
                    seen.add(key)
                    all_rows.append(row)
        except (OSError, ValueError, TypeError):
            invalid_reports += 1
    unmapped_runs = 0
    for run in service_runs:
        if not isinstance(run, dict) or not isinstance(run.get("id"), str):
            continue
        row = service_observation(run, aliases)
        if row is None:
            unmapped_runs += 1
        elif ("service", row["cell"]) not in seen:
            seen.add(("service", row["cell"]))
            all_rows.append(row)
    groups = defaultdict(list)
    for row in all_rows:
        if row["quality_gate"]:
            groups[row["cohort"]].append(row)
    leaderboard = []
    for cohort, rows in groups.items():
        # Different inferential tasks, context lengths and output caps stay in different rankings.
        rows = sorted(rows, key=lambda x: (-x["verified_tasks_per_hour"], x["model"]))
        fastest = max(
            (r for r in rows if r["aggregate_generated_tps"] is not None),
            key=lambda r: r["aggregate_generated_tps"],
            default=None,
        )
        fastest_ref = (
            {
                k: fastest[k]
                for k in ("source", "cell", "model", "aggregate_generated_tps")
            }
            if fastest
            else None
        )
        leaderboard.append(
            {
                "cohort": cohort,
                "context_budget": rows[0]["context_budget"],
                "task_mix": rows[0]["task_mix"],
                "generation_budget": rows[0]["generation_budget"],
                "ranked_by_verified_tasks_per_hour": rows,
                "highest_aggregate_generated_tps": fastest_ref,
            }
        )
    for item in result["items"]:
        item["local_measurements"] = [r for r in all_rows if r["model"] == item["id"]]
        for artifact in item["quantizations"]:
            size = artifact["bytes"]
            artifact["free_bytes_after_single_copy_if_absent"] = free - size
            artifact["storage_admissible_now"] = (
                free > 60 * GIB and free - size >= 50 * GIB
            )
        item["automatic_parameter_gate"] = 0 < item["parameters"] <= 500_000_000_000
        item["recommended_for_automatic_deployment"] = False
    result.update(
        available_storage_bytes=free,
        storage_floor_bytes=50 * GIB,
        storage_pause_bytes=60 * GIB,
        leaderboards=leaderboard,
        quality_incomplete_trials=[r for r in all_rows if not r["quality_gate"]],
        invalid_or_partial_cells=rejected_cells,
        invalid_reports=invalid_reports,
        unranked_service_runs=unmapped_runs,
        specialist_role_results={
            key: None
            for key in (
                "best_ui_designer",
                "best_coordinator",
                "best_split_gpu",
                "best_overall_balance",
            )
        },
        role_evidence_note="The source workload verifies SQL, a Python expression and a tool call. It supplies no scored Atlas design, coordinator, or validated joint two-GPU role trial. Single-lane rates and independent runs are not added.",
    )
    return result
