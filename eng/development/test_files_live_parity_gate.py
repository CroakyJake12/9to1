"""Read-only localhost/stdlib acceptance for the existing Files deployment parity gate."""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest import mock
from urllib.error import URLError
import zipfile

import files_live_parity_gate as gate

SHA = 'a' * 40
ASSETS = {name: (b'/* public ' + name.encode() + b' */') for name in gate.ASSETS}


def make_candidate(path, assets=ASSETS):
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr('publish-manifest.json', json.dumps({'publishFiles': [
            {'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
            for name, data in assets.items()
        ]}))


@contextmanager
def local_fixture(assets=ASSETS, *, status=200, missing=(), overridden=None, content_type='application/javascript', encoding=None):
    calls = []
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            calls.append({'method': 'GET', 'path': self.path, 'cookie': self.headers.get('Cookie'),
                          'authorization': self.headers.get('Authorization'),
                          'accept_encoding': self.headers.get('Accept-Encoding')})
            assert self.path.startswith('/files/')
            name = self.path.removeprefix('/files/')
            if name in missing:
                self.send_response(404)
                self.end_headers()
                return
            self.send_response(status)
            self.send_header('Content-Type', content_type)
            self.send_header('X-Nine-Files-Version', '2026-11-15.1')
            if encoding:
                self.send_header('Content-Encoding', encoding)
            if status in (301, 302, 303, 307, 308):
                self.send_header('Location', 'https://example.invalid/never-follow')
            body = (overridden or {}).get(name, assets.get(name, b'no-image'))
            if status == 200:
                self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            if status == 200:
                try:
                    self.wfile.write(body)
                except (BrokenPipeError, ConnectionResetError):
                    pass
        def do_POST(self):
            calls.append({'method': 'POST'})
            self.send_error(405)
        def log_message(self, *args):
            pass
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield 'http://127.0.0.1:' + str(server.server_port), calls
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)


class ParityTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.artifact = Path(self.tmp.name, 'candidate.zip')
        make_candidate(self.artifact)
        # The 21 genuine candidate-gate tests run separately on the repository's
        # own release verifier. These tests isolate live HTTP parity semantics.
        patch = mock.patch.object(gate, 'verify', return_value={'status': 'PASS'})
        patch.start()
        self.addCleanup(patch.stop)

    def test_exact_public_scripts_match_and_never_leak_credentials(self):
        with local_fixture() as (origin, calls):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(result['status'], 'CURRENT_ASSETS_VERIFIED')
        self.assertTrue(result['sealedArtifactVerified'])
        self.assertFalse(result['readyToUseAsGoogleDrive'])
        self.assertEqual(result['googleOAuth'], 'NOT_TESTED')
        self.assertEqual([c['path'] for c in calls], ['/files/' + name for name in gate.ASSETS])
        for call in calls:
            self.assertIsNone(call['cookie'])
            self.assertIsNone(call['authorization'])
            self.assertEqual(call['accept_encoding'], 'identity')
        self.assertEqual({a['result'] for a in result['assets']}, {'MATCH'})
        self.assertEqual({a['workerVersion'] for a in result['assets']}, {'2026-11-15.1'})

    def test_stale_script_detected_with_actual_sha256(self):
        with local_fixture(overridden={'files-browser.js': b'old production bridge'}) as (origin, calls):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(result['status'], 'STALE_OR_MISSING')
        entry = next(a for a in result['assets'] if a['asset'] == 'files-browser.js')
        self.assertEqual(entry['result'], 'STALE_OR_MISMATCHED')
        self.assertEqual(entry['observedSha256'], hashlib.sha256(b'old production bridge').hexdigest())
        self.assertEqual(len(calls), 3)

    def test_missing_asset_detected(self):
        with local_fixture(missing=['main.js']) as (origin, _):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(result['status'], 'STALE_OR_MISSING')
        self.assertEqual(result['assets'][0]['result'], 'NOT_FOUND')

    def test_access_redirect_is_not_followed(self):
        with local_fixture(status=302) as (origin, calls):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(result['status'], 'ACCESS_GATED_UNVERIFIABLE')
        self.assertEqual(len(calls), 3, 'no second network request to redirect destination')
        self.assertTrue(all(a['result'] == 'ACCESS_GATE_OR_REDIRECT' for a in result['assets']))

    def test_unauthorized_is_not_classified_as_stale(self):
        for status in (401, 403):
            with self.subTest(status=status):
                with local_fixture(status=status) as (origin, calls):
                    result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
                self.assertEqual(result['status'], 'ACCESS_GATED_UNVERIFIABLE')
                self.assertEqual(len(calls), 3)

    def test_unexpected_html_or_content_encoding_fails_closed(self):
        for kwargs in ({'content_type': 'text/html'}, {'encoding': 'gzip'}):
            with self.subTest(kwargs=kwargs):
                with local_fixture(**kwargs) as (origin, _):
                    result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
                self.assertEqual(result['status'], 'UNVERIFIABLE')
                self.assertTrue(all(a['result'] == 'UNEXPECTED_CONTENT_TYPE_OR_ENCODING' for a in result['assets']))

    def test_body_larger_than_bound_is_rejected(self):
        huge = b'Z' * (gate.MAX_ASSET + 1)
        with local_fixture(overridden={'files-browser.js': huge}) as (origin, _):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(result['status'], 'UNVERIFIABLE')
        bad = next(a for a in result['assets'] if a['asset'] == 'files-browser.js')
        self.assertEqual(bad['result'], 'OVERSIZED_RESPONSE')
        self.assertIsNone(bad['observedSha256'])

    def test_network_failures_never_accidentally_pass(self):
        class Offline:
            def open(self, *_args, **_kwargs):
                raise URLError('offline')
        with local_fixture() as (origin, calls):
            result = gate.inspect(self.artifact, SHA, origin, allow_loopback=True, opener=Offline())
        self.assertEqual(result['status'], 'UNVERIFIABLE')
        self.assertEqual(calls, [])

    def test_unapproved_endpoints_and_credential_urls_refused_before_network(self):
        disallowed = ['https://evil.example', 'http://9to1.uk', 'https://9to1.uk@evil.example',
                      'https://9to1.uk/files/', 'https://9to1.uk?access_token=bad',
                      'http://192.168.1.1:8080', 'http://localhost:8080', 'file:///etc/passwd']
        for origin in disallowed:
            with self.subTest(origin=origin):
                with self.assertRaises(gate.ParityError):
                    gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        with self.assertRaises(gate.ParityError):
            gate.inspect(self.artifact, SHA, 'http://127.0.0.1:8080', allow_loopback=False)

    def test_invalid_candidate_refused_before_http(self):
        gate.verify.side_effect = gate.RejectedCandidate('bad sealed package')
        with local_fixture() as (origin, calls):
            with self.assertRaises(gate.ParityError):
                gate.inspect(self.artifact, SHA, origin, allow_loopback=True)
        self.assertEqual(calls, [])

    def test_missing_manifest_path_and_oversized_claim_are_refused(self):
        for payload in ({'main.js': b'ok'}, {**ASSETS, 'files-browser.js': b'a' * (gate.MAX_ASSET + 1)}):
            make_candidate(self.artifact, payload)
            with self.assertRaises(gate.ParityError):
                gate.sealed_asset_rows(self.artifact, SHA)

    def test_invalid_timeouts_are_refused(self):
        for timeout in (0, -1, 31, 1.5, '5'):
            with self.assertRaises(gate.ParityError):
                gate.inspect(self.artifact, SHA, gate.PRODUCTION_ORIGIN, timeout=timeout)

    def test_restricted_network_is_not_a_production_probe(self):
        with self.assertRaises(gate.ParityError):
            gate.assert_origin('http://127.0.0.1:1234')
        self.assertEqual(gate.assert_origin('https://9to1.uk/'), 'https://9to1.uk')


if __name__ == '__main__':
    unittest.main()
