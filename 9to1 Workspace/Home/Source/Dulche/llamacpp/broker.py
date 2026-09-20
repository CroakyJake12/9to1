#!/usr/bin/env python3
"""Haven local inference broker for a single, supervised llama.cpp worker.

The broker owns the HUI-facing API. llama-server is an implementation detail
reachable only over a private Unix-domain socket. Model-store mutation and
network downloads are deliberately outside this least-privilege service.
"""
from __future__ import annotations

import errno
import hashlib
import http.client
import http.server
import json
import os
import pathlib
import re
import signal
import socket
import socketserver
import stat
import subprocess
import threading
import time
import urllib.parse
from dataclasses import dataclass, field
from typing import Any

from gguf import GgufValidationError, read_gguf_version
from model_lease import ModelLease, ModelLeaseBusy, ModelLeaseError, acquire_model_lease, ensure_private_directory


class BrokerError(RuntimeError):
    pass


MODEL_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
REQUEST_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")


def _bounded_env_int(name: str, default: int, minimum: int, maximum: int) -> int:
    raw = os.environ.get(name)
    if raw is None or not raw.strip():
        return default
    try:
        value = int(raw)
    except ValueError as exc:
        raise BrokerError(f"{name} must be an integer") from exc
    if not minimum <= value <= maximum:
        raise BrokerError(f"{name} must be between {minimum} and {maximum}")
    return value


_xdg_runtime = os.environ.get("XDG_RUNTIME_DIR")
RUNTIME_DIR = pathlib.Path(_xdg_runtime) / "haven" if _xdg_runtime else pathlib.Path("/tmp") / f"haven-{os.getuid()}"
DATA_HOME = pathlib.Path(os.environ.get("XDG_DATA_HOME", pathlib.Path.home() / ".local/share")) / "haven"
MODEL_ROOT = DATA_HOME / "models"
MANIFEST_ROOT = MODEL_ROOT / "manifests"
BLOB_ROOT = MODEL_ROOT / "blobs" / "sha256"
BROKER_SOCKET = pathlib.Path(os.environ.get("HAVEN_INFERENCE_SOCKET", RUNTIME_DIR / "inference.sock"))
WORKER_SOCKET = pathlib.Path(os.environ.get("HAVEN_LLAMA_WORKER_SOCKET", RUNTIME_DIR / "llamacpp-worker.sock"))
WORKER_HOME = RUNTIME_DIR / "worker-home"
LLAMA_SERVER = pathlib.Path(os.environ.get("HAVEN_LLAMA_SERVER", "/usr/lib/haven/llama.cpp/llama-server"))
START_TIMEOUT_SECONDS = float(os.environ.get("HAVEN_LLAMA_START_TIMEOUT", "30"))
CONTEXT_LIMIT = _bounded_env_int("HAVEN_LLAMA_CONTEXT_LIMIT", 8192, 512, 131072)
PROVIDER_ID = "llamacpp"


def canonical_blob_relative(digest: str) -> str:
    return str(pathlib.PurePosixPath(digest[:2]) / f"{digest}.gguf")


def provider_key(model_id: str) -> str:
    return f"{PROVIDER_ID}:{model_id}"


def validate_request_id(request_id: str) -> str:
    value = request_id.strip()
    if not REQUEST_ID_RE.fullmatch(value):
        raise BrokerError("request_id must use only letters, digits, '.', '_', ':', or '-' and be at most 128 characters")
    return value


@dataclass(frozen=True)
class ModelManifest:
    model_id: str
    display_name: str
    sha256: str
    size: int
    relative_blob: str
    license_id: str
    source: str
    gguf_versions: tuple[int, ...]

    @property
    def key(self) -> str:
        return provider_key(self.model_id)

    @property
    def blob_path(self) -> pathlib.Path:
        expected = canonical_blob_relative(self.sha256)
        if self.relative_blob != expected:
            raise BrokerError(f"manifest blob must use canonical content address {expected}")
        candidate = (BLOB_ROOT / self.relative_blob).resolve()
        root = BLOB_ROOT.resolve()
        if root not in candidate.parents:
            raise BrokerError("manifest blob path escapes the content-addressed store")
        return candidate


def _json(path: pathlib.Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise BrokerError(f"{path.name}: expected a JSON object")
    return value


def load_manifests() -> dict[str, ModelManifest]:
    result: dict[str, ModelManifest] = {}
    if not MANIFEST_ROOT.exists():
        return result
    for path in sorted(MANIFEST_ROOT.glob("*.json")):
        raw = _json(path)
        required = ("id", "displayName", "sha256", "size", "blob", "license", "source", "ggufVersions")
        missing = [key for key in required if key not in raw]
        if missing:
            raise BrokerError(f"{path.name}: missing {', '.join(missing)}")

        digest = str(raw["sha256"]).lower()
        if len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise BrokerError(f"{path.name}: invalid sha256")

        model_id = str(raw["id"])
        if not MODEL_ID_RE.fullmatch(model_id) or model_id in result:
            raise BrokerError(f"{path.name}: invalid or duplicate model id")
        if path.stem != model_id:
            raise BrokerError(f"{path.name}: manifest filename must match model id '{model_id}'")

        try:
            size = int(raw["size"])
        except (TypeError, ValueError) as exc:
            raise BrokerError(f"{path.name}: invalid model size") from exc
        if size < 8:
            raise BrokerError(f"{path.name}: model size is too small to contain a GGUF header")

        versions_raw = raw["ggufVersions"]
        if not isinstance(versions_raw, list):
            raise BrokerError(f"{path.name}: ggufVersions must be a list")
        gguf_versions = tuple(int(v) for v in versions_raw)
        if not gguf_versions or any(v not in (2, 3) for v in gguf_versions):
            raise BrokerError(f"{path.name}: manifests may declare only GGUF versions 2 and 3")

        display_name = str(raw["displayName"]).strip()
        license_id = str(raw["license"]).strip()
        source = str(raw["source"]).strip()
        if not display_name or not license_id or not source:
            raise BrokerError(f"{path.name}: displayName, license, and source must be non-empty")

        manifest = ModelManifest(
            model_id=model_id,
            display_name=display_name,
            sha256=digest,
            size=size,
            relative_blob=str(raw["blob"]),
            license_id=license_id,
            source=source,
            gguf_versions=gguf_versions,
        )
        _ = manifest.blob_path
        result[model_id] = manifest
    return result


def verify_blob(manifest: ModelManifest) -> pathlib.Path:
    path = manifest.blob_path
    if not path.is_file() or path.is_symlink():
        raise BrokerError("model blob is not installed as a regular file")
    stat_result = path.stat()
    if stat_result.st_size != manifest.size:
        raise BrokerError("model size does not match its manifest")
    try:
        version = read_gguf_version(path)
    except GgufValidationError as exc:
        raise BrokerError(str(exc)) from exc
    if version not in manifest.gguf_versions:
        raise BrokerError(f"GGUF version {version} is not allowed by this model manifest")
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest() != manifest.sha256:
        raise BrokerError("model sha256 does not match its manifest")
    return path


class UnixHTTPConnection(http.client.HTTPConnection):
    def __init__(self, unix_path: pathlib.Path, timeout: float = 3600):
        super().__init__("localhost", timeout=timeout)
        self.unix_path = str(unix_path)

    def connect(self) -> None:
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(self.unix_path)
        self.sock = sock

    def duplicate_transport(self) -> socket.socket:
        if self.sock is None:
            raise BrokerError("cannot duplicate an unconnected worker transport")
        return self.sock.dup()

    def abort(self) -> None:
        sock = self.sock
        if sock is not None:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        self.close()


def build_worker_args(manifest: ModelManifest, model_path: pathlib.Path) -> list[str]:
    return [
        str(LLAMA_SERVER),
        "--model", str(model_path),
        "--alias", manifest.model_id,
        "--host", str(WORKER_SOCKET),
        "--no-ui",
        "--no-slots",
        "--log-disable",
        "--offline",
        "--parallel", "1",
        "--ctx-size", str(CONTEXT_LIMIT),
        "--cache-ram", "0",
    ]


def worker_environment() -> dict[str, str]:
    env = os.environ.copy()
    for key in tuple(env):
        upper = key.upper()
        if key.startswith("LLAMA_ARG_") or key.startswith("GGML_") or upper in {
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "ALL_PROXY",
            "HF_TOKEN",
            "HUGGING_FACE_HUB_TOKEN",
            "LLAMA_API_KEY",
            "LD_PRELOAD",
            "LD_LIBRARY_PATH",
        }:
            env.pop(key, None)
    env["HOME"] = str(WORKER_HOME)
    return env


class Worker:
    def __init__(self) -> None:
        self._process: subprocess.Popen[bytes] | None = None
        self._model: ModelManifest | None = None
        self._lease: ModelLease | None = None
        self._lock = threading.RLock()

    @property
    def model(self) -> ModelManifest | None:
        with self._lock:
            return self._model

    @property
    def ready(self) -> bool:
        with self._lock:
            return self._process is not None and self._process.poll() is None and WORKER_SOCKET.exists()

    def load(self, manifest: ModelManifest) -> None:
        with self._lock:
            if self.ready and self._model and self._model.model_id == manifest.model_id:
                return
            try:
                lease = acquire_model_lease(RUNTIME_DIR, manifest.model_id, exclusive=False, blocking=False)
            except (ModelLeaseBusy, ModelLeaseError) as exc:
                raise BrokerError(f"model is being modified and cannot be loaded: {manifest.model_id}") from exc
            try:
                model_path = verify_blob(manifest)
                if not LLAMA_SERVER.is_file():
                    raise BrokerError(f"llama-server is unavailable at {LLAMA_SERVER}")
                self._stop_locked()
                WORKER_SOCKET.unlink(missing_ok=True)
                ensure_private_directory(WORKER_HOME)
                self._process = subprocess.Popen(
                    build_worker_args(manifest, model_path),
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    env=worker_environment(),
                    close_fds=True,
                    start_new_session=True,
                )
                self._lease = lease
                lease = None
                deadline = time.monotonic() + START_TIMEOUT_SECONDS
                while time.monotonic() < deadline:
                    if self._process.poll() is not None:
                        code = self._process.returncode
                        self._stop_locked()
                        raise BrokerError(f"llama-server exited during startup with code {code}")
                    if WORKER_SOCKET.exists():
                        try:
                            os.chmod(WORKER_SOCKET, 0o600)
                            conn = UnixHTTPConnection(WORKER_SOCKET, timeout=1)
                            conn.request("GET", "/health")
                            response = conn.getresponse()
                            response.read()
                            conn.close()
                            if 200 <= response.status < 300:
                                self._model = manifest
                                return
                        except OSError:
                            pass
                    time.sleep(0.1)
                self._stop_locked()
                raise BrokerError("llama-server did not become healthy before the startup deadline")
            except Exception as exc:
                if self._process is not None or self._lease is not None:
                    try:
                        self._stop_locked()
                    except BrokerError as cleanup_exc:
                        raise BrokerError(f"worker startup failed and cleanup also failed: {cleanup_exc}") from exc
                raise
            finally:
                if lease is not None:
                    lease.release()

    def unload(self) -> None:
        with self._lock:
            self._stop_locked()

    def _stop_locked(self) -> None:
        process = self._process
        if process is not None and process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGTERM)
                process.wait(timeout=5)
            except ProcessLookupError:
                pass
            except subprocess.TimeoutExpired:
                if process.poll() is None:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    try:
                        process.wait(timeout=2)
                    except subprocess.TimeoutExpired as exc:
                        raise BrokerError("unable to stop llama-server; retaining model lifecycle lease") from exc
        if process is not None and process.poll() is None:
            raise BrokerError("llama-server remained alive after stop request; retaining model lifecycle lease")
        self._process = None
        self._model = None
        lease = self._lease
        self._lease = None
        try:
            WORKER_SOCKET.unlink(missing_ok=True)
        finally:
            if lease is not None:
                lease.release()


@dataclass
class ActiveRequest:
    connection: UnixHTTPConnection | None
    cancellation_transport: socket.socket | None = None
    cancelled: threading.Event = field(default_factory=threading.Event)
    _lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    def cancel(self) -> None:
        self.cancelled.set()
        with self._lock:
            connection = self.connection
            cancellation_transport = self.cancellation_transport
        if cancellation_transport is not None:
            try:
                cancellation_transport.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        if connection is not None:
            connection.abort()

    def detach(self) -> None:
        with self._lock:
            cancellation_transport = self.cancellation_transport
            self.cancellation_transport = None
            self.connection = None
        if cancellation_transport is not None:
            cancellation_transport.close()


WORKER = Worker()
ACTIVE_REQUESTS: dict[str, ActiveRequest] = {}
ACTIVE_LOCK = threading.Lock()


class ThreadingUnixServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    address_family = socket.AF_UNIX
    daemon_threads = True

    def server_bind(self) -> None:
        path = pathlib.Path(self.server_address)
        if path.exists() or path.is_symlink():
            mode = path.lstat().st_mode
            if not stat.S_ISSOCK(mode):
                raise OSError(errno.EEXIST, f"refusing to replace non-socket path {path}")
            probe = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
            probe.settimeout(0.25)
            try:
                probe.connect(str(path))
            except OSError as exc:
                if exc.errno not in {errno.ECONNREFUSED, errno.ENOENT}:
                    raise
                path.unlink(missing_ok=True)
            else:
                raise OSError(errno.EADDRINUSE, f"broker socket is already active at {path}")
            finally:
                probe.close()
        super().server_bind()
        os.chmod(self.server_address, 0o600)


class Handler(http.server.BaseHTTPRequestHandler):
    server_version = "HavenInference/0"

    def log_message(self, fmt: str, *args: Any) -> None:
        # AF_UNIX peers do not have the TCP-style (host, port) tuple expected by
        # BaseHTTPRequestHandler.address_string(). Keep transport logging local
        # and prompt-free instead of trying to resolve a peer address.
        print(f"haven-inference: local-uds {fmt % args}")

    def _send_json(self, status: int, value: Any) -> None:
        body = json.dumps(value, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _read_json(self) -> dict[str, Any]:
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError as exc:
            raise BrokerError("invalid Content-Length") from exc
        if length < 0 or length > 16 * 1024 * 1024:
            raise BrokerError("request body is too large")
        raw = self.rfile.read(length)
        value = json.loads(raw or b"{}")
        if not isinstance(value, dict):
            raise BrokerError("expected a JSON object")
        return value

    def do_GET(self) -> None:
        try:
            if self.path == "/health":
                self._send_json(200, {"status": "ok", "provider": PROVIDER_ID, "workerReady": WORKER.ready})
                return
            if self.path == "/v1/provider":
                self._send_json(200, {
                    "id": PROVIDER_ID,
                    "displayName": "llama.cpp",
                    "isLocal": True,
                    "modelKeyPrefix": f"{PROVIDER_ID}:",
                    "legacyUnqualifiedProvider": "ollama",
                    "transport": "http-over-unix",
                    "contextLimit": CONTEXT_LIMIT,
                    "parallel": 1,
                    "modelStoreWritable": False,
                })
                return
            if self.path == "/v1/models":
                manifests = load_manifests()
                current = WORKER.model.model_id if WORKER.model else None
                self._send_json(200, {"models": [
                    {
                        "id": item.model_id,
                        "key": item.key,
                        "displayName": item.display_name,
                        "provider": PROVIDER_ID,
                        "installed": item.blob_path.is_file(),
                        "loaded": item.model_id == current,
                        "size": item.size,
                        "license": item.license_id,
                        "source": item.source,
                        "ggufVersions": item.gguf_versions,
                    }
                    for item in manifests.values()
                ]})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def do_POST(self) -> None:
        try:
            parsed = urllib.parse.urlparse(self.path)
            segments = [segment for segment in parsed.path.split("/") if segment]
            if len(segments) == 4 and segments[:2] == ["v1", "models"] and segments[3] in {"load", "unload"}:
                model_id = urllib.parse.unquote(segments[2])
                manifests = load_manifests()
                if model_id not in manifests:
                    self._send_json(404, {"error": "model_not_found"})
                    return
                if segments[3] == "load":
                    WORKER.load(manifests[model_id])
                    self._send_json(200, {"status": "ready", "model": model_id, "key": provider_key(model_id)})
                else:
                    if WORKER.model and WORKER.model.model_id == model_id:
                        WORKER.unload()
                    self._send_json(200, {"status": "unloaded", "model": model_id, "key": provider_key(model_id)})
                return
            if parsed.path == "/v1/chat/completions":
                self._proxy_chat()
                return
            if len(segments) == 4 and segments[:2] == ["v1", "requests"] and segments[3] == "cancel":
                request_id = validate_request_id(urllib.parse.unquote(segments[2]))
                with ACTIVE_LOCK:
                    active = ACTIVE_REQUESTS.get(request_id)
                if active is None:
                    self._send_json(404, {"error": "request_not_found"})
                else:
                    active.cancel()
                    self._send_json(202, {"status": "cancelling", "requestId": request_id})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def _proxy_chat(self) -> None:
        if not WORKER.ready or WORKER.model is None:
            raise BrokerError("no llama.cpp model is loaded")
        payload = self._read_json()
        request_id = validate_request_id(str(payload.pop("request_id", "")))
        payload["model"] = WORKER.model.model_id
        payload["stream"] = True

        conn = UnixHTTPConnection(WORKER_SOCKET)
        conn.connect()
        active = ActiveRequest(conn, conn.duplicate_transport())
        with ACTIVE_LOCK:
            if request_id in ACTIVE_REQUESTS:
                active.detach()
                conn.close()
                raise BrokerError("request_id is already active")
            ACTIVE_REQUESTS[request_id] = active

        headers_sent = False
        try:
            if active.cancelled.is_set():
                self._send_json(409, {"error": "request_cancelled", "requestId": request_id})
                return
            encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
            conn.request("POST", "/v1/chat/completions", body=encoded, headers={"Content-Type": "application/json"})
            if active.cancelled.is_set():
                self._send_json(409, {"error": "request_cancelled", "requestId": request_id})
                return
            response = conn.getresponse()
            self.send_response(response.status)
            content_type = response.getheader("Content-Type", "text/event-stream")
            self.send_header("Content-Type", content_type)
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Haven-Request-Id", request_id)
            self.end_headers()
            headers_sent = True
            while not active.cancelled.is_set():
                chunk = response.read1(4096)
                if not chunk:
                    break
                self.wfile.write(chunk)
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, OSError, http.client.HTTPException) as exc:
            requested_cancel = active.cancelled.is_set()
            active.cancel()
            if not headers_sent:
                if requested_cancel:
                    self._send_json(409, {"error": "request_cancelled", "requestId": request_id})
                else:
                    raise BrokerError(f"llama.cpp worker stream failed: {exc}") from exc
        finally:
            active.detach()
            conn.close()
            with ACTIVE_LOCK:
                if ACTIVE_REQUESTS.get(request_id) is active:
                    ACTIVE_REQUESTS.pop(request_id, None)


def main() -> int:
    ensure_private_directory(RUNTIME_DIR)
    ensure_private_directory(WORKER_HOME)
    server = ThreadingUnixServer(str(BROKER_SOCKET), Handler)
    try:
        server.serve_forever(poll_interval=0.25)
    finally:
        WORKER.unload()
        server.server_close()
        BROKER_SOCKET.unlink(missing_ok=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
