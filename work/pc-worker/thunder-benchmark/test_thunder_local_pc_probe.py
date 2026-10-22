"""Offline unit tests for the Thunder local PC probe (`thunder_local_pc_probe.py`).

These tests run purely in the cloud with mocked clocks and transport. They never touch a
live PC key, loopback 8081, a real model, the public HTTPS endpoint, or the network
(except a hermetic, ephemeral localhost HTTP server used solely to exercise the ACTUAL
stdlib `http.client` framing with coalesced and fragmented bodies, which is fully offline
and touches no owner hardware/service/credentials).

Coverage: window logic, unified UTC+monotonic deadline enforcement, verified public
endpoint identity, 5-short/5-long interleaved target with per-size counts, local endpoint
isolation, metadata whitelist/no-leak, safe model-record redaction, exclusive non-overwrite
private output in a newly owned directory, and real-transport framing.

Run:
    python3 work/pc-worker/thunder-benchmark/test_thunder_local_pc_probe.py
"""

import datetime as _dt
import hashlib
import http.server
import json
import os
import pathlib
import stat
import tempfile
import threading
import time
import unittest
from unittest import mock

import thunder_local_pc_probe as probe

_ORIGINALS = {name: getattr(probe, name) for name in (
    "http_get", "http_post_stream", "mono_ns_now", "wall_utc_now",
    "read_api_key", "collect_native", "try_nvidia_smi",
    "ENABLE_LOCAL_8081", "run_one", "PRIVATE_OUTPUT_DIR", "_finalize")}


def _restore_all():
    for k, v in _ORIGINALS.items():
        setattr(probe, k, v)


class ProbeTestBase(unittest.TestCase):
    def tearDown(self):
        _restore_all()


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
    return probe.WINDOW_START_UTC + (probe.WINDOW_END_UTC - probe.WINDOW_START_UTC) / 2


class FakeTransport:
    """Dispatchable GET/POST fake. session maps (endpoint_name, path) or plain path -> spec.

    spec may be bytes, an Exception, or a (status, body) tuple. A plain path key applies
    to any endpoint; a (name, path) tuple key is endpoint-specific.
    """

    def __init__(self, session):
        self.session = session
        self.calls = []

    def _lookup(self, ep, path):
        key = (ep.name, path)
        if key in self.session:
            return self.session[key]
        return self.session.get(path)

    def get(self, path, headers=None, timeout_s=10.0, endpoint=None, dl=None):
        ep = endpoint if endpoint is not None else probe.PUBLIC_ENDPOINT
        self.calls.append(("GET", ep.name, path))
        spec = self._lookup(ep, path)
        if isinstance(spec, Exception):
            raise spec
        if spec is None:
            return 404, b"{}"
        if isinstance(spec, tuple):
            return spec
        return 200, spec

    def post_stream(self, path, body, headers, dl=None, endpoint=None):
        ep = endpoint if endpoint is not None else probe.PUBLIC_ENDPOINT
        self.calls.append(("POST", ep.name, path))
        spec = self._lookup(ep, path)
        if isinstance(spec, Exception):
            raise spec
        if spec is None:
            return 500, {}, iter([]), None
        return 200, {}, iter([spec]), None


def install(session, key_status="ok", wall=None, native=None, gpu=None, local_enabled=False):
    """Install fakes on the probe module; return (fake_transport, fake_clock)."""
    transp = FakeTransport(session)
    clock = FakeClock(wall if wall is not None else _inside_wall())

    probe.http_get = transp.get
    probe.http_post_stream = transp.post_stream
    probe.mono_ns_now = clock.ns
    probe.wall_utc_now = clock.wall_now
    probe.ENABLE_LOCAL_8081 = local_enabled

    if key_status == "ok":
        probe.read_api_key = lambda: {"status": "ok", "key": "dummy-key"}
    else:
        probe.read_api_key = lambda: {"status": key_status}

    probe.collect_native = lambda dl: {
        "os": {"system": "Linux", "release": "x", "architecture": "test"},
        "cpu": {"available": True, "model": None, "count": 8, "source": "mock"},
        "cpu_count": 8,
        "cpu_model": None,
        "ram_bytes_total": None,
        "ram_source": None,
        "sources": ["mock"],
    }
    probe.try_nvidia_smi = lambda dl: {"status": "unavailable", "reason": "mocked"}

    def _quiet_finalize(result):
        probe._write_private_json(result)
        return 0

    probe._finalize = _quiet_finalize
    return transp, clock


def _models_body(ids):
    return json.dumps({"data": [{"id": i} for i in ids]}).encode("utf-8")


def _run_in(tmp, wall):
    prev = probe.PRIVATE_OUTPUT_DIR
    probe.PRIVATE_OUTPUT_DIR = tmp
    try:
        probe.main([])
    finally:
        probe.PRIVATE_OUTPUT_DIR = prev
    files = sorted(pathlib.Path(tmp).rglob("thunder_local_pc_probe_result_*.json"))
    if not files:
        raise AssertionError("no result file written to %s" % tmp)
    return json.loads(files[0].read_text())


def _public_session(ids, chat_sse):
    return {
        "/v1/models": _models_body(ids),
        "/v1/chat/completions": chat_sse,
    }


class WindowBehaviorTests(ProbeTestBase):
    def test_nvidia_observation_timeout_is_capped_by_owner_cutoff(self):
        _, clock = install({}, wall=probe.WINDOW_END_UTC - _dt.timedelta(seconds=0.5))
        with mock.patch.object(probe.shutil, "which", return_value="/test/nvidia-smi"), \
                mock.patch.object(probe.subprocess, "run", return_value=mock.Mock(
                    returncode=0, stdout=b"RTX 5080, 16376, 0, 570\n")) as run:
            result = _ORIGINALS["try_nvidia_smi"](probe.Deadline(probe.WINDOW_END_UTC))
        self.assertEqual(result["status"], "ok")
        self.assertGreater(run.call_args.kwargs["timeout"], 0)
        self.assertLessEqual(run.call_args.kwargs["timeout"], 0.5)

    def test_inventory_only_never_requests_inference(self):
        with tempfile.TemporaryDirectory() as tmp:
            transp, _ = install({"/v1/models": _models_body([probe.EXPECTED_MODEL_ID])})
            probe.PRIVATE_OUTPUT_DIR = tmp
            probe.main(["--inventory-only"])
            files = list(pathlib.Path(tmp).rglob("thunder_local_pc_probe_result_*.json"))
            result = json.loads(files[0].read_text())
            self.assertEqual(result["mode"], "inventory-only")
            self.assertEqual(result["runs"], [])
            self.assertEqual(result["summary"]["plan"], [])
            self.assertFalse(result["summary"]["target_complete"])
            self.assertTrue(result["service"]["models_stable"])
            self.assertEqual([call[0] for call in transp.calls], ["GET", "GET"])

    def test_inventory_and_baseline_are_mutually_exclusive(self):
        with self.assertRaises(SystemExit):
            probe.main(["--inventory-only", "--baseline"])

    def test_native_observation_crossing_cutoff_preserves_closed_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            transp, clock = install({}, wall=probe.WINDOW_END_UTC - _dt.timedelta(seconds=1))
            probe.try_nvidia_smi = lambda dl: (_ for _ in ()).throw(probe.DeadlineExceeded())
            result = _run_in(tmp, clock.wall)
            self.assertEqual(result["window_status"], "window_closed")
            self.assertEqual(transp.calls, [])

    def test_previous_day_does_not_inherit_current_authorisation(self):
        with tempfile.TemporaryDirectory() as tmp:
            wall = _inside_wall() - _dt.timedelta(days=1)
            transp, _ = install({}, wall=wall)
            probe.collect_native = lambda dl: self.fail("expired-day native observation")
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "not_started")
            self.assertEqual(transp.calls, [])

    def test_exact_cutoff_stops_before_hardware_or_network(self):
        with tempfile.TemporaryDirectory() as tmp:
            wall = probe.WINDOW_END_UTC
            transp, _ = install({}, wall=wall)
            probe.collect_native = lambda dl: self.fail("after-cutoff native observation")
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "window_closed")
            self.assertEqual(transp.calls, [])

    def test_not_started_no_network(self):
        with tempfile.TemporaryDirectory() as tmp:
            wall = probe.WINDOW_START_UTC - _dt.timedelta(seconds=1)
            transp, _ = install({}, wall=wall)
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "not_started")
            self.assertEqual(transp.calls, [])

    def test_window_closed_no_network(self):
        with tempfile.TemporaryDirectory() as tmp:
            wall = probe.WINDOW_END_UTC + _dt.timedelta(seconds=1)
            transp, _ = install({}, wall=wall)
            result = _run_in(tmp, wall)
            self.assertEqual(result["window_status"], "window_closed")
            self.assertEqual(transp.calls, [])

    def test_missing_key_skips_authenticated(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = {"/v1/models": _models_body([probe.EXPECTED_MODEL_ID])}
            transp, _ = install(sess, key_status="missing")
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["window_status"], "open")
            self.assertEqual(result["service"]["key_status"], "missing")
            self.assertIsNone(result["service"]["models_pre"])
            self.assertTrue(any("api key unavailable" in l for l in result["limitations"]))
            self.assertTrue(all(p not in ("/v1/models", "/v1/chat/completions")
                                for _, _, p in transp.calls))


class DeadlineTests(ProbeTestBase):
    def test_deadline_enforced_before_socket(self):
        with tempfile.TemporaryDirectory() as tmp:
            transp, clock = install({})
            # Negative timeout => already expired; ensure() must raise before any call.
            dl = probe.Deadline.from_timeout(-5)
            self.assertLessEqual(dl.ns(), 0)
            with self.assertRaises(probe.DeadlineExceeded):
                dl.ensure()
            self.assertEqual(transp.calls, [])

    def test_monotonic_clock_governs_wall_rollback(self):
        # Wall stays fixed inside the window, but real monotonic time advances past the end:
        # the cost must be enforced (a wall rollback must never extend the permitted window).
        t0 = _inside_wall()  # Midpoint of the explicit owner-authorised dated window.
        mono = [1_000_000_000_000]
        probe.wall_utc_now = lambda: t0
        probe.mono_ns_now = lambda: mono[0]
        try:
            dl = probe.Deadline(probe.WINDOW_END_UTC)
            mono[0] += int(3 * 60 * 60 * 1e9)  # monotonic elapsed 3h > remaining 2.5h
            with self.assertRaises(probe.DeadlineExceeded):
                dl.ensure()
        finally:
            probe.wall_utc_now = _dt.datetime.now(_dt.timezone.utc)
            probe.mono_ns_now = time.monotonic_ns

    def test_end_during_preflight(self):
        with tempfile.TemporaryDirectory() as tmp:
            _, clock = install({})
            dl = probe.Deadline(probe.WINDOW_END_UTC)
            clock.advance(3 * 60 * 60)  # 17:30 + 3h => past 20:00 end
            with self.assertRaises(probe.DeadlineExceeded):
                dl.ensure()


class ModelsAndRedactionTests(ProbeTestBase):
    def test_public_endpoint_identity_and_pre_post(self):
        with tempfile.TemporaryDirectory() as tmp:
            sse = (
                b'data: {"choices":[{"delta":{"role":"assistant"}}]}\n\n'
                b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
                b'data: {"choices":[{"delta":{}}],"usage":{"prompt_tokens":9,'
                b'"completion_tokens":1,"total_tokens":10}}\n\n'
                b'data: [DONE]\n\n'
            )
            sess = _public_session([probe.EXPECTED_MODEL_ID], sse)
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["service"]["endpoint"], probe.PUBLIC_ENDPOINT.base_url)
            self.assertEqual(result["service"]["expected_model_id"], probe.EXPECTED_MODEL_ID)
            self.assertIs(result["service"]["identity_match"], True)
            self.assertEqual(result["service"]["selected_model_id"], probe.EXPECTED_MODEL_ID)
            self.assertEqual(result["service"]["models_pre"]["status"], "ok")
            self.assertIs(result["service"]["models_stable"], True)
            self.assertTrue(any(e == ("GET", "public", "/v1/models") for e in transp.calls))

    def test_identity_mismatch_skips_inference(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session(["some-other-model"], b"")
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertIs(result["service"]["identity_match"], False)
            self.assertIsNone(result["service"]["selected_model_id"])
            self.assertEqual(result["runs"], [])
            self.assertEqual(result["summary"]["success_count"], 0)
            self.assertTrue(any("expected public model ids not observed" in l
                                for l in result["limitations"]))
            self.assertTrue(all(e[0] != "POST" for e in transp.calls))

    def test_default_model_prefers_strata_dynamic(self):
        # When the endpoint returns both verified ids, the default chat model is the dynamic alias
        # (strata-dynamic); inference is sent against it.
        with tempfile.TemporaryDirectory() as tmp:
            sse = (
                b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
                b'data: [DONE]\n\n'
            )
            sess = _public_session([probe.EXPECTED_MODEL_ID, probe.PUBLIC_MODEL_ALIAS], sse)
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertIs(result["service"]["identity_match"], True)
            self.assertEqual(result["service"]["selected_model_id"], probe.PUBLIC_MODEL_ALIAS)
            self.assertTrue(any(e[0] == "POST" for e in transp.calls))

    def test_parse_args_key_file_path_only(self):
        self.assertEqual(probe._parse_args(["--key-file", "/x/homepc-key"]),
                         ("/x/homepc-key", False))
        self.assertEqual(probe._parse_args([]), (None, False))
        self.assertEqual(probe._parse_args(["--baseline"]), (None, True))
        self.assertEqual(probe._parse_args(["--baseline", "--key-file", "/x/k"]),
                         ("/x/k", True))
        with self.assertRaises(SystemExit):
            probe._parse_args(["--key-file"])
        with self.assertRaises(SystemExit):
            probe._parse_args(["--unknown"])

    def test_unsafe_model_id_redacted(self):
        rec = probe.safe_model_record("/home/user/.cache/private/model")
        self.assertTrue(rec["redacted"])
        self.assertEqual(rec["id"], "REDACTED_PATH")
        self.assertEqual(rec["sha256"],
                         hashlib.sha256(b"/home/user/.cache/private/model").hexdigest())

    def test_control_oversize_model_id_redacted(self):
        rec = probe.safe_model_record("a\x00b")
        self.assertTrue(rec["redacted"])
        rec2 = probe.safe_model_record("m" * 500)
        self.assertTrue(rec2["redacted"])
        self.assertEqual(probe.safe_model_record("")["id"], "REDACTED_INVALID")

    def test_safe_model_id_kept(self):
        rec = probe.safe_model_record(probe.EXPECTED_MODEL_ID)
        self.assertFalse(rec["redacted"])
        self.assertEqual(rec["id"], probe.EXPECTED_MODEL_ID)


class FullRunTests(ProbeTestBase):
    SSE = (
        b'data: {"choices":[{"delta":{"role":"assistant"}}]}\n\n'
        b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
        b'data: {"choices":[{"delta":{}}],"usage":{"prompt_tokens":10,'
        b'"completion_tokens":1,"total_tokens":11}}\n\n'
        b'data: [DONE]\n\n'
    )

    def test_five_short_five_long_target_complete(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session([probe.EXPECTED_MODEL_ID], self.SSE)
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertEqual(result["summary"]["plan"], ["short", "long"] * 5)
            self.assertEqual(result["summary"]["success_short"], 5)
            self.assertEqual(result["summary"]["success_long"], 5)
            self.assertEqual(result["summary"]["success_count"], 10)
            self.assertTrue(result["summary"]["target_complete"])
            self.assertEqual([r["kind"] for r in result["runs"]],
                             ["short", "long"] * 5)
            for r in result["runs"]:
                self.assertEqual(r["outcome"], "completed")
                self.assertEqual(r["output_chars"], 1)
                self.assertEqual(r["usage"]["total_tokens"], 11)
            # pre + 10 runs + post = 12 public calls; no local/loopback calls at all.
            self.assertEqual(len(transp.calls), 12)
            self.assertTrue(all(e[1] == "public" for e in transp.calls))

    def test_per_size_partial_counts_incomplete(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session([probe.EXPECTED_MODEL_ID], self.SSE)
            install(sess)
            # Simulate: short runs succeed, long runs fail (http_500).
            def fake_run(kind, model_id, seq, dl, key, ep, prompt=None, max_tokens=None):
                if kind == "short":
                    return {"kind": kind, "seq": seq, "outcome": "completed",
                            "first_text_delta_s": 0.1, "total_s": 0.2,
                            "output_chars": 1, "output_sha256": "x", "usage": None}
                return {"kind": kind, "seq": seq, "outcome": "error",
                        "error_category": "http_500"}
            probe.run_one = fake_run
            try:
                result = _run_in(tmp, _inside_wall())
            finally:
                del probe.run_one
            self.assertEqual(result["summary"]["success_short"], 5)
            self.assertEqual(result["summary"]["success_long"], 0)
            self.assertEqual(result["summary"]["success_count"], 5)
            self.assertFalse(result["summary"]["target_complete"])
            self.assertEqual(len([r for r in result["runs"] if r["kind"] == "long"]), 5)


class LocalEndpointTests(ProbeTestBase):
    def test_local_uses_only_returned_ids_never_public_alias(self):
        with tempfile.TemporaryDirectory() as tmp:
            local_ids = ["local-own-model"]
            sess = {
                ("public", "/v1/models"): _models_body([probe.EXPECTED_MODEL_ID]),
                ("local", "/v1/models"): _models_body(local_ids),
                ("local", "/health"): b'{"status":"ok"}\n',
                ("local", "/version"): b'{"version":"1.2.3"}\n',
                ("local", "/props"): b'{"max_tokens":4096,"secret_key":"should-not-leak"}\n',
                ("public", "/v1/chat/completions"): FullRunTests.SSE,
            }
            transp, _ = install(sess, local_enabled=True)
            result = _run_in(tmp, _inside_wall())
            local = result["service"]["local"]
            self.assertTrue(local["probed"])
            self.assertIs(local["expected_model_id_assumed"], False)
            self.assertEqual(local["models"]["ids"], local_ids)
            # Public inference still selected the public id, not the local one.
            self.assertEqual(result["service"]["selected_model_id"], probe.EXPECTED_MODEL_ID)
            self.assertEqual(result["summary"]["success_short"], 5)
            # The local /props secret value must not appear anywhere in the result.
            serialized = json.dumps(result)
            self.assertNotIn("should-not-leak", serialized)
            self.assertNotIn("secret_key", serialized)


class MetadataWhitelistTests(ProbeTestBase):
    def test_whitelist_drops_unknown_and_pathish(self):
        obj = {
            "status": "ok",
            "version": "1.0",
            "secret": "s3cr3t",
            "launch_args": ["--model", "/tmp/x"],
            "nested": {"a": 1},
        }
        out = probe.sanitize_metadata(obj, probe.LOCAL_VERSION_ALLOWED)
        self.assertEqual(out, {"version": "1.0"})

    def test_non_json_metadata_recorded_as_len_only(self):
        self.assertEqual(probe.sanitize_metadata("not-a-dict", {"status"}), {})


class OutputPrivacyTests(ProbeTestBase):
    def test_private_output_exclusive_non_overwrite_no_parent_chmod(self):
        with tempfile.TemporaryDirectory() as tmp:
            # The parent (tmp) is created 0700 by tempfile; keep a distinguishable mode and
            # verify we do NOT chmod it.
            parent_mode_before = stat.S_IMODE(os.stat(tmp).st_mode)
            result = probe.base_result(_inside_wall())
            result["summary"] = {
                "plan": [], "success_count": 0, "success_short": 0,
                "success_long": 0, "target_complete": False,
            }
            path1 = probe._write_private_json(result, directory=tmp)
            path2 = probe._write_private_json(result, directory=tmp)
            self.assertNotEqual(path1, path2)   # unique exclusive file names
            self.assertEqual(stat.S_IMODE(os.stat(path1).st_mode), 0o600)
            self.assertEqual(stat.S_IMODE(os.stat(path2).st_mode), 0o600)
            content = pathlib.Path(path1).read_text()
            self.assertNotIn("dummy-key", content)
            self.assertIn("schema_version", content)
            # Parent directory mode is unchanged (no hidden chmod mutation).
            self.assertEqual(stat.S_IMODE(os.stat(tmp).st_mode), parent_mode_before)
            self.assertEqual(stat.S_IMODE(os.stat(pathlib.Path(path1).parent).st_mode), 0o700)
            self.assertEqual(stat.S_IMODE(os.stat(pathlib.Path(path2).parent).st_mode), 0o700)
            # Each result landed in its own FRESH private directory; two files survive.
            self.assertNotEqual(pathlib.Path(path1).parent, pathlib.Path(path2).parent)
            self.assertEqual(len(list(pathlib.Path(tmp).rglob("thunder_local_pc_probe_result_*.json"))), 2)

    def test_private_output_existing_dir_not_chmodded(self):
        # An EXISTING directory passed as base must never be chmod'd or reused as the output dir;
        # a fresh private subdirectory is created instead.
        with tempfile.TemporaryDirectory() as tmp:
            existing = pathlib.Path(tmp) / "existing"
            existing.mkdir(mode=0o750)
            os.chmod(existing, 0o750)  # defeat restrictive umask so 0750 is observable
            result = probe.base_result(_inside_wall())
            result["summary"] = {
                "plan": [], "success_count": 0, "success_short": 0,
                "success_long": 0, "target_complete": False,
            }
            path = probe._write_private_json(result, directory=str(existing))
            self.assertEqual(stat.S_IMODE(os.stat(existing).st_mode), 0o750)  # unchanged
            # The output directory is a NEW 0700 child, not the existing dir itself.
            out_dir = pathlib.Path(path).parent
            self.assertNotEqual(out_dir, existing)
            self.assertEqual(stat.S_IMODE(os.stat(out_dir).st_mode), 0o700)
            self.assertTrue(out_dir.name.startswith("thunder_probe_private_"))

    def test_private_output_symlink_base_refused(self):
        # A symlink base must be refused (never write through / mutate the target).
        with tempfile.TemporaryDirectory() as tmp:
            target = pathlib.Path(tmp) / "target"
            target.mkdir(mode=0o750)
            os.chmod(target, 0o750)  # defeat restrictive umask so 0750 is observable
            link = pathlib.Path(tmp) / "link"
            link.symlink_to(target)
            result = probe.base_result(_inside_wall())
            result["summary"] = {
                "plan": [], "success_count": 0, "success_short": 0,
                "success_long": 0, "target_complete": False,
            }
            with self.assertRaises(RuntimeError):
                probe._write_private_json(result, directory=str(link))
            self.assertEqual(stat.S_IMODE(os.stat(target).st_mode), 0o750)  # target untouched
            self.assertEqual(list(target.rglob("thunder_local_pc_probe_result_*.json")), [])


class RealTransportFramingTests(ProbeTestBase):
    """Exercise the ACTUAL stdlib http.client framing (not a mirror) with coalesced and
    fragmented bodies via a hermetic, ephemeral localhost HTTP server. Fully offline; no
    owner PC/key/service/8081/public endpoint touched."""

    BODY = b'{"ok":true,"blob":"' + b"x" * 7000 + b'"}'

    @staticmethod
    def _start(writer_fn):
        class Handler(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def do_GET(self):  # noqa: N802
                writer_fn(self)

            def log_message(self, *a):  # quiet
                pass

        httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=httpd.serve_forever, daemon=True)
        thread.start()
        return httpd, httpd.server_address[1]

    @classmethod
    def _probe_get(cls, port):
        ep = probe.Endpoint("test", "http", "127.0.0.1", port, False, "", {
            "models": "/thing", "chat": "/thing"})
        dl = probe.Deadline.from_timeout(10)
        return probe.http_get(ep.path("models"), headers={}, endpoint=ep, dl=dl)

    def test_coalesced_headers_body(self):
        def write(handler):
            raw = (b"HTTP/1.1 200 OK\r\nContent-Length: %d\r\n"
                   b"Content-Type: application/json\r\nConnection: close\r\n\r\n"
                   % len(self.BODY)) + self.BODY
            handler.wfile.write(raw)
            handler.wfile.flush()

        httpd, port = self._start(write)
        try:
            status, body = self._probe_get(port)
            self.assertEqual(status, 200)
            self.assertEqual(body, self.BODY)
            self.assertEqual(len(body), len(self.BODY))
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_fragmented_headers_body(self):
        def write(handler):
            head = (b"HTTP/1.1 200 OK\r\nContent-Length: %d\r\n"
                    b"Content-Type: application/json\r\nConnection: close\r\n\r\n"
                    % len(self.BODY))
            handler.wfile.write(head)
            handler.wfile.flush()
            # Send body one byte at a time to force many recv() calls.
            for i in range(len(self.BODY)):
                handler.wfile.write(self.BODY[i:i + 1])
                handler.wfile.flush()
                time.sleep(0.001)

        httpd, port = self._start(write)
        try:
            status, body = self._probe_get(port)
            self.assertEqual(status, 200)
            self.assertEqual(body, self.BODY)
            self.assertEqual(len(body), len(self.BODY))
        finally:
            httpd.shutdown()
            httpd.server_close()


class SSETests(ProbeTestBase):
    def _patch_clocks(self, wall=None, mono_ns=1_000_000_000_000):
        clk = FakeClock(wall if wall is not None else _inside_wall(), mono_ns)
        probe.mono_ns_now = clk.ns
        probe.wall_utc_now = clk.wall_now
        return clk

    def test_trickling_deadline_aborts(self):
        clk = self._patch_clocks()
        start = clk.mono_ns
        dl = probe.Deadline.from_mono_ns(start + int(1e9))

        def chunks():
            yield b'data: {"choices":[{"delta":{"content":"x"}}]}\n\n'
            clk.advance(2)  # now past the deadline
            yield b'data: [DONE]\n\n'

        with self.assertRaises(probe.DeadlineExceeded):
            probe.parse_sse_chunks(chunks(), start, dl, clock=clk.ns)

    def test_malformed_metadata_is_bounded_and_ignored(self):
        clk = self._patch_clocks()
        start = clk.mono_ns
        dl = probe.Deadline.from_mono_ns(start + int(10_000_000_000))

        def chunks():
            yield b'data: not-json\n\n'
            yield b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
            yield b'garbage-event\n\n'
            yield b'data: [DONE]\n\n'

        res = probe.parse_sse_chunks(chunks(), start, dl, clock=clk.ns)
        self.assertTrue(res["complete"])
        self.assertEqual(res["output_chars"], 1)
        self.assertIsNotNone(res["output_sha256"])
        self.assertIsNone(res["usage"])

    def test_usage_and_server_timings_captured_separately(self):
        clk = self._patch_clocks()
        start = clk.mono_ns
        dl = probe.Deadline.from_mono_ns(start + int(10_000_000_000))

        def chunks():
            yield b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
            yield (b'data: {"choices":[{"delta":{}}],"usage":{"prompt_tokens":9,'
                   b'"completion_tokens":1,"total_tokens":10},'
                   b'"server_timings":{"queue_ms":3.5,"predict_ms":12.0,"draft_n":5}}\n\n')
            yield b'data: [DONE]\n\n'

        res = probe.parse_sse_chunks(chunks(), start, dl, clock=clk.ns)
        self.assertTrue(res["complete"])
        self.assertEqual(res["usage"]["total_tokens"], 10)
        # Server timings are kept separate from the client-side totals.
        self.assertIsNotNone(res["server_timings"])
        self.assertEqual(res["server_timings"]["source"], "server")
        self.assertEqual(res["server_timings"]["values"]["draft_n"], 5)
        self.assertAlmostEqual(res["server_timings"]["values"]["queue_ms"], 3.5)
        # Client totals are distinct and present.
        self.assertIsNotNone(res["total_s"])
        self.assertIsNotNone(res["first_text_delta_s"])

    def test_crlf_ssE_delimiters_normalized(self):
        clk = self._patch_clocks()
        start = clk.mono_ns
        dl = probe.Deadline.from_mono_ns(start + int(10_000_000_000))

        def chunks():
            yield b'data: {"choices":[{"delta":{"content":"A"}}]}\r\n\r\n'
            yield b'data: {"choices":[{"delta":{"content":"B"}}]}\r\n\r\n'
            yield b'data: [DONE]\r\n\r\n'

        res = probe.parse_sse_chunks(chunks(), start, dl, clock=clk.ns)
        self.assertTrue(res["complete"])
        self.assertEqual(res["output_chars"], 2)   # both CRLF-separated events parsed

    def test_sse_error_object_raises_classified(self):
        clk = self._patch_clocks()
        start = clk.mono_ns
        dl = probe.Deadline.from_mono_ns(start + int(10_000_000_000))

        def chunks():
            yield b'data: {"choices":[{"delta":{"content":"x"}}]}\n\n'
            yield (b'data: {"error":{"type":"server_error",'
                   b'"message":"the engine stopped unexpectedly (exit code -6)"}}\n\n')

        with self.assertRaises(probe.SSEError) as cm:
            probe.parse_sse_chunks(chunks(), start, dl, clock=clk.ns)
        self.assertEqual(cm.exception.category, "engine_crash")

    def test_classify_sse_error_categories(self):
        crash = {"error": {"type": "server_error",
                           "message": "the engine stopped unexpectedly (exit code -6)"}}
        auth = {"error": {"type": "authentication_error", "message": "invalid api key"}}
        rate = {"error": {"type": "rate_limit_exceeded", "message": "slow down"}}
        generic = {"error": {"type": "server_error", "message": "boom"}}
        self.assertEqual(probe._classify_sse_error(crash), "engine_crash")
        self.assertEqual(probe._classify_sse_error(auth), "auth_error")
        self.assertEqual(probe._classify_sse_error(rate), "rate_error")
        self.assertEqual(probe._classify_sse_error(generic), "server_error")
        self.assertEqual(probe._classify_sse_error({"error": "oops"}), "server_error")
        self.assertEqual(probe._classify_sse_error({}), "server_error")
        self.assertEqual(probe._classify_sse_error(None), "server_error")


class Pass11RealRouteStreamTests(ProbeTestBase):
    """REAL loopback HTTP fixtures for pass-11. Hermetic, ephemeral localhost servers only:
    exact route (no double `/v1`), no-redirect credential safety, delayed local SSE stream
    (first content observed before later body released), real SSE error detection, and
    deadline+stall bounding. No owner PC/key/service/8081/public endpoint touched."""

    @staticmethod
    def _serve(handler_cls):
        httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler_cls)
        threading.Thread(target=httpd.serve_forever, daemon=True).start()
        return httpd, httpd.server_address[1]

    @staticmethod
    def _ep(port, paths=None):
        if paths is None:
            paths = dict(probe.PUBLIC_PATHS)   # {"models": "/models", "chat": "/chat/completions"}
        return probe.Endpoint("lo", "http", "127.0.0.1", port, False, "/v1", paths)

    @staticmethod
    def _read_body(handler):
        n = int(handler.headers.get("Content-Length") or 0)
        return handler.rfile.read(n) if n else b""

    def test_real_loopback_exact_route_no_double_prefix(self):
        seen = []

        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def _json(self, body):
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(body)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(body)

            def do_GET(self):  # noqa: N802
                seen.append((self.command, self.path, dict(self.headers), b""))
                self._json(b'{"data":[{"id":"strata-dynamic"}]}')

            def do_POST(self):  # noqa: N802
                seen.append((self.command, self.path, dict(self.headers),
                             Pass11RealRouteStreamTests._read_body(self)))
                sse = (b'data: {"choices":[{"delta":{"content":"4"}}]}\n\n'
                       b'data: [DONE]\n\n')
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Content-Length", str(len(sse)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(sse)

        httpd, port = self._serve(H)
        try:
            ep = self._ep(port)
            dl = probe.Deadline.from_timeout(10)
            # GET: exact public route, exactly once, no double prefix.
            status, body = probe.http_get(ep.path("models"),
                                          headers={"Authorization": "Bearer dummy"},
                                          endpoint=ep, dl=dl)
            self.assertEqual(status, 200)
            self.assertIn(b"strata-dynamic", body)
            self.assertEqual(len(seen), 1)
            self.assertEqual(seen[0][0], "GET")
            self.assertEqual(seen[0][1], "/v1/models")     # NOT /v1/v1/models
            self.assertEqual(seen[0][2].get("Authorization"), "Bearer dummy")
            # POST: exact chat route and exact body.
            payload = json.dumps({"model": "strata-dynamic", "stream": True}).encode()
            start = probe.mono_ns_now()
            status, _, chunks, conn = probe.http_post_stream(
                ep.path("chat"), payload, {"Content-Type": "application/json", "Authorization": "Bearer dummy"},
                dl, endpoint=ep)
            self.assertEqual(status, 200)
            try:
                parsed = probe.parse_sse_chunks(chunks, start, dl)
            finally:
                try:
                    conn.close()
                except Exception:
                    pass
            self.assertTrue(parsed["complete"])
            # second request recorded with exact route + exact body
            self.assertEqual(len(seen), 2)
            self.assertEqual(seen[1][0], "POST")
            self.assertEqual(seen[1][1], "/v1/chat/completions")
            self.assertEqual(seen[1][3], payload)
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_real_two_server_no_redirect_credentials_not_forwarded(self):
        b_seen = []

        class B(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def _record(self):
                b_seen.append((self.command, self.path, dict(self.headers),
                               Pass11RealRouteStreamTests._read_body(self)))

            def do_GET(self):  # noqa: N802
                self._record()
                body = b'{"ok":"B"}'
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)

            do_POST = do_GET

        b_httpd, bport = self._serve(B)
        a_seen = []

        class A(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_GET(self):  # noqa: N802
                a_seen.append((self.command, self.path, dict(self.headers), b""))
                self.send_response(302)
                self.send_header("Location", "http://127.0.0.1:%d/other" % bport)
                self.send_header("Content-Length", "0")
                self.end_headers()

        a_httpd, aport = self._serve(A)
        try:
            ep = self._ep(aport)
            dl = probe.Deadline.from_timeout(10)
            status, body = probe.http_get(ep.path("models"),
                                          headers={"Authorization": "Bearer dummy"},
                                          endpoint=ep, dl=dl)
            # Redirect is REFUSED, not followed: status stays 3xx.
            self.assertEqual(status, 302)
            self.assertTrue(a_seen, "fixed origin must receive the request")
            self.assertEqual(a_seen[0][2].get("Authorization"), "Bearer dummy")
            # The redirected host must NEVER have been contacted (no credentials forwarded).
            self.assertEqual(b_seen, [])
        finally:
            a_httpd.shutdown()
            a_httpd.server_close()
            b_httpd.shutdown()
            b_httpd.server_close()

    def test_real_delayed_local_stream_first_content_before_later_body(self):
        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_POST(self):  # noqa: N802
                Pass11RealRouteStreamTests._read_body(self)
                first = b'data: {"choices":[{"delta":{"content":"A"}}]}\n\n'
                second = b'data: {"choices":[{"delta":{"content":"B"}}]}\n\n'
                done = b'data: [DONE]\n\n'
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Transfer-Encoding", "chunked")
                self.end_headers()
                w = self.wfile

                def chunk(data):
                    w.write(("%x\r\n" % len(data)).encode())
                    w.write(data)
                    w.write(b"\r\n")
                    w.flush()

                chunk(first)
                time.sleep(0.4)          # later body withheld; first content already flushed
                chunk(second)
                chunk(done)
                w.write(b"0\r\n\r\n")
                w.flush()

        httpd, port = self._serve(H)
        try:
            ep = self._ep(port)
            dl = probe.Deadline.from_timeout(15)
            start = probe.mono_ns_now()
            status, _, chunks, conn = probe.http_post_stream(
                ep.path("chat"), b'{}', {"Content-Type": "application/json"}, dl, endpoint=ep)
            self.assertEqual(status, 200)
            try:
                res = probe.parse_sse_chunks(chunks, start, dl)
            finally:
                try:
                    conn.close()
                except Exception:
                    pass
            self.assertTrue(res["complete"])
            self.assertEqual(res["output_chars"], 2)
            # First token was observed well before the later body was released.
            self.assertIsNotNone(res["first_text_delta_s"])
            self.assertLess(res["first_text_delta_s"], res["total_s"] - 0.3)
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_real_sse_error_detected_via_transport(self):
        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_POST(self):  # noqa: N802
                Pass11RealRouteStreamTests._read_body(self)
                evt = (b'data: {"error":{"type":"server_error",'
                       b'"message":"the engine stopped unexpectedly (exit code -6)"}}\n\n')
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Content-Length", str(len(evt)))
                self.send_header("Connection", "close")
                self.end_headers()
                self.wfile.write(evt)

        httpd, port = self._serve(H)
        try:
            ep = self._ep(port)
            dl = probe.Deadline.from_timeout(10)
            start = probe.mono_ns_now()
            status, _, chunks, conn = probe.http_post_stream(
                ep.path("chat"), b'{}', {"Content-Type": "application/json"}, dl, endpoint=ep)
            self.assertEqual(status, 200)
            try:
                with self.assertRaises(probe.SSEError) as cm:
                    probe.parse_sse_chunks(chunks, start, dl)
            finally:
                try:
                    conn.close()
                except Exception:
                    pass
            self.assertEqual(cm.exception.category, "engine_crash")
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_real_stall_deadline_aborts(self):
        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_POST(self):  # noqa: N802
                Pass11RealRouteStreamTests._read_body(self)
                first = b'data: {"choices":[{"delta":{"content":"A"}}]}\n\n'
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Transfer-Encoding", "chunked")
                self.end_headers()
                w = self.wfile
                w.write(("%x\r\n" % len(first)).encode())
                w.write(first)
                w.write(b"\r\n")
                w.flush()
                time.sleep(3.0)          # keepalive/stall: no further bytes for 3s
                w.write(b"0\r\n\r\n")
                w.flush()

        httpd, port = self._serve(H)
        try:
            ep = self._ep(port)
            # Budget comfortably above connect/first-frame overhead but well below the server's
            # 3s stall: the bounded per-read reads must abort at the absolute deadline, not wait
            # for the full 3s keepalive stall.
            dl = probe.Deadline.from_timeout(1.5)
            start = probe.mono_ns_now()
            status, _, chunks, conn = probe.http_post_stream(
                ep.path("chat"), b'{}', {"Content-Type": "application/json"}, dl, endpoint=ep)
            self.assertEqual(status, 200)
            try:
                with self.assertRaises(probe.DeadlineExceeded):
                    probe.parse_sse_chunks(chunks, start, dl)
            finally:
                try:
                    conn.close()
                except Exception:
                    pass
        finally:
            httpd.shutdown()
            httpd.server_close()

    def test_real_deadline_bound_applies_between_headers_and_body(self):
        class H(http.server.BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *a):
                pass

            def do_GET(self):  # noqa: N802
                # Headers are flushed first, then the body is withheld for 4s. The budget is
                # consumed AFTER the status/headers (between headers and body read), so the
                # per-read SOCKET timeout must bound each body read and the absolute deadline
                # must abort near the bound — NOT wait the full server stall.
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", "1000")
                self.send_header("Connection", "keep-alive")
                self.end_headers()
                self.wfile.flush()
                time.sleep(4.0)
                self.wfile.write(b"{")
                self.wfile.flush()

        httpd, port = self._serve(H)
        try:
            ep = self._ep(port)
            dl = probe.Deadline.from_timeout(1.2)
            start = probe.mono_ns_now()
            with self.assertRaises(probe.DeadlineExceeded):
                probe.http_get(ep.path("models"),
                               headers={"Authorization": "Bearer dummy"},
                               endpoint=ep, dl=dl)
            elapsed = (probe.mono_ns_now() - start) / 1e9
            # The absolute deadline must abort near the bound (bounded per-read socket read),
            # not block for the full 4s server stall.
            self.assertLess(elapsed, 3.0)
        finally:
            httpd.shutdown()
            httpd.server_close()


class Pass11ModeAndGatingTests(ProbeTestBase):
    def test_baseline_mode_prompts_cap_plan(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session([probe.EXPECTED_MODEL_ID], FullRunTests.SSE)
            install(sess)
            captured = []

            def fake_run(kind, model_id, seq, dl, key, ep, prompt=None, max_tokens=None):
                captured.append((kind, prompt, max_tokens))
                return {"kind": kind, "seq": seq, "outcome": "completed",
                        "first_text_delta_s": 0.1, "total_s": 0.2,
                        "output_chars": 1, "output_sha256": "x", "usage": None}

            probe.run_one = fake_run
            prev = probe.PRIVATE_OUTPUT_DIR
            probe.PRIVATE_OUTPUT_DIR = tmp
            try:
                probe.main(["--baseline"])
            finally:
                probe.PRIVATE_OUTPUT_DIR = prev
                del probe.run_one
            files = sorted(pathlib.Path(tmp).rglob("thunder_local_pc_probe_result_*.json"))
            result = json.loads(files[-1].read_text())
            self.assertEqual(result["mode"], "baseline")
            self.assertEqual(len(captured), 10)
            shorts = [p for k, p, _ in captured if k == "short"]
            longs = [p for k, p, _ in captured if k == "long"]
            self.assertTrue(all(p == probe.BASELINE_SHORT_PROMPT for p in shorts))
            self.assertTrue(all(p == probe.BASELINE_LONG_PROMPT for p in longs))
            self.assertTrue(all(mt == 256 for _, _, mt in captured))
            self.assertEqual(result["summary"]["success_short"], 5)
            self.assertEqual(result["summary"]["success_long"], 5)

    def test_fatal_sse_error_stops_runs_yet_captures_post_inventory(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session([probe.EXPECTED_MODEL_ID], FullRunTests.SSE)
            install(sess)
            calls = {"n": 0}

            def fake_run(kind, model_id, seq, dl, key, ep, prompt=None, max_tokens=None):
                calls["n"] += 1
                if calls["n"] >= 3:
                    return {"kind": kind, "seq": seq, "outcome": "error",
                            "error_category": "engine_crash", "fatal": True}
                return {"kind": kind, "seq": seq, "outcome": "completed",
                        "first_text_delta_s": 0.1, "total_s": 0.2,
                        "output_chars": 1, "output_sha256": "x", "usage": None}

            probe.run_one = fake_run
            prev = probe.PRIVATE_OUTPUT_DIR
            probe.PRIVATE_OUTPUT_DIR = tmp
            try:
                probe.main([])
            finally:
                probe.PRIVATE_OUTPUT_DIR = prev
                del probe.run_one
            files = sorted(pathlib.Path(tmp).rglob("thunder_local_pc_probe_result_*.json"))
            result = json.loads(files[-1].read_text())
            self.assertEqual(result["summary"]["truncated_reason"], "fatal_engine_crash")
            self.assertLessEqual(calls["n"], 3)          # loop stopped
            self.assertIsNotNone(result["service"]["models_post"])   # post-inventory captured

    def test_empty_inventory_never_launches_inference(self):
        with tempfile.TemporaryDirectory() as tmp:
            sess = _public_session([], b"")
            transp, _ = install(sess)
            result = _run_in(tmp, _inside_wall())
            self.assertIs(result["service"]["identity_match"], False)
            self.assertIsNone(result["service"]["selected_model_id"])
            self.assertTrue(all(e[0] != "POST" for e in transp.calls))


if __name__ == "__main__":
    unittest.main()
