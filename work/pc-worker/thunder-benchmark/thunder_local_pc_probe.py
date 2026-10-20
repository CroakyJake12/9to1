#!/usr/bin/env python3
# thunder_local_pc_probe.py
#
# Owner-run acceptance probe for the Thunder (embedded Strata) local loopback service.
#
# Intended to be run BY THE OWNER on the PC, inside the hard observation window
# (2026-10-20 20:00:00Z .. 21:00:00Z). It never talks to the public inference endpoint and
# performs only bounded read-only local observations plus fixed loopback (127.0.0.1:8081)
# service calls. It is a single-file, standard-library, Python 3 probe with no external
# dependencies.
#
# IMPORTANT: This script is owned and run by the owner after independent review. A worker must
# NOT execute it. Workers only author it, hash it, and run its OFFLINE unit tests.
#
# Output: a private JSON result written atomically (0600 file, 0700 parent on POSIX) with the
# exact schema/version, UTC window, native observation sources, model pre/post, ordered plan /
# actual runs / errors and limitations. No secrets, personal paths, hostname or raw payloads are
# ever written to the output or the console.

import datetime as _dt
import hashlib
import json
import os
import platform
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time

# ---------------------------------------------------------------------------
# Configuration / constants
# ---------------------------------------------------------------------------

WINDOW_START_UTC = _dt.datetime(2026, 10, 20, 20, 0, 0, tzinfo=_dt.timezone.utc)
WINDOW_END_UTC = _dt.datetime(2026, 10, 20, 21, 0, 0, tzinfo=_dt.timezone.utc)

LOOPBACK_HOST = "127.0.0.1"
LOOPBACK_PORT = 8081

# The ONLY external network endpoints ever touched (fixed loopback allowlist).
HEALTH_PATH = "/health"
VERSION_PATH = "/version"
PROPS_PATH = "/props"
MODELS_PATH = "/v1/models"
CHAT_PATH = "/v1/chat/completions"

EXPECTED_MODEL_ID = "3-8-flash--q3-pro-"

KEY_PATH = os.path.join(os.path.expanduser("~"), ".config", "homepc-ai", "api-key")
MAX_KEY_BYTES = 4096

STREAM_MAX_BYTES = 4 * 1024 * 1024      # hard cap on one streamed body
MAX_GET_BYTES = 1 * 1024 * 1024         # hard cap on one GET body
READ_CHUNK = 4096
LINE_MAX_BYTES = 64 * 1024
HEADERS_MAX_BYTES = 64 * 1024

CONNECT_TIMEOUT_S = 15.0
MAX_RUN_SECONDS = 45.0                  # per-request ceiling
POST_CAPTURE_MARGIN_S = 60.0            # reserve before window end for /v1/models post
MIN_REMAINING_TO_START_S = 90.0         # stop launching runs when less than this remains

SHORT_PROMPT = "Solve: 2+2. Reply with exactly one number."
LONG_PROMPT = (
    "The following is neutral filler context for an inference timing sample. "
    + ("A plain sentence of no personal or sensitive information. " * 240)
)
assert len(LONG_PROMPT.encode("utf-8")) <= 16 * 1024, "LONG_PROMPT must be <= 16KiB UTF-8"

MAX_OUTPUT_TOKENS = 32

SCHEMA_VERSION = 1
PROBE_VERSION = "thunder-local-pc-probe-1.0.0"

LIMITATIONS = [
    "current-service/client timings only",
    "candidate/upstream comparison NOT RUN",
    "candidate deployment/loading NOT VERIFIED",
    "GPU workload binding/attributed RAM NOT VERIFIED",
    "no speed-gain conclusion / full-engine / Windows / app acceptance",
]

OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_FILENAME = "thunder_local_pc_probe_result.json"

pathish_re = re.compile(r"[/\\]|\.\.")


class TransportError(Exception):
    def __init__(self, category, detail=""):
        super().__init__(category + (": " + detail if detail else ""))
        self.category = category
        self.detail = detail


class DeadlineExceeded(TransportError):
    def __init__(self):
        super().__init__("timeout")


# ---------------------------------------------------------------------------
# Clocks (seams for offline tests)
# ---------------------------------------------------------------------------

def wall_utc_now():
    return _dt.datetime.now(_dt.timezone.utc)


def mono_ns_now():
    return time.time_ns()


# ---------------------------------------------------------------------------
# Safe model ID handling
# ---------------------------------------------------------------------------

def safe_model_record(model_id):
    """Keep an exact model ID only when it is safe; redact path-like IDs."""
    if pathish_re.search(model_id):
        return {
            "id": "REDACTED_PATH",
            "redacted": True,
            "sha256": hashlib.sha256(model_id.encode("utf-8")).hexdigest(),
        }
    return {
        "id": model_id,
        "redacted": False,
        "sha256": hashlib.sha256(model_id.encode("utf-8")).hexdigest(),
    }


def extract_safe_scalars(obj):
    """Keep only shallow safe scalar values; never dump raw payloads or path-ish strings."""
    out = {}
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, (str, int, float, bool)):
                if not (isinstance(v, str) and pathish_re.search(v)):
                    out[k] = v
    return out


# ---------------------------------------------------------------------------
# Bounded HTTP transport over a raw socket (loopback only, Connection: close)
# ---------------------------------------------------------------------------

def _recv_exact(sock, n, deadline_ns):
    out = b""
    while len(out) < n:
        remaining = deadline_ns - mono_ns_now()
        if remaining <= 0:
            raise DeadlineExceeded()
        sock.settimeout(min(max(remaining / 1e9, 0.05), CONNECT_TIMEOUT_S))
        try:
            chunk = sock.recv(min(n - len(out), READ_CHUNK))
        except socket.timeout:
            raise DeadlineExceeded()
        if not chunk:
            raise TransportError("eof", "connection closed before complete data")
        out += chunk
    return out


def _recv_line(sock, deadline_ns, cap=LINE_MAX_BYTES):
    buf = b""
    while True:
        idx = buf.find(b"\n")
        if idx >= 0:
            return buf[:idx].rstrip(b"\r")
        if len(buf) > cap:
            raise TransportError("too_large", "response line too long")
        remaining = deadline_ns - mono_ns_now()
        if remaining <= 0:
            raise DeadlineExceeded()
        sock.settimeout(min(max(remaining / 1e9, 0.05), CONNECT_TIMEOUT_S))
        try:
            chunk = sock.recv(READ_CHUNK)
        except socket.timeout:
            raise DeadlineExceeded()
        if not chunk:
            raise TransportError("eof")
        buf += chunk


def _body_chunks(sock, headers, deadline_ns):
    te = headers.get("transfer-encoding", "")
    if "chunked" in te.lower():
        while True:
            line = _recv_line(sock, deadline_ns)
            hex_size = line.strip().split(b";")[0].strip()
            try:
                size = int(hex_size, 16)
            except ValueError:
                raise TransportError("bad_chunk")
            if size == 0:
                _recv_line(sock, deadline_ns)  # trailing CRLF
                return
            yield _recv_exact(sock, size, deadline_ns)
            _recv_line(sock, deadline_ns)  # chunk CRLF
        return
    clen = headers.get("content-length")
    if clen is not None:
        try:
            remaining = int(clen)
        except ValueError:
            raise TransportError("bad_length")
        while remaining > 0:
            n = min(remaining, READ_CHUNK)
            chunk = _recv_exact(sock, n, deadline_ns)
            yield chunk
            remaining -= len(chunk)
        return
    # No framing: read until connection close (Connection: close was requested).
    while True:
        remaining = deadline_ns - mono_ns_now()
        if remaining <= 0:
            raise DeadlineExceeded()
        sock.settimeout(min(max(remaining / 1e9, 0.05), CONNECT_TIMEOUT_S))
        try:
            chunk = sock.recv(READ_CHUNK)
        except socket.timeout:
            raise DeadlineExceeded()
        if not chunk:
            return
        yield chunk


def _request(method, path, headers=None, body=None, deadline_ns=None):
    if deadline_ns is None:
        deadline_ns = mono_ns_now() + int(CONNECT_TIMEOUT_S * 1e9)
    connect_timeout = min(max((deadline_ns - mono_ns_now()) / 1e9, 0.1), CONNECT_TIMEOUT_S)
    sock = socket.create_connection((LOOPBACK_HOST, LOOPBACK_PORT), timeout=connect_timeout)
    try:
        path = path or "/"
        loc = "%s:%d" % (LOOPBACK_HOST, LOOPBACK_PORT)
        hdrs = {"Host": loc, "Connection": "close", "Accept": "*/*"}
        if headers:
            hdrs.update(headers)
        if body is not None:
            if isinstance(body, str):
                body = body.encode("utf-8")
            hdrs["Content-Length"] = str(len(body))
        head = "%s %s HTTP/1.1\r\n" % (method, path)
        for k, v in hdrs.items():
            head += "%s: %s\r\n" % (k, v)
        head += "\r\n"
        sock.sendall(head.encode("iso-8859-1"))
        if body is not None:
            sock.sendall(body)
        status_line = _recv_line(sock, deadline_ns)
        parts = status_line.split(None, 2)
        if len(parts) < 2:
            raise TransportError("bad_status")
        try:
            status = int(parts[1])
        except ValueError:
            raise TransportError("bad_status")
        resp_headers = {}
        while True:
            line = _recv_line(sock, deadline_ns)
            if line in (b"", b"\r"):
                break
            if b":" in line:
                k, _, v = line.partition(b":")
                resp_headers[k.strip().lower().decode("latin-1")] = v.strip().decode("latin-1")
        return status, resp_headers, sock
    except BaseException:
        sock.close()
        raise


def http_get(path, headers=None, timeout_s=10.0):
    """Bounded GET; returns (status, body_bytes)."""
    deadline = mono_ns_now() + int(timeout_s * 1e9)
    try:
        status, rh, sock = _request("GET", path, headers=headers, deadline_ns=deadline)
    except DeadlineExceeded:
        raise
    except TransportError:
        raise
    except (OSError, socket.error) as exc:
        raise TransportError("connect_error", type(exc).__name__)
    try:
        body = b""
        total = 0
        for chunk in _body_chunks(sock, rh, deadline):
            total += len(chunk)
            if total > MAX_GET_BYTES:
                raise TransportError("too_large")
            body += chunk
        return status, body
    finally:
        sock.close()


def http_post_stream(path, body, headers, deadline_ns):
    """Bounded streaming POST; returns (status, resp_headers, chunk_iter, sock)."""
    try:
        status, rh, sock = _request(
            "POST", path, headers=headers, body=body, deadline_ns=deadline_ns)
    except DeadlineExceeded:
        raise
    except TransportError:
        raise
    except (OSError, socket.error) as exc:
        raise TransportError("connect_error", type(exc).__name__)
    return status, rh, _body_chunks(sock, rh, deadline_ns), sock


# ---------------------------------------------------------------------------
# SSE chat-stream parser (pure, offline-testable)
# ---------------------------------------------------------------------------

def parse_sse_chunks(chunk_iter, start_ns, deadline_ns, clock=None):
    """Consume byte chunks, parse SSE `data:` events for a chat completion stream.

    Returns:
        {first_text_delta_s, total_s, output_chars, output_sha256, usage, complete}
    Raises DeadlineExceeded / TransportError("too_large") as appropriate.
    """
    if clock is None:
        clock = mono_ns_now
    pieces = []
    first_text_ns = None
    usage = None
    complete = False
    buf = b""
    total_bytes = 0
    for chunk in chunk_iter:
        if clock() > deadline_ns:
            raise DeadlineExceeded()
        total_bytes += len(chunk)
        if total_bytes > STREAM_MAX_BYTES:
            raise TransportError("too_large", "stream exceeds bound")
        buf += chunk
        while True:
            idx = buf.find(b"\n\n")
            if idx < 0:
                break
            event = buf[:idx]
            buf = buf[idx + 2:]
            line = event.strip()
            if not line.startswith(b"data:"):
                continue
            payload = line[len(b"data:"):].strip()
            if payload == b"[DONE]":
                complete = True
                break
            try:
                obj = json.loads(payload.decode("utf-8"))
            except (ValueError, UnicodeDecodeError):
                continue  # malformed metadata: bounded and ignored
            if not isinstance(obj, dict):
                continue
            if isinstance(obj.get("usage"), dict):
                usage = {
                    "prompt_tokens": obj["usage"].get("prompt_tokens"),
                    "completion_tokens": obj["usage"].get("completion_tokens"),
                    "total_tokens": obj["usage"].get("total_tokens"),
                }
            choices = obj.get("choices")
            if isinstance(choices, list) and choices:
                ch = choices[0]
                if isinstance(ch, dict) and isinstance(ch.get("delta"), dict):
                    content = ch["delta"].get("content")
                    if isinstance(content, str) and content:
                        if first_text_ns is None:
                            first_text_ns = clock()
                        pieces.append(content)
        if complete:
            break
    output = "".join(pieces)
    now = clock()
    return {
        "first_text_delta_s": ((first_text_ns - start_ns) / 1e9) if first_text_ns is not None else None,
        "total_s": (now - start_ns) / 1e9,
        "output_chars": len(output),
        "output_sha256": hashlib.sha256(output.encode("utf-8")).hexdigest() if output else None,
        "usage": usage,
        "complete": complete,
    }


# ---------------------------------------------------------------------------
# Authenticated /v1/models
# ---------------------------------------------------------------------------

def fetch_models(key):
    headers = {"Authorization": "Bearer " + key}
    try:
        status, body = http_get(MODELS_PATH, headers=headers, timeout_s=10.0)
    except DeadlineExceeded:
        return {"status": "error", "category": "timeout"}
    except TransportError as exc:
        return {"status": "error", "category": exc.category}
    except Exception:
        return {"status": "error", "category": "unexpected"}
    if status != 200:
        return {"status": "error", "category": "http_%d" % status}
    try:
        obj = json.loads(body.decode("utf-8"))
    except (ValueError, UnicodeDecodeError):
        return {"status": "error", "category": "malformed"}
    models = obj.get("data") if isinstance(obj, dict) else None
    if not isinstance(models, list):
        return {"status": "error", "category": "schema"}
    out = []
    for m in models:
        mid = m.get("id") if isinstance(m, dict) else None
        if isinstance(mid, str):
            out.append(safe_model_record(mid))
    return {"status": "ok", "models": out, "ids": [x["id"] for x in out]}


# ---------------------------------------------------------------------------
# Read-only loopback metadata endpoints
# ---------------------------------------------------------------------------

def probe_simple(path):
    try:
        status, body = http_get(path, headers={}, timeout_s=5.0)
    except DeadlineExceeded:
        return {"status": "error", "category": "timeout"}
    except TransportError as exc:
        return {"status": "error", "category": exc.category}
    except Exception:
        return {"status": "error", "category": "unexpected"}
    if status != 200:
        return {"status": "status_%d" % status}
    try:
        obj = json.loads(body.decode("utf-8"))
    except Exception:
        return {"status": "ok", "media": "non-json", "length": len(body)}
    return {"status": "ok", "extracted": extract_safe_scalars(obj)}


# ---------------------------------------------------------------------------
# Native observations (bounded read-only)
# ---------------------------------------------------------------------------

def read_proc_cpuinfo(path="/proc/cpuinfo"):
    model = None
    count = 0
    try:
        with open(path, "r", errors="replace") as f:
            for line in f:
                line = line.strip()
                if line.startswith("processor") and ":" in line:
                    count += 1
                elif line.startswith("model name") and ":" in line:
                    model = line.split(":", 1)[1].strip()
                elif model is None and line.startswith("Hardware") and ":" in line:
                    model = line.split(":", 1)[1].strip()
    except OSError:
        return {"available": False, "model": None, "count": None, "source": path}
    if count == 0:
        count = os.cpu_count()
    return {"available": True, "model": model, "count": count, "source": path}


def read_ram_total(system):
    if system == "Linux":
        try:
            with open("/proc/meminfo", "r", errors="replace") as f:
                for line in f:
                    if line.startswith("MemTotal:"):
                        parts = line.split()
                        if len(parts) >= 2:
                            return int(parts[1]) * 1024
        except (OSError, ValueError, IndexError):
            return None
    elif system == "Windows":
        try:
            import ctypes

            class MEMORYSTATUSEX(ctypes.Structure):
                _fields_ = [
                    ("dwLength", ctypes.c_ulong),
                    ("dwMemoryLoad", ctypes.c_ulong),
                    ("ullTotalPhys", ctypes.c_ulonglong),
                    ("ullAvailPhys", ctypes.c_ulonglong),
                    ("ullTotalPageFile", ctypes.c_ulonglong),
                    ("ullAvailPageFile", ctypes.c_ulonglong),
                    ("ullTotalVirtual", ctypes.c_ulonglong),
                    ("ullAvailVirtual", ctypes.c_ulonglong),
                    ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
                ]

            ms = MEMORYSTATUSEX()
            ms.dwLength = ctypes.sizeof(ms)
            if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(ms)):
                return int(ms.ullTotalPhys)
        except Exception:
            return None
    return None


def collect_native():
    res = {"sources": []}
    system = platform.system()
    res["os"] = {
        "system": system,
        "release": platform.release(),
        "architecture": platform.machine(),
    }
    res["sources"].append("platform")
    cpu_model = None
    cpu_count = os.cpu_count()
    if system == "Linux":
        cpu = read_proc_cpuinfo()
        res["cpu"] = cpu
        res["sources"].extend(["proc/cpuinfo", "proc/meminfo"])
        if cpu["count"] is not None:
            cpu_count = cpu["count"]
        cpu_model = cpu["model"]
    elif system == "Windows":
        cpu_model = os.environ.get("PROCESSOR_IDENTIFIER")
        res["sources"].append("os.cpu_count/PROCESSOR_IDENTIFIER")
    res["cpu_count"] = cpu_count
    res["cpu_model"] = cpu_model
    res["ram_bytes_total"] = read_ram_total(system)
    res["ram_source"] = "proc/meminfo" if system == "Linux" else ("ctypes" if system == "Windows" else None)
    return res


def try_nvidia_smi():
    exe = shutil.which("nvidia-smi")
    if not exe:
        return {"status": "unavailable", "reason": "binary_not_found"}
    cmd = [exe, "--query-gpu=name,memory.total,memory.used,driver_version",
           "--format=csv,noheader,nounits"]
    try:
        proc = subprocess.run(cmd, capture_output=True, stdin=subprocess.DEVNULL,
                              timeout=15, check=False)
    except (subprocess.SubprocessError, OSError):
        return {"status": "unavailable", "reason": "exec_failed"}
    if proc.returncode != 0:
        return {"status": "unavailable", "reason": "nonzero_exit"}
    text = proc.stdout.decode("utf-8", errors="replace")
    entries = []
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        parts = [x.strip() for x in line.split(",")]
        if len(parts) == 4:
            entries.append({
                "name": parts[0],
                "memory_total": parts[1],
                "memory_used": parts[2],
                "driver_version": parts[3],
            })
    return {"status": "ok", "entries": entries}


# ---------------------------------------------------------------------------
# Credential (read-only, bounded, never emitted)
# ---------------------------------------------------------------------------

def read_api_key():
    try:
        with open(KEY_PATH, "rb") as f:
            data = f.read(MAX_KEY_BYTES + 1)
    except FileNotFoundError:
        return {"status": "missing"}
    except OSError:
        return {"status": "unreadable"}
    if len(data) > MAX_KEY_BYTES:
        return {"status": "oversize"}
    key = data.strip()
    if not key:
        return {"status": "empty"}
    return {"status": "ok", "key": key.decode("latin-1")}


# ---------------------------------------------------------------------------
# Individual chat run
# ---------------------------------------------------------------------------

def run_one(kind, model_id, seq, deadline_ns, key):
    start_wall = wall_utc_now()
    start_ns = mono_ns_now()
    prompt = SHORT_PROMPT if kind == "short" else LONG_PROMPT
    payload = json.dumps({
        "model": model_id,
        "messages": [{"role": "user", "content": prompt}],
        "stream": True,
        "max_tokens": MAX_OUTPUT_TOKENS,
    }).encode("utf-8")
    headers = {"Authorization": "Bearer " + key, "Content-Type": "application/json"}
    record = {"kind": kind, "seq": seq, "outcome": "started"}
    try:
        status, _, chunks, sock = http_post_stream(CHAT_PATH, payload, headers, deadline_ns)
        try:
            if status != 200:
                record["outcome"] = "error"
                record["error_category"] = "http_%d" % status
                return record
            parsed = parse_sse_chunks(chunks, start_ns, deadline_ns)
            record["outcome"] = "completed" if parsed["complete"] else "incomplete"
            record["first_text_delta_s"] = parsed["first_text_delta_s"]
            record["total_s"] = parsed["total_s"]
            record["output_chars"] = parsed["output_chars"]
            record["output_sha256"] = parsed["output_sha256"]
            record["usage"] = parsed["usage"]
            return record
        finally:
            if sock is not None:
                sock.close()
    except DeadlineExceeded:
        record["outcome"] = "timeout"
        return record
    except TransportError as exc:
        record["outcome"] = "error"
        record["error_category"] = exc.category
        return record
    except Exception:
        record["outcome"] = "error"
        record["error_category"] = "unexpected"
        return record


# ---------------------------------------------------------------------------
# Output (private, atomic, 0600/0700)
# ---------------------------------------------------------------------------

def _write_private_json(result, filename=OUTPUT_FILENAME):
    d = OUTPUT_DIR
    if os.name == "posix":
        try:
            os.chmod(d, 0o700)
        except OSError:
            pass
    path = os.path.join(d, filename)
    data = json.dumps(result, indent=2, sort_keys=True) + "\n"
    try:
        fd, tmp = tempfile.mkstemp(dir=d, prefix="." + filename + ".", suffix=".tmp")
        with os.fdopen(fd, "w", encoding="utf-8") as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, path)
        if os.name == "posix":
            try:
                os.chmod(path, 0o600)
            except OSError:
                pass
        return path
    except OSError as exc:
        raise RuntimeError("private output write failed: %s" % type(exc).__name__)


def _finalize(result):
    path = _write_private_json(result)
    target = "COMPLETE" if result["summary"]["target_complete"] else "INCOMPLETE"
    print("probe result: %s" % os.path.basename(path))
    print("window_status=%s target=%s success=%d/%d" % (
        result["window_status"], target,
        result["summary"]["success_count"],
        len(result["summary"]["plan"])))
    return 0


def base_result(now):
    return {
        "schema_version": SCHEMA_VERSION,
        "probe_version": PROBE_VERSION,
        "generated_at_utc": now.isoformat(),
        "window": {
            "start_utc": WINDOW_START_UTC.isoformat(),
            "end_utc": WINDOW_END_UTC.isoformat(),
        },
        "window_status": None,
        "native": None,
        "service": {
            "base_url": "http://%s:%d" % (LOOPBACK_HOST, LOOPBACK_PORT),
            "health": None,
            "version": None,
            "props": None,
            "models_pre": None,
            "models_post": None,
            "models_stable": None,
            "selected_model_id": None,
            "identity_match": None,
        },
        "runs": [],
        "summary": {
            "plan": ["short", "long", "short", "long", "short", "long"],
            "success_count": 0,
            "target_complete": False,
        },
        "limitations": list(LIMITATIONS),
    }


def main(argv=None):
    start_now = wall_utc_now()
    result = base_result(start_now)

    if start_now < WINDOW_START_UTC:
        result["window_status"] = "not_started"
        return _finalize(result)
    if start_now >= WINDOW_END_UTC:
        result["window_status"] = "window_closed"
        return _finalize(result)

    result["window_status"] = "open"

    # Native observations (bounded read-only) and optional nvidia-smi.
    result["native"] = collect_native()
    if os.path.exists("/proc/cpuinfo"):
        pass  # source already recorded via collect_native()
    result["native"]["gpu"] = try_nvidia_smi()

    # Read-only loopback metadata endpoints (unauthenticated).
    result["service"]["health"] = probe_simple(HEALTH_PATH)
    result["service"]["version"] = probe_simple(VERSION_PATH)
    result["service"]["props"] = probe_simple(PROPS_PATH)

    key_info = read_api_key()
    if key_info["status"] != "ok":
        result["service"]["key_status"] = key_info["status"]
        result["limitations"].append(
            "api key unavailable; authenticated /v1/models and inference skipped")
        return _finalize(result)
    key = key_info["key"]

    # Authenticated pre-run model capture.
    models_pre = fetch_models(key)
    result["service"]["models_pre"] = models_pre
    if models_pre["status"] != "ok":
        result["limitations"].append("pre-run /v1/models unavailable")
        return _finalize(result)

    safe_ids = list(models_pre["ids"])
    matched = [mid for mid in models_pre["ids"] if mid == EXPECTED_MODEL_ID]
    if not matched:
        result["service"]["identity_match"] = False
        result["service"]["selected_model_id"] = None
        result["limitations"].append(
            "expected model id not observed on loopback; inference skipped (no guessed alias)")
        if start_now < WINDOW_END_UTC:
            models_post = fetch_models(key)
            result["service"]["models_post"] = models_post
            result["service"]["models_stable"] = (
                models_post["status"] == "ok" and models_post.get("ids") == safe_ids)
        return _finalize(result)

    result["service"]["identity_match"] = True
    result["service"]["selected_model_id"] = matched[0]

    # Ordered interleaved runs, respecting the hard window.
    reserved_end = WINDOW_END_UTC - _dt.timedelta(seconds=POST_CAPTURE_MARGIN_S)
    seq = 1
    for kind in ("short", "long", "short", "long", "short", "long"):
        wall_now = wall_utc_now()
        if wall_now >= reserved_end:
            result["summary"]["truncated_reason"] = "insufficient_time_before_window_end"
            break
        remaining_to_end_s = (WINDOW_END_UTC - wall_now).total_seconds()
        per_run = min(MAX_RUN_SECONDS, max(remaining_to_end_s, 0.0))
        deadline_ns = mono_ns_now() + int(per_run * 1e9)
        rec = run_one(kind, matched[0], seq, deadline_ns, key)
        result["runs"].append(rec)
        if rec["outcome"] == "completed":
            result["summary"]["success_count"] += 1
        seq += 1
        if wall_utc_now() >= WINDOW_END_UTC - _dt.timedelta(seconds=10):
            result["summary"]["truncated_reason"] = "near_window_end"
            break

    result["summary"]["target_complete"] = result["summary"]["success_count"] >= 5

    # Authenticated post-run model capture before the window closes.
    if wall_utc_now() < WINDOW_END_UTC:
        models_post = fetch_models(key)
        result["service"]["models_post"] = models_post
        result["service"]["models_stable"] = (
            models_post["status"] == "ok" and models_post.get("ids") == safe_ids)

    return _finalize(result)


if __name__ == "__main__":
    sys.exit(main())
