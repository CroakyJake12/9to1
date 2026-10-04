#!/usr/bin/env python3
"""Run a real-model CPU inference proof through the Haven llama.cpp boundary.

The workflow owns network retrieval and supplies an already-local, hash-pinned
GGUF. This script deliberately performs no network I/O. It validates the file,
imports it through the production model manager, starts the production broker,
loads the real pinned llama-server worker over AF_UNIX, performs one short
streamed chat completion, unloads the worker, and writes prompt-free evidence.

By default the proof uses the repository source tree. Installed-package mode is
explicit and verifies dpkg ownership before exercising the installed modelctl,
broker, and llama-server paths.
"""
from __future__ import annotations

import hashlib
import http.client
import json
import os
import pathlib
import platform
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import time
from typing import Any

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
LOCK_PATH = RUNTIME / "test-model.lock.json"
MODEL_ID = "ci-smollm2-135m"
REQUEST_ID = "real-ci-proof-1"
PACKAGE_NAME = "haven-llamacpp-runtime"
INSTALLED_MODELCTL = pathlib.Path("/usr/bin/haven-modelctl")
INSTALLED_BROKER = pathlib.Path("/usr/lib/haven/inference/broker.py")
INSTALLED_LLAMA_SERVER = pathlib.Path("/usr/lib/haven/llama.cpp/llama-server")
INSTALLED_UNIT = pathlib.Path("/usr/lib/systemd/user/haven-inference-broker.service")


class ProofError(RuntimeError):
    pass


class UnixHTTPConnection(http.client.HTTPConnection):
    def __init__(self, unix_path: pathlib.Path, timeout: float = 120.0) -> None:
        super().__init__("localhost", timeout=timeout)
        self.unix_path = str(unix_path)

    def connect(self) -> None:
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.settimeout(self.timeout)
        sock.connect(self.unix_path)
        self.sock = sock


def _load_lock() -> dict[str, Any]:
    value = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ProofError("test-model lock must be a JSON object")
    return value


def _require_regular_file(path: pathlib.Path, label: str, *, executable: bool = False) -> pathlib.Path:
    try:
        info = path.lstat()
    except OSError as exc:
        raise ProofError(f"cannot stat {label}: {exc}") from exc
    if not stat.S_ISREG(info.st_mode) or path.is_symlink():
        raise ProofError(f"{label} must be a regular non-symlink file: {path}")
    if executable and not os.access(path, os.X_OK):
        raise ProofError(f"{label} is not executable: {path}")
    return path


def _hash_regular_file(path: pathlib.Path) -> tuple[str, int]:
    _require_regular_file(path, "real-runtime proof model")
    digest = hashlib.sha256()
    total = 0
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
            total += len(chunk)
    return digest.hexdigest(), total


def _installed_package_state(
    modelctl_path: pathlib.Path,
    broker_path: pathlib.Path,
    llama_server: pathlib.Path,
) -> str:
    expected = {
        str(INSTALLED_MODELCTL),
        str(INSTALLED_BROKER),
        str(INSTALLED_LLAMA_SERVER),
        str(INSTALLED_UNIT),
    }
    supplied = {str(modelctl_path), str(broker_path), str(llama_server)}
    if supplied != {str(INSTALLED_MODELCTL), str(INSTALLED_BROKER), str(INSTALLED_LLAMA_SERVER)}:
        raise ProofError("installed-package proof must use the canonical installed CakeOS runtime paths")

    state = subprocess.run(
        ["dpkg-query", "-W", "-f=${Status}\t${Version}", PACKAGE_NAME],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=10,
    )
    if state.returncode != 0:
        raise ProofError(f"{PACKAGE_NAME} is not installed: {state.stderr.strip()}")
    try:
        status, version = state.stdout.strip().split("\t", 1)
    except ValueError as exc:
        raise ProofError("dpkg returned an unexpected package-state record") from exc
    if status != "install ok installed" or not version:
        raise ProofError(f"unexpected dpkg state for {PACKAGE_NAME}: {state.stdout.strip()}")

    listing = subprocess.run(
        ["dpkg-query", "-L", PACKAGE_NAME],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=10,
    )
    if listing.returncode != 0:
        raise ProofError(f"cannot list installed files for {PACKAGE_NAME}: {listing.stderr.strip()}")
    owned = {line.strip() for line in listing.stdout.splitlines() if line.strip()}
    missing = sorted(expected - owned)
    if missing:
        raise ProofError(f"installed package does not own required runtime paths: {', '.join(missing)}")
    return version


def _request(
    socket_path: pathlib.Path,
    method: str,
    path: str,
    value: dict[str, Any] | None = None,
    *,
    timeout: float = 120.0,
) -> tuple[int, dict[str, str], bytes]:
    connection = UnixHTTPConnection(socket_path, timeout=timeout)
    body = None if value is None else json.dumps(value, separators=(",", ":")).encode("utf-8")
    headers = {} if body is None else {"Content-Type": "application/json"}
    try:
        connection.request(method, path, body=body, headers=headers)
        response = connection.getresponse()
        response_headers = {key.lower(): val for key, val in response.getheaders()}
        return response.status, response_headers, response.read()
    finally:
        connection.close()


def _wait_for_broker(socket_path: pathlib.Path, process: subprocess.Popen[bytes], timeout: float = 10.0) -> None:
    deadline = time.monotonic() + timeout
    last_error: BaseException | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise ProofError(f"broker exited before becoming healthy with code {process.returncode}")
        if socket_path.exists():
            try:
                status, _, body = _request(socket_path, "GET", "/health", timeout=1.0)
                if status == 200 and json.loads(body).get("status") == "ok":
                    return
            except (OSError, http.client.HTTPException, json.JSONDecodeError) as exc:
                last_error = exc
        time.sleep(0.05)
    detail = "" if last_error is None else f": {last_error}"
    raise ProofError(f"broker did not become healthy before deadline{detail}")


def _parse_stream(body: bytes) -> tuple[bytes, bool, int]:
    generated: list[str] = []
    done = False
    json_events = 0
    for raw_line in body.splitlines():
        line = raw_line.strip()
        if not line.startswith(b"data:"):
            continue
        payload = line[5:].strip()
        if payload == b"[DONE]":
            done = True
            continue
        if not payload:
            continue
        try:
            event = json.loads(payload)
        except json.JSONDecodeError as exc:
            raise ProofError(f"invalid JSON event in llama.cpp stream: {exc}") from exc
        json_events += 1
        choices = event.get("choices", []) if isinstance(event, dict) else []
        if not isinstance(choices, list):
            continue
        for choice in choices:
            if not isinstance(choice, dict):
                continue
            delta = choice.get("delta")
            if not isinstance(delta, dict):
                continue
            content = delta.get("content")
            if isinstance(content, str) and content:
                generated.append(content)
    return "".join(generated).encode("utf-8"), done, json_events


def _stop_broker(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is not None:
        return
    try:
        process.send_signal(signal.SIGINT)
        process.wait(timeout=8)
        return
    except subprocess.TimeoutExpired:
        pass
    process.terminate()
    try:
        process.wait(timeout=3)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=3)


def run() -> dict[str, Any]:
    lock = _load_lock()
    model_raw = os.environ.get("HAVEN_REAL_MODEL_PATH", "").strip()
    server_raw = os.environ.get("HAVEN_LLAMA_SERVER", "").strip()
    output_raw = os.environ.get("HAVEN_RUNTIME_PROOF_OUTPUT", "").strip()
    proof_surface = os.environ.get("HAVEN_PROOF_SURFACE", "source-tree").strip()
    modelctl_raw = os.environ.get("HAVEN_MODELCTL_EXECUTABLE", "").strip()
    broker_raw = os.environ.get("HAVEN_BROKER_SCRIPT", "").strip()
    if not model_raw or not server_raw or not output_raw:
        raise ProofError("HAVEN_REAL_MODEL_PATH, HAVEN_LLAMA_SERVER, and HAVEN_RUNTIME_PROOF_OUTPUT are required")
    if proof_surface not in {"source-tree", "installed-package"}:
        raise ProofError("HAVEN_PROOF_SURFACE must be 'source-tree' or 'installed-package'")
    if proof_surface == "installed-package" and (not modelctl_raw or not broker_raw):
        raise ProofError("installed-package proof requires HAVEN_MODELCTL_EXECUTABLE and HAVEN_BROKER_SCRIPT")

    model_path = pathlib.Path(model_raw)
    llama_server = _require_regular_file(pathlib.Path(server_raw), "llama-server", executable=True)
    output_path = pathlib.Path(output_raw)
    modelctl_path = pathlib.Path(modelctl_raw) if modelctl_raw else RUNTIME / "modelctl.py"
    broker_path = pathlib.Path(broker_raw) if broker_raw else RUNTIME / "broker.py"
    _require_regular_file(modelctl_path, "modelctl", executable=bool(modelctl_raw))
    _require_regular_file(broker_path, "broker")

    package_version: str | None = None
    if proof_surface == "installed-package":
        package_version = _installed_package_state(modelctl_path, broker_path, llama_server)
        modelctl_command = [str(modelctl_path)]
    else:
        modelctl_command = [sys.executable, str(modelctl_path)]

    digest, size = _hash_regular_file(model_path)
    if digest != str(lock["sha256"]):
        raise ProofError(f"model SHA-256 mismatch: expected {lock['sha256']}, got {digest}")
    if size != int(lock["size"]):
        raise ProofError(f"model size mismatch: expected {lock['size']}, got {size}")

    with tempfile.TemporaryDirectory(prefix="haven-real-runtime-") as temp_name:
        root = pathlib.Path(temp_name)
        xdg_runtime = root / "runtime"
        xdg_data = root / "data"
        runtime_dir = xdg_runtime / "haven"
        model_root = xdg_data / "haven" / "models"
        broker_socket = runtime_dir / "inference.sock"
        worker_socket = runtime_dir / "llamacpp-worker.sock"
        log_path = root / "broker.log"
        env = os.environ.copy()
        env.update({
            "XDG_RUNTIME_DIR": str(xdg_runtime),
            "XDG_DATA_HOME": str(xdg_data),
            "HAVEN_INFERENCE_SOCKET": str(broker_socket),
            "HAVEN_LLAMA_WORKER_SOCKET": str(worker_socket),
            "HAVEN_LLAMA_SERVER": str(llama_server),
            "HAVEN_LLAMA_START_TIMEOUT": "60",
            "HAVEN_LLAMA_CONTEXT_LIMIT": "512",
        })

        import_result = subprocess.run(
            [
                *modelctl_command,
                "--model-root", str(model_root),
                "--runtime-dir", str(runtime_dir),
                "import", str(model_path),
                "--id", MODEL_ID,
                "--name", "CI SmolLM2 135M Instruct Q4_K_M",
                "--license", str(lock["license"]),
                "--origin", str(lock["downloadUrl"]),
                "--capability", "chat",
                "--redistribution", "ci-test-only-no-redistribution",
            ],
            env=env,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=60,
        )
        if import_result.returncode != 0:
            raise ProofError(f"modelctl import failed: {import_result.stderr.strip()}")
        imported = json.loads(import_result.stdout)
        manifest = imported.get("model", {})
        if manifest.get("sha256") != digest or int(manifest.get("size", -1)) != size:
            raise ProofError("modelctl manifest does not match independently verified model bytes")

        broker_process: subprocess.Popen[bytes] | None = None
        load_seconds = 0.0
        completion_seconds = 0.0
        generated = b""
        stream_done = False
        stream_events = 0
        unloaded = False
        try:
            with log_path.open("wb") as broker_log:
                broker_process = subprocess.Popen(
                    [sys.executable, str(broker_path)],
                    stdin=subprocess.DEVNULL,
                    stdout=broker_log,
                    stderr=subprocess.STDOUT,
                    env=env,
                    close_fds=True,
                    start_new_session=True,
                )
                _wait_for_broker(broker_socket, broker_process)

                started = time.monotonic()
                status, _, body = _request(broker_socket, "POST", f"/v1/models/{MODEL_ID}/load", timeout=90.0)
                load_seconds = time.monotonic() - started
                if status != 200:
                    raise ProofError(f"real model load failed with HTTP {status}: {body[:2048]!r}")
                load_response = json.loads(body)
                if load_response.get("key") != str(lock["providerKey"]):
                    raise ProofError("broker returned an unexpected provider-qualified model key")
                if not worker_socket.exists() or not stat.S_ISSOCK(worker_socket.lstat().st_mode):
                    raise ProofError("real llama-server worker did not expose its Unix socket")
                if stat.S_IMODE(worker_socket.stat().st_mode) != 0o600:
                    raise ProofError("real llama-server worker socket is not mode 0600")

                status, _, body = _request(broker_socket, "GET", "/v1/models")
                if status != 200:
                    raise ProofError(f"model inventory failed with HTTP {status}")
                models = json.loads(body).get("models", [])
                current = next((item for item in models if item.get("id") == MODEL_ID), None)
                if not current or not current.get("loaded") or current.get("key") != str(lock["providerKey"]):
                    raise ProofError("real model is not reported as loaded through the provider contract")

                request = {
                    "request_id": REQUEST_ID,
                    "messages": [{"role": "user", "content": "Reply with one word: ready"}],
                    "temperature": 0,
                    "seed": 1,
                    "max_tokens": 8,
                }
                started = time.monotonic()
                status, headers, stream = _request(
                    broker_socket,
                    "POST",
                    "/v1/chat/completions",
                    request,
                    timeout=120.0,
                )
                completion_seconds = time.monotonic() - started
                if status != 200:
                    raise ProofError(f"real streamed completion failed with HTTP {status}: {stream[:2048]!r}")
                if headers.get("x-haven-request-id") != REQUEST_ID:
                    raise ProofError("broker did not preserve the validated request id header")
                generated, stream_done, stream_events = _parse_stream(stream)
                if not generated:
                    raise ProofError("real llama.cpp completion produced no streamed assistant content")
                if not stream_done:
                    raise ProofError("real llama.cpp completion did not emit the terminal [DONE] event")
                if stream_events < 1:
                    raise ProofError("real llama.cpp completion produced no JSON stream events")

                status, _, body = _request(broker_socket, "POST", f"/v1/models/{MODEL_ID}/unload")
                if status != 200:
                    raise ProofError(f"real model unload failed with HTTP {status}: {body[:2048]!r}")
                unloaded = True
                if worker_socket.exists():
                    deadline = time.monotonic() + 3.0
                    while worker_socket.exists() and time.monotonic() < deadline:
                        time.sleep(0.05)
                    if worker_socket.exists():
                        raise ProofError("worker Unix socket remained after unload")
        except BaseException:
            if broker_process is not None and broker_process.poll() is None and broker_socket.exists():
                try:
                    _request(broker_socket, "POST", f"/v1/models/{MODEL_ID}/unload", timeout=5.0)
                except BaseException:
                    pass
            raise
        finally:
            if broker_process is not None:
                _stop_broker(broker_process)

        if not unloaded:
            raise ProofError("real model did not complete the unload path")

        runtime_surface: dict[str, Any] = {
            "kind": proof_surface,
            "systemdManaged": False,
            "installedPackagePathsVerified": proof_surface == "installed-package",
        }
        if package_version is not None:
            runtime_surface.update({
                "package": PACKAGE_NAME,
                "packageVersion": package_version,
                "pathsOwnedByPackage": True,
            })

        evidence = {
            "schemaVersion": 1,
            "proofType": "installed-package-real-model-cpu-inference-ci" if proof_surface == "installed-package" else "real-model-cpu-inference-ci",
            "provider": "llamacpp",
            "providerKey": str(lock["providerKey"]),
            "runtimeSurface": runtime_surface,
            "upstream": {
                "llamaCppTag": "v0.4.0",
                "llamaCppCommit": "5266f24da75dc449bd56cbed7addb9c8e4a6a73e",
            },
            "model": {
                "repository": str(lock["repository"]),
                "revision": str(lock["revision"]),
                "filename": str(lock["filename"]),
                "sha256": digest,
                "size": size,
                "license": str(lock["license"]),
            },
            "checks": {
                "modelBytesVerified": True,
                "modelctlImportVerified": True,
                "brokerHealthVerified": True,
                "workerUnixSocketVerified": True,
                "workerSocketMode0600": True,
                "providerInventoryVerified": True,
                "streamedChatCompletionVerified": True,
                "streamDoneVerified": stream_done,
                "unloadVerified": unloaded,
            },
            "completion": {
                "utf8Bytes": len(generated),
                "sha256": hashlib.sha256(generated).hexdigest(),
                "jsonEvents": stream_events,
            },
            "timingObservations": {
                "loadSeconds": round(load_seconds, 6),
                "completionSeconds": round(completion_seconds, 6),
                "classification": "single-run-observation-not-a-benchmark",
            },
            "environment": {
                "platform": platform.platform(),
                "python": platform.python_version(),
            },
            "claimBoundaries": {
                "approvedVmRuntimeProven": False,
                "gpuRuntimeProven": False,
                "productionModelBenchmarked": False,
                "modelPackagedOrUploaded": False,
                "systemdManagedRuntimeProven": False,
            },
        }
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(json.dumps(evidence, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        return evidence


def main() -> int:
    try:
        evidence = run()
    except BaseException as exc:
        print(f"real-runtime-proof: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 1
    print(json.dumps(evidence, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
