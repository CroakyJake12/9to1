"""Thunder offline benchmark harness — SOURCE-ONLY.

This module is authored offline and has NOT executed on any owner PC. It implements the fail-closed
guard contract, interleaved existing/upstream/optimised scheduling, bounded process lifetime, the
candidate `strata --serve` line-protocol parser, raw result recording, and median/variability
aggregation. No measurement here is a benchmark result; nothing runs without a positive, in-window,
authorised endpoint.

Run: `python3 -m unittest thunder_bench.test_*` (build-free, deterministic fixtures only).
"""

from __future__ import annotations

import argparse
import dataclasses
import datetime
import json
import math
import os
import re
import signal
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

# ------------------------------------------------------------------ owner permission window (absolute UTC)
def _utc_epoch(year: int, month: int, day: int, hour: int, minute: int = 0, second: int = 0) -> float:
    return datetime.datetime(year, month, day, hour, minute, second,
                             tzinfo=datetime.timezone.utc).timestamp()


DEFAULT_OWNER_WINDOW_START_UTC = _utc_epoch(2026, 10, 22, 17, 10)
DEFAULT_OWNER_WINDOW_END_UTC = _utc_epoch(2026, 10, 22, 21, 0)


def _fmt_utc(epoch: float) -> str:
    return datetime.datetime.fromtimestamp(epoch, tz=datetime.timezone.utc).strftime("%Y-%m-%d %H:%M")

# ------------------------------------------------------------------ manifests (immutable)
@dataclasses.dataclass(frozen=True)
class EngineManifest:
    id: str
    kind: str          # "existing" | "upstream" | "optimised"
    binary: Path
    build_receipt_sha: str
    source_lock_sha: str
    profile: str
    source_version: Optional[str] = None   # optional provenance (source/engine version)
    hardware: Optional[str] = None         # optional provenance (no fake hardware)

    def validate(self) -> None:
        if self.kind not in ("existing", "upstream", "optimised"):
            raise ValueError("engine.kind must be existing|upstream|optimised")
        if not self.binary.exists():
            raise ValueError("engine binary missing: %s" % self.binary)


@dataclasses.dataclass(frozen=True)
class ModelManifest:
    id: str
    path: Path
    quantisation: str
    context_tokens: int
    expected_registration: str
    file_sha256: Optional[str] = None   # optional provenance (artifact identity; not just the id label)

    def validate(self) -> None:
        if self.context_tokens <= 0:
            raise ValueError("model context_tokens must be > 0")


@dataclasses.dataclass(frozen=True)
class ConfigManifest:
    id: str
    temperature: float
    top_p: float
    max_new_tokens: int
    prompts: Tuple[str, ...]
    repeats_per_config: int = 5
    timeout_seconds: int = 300

    def validate(self) -> None:
        if self.repeats_per_config < 5:
            raise ValueError("valid methodology requires >=5 valid runs per configuration")
        if self.max_new_tokens <= 0:
            raise ValueError("max_new_tokens must be > 0")
        if not self.prompts:
            raise ValueError("at least one prompt is required")


def load_engine_manifest(path: Path) -> EngineManifest:
    return EngineManifest(**json.loads(path.read_text()))


def load_model_manifest(path: Path) -> ModelManifest:
    return ModelManifest(**json.loads(path.read_text()))


def load_config_manifest(path: Path) -> ConfigManifest:
    d = json.loads(path.read_text())
    d["prompts"] = tuple(d["prompts"])
    return ConfigManifest(**d)


# ------------------------------------------------------------------ scheduling
@dataclasses.dataclass(frozen=True)
class Job:
    engine: EngineManifest
    model: ModelManifest
    config: ConfigManifest
    run_index: int


def build_interleaved_plan(engines: Sequence[EngineManifest],
                           model: ModelManifest,
                           config: ConfigManifest) -> List[Job]:
    """Interleave runs across engines/configs so bias/order effects are spread."""
    jobs: List[Job] = []
    n = config.repeats_per_config
    for i in range(n):
        ordered = engines[i % len(engines):] + engines[: i % len(engines)]  # rotate each repeat
        for e in ordered:
            jobs.append(Job(e, model, config, i))
    return jobs


# ------------------------------------------------------------------ fail-closed guard
@dataclasses.dataclass
class GuardState:
    now_utc: float
    endpoint_verified: bool
    device_verified: bool
    model_verified: bool
    rollback_retained: bool
    window_start_utc: float = DEFAULT_OWNER_WINDOW_START_UTC
    window_end_utc: float = DEFAULT_OWNER_WINDOW_END_UTC

    def check(self) -> List[str]:
        failures: List[str] = []
        # absolute UTC bounds: rejects any other date, before start, and after end (no time-of-day
        # inference from the current clock). Tomorrow requires a new explicit owner permission window.
        if not (self.window_start_utc <= self.now_utc < self.window_end_utc):
            failures.append("outside authorised owner window (%s-%s UTC)"
                            % (_fmt_utc(self.window_start_utc), _fmt_utc(self.window_end_utc)))
        if not self.endpoint_verified:
            failures.append("endpoint not positively verified")
        if not self.device_verified:
            failures.append("device not verified (expected RTX 5080)")
        if not self.model_verified:
            failures.append("model identity not verified from runtime state")
        if not self.rollback_retained:
            failures.append("known-good rollback not retained")
        return failures

    def fail_closed(self) -> bool:
        return bool(self.check())


# ------------------------------------------------------------------ bounded process lifetime
@dataclasses.dataclass
class ProcessOutcome:
    exit_code: Optional[int]
    timed_out: bool
    elapsed_seconds: float
    stdout: str = ""
    stderr: str = ""


class BoundedProcess:
    """Run a command with a hard deadline; kill the whole tree if it overruns."""

    def __init__(self, argv: Sequence[str], timeout_seconds: float, cwd: Optional[Path] = None):
        self.argv = list(argv)
        self.timeout = timeout_seconds
        self.cwd = cwd
        self._proc: Optional[subprocess.Popen] = None
        self._pgid: Optional[int] = None
        self._timer: Optional[threading.Timer] = None
        self._fired = False

    def __enter__(self) -> "BoundedProcess":
        kwargs: Dict[str, object] = {"cwd": self.cwd}
        if os.name == "nt":
            kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
        else:
            kwargs["start_new_session"] = True
        self._proc = subprocess.Popen(self.argv, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, stdin=subprocess.PIPE, **kwargs)
        # With start_new_session the child is its own session/process-group leader, so its pid IS the
        # group id. We keep it so we can reap the whole tree even after the leader exits.
        if os.name != "nt":
            self._pgid = self._proc.pid
        self._timer = threading.Timer(self.timeout, self._timeout_kill)
        self._timer.daemon = True
        self._timer.start()
        return self

    def _kill_tree(self) -> None:
        if self._proc is None:
            return
        # Kill the whole owned process group regardless of whether the leader has already exited, so
        # descendants that outlive the parent (e.g. a backgrounded job) are still reaped.
        try:
            if os.name == "nt":
                subprocess.run(["taskkill", "/T", "/F", "/PID", str(self._proc.pid)],
                               capture_output=True)
            elif self._pgid is not None:
                os.killpg(self._pgid, signal.SIGKILL)
        except Exception:
            try:
                if self._proc.poll() is None:
                    self._proc.kill()
            except Exception:
                pass

    def _timeout_kill(self) -> None:
        self._fired = True
        self._kill_tree()

    def communicate(self, input_bytes: bytes = b"") -> ProcessOutcome:
        assert self._proc is not None
        start = time.monotonic()
        try:
            out, err = self._proc.communicate(input_bytes, timeout=self.timeout + 10)
        except subprocess.TimeoutExpired:
            self._timeout_kill()
            out, err = self._proc.communicate() if self._proc.poll() is not None else (b"", b"")
        elapsed = time.monotonic() - start
        return ProcessOutcome(self._proc.returncode, self._fired, elapsed,
                              out.decode("utf-8", "replace"), err.decode("utf-8", "replace"))

    def __exit__(self, *exc) -> None:
        if self._timer is not None:
            self._timer.cancel()
        self._kill_tree()


# ------------------------------------------------------------------ candidate serve-protocol parser
def parse_gen_line(max_new: int, token_ids: Sequence[int], *,
                   temperature: float = 0.0, top_p: float = 1.0,
                   top_k: int = 0, seed: int = 0) -> str:
    line = "GEN %d" % max_new
    if temperature > 0.0:
        line += " temperature=%g" % temperature
        line += " top_p=%g" % (top_p if top_p > 0.0 else 1.0)
        if top_k:
            line += " top_k=%d" % top_k
        if seed:
            line += " seed=%d" % seed
    line += " " + ",".join(str(t) for t in token_ids) + "\n"
    return line


@dataclasses.dataclass
class DoneMetrics:
    generated: int
    prompt_tokens: int
    prompt_ms: float
    decode_ms: float
    finish: str
    drafts_accepted: int
    drafts_offered: int
    reused: int
    read_n: int


def parse_done_line(line: str) -> DoneMetrics:
    if not line.startswith("DONE "):
        raise ValueError("not a DONE terminal: %r" % line[:20])
    tok = line.split()
    if len(tok) < 9:
        raise ValueError("malformed DONE terminal")
    try:
        return DoneMetrics(int(tok[1]), int(tok[2]), float(tok[3]), float(tok[4]),
                           tok[5], int(tok[6]), int(tok[7]), int(tok[8]), 0)
    except ValueError as e:
        raise ValueError("malformed DONE numbers: %s" % e)


def parse_serve_stream(stream: str) -> Tuple[List[int], DoneMetrics, List[str]]:
    """Parse a `strata --serve` output into (token_ids, done, errors). Pure/guarded."""
    tokens: List[int] = []
    done: Optional[DoneMetrics] = None
    errors: List[str] = []
    for line in stream.splitlines():
        if line.startswith("T "):
            try:
                tokens.append(int(line[2:]))
            except ValueError:
                errors.append("bad T line: %r" % line[:20])
        elif line.startswith("ERR "):
            errors.append(line[4:])
        elif line.startswith("DONE "):
            done = parse_done_line(line)
    if done is None:
        raise ValueError("serve stream ended without a DONE terminal")
    return tokens, done, errors


# ------------------------------------------------------------------ raw results + aggregation
@dataclasses.dataclass
class RunResult:
    job: Job
    outcome: ProcessOutcome
    token_ids: List[int]
    done: DoneMetrics
    errors: List[str]
    phases: Dict[str, Optional[float]]  # load/prefill/decode/TTFT/memory/... (None = unavailable)


def median(values: Sequence[float]) -> Optional[float]:
    if not values:
        return None
    s = sorted(values)
    n = len(s)
    mid = n // 2
    if n % 2:
        return s[mid]
    return (s[mid - 1] + s[mid]) / 2.0


def median_absolute_deviation(values: Sequence[float], med: float) -> float:
    if not values:
        return 0.0
    return median([abs(v - med) for v in values]) or 0.0


def aggregate_metric(results: Sequence[RunResult], key: str) -> Optional[Dict[str, float]]:
    vals = [getattr(r.done, key) for r in results]
    vals = [v for v in vals if v is not None]
    if not vals:
        return None
    med = median(vals)
    return {"median": med, "mad": median_absolute_deviation(vals, med),
            "n": len(vals), "min": min(vals), "max": max(vals)}


# ------------------------------------------------------------------ run validity (fail-closed)
def _run_is_success(r: RunResult) -> bool:
    """A run counts as evidence only if it completed successfully: exit 0, not timed out,
    a DONE terminal, and no recorded errors."""
    o = r.outcome
    if o is None:
        return False
    if o.timed_out:
        return False
    if o.exit_code != 0:
        return False
    if r.done is None:
        return False
    if r.errors:
        return False
    return True


def _done_metric_issues(r: RunResult) -> List[str]:
    """Validate the DONE protocol metric contract used by TPS. Rejects non-integer/zero/negative
    counts, non-finite or non-positive decode_ms, and token-count inconsistency when recorded."""
    issues: List[str] = []
    d = r.done
    if d is None:
        return issues
    if not isinstance(d.generated, int) or d.generated <= 0:
        issues.append("invalid DONE generated=%r" % (d.generated,))
    if not isinstance(d.prompt_tokens, int) or d.prompt_tokens < 0:
        issues.append("invalid DONE prompt_tokens=%r" % (d.prompt_tokens,))
    for nm, v in (("prompt_ms", d.prompt_ms), ("decode_ms", d.decode_ms)):
        try:
            fv = float(v)
        except (TypeError, ValueError):
            issues.append("non-numeric DONE %s=%r" % (nm, v))
            continue
        if not math.isfinite(fv) or fv < 0:
            issues.append("invalid DONE %s=%r" % (nm, v))
    try:
        if float(d.decode_ms) <= 0:
            issues.append("DONE decode_ms must be > 0")
    except (TypeError, ValueError):
        issues.append("DONE decode_ms is not numeric")
    for nm, v in (("drafts_accepted", d.drafts_accepted), ("drafts_offered", d.drafts_offered),
                  ("reused", d.reused), ("read_n", d.read_n)):
        if not isinstance(v, int) or v < 0:
            issues.append("invalid DONE %s=%r" % (nm, v))
    if isinstance(d.drafts_accepted, int) and isinstance(d.drafts_offered, int) \
            and d.drafts_accepted > d.drafts_offered:
        issues.append("DONE drafts_accepted > drafts_offered")
    if r.token_ids and isinstance(d.generated, int) and len(r.token_ids) != d.generated:
        issues.append("DONE generated=%d inconsistent with %d recorded token ids"
                      % (d.generated, len(r.token_ids)))
    return issues


def _measurement_issues(r: RunResult) -> List[str]:
    issues: List[str] = []
    for k, v in r.phases.items():
        if v is None:
            continue
        try:
            fv = float(v)
        except (TypeError, ValueError):
            issues.append("non-numeric phase %s=%r" % (k, v))
            continue
        if not math.isfinite(fv) or fv < 0:
            issues.append("invalid phase %s=%r" % (k, v))
    issues.extend(_done_metric_issues(r))
    return issues


# ------------------------------------------------------------------ provenance identity (fail-closed)
_HEX64_RE = re.compile(r"^[0-9a-fA-F]{64}$")


def _provenance_ok(job: Job) -> bool:
    """Syntactic provenance identity: model artifact sha must be 64 hex and nonblank; engine source
    version and hardware must be nonblank. This is a metadata assertion, NOT hardware authentication."""
    m, e = job.model, job.engine
    if not isinstance(m.file_sha256, str) or not _HEX64_RE.fullmatch(m.file_sha256):
        return False
    if not isinstance(e.source_version, str) or not e.source_version.strip():
        return False
    if not isinstance(e.hardware, str) or not e.hardware.strip():
        return False
    if not isinstance(m.id, str) or not m.id.strip():
        return False
    return True


def provenance_key(job: Job) -> Optional[Tuple]:
    """Real invariant identity that must be identical for a fair comparison: the model artifact and
    the hardware. Returns None when provenance is missing or syntactically invalid, so the caller must
    treat the evidence as incomplete rather than claiming comparability. The engine's own kind/version
    are NOT part of this invariant — baseline and upstream differ exactly there."""
    if not _provenance_ok(job):
        return None
    m, e = job.model, job.engine
    return (m.id, str(m.path), m.file_sha256, m.quantisation, m.context_tokens, e.hardware)


def engine_identity_key(job: Job) -> Tuple:
    """Engine identity/version/receipt used to ensure a single engine per group (baseline vs upstream
    may legitimately differ across groups)."""
    e = job.engine
    return (e.kind, e.source_version, e.build_receipt_sha, e.source_lock_sha)


# ------------------------------------------------------------------ comparability (fail-closed)
def comparability_key(job: Job) -> Tuple:
    """Identity of the fixed comparison workload: model identity + fixed prompt/decoding settings.
    Artifact/source/hardware provenance is checked separately via `provenance_key`."""
    m, c = job.model, job.config
    return (m.id, m.quantisation, m.context_tokens, c.id, c.temperature, c.top_p,
            c.max_new_tokens, tuple(c.prompts))


def _analyze_group(results: Sequence[RunResult], label: str, min_runs: int):
    issues: List[str] = []
    seen: set = set()
    for r in results:
        key = (r.job.engine.id, r.job.run_index)
        if key in seen:
            issues.append("%s duplicate engine/run_index %r" % (label, key))
        seen.add(key)
    valid = [r for r in results if _run_is_success(r)]
    if len(valid) < min_runs:
        issues.append("%s has fewer than %d valid successful runs (%d)" % (label, min_runs, len(valid)))
    for r in valid:
        issues.extend(_measurement_issues(r))
    workload = {comparability_key(r.job) for r in valid}
    if len(workload) > 1:
        issues.append("%s mixes multiple workload identities" % label)
    engine_ids = {engine_identity_key(r.job) for r in valid}
    if len(engine_ids) > 1:
        issues.append("%s mixes multiple engine identities (version/receipt)" % label)
    prov = {provenance_key(r.job) for r in valid}
    if None in prov:
        issues.append("%s has incomplete provenance (missing/invalid artifact sha / source version / hardware)" % label)
    elif len(prov) > 1:
        issues.append("%s contradicts its own provenance" % label)
    return valid, workload, engine_ids, prov, issues


def validate_sample_comparability(baseline: Sequence[RunResult],
                                  candidate: Sequence[RunResult],
                                  min_runs: int = 5) -> List[str]:
    """Return a list of failure reasons; an empty list means the two samples are comparable.

    Fail-closed: only successful runs count; duplicate engine/run_index identities are rejected;
    metric scores (including the protocol DONE metrics) must be finite/non-negative; provenance must be
    complete and syntactically valid; each group must use one fixed workload AND one engine identity;
    the two groups must share the same model/config workload. `min_runs` must be an integer >= 5.
    """
    if not isinstance(min_runs, int) or min_runs < 5:
        return ["min_runs must be an integer >= 5"]
    failures: List[str] = []
    b_valid, b_w, b_e, b_p, bi = _analyze_group(baseline, "baseline", min_runs)
    c_valid, c_w, c_e, c_p, ci = _analyze_group(candidate, "candidate", min_runs)
    failures += bi + ci
    if b_valid and c_valid:
        if b_w and c_w and b_w != c_w:
            failures.append("baseline and candidate use different model/config identity")
        if b_p and c_p and b_p != c_p:
            failures.append("baseline and candidate provenance differ")
    return failures


# ------------------------------------------------------------------ serialization / offline comparison CLI
def _job_from_dict(d: dict) -> Job:
    e, m, c = d["engine"], d["model"], d["config"]
    return Job(
        EngineManifest(id=e["id"], kind=e["kind"], binary=Path(e["binary"]),
                       build_receipt_sha=e["build_receipt_sha"], source_lock_sha=e["source_lock_sha"],
                       profile=e["profile"], source_version=e.get("source_version"),
                       hardware=e.get("hardware")),
        ModelManifest(id=m["id"], path=Path(m["path"]), quantisation=m["quantisation"],
                      context_tokens=m["context_tokens"], expected_registration=m["expected_registration"],
                      file_sha256=m.get("file_sha256")),
        ConfigManifest(id=c["id"], temperature=c["temperature"], top_p=c["top_p"],
                       max_new_tokens=c["max_new_tokens"], prompts=tuple(c["prompts"]),
                       repeats_per_config=c.get("repeats_per_config", 5),
                       timeout_seconds=c.get("timeout_seconds", 300)),
        d["run_index"])


def run_result_from_dict(d: dict) -> RunResult:
    o = d["outcome"]
    outcome = ProcessOutcome(o["exit_code"], o["timed_out"], o["elapsed_seconds"],
                             o.get("stdout", ""), o.get("stderr", ""))
    dn = d["done"]
    done = None
    if dn is not None:
        done = DoneMetrics(dn["generated"], dn["prompt_tokens"], dn["prompt_ms"], dn["decode_ms"],
                           dn["finish"], dn["drafts_accepted"], dn["drafts_offered"],
                           dn["reused"], dn["read_n"])
    return RunResult(_job_from_dict(d), outcome, list(d["token_ids"]), done,
                     list(d.get("errors", [])), dict(d.get("phases", {})))


def run_result_to_dict(r: RunResult) -> dict:
    o = r.outcome
    return {
        "engine": {"id": r.job.engine.id, "kind": r.job.engine.kind,
                   "binary": str(r.job.engine.binary),
                   "build_receipt_sha": r.job.engine.build_receipt_sha,
                   "source_lock_sha": r.job.engine.source_lock_sha,
                   "profile": r.job.engine.profile,
                   "source_version": r.job.engine.source_version,
                   "hardware": r.job.engine.hardware},
        "model": {"id": r.job.model.id, "path": str(r.job.model.path),
                  "quantisation": r.job.model.quantisation,
                  "context_tokens": r.job.model.context_tokens,
                  "expected_registration": r.job.model.expected_registration,
                  "file_sha256": r.job.model.file_sha256},
        "config": {"id": r.job.config.id, "temperature": r.job.config.temperature,
                   "top_p": r.job.config.top_p,
                   "max_new_tokens": r.job.config.max_new_tokens,
                   "prompts": list(r.job.config.prompts),
                   "repeats_per_config": r.job.config.repeats_per_config,
                   "timeout_seconds": r.job.config.timeout_seconds},
        "run_index": r.job.run_index,
        "outcome": {"exit_code": o.exit_code, "timed_out": o.timed_out,
                    "elapsed_seconds": o.elapsed_seconds, "stdout": o.stdout, "stderr": o.stderr},
        "token_ids": list(r.token_ids),
        "done": ({"generated": r.done.generated, "prompt_tokens": r.done.prompt_tokens,
                  "prompt_ms": r.done.prompt_ms, "decode_ms": r.done.decode_ms,
                  "finish": r.done.finish, "drafts_accepted": r.done.drafts_accepted,
                  "drafts_offered": r.done.drafts_offered, "reused": r.done.reused,
                  "read_n": r.done.read_n} if r.done else None),
        "errors": list(r.errors),
        "phases": dict(r.phases),
    }


def tps(r: RunResult) -> Optional[float]:
    """Decode throughput (tokens/sec) from a successfully parsed DONE terminal. Never returns
    NaN/Inf/negative and never divides by zero."""
    if r.done is None:
        return None
    g = r.done.generated
    ms = r.done.decode_ms
    if not isinstance(g, int) or g <= 0:
        return None
    try:
        fms = float(ms)
    except (TypeError, ValueError):
        return None
    if not math.isfinite(fms) or fms <= 0:
        return None
    return g / (fms / 1000.0)


def _summarize(vals: Sequence[float]) -> dict:
    med = median(vals)
    return {"validRuns": len(vals), "medianTps": med,
            "madTps": median_absolute_deviation(vals, med) if med is not None else None,
            "minTps": min(vals) if vals else None,
            "maxTps": max(vals) if vals else None}


def compare_report(baseline: Sequence[RunResult], candidate: Sequence[RunResult],
                   min_runs: int = 5) -> dict:
    """Produce a DESCRIPTIVE comparison. Comparability (valid, matched-window runs) is required to
    report anything, but it does NOT by itself prove a statistically reliable performance gain: no
    independent actual-evidence criterion currently exists, so `gainReliable` is always false. The
    report describes median/MAD/range/valid counts and the raw paired TPS, with differences as
    descriptive percentages only — never a reliable gain conclusion."""
    failures = validate_sample_comparability(baseline, candidate, min_runs)
    if failures:
        return {"comparable": False, "gainReliable": False, "failures": failures}
    b_tps = [tps(r) for r in baseline if _run_is_success(r) and tps(r) is not None]
    c_tps = [tps(r) for r in candidate if _run_is_success(r) and tps(r) is not None]
    delta = None
    b_med, c_med = median(b_tps), median(c_tps)
    if b_med and c_med and b_med > 0:
        delta = (c_med - b_med) / b_med * 100.0
    return {
        "comparable": True,
        "gainReliable": False,  # metadata comparability alone is not statistical reliability
        "baseline": _summarize(b_tps),
        "candidate": _summarize(c_tps),
        "deltaPct": delta,      # descriptive only; no reliable gain label
        "raw": {"baseline": b_tps, "candidate": c_tps},
        "note": "descriptive comparison only; no reliable performance-gain conclusion without an "
                "independent actual-evidence criterion",
        "limitations": ["no independent statistical significance criterion", "no hardware authentication",
                        "no live PC/inference evidence", "flat zero-difference or wide-variability "
                        "samples are not reliable gains"],
    }


def _offline_compare(argv: Sequence[str]) -> int:
    ap = argparse.ArgumentParser(prog="thunder_bench --offline-compare")
    ap.add_argument("--baseline", type=Path, required=True)
    ap.add_argument("--candidate", type=Path, required=True)
    ap.add_argument("--min-runs", type=int, default=5)
    args = ap.parse_args(argv)
    if not isinstance(args.min_runs, int) or args.min_runs < 5:
        print("error: --min-runs must be an integer >= 5 (required methodology)", file=sys.stderr)
        return 2
    baseline = [run_result_from_dict(d) for d in json.loads(args.baseline.read_text())]
    candidate = [run_result_from_dict(d) for d in json.loads(args.candidate.read_text())]
    report = compare_report(baseline, candidate, args.min_runs)
    print(json.dumps(report, indent=2))
    return 0 if report.get("comparable") else 2


def main(argv: Sequence[str] = ()) -> int:
    argv = list(argv) if argv else sys.argv[1:]
    if argv and argv[0] == "--offline-compare":
        return _offline_compare(argv[1:])
    print("Thunder harness is SOURCE-ONLY; no run attempted. Provide manifests and a verified "
          "in-window endpoint to execute (or use --offline-compare).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
