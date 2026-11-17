#!/usr/bin/env python3
"""Optional genuine Chrome boot probe against an *unmodified* sealed Web wwwroot.

This is a disposable loopback fixture, not a Google Drive authentication or
provider acceptance test. Never forwards test requests to Google or 9to1.uk.
"""
import argparse
from collections import Counter
from contextlib import contextmanager
import html
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import mimetypes
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from threading import Thread
from urllib.parse import urlsplit

REQUIRED = ('index.html', 'main.js', '_framework/dotnet.js')
SNAPSHOT_ID = 'files-runtime-smoke-evidence'
# Test-only instrumentation on the served HTML response. Published bytes on disk
# (including the actual .NET/Avalonia modules) remain completely unchanged.
PROBE_KEY = '9to1.files.runtime-smoke.v1'
PROBE = f'''<script>
(() => {{
    // This instrumentation is injected in the disposable HTTP response only;
    // neither the sealed assets nor the production session code are modified.
    const evidence = document.createElement('pre');
    evidence.id = '{SNAPSHOT_ID}';
    evidence.hidden = true;
    document.body.append(evidence);
    const failures = [];
    function record() {{
        const status = document.querySelector('#browser-status');
        const snapshot = {{
            markers: performance.getEntriesByType('mark').filter(x => x.name.startsWith('9to1:')).map(x => x.name),
            hash: location.hash,
            status: status?.dataset.code ?? null,
            message: status?.textContent?.slice(0, 250) ?? null,
            errors: failures.slice(0, 8),
            rootExists: !!document.querySelector('#nine-to-one-root')
        }};
        const encoded = JSON.stringify(snapshot);
        evidence.textContent = encoded;
        try {{ sessionStorage.setItem('{PROBE_KEY}', encoded); }} catch {{ /* fail closed: no fake evidence */ }}
    }}
    window.addEventListener('error', e => {{ failures.push(String(e.message || 'script resource error')); record(); }});
    window.addEventListener('unhandledrejection', e => {{ failures.push(String(e.reason)); record(); }});
    // Preserve the last *observed* markers before an actual 401-driven
    // sign-out tears down this document. No marker is ever synthesized.
    window.addEventListener('pagehide', record);
    new PerformanceObserver(record).observe({{ type: 'mark', buffered: true }});
    setInterval(record, 200);
    record();
}})();
</script>'''

# The genuine app can end its anonymous boot by navigating to /account.
# Preserve its *previously observed* evidence on that exact test-only landing.
# This is not an account page or an authenticated session simulation.
ACCOUNT_LANDING = f'''<!doctype html><html><head><meta charset="utf-8">
<title>Files browser smoke: anonymous redirect</title></head><body>
<p>Anonymous test fixture: no account authenticated.</p>
<pre id="{SNAPSHOT_ID}"></pre>
<script>
(() => {{
    let prior = null;
    try {{ prior = JSON.parse(sessionStorage.getItem('{PROBE_KEY}') || 'null'); }} catch {{}}
    const observed = prior && typeof prior === 'object' && !Array.isArray(prior) ? prior : {{}};
    const evidence = {{ ...observed, redirectObserved: true, landedAt: location.pathname,
        markers: Array.isArray(observed.markers) ? observed.markers : [] }};
    document.getElementById('{SNAPSHOT_ID}').textContent = JSON.stringify(evidence);
}})();
</script></body></html>'''



def verified_root(path):
    root = Path(path).resolve(strict=True)
    if not root.is_dir():
        raise ValueError('Published root must be an existing directory')
    for name in REQUIRED:
        file = root / name
        if not file.is_file() or file.is_symlink():
            raise ValueError('Actual sealed publication is incomplete: ' + name)
    return root


def make_handler(root, hits):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_args):
            pass

        def do_GET(self):
            self._serve()

        def do_HEAD(self):
            self._serve(send_body=False)

        def _serve(self, send_body=True):
            path = urlsplit(self.path).path
            hits[path] += 1
            if path.startswith('/api/files/v1/'):
                # Never invent a successful authenticated owner. The real
                # Files feature must reject this anonymous fixture.
                content = b'{"error":{"code":"SignInRequired","message":"No test owner session"}}'
                self.send_response(401)
                self.send_header('Content-Type', 'application/json')
            elif path == '/account':
                content = ACCOUNT_LANDING.encode('utf-8')
                self.send_response(200)
                self.send_header('Content-Type', 'text/html; charset=utf-8')
            elif path.startswith('/files/') and '%' not in path and '\\' not in path and '//' not in path:
                name = path[len('/files/'):]
                if not name or name == 'index.html':
                    name = 'index.html'
                parts = Path(name).parts
                if any(part in ('.', '..') or part.startswith('.') for part in parts):
                    self.send_error(404)
                    return
                file = (root / name).resolve()
                if not file.is_relative_to(root) or not file.is_file() or file.is_symlink():
                    self.send_error(404)
                    return
                content = file.read_bytes()
                if name == 'index.html':
                    document = content.decode('utf-8')
                    if '</body>' not in document:
                        self.send_error(500)
                        return
                    content = document.replace('</body>', PROBE + '\n</body>').encode('utf-8')
                mime = {
                    '.wasm': 'application/wasm', '.js': 'text/javascript',
                    '.mjs': 'text/javascript', '.json': 'application/json',
                    '.css': 'text/css', '.html': 'text/html',
                }.get(file.suffix, mimetypes.guess_type(name)[0] or 'application/octet-stream')
                self.send_response(200)
                self.send_header('Content-Type', mime + ('; charset=utf-8' if mime.startswith('text/') else ''))
            else:
                self.send_error(404)
                return
            self.send_header('Content-Length', str(len(content)))
            self.send_header('Cache-Control', 'no-store')
            self.end_headers()
            if send_body:
                try:
                    self.wfile.write(content)
                except (BrokenPipeError, ConnectionResetError):
                    pass
    return Handler


@contextmanager
def serve(root):
    hits = Counter()
    server = ThreadingHTTPServer(('127.0.0.1', 0), make_handler(root, hits))
    worker = Thread(target=server.serve_forever, daemon=True)
    worker.start()
    try:
        yield f'http://127.0.0.1:{server.server_port}/files/', hits
    finally:
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)


def read_probe(dom):
    match = re.search(r'<pre\b[^>]*\bid="files-runtime-smoke-evidence"[^>]*>(.*?)</pre>', dom, flags=re.S)
    if not match:
        return None
    try:
        return json.loads(html.unescape(match.group(1)))
    except ValueError:
        return None


def boot_complete(probe):
    if not isinstance(probe, dict):
        return False
    markers = probe.get('markers', [])
    return isinstance(markers, list) and {'9to1:runtime:ready', '9to1:backend:ready'}.issubset(markers) \
        and not probe.get('errors') and probe.get('status') != 'BrowserRuntimeUnavailable'


def run(root, chrome, budget_ms, timeout):
    with tempfile.TemporaryDirectory(prefix='9to1-chrome-profile-') as profile:
        with serve(root) as (url, hits):
            command = [chrome, '--headless=new', '--disable-gpu', '--disable-dev-shm-usage',
                       '--no-sandbox', '--no-first-run', '--no-default-browser-check',
                       f'--user-data-dir={profile}', f'--virtual-time-budget={budget_ms}',
                       '--dump-dom', url]
            try:
                process = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                         text=True, errors='replace', timeout=timeout, check=False)
                probe = read_probe(process.stdout)
                markers = probe.get('markers', []) if isinstance(probe, dict) else []
                runtime_ready = '9to1:runtime:ready' in markers
                backend_ready = '9to1:backend:ready' in markers
                # Explicitly distinguish boot evidence from the authenticated
                # Files user journey, which must remain unverified here.
                record = {'runner': 'real headless Chromium against actual published wwwroot',
                          'auth': 'unauthenticated disposable 401 fixture',
                          'runtimeReady': runtime_ready, 'backendReady': backend_ready,
                          'bootComplete': boot_complete(probe),
                          'anonymousRedirect': bool(probe and probe.get('redirectObserved')),
                          'exitCode': process.returncode,
                          'probe': probe, 'resourceRequests': dict(hits),
                          'chromeErrors': process.stderr[-3000:],
                          'publishedAssetsUntouched': True,
                          'googleDriveAcceptance': 'NOT RUN'}
                return record
            except subprocess.TimeoutExpired:
                return {'runtimeReady': False, 'error': 'Chrome timed out',
                        'timeoutSeconds': timeout, 'resourceRequests': dict(hits),
                        'googleDriveAcceptance': 'NOT RUN'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--wwwroot', type=Path, required=True)
    ap.add_argument('--report', type=Path, required=True)
    ap.add_argument('--chrome', default=None)
    ap.add_argument('--timeout-seconds', type=int, default=110)
    ap.add_argument('--virtual-time-budget-ms', type=int, default=45000)
    args = ap.parse_args()
    root = verified_root(args.wwwroot)
    chrome = args.chrome or shutil.which('google-chrome') or shutil.which('chromium')
    if not chrome:
        result = {'status': 'NOT_RUN', 'reason': 'Chrome executable absent',
                  'googleDriveAcceptance': 'NOT RUN'}
    else:
        result = run(root, chrome, args.virtual_time_budget_ms, args.timeout_seconds)
        result['status'] = 'PASS' if result.get('bootComplete') and result.get('exitCode') == 0 else 'FAIL'
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'chromeErrors'}, indent=2))
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
