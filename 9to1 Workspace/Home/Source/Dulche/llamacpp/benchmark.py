#!/usr/bin/env python3
"""Local-only Haven inference benchmark harness.

The harness talks only to the Haven Unix socket. It never downloads a model and
records a prompt fingerprint rather than prompt text in benchmark output.
"""
from __future__ import annotations

import argparse
import hashlib
import http.client
import json
import os
import pathlib
import socket
import threading
import time
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any


MAX_SSE_LINE_BYTES = 1024 * 1024


class BenchmarkError(RuntimeError):
    pass


class UnixHTTPConnection(http.client.HTTPConnection):
    def __init__(self, unix_path: pathlib.Path, timeout: float):
        super().__init__("localhost", timeout=timeout)
        self.unix_path = str(unix_path)

    def connect(self) -> None:
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(self.unix_path)
        self.sock = sock


def default_broker_socket() -> pathlib.Path:
    runtime = os.environ.get("XDG_RUNTIME_DIR")
    root = pathlib.Path(runtime) / "haven" if runtime else pathlib.Path("/tmp") / f"haven-{os.getuid()}"
    return root / "inference.sock"


def parse_sse_data_line(line: bytes, *, strict: bool = False) -> dict[str, Any] | str | None:
    try:
        text = line.decode("utf-8", "strict" if strict else "replace").strip()
    except UnicodeDecodeError as exc:
        raise BenchmarkError("chat stream contains invalid UTF-8") from exc
    if not text.startswith("data:"):
        return None
    payload = text[5:].strip()
    if payload == "[DONE]":
        return "DONE"
    try:
        value = json.loads(payload)
    except json.JSONDecodeError as exc:
        if strict:
            raise BenchmarkError("chat stream contains malformed data") from exc
        return None
    if isinstance(value, dict):
        return value
    if strict:
        raise BenchmarkError("chat stream data must be an object")
    return None


def _json_request(socket_path: pathlib.Path, method: str, path: str, *, timeout: float, payload: dict[str, Any] | None = None) -> tuple[int, dict[str, Any]]:
    conn = UnixHTTPConnection(socket_path, timeout)
    body = None if payload is None else json.dumps(payload, separators=(",", ":")).encode("utf-8")
    headers = {} if body is None else {"Content-Type": "application/json"}
    try:
        conn.request(method, path, body=body, headers=headers)
        response = conn.getresponse()
        raw = response.read(1024 * 1024)
        try:
            value = json.loads(raw or b"{}")
        except json.JSONDecodeError as exc:
            raise BenchmarkError(f"non-JSON response from {path}") from exc
        if not isinstance(value, dict):
            raise BenchmarkError(f"unexpected response shape from {path}")
        return response.status, value
    finally:
        conn.close()


def _rss_bytes(pid: int) -> int | None:
    try:
        text = pathlib.Path(f"/proc/{pid}/status").read_text(encoding="utf-8")
    except OSError:
        return None
    for line in text.splitlines():
        if line.startswith("VmRSS:"):
            parts = line.split()
            if len(parts) >= 2 and parts[1].isdigit():
                return int(parts[1]) * 1024
    return None


def _children(pid: int) -> list[int]:
    try:
        text = pathlib.Path(f"/proc/{pid}/task/{pid}/children").read_text(encoding="utf-8").strip()
    except OSError:
        return []
    return [int(value) for value in text.split() if value.isdigit()]


def process_tree(pid: int) -> set[int]:
    result: set[int] = set()
    stack = [pid]
    while stack:
        current = stack.pop()
        if current in result:
            continue
        result.add(current)
        stack.extend(_children(current))
    return result


@dataclass
class MemorySampler:
    root_pid: int | None
    interval: float = 0.05
    peak_bytes: int | None = None

    def __post_init__(self) -> None:
        self._stop = threading.Event()
        self._thread: threading.Thread | None = None

    def start(self) -> None:
        if self.root_pid is None:
            return
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def _run(self) -> None:
        while not self._stop.is_set():
            total = 0
            found = False
            for pid in process_tree(self.root_pid):
                value = _rss_bytes(pid)
                if value is not None:
                    total += value
                    found = True
            if found and (self.peak_bytes is None or total > self.peak_bytes):
                self.peak_bytes = total
            self._stop.wait(self.interval)

    def stop(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=1)


def _stream_chat(socket_path: pathlib.Path, payload: dict[str, Any], *, timeout: float, sampler: MemorySampler) -> dict[str, Any]:
    conn = UnixHTTPConnection(socket_path, timeout)
    encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
    started = time.monotonic()
    first_content: float | None = None
    completion_tokens: int | None = None
    finish_reason: str | None = None
    status = 0
    terminal_received = False
    try:
        conn.request("POST", "/v1/chat/completions", body=encoded, headers={"Content-Type": "application/json"})
        response = conn.getresponse()
        status = response.status
        if not 200 <= status < 300:
            detail = response.read(64 * 1024).decode("utf-8", "replace")
            raise BenchmarkError(f"chat failed with HTTP {status}: {detail[:1000]}")
        sampler.start()
        while True:
            line = response.readline(MAX_SSE_LINE_BYTES + 1)
            if len(line) > MAX_SSE_LINE_BYTES:
                raise BenchmarkError("chat stream data line exceeds 1 MiB")
            if not line:
                raise BenchmarkError("chat stream ended before terminal [DONE]")
            event = parse_sse_data_line(line, strict=True)
            if event == "DONE":
                terminal_received = True
                break
            if not isinstance(event, dict):
                continue
            if "error" in event:
                raise BenchmarkError("chat stream reported an error event")
            usage = event.get("usage")
            if usage is not None and not isinstance(usage, dict):
                raise BenchmarkError("chat stream usage must be an object")
            if isinstance(usage, dict) and "completion_tokens" in usage:
                count = usage["completion_tokens"]
                if type(count) is not int or count < 0:
                    raise BenchmarkError("chat stream completion usage must be a nonnegative integer")
                completion_tokens = count
            choices = event.get("choices")
            if choices is not None and not isinstance(choices, list):
                raise BenchmarkError("chat stream choices must be a list")
            if isinstance(choices, list) and choices:
                if len(choices) != 1 or not isinstance(choices[0], dict):
                    raise BenchmarkError("chat stream must contain one choice per event")
                choice = choices[0]
                delta = choice.get("delta")
                if delta is not None and not isinstance(delta, dict):
                    raise BenchmarkError("chat stream delta must be an object")
                content = delta.get("content") if isinstance(delta, dict) else None
                if content is not None and not isinstance(content, str):
                    raise BenchmarkError("chat stream content must be text")
                if content and first_content is None:
                    first_content = time.monotonic()
                reason = choice.get("finish_reason")
                if reason is not None:
                    if not isinstance(reason, str) or not reason.strip():
                        raise BenchmarkError("chat stream finish reason must be a nonempty string")
                    if finish_reason is not None and finish_reason != reason:
                        raise BenchmarkError("chat stream has conflicting finish reasons")
                    finish_reason = reason
        if not terminal_received or finish_reason is None:
            raise BenchmarkError("chat stream lacks a completed choice")
        ended = time.monotonic()
    finally:
        sampler.stop()
        conn.close()
    generation_seconds = None if first_content is None else max(ended - first_content, 0.0)
    tokens_per_second = None
    if completion_tokens is not None and generation_seconds and generation_seconds > 0:
        tokens_per_second = completion_tokens / generation_seconds
    request_seconds = max(ended - started, 0.0)
    request_tokens_per_second = None
    if completion_tokens is not None and request_seconds > 0:
        request_tokens_per_second = completion_tokens / request_seconds
    return {
        "httpStatus": status,
        "timeToFirstTokenMs": None if first_content is None else (first_content - started) * 1000.0,
        "totalGenerationMs": (ended - started) * 1000.0,
        "completionTokens": completion_tokens,
        # Preserve the legacy scalar's interval; it is not native decode speed.
        # Usage can include reasoning tokens emitted before visible content.
        "completionTokensPerSecond": tokens_per_second,
        "completionTokensPerSecondBasis": "reportedCompletionTokens / secondsAfterFirstVisibleContent",
        "completionTokensPerSecondIsNativeDecodeRate": False,
        "requestCompletionTokensPerSecond": request_tokens_per_second,
        "requestCompletionTokensPerSecondBasis": "reportedCompletionTokens / requestStartToTerminalSeconds",
        "nativeDecodeTokensPerSecond": None,
        "streamCompleted": terminal_received,
        "finishReason": finish_reason,
        "peakProcessTreeRssBytes": sampler.peak_bytes,
    }


def run_benchmark(*, socket_path: pathlib.Path, model_id: str, prompt: str, max_tokens: int, timeout: float, root_pid: int | None, unload_after: bool) -> dict[str, Any]:
    if not socket_path.exists():
        raise BenchmarkError(f"broker socket does not exist: {socket_path}")
    prompt_bytes = prompt.encode("utf-8")
    if not prompt_bytes or len(prompt_bytes) > 1024 * 1024:
        raise BenchmarkError("prompt must be non-empty and at most 1 MiB")
    if max_tokens <= 0:
        raise BenchmarkError("max_tokens must be positive")
    status, health = _json_request(socket_path, "GET", "/health", timeout=timeout)
    if status != 200:
        raise BenchmarkError(f"broker health failed: {health}")
    load_started = time.monotonic()
    status, loaded = _json_request(socket_path, "POST", f"/v1/models/{model_id}/load", timeout=timeout)
    load_ended = time.monotonic()
    if status != 200:
        raise BenchmarkError(f"model load failed: {loaded}")
    request_id = f"bench-{uuid.uuid4().hex}"
    sampler = MemorySampler(root_pid)
    payload = {
        "request_id": request_id,
        "messages": [{"role": "user", "content": prompt}],
        "temperature": 0,
        "max_tokens": max_tokens,
        "stream_options": {"include_usage": True},
    }
    try:
        result = _stream_chat(socket_path, payload, timeout=timeout, sampler=sampler)
    except KeyboardInterrupt:
        _json_request(socket_path, "POST", f"/v1/requests/{request_id}/cancel", timeout=min(timeout, 5.0))
        raise
    finally:
        if unload_after:
            try:
                _json_request(socket_path, "POST", f"/v1/models/{model_id}/unload", timeout=min(timeout, 10.0))
            except Exception:
                pass
    return {
        "schemaVersion": 1,
        "evidenceClass": "benchmark-result",
        "recordedAt": datetime.now(timezone.utc).isoformat(),
        "modelId": model_id,
        "prompt": {
            "sha256": hashlib.sha256(prompt_bytes).hexdigest(),
            "bytes": len(prompt_bytes),
            "textStored": False,
        },
        "modelLoadMs": (load_ended - load_started) * 1000.0,
        "memoryRootPid": root_pid,
        "health": health,
        "result": result,
        "notes": [
            "A benchmark result is runtime evidence only for the exact model/backend/hardware/software combination that produced it.",
            "Missing completion token usage leaves tokens-per-second null rather than estimating tokens from text.",
            "Legacy completionTokensPerSecond retains its post-first-visible-content interval and must not be compared as native decode speed; reported usage can include earlier reasoning tokens.",
            "requestCompletionTokensPerSecond is end-to-end request throughput, including prefill, reasoning, transport and terminal handling; native decode speed requires separate engine telemetry.",
            "RSS sampling covers the supplied root PID and descendants visible through procfs."
        ],
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Benchmark an already-installed Haven llama.cpp model")
    parser.add_argument("--socket", type=pathlib.Path, default=default_broker_socket())
    parser.add_argument("--model", required=True)
    prompt = parser.add_mutually_exclusive_group(required=True)
    prompt.add_argument("--prompt")
    prompt.add_argument("--prompt-file", type=pathlib.Path)
    parser.add_argument("--max-tokens", type=int, default=128)
    parser.add_argument("--timeout", type=float, default=300.0)
    parser.add_argument("--root-pid", type=int, help="Optional broker service PID for aggregate broker+worker RSS sampling")
    parser.add_argument("--unload-after", action="store_true")
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args(argv)
    text = args.prompt if args.prompt is not None else args.prompt_file.read_text(encoding="utf-8")
    value = run_benchmark(
        socket_path=args.socket, model_id=args.model, prompt=text, max_tokens=args.max_tokens,
        timeout=args.timeout, root_pid=args.root_pid, unload_after=args.unload_after,
    )
    rendered = json.dumps(value, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
