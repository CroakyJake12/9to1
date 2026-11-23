#!/usr/bin/env python3
"""Dulche v1 local HTTP API. Only this user's Unix socket accepts management calls."""

import argparse
import contextlib
import fcntl
import json
import logging
import os
from pathlib import Path
import signal
import socket
import socketserver
import stat
import struct
import threading
import time
from http.server import BaseHTTPRequestHandler
from urllib.parse import parse_qs, unquote, urlsplit

import benchmarks
from controller import Controller
from engines import EngineManager
from external import External
from jobs import TERMINAL
from model_catalog import ModelCatalog
from state import Fault

LIMIT = 8 * 1024 * 1024


class UnixServer(socketserver.ThreadingMixIn, socketserver.UnixStreamServer):
    daemon_threads = True
    request_queue_size = 32

    def verify_request(self, request, address):
        _, uid, _ = struct.unpack(
            "3i", request.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
        )
        return uid == os.getuid()


class Handler(BaseHTTPRequestHandler):
    # Close-delimited SSE works across HTTP/1.0 and HTTP/1.1 clients.
    protocol_version = "HTTP/1.0"
    server_version = "Dulche/1"

    def log_message(self, fmt, *args):
        pass

    def send_json(self, status, data, extra=None):
        payload = json.dumps(data, ensure_ascii=False, allow_nan=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(payload)

    def _body(self):
        transfer = self.headers.get("Transfer-Encoding", "").lower()
        length = self.headers.get("Content-Length")
        if transfer:
            if transfer != "chunked" or length is not None:
                raise Fault(400, "Invalid transfer framing")
            parts = []
            total = 0
            while True:
                line = self.rfile.readline(96)
                if not line.endswith(b"\r\n"):
                    raise Fault(400, "Invalid chunk header")
                n = int(line.split(b";", 1)[0].strip(), 16)
                if n < 0 or n > LIMIT - total:
                    raise Fault(413, "Request too large")
                if not n:
                    if self.rfile.readline(8193) != b"\r\n":
                        raise Fault(400, "Unexpected trailer")
                    break
                chunk = self.rfile.read(n)
                if len(chunk) != n or self.rfile.read(2) != b"\r\n":
                    raise Fault(400, "Incomplete request")
                parts.append(chunk)
                total += n
            raw = b"".join(parts)
        else:
            n = int(length or 0)
            if not 0 <= n <= LIMIT:
                raise Fault(413, "Request too large")
            raw = self.rfile.read(n)
            if len(raw) != n:
                raise Fault(400, "Incomplete request")
        data = json.loads(raw or b"{}")
        if not isinstance(data, dict):
            raise Fault(400, "A JSON object is required")
        return data

    def _chat(self, ctx, body, client_id=None):
        if body.get("n", 1) != 1:
            raise Fault(400, "One completion per local request is supported")
        key = self.headers.get("Idempotency-Key")
        if client_id and key:
            import hashlib

            key = "client:" + client_id + ":" + hashlib.sha256(key.encode()).hexdigest()
        run = ctx.jobs.create(
            {
                "kind": "chat",
                "model": body.get("model"),
                "objective": "External client conversation"
                if client_id
                else "Conversation",
                "external_client_id": client_id,
                "steps": [{"type": "generate", "payload": body}],
            },
            key,
        )
        headers = {"X-Dulche-Run-Id": run}
        stream = body.get("stream") is True
        if stream:
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("X-Dulche-Run-Id", run)
            self.end_headers()
        info = ctx.state.status(run, full=True)
        if info["state"] == "completed" and info["completed_steps"]:
            result = info["completed_steps"][-1]
            if not stream:
                return self.send_json(200, result, headers)
            choice = result["choices"][0]
            packet = {
                "id": result.get("id", run),
                "model": info["model"],
                "object": "chat.completion.chunk",
                "choices": [
                    {
                        "index": 0,
                        "delta": choice["message"],
                        "finish_reason": choice["finish_reason"],
                    }
                ],
                "usage": result.get("usage", {}),
            }
            self.wfile.write(
                b"data: " + json.dumps(packet).encode() + b"\n\ndata: [DONE]\n\n"
            )
            self.wfile.flush()
            return
        cursor = ctx.state.stream_cursor(run)
        last = time.monotonic()
        attempt = None
        emitted = False
        while True:
            with ctx.jobs.cv:
                events = ctx.state.events(run, cursor)
                status = ctx.state.status(run)
                if not events and status["state"] not in TERMINAL:
                    ctx.jobs.cv.wait(timeout=1)
            if stream and time.monotonic() - last > 10:
                self.wfile.write(b": heartbeat\n\n")
                self.wfile.flush()
                last = time.monotonic()
            for event in events:
                cursor = event["seq"]
                if event["kind"] == "generation_start":
                    if stream and emitted and attempt != event["attempt"]:
                        error = {
                            "error": {
                                "message": "Generation resumed from its prompt; reconnect to the saved run",
                                "run_id": run,
                            }
                        }
                        self.wfile.write(
                            b"data: "
                            + json.dumps(error).encode()
                            + b"\n\ndata: [DONE]\n\n"
                        )
                        self.wfile.flush()
                        return
                    attempt = event["attempt"]
                if stream and event["kind"] == "chunks" and event["attempt"] == attempt:
                    emitted = True
                    for chunk in event["data"]:
                        self.wfile.write(
                            b"data: "
                            + json.dumps(chunk, separators=(",", ":")).encode()
                            + b"\n\n"
                        )
                    self.wfile.flush()
                    last = time.monotonic()
            if events or status["state"] not in TERMINAL:
                continue
            if status["state"] != "completed":
                error = {
                    "error": {
                        "message": status.get("reason") or status["state"],
                        "run_id": run,
                        "type": status["state"],
                    }
                }
                if not stream:
                    return self.send_json(409, error, headers)
                self.wfile.write(b"data: " + json.dumps(error).encode() + b"\n\n")
            if stream:
                self.wfile.write(b"data: [DONE]\n\n")
                self.wfile.flush()
                return
            return self.send_json(
                200, ctx.state.status(run, full=True)["completed_steps"][-1], headers
            )

    def handle_api(self):
        ctx = self.server.controller
        u = urlsplit(self.path)
        parts = [unquote(x) for x in u.path.strip("/").split("/")]
        q = parse_qs(u.query)
        get = self.command == "GET"
        body = {} if get else self._body()
        peer = struct.unpack(
            "3i", self.connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
        )[0]
        if len(parts) < 2 or parts[0] != "v1":
            raise Fault(404, "Unknown API version or route")
        route = parts[1:]
        group = route[0]
        if group == "external-api":
            cid = ctx.external.authorize(self.headers.get("Authorization"))
            if get and route == ["external-api", "health"]:
                return self.send_json(
                    200 if ctx.phase == "ready" else 503,
                    {
                        "status": "ok" if ctx.phase == "ready" else "unavailable",
                        "service": "Dulche",
                        "phase": ctx.phase,
                    },
                )
            if get and route == ["external-api", "models"]:
                models = ctx.external.models(loaded_only=True)
                return self.send_json(
                    200,
                    {
                        "object": "list",
                        "data": [
                            {
                                "id": r["id"],
                                "object": "model",
                                "owned_by": "dulche",
                                "max_model_len": r["context"],
                                "context_length": r["context"],
                            }
                            for r in models
                        ],
                    },
                )
            if not get and route == ["external-api", "chat", "completions"]:
                if not isinstance(body.get("model"), str) or not isinstance(
                    body.get("messages"), list
                ):
                    raise Fault(400, "model and messages are required")
                for msg in body["messages"]:
                    content = msg.get("content") if isinstance(msg, dict) else None
                    if isinstance(content, list):
                        for part in content:
                            if not isinstance(part, dict) or part.get("type") != "text":
                                raise Fault(
                                    400,
                                    "This inference endpoint accepts text content and function tool calls",
                                )
                return self._chat(ctx, body, cid)
            raise Fault(404, "Unsupported inference route")
        if group == "external":
            if route == ["external"]:
                return self.send_json(
                    200,
                    ctx.external.info()
                    if get
                    else ctx.external.enable(body.get("enabled")),
                )
            if not get and route == ["external", "clients"]:
                return self.send_json(201, ctx.external.issue(body.get("name")))
            if (
                not get
                and len(route) == 4
                and route[1] == "clients"
                and route[3] == "revoke"
            ):
                return self.send_json(200, ctx.external.revoke(route[2]))
            if not get and route == ["external", "opencode"]:
                return self.send_json(
                    200, ctx.external.connect_opencode(body.get("name", "OpenCode"))
                )
            raise Fault(404, "Unknown provider settings route")
        if not get and route == ["service", "prepare-upgrade"]:
            return self.send_json(200, ctx.prepare_upgrade())
        if not get and route == ["service", "cancel-upgrade"]:
            with ctx.lock:
                if ctx.phase == "upgrading":
                    ctx.phase = "ready"
                return self.send_json(200, {"phase": ctx.phase})
        if get and route == ["status"]:
            return self.send_json(200, ctx.status())
        if get and route == ["logs"]:
            return self.send_json(
                200, {"items": ctx.state.logs(int(q.get("after", [0])[0]))}
            )
        if get and route == ["resources"]:
            return self.send_json(200, ctx.resources)
        if not get and route == ["resources", "requests"]:
            return self.send_json(200, ctx.resource_request(body))
        if get and route == ["training", "status"]:
            from training_jobs import training_status

            return self.send_json(200, training_status(ctx.state.directory))
        if group == "swarms":
            if ctx.phase != "ready" and not get:
                raise Fault(503, "Service is draining")
            code, data = ctx.swarm.handle(
                self.command, route, body, self.headers.get("Idempotency-Key")
            )
            return self.send_json(code, data)
        if group == "benchmarks":
            if len(route) == 1:
                if get:
                    return self.send_json(200, {"items": benchmarks.listing(ctx)})
                run = benchmarks.submit(ctx, body, self.headers.get("Idempotency-Key"))
                return self.send_json(202, ctx.state.status(run))
            if len(route) == 2 and get:
                if route[1] == "candidates":
                    from candidate_screen import candidate_report

                    return self.send_json(
                        200,
                        candidate_report(
                            ctx.state.directory,
                            service_runs=benchmarks.listing(ctx),
                            installed_models=ctx.catalog.installed(),
                        ),
                    )
                return self.send_json(200, benchmarks.result(ctx, route[1]))
        if group == "frontend" and len(route) == 2 and not get:
            return self.send_json(200, ctx.frontend(route[1], body, peer))
        if route == ["settings"]:
            result = (
                {"policy": ctx.state.get("policy", "never")}
                if get
                else ctx.set_policy(body.get("policy"))
            )
            return self.send_json(200, result)
        if group == "catalog":
            if not get and ctx.phase != "ready":
                raise Fault(503, "Dulche is draining")
            # Serialize unregister against model admission and transitions.
            with (
                ctx.lock
                if route == ["catalog", "uninstall"]
                else contextlib.nullcontext()
            ):
                code, result = ctx.catalog.handle(self.command, u.path, q, body)
            return self.send_json(code, result)
        if group == "models":
            if len(route) == 1 and get:
                return self.send_json(
                    200, {"items": [ctx.model(k) for k in list(ctx.cache)]}
                )
            if len(route) == 2 and get:
                return self.send_json(200, ctx.model(route[1]))
            if len(route) >= 3 and route[2] == "profiles":
                if get and len(route) == 3:
                    return self.send_json(200, {"items": ctx.profiles(route[1])})
                if not get and len(route) == 3:
                    return self.send_json(201, ctx.edit_profile(route[1], body))
                if not get and len(route) == 5 and route[4] in ("select", "rename"):
                    return self.send_json(
                        200, ctx.edit_profile(route[1], body, route[3], route[4])
                    )
            if len(route) == 3 and not get and route[2] == "count-tokens":
                return self.send_json(
                    200, ctx.count_prompt(route[1], body.get("messages"))
                )
            if (
                len(route) == 3
                and not get
                and route[2] in ("load", "unload", "suspend", "resume")
            ):
                return self.send_json(
                    202, ctx.change(route[1], route[2], profile=body.get("profile_id"))
                )
        if route == ["chat", "completions"] and not get:
            return self._chat(ctx, body)
        if group == "agents":
            if len(route) == 1:
                if get:
                    return self.send_json(200, {"items": ctx.state.recent()})
                run = ctx.jobs.create(
                    ctx.jobs.prepare({**body, "kind": "agent"}),
                    self.headers.get("Idempotency-Key"),
                )
                return self.send_json(202, ctx.state.status(run))
            if route == ["agents", "stop-all"] and not get:
                with ctx.lock:
                    affected = ctx.jobs.stop()
                    ctx.swarm.stop_all()
                return self.send_json(200, {"stopped": affected})
            if route == ["agents", "allow-new"] and not get:
                ctx.state.set("stop_all", False)
                return self.send_json(200, {"allow_new": True})
            if len(route) == 2 and get:
                return self.send_json(200, ctx.state.status(route[1], full=True))
            if len(route) == 3 and route[2] == "events" and get:
                ctx.state.status(route[1])
                return self.send_json(
                    200,
                    {"items": ctx.state.events(route[1], int(q.get("after", [0])[0]))},
                )
            if len(route) == 3 and not get:
                if route[2] == "stop":
                    with ctx.lock:
                        ctx.jobs.stop(run=route[1])
                        ctx.swarm.on_job_stop(route[1])
                elif route[2] == "resume":
                    ctx.jobs.resume(route[1])
                elif route[2] == "review":
                    return self.send_json(
                        200,
                        ctx.jobs.resolve(
                            route[1], body.get("decision"), body.get("note")
                        ),
                    )
                else:
                    raise Fault(404, "Unknown agent action")
                return self.send_json(200, ctx.state.status(route[1]))
        raise Fault(404, "Unknown Dulche route")

    def _handle(self):
        try:
            self.connection.settimeout(30)
            self.handle_api()
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            # A transport disconnect has no authority to cancel an admitted task.
            pass
        except Fault as e:
            self.send_json(e.status, {"error": {"message": str(e)}})
        except (ValueError, TypeError, KeyError) as e:
            self.send_json(400, {"error": {"message": str(e)}})
        except Exception as e:
            logging.exception("Dulche request failed")
            self.send_json(503, {"error": {"message": str(e)[:1200]}})

    do_GET = _handle
    do_POST = _handle


def default_socket():
    return Path(
        os.environ.get(
            "DULCHE_SOCKET",
            Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
            / "9to1-dulche/api.sock",
        )
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--socket", type=Path, default=default_socket())
    parser.add_argument(
        "--state",
        type=Path,
        default=Path(
            os.environ.get(
                "DULCHE_STATE_DIR",
                Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state"))
                / "9to1/dulche",
            )
        ),
    )
    args = parser.parse_args()
    os.umask(0o077)
    logging.basicConfig(level=logging.INFO)
    args.socket.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    parent = args.socket.parent.lstat()
    if (
        not stat.S_ISDIR(parent.st_mode)
        or parent.st_uid != os.getuid()
        or parent.st_mode & 0o077
    ):
        raise SystemExit("Dulche requires a private runtime socket directory")
    inherited = (
        os.environ.get("LISTEN_PID") == str(os.getpid())
        and os.environ.get("LISTEN_FDS") == "1"
    )
    with (args.socket.parent / "daemon.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if not inherited and (args.socket.exists() or args.socket.is_symlink()):
            info = args.socket.lstat()
            if not stat.S_ISSOCK(info.st_mode) or info.st_uid != os.getuid():
                raise SystemExit("Unexpected socket path")
            args.socket.unlink()
        args.state.mkdir(mode=0o700, parents=True, exist_ok=True)
        manager = EngineManager(args.state)
        catalog = ModelCatalog(args.state)
        if hasattr(manager, "use_catalog"):
            manager.use_catalog(catalog)
        ctx = Controller(args.state, manager, catalog)
        ctx.external = External(ctx)

        def in_use(mid):
            with ctx.lock:
                return (
                    mid in ctx.transitions
                    or ctx.cache.get(mid, {}).get("state") in ("loaded", "loading")
                    or any(a["model"] == mid for a in ctx.jobs.active.values())
                )

        catalog.in_use = in_use
        try:
            with UnixServer(
                str(args.socket), Handler, bind_and_activate=not inherited
            ) as server:
                if inherited:
                    server.socket.close()
                    server.socket = socket.socket(fileno=3)
                    if (
                        server.socket.family != socket.AF_UNIX
                        or server.socket.getsockname() != str(args.socket)
                        or not server.socket.getsockopt(
                            socket.SOL_SOCKET, socket.SO_ACCEPTCONN
                        )
                    ):
                        raise RuntimeError(
                            "Supervisor supplied the wrong listening socket"
                        )
                args.socket.chmod(0o600)
                server.controller = ctx
                ctx.stop_server = lambda: threading.Thread(
                    target=server.shutdown, daemon=True
                ).start()
                for sig in (signal.SIGTERM, signal.SIGINT):
                    signal.signal(sig, lambda *_: ctx.stop_server())
                if ctx.shutdown_requested.is_set():
                    ctx.stop_server()
                logging.info("Dulche v1 listening on %s", args.socket)
                server.serve_forever(poll_interval=0.2)
        finally:
            ctx.close()
            if not inherited:
                args.socket.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
