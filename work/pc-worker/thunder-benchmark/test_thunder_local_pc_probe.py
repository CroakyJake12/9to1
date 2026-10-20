"""Offline unit tests for the Thunder local PC probe (`thunder_local_pc_probe.py`).

These tests run purely in the cloud with mocked clocks and transport. They never touch a live PC
key, loopback 8081, a real model, or the network. They exercise the probe's window logic, bounded
transport/SSE handling, model-ID redaction, key handling without leakage, private output semantics,
and synthetic short/long run order and incomplete counts.

Run:  python3 work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py
"""

import datetime as _dt
import hashlib
import json
import os
import pathlib
import stat
import tempfile
import unittest

import thunder_local_pc_probe as probe


class FakeClock:
    def __init__(self, wall, mono_ns=1_000_000_000_000):
        self.wall = wall
        self.mono_ns = mono_ns

    def ns(self):
        return self.mono_ns

    def wall_now(self):
        return self.wall

    def advance(self, seconds):
        self.mono_ns += int(seconds * 1e9)
        self.wall = self.wall + _dt.timedelta(seconds=seconds)


def _inside_wall():
    return _dt.datetime(2026, 10, 20, 20, 30, 0, tzinfo=_dt.timezone.utc)


class FakeTransport:
    """Dispatchable GET/POST fake. session maps path -> handler or bytes/exception."""

    def __init__(self, session):
        self.session = session
        self.calls = []

    def get(self, path, headers=None, timeout_s=10.0):
        self.calls.append(("GET", path))
        spec = self.session.get(path)
        if isinstance(spec, Exception):
            raise spec
        if spec is None:
            return 404, b"{}"
        if isinstance(spec, tuple):
            return spec
        return 200, spec

    def post_stream(self, path, body, headers, deadline_ns):
        self.calls.append(("POST", path))
        spec = self.session.get(path)
        if isinstance(spec, Exception):
            raise spec
        if spec is None:
            return 500, {}, iter([]), None
        return 200, {}, iter([spec]), None


def install(session, key_status="ok", wall=None, native=None, gpu=None):
    """Install fakes on the probe module; return (fake_transport, fake_clock)."""
    transp = FakeTransport(session)
    clock = FakeClock(wall if wall is not None else _inside_wall())

    probe.http_get = transp.get
    probe.http_post_stream = transp.post_stream
    probe.mono_ns_now = clock.ns
    probe.wall_utc_now = clock.wall_now

    if key_status == "ok":
        probe.read_api_key = lambda: {"status": "ok", "key": "dummy-key"}
    else:
        probe.read_api_key = lambda: {"status": key_status}

    if native is not None:
        probe.collect_native = lambda: native
    if gpu is not None:
        probe.try_nvidia_smi = lambda: gpu
    return transp, clock


def _models_body(ids):
    return json.dumps({"data": [{"id": i} for i in ids]}).encode("utf-8")


def _run_in(tmp, wall):
    prev_dir = probe.OUTPUT_DIR
    probe.OUTPUT_DIR = tmp
    try:
        probe.main([])
    finally:
        probe.OUTPUT_DIR = prev_dir
    path = pathlib.Path(tmp) / probe.OUTPUT_FILENAME
    if not path.exists():
        raise AssertionError("no result file written to %s" % tmp)
    return json.loads(path.read_text())


class WindowBehaviorTests(unittest.TestCase):
    def test_not_started_no_network(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            wall = _dt.datetime(2026, 10, 20, 19, 0, 0, tzinfo=_dt.timezone.utc)
            transp, _ = install({}, wall=wall)
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "not_started")
            self.assertEqual(transp.calls, [])

    def test_window_closed_no_network(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            wall = _dt.datetime(2026, 10, 20, 21, 5, 0, tzinfo=_dt.timezone.utc)
            transp, _ = install({}, wall=wall)
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "window_closed")
            self.assertEqual(transp.calls, [])

    def test_missing_key_skips_authenticated(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            sess = {"/health": b"{}\n", "/version": b"{}\n", "/props": b"{}\n"}
            transp, _ = install(sess, key_status="missing")
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["window_status"], "open")
            self.assertEqual(result["service"]["key_status"], "missing")
            self.assertIsNone(result["service"]["models_pre"])
            self.assertTrue(any("api key unavailable" in l for l in result["limitations"]))
            self.assertTrue(all(p not in ("/v1/models", "/v1/chat/completions")
                                for _, p in transp.calls))


class ModelsAndRedactionTests(unittest.TestCase):
    def test_identity_mismatch_skips_inference(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            sess = {
                "/health": b"{}\n",
                "/version": b"{}\n",
                "/props": b"{}\n",
                "/v1/models": _models_body(["some-other-model"]),
            }
            install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertIs(result["service"]["identity_match"], False)
            self.assertIsNone(result["service"]["selected_model_id"])
            self.assertEqual(result["runs"], [])
            self.assertEqual(result["summary"]["success_count"], 0)
            self.assertTrue(any("expected model id not observed" in l for l in result["limitations"]))

    def test_unsafe_model_id_redacted(self):
        rec = probe.safe_model_record("/home/user/.cache/private/model")
        self.assertTrue(rec["redacted"])
        self.assertEqual(rec["id"], "REDACTED_PATH")
        self.assertEqual(rec["sha256"], hashlib.sha256(b"/home/user/.cache/private/model").hexdigest())

    def test_safe_model_id_kept(self):
        rec = probe.safe_model_record(probe.EXPECTED_MODEL_ID)
        self.assertFalse(rec["redacted"])
        self.assertEqual(rec["id"], probe.EXPECTED_MODEL_ID)


class FullRunTests(unittest.TestCase):
    SSE = (
        b'data: {"choices":[{"delta":{"role":"assistant"}}]}\n\n'
        b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
        b'data: {"choices":[{"delta":{}}],"usage":{"prompt_tokens":10,'
        b'"completion_tokens":1,"total_tokens":11}}\n\n'
        b'data: [DONE]\n\n'
    )

    def test_six_interleaved_runs_complete(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            sess = {
                "/health": b"{}\n",
                "/version": b"{}\n",
                "/props": b"{}\n",
                "/v1/models": _models_body([probe.EXPECTED_MODEL_ID]),
                "/v1/chat/completions": self.SSE,
            }
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["summary"]["success_count"], 6)
            self.assertTrue(result["summary"]["target_complete"])
            self.assertEqual([r["kind"] for r in result["runs"]],
                             ["short", "long", "short", "long", "short", "long"])
            self.assertIs(result["service"]["identity_match"], True)
            self.assertIs(result["service"]["models_stable"], True)
            for r in result["runs"]:
                self.assertEqual(r["outcome"], "completed")
                self.assertEqual(r["output_chars"], 1)
                self.assertIsNotNone(r["output_sha256"])
                self.assertEqual(r["usage"]["total_tokens"], 11)
            self.assertEqual(len(transp.calls), 11)  # health/version/props(3)+pre+post+6 runs

    def test_incomplete_success_counts_only_completed(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            def failing_stream(path, body, headers, deadline_ns):
                raise probe.TransportError("connect_error")

            sess = {
                "/health": b"{}\n",
                "/version": b"{}\n",
                "/props": b"{}\n",
                "/v1/models": _models_body([probe.EXPECTED_MODEL_ID]),
                "/v1/chat/completions": failing_stream,
            }
            install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["summary"]["success_count"], 0)
            self.assertFalse(result["summary"]["target_complete"])
            for r in result["runs"]:
                self.assertNotEqual(r["outcome"], "completed")


class SSETests(unittest.TestCase):
    def test_trickling_deadline_aborts(self):
        class AdvClock:
            def __init__(self):
                self.t = 1_000_000_000_000

            def __call__(self):
                return self.t

        clk = AdvClock()
        start = clk()
        deadline = start + int(1e9)

        def chunks():
            yield b'data: {"choices":[{"delta":{"content":"x"}}]}\n\n'
            clk.t += int(2e9)  # now past the deadline
            yield b'data: [DONE]\n\n'

        with self.assertRaises(probe.DeadlineExceeded):
            probe.parse_sse_chunks(chunks(), start, deadline, clock=clk)

    def test_malformed_metadata_is_bounded_and_ignored(self):
        def chunks():
            yield b'data: not-json\n\n'
            yield b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
            yield b'garbage-event\n\n'
            yield b'data: [DONE]\n\n'

        start = 0
        deadline = 10_000_000_000
        res = probe.parse_sse_chunks(chunks(), start, deadline, clock=lambda: start)
        self.assertTrue(res["complete"])
        self.assertEqual(res["output_chars"], 1)
        self.assertIsNotNone(res["output_sha256"])
        self.assertIsNone(res["usage"])

    def test_long_prompt_within_16kib(self):
        self.assertLessEqual(len(probe.LONG_PROMPT.encode("utf-8")), 16 * 1024)


class OutputPrivacyTests(unittest.TestCase):
    def test_private_output_0600_and_no_key(self):
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            prev = probe.OUTPUT_DIR
            probe.OUTPUT_DIR = tmp
            try:
                result = probe.base_result(_inside_wall())
                result["summary"] = {"success_count": 0, "target_complete": False, "plan": []}
                path = probe._write_private_json(result)
            finally:
                probe.OUTPUT_DIR = prev
            self.assertEqual(stat.S_IMODE(os.stat(path).st_mode), 0o600)
            content = pathlib.Path(path).read_text()
            self.assertNotIn("dummy-key", content)
            self.assertIn("schema_version", content)


if __name__ == "__main__":
    unittest.main()
