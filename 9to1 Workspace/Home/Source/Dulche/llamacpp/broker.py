#!/usr/bin/env python3
"""Dulche local inference broker for supervised llama.cpp slot workers.

The broker owns the CUI-facing API. llama-server is an implementation detail
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
import queue
import re
import select
import signal
import socket
import socketserver
import stat
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
import uuid
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
_runtime_identity = str(os.getuid()) if hasattr(os, "getuid") else str(os.getpid())
RUNTIME_DIR = pathlib.Path(_xdg_runtime) / "haven" if _xdg_runtime else pathlib.Path(tempfile.gettempdir()) / f"haven-{_runtime_identity}"
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
DEFAULT_DULCHE_PORT = 9477
MAX_MODEL_SLOTS = _bounded_env_int("DULCHE_MAX_MODEL_SLOTS", 8, 1, 64)
MAX_TOOL_OUTPUT = 64_000
HAS_UNIX_SOCKETS = hasattr(socket, "AF_UNIX")


def canonical_blob_relative(digest: str) -> str:
    return str(pathlib.PurePosixPath(digest[:2]) / f"{digest}.gguf")


def provider_key(model_id: str) -> str:
    return f"{PROVIDER_ID}:{model_id}"


def validate_request_id(request_id: str) -> str:
    value = request_id.strip()
    if not REQUEST_ID_RE.fullmatch(value):
        raise BrokerError("request_id must use only letters, digits, '.', '_', ':', or '-' and be at most 128 characters")
    return value


def validate_context_limit(value: Any | None) -> int:
    if value is None or (isinstance(value, str) and not value.strip()):
        return CONTEXT_LIMIT
    try:
        context = int(value)
    except (TypeError, ValueError) as exc:
        raise BrokerError("context must be an integer") from exc
    if not 512 <= context <= 131072:
        raise BrokerError("context must be between 512 and 131072")
    return context


def validate_slot(value: Any) -> int:
    if isinstance(value, bool):
        raise BrokerError("slot must be a non-negative integer")
    try:
        slot = int(value)
    except (TypeError, ValueError) as exc:
        raise BrokerError("slot must be a non-negative integer") from exc
    if not 0 <= slot < MAX_MODEL_SLOTS:
        raise BrokerError(f"slot must be between 0 and {MAX_MODEL_SLOTS - 1}")
    return slot


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
        if not HAS_UNIX_SOCKETS:
            raise BrokerError("Unix-domain sockets are unavailable on this platform")
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(self.unix_path)
        self.sock = sock

    def abort(self) -> None:
        sock = self.sock
        if sock is not None:
            try:
                sock.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass
        self.close()


def cancel_worker_stream(request_id: str, worker_socket: pathlib.Path = WORKER_SOCKET) -> bool:
    """Ask the pinned worker to cancel its resumable stream session."""
    connection = UnixHTTPConnection(worker_socket, timeout=5)
    try:
        query = urllib.parse.urlencode({"conv_id": request_id})
        connection.request("DELETE", f"/v1/stream?{query}")
        response = connection.getresponse()
        response.read()
        return 200 <= response.status < 300
    except (OSError, http.client.HTTPException):
        return False
    finally:
        connection.close()


def build_worker_args(
    manifest: ModelManifest,
    model_path: pathlib.Path,
    worker_socket: pathlib.Path = WORKER_SOCKET,
    context_limit: int = CONTEXT_LIMIT,
) -> list[str]:
    return [
        str(LLAMA_SERVER),
        "--model", str(model_path),
        "--alias", manifest.model_id,
        "--host", str(worker_socket),
        "--no-ui",
        "--no-slots",
        "--log-disable",
        "--offline",
        "--parallel", "1",
        "--ctx-size", str(context_limit),
        "--cache-ram", "0",
    ]


def worker_environment(worker_home: pathlib.Path = WORKER_HOME) -> dict[str, str]:
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
    env["HOME"] = str(worker_home)
    return env


class Worker:
    def __init__(
        self,
        slot: int = 0,
        worker_socket: pathlib.Path | None = None,
        worker_home: pathlib.Path | None = None,
        context_limit: int = CONTEXT_LIMIT,
    ) -> None:
        self.slot = validate_slot(slot)
        self.worker_socket = worker_socket or WORKER_SOCKET
        self.worker_home = worker_home or WORKER_HOME
        self._context_limit = validate_context_limit(context_limit)
        self._pending_context: int | None = None
        self._active_turns = 0
        self._process: subprocess.Popen[bytes] | None = None
        self._model: ModelManifest | None = None
        self._lease: ModelLease | None = None
        self._lock = threading.RLock()

    @property
    def model(self) -> ModelManifest | None:
        with self._lock:
            return self._model

    @property
    def context_limit(self) -> int:
        with self._lock:
            return self._context_limit

    @property
    def pending_context(self) -> int | None:
        with self._lock:
            return self._pending_context

    @property
    def ready(self) -> bool:
        with self._lock:
            return self._process is not None and self._process.poll() is None and self.worker_socket.exists()

    def load(self, manifest: ModelManifest, context_limit: int | None = None, force: bool = False) -> None:
        with self._lock:
            requested_context = self._context_limit if context_limit is None else validate_context_limit(context_limit)
            if self.ready and self._model and self._model.model_id == manifest.model_id and not force:
                if requested_context != self._context_limit:
                    self._pending_context = requested_context
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
                self.worker_socket.unlink(missing_ok=True)
                ensure_private_directory(self.worker_home)
                self._process = subprocess.Popen(
                    build_worker_args(manifest, model_path, self.worker_socket, requested_context),
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    env=worker_environment(self.worker_home),
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
                    if self.worker_socket.exists():
                        try:
                            os.chmod(self.worker_socket, 0o600)
                            conn = UnixHTTPConnection(self.worker_socket, timeout=1)
                            conn.request("GET", "/health")
                            response = conn.getresponse()
                            response.read()
                            conn.close()
                            if 200 <= response.status < 300:
                                self._model = manifest
                                self._context_limit = requested_context
                                self._pending_context = None
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

    def set_context(self, context_limit: Any) -> int:
        """Queue a context change for the next turn without exposing worker controls to models."""
        context = validate_context_limit(context_limit)
        with self._lock:
            if self._model is None:
                raise BrokerError(f"slot {self.slot} has no loaded model")
            self._pending_context = context
            return context

    def begin_turn(self) -> None:
        with self._lock:
            if self._pending_context is not None and self._active_turns == 0:
                manifest = self._model
                if manifest is None:
                    raise BrokerError(f"slot {self.slot} has no loaded model")
                pending = self._pending_context
                self.load(manifest, pending, force=True)
            if not self.ready:
                raise BrokerError(f"slot {self.slot} is not ready")
            self._active_turns += 1

    def end_turn(self) -> None:
        with self._lock:
            if self._active_turns > 0:
                self._active_turns -= 1

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
        self._pending_context = None
        lease = self._lease
        self._lease = None
        try:
            self.worker_socket.unlink(missing_ok=True)
        finally:
            if lease is not None:
                lease.release()


@dataclass
class ActiveRequest:
    connection: UnixHTTPConnection | None
    request_id: str
    worker_socket: pathlib.Path = WORKER_SOCKET
    response_started: threading.Event = field(default_factory=threading.Event)
    cancelled: threading.Event = field(default_factory=threading.Event)
    _lock: threading.Lock = field(default_factory=threading.Lock, repr=False)

    def cancel(self) -> dict[str, bool | str]:
        self.cancelled.set()
        with self._lock:
            connection = self.connection
        control_accepted = cancel_worker_stream(self.request_id, self.worker_socket)
        transport_fallback = not control_accepted or not self.response_started.is_set()
        if transport_fallback and connection is not None:
            connection.abort()
        return {
            "mode": "upstream-stream-delete" if control_accepted else "transport-close",
            "transportFallback": transport_fallback,
            "workerCompletionConfirmed": False,
        }

    def detach(self) -> None:
        with self._lock:
            self.connection = None


@dataclass(frozen=True)
class NativeTool:
    tool_id: str
    display_name: str
    permission: str
    description: str


NATIVE_TOOLS = {
    "dulche.run_command": NativeTool(
        "dulche.run_command",
        "Run command",
        "command.execute",
        "Runs an argument-vector command after explicit permission.",
    ),
    "dulche.run_code": NativeTool(
        "dulche.run_code",
        "Run code",
        "code.execute",
        "Runs supported source code in Dulche's temporary tool directory after explicit permission.",
    ),
}


@dataclass
class PermissionRequest:
    request_id: str
    model_id: str
    slot: int
    permission: str
    action: str
    details: dict[str, Any]
    created_at: float = field(default_factory=time.monotonic)
    decision: str | None = None
    _resolved: threading.Event = field(default_factory=threading.Event, repr=False)


class PermissionCoordinator:
    """Central permission fan-out. Models can request approval but cannot change runtime state."""

    def __init__(self) -> None:
        self._listeners: list[Any] = []
        self._pending: dict[str, PermissionRequest] = {}
        self._subscribers: list[queue.Queue[PermissionRequest | None]] = []
        self._lock = threading.RLock()

    def add_listener(self, listener: Any) -> None:
        if not callable(listener):
            raise TypeError("permission listener must be callable")
        with self._lock:
            self._listeners.append(listener)

    def pending(self) -> list[PermissionRequest]:
        with self._lock:
            return [request for request in self._pending.values() if request.decision is None]

    def resolve(self, request_id: str, approved: bool) -> bool:
        with self._lock:
            request = self._pending.get(request_id)
            if request is None or request.decision is not None:
                return False
            request.decision = "approved" if approved else "denied"
            request._resolved.set()
            return True

    def subscribe(self) -> queue.Queue[PermissionRequest | None]:
        subscriber: queue.Queue[PermissionRequest | None] = queue.Queue()
        with self._lock:
            self._subscribers.append(subscriber)
        return subscriber

    def unsubscribe(self, subscriber: queue.Queue[PermissionRequest | None]) -> None:
        with self._lock:
            if subscriber in self._subscribers:
                self._subscribers.remove(subscriber)

    def close(self) -> None:
        with self._lock:
            pending = tuple(request for request in self._pending.values() if request.decision is None)
            subscribers = tuple(self._subscribers)
            self._subscribers.clear()
            self._pending.clear()
        for request in pending:
            request.decision = "ask_user"
            request._resolved.set()
        for subscriber in subscribers:
            subscriber.put(None)

    def request(
        self,
        model_id: str,
        slot: int,
        permission: str,
        action: str,
        details: dict[str, Any],
        timeout_seconds: float = 10,
    ) -> PermissionRequest:
        if not permission or not action:
            raise BrokerError("permission and action are required")
        if not isinstance(details, dict):
            raise BrokerError("permission details must be an object")
        request = PermissionRequest(uuid.uuid4().hex, model_id, validate_slot(slot), permission, action, details)
        with self._lock:
            self._pending[request.request_id] = request
            listeners = tuple(self._listeners)
            subscribers = tuple(self._subscribers)

        for subscriber in subscribers:
            subscriber.put(request)
        for listener in listeners:
            threading.Thread(
                target=self._notify_listener,
                args=(listener, request),
                daemon=True,
                name="dulche-permission-listener",
            ).start()

        request._resolved.wait(timeout=max(0, min(float(timeout_seconds), 10)))
        with self._lock:
            if request.decision is None:
                request.decision = "ask_user"
                request._resolved.set()
            self._pending.pop(request.request_id, None)
        return request

    def _notify_listener(self, listener: Any, request: PermissionRequest) -> None:
        try:
            decision = listener(request)
        except Exception:
            return
        if isinstance(decision, bool):
            self.resolve(request.request_id, decision)


def permission_payload(request: PermissionRequest) -> dict[str, Any]:
    return {
        "id": request.request_id,
        "model": request.model_id,
        "slot": request.slot,
        "permission": request.permission,
        "action": request.action,
        "details": request.details,
    }


class DulcheModel:
    """A model handle exposes turn-safe context changes and permission requests, never worker internals."""

    def __init__(self, runtime: "DulcheRuntime", model_id: str, slot: int) -> None:
        self._runtime = runtime
        self.model_id = model_id
        self.slot = slot

    def SetContext(self, context: Any) -> int:
        return self._runtime.set_model_context(self.model_id, self.slot, context)

    def RequestPermission(self, permission: str, action: str, details: dict[str, Any] | None = None) -> PermissionRequest:
        return self._runtime.request_model_permission(self.model_id, self.slot, permission, action, details or {})

    def InvokeTool(self, tool_id: str, arguments: dict[str, Any]) -> dict[str, Any]:
        return self._runtime.invoke_model_tool(self.model_id, self.slot, tool_id, arguments)


class DulcheRuntime:
    """One user-level Dulche runtime supervising one worker per explicitly loaded slot."""

    def __init__(self, default_worker: Worker | None = None) -> None:
        self._slots: dict[int, Worker] = {0: default_worker or Worker()}
        self._lock = threading.RLock()
        self.permissions = PermissionCoordinator()
        self._control_server: ThreadingLoopbackServer | None = None
        self._control_thread: threading.Thread | None = None

    def load_model(self, model_id: str, slot: Any = None, context: Any = None) -> DulcheModel:
        if not isinstance(model_id, str) or not model_id.strip():
            raise BrokerError("model must be a non-empty installed model id")
        model_id = model_id.strip()
        manifests = load_manifests()
        if model_id not in manifests:
            raise BrokerError(f"model is not installed: {model_id}")
        requested_context = validate_context_limit(context)
        with self._lock:
            requested_slot = self._next_available_slot() if slot is None or (isinstance(slot, str) and not slot.strip()) else validate_slot(slot)
            worker = self._slots.get(requested_slot)
            if worker is None:
                worker_socket = RUNTIME_DIR / f"llamacpp-worker-{requested_slot}.sock"
                worker_home = RUNTIME_DIR / f"worker-home-{requested_slot}"
                worker = Worker(requested_slot, worker_socket, worker_home, requested_context)
                self._slots[requested_slot] = worker
            # Loading a new model in an occupied slot stops that slot's prior worker first.
            worker.load(manifests[model_id], requested_context)
        return DulcheModel(self, model_id, requested_slot)

    def model(self, model_id: str) -> DulcheModel:
        with self._lock:
            matches = [slot for slot, worker in self._slots.items() if worker.model and worker.model.model_id == model_id]
        if len(matches) != 1:
            raise BrokerError("model id must be loaded in exactly one slot; use the returned slot handle when duplicated")
        return DulcheModel(self, model_id, matches[0])

    def worker_for_slot(self, slot: Any = 0) -> Worker:
        requested_slot = validate_slot(slot)
        with self._lock:
            worker = self._slots.get(requested_slot)
        if worker is None or worker.model is None:
            raise BrokerError(f"slot {requested_slot} has no loaded model")
        return worker

    def set_context(self, slot: Any, context: Any) -> int:
        return self.worker_for_slot(slot).set_context(context)

    def set_model_context(self, model_id: str, slot: Any, context: Any) -> int:
        return self._model_worker(model_id, slot).set_context(context)

    def request_permission(self, slot: Any, permission: str, action: str, details: dict[str, Any]) -> PermissionRequest:
        worker = self.worker_for_slot(slot)
        model = worker.model
        if model is None:
            raise BrokerError(f"slot {worker.slot} has no loaded model")
        return self.permissions.request(model.model_id, worker.slot, permission, action, details)

    def invoke_tool(self, slot: Any, tool_id: str, arguments: dict[str, Any]) -> dict[str, Any]:
        if tool_id not in NATIVE_TOOLS:
            raise BrokerError("tool is not registered by Dulche")
        if not isinstance(arguments, dict):
            raise BrokerError("tool arguments must be an object")
        tool = NATIVE_TOOLS[tool_id]
        request = self.request_permission(slot, tool.permission, tool_id, arguments)
        if request.decision != "approved":
            return {
                "status": "permission_required",
                "decision": request.decision,
                "requestId": request.request_id,
                "message": "No permission UI approved this action. Ask the user directly before trying again.",
            }
        return self._execute_tool(tool, arguments)

    def invoke_model_tool(self, model_id: str, slot: Any, tool_id: str, arguments: dict[str, Any]) -> dict[str, Any]:
        self._model_worker(model_id, slot)
        return self.invoke_tool(slot, tool_id, arguments)

    def request_model_permission(
        self,
        model_id: str,
        slot: Any,
        permission: str,
        action: str,
        details: dict[str, Any],
    ) -> PermissionRequest:
        worker = self._model_worker(model_id, slot)
        return self.permissions.request(model_id, worker.slot, permission, action, details)

    def _execute_tool(self, tool: NativeTool, arguments: dict[str, Any]) -> dict[str, Any]:
        if tool.tool_id == "dulche.run_command":
            command = arguments.get("command")
            if not isinstance(command, list) or not command or any(not isinstance(part, str) or not part for part in command):
                raise BrokerError("run_command requires a non-empty command string array")
            timeout = _bounded_tool_timeout(arguments.get("timeoutSeconds"))
            return _run_tool_process(command, timeout)
        if tool.tool_id == "dulche.run_code":
            source = arguments.get("source")
            language = arguments.get("language", "python")
            if language != "python" or not isinstance(source, str) or not source.strip() or len(source) > 65_536:
                raise BrokerError("run_code currently supports non-empty Python source up to 65536 characters")
            timeout = _bounded_tool_timeout(arguments.get("timeoutSeconds"))
            return _run_tool_process([sys.executable, "-I", "-c", source], timeout)
        raise BrokerError("tool is not executable")

    def slots(self) -> list[dict[str, Any]]:
        with self._lock:
            workers = list(self._slots.values())
        return [
            {
                "slot": worker.slot,
                "model": None if worker.model is None else worker.model.model_id,
                "ready": worker.ready,
                "context": worker.context_limit,
                "pendingContext": worker.pending_context,
            }
            for worker in sorted(workers, key=lambda candidate: candidate.slot)
        ]

    def start(self, port: Any = None) -> int:
        if port is None or (isinstance(port, str) and not port.strip()):
            requested_port = DEFAULT_DULCHE_PORT
        else:
            try:
                requested_port = int(port)
            except (TypeError, ValueError) as exc:
                raise BrokerError("port must be an integer between 0 and 65535") from exc
        if not 0 <= requested_port <= 65535:
            raise BrokerError("port must be between 0 and 65535")
        with self._lock:
            if self._control_server is not None:
                if self._control_server.server_address[1] != requested_port:
                    raise BrokerError("Dulche is already running on a different port")
                return requested_port
            self._control_server = ThreadingLoopbackServer(("127.0.0.1", requested_port), Handler)
            self._control_thread = threading.Thread(
                target=self._control_server.serve_forever,
                kwargs={"poll_interval": 0.25},
                daemon=True,
                name="dulche-control",
            )
            self._control_thread.start()
            return int(self._control_server.server_address[1])

    def stop(self) -> None:
        with self._lock:
            server, thread = self._control_server, self._control_thread
            self._control_server, self._control_thread = None, None
            workers = list(self._slots.values())
        if server is not None:
            server.shutdown()
            server.server_close()
        if thread is not None:
            thread.join(timeout=2)
        self.permissions.close()
        for worker in workers:
            worker.unload()

    def _next_available_slot(self) -> int:
        with self._lock:
            for slot in range(MAX_MODEL_SLOTS):
                worker = self._slots.get(slot)
                if worker is None or worker.model is None:
                    return slot
        raise BrokerError("no Dulche model slots are available")

    def _model_worker(self, model_id: str, slot: Any) -> Worker:
        worker = self.worker_for_slot(slot)
        if worker.model is None or worker.model.model_id != model_id:
            raise BrokerError(f"slot {worker.slot} no longer hosts model {model_id}")
        return worker


def _bounded_tool_timeout(value: Any) -> float:
    if value is None:
        return 30.0
    try:
        timeout = float(value)
    except (TypeError, ValueError) as exc:
        raise BrokerError("timeoutSeconds must be numeric") from exc
    if not 1 <= timeout <= 300:
        raise BrokerError("timeoutSeconds must be between 1 and 300")
    return timeout


def _run_tool_process(command: list[str], timeout: float) -> dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="dulche-tool-") as tool_directory:
        process = subprocess.Popen(
            command,
            shell=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=tool_directory,
            close_fds=True,
            start_new_session=True,
        )
        try:
            stdout, stderr = process.communicate(timeout=timeout)
            timed_out = False
        except subprocess.TimeoutExpired:
            timed_out = True
            _terminate_tool_process(process)
            stdout, stderr = process.communicate()
    output = (stdout + stderr)[:MAX_TOOL_OUTPUT].decode("utf-8", errors="replace")
    if timed_out:
        return {"status": "timed_out", "timeoutSeconds": timeout, "output": output}
    return {"status": "completed", "exitCode": process.returncode, "output": output, "truncated": len(stdout) + len(stderr) > MAX_TOOL_OUTPUT}


def _terminate_tool_process(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], check=False, capture_output=True)
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


WORKER = Worker()
DULCHE_RUNTIME = DulcheRuntime(WORKER)


def dulche_runtime() -> DulcheRuntime:
    """Keeps the default slot bound to test-replaceable legacy Worker state."""
    global DULCHE_RUNTIME
    if DULCHE_RUNTIME._slots.get(0) is not WORKER:
        DULCHE_RUNTIME = DulcheRuntime(WORKER)
    return DULCHE_RUNTIME


class Dulche:
    """Simple embedding API: Start, LoadModel, named handles, and central permission listeners."""

    @classmethod
    def Start(cls, port: Any = None) -> int:
        return dulche_runtime().start(port)

    @classmethod
    def Stop(cls) -> None:
        dulche_runtime().stop()

    @classmethod
    def LoadModel(cls, model: str, slot: Any = None, context: Any = None) -> DulcheModel:
        return dulche_runtime().load_model(model, slot, context)

    @classmethod
    def Model(cls, model_id: str) -> DulcheModel:
        return dulche_runtime().model(model_id)

    @classmethod
    def OnPermissionRequested(cls, listener: Any) -> None:
        dulche_runtime().permissions.add_listener(listener)

ACTIVE_REQUESTS: dict[str, ActiveRequest] = {}
ACTIVE_LOCK = threading.Lock()


def runtime_diagnostics() -> dict[str, Any]:
    runtime = dulche_runtime()
    default_worker = runtime.worker_for_slot(0) if WORKER.model is not None else WORKER
    with ACTIVE_LOCK:
        active_request_count = len(ACTIVE_REQUESTS)
    return {
        "topology": "single-broker-owned-slot-workers",
        "perAppServers": False,
        "slotCount": len(runtime.slots()),
        "slots": runtime.slots(),
        "worker": {
            "ready": default_worker.ready,
            "loadedModel": None if default_worker.model is None else default_worker.model.model_id,
            "activeRequestCount": active_request_count,
        },
        "cancellation": {
            "primary": "upstream-resumable-stream-delete",
            "transportFallback": "before-worker-response-or-control-failure",
            "workerCompletionConfirmed": False,
            "evidence": "pinned-upstream-source-and-model-free-integration",
        },
    }


class ThreadingUnixServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    address_family = socket.AF_UNIX if HAS_UNIX_SOCKETS else socket.AF_INET
    daemon_threads = True

    def __init__(self, server_address: str, request_handler_class: type[http.server.BaseHTTPRequestHandler]) -> None:
        if not HAS_UNIX_SOCKETS:
            raise BrokerError("Unix-domain sockets are unavailable on this platform")
        super().__init__(server_address, request_handler_class)

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


class ThreadingLoopbackServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True
    allow_reuse_address = True


class Handler(http.server.BaseHTTPRequestHandler):
    server_version = "HavenInference/0"

    def log_message(self, fmt: str, *args: Any) -> None:
        print(f"dulche: local-runtime {fmt % args}")

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
                runtime = runtime_diagnostics()
                self._send_json(200, {
                    "status": "ok",
                    "provider": PROVIDER_ID,
                    "workerReady": runtime["worker"]["ready"],
                    "runtime": runtime,
                })
                return
            if self.path == "/v1/provider":
                runtime = dulche_runtime()
                self._send_json(200, {
                    "id": PROVIDER_ID,
                    "displayName": "llama.cpp",
                    "isLocal": True,
                    "modelKeyPrefix": f"{PROVIDER_ID}:",
                    "legacyUnqualifiedProvider": "ollama",
                    "transport": "http-over-unix",
                    "contextLimit": CONTEXT_LIMIT,
                    "defaultPort": DEFAULT_DULCHE_PORT,
                    "maxModelSlots": MAX_MODEL_SLOTS,
                    "modelStoreWritable": False,
                    "runtime": {
                        "topology": "single-broker-owned-slot-workers",
                        "perAppServers": False,
                        "slots": runtime.slots(),
                        "cancellation": {
                            "primary": "upstream-resumable-stream-delete",
                            "transportFallback": "before-worker-response-or-control-failure",
                            "workerCompletionConfirmed": False,
                        },
                    },
                })
                return
            if self.path == "/v1/models":
                manifests = load_manifests()
                slots = dulche_runtime().slots()
                loaded_slots = {
                    entry["model"]: entry["slot"]
                    for entry in slots
                    if entry["model"] is not None
                }
                self._send_json(200, {"models": [
                    {
                        "id": item.model_id,
                        "key": item.key,
                        "displayName": item.display_name,
                        "provider": PROVIDER_ID,
                        "installed": item.blob_path.is_file(),
                        "loaded": item.model_id in loaded_slots,
                        "loadedSlots": [entry["slot"] for entry in slots if entry["model"] == item.model_id],
                        "size": item.size,
                        "license": item.license_id,
                        "source": item.source,
                        "ggufVersions": item.gguf_versions,
                    }
                    for item in manifests.values()
                ]})
                return
            if self.path == "/v1/slots":
                self._send_json(200, {"slots": dulche_runtime().slots()})
                return
            if self.path == "/v1/tools":
                self._send_json(200, {"tools": [
                    {
                        "id": tool.tool_id,
                        "displayName": tool.display_name,
                        "permission": tool.permission,
                        "description": tool.description,
                    }
                    for tool in NATIVE_TOOLS.values()
                ]})
                return
            if self.path == "/v1/permissions/events":
                self._stream_permission_events()
                return
            if self.path == "/v1/permissions/requests":
                self._send_json(200, {"requests": [permission_payload(request) for request in dulche_runtime().permissions.pending()]})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def _stream_permission_events(self) -> None:
        coordinator = dulche_runtime().permissions
        subscriber = coordinator.subscribe()
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        try:
            while True:
                try:
                    request = subscriber.get(timeout=5)
                    if request is None:
                        return
                    encoded = json.dumps(permission_payload(request), separators=(",", ":"))
                    self.wfile.write(f"event: permission-request\ndata: {encoded}\n\n".encode("utf-8"))
                except queue.Empty:
                    self.wfile.write(b": keep-alive\n\n")
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass
        finally:
            coordinator.unsubscribe(subscriber)

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
                payload = self._read_json()
                runtime = dulche_runtime()
                if segments[3] == "load":
                    model = runtime.load_model(model_id, payload.get("slot"), payload.get("context"))
                    worker = runtime.worker_for_slot(model.slot)
                    self._send_json(200, {
                        "status": "ready",
                        "model": model_id,
                        "slot": model.slot,
                        "context": worker.context_limit,
                        "pendingContext": worker.pending_context,
                        "key": provider_key(model_id),
                    })
                else:
                    slot = validate_slot(payload.get("slot", 0))
                    worker = runtime.worker_for_slot(slot)
                    if worker.model is None or worker.model.model_id != model_id:
                        self._send_json(409, {"error": "model_not_loaded_in_slot", "slot": slot})
                        return
                    worker.unload()
                    self._send_json(200, {"status": "unloaded", "model": model_id, "slot": slot, "key": provider_key(model_id)})
                return
            if len(segments) == 4 and segments[:2] == ["v1", "slots"] and segments[3] == "context":
                slot = validate_slot(segments[2])
                payload = self._read_json()
                context = dulche_runtime().set_context(slot, payload.get("context"))
                self._send_json(202, {"status": "scheduled", "slot": slot, "context": context})
                return
            if len(segments) == 5 and segments[:2] == ["v1", "slots"] and segments[3] == "tools":
                slot = validate_slot(segments[2])
                payload = self._read_json()
                result = dulche_runtime().invoke_tool(slot, urllib.parse.unquote(segments[4]), payload)
                self._send_json(200, result)
                return
            if len(segments) == 4 and segments[:2] == ["v1", "slots"] and segments[3] == "permissions":
                slot = validate_slot(segments[2])
                payload = self._read_json()
                request = dulche_runtime().request_permission(
                    slot,
                    str(payload.get("permission", "")),
                    str(payload.get("action", "")),
                    payload.get("details", {}),
                )
                self._send_json(200, {"requestId": request.request_id, "decision": request.decision})
                return
            if len(segments) == 4 and segments[:2] == ["v1", "permissions"] and segments[3] == "resolve":
                payload = self._read_json()
                approved = payload.get("approved")
                if not isinstance(approved, bool):
                    raise BrokerError("approved must be boolean")
                resolved = dulche_runtime().permissions.resolve(urllib.parse.unquote(segments[2]), approved)
                self._send_json(200 if resolved else 404, {"resolved": resolved})
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
                    cancellation = active.cancel()
                    self._send_json(202, {"status": "cancelling", "requestId": request_id, "cancellation": cancellation})
                return
            self._send_json(404, {"error": "not_found"})
        except (BrokerError, OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
            self._send_json(400, {"error": "invalid_request", "detail": str(exc)})

    def _proxy_chat(self) -> None:
        payload = self._read_json()
        slot = validate_slot(payload.pop("slot", 0))
        request_id = validate_request_id(str(payload.pop("request_id", "")))
        worker = dulche_runtime().worker_for_slot(slot)
        worker.begin_turn()
        conn = UnixHTTPConnection(worker.worker_socket)
        active = ActiveRequest(conn, request_id, worker.worker_socket)
        headers_sent = False
        try:
            if not worker.ready or worker.model is None:
                raise BrokerError(f"slot {slot} has no ready llama.cpp model")
            payload["model"] = worker.model.model_id
            payload["stream"] = True
            conn.connect()
            with ACTIVE_LOCK:
                if request_id in ACTIVE_REQUESTS:
                    raise BrokerError("request_id is already active")
                ACTIVE_REQUESTS[request_id] = active
            if active.cancelled.is_set():
                self._send_json(409, {"error": "request_cancelled", "requestId": request_id})
                return
            encoded = json.dumps(payload, separators=(",", ":")).encode("utf-8")
            conn.request("POST", "/v1/chat/completions", body=encoded, headers={
                "Content-Type": "application/json",
                "X-Conversation-Id": request_id,
            })
            if active.cancelled.is_set():
                self._send_json(409, {"error": "request_cancelled", "requestId": request_id})
                return
            response = conn.getresponse()
            active.response_started.set()
            self.send_response(response.status)
            content_type = response.getheader("Content-Type", "text/event-stream")
            self.send_header("Content-Type", content_type)
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Haven-Request-Id", request_id)
            self.end_headers()
            headers_sent = True
            while not active.cancelled.is_set():
                if conn.sock is None:
                    raise BrokerError("worker transport closed while streaming")
                readable, _, _ = select.select((conn.sock,), (), (), 0.25)
                if not readable:
                    continue
                chunk = response.read1(4096)
                if not chunk:
                    break
                self.wfile.write(chunk)
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, OSError, ValueError, http.client.HTTPException) as exc:
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
            worker.end_turn()
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
