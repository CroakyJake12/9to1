"""Service-owned benchmark tickets. Separate accepted-token speed, latency and correctness."""

import json
from pathlib import Path
import statistics
import time
from state import Fault

# Deliberately deterministic, small diagnostics. Full repository and Atlas outcomes are independent evidence.
CASES = (
    (
        "boundary",
        "Return a JSON list giving max(0,min(b,d)-max(a,c)) for intervals "
        "(0,2,2,4), (1,6,3,10), (5,5,2,8), in that order. Return only the JSON list.",
        [0, 3, 0],
    ),
    (
        "contract",
        'Given {"files":["b","a","b"]}, produce a JSON object with count equal to the number '
        "of distinct files and files equal to their sorted unique names. Return only JSON.",
        {"count": 2, "files": ["a", "b"]},
    ),
)


def submit(ctx, body, key):
    mid = body.get("model")
    if not isinstance(mid, str):
        raise Fault(400, "Select an installed, loaded model")
    ctx.permit(mid)
    repeats = int(body.get("repeats", 1))
    cap = int(body.get("max_tokens", 1024))
    if not 1 <= repeats <= 10 or not 16 <= cap <= 8192:
        raise Fault(400, "Invalid repeats or output limit")
    model = ctx.model(mid)
    steps = []
    for _ in range(repeats):
        for _, prompt, _ in CASES:
            steps.append(
                {
                    "type": "generate",
                    "payload": {
                        "messages": [{"role": "user", "content": prompt}],
                        "stream": True,
                        "stream_options": {"include_usage": True},
                        "temperature": 0,
                        "seed": 4703,
                        "max_tokens": cap,
                    },
                }
            )
    return ctx.jobs.create(
        {
            "kind": "benchmark",
            "model": mid,
            "objective": "Dulche structured coding checks",
            "profile": model["profile_id"],
            "settings": model.get("settings", {}),
            "engine": model.get("engine"),
            "gpu": model.get("gpu"),
            "context": model.get("context"),
            "max_tokens": cap,
            "steps": steps,
        },
        key,
    )


def result(ctx, run):
    info = ctx.state.status(run, full=True)
    req = info["request"]
    if req.get("kind") != "benchmark":
        raise Fault(404, "Not a benchmark run")
    rows = []
    for i, answer in enumerate(info["completed_steps"]):
        name, _, expected = CASES[i % len(CASES)]
        choice = answer["choices"][0]
        raw = (choice.get("message", {}).get("content") or "").strip()
        try:
            correct = (
                json.loads(raw) == expected and choice.get("finish_reason") == "stop"
            )
        except ValueError:
            correct = False
        tokens = answer.get("usage", {}).get("completion_tokens")
        rows.append(
            {
                "task": name,
                "correct": correct,
                "accepted_output_tokens": tokens,
                "elapsed_s": answer.get("elapsed_s"),
                "first_token_s": answer.get("ttft_s"),
                "server_timings": answer.get("timings"),
            }
        )
    wall = max(0.000001, info["updated"] - info["created"])
    passed = sum(r["correct"] for r in rows)
    all_counted = bool(rows) and all(
        isinstance(r["accepted_output_tokens"], int) for r in rows
    )
    tps = sum(r["accepted_output_tokens"] for r in rows) / wall if all_counted else None
    done = info["state"] == "completed"
    return {
        "id": run,
        "model": req["model"],
        "state": info["state"],
        "profile": req["profile"],
        "engine": req["engine"],
        "gpu": req["gpu"],
        "context": req["context"],
        "settings": req["settings"],
        "max_tokens": req.get("max_tokens"),
        "started_epoch": info["created"],
        "issued": len(req["steps"]),
        "passed": passed,
        "wall_s": wall,
        "accepted_output_tps": tps,
        "verified_tasks_per_hour": 3600 * passed / wall if done else None,
        "tickets": rows,
        "quality_gate_passed": done and passed == len(req["steps"]),
        "microbenchmark_speed_gate_passed": bool(
            done and tps is not None and tps >= 20
        ),
        "scope": "Short single-model tickets. Sustained throughput, full coding, tool reliability and visual fidelity need separate evidence.",
    }


def listing(ctx):
    with ctx.lock:
        ids = [
            r[0]
            for r in ctx.state.db.execute(
                "SELECT id FROM runs WHERE json_extract(request,'$.kind')='benchmark' ORDER BY created DESC LIMIT 100"
            )
        ]
    return [result(ctx, r) for r in ids]
