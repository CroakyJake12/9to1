#!/usr/bin/python3
"""Model-free llama-server stand-in used only for broker integration tests."""
from __future__ import annotations

import argparse
import http.server
import json
import os
import pathlib
import socket
import socketserver
import threading
import time
import urllib.parse
from typing import Any


class UnixServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
    address_family = socket.AF_UNIX
    daemon_threads = True


SESSIONS: dict[str, threading.Event] = {}
SESSIONS_LOCK = threading.Lock()


def session_for(conversation_id: str) -> threading.Event:
    with SESSIONS_LOCK:
        return SESSIONS.setdefault(conversation_id, threading.Event())


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, fmt: str, *args: Any) -> None:
        return

    def _json(self, status: int, value: dict[str, Any]) -> None:
        body = json.dumps(value, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path == "/health":
            self._json(200, {"status": "ok"})
            return
        self._json(404, {"error": "not_found"})

    def do_POST(self) -> None:
        if self.path != "/v1/chat/completions":
            self._json(404, {"error": "not_found"})
            return
        length = int(self.headers.get("Content-Length", "0"))
        payload = json.loads(self.rfile.read(length) or b"{}")
        messages = payload.get("messages", []) if isinstance(payload, dict) else []
        content = ""
        if isinstance(messages, list) and messages and isinstance(messages[-1], dict):
            content = str(messages[-1].get("content", ""))
        conversation_id = self.headers.get("X-Conversation-Id", "")
        session = session_for(conversation_id) if conversation_id else None

        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.flush()

        if content == "__BLOCK_UNTIL_CANCELLED__":
            ready_path = os.environ.get("HAVEN_FAKE_SESSION_READY", "")
            if ready_path:
                pathlib.Path(ready_path).write_text(conversation_id, encoding="utf-8")
            if session is not None:
                session.wait(30)
            else:
                time.sleep(30)
            return

        events = [
            {"choices": [{"delta": {"content": "hello-from-fake"}, "finish_reason": None}]},
            {"choices": [{"delta": {}, "finish_reason": "stop"}], "usage": {"completion_tokens": 3}},
        ]
        try:
            for event in events:
                self.wfile.write(b"data: " + json.dumps(event, separators=(",", ":")).encode("utf-8") + b"\n\n")
                self.wfile.flush()
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_DELETE(self) -> None:
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path != "/v1/stream":
            self._json(404, {"error": "not_found"})
            return
        conversation_id = urllib.parse.parse_qs(parsed.query).get("conv_id", [""])[0]
        with SESSIONS_LOCK:
            session = SESSIONS.get(conversation_id)
        if session is not None:
            session.set()
            proof_path = os.environ.get("HAVEN_FAKE_CANCEL_PROOF", "")
            if proof_path:
                pathlib.Path(proof_path).write_text(conversation_id, encoding="utf-8")
        self.send_response(204)
        self.send_header("Content-Length", "0")
        self.end_headers()


def main() -> int:
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument("--host", required=True)
    args, _ = parser.parse_known_args()
    socket_path = pathlib.Path(args.host)
    socket_path.unlink(missing_ok=True)
    server = UnixServer(str(socket_path), Handler)
    os.chmod(socket_path, 0o600)
    try:
        server.serve_forever(poll_interval=0.05)
    finally:
        server.server_close()
        socket_path.unlink(missing_ok=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
