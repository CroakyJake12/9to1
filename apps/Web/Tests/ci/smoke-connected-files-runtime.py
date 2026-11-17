#!/usr/bin/env python3
"""Read-only connected Files UI witness against the actual sealed WebAssembly app.

This exclusively uses a synthetic localhost Files API, no session cookies, Google
API calls, OAuth or account impersonation. Do not report it as live Drive acceptance.
"""
import argparse
from collections import Counter
from contextlib import contextmanager
import importlib.util
import json
from http.server import ThreadingHTTPServer
from pathlib import Path
import shutil
import subprocess
import tempfile
from threading import Thread
from urllib.parse import parse_qs, urlsplit

spec = importlib.util.spec_from_file_location('files_public_smoke', Path(__file__).parent / 'smoke-public-files-runtime.py')
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)

LOCATION_ID = '45c45608-231a-42bf-89b6-eec6b9ec2007'
FOLDER_ID = '603084e6-72df-4f4e-bd73-b8ee96e34d25'
FILE_ID = 'dcbddc42-3d6d-4f3f-b472-6d0c36267533'
FOLDER_NAME = 'Project Atlas'
FILE_NAME = 'draft-notes.txt'
OWNER = 'disposable-connected-ui-only'


def item(item_id, name, kind, revision, parent_id=None):
    return {
        'id': {'value': item_id}, 'locationId': {'value': LOCATION_ID},
        'parentId': {'value': parent_id} if parent_id else None,
        'name': name, 'kind': kind,
        'contentType': 'application/vnd.google-apps.folder' if kind == 'Folder' else 'text/plain',
        'ownerPrincipalId': OWNER, 'scope': 'connected-provider',
        'sizeBytes': None if kind == 'Folder' else 42,
        'createdAt': '2026-11-01T12:00:00Z', 'modifiedAt': '2026-11-02T12:00:00Z',
        'currentRevisionId': {'value': revision}, 'availability': 'CloudOnly',
        'isShared': False, 'contentHash': None, 'metadataSchemaVersion': 1,
    }


FOLDER = item(FOLDER_ID, FOLDER_NAME, 'Folder', '79cfea2a-3294-4fbd-9821-ed37bf6032aa')
DOCUMENT = item(FILE_ID, FILE_NAME, 'File', 'bce378e6-303b-4695-bc7b-3ab4b0e5db53')
CONTEXT = {
    'accountId': OWNER, 'displayName': 'Synthetic connected fixture',
    'connected': True, 'configurationRequired': False, 'usage': None,
    'location': {**smoke.DISCONNECTED_CONTEXT['location'],
                 'capabilities': 'Read, Write, Rename, Move, Trash, Search, Streaming, ResumableTransfers'},
}


def response_for(method, path, query):
    """Only explicit read routes exist, with real canonical metadata shapes."""
    if method != 'GET':
        return 405, {'code': 'ReadOnlyFixture', 'message': 'Mutations are never accepted'}
    if path == '/api/files/v1/context':
        return 200, CONTEXT
    if path == '/api/files/v1/usage':
        return 403, {'code': 'ReadOnlyFixture', 'message': 'No usage ledger exists'}
    if path == '/api/files/v1/items':
        if any(key not in ('parentId', 'query', 'pageToken') for key in query):
            return 400, {'code': 'InvalidFixtureQuery'}
        if query.get('pageToken'):
            return 400, {'code': 'InvalidFixturePage'}
        parent = query.get('parentId', [None])[0]
        if parent is None:
            rows = [FOLDER, DOCUMENT]
        elif parent == FOLDER_ID:
            rows = [item('bad44bb0-6f50-41c3-8a7d-f3c17929e89e', 'inside.txt', 'File',
                         '49bcfa9e-ec65-43f7-a91b-a03e9421bdf9', FOLDER_ID)]
        else:
            return 404, {'code': 'ItemNotFound'}
        name = query.get('query', [''])[0].casefold()
        return 200, {'items': [row for row in rows if name in row['name'].casefold()], 'nextPageToken': None}
    if path == f'/api/files/v1/items/{FOLDER_ID}':
        return 200, {'value': FOLDER, 'error': None}
    if path == f'/api/files/v1/items/{FILE_ID}':
        return 200, {'value': DOCUMENT, 'error': None}
    return 403, {'code': 'ReadOnlyFixture', 'message': 'No account or provider mutation is available'}


def connected_probe():
    """Extend the existing real-DOM witness without inventing a CUI peer."""
    original = "'Disconnect Google Drive'].includes(name)"
    if original not in smoke.PROBE:
        raise RuntimeError('The shared Files CUI probe changed; review its actual peers first')
    return smoke.PROBE.replace(original,
                               "'Disconnect Google Drive', 'Project Atlas', 'draft-notes.txt'].includes(name)")


@contextmanager
def serve_connected(root):
    root = smoke.verified_root(root)
    hits = Counter()
    base = smoke.make_handler(root, hits, 'disconnected')

    class Handler(base):
        def do_POST(self):
            hits[urlsplit(self.path).path] += 1
            self.send_error(405)

        def do_PUT(self):
            hits[urlsplit(self.path).path] += 1
            self.send_error(405)

        def do_DELETE(self):
            hits[urlsplit(self.path).path] += 1
            self.send_error(405)

        def _serve(self, send_body=True):
            url = urlsplit(self.path)
            if url.path.startswith('/api/files/v1/'):
                hits[url.path] += 1
                status, payload = response_for(self.command, url.path, parse_qs(url.query, keep_blank_values=True))
                data = json.dumps(payload).encode('utf-8')
                self.send_response(status)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(data)))
                self.send_header('Cache-Control', 'no-store')
                self.end_headers()
                if send_body:
                    self.wfile.write(data)
                return
            super()._serve(send_body)

    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    worker = Thread(target=server.serve_forever, daemon=True)
    original_probe = smoke.PROBE
    # The local handler injects the probe only into the HTTP response. Disk
    # bytes of wwwroot (including all assemblies) remain completely untouched.
    smoke.PROBE = connected_probe()
    worker.start()
    try:
        yield f'http://127.0.0.1:{server.server_port}/files/', hits
    finally:
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)
        smoke.PROBE = original_probe


def connected_visible(probe):
    if not smoke.boot_complete(probe) or probe.get('status') != 'Ready' or not probe.get('rootExists') or probe.get('hash') != '#/app.files':
        return False
    peers = probe.get('filesControls')
    return isinstance(peers, list) and all(name in peers for name in
        ('Disconnect Google Drive', 'Files privacy', FOLDER_NAME, FILE_NAME)) and 'Connect Google Drive' not in peers


def run(root, chrome, budget_ms=45000, timeout=110):
    with tempfile.TemporaryDirectory(prefix='9to1-connected-files-chrome-') as profile:
        with serve_connected(root) as (url, hits):
            command = [chrome, '--headless=new', '--disable-gpu', '--disable-dev-shm-usage',
                       '--no-sandbox', '--no-first-run', '--no-default-browser-check',
                       f'--user-data-dir={profile}', f'--virtual-time-budget={budget_ms}',
                       '--dump-dom', url]
            try:
                process = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                         text=True, errors='replace', timeout=timeout, check=False)
                probe = smoke.read_probe(process.stdout)
                success = connected_visible(probe) and hits['/api/files/v1/context'] >= 1 \
                    and hits['/api/files/v1/items'] >= 1 and process.returncode == 0
                return {
                    'status': 'PASS' if success else 'FAIL',
                    'runner': 'actual sealed WebAssembly/Avalonia/CUI via disposable Chrome',
                    'fixture': 'synthetic connected read-only owner; no actual account, OAuth or Google API',
                    'bootComplete': smoke.boot_complete(probe), 'filesConnectedRootVisible': connected_visible(probe),
                    'probe': probe, 'resourceRequests': dict(hits), 'exitCode': process.returncode,
                    'chromeErrors': process.stderr[-2500:], 'publishedAssetsUntouched': True,
                    'realGoogleDriveAcceptance': 'NOT RUN', 'realAuthentication': 'NOT RUN',
                    'deployment': 'NOT RUN',
                }
            except subprocess.TimeoutExpired:
                return {'status': 'FAIL', 'error': 'Chrome timed out', 'fixture': 'connected-synthetic',
                        'resourceRequests': dict(hits), 'realGoogleDriveAcceptance': 'NOT RUN'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--wwwroot', type=Path, required=True)
    ap.add_argument('--report', type=Path, required=True)
    ap.add_argument('--chrome', default=None)
    ap.add_argument('--timeout-seconds', type=int, default=110)
    ap.add_argument('--virtual-time-budget-ms', type=int, default=45000)
    args = ap.parse_args()
    root = smoke.verified_root(args.wwwroot)
    chrome = args.chrome or shutil.which('google-chrome') or shutil.which('chromium')
    if not chrome:
        result = {'status': 'NOT_RUN', 'reason': 'Chrome executable absent', 'realGoogleDriveAcceptance': 'NOT RUN'}
    else:
        result = run(root, chrome, args.virtual_time_budget_ms, args.timeout_seconds)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: v for k, v in result.items() if k != 'chromeErrors'}, indent=2))
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
