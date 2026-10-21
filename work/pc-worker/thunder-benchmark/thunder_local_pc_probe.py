#!/usr/bin/env python3
# thunder_local_pc_probe.py
#
# Owner-run acceptance probe for the Thunder (embedded Strata) current service.
#
# Intended to be run BY THE OWNER on the PC, inside the hard observation window
# (2026-10-21 15:00:00Z .. 20:00:00Z). It probes the verified fixed public HTTPS
# inference endpoint `https://ai.9to1.uk/v1` (TLS verification ON) using an API key read
# from the configured local path (`~/.config/homepc-ai/api-key`) or an explicit `--key-file
# PATH` (path only, never the value; missing/auth failure fails closed). An optional local
# loopback (127.0.0.1:8081) observation is separate and enables ONLY via
# `THUNDER_PROBE_LOCAL_8081=1`; it never assumes the public model id/alias and uses
# only the IDs that endpoint actually returns.
#
# Single-file, standard-library (Python 3) probe. The public route uses a bounded urllib
# opener that honours inherited HTTP(S)_PROXY/NO_PROXY plus the system CA bundle; loopback is
# always direct. Real monotonic clock for durations/budgets paired with an explicit
# UTC absolute cutoff that is enforced before EVERY hardware/model/metadata operation
# and every connect/TLS/send/header/body/SSE phase.
#
# IMPORTANT: This script is owned and run by the owner after independent review. A
# worker must NOT execute it. Workers only author it, hash it, and run its OFFLINE
# unit tests. It is NOT approved/package; it only helps gather honest current-service
# observations for root review.
#
# Output: a private JSON result written into a NEW owned private output directory
# (0700) as a unique exclusive file (0600), atomically and non-overwriting existing
# evidence. No secrets, personal paths, hostname or raw payloads are ever written to
# the output or the console.

import datetime as _dt
import hashlib
import http.client
import json
import os
import platform
import re
import secrets
import shutil
import socket
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request

# ---------------------------------------------------------------------------
# Configuration / constants
# ---------------------------------------------------------------------------

WINDOW_START_UTC = _dt.datetime(2026, 10, 21, 15, 0, 0, tzinfo=_dt.timezone.utc)
WINDOW_END_UTC = _dt.datetime(2026, 10, 21, 20, 0, 0, tzinfo=_dt.timezone.utc)

# Verified fixed public inference endpoint.
PUBLIC_BASE_URL = "https://ai.9to1.uk/v1"
PUBLIC_HOST = "ai.9to1.uk"
PUBLIC_PORT = 443
PUBLIC_USE_TLS = True
PUBLIC_PREFIX = "/v1"
PUBLIC_PATHS = {"models": "/models", "chat": "/chat/completions"}
# The two model ids this endpoint actually returns (owner-verified, 2026-10-21).
# `PUBLIC_MODEL_ALIAS` (strata-dynamic) is the preferred/default chat model when present.
EXPECTED_MODEL_ID = "3-8-flash--q3-pro-"
PUBLIC_MODEL_ALIAS = "strata-dynamic"
EXPECTED_MODEL_IDS = (EXPECTED_MODEL_ID, PUBLIC_MODEL_ALIAS)

# Optional separate local loopback observation (disabled unless explicitly enabled).
LOCAL_BASE_URL = "http://127.0.0.1:8081"
LOCAL_HOST = "127.0.0.1"
LOCAL_PORT = 8081
LOCAL_USE_TLS = False
LOCAL_PREFIX = ""
LOCAL_PATHS = {
    "models": "/v1/models",
    "chat": "/v1/chat/completions",
    "health": "/health",
    "version": "/version",
    "props": "/props",
}
ENABLE_LOCAL_8081 = os.environ.get("THUNDER_PROBE_LOCAL_8081") == "1"

# Default owner-PC key path. Used ONLY if the actual owner-PC path exists; an explicit
# `--key-file PATH` selects another path (path only, never the key value). Missing/auth
# failure fails closed (no unauthenticated inference).
KEY_PATH = os.path.join(os.path.expanduser("~"), ".config", "homepc-ai", "api-key")
MAX_KEY_BYTES = 4096

STREAM_MAX_BYTES = 4 * 1024 * 1024      # hard cap on one streamed body
MAX_GET_BYTES = 1 * 1024 * 1024         # hard cap on one GET body
LINE_MAX_BYTES = 64 * 1024
HEADERS_MAX_BYTES = 64 * 1024
MAX_MODEL_ID_LEN = 128
MAX_METADATA_STR_LEN = 256

CONNECT_TIMEOUT_S = 15.0
MAX_RUN_SECONDS = 45.0                  # per-request ceiling
POST_CAPTURE_MARGIN_S = 60.0            # reserve before window end for /v1/models post
NEAR_END_STOP_S = 10.0                  # stop launching new runs when this close to end
STALL_SOCKET_FLOOR_S = 0.05             # never busy-spin below this socket timeout
READ_STALL_MAX_S = 0.5                  # bound EACH blocking read to this cap (kept <= remaining budget)

SHORT_PROMPT = "Solve: 2+2. Reply with exactly one number."
LONG_PROMPT = (
    "The following is neutral filler context for an inference timing sample. "
    + ("A plain sentence of no personal or sensitive information. " * 240)
)
assert len(LONG_PROMPT.encode("utf-8")) <= 16 * 1024, "LONG_PROMPT must be <= 16KiB UTF-8"

MAX_OUTPUT_TOKENS = 32

# Documented BOUNDED BASELINE mode (opt-in via --baseline). Fixed synthetic prompts, per-run
# ceiling 90s, output cap 256 tokens, and 5 interleaved SHORT/LONG runs. This is a steady-decode
# sample; it is intentionally separate from the default quick-smoke (2+2 / max 32). Client and
# server-provided usage/timings/cache/draft are kept distinct; we never fabricate GPU data.
BASELINE_MAX_RUN_SECONDS = 90.0
BASELINE_MAX_OUTPUT_TOKENS = 256
_BASELINE_LINE = ("A sorted integer list supports comparison, lower and upper bounds, and a "
                  "halving search interval.")
BASELINE_LONG_PROMPT = (
    "Neutral synthetic context for a timing observation.\n"
    + "\n".join("Record %03d: %s" % (i, _BASELINE_LINE) for i in range(320))
)
assert len(BASELINE_LONG_PROMPT.encode("utf-8")) <= 64 * 1024, "BASELINE_LONG_PROMPT <= 64KiB"

BASELINE_SHORT_PROMPT = "Recite the first eight prime numbers."

SCHEMA_VERSION = 1
PROBE_VERSION = "thunder-local-pc-probe-1.3.0"

LIMITATIONS = [
    "current-service/client timings only (includes public network overhead)",
    "candidate/upstream comparison NOT RUN",
    "candidate deployment/loading NOT VERIFIED",
    "GPU workload binding/attributed RAM NOT VERIFIED",
    "no speed-gain conclusion / full-engine / Windows / app acceptance",
    "public endpoint identity verified only via actual returned model ids",
]


class Endpoint:
    """A fixed, allowlisted HTTP(S) endpoint; no proxy/redirect/arbitrary URL."""

    def __init__(self, name, scheme, host, port, use_tls, prefix, paths):
        self.name = name
        self.scheme = scheme
        self.host = host
        self.port = port
        self.use_tls = use_tls
        self.prefix = prefix
        self.paths = paths
        self.base_url = "%s://%s:%d%s" % (scheme, host, port, prefix)

    def path(self, key):
        return self.prefix + self.paths[key]


PUBLIC_ENDPOINT = Endpoint(
    "public", "https", PUBLIC_HOST, PUBLIC_PORT, PUBLIC_USE_TLS, PUBLIC_PREFIX, PUBLIC_PATHS)
LOCAL_ENDPOINT = Endpoint(
    "local", "http", LOCAL_HOST, LOCAL_PORT, LOCAL_USE_TLS, LOCAL_PREFIX, LOCAL_PATHS)

pathish_re = re.compile(r"[/\\]|\.\.")
control_re = re.compile(r"[\x00-\x1f\x7f]")


class TransportError(Exception):
    def __init__(self, category, detail=""):
        super().__init__(category + (": " + detail if detail else ""))
        self.category = category
        self.detail = detail


class DeadlineExceeded(TransportError):
    def __init__(self):
        super().__init__("timeout")


class SSEError(TransportError):
    """Raised when the server sends an SSE `error` object. `category` is the bounded,
    sanitized error classification (never a raw/secrets-bearing payload)."""

    def __init__(self, category):
        super().__init__(category)


# Categories that must STOP the run loop (engine crash / auth / rate). A generic server_error
# fails the one request but does NOT stop the whole probe.
FATAL_ERROR_CATEGORIES = frozenset({"engine_crash", "auth_error", "rate_error"})


def _classify_sse_error(obj):
    """Map an SSE `error` object to a bounded, sanitized category."""
    err = obj.get("error") if isinstance(obj, dict) else None
    if not isinstance(err, dict):
        return "server_error"
    etype = err.get("type")
    emsg = err.get("message")
    comb = " ".join(str(x).lower() for x in (etype, emsg) if isinstance(x, str))
    if any(k in comb for k in ("auth", "invalid_api_key", "permission", "forbidden")):
        return "auth_error"
    if any(k in comb for k in ("rate", "limit", "quota", "429")):
        return "rate_error"
    # Engine terminal failure e.g. "the engine stopped unexpectedly (exit code -6)".
    if any(k in comb for k in ("exit", "crash", "stopped", "restart", "engine")):
        return "engine_crash"
    return "server_error"


# ---------------------------------------------------------------------------
# Clocks (seams for offline tests)
# ---------------------------------------------------------------------------

def wall_utc_now():
    return _dt.datetime.now(_dt.timezone.utc)


def mono_ns_now():
    """Actual monotonic clock (ns). time.time_ns() is NOT monotonic."""
    return time.monotonic_ns()


class Deadline:
    """Combined absolute UTC + monotonic end bound (min of both)."""

    def __init__(self, end_wall_utc):
        self.end_wall = end_wall_utc
        now_wall = wall_utc_now()
        self.end_mono = mono_ns_now() + int(
            max((end_wall_utc - now_wall).total_seconds(), 0.0) * 1e9)

    @classmethod
    def from_timeout(cls, timeout_s):
        return cls(wall_utc_now() + _dt.timedelta(seconds=timeout_s))

    @classmethod
    def from_mono_ns(cls, mono_ns):
        d = cls(wall_utc_now() + _dt.timedelta(days=365))
        d.end_mono = mono_ns
        return d

    def ns(self):
        """Remaining nanoseconds, min of remaining UTC wall time and monotonic time."""
        wall_left = (self.end_wall - wall_utc_now()).total_seconds() * 1e9
        mono_left = self.end_mono - mono_ns_now()
        return min(wall_left, mono_left)

    def ensure(self):
        if self.ns() <= 0:
            raise DeadlineExceeded()

    @property
    def mono_end(self):
        return self.end_mono


# ---------------------------------------------------------------------------
# Safe model ID handling
# ---------------------------------------------------------------------------

def safe_model_record(model_id):
    """Keep an exact model ID only when safe; redact/hash unsafe IDs."""
    if not isinstance(model_id, str) or not model_id.strip():
        return {"id": "REDACTED_INVALID", "redacted": True, "sha256": None}
    if (len(model_id) > MAX_MODEL_ID_LEN or control_re.search(model_id)
            or pathish_re.search(model_id)):
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


def sanitize_metadata(obj, allowed_fields, max_str_len=MAX_METADATA_STR_LEN):
    """Keep ONLY explicit whitelisted scalar fields; never dump raw payloads/paths."""
    out = {}
    if isinstance(obj, dict):
        for k, v in obj.items():
            if k not in allowed_fields:
                continue
            if isinstance(v, bool):
                out[k] = v
            elif isinstance(v, int):
                out[k] = v
            elif isinstance(v, float):
                out[k] = v
            elif isinstance(v, str):
                if (0 < len(v) <= max_str_len and not pathish_re.search(v)
                        and not control_re.search(v)):
                    out[k] = v
    return out


LOCAL_HEALTH_ALLOWED = frozenset({"status"})
LOCAL_VERSION_ALLOWED = frozenset({"version"})
LOCAL_PROPS_ALLOWED = frozenset(
    {"max_tokens", "context_length", "max_input_tokens", "max_output_tokens"})


# ---------------------------------------------------------------------------
# Bounded HTTP transport
# ---------------------------------------------------------------------------
# The public cloud route is served through an inherited HTTP(S)_PROXY + system CA (a urllib
# opener). Direct `http.client` is not used for the public route because it fails on the executor
# proxy route. Loopback endpoints are always opened directly (no proxy). Deadline/revocation is
# enforced before every connect and every read phase.

_opener_cache = {}


class _NoRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Refuse ALL redirects: returning None makes urllib surface the 3xx as an HTTPError
    instead of following it. Credential-bearing requests therefore never leave the fixed,
    allowlisted origin (the pass-10 actual double-prefix defect is fixed in `_origin_url`).
    """

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def _is_loopback(endpoint):
    return endpoint.host in ("127.0.0.1", "::1", "localhost")


def _build_opener(endpoint):
    key = (endpoint.scheme, endpoint.use_tls, _is_loopback(endpoint))
    if key in _opener_cache:
        return _opener_cache[key]
    handlers = []
    if _is_loopback(endpoint):
        # Never proxy a loopback endpoint (keeps offline framing tests hermetic).
        handlers.append(urllib.request.ProxyHandler({}))
    else:
        proxies = urllib.request.getproxies()   # honour inherited HTTP(S)_PROXY / NO_PROXY
        handlers.append(urllib.request.ProxyHandler(proxies if proxies else {}))
    # Refuse all redirects; credentials must never be forwarded to a redirected origin.
    handlers.append(_NoRedirectHandler())
    if endpoint.use_tls:
        # Verification ON, fixed origin: use the system CA bundle.
        handlers.append(urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    opener = urllib.request.build_opener(*handlers)
    _opener_cache[key] = opener
    return opener


def _origin_url(endpoint, request_path):
    # `request_path` ALREADY includes the allowlisted prefix (Endpoint.path adds it). Do NOT add
    # base_url's prefix a second time (this was the pass-10 double-`/v1` defect). The single
    # correct route for GET/POST/local/public alike is scheme://host:port + request_path.
    return "%s://%s:%d%s" % (endpoint.scheme, endpoint.host, endpoint.port, request_path)


def _request(endpoint, http_method, request_path, headers, body, dl):
    """Send one request via a bounded urllib opener; returns (resp, resp, headers, status).

    Enforces the combined UTC+monotonic deadline before socket creation and before every phase.
    Raises DeadlineExceeded / TransportError. Redirects are refused, so a 3xx surfaces as a
    readable HTTPError response (never followed; Authorization never leaves the fixed origin).
    """
    dl.ensure()  # refuse already-expired work before socket creation
    timeout = max(dl.ns() / 1e9, STALL_SOCKET_FLOOR_S)
    url = _origin_url(endpoint, request_path)
    req = urllib.request.Request(url, data=body, method=http_method, headers=headers or {})
    try:
        opener = _build_opener(endpoint)
        resp = opener.open(req, timeout=timeout)
    except DeadlineExceeded:
        raise
    except urllib.error.HTTPError as e:
        resp = e                      # non-2xx/redirect: readable response, not an exception
    except urllib.error.URLError as exc:
        reason = getattr(exc, "reason", exc)
        if isinstance(reason, socket.timeout):
            raise DeadlineExceeded()
        raise TransportError("transport_error", type(reason).__name__)
    except socket.timeout:
        raise DeadlineExceeded()
    except (OSError, ValueError) as exc:
        raise TransportError("transport_error", type(exc).__name__)
    status = getattr(resp, "status", None)
    if status is None:
        status = getattr(resp, "code", 0)
    try:
        rheaders = {k.lower(): v for k, v in resp.headers.items()}
    except Exception:
        rheaders = {}
    return resp, resp, rheaders, status


def _set_read_timeout(resp, timeout):
    """Bound the underlying socket read so each blocking read is limited to `timeout`.

    Reaches the real stdlib socket WITHOUT bypassing TLS:
      * an HTTPS response's `fp.raw` is the SSLSocket, which itself owns `settimeout` -> use it;
      * a plain HTTP response's `fp.raw` is a socket.SocketIO, which has NO `settimeout`
        (AttributeError) -> descend to its `_sock` (the real socket) and bound that.
    Any unexpected object shape leaves the absolute deadline loop (`_bounded_chunks`) as the
    backstop, so a hard bound is never silently claimed when the socket is unbound.
    """
    try:
        raw = getattr(getattr(resp, "fp", None), "raw", None)
        if raw is None:
            return
        setter = getattr(raw, "settimeout", None)
        if setter is None:
            sock = getattr(raw, "_sock", None)
            setter = getattr(sock, "settimeout", None) if sock is not None else None
        if setter is not None:
            setter(timeout)
    except Exception:
        pass  # best-effort; the absolute deadline loop still guards `_bounded_chunks`


def _read_some(resp, dl):
    """One bounded read. Returns bytes (b"" = EOF) or None (socket stall/timeout)."""
    remaining_s = dl.ns() / 1e9
    timeout = min(remaining_s, READ_STALL_MAX_S)
    if timeout < STALL_SOCKET_FLOOR_S:
        timeout = STALL_SOCKET_FLOOR_S
    _set_read_timeout(resp, timeout)
    try:
        reader = getattr(resp, "read1", None)
        if reader is None:
            reader = resp.read      # e.g. an HTTPError (redirect) body: read once
        return reader()
    except socket.timeout:
        return None                  # stall: caller loops under absolute-deadline control
    except (OSError, ValueError) as exc:
        # A socket whose timeout has expired surfaces (through http.client's buffered reader
        # over the timed-out SocketIO) as OSError("cannot read from timed out object") rather
        # than socket.timeout; treat that as a stall, not a hard transport failure. The
        # absolute deadline loop still enforces the real bound. Any genuine OSError or
        # ValueError (e.g. an HTTPError redirect body) stays a transport error.
        if isinstance(exc, OSError) and ("timed out" in str(exc) or "timeout" in str(exc).lower()):
            return None
        raise TransportError("transport_error", type(exc).__name__)


def _bounded_chunks(resp, dl):
    """Yield body chunks; EACH blocking read is bounded to min(remaining budget,
    READ_STALL_MAX_S). Stalls loop until the absolute UTC+monotonic deadline stops them."""
    while True:
        dl.ensure()                  # absolute bound checked before every read
        chunk = _read_some(resp, dl)
        if chunk is None:
            continue                 # stalled: re-check deadline at top of loop
        if not chunk:
            return                   # EOF
        yield chunk


def _read_all(resp, dl, cap):
    total = 0
    out = b""
    for chunk in _bounded_chunks(resp, dl):
        total += len(chunk)
        if total > cap:
            raise TransportError("too_large")
        out += chunk
    return out


def http_get(request_path, headers=None, timeout_s=10.0, endpoint=None, dl=None):
    """Bounded GET; returns (status, body_bytes)."""
    ep = endpoint if endpoint is not None else PUBLIC_ENDPOINT
    if dl is None:
        dl = Deadline.from_timeout(timeout_s)
    try:
        resp, conn, _, status = _request(ep, "GET", request_path, headers, None, dl)
    except DeadlineExceeded:
        raise
    except TransportError:
        raise
    except (OSError, socket.error) as exc:
        raise TransportError("connect_error", type(exc).__name__)
    try:
        return status, _read_all(resp, dl, MAX_GET_BYTES)
    finally:
        try:
            conn.close()
        except Exception:
            pass


def http_post_stream(request_path, body, headers, dl, endpoint=None):
    """Bounded streaming POST; returns (status, resp_headers, chunk_iter, conn)."""
    ep = endpoint if endpoint is not None else PUBLIC_ENDPOINT
    try:
        resp, conn, rheaders, status = _request(ep, "POST", request_path, headers, body, dl)
    except DeadlineExceeded:
        raise
    except TransportError:
        raise
    except (OSError, socket.error) as exc:
        raise TransportError("connect_error", type(exc).__name__)
    return status, rheaders, _bounded_chunks(resp, dl), conn


# ---------------------------------------------------------------------------
# SSE chat-stream parser (pure, offline-testable)
# ---------------------------------------------------------------------------

def parse_sse_chunks(chunk_iter, start_ns, dl, clock=None):
    """Consume byte chunks, parse SSE `data:` events for a chat completion stream.

    Returns {first_text_delta_s, total_s, output_chars, output_sha256, usage, complete}.
    Raises DeadlineExceeded / TransportError("too_large") / SSEError (server `error` event)
    as appropriate. Standard CRLF (`\\r\\n`) SSE delimiters are normalized.
    """
    if clock is None:
        clock = mono_ns_now
    pieces = []
    first_text_ns = None
    usage = None
    complete = False
    buf = b""
    total_bytes = 0
    server_timings = None
    for chunk in chunk_iter:
        dl.ensure()
        total_bytes += len(chunk)
        if total_bytes > STREAM_MAX_BYTES:
            raise TransportError("too_large", "stream exceeds bound")
        buf += chunk
        buf = buf.replace(b"\r\n", b"\n")   # normalize standard CRLF SSE delimiters
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
            # SSE `error` object: fail THIS request with a bounded, sanitized category. The
            # caller decides whether it is fatal (engine crash/auth/rate) and must stop.
            if "error" in obj:
                raise SSEError(_classify_sse_error(obj))
            if isinstance(obj.get("usage"), dict):
                usage = {
                    "prompt_tokens": obj["usage"].get("prompt_tokens"),
                    "completion_tokens": obj["usage"].get("completion_tokens"),
                    "total_tokens": obj["usage"].get("total_tokens"),
                }
            # Optional server-provided timings (queue/draft/cache/...) are kept SEPARATE from the
            # client-side totals; only bounded numeric scalars are retained, never raw payloads.
            if server_timings is None and (
                    isinstance(obj.get("timings"), dict) or isinstance(obj.get("server_timings"), dict)):
                _src = obj.get("timings") if isinstance(obj.get("timings"), dict) else obj.get("server_timings")
                st = {}
                for _k, _v in _src.items():
                    if len(st) >= 32:
                        break
                    if isinstance(_v, bool):
                        st[_k] = _v
                    elif isinstance(_v, int) and not isinstance(_v, bool):
                        st[_k] = _v
                    elif isinstance(_v, float):
                        st[_k] = _v
                if st:
                    server_timings = {"source": "server", "values": st}
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
        "first_text_delta_s": ((first_text_ns - start_ns) / 1e9)
        if first_text_ns is not None else None,
        "total_s": (now - start_ns) / 1e9,
        "output_chars": len(output),
        "output_sha256": hashlib.sha256(output.encode("utf-8")).hexdigest() if output else None,
        "usage": usage,
        "server_timings": server_timings,
        "complete": complete,
    }


# ---------------------------------------------------------------------------
# Authenticated /models (per-endpoint, actual returned ids only)
# ---------------------------------------------------------------------------

def fetch_models(key, ep, dl):
    headers = {"Authorization": "Bearer " + key}
    try:
        status, body = http_get(ep.path("models"), headers=headers, endpoint=ep, dl=dl)
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
# Read-only local loopback metadata (whitelist-only, optional, separate)
# ---------------------------------------------------------------------------

def probe_simple(ep, path_key, dl, allowed_fields):
    try:
        status, body = http_get(ep.path(path_key), headers={}, endpoint=ep, dl=dl)
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
    return {"status": "ok", "extracted": sanitize_metadata(obj, allowed_fields)}


def optional_local_probe(dl):
    out = {"probed": False, "base_url": LOCAL_ENDPOINT.base_url}
    if not ENABLE_LOCAL_8081:
        out["reason"] = "disabled (set THUNDER_PROBE_LOCAL_8081=1 to enable)"
        return out
    out["probed"] = True
    out["health"] = probe_simple(LOCAL_ENDPOINT, "health", dl, LOCAL_HEALTH_ALLOWED)
    out["version"] = probe_simple(LOCAL_ENDPOINT, "version", dl, LOCAL_VERSION_ALLOWED)
    out["props"] = probe_simple(LOCAL_ENDPOINT, "props", dl, LOCAL_PROPS_ALLOWED)
    key_info = read_api_key()
    if key_info["status"] != "ok":
        out["models"] = {"status": "error", "category": "key_%s" % key_info["status"]}
        return out
    out["models"] = fetch_models(key_info["key"], LOCAL_ENDPOINT, dl)
    # Local endpoint identity is recorded as observed; never assume the public id/alias.
    out["expected_model_id_assumed"] = False
    return out


# ---------------------------------------------------------------------------
# Native observations (bounded read-only; each honours the deadline)
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


def collect_native(dl):
    dl.ensure()
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


def try_nvidia_smi(dl):
    dl.ensure()
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

def run_one(kind, model_id, seq, dl, key, ep, prompt=None, max_tokens=None):
    start_wall = wall_utc_now()
    start_ns = mono_ns_now()
    if prompt is None:
        prompt = SHORT_PROMPT if kind == "short" else LONG_PROMPT
    if max_tokens is None:
        max_tokens = MAX_OUTPUT_TOKENS
    payload = json.dumps({
        "model": model_id,
        "messages": [{"role": "user", "content": prompt}],
        "stream": True,
        "max_tokens": max_tokens,
    }).encode("utf-8")
    headers = {"Authorization": "Bearer " + key, "Content-Type": "application/json"}
    record = {"kind": kind, "seq": seq, "outcome": "started"}
    try:
        status, _, chunks, conn = http_post_stream(ep.path("chat"), payload, headers, dl, endpoint=ep)
        try:
            if status != 200:
                record["outcome"] = "error"
                record["error_category"] = "http_%d" % status
                return record
            parsed = parse_sse_chunks(chunks, start_ns, dl)
            record["outcome"] = "completed" if parsed["complete"] else "incomplete"
            record["first_text_delta_s"] = parsed["first_text_delta_s"]
            record["total_s"] = parsed["total_s"]
            record["output_chars"] = parsed["output_chars"]
            record["output_sha256"] = parsed["output_sha256"]
            record["usage"] = parsed["usage"]
            return record
        finally:
            if conn is not None:
                try:
                    conn.close()
                except Exception:
                    pass
    except DeadlineExceeded:
        record["outcome"] = "timeout"
        return record
    except SSEError as exc:
        # Server sent an SSE `error` object: fail THIS request with the bounded, sanitized
        # category. `fatal` marks engine crash/auth/rate, which must stop the run loop.
        record["outcome"] = "error"
        record["error_category"] = exc.category
        record["fatal"] = exc.category in FATAL_ERROR_CATEGORIES
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
# Output (private, NEW owned dir, unique exclusive non-overwrite file)
# ---------------------------------------------------------------------------

PRIVATE_OUTPUT_DIR = None  # optional test redirect; None => default owned dir


def _private_output_dir():
    if PRIVATE_OUTPUT_DIR:
        return PRIVATE_OUTPUT_DIR
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), "thunder_probe_private")


def _resolve_output_base(directory):
    """Resolve the BASE directory where a fresh private output dir is created.

    The base is never chmod'd, reused, or written through. A symlink base is refused (we must
    never write through a symlink). Returns the base path (which exists as a real directory).
    """
    base = directory if directory is not None else _private_output_dir()
    if os.path.islink(base):
        raise RuntimeError("private output base must not be a symlink")
    if os.path.exists(base):
        if not os.path.isdir(base):
            raise RuntimeError("private output base exists and is not a directory")
        return base
    try:
        os.makedirs(base, mode=0o700)
    except OSError as exc:
        raise RuntimeError("private output base failed: %s" % type(exc).__name__)
    return base


def _fresh_private_dir(base):
    """Exclusively create a FRESH private 0700 directory under base.

    Uses os.mkdir (which fails if the name already exists and never follows a symlink), so we
    never chmod or reuse a pre-existing directory/symlink and never overwrite existing output.
    """
    last_err = None
    for _ in range(16):
        token = secrets.token_hex(4)
        candidate = os.path.join(base, "thunder_probe_private_" + token)
        try:
            os.mkdir(candidate, 0o700)
            return candidate
        except FileExistsError as exc:
            last_err = exc
            continue
    raise RuntimeError("could not allocate exclusive private output dir: %s" % last_err)


def _write_private_json(result, directory=None):
    base = _resolve_output_base(directory)
    d = _fresh_private_dir(base)
    data = json.dumps(result, indent=2, sort_keys=True) + "\n"
    base_name = "thunder_local_pc_probe_result_"
    last_err = None
    for _ in range(16):
        token = time.strftime("%Y%m%dT%H%M%S", time.gmtime()) + "_" + secrets.token_hex(4)
        path = os.path.join(d, base_name + token + ".json")
        try:
            fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
            break
        except FileExistsError as exc:
            last_err = exc
            continue
    else:
        raise RuntimeError("could not allocate exclusive output file: %s" % last_err)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    return path


def _finalize(result):
    path = _write_private_json(result)
    target = "COMPLETE" if result["summary"]["target_complete"] else "INCOMPLETE"
    print("probe result: %s" % os.path.basename(path))
    print("window_status=%s target=%s short=%d/%d long=%d/%d" % (
        result["window_status"], target,
        result["summary"]["success_short"], 5,
        result["summary"]["success_long"], 5))
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
        "mode": "quick-smoke",      # "baseline" when --baseline is passed
        "native": None,
        "service": {
            "endpoint": PUBLIC_ENDPOINT.base_url,
            "expected_model_id": EXPECTED_MODEL_ID,
            "expected_model_ids": list(EXPECTED_MODEL_IDS),
            "models_pre": None,
            "models_post": None,
            "models_stable": None,
            "selected_model_id": None,
            "identity_match": None,
            "local": None,
        },
        "runs": [],
        "summary": {
            "plan": ["short", "long"] * 5,
            "success_count": 0,
            "success_short": 0,
            "success_long": 0,
            "target_complete": False,
        },
        "limitations": list(LIMITATIONS),
    }


def _parse_args(argv):
    """Parse CLI args. Only the key-file PATH are accepted (never the key value), plus the
    opt-in `--baseline` flag for the documented bounded baseline mode. Returns (key_file,
    baseline)."""
    key_file = None
    baseline = False
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--key-file":
            if i + 1 >= len(argv):
                raise SystemExit("--key-file requires a path argument")
            key_file = argv[i + 1]
            i += 2
        elif a == "--baseline":
            baseline = True
            i += 1
        elif a in ("-h", "--help"):
            print("usage: thunder_local_pc_probe.py [--key-file PATH] [--baseline]")
            raise SystemExit(0)
        else:
            raise SystemExit("unknown argument: %s" % a)
    return key_file, baseline


def main(argv=None):
    global KEY_PATH
    args = list(argv) if argv is not None else sys.argv[1:]
    key_file, baseline = _parse_args(args)
    if key_file:
        KEY_PATH = key_file          # explicit path; only the path is used, never the value
    start_now = wall_utc_now()
    result = base_result(start_now)
    result["mode"] = "baseline" if baseline else "quick-smoke"

    if start_now < WINDOW_START_UTC:
        result["window_status"] = "not_started"
        return _finalize(result)
    if start_now >= WINDOW_END_UTC:
        result["window_status"] = "window_closed"
        return _finalize(result)

    result["window_status"] = "open"
    dl = Deadline(WINDOW_END_UTC)          # ONE absolute UTC+monotonic end bound

    # Native observations (bounded read-only, before we need authentication).
    try:
        dl.ensure()
    except DeadlineExceeded:
        result["window_status"] = "window_closed"
        return _finalize(result)
    result["native"] = collect_native(dl)
    result["native"]["gpu"] = try_nvidia_smi(dl)

    # Optional local loopback observation (separate, actual ids only).
    try:
        dl.ensure()
    except DeadlineExceeded:
        result["window_status"] = "window_closed"
        return _finalize(result)
    result["service"]["local"] = optional_local_probe(dl)

    # Credential (local, read-only, never emitted).
    key_info = read_api_key()
    if key_info["status"] != "ok":
        result["service"]["key_status"] = key_info["status"]
        result["limitations"].append(
            "api key unavailable; authenticated /models and inference skipped")
        return _finalize(result)
    key = key_info["key"]

    # Authenticated pre-run model capture on the verified public endpoint.
    try:
        dl.ensure()
    except DeadlineExceeded:
        result["window_status"] = "window_closed"
        return _finalize(result)
    models_pre = fetch_models(key, PUBLIC_ENDPOINT, dl)
    result["service"]["models_pre"] = models_pre
    if models_pre["status"] != "ok":
        result["limitations"].append("pre-run public /models unavailable")
        return _finalize(result)

    present = [mid for mid in models_pre["ids"] if mid in EXPECTED_MODEL_IDS]
    if not present:
        result["service"]["identity_match"] = False
        result["service"]["selected_model_id"] = None
        result["limitations"].append(
            "expected public model ids not observed on the fixed endpoint; inference skipped")
        try:
            dl.ensure()
            models_post = fetch_models(key, PUBLIC_ENDPOINT, dl)
            result["service"]["models_post"] = models_post
            result["service"]["models_stable"] = (
                models_post["status"] == "ok" and models_post.get("ids") == models_pre["ids"])
        except DeadlineExceeded:
            pass
        return _finalize(result)

    # Default model selection: prefer the dynamic alias (strata-dynamic) when present, else the
    # verified flash id. Only exact ids the endpoint actually returned are used.
    result["service"]["identity_match"] = True
    result["service"]["selected_model_id"] = (
        PUBLIC_MODEL_ALIAS if PUBLIC_MODEL_ALIAS in present else present[0])
    selected = result["service"]["selected_model_id"]

    # Ordered interleaved runs: short,long repeated 5 (need 5 valid of each). In baseline mode
    # use fixed synthetic prompts, a 256-token cap, a 90s per-run ceiling (steady-decode
    # sample); the default is the quick-smoke (2+2 / max 32). Cached/uncached are never mixed
    # as the same prefill.
    plan = ["short", "long"] * 5
    result["summary"]["plan"] = plan
    max_run_s = MAX_RUN_SECONDS
    max_tokens = MAX_OUTPUT_TOKENS
    if baseline:
        max_run_s = BASELINE_MAX_RUN_SECONDS
        max_tokens = BASELINE_MAX_OUTPUT_TOKENS

    reserved_end_ns = int(POST_CAPTURE_MARGIN_S * 1e9)
    near_end_ns = int(NEAR_END_STOP_S * 1e9)
    seq = 1
    for kind in plan:
        try:
            dl.ensure()
        except DeadlineExceeded:
            result["summary"]["truncated_reason"] = "window_end_reached"
            break
        if dl.ns() <= reserved_end_ns:
            result["summary"]["truncated_reason"] = "insufficient_time_before_window_end"
            break
        # Per-run deadline: min(overall window bound, now + per-run ceiling).
        run_dl = Deadline(WINDOW_END_UTC)
        run_dl.end_mono = min(run_dl.end_mono, mono_ns_now() + int(max_run_s * 1e9))
        if baseline:
            prompt_arg = BASELINE_SHORT_PROMPT if kind == "short" else BASELINE_LONG_PROMPT
        else:
            prompt_arg = None
        rec = run_one(kind, selected, seq, run_dl, key, PUBLIC_ENDPOINT,
                      prompt=prompt_arg, max_tokens=max_tokens)
        result["runs"].append(rec)
        if rec["outcome"] == "completed":
            result["summary"]["success_count"] += 1
            if kind == "short":
                result["summary"]["success_short"] += 1
            else:
                result["summary"]["success_long"] += 1
        # Engine crash / auth / rate must STOP further inference (post-inventory is still
        # captured below, without retrying or reloading).
        if rec.get("fatal"):
            result["summary"]["truncated_reason"] = "fatal_%s" % rec["error_category"]
            break
        seq += 1
        if dl.ns() <= near_end_ns:
            result["summary"]["truncated_reason"] = "near_window_end"
            break

    result["summary"]["target_complete"] = (
        result["summary"]["success_short"] >= 5 and result["summary"]["success_long"] >= 5)

    # Authenticated post-run model capture within the window.
    try:
        dl.ensure()
        models_post = fetch_models(key, PUBLIC_ENDPOINT, dl)
        result["service"]["models_post"] = models_post
        result["service"]["models_stable"] = (
            models_post["status"] == "ok" and models_post.get("ids") == models_pre["ids"])
    except DeadlineExceeded:
        result["limitations"].append("post-run /models skipped: window end reached")

    return _finalize(result)


if __name__ == "__main__":
    sys.exit(main())
