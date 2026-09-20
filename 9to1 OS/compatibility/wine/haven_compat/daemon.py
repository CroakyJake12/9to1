from __future__ import annotations

import json
import os
import socket
import stat
import struct
from pathlib import Path
from typing import Any

from .broker import CompatibilityBroker, CompatibilityError
from .manifest import AppManifest, ManifestError


MAX_MESSAGE_BYTES = 1024 * 1024
_HEADER = struct.Struct("!I")


class DaemonError(RuntimeError):
    pass


class RequestError(ValueError):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code
        self.message = message


def _effective_uid() -> int:
    getter = getattr(os, "geteuid", None)
    if getter is None:
        raise DaemonError("compatibility daemon requires Linux/POSIX effective-UID support")
    return int(getter())


def default_socket_path() -> Path:
    runtime_dir = os.environ.get("XDG_RUNTIME_DIR")
    if not runtime_dir:
        raise DaemonError("XDG_RUNTIME_DIR is required for the compatibility daemon")
    return Path(runtime_dir) / "haven" / "compat.sock"


def dispatch_request(broker: CompatibilityBroker, request: dict[str, Any]) -> Any:
    method = request.get("method")
    params = request.get("params", {})
    if set(request) - {"method", "params"}:
        raise RequestError("invalid_request", "unexpected request fields")
    if not isinstance(method, str) or not method:
        raise RequestError("invalid_request", "method must be a non-empty string")
    if not isinstance(params, dict):
        raise RequestError("invalid_request", "params must be an object")

    if method == "capabilities":
        _require_no_params(params)
        return broker.capabilities()
    if method == "health":
        _require_no_params(params)
        return broker.health()
    if method == "listApps":
        _require_no_params(params)
        return broker.list_apps()
    if method == "registerApp":
        if set(params) != {"manifest"}:
            raise RequestError("invalid_params", "registerApp accepts only params.manifest")
        raw_manifest = params.get("manifest")
        if not isinstance(raw_manifest, dict):
            raise RequestError("invalid_params", "registerApp requires params.manifest object")
        try:
            manifest = AppManifest.from_dict(raw_manifest)
        except ManifestError as exc:
            raise RequestError("invalid_manifest", str(exc)) from exc
        return broker.register_app(manifest)
    if method == "unregisterApp":
        app_id = _require_app_id(params, allowed_extra={"deleteState"})
        delete_state = params.get("deleteState", False)
        if not isinstance(delete_state, bool):
            raise RequestError("invalid_params", "deleteState must be boolean")
        return broker.unregister_app(app_id, delete_state=delete_state)
    if method == "launch":
        return broker.launch_registered(_require_app_id(params)).as_dict()
    if method == "status":
        return broker.status_registered(_require_app_id(params)).as_dict()
    if method == "stop":
        return broker.stop_registered(_require_app_id(params)).as_dict()
    if method == "logs":
        app_id = _require_app_id(params, allowed_extra={"lines"})
        lines = params.get("lines", 200)
        if isinstance(lines, bool) or not isinstance(lines, int) or not 1 <= lines <= 1000:
            raise RequestError("invalid_params", "lines must be an integer between 1 and 1000")
        return {"appId": app_id, "text": broker.logs_registered(app_id, lines)}
    if method == "reset":
        app_id = _require_app_id(params)
        broker.reset_registered(app_id)
        return {"appId": app_id, "reset": True}

    raise RequestError("method_not_found", f"unsupported compatibility method: {method}")


def serve_connection(connection: socket.socket, broker: CompatibilityBroker) -> None:
    connection.settimeout(10)
    _verify_peer(connection)
    try:
        request = _receive_json(connection)
        result = dispatch_request(broker, request)
        response = {"ok": True, "result": result}
    except RequestError as exc:
        response = {"ok": False, "error": {"code": exc.code, "message": exc.message}}
    except CompatibilityError as exc:
        response = {"ok": False, "error": {"code": "backend_error", "message": str(exc)}}
    except Exception:
        response = {
            "ok": False,
            "error": {
                "code": "internal_error",
                "message": "compatibility broker request failed",
            },
        }
    _send_json(connection, response)


def serve_forever(socket_path: Path | None = None, broker: CompatibilityBroker | None = None) -> None:
    uid = _effective_uid()
    if uid == 0:
        raise DaemonError("compatibility daemon must run as an unprivileged user")
    if not hasattr(socket, "SO_PEERCRED"):
        raise DaemonError("same-user peer verification requires Linux SO_PEERCRED")

    path = socket_path or default_socket_path()
    _prepare_socket_parent(path.parent)
    _remove_stale_socket(path)
    broker = broker or CompatibilityBroker()

    old_umask = os.umask(0o077)
    server: socket.socket | None = None
    try:
        server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        server.bind(str(path))
        os.chmod(path, 0o600)
        server.listen(16)
        while True:
            connection, _ = server.accept()
            with connection:
                try:
                    serve_connection(connection, broker)
                except (DaemonError, OSError, ValueError):
                    continue
    finally:
        if server is not None:
            server.close()
        os.umask(old_umask)
        _remove_owned_socket(path)


def _require_no_params(params: dict[str, Any]) -> None:
    if params:
        raise RequestError("invalid_params", "method does not accept parameters")


def _require_app_id(params: dict[str, Any], allowed_extra: set[str] | None = None) -> str:
    allowed = {"id"} | (allowed_extra or set())
    if set(params) - allowed:
        raise RequestError("invalid_params", "unexpected parameters")
    app_id = params.get("id")
    if not isinstance(app_id, str) or not app_id:
        raise RequestError("invalid_params", "params.id must be a non-empty string")
    return app_id


def _verify_peer(connection: socket.socket) -> None:
    if not hasattr(socket, "SO_PEERCRED"):
        raise DaemonError("SO_PEERCRED is unavailable")
    credentials = connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, struct.calcsize("3i"))
    _pid, uid, _gid = struct.unpack("3i", credentials)
    if uid != _effective_uid():
        raise DaemonError("compatibility daemon rejected a different-user client")


def _receive_json(connection: socket.socket) -> dict[str, Any]:
    header = _read_exact(connection, _HEADER.size)
    (length,) = _HEADER.unpack(header)
    if length < 2 or length > MAX_MESSAGE_BYTES:
        raise RequestError("invalid_request", "request size is outside the allowed bounds")
    payload = _read_exact(connection, length)
    try:
        value = json.loads(payload.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise RequestError("invalid_request", "request must be valid UTF-8 JSON") from exc
    if not isinstance(value, dict):
        raise RequestError("invalid_request", "request root must be an object")
    return value


def _send_json(connection: socket.socket, value: dict[str, Any]) -> None:
    payload = json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    if len(payload) > MAX_MESSAGE_BYTES:
        payload = json.dumps({
            "ok": False,
            "error": {
                "code": "response_too_large",
                "message": "compatibility broker response exceeded the protocol limit",
            },
        }, separators=(",", ":")).encode("utf-8")
    connection.sendall(_HEADER.pack(len(payload)) + payload)


def _read_exact(connection: socket.socket, size: int) -> bytes:
    chunks: list[bytes] = []
    remaining = size
    while remaining:
        chunk = connection.recv(remaining)
        if not chunk:
            raise RequestError("invalid_request", "client disconnected before request was complete")
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def _prepare_socket_parent(parent: Path) -> None:
    uid = _effective_uid()
    runtime_dir_value = os.environ.get("XDG_RUNTIME_DIR")
    if not runtime_dir_value:
        raise DaemonError("XDG_RUNTIME_DIR is required")
    runtime_dir = Path(runtime_dir_value)
    try:
        runtime_stat = runtime_dir.stat()
        runtime_resolved = runtime_dir.resolve(strict=True)
    except OSError as exc:
        raise DaemonError("XDG_RUNTIME_DIR is unavailable") from exc
    if runtime_dir.is_symlink() or not runtime_dir.is_dir():
        raise DaemonError("XDG_RUNTIME_DIR must be a real directory")
    if runtime_stat.st_uid != uid:
        raise DaemonError("XDG_RUNTIME_DIR must be owned by the current user")

    parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if parent.is_symlink() or not parent.is_dir():
        raise DaemonError("compatibility socket parent must be a real directory")
    try:
        parent_resolved = parent.resolve(strict=True)
    except OSError as exc:
        raise DaemonError("compatibility socket parent is unavailable") from exc
    if runtime_resolved != parent_resolved and runtime_resolved not in parent_resolved.parents:
        raise DaemonError("compatibility socket must remain beneath XDG_RUNTIME_DIR")
    parent_stat = parent.stat()
    if parent_stat.st_uid != uid:
        raise DaemonError("compatibility socket parent must be owned by the current user")
    os.chmod(parent, 0o700)


def _remove_stale_socket(path: Path) -> None:
    uid = _effective_uid()
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return
    if metadata.st_uid != uid or not stat.S_ISSOCK(metadata.st_mode):
        raise DaemonError("refusing to replace a non-owned or non-socket compatibility path")
    path.unlink()


def _remove_owned_socket(path: Path) -> None:
    uid = _effective_uid()
    try:
        metadata = path.lstat()
    except FileNotFoundError:
        return
    if metadata.st_uid == uid and stat.S_ISSOCK(metadata.st_mode):
        path.unlink()
