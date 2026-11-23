"""Real ephemeral TCP and Unix sockets. No real Dulche service, authentication secret or inference."""

import http.client
import http.server
import json
import os
import socket
import socketserver
import tempfile
import threading
import time
import unittest
from pathlib import Path
from unittest import mock

import openai_gateway as gateway

TOKEN = "Bearer fixture-only-token"
FRAME = b'data: {"choices":[{"delta":{"tool_calls":[{"function":{"name":"lookup","arguments":"{\\"q\\":"}}]}}]}\n\n'
END = b'data: {"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"1}"}}]},"finish_reason":"tool_calls"}]}\n\ndata: [DONE]\n\n'


class MockCore(socketserver.ThreadingMixIn, socketserver.UnixStreamServer):
    daemon_threads = True

    def handle_error(self, request, client_address):
        pass


class PrivateHandler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, *a):
        pass

    def handle_(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        self.server.received.append(
            (self.command, self.path, dict(self.headers.items()), body)
        )
        if self.headers.get("Authorization") != TOKEN:
            self.send_response(401)
            self.send_header("Content-Type", "application/json")
            self.send_header("WWW-Authenticate", 'Bearer realm="Fixture"')
            self.end_headers()
            self.wfile.write(b'{"error":{"message":"bad key"}}')
            return
        if body and json.loads(body).get("stream"):
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream; charset=utf-8")
            self.send_header("X-Dulche-Run-Id", "test-run")
            self.send_header("Set-Cookie", "private=off")
            self.end_headers()
            self.wfile.write(FRAME)
            self.wfile.flush()
            self.server.first_frame.set()
            self.server.release.wait(5)
            self.wfile.write(END)
            self.wfile.flush()
            return
        if body and json.loads(body).get("stall"):
            self.server.release.wait(5)
        table = {
            "/v1/external-api/models": b'{"object":"list","data":[{"id":"fixture","object":"model"}]}',
            "/v1/external-api/health": b'{"status":"ok","service":"Dulche"}',
            "/v1/external-api/chat/completions": b'{"object":"chat.completion","choices":[{"message":{"content":null,"tool_calls":[{"id":"x","type":"function","function":{"name":"lookup","arguments":"{}"}}]},"finish_reason":"tool_calls"}]}',
        }
        data = table.get(self.path, b"{}")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Set-Cookie", "private=off")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(data)

    do_GET = handle_
    do_POST = handle_


class GatewayTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        path = str(Path(self.tmp.name) / "core.sock")
        self.core = MockCore(path, PrivateHandler)
        os.chmod(path, 0o600)
        self.core.release = threading.Event()
        self.core.first_frame = threading.Event()
        self.core.received = []
        self.front = gateway.InferenceServer(("127.0.0.1", 0), path)
        self.port = self.front.server_address[1]
        self.ts = []
        for s in (self.core, self.front):
            th = threading.Thread(
                target=s.serve_forever, kwargs={"poll_interval": 0.02}, daemon=True
            )
            th.start()
            self.ts.append(th)
        self.addCleanup(self.close)

    def close(self):
        self.core.release.set()
        for s in (self.front, self.core):
            s.shutdown()
            s.server_close()

    def get(self, method, path, body=None, headers=None, token=TOKEN):
        c = http.client.HTTPConnection("127.0.0.1", self.port, timeout=3)
        h = {}
        if token is not None:
            h["Authorization"] = token
        if body is not None:
            h["Content-Type"] = "application/json"
        h.update(headers or {})
        c.request(method, path, body=body, headers=h)
        r = c.getresponse()
        try:
            return r.status, dict(r.getheaders()), r.read()
        finally:
            c.close()

    def raw(self, headers, body=b""):
        with socket.create_connection(("127.0.0.1", self.port), timeout=2) as s:
            s.sendall(
                (
                    f"POST /v1/chat/completions HTTP/1.1\r\nHost: 127.0.0.1:{self.port}\r\nAuthorization: {TOKEN}\r\nContent-Type: application/json\r\n"
                    + headers
                    + "\r\n\r\n"
                ).encode()
                + body
            )
            with s.makefile("rb") as f:
                return int(f.readline().split()[1])

    def test_exact_public_models_and_health_require_bearer(self):
        for public, private in [
            ("/health", "/v1/external-api/health"),
            ("/v1/models", "/v1/external-api/models"),
        ]:
            status, h, data = self.get("GET", public)
            self.assertEqual(status, 200)
            self.assertTrue(json.loads(data))
            self.assertEqual(self.core.received[-1][:2], ("GET", private))
            self.assertNotIn("Access-Control-Allow-Origin", h)
            self.assertNotIn("Set-Cookie", h)
            self.assertNotIn("Content-Length", h)
            self.assertEqual(self.get("GET", public, token=None)[0], 401)
        self.assertEqual(len(self.core.received), 2)

    def test_private_core_authoritatively_rejects_invalid_bearer(self):
        status, headers, body = self.get("GET", "/health", token="Bearer incorrect")
        self.assertEqual(status, 401)
        self.assertIn("WWW-Authenticate", headers)
        self.assertNotIn(b"incorrect", body)
        self.assertEqual(self.core.received[-1][2]["Authorization"], "Bearer incorrect")

    def test_json_tool_and_idempotency_are_transparent_without_private_headers(self):
        body = b'{"model":"fixture","messages":[{"role":"user","content":"call it"}],"tools":[{"type":"function","function":{"name":"lookup","parameters":{"type":"object"}}}]}'
        status, _headers, response = self.get(
            "POST",
            "/v1/chat/completions",
            body,
            {
                "Idempotency-Key": "case-1",
                "Cookie": "local-private",
                "X-Forwarded-For": "localhost",
                "Upgrade": "test",
            },
        )
        self.assertEqual(status, 200)
        self.assertEqual(
            json.loads(response)["choices"][0]["finish_reason"], "tool_calls"
        )
        method, route, actual, sent = self.core.received[-1]
        self.assertEqual(
            (method, route, sent), ("POST", "/v1/external-api/chat/completions", body)
        )
        self.assertEqual(actual["Authorization"], TOKEN)
        self.assertEqual(actual["Idempotency-Key"], "case-1")
        self.assertEqual(actual["Content-Length"], str(len(body)))
        for h in ("Cookie", "X-Forwarded-For", "Upgrade"):
            self.assertNotIn(h, actual)

    def test_sse_tool_delta_flushed_before_finish_and_done(self):
        c = http.client.HTTPConnection("127.0.0.1", self.port, timeout=2)
        c.request(
            "POST",
            "/v1/chat/completions",
            b'{"stream":true}',
            {"Authorization": TOKEN, "Content-Type": "application/json"},
        )
        try:
            with c.getresponse() as r:
                self.assertEqual(r.status, 200)
                self.assertEqual(r.getheader("X-Dulche-Run-Id"), "test-run")
                self.assertIsNone(r.getheader("Set-Cookie"))
                self.assertTrue(self.core.first_frame.wait(1))
                self.assertEqual(r.fp.readline() + r.fp.readline(), FRAME)
                self.assertFalse(self.core.release.is_set())
                self.core.release.set()
                self.assertEqual(r.read(), END)
        finally:
            c.close()

    def test_unknown_routes_cors_dns_rebinding_and_encoded_routes_are_blocked(self):
        blocked = [
            ("POST", "/v1/agents/stop-all", None),
            ("GET", "/v1/status", None),
            ("GET", "//v1/models", None),
            ("GET", "/v1/models?token=bad", None),
            ("GET", "/v1/%6dodels", None),
            ("GET", "/v1/models", {"Host": "bad.invalid"}),
            ("GET", "/health", {"Origin": "https://example.invalid"}),
            ("GET", "/v1/models", {"Sec-Fetch-Site": "cross-site"}),
            ("OPTIONS", "/v1/models", None),
        ]
        for verb, path, h in blocked:
            with self.subTest(path=path, h=h):
                status, headers, _ = self.get(verb, path, headers=h)
                self.assertGreaterEqual(status, 400)
                self.assertNotIn("Access-Control-Allow-Origin", headers)
        self.assertEqual(self.core.received, [])

    def test_ambiguous_or_oversize_framing_is_rejected_before_read(self):
        for text, status in [
            ("Content-Length: 2\r\nContent-Length: 2", 400),
            ("Transfer-Encoding: chunked\r\nContent-Length: 0", 400),
            ("Content-Length: -1", 400),
            ("Content-Length: " + str(gateway.REQUEST_LIMIT + 1), 413),
            ("X-Request: ok", 411),
            ("Content-Length: 0\r\nContent-Encoding: gzip", 415),
            ("Content-Length: 0\r\nAuthorization: Bearer duplicate", 401),
        ]:
            with self.subTest(text=text):
                self.assertEqual(self.raw(text), status)
        self.assertFalse(self.core.received)

    def test_overall_response_deadline(self):
        self.front.upstream_seconds = 0.15
        start = time.monotonic()
        s, _h, b = self.get("POST", "/v1/chat/completions", b'{"stall":true}')
        self.assertEqual(s, 504)
        self.assertLess(time.monotonic() - start, 1)
        self.assertNotIn(TOKEN.encode(), b)

    def test_connection_limit_rejects_overload_and_recovers_after_disconnect(self):
        self.front.capacity = threading.BoundedSemaphore(1)
        c = http.client.HTTPConnection("127.0.0.1", self.port, timeout=3)
        c.request(
            "POST",
            "/v1/chat/completions",
            b'{"stream":true}',
            {"Authorization": TOKEN, "Content-Type": "application/json"},
        )
        with c.getresponse() as first:
            self.assertEqual(first.status, 200)
            self.assertTrue(self.core.first_frame.wait(1))
            status, headers, _ = self.get("GET", "/health")
            self.assertEqual(status, 429)
            self.assertEqual(headers["Retry-After"], "2")
            self.core.release.set()
            self.assertTrue(first.read().endswith(b"data: [DONE]\n\n"))
        c.close()
        deadline = time.monotonic() + 1
        while time.monotonic() < deadline:
            if self.get("GET", "/health")[0] == 200:
                break
            time.sleep(0.01)
        else:
            self.fail("A completed stream did not release its bounded connection slot")

    def test_incomplete_request_times_out_without_forwarding(self):
        self.front.input_seconds = 0.15
        with socket.create_connection(("127.0.0.1", self.port), timeout=1) as client:
            request = (
                f"POST /v1/chat/completions HTTP/1.1\r\nHost: localhost:{self.port}\r\n"
                f"Authorization: {TOKEN}\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{{"
            )
            client.sendall(request.encode())
            self.assertEqual(client.recv(1), b"")
        self.assertEqual(self.core.received, [])

    def test_missing_private_service_and_wrong_kernel_peer_fail_closed(self):
        valid = self.front.upstream
        self.front.upstream = valid + ".absent"
        self.assertEqual(self.get("GET", "/health")[0], 502)
        self.front.upstream = valid
        uid = os.getuid()
        with mock.patch.object(gateway.os, "getuid", return_value=uid + 1):
            self.assertEqual(self.get("GET", "/health")[0], 503)
        self.assertFalse(self.core.received)

    def test_server_refuses_to_bind_a_public_interface(self):
        with self.assertRaisesRegex(ValueError, "loopback"):
            gateway.InferenceServer(("0.0.0.0", 0), self.front.upstream)

    def test_inherited_fd_is_mandatory_and_cannot_listen_on_a_different_port(self):
        with (
            mock.patch.dict(os.environ, {"LISTEN_PID": "0", "LISTEN_FDS": "1"}),
            self.assertRaises(RuntimeError),
        ):
            gateway.inherited_listener()
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            sock.listen()
            with (
                mock.patch.dict(
                    os.environ, {"LISTEN_PID": str(os.getpid()), "LISTEN_FDS": "1"}
                ),
                mock.patch.object(gateway.socket, "fromfd", return_value=sock.dup()),
                self.assertRaisesRegex(RuntimeError, "loopback listener"),
            ):
                gateway.inherited_listener()


if __name__ == "__main__":
    unittest.main()
