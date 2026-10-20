"""Thunder offline benchmark harness — SOURCE-ONLY.

This module is authored offline and has NOT executed on any owner PC. It implements the fail-closed
guard contract, interleaved existing/upstream/optimised scheduling, bounded process lifetime, the
candidate `strata --serve` line-protocol parser, raw result recording, and median/variability
aggregation. No measurement here is a benchmark result; nothing runs without a positive, in-window,
authorised endpoint.

Run: `python3 -m unittest thunder_bench.test_*` (build-free, deterministic fixtures only).
"""

from __future__ import annotations

import dataclasses
import json
import math
import os
import signal
import subprocess
import threading
import time
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple

# ------------------------------------------------------------------ manifests (immutable)
@dataclasses.dataclass(frozen=True)
class EngineManifest:
    id: str
    kind: str          # "existing" | "upstream" | "optimised"
    binary: Path
    build_receipt_sha: str
    source_lock_sha: str
    profile: str

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
    in_window_start: float = 20.0 * 3600
    in_window_end: float = 21.0 * 3600

    def check(self) -> List[str]:
        failures: List[str] = []
        t = time.gmtime(self.now_utc)
        secs = t.tm_hour * 3600 + t.tm_min * 60 + t.tm_sec
        if secs < self.in_window_start or secs >= self.in_window_end:
            failures.append("outside authorised owner window (20:00-21:00 UTC)")
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
        self._timer = threading.Timer(self.timeout, self._timeout_kill)
        self._timer.daemon = True
        self._timer.start()
        return self

    def _kill_tree(self) -> None:
        if self._proc is None or self._proc.poll() is not None:
            return
        try:
            if os.name == "nt":
                subprocess.run(["taskkill", "/T", "/F", "/PID", str(self._proc.pid)],
                               capture_output=True)
            else:
                os.killpg(os.getpgid(self._proc.pid), signal.SIGKILL)
        except Exception:
            try:
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
        if self._proc is not None and self._proc.poll() is None:
            try:
                self._proc.kill()
            except Exception:
                pass


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


# ------------------------------------------------------------------ CLI (source-only runner stub)
def main(argv: Sequence[str] = ()) -> int:
    # The runner requires a positive in-window endpoint and verified manifests. It is intentionally
    # not run in this source checkpoint.
    print("Thunder harness is SOURCE-ONLY; no run attempted. Provide manifests and a verified "
          "in-window endpoint to execute.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
