#!/usr/bin/python3
"""Expose only the authenticated inference subset of Dulche on a fixed TCP loopback socket."""

import contextlib
import http.client
import ipaddress
import json
import os
import re
import socket
import socketserver
import struct
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

from dulchectl import LocalHTTP

HOST = "127.0.0.1"
PORT = 18740
REQUEST_LIMIT = 8 * 1024**2
RESPONSE_LIMIT = 64 * 1024**2
ROUTES = {
    ("GET", "/health"): "/v1/external-api/health",
    ("GET", "/v1/models"): "/v1/external-api/models",
    ("POST", "/v1/chat/completions"): "/v1/external-api/chat/completions",
}
BEARER = re.compile(r"Bearer [A-Za-z0-9._~+/=-]{1,2048}", re.IGNORECASE)


def error_body(message):
    return json.dumps({"error": {"message": message, "type": "gateway_error"}}).encode()


def expire(sock):
    with contextlib.suppress(OSError):
        sock.shutdown(socket.SHUT_RDWR)


def timer(seconds, callback, *args):
    t = threading.Timer(seconds, callback, args=args)
    t.daemon = True
    t.start()
    return t


class InferenceServer(socketserver.ThreadingMixIn, HTTPServer):
    daemon_threads = True
    request_queue_size = 32

    def __init__(
        self, address, upstream, *, bind_and_activate=True, max_connections=16
    ):
        if address[0] != HOST:
            raise ValueError(
                "TCP inference requires the fixed IPv4 loopback interface."
            )
        self.upstream = os.fspath(upstream)
        self.capacity = threading.BoundedSemaphore(max_connections)
        self.input_seconds = 30
        self.upstream_seconds = 3600
        super().__init__(address, InferenceHandler, bind_and_activate=bind_and_activate)

    def verify_request(self, request, client_address):
        return ipaddress.ip_address(client_address[0]).is_loopback

    def process_request(self, request, client_address):
        if not self.capacity.acquire(blocking=False):
            body = error_body("All inference gateway connections are in use.")
            with contextlib.suppress(OSError):
                request.settimeout(0.2)
                request.sendall(
                    b"HTTP/1.0 429 Too Many Requests\r\nConnection: close\r\nRetry-After: 2\r\n"
                    b"Cache-Control: no-store\r\nContent-Type: application/json\r\nContent-Length: "
                    + str(len(body)).encode()
                    + b"\r\n\r\n"
                    + body
                )
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except BaseException:
            self.capacity.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.capacity.release()

    def handle_error(self, request, client_address):
        # Never log the request, authentication header, body, upstream error body or tool arguments.
        pass


class InferenceHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"
    server_version = "Dulche"
    sys_version = ""

    def setup(self):
        super().setup()
        self.connection.settimeout(self.server.input_seconds)
        self.input_deadline = timer(self.server.input_seconds, expire, self.connection)
        self.response_started = False

    def finish(self):
        self.input_deadline.cancel()
        with contextlib.suppress(OSError):
            super().finish()

    def log_message(self, fmt, *args):
        pass

    def send_error(self, code, message=None, explain=None):
        self.reject(code, "Unsupported or malformed HTTP request.")

    def reject(self, code, message):
        self.close_connection = True
        body = error_body(message)
        self.send_response(code)
        self.send_header("Connection", "close")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        if code == 401:
            self.send_header("WWW-Authenticate", 'Bearer realm="Dulche"')
        self.end_headers()
        if getattr(self, "command", None) != "HEAD":
            self.wfile.write(body)

    def parse_length(self):
        lengths = self.headers.get_all("Content-Length") or []
        if self.headers.get_all("Transfer-Encoding") or len(lengths) > 1:
            self.reject(400, "Use an unambiguous Content-Length.")
            return None
        if not lengths:
            if self.command == "POST":
                self.reject(411, "A JSON request requires Content-Length.")
                return None
            return 0
        text = lengths[0].strip()
        if not re.fullmatch(r"[0-9]{1,12}", text):
            self.reject(400, "Invalid Content-Length.")
            return None
        length = int(text)
        if length > REQUEST_LIMIT:
            self.reject(413, "Request exceeds the 8 MiB input limit.")
            return None
        if self.command == "GET" and length:
            self.reject(400, "GET requests must have an empty body.")
            return None
        return length

    def forward(self):
        self.close_connection = True
        host = self.headers.get_all("Host") or []
        port = self.server.server_address[1]
        if len(host) != 1 or host[0].lower() not in (
            f"localhost:{port}",
            f"127.0.0.1:{port}",
        ):
            return self.reject(403, "Use the local inference gateway address.")
        if (
            self.headers.get_all("Origin")
            or self.headers.get("Sec-Fetch-Site", "").lower() == "cross-site"
        ):
            return self.reject(403, "Cross-origin browser requests are disabled.")
        # Match the raw request target too. BaseHTTPRequestHandler normalises a leading //.
        words = self.requestline.split()
        upstream_path = ROUTES.get((self.command, self.path))
        if not upstream_path or len(words) != 3 or words[1] != self.path:
            return self.reject(404, "Unknown inference endpoint.")
        tokens = self.headers.get_all("Authorization") or []
        if len(tokens) != 1 or not BEARER.fullmatch(tokens[0]):
            return self.reject(401, "Provide a Dulche bearer API key.")
        length = self.parse_length()
        if length is None:
            return
        headers = {"Authorization": tokens[0], "Connection": "close"}
        if self.command == "POST":
            types = self.headers.get_all("Content-Type") or []
            if (
                len(types) != 1
                or types[0].split(";", 1)[0].strip().lower() != "application/json"
            ):
                return self.reject(415, "Use application/json.")
            if self.headers.get("Content-Encoding", "identity").lower() != "identity":
                return self.reject(415, "Compressed request bodies are unsupported.")
            headers["Content-Type"] = "application/json"
            keys = self.headers.get_all("Idempotency-Key") or []
            if keys:
                if len(keys) != 1 or not re.fullmatch(r"[!-~]{1,256}", keys[0]):
                    return self.reject(400, "Invalid idempotency key.")
                headers["Idempotency-Key"] = keys[0]
        body = self.rfile.read(length) if length else b""
        if len(body) != length:
            return self.reject(400, "Incomplete request body.")
        self.input_deadline.cancel()
        self.connection.settimeout(30)
        # Inputs remain opaque. The private endpoint validates the bearer and owns inference admission.
        upstream = LocalHTTP(self.server.upstream, timeout=3)
        deadline = None
        timed_out = threading.Event()
        try:
            upstream.connect()
            transport = upstream.sock
            _, uid, _ = struct.unpack(
                "3i", transport.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12)
            )
            if uid != os.getuid():
                return self.reject(
                    503, "Private inference service identity is unavailable."
                )

            def timeout():
                timed_out.set()
                expire(transport)

            deadline = timer(self.server.upstream_seconds, timeout)
            transport.settimeout(self.server.upstream_seconds)
            upstream.request(
                self.command,
                upstream_path,
                body if self.command == "POST" else None,
                headers,
            )
            with upstream.getresponse() as reply:
                self.relay(reply)
        except (OSError, http.client.HTTPException, ValueError) as exc:
            if not self.response_started:
                if timed_out.is_set() or isinstance(exc, TimeoutError):
                    self.reject(504, "Private inference request exceeded its deadline.")
                else:
                    self.reject(502, "The private inference service is unavailable.")
        finally:
            if deadline is not None:
                deadline.cancel()
            upstream.close()

    def relay(self, reply):
        media = reply.getheader("Content-Type") or "application/json"
        if "\r" in media or "\n" in media or len(media) > 256:
            raise ValueError("Invalid upstream content type")
        declared = reply.getheader("Content-Length")
        if declared is not None and (
            not re.fullmatch(r"[0-9]{1,12}", declared) or int(declared) > RESPONSE_LIMIT
        ):
            raise ValueError("Invalid upstream content size")
        self.send_response(reply.status)
        self.send_header("Connection", "close")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Content-Type", media)
        for name in (
            "Retry-After",
            "WWW-Authenticate",
            "X-Dulche-Run-Id",
            "X-Request-Id",
        ):
            value = reply.getheader(name)
            if value and len(value) <= 256 and all(32 <= ord(ch) < 127 for ch in value):
                self.send_header(name, value)
        self.end_headers()
        self.response_started = True
        sent = 0
        while True:
            chunk = reply.read1(65536)
            if not chunk:
                break
            sent += len(chunk)
            if sent > RESPONSE_LIMIT:
                raise ValueError("Response exceeds limit")
            self.wfile.write(chunk)
            self.wfile.flush()

    do_GET = forward
    do_POST = forward


def inherited_listener():
    if (
        os.environ.get("LISTEN_PID") != str(os.getpid())
        or os.environ.get("LISTEN_FDS") != "1"
    ):
        raise RuntimeError(
            "Start the Dulche inference gateway through its user socket unit."
        )
    sock = socket.fromfd(3, socket.AF_INET, socket.SOCK_STREAM)
    try:
        if (
            sock.getsockopt(socket.SOL_SOCKET, socket.SO_DOMAIN) != socket.AF_INET
            or sock.getsockopt(socket.SOL_SOCKET, socket.SO_TYPE) != socket.SOCK_STREAM
            or sock.getsockname() != (HOST, PORT)
            or not sock.getsockopt(socket.SOL_SOCKET, socket.SO_ACCEPTCONN)
        ):
            raise RuntimeError(
                "The inherited socket is not the configured loopback listener."
            )
        return sock
    except BaseException:
        sock.close()
        raise


def main():
    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    sock = inherited_listener()
    with InferenceServer(
        (HOST, PORT), runtime / "9to1-dulche/api.sock", bind_and_activate=False
    ) as server:
        server.socket.close()
        server.socket = sock
        server.serve_forever()


if __name__ == "__main__":
    main()
