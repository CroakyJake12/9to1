"""Count reported accepted tokens across a common monotonic interval. Never count SSE chunks as tokens."""

import time


def sample(ctx, doc, entry):
    run = entry.get("run")
    if not run or entry["kind"] == "tools":
        return
    rows = ctx.state.events(run, entry.get("cursor", 0), limit=128)
    now = time.monotonic()
    wall = time.time()
    for e in rows:
        entry["cursor"] = e["seq"]
        if e["kind"] != "chunks":
            continue
        for packet in e["data"]:
            timing = packet.get("timings") or {}
            usage = packet.get("usage") or {}
            count = timing.get("predicted_n")
            if type(count) is int and count > entry.get("rolling_reported", 0):
                delta = count - entry.get("rolling_reported", 0)
                entry["rolling_reported"] = count
                # Final-only accounting cannot reveal which part of a long request produced the tokens.
                if not usage and not any(
                    c.get("finish_reason") for c in packet.get("choices", [])
                ):
                    t = now - max(0, wall - e["ts"])
                    doc["samples"].append([t, entry["worker"], delta])
                    entry["live_counts"] = True
            if type(usage.get("completion_tokens")) is int:
                entry["usage_tokens"] = usage["completion_tokens"]
    doc["samples"] = [r for r in doc["samples"] if r[0] >= now - 60]
    # If a daemon restarts, the buffered cursor, cumulative count and samples recover as one document.
    return len(rows)


def metrics(doc, window=30):
    now = doc.get("ended_mono") or time.monotonic()
    elapsed = max(0, now - doc["started_mono"])
    duration = min(float(window), elapsed)
    all_entries = [
        e for e in doc["jobs"].values() if e["kind"] != "tools" and e.get("run")
    ]
    recent = [r for r in doc["samples"] if now - duration <= r[0] <= now]
    workers = {}
    for e in all_entries:
        w = workers.setdefault(
            e["worker"],
            {
                "worker": e["worker"],
                "model": e["model"],
                "role": e["role"],
                "accepted_tokens": 0,
                "finished_request_s": 0,
                "completed_requests": 0,
                "missing_usage": 0,
            },
        )
        if e.get("handled"):
            tokens = e.get("usage_tokens")
            if type(tokens) is int and tokens >= 0:
                w["accepted_tokens"] += tokens
                w["finished_request_s"] += e.get("response_s") or 0
            else:
                w["missing_usage"] += 1
            w["completed_requests"] += 1
    for w in workers.values():
        secs = w["finished_request_s"]
        w["individual_tps"] = (
            w["accepted_tokens"] / secs if secs > 0 and not w["missing_usage"] else None
        )
    rolling = []
    for name, w in workers.items():
        count = sum(r[2] for r in recent if r[1] == name)
        relevant = [
            e
            for e in all_entries
            if e["worker"] == name
            and (not e.get("ended_mono") or e["ended_mono"] >= now - duration)
        ]
        missing = sum(not e.get("live_counts") for e in relevant)
        rolling.append(
            {
                "worker": name,
                "role": w["role"],
                "model": w["model"],
                "accepted_tokens": count,
                "tps": count / duration if duration and not missing else None,
                "missing_usage": missing,
            }
        )
    accepted = sum(w["accepted_tokens"] for w in workers.values())
    miss = sum(w["missing_usage"] for w in workers.values())
    rolling_missing = sum(w["missing_usage"] for w in rolling)
    rcount = sum(r[2] for r in recent)
    verified = sum(t["verified"] for t in doc["tasks"].values())
    return {
        "quality_outcomes": {
            label: sum(e.get("failure_class") == label for e in all_entries)
            for label in (
                "reasoning_only_budget_exhausted",
                "generation_budget_exhausted",
            )
        },
        "basis": "Engine-reported accepted output tokens including reported reasoning. Visible-only token counts unavailable. Rolling rates use incremental timing counters only; final-only usage does not get assigned to a short window.",
        "rolling": {
            "window_seconds": window,
            "elapsed_s": duration,
            "accepted_tokens": rcount,
            "workers": rolling,
            "aggregate_tps": rcount / duration
            if duration and not rolling_missing
            else None,
            "missing_usage": rolling_missing,
        },
        "finished": {
            "elapsed_s": elapsed,
            "accepted_tokens": accepted,
            "workers": list(workers.values()),
            "aggregate_tps": accepted / elapsed if elapsed and not miss else None,
            "missing_usage": miss,
            "verified_tasks_h": verified * 3600 / elapsed if elapsed else None,
        },
        "performance_gates": "Require sustained physical model trials; no automatic admission based on microbenchmarks.",
        "atlas_fidelity": None,
        "idle_gpu_time_s": None,
        "strategy_comparison": "unmeasured until matched trials finish",
    }


def comparisons(docs):
    """Compare only completed, equally verified workloads with an identical task DAG and role configuration."""
    import hashlib
    import json
    import statistics

    groups = {}
    trials = []
    for doc in docs:
        if doc["state"] not in ("completed", "completed_unverified"):
            continue
        cfg = doc["config"]
        m = metrics(doc)["finished"]
        verified = sum(t["verified"] for t in doc["tasks"].values())
        key = {
            "objective": cfg["objective"],
            "criteria": cfg["completion_criteria"],
            "mentor": cfg["mentor"],
            "workers": cfg["workers"],
            "concurrency": cfg["max_workers"],
            "check_in": cfg["check_in"],
            "limits": cfg["limits"],
            "project_state": cfg.get("project_state"),
            "tasks": sorted(
                (
                    t["id"],
                    t["role"],
                    t["instruction"],
                    t["acceptance"],
                    tuple(t["depends_on"]),
                )
                for t in doc["tasks"].values()
            ),
        }
        cohort = hashlib.sha256(json.dumps(key, sort_keys=True).encode()).hexdigest()[
            :20
        ]
        concurrency_key = {k: v for k, v in key.items() if k != "concurrency"}
        concurrency_key["strategy"] = cfg["strategy"]
        concurrency_cohort = hashlib.sha256(
            json.dumps(concurrency_key, sort_keys=True).encode()
        ).hexdigest()[:20]
        checks = [
            decision["exclusive_check_in_s"]
            for decision in doc["decisions"]
            if "exclusive_check_in_s" in decision
        ]
        active_time = m["elapsed_s"] - sum(checks)
        row = {
            "swarm_id": doc["id"],
            "strategy": cfg["strategy"],
            "cohort": cohort,
            "concurrency_cohort": concurrency_cohort,
            "max_workers": cfg["max_workers"],
            "mentor_reasoning": cfg["mentor"].get("reasoning", "default"),
            "worker_reasoning": [w.get("reasoning", "default") for w in cfg["workers"]],
            "tasks": len(doc["tasks"]),
            "verified": verified,
            "verified_tasks_h": m["verified_tasks_h"],
            "aggregate_tps": m["aggregate_tps"],
            "common_wall_s": m["elapsed_s"],
            "workers": m["workers"],
            "check_ins": len(checks),
            "mean_exclusive_check_in_s": statistics.mean(checks) if checks else None,
            "non_checkin_verified_tasks_s": verified / active_time
            if active_time > 0
            else None,
            "all_correct": bool(verified) and verified == len(doc["tasks"]),
            "role_quality": "Only matching operator verifier receipts count; Atlas and semantic quality need specific verifiers.",
        }
        trials.append(row)
        if row["all_correct"] and not doc.get("strategy_changed"):
            groups.setdefault(cohort, []).append(row)
    estimates = []
    for cohort, rows in groups.items():
        for i, a in enumerate(rows):
            for b in rows[i + 1 :]:
                if (
                    a["strategy"] == b["strategy"]
                    or not a["check_ins"]
                    or not b["check_ins"]
                ):
                    continue
                ra, rb = (
                    a["non_checkin_verified_tasks_s"],
                    b["non_checkin_verified_tasks_s"],
                )
                ca, cb = a["mean_exclusive_check_in_s"], b["mean_exclusive_check_in_s"]
                if ra is None or rb is None or abs(ra - rb) < 1e-12:
                    continue
                interval = (ra * ca - rb * cb) / (ra - rb)
                if interval <= max(ca, cb):
                    continue
                estimates.append(
                    {
                        "cohort": cohort,
                        "trials": [a["swarm_id"], b["swarm_id"]],
                        "strategies": [a["strategy"], b["strategy"]],
                        "estimated_break_even_cycle_seconds": interval,
                    }
                )
    shared = []
    for cid in dict.fromkeys(row["concurrency_cohort"] for row in trials):
        members = [r for r in trials if r["concurrency_cohort"] == cid]
        if len({r["max_workers"] for r in members}) >= 2:
            shared.append(
                {
                    "cohort": cid,
                    "trials": [
                        {
                            k: r[k]
                            for k in (
                                "swarm_id",
                                "max_workers",
                                "verified",
                                "tasks",
                                "verified_tasks_h",
                                "aggregate_tps",
                                "all_correct",
                            )
                        }
                        for r in members
                    ],
                    "basis": "Same plan, input references, roles, reasoning settings, stop limits, check-ins and residency strategy. Concurrency varies.",
                }
            )
    return {
        "trials": trials,
        "matched_worker_counts": shared,
        "matched_break_even_estimates": estimates,
        "method": "For matched, fully verified trials, model each cycle as work rate × (1 − exclusive mentor overhead / cycle duration). Rates must remain stable for the estimate to transfer. A positive feasible crossing is required.",
        "four_thousand_tps": "Unverified. Aggregate accepted tokens divide by one common interval; request rates are never summed.",
        "separate_instance_comparison": "Requires distinct configured model-instance IDs and comparable measured trials.",
    }


def resource_sample(doc, resources):
    now = time.monotonic()
    summary = doc.setdefault(
        "resource_summary",
        {
            "sample_count": 0,
            "gpus": {},
            "minimum_host_available_mib": None,
            "peak_desktop_roundtrip_ms": None,
        },
    )
    last = summary.get("last_sample_mono")
    if last is not None and now - last < 4:
        return
    dt = min(8, max(0, now - last)) if last is not None and now - last <= 8 else 0
    summary["last_sample_mono"] = now
    summary["sample_count"] += 1
    available = resources.get("ram_available_mib", resources.get("host_available_mib"))
    if type(available) in (int, float):
        old = summary["minimum_host_available_mib"]
        summary["minimum_host_available_mib"] = (
            min(old, available) if old is not None else available
        )
    lag = resources.get("desktop_roundtrip_ms")
    if type(lag) in (int, float):
        summary["peak_desktop_roundtrip_ms"] = max(
            summary["peak_desktop_roundtrip_ms"] or 0, lag
        )
    for gpu in resources.get("gpus", []):
        if not isinstance(gpu, dict) or not gpu.get("name"):
            continue
        name = gpu["name"]
        g = summary["gpus"].setdefault(
            name,
            {
                "sampled_seconds": 0,
                "sampled_idle_seconds": 0,
                "peak_used_mib": None,
                "peak_temp_c": None,
            },
        )
        util = gpu.get("utilization")
        if type(util) in (int, float):
            g["sampled_seconds"] += dt
            if util <= 5:
                g["sampled_idle_seconds"] += dt
        for dest, src in [
            ("peak_used_mib", "used_mib"),
            ("peak_temp_c", "temperature_c"),
        ]:
            if type(gpu.get(src)) in (int, float):
                g[dest] = max(g[dest] or 0, gpu[src])
