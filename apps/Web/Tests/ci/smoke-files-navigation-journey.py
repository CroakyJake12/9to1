#!/usr/bin/env python3
"""Read-only interactive Files CUI journey in actual published WebAssembly/Chrome.

This is a localhost synthetic provider acceptance harness, NOT Google OAuth or
live Drive acceptance. The real native accessibility bridge handles every click.
The sealed build on disk remains unmodified; script injection is HTTP-only.
"""
import argparse
from contextlib import contextmanager
import html
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from urllib.parse import parse_qs

HERE = Path(__file__).resolve().parent
BASE = HERE / 'smoke-connected-files-runtime.py'
spec = importlib.util.spec_from_file_location('files_connected_root', BASE)
connected = importlib.util.module_from_spec(spec)
spec.loader.exec_module(connected)

EVIDENCE_ID = 'files-navigation-smoke-evidence'
SCRIPT = HERE / 'files-connected-navigation-probe.js'
STEPS = [
    'select-folder', 'open-folder', 'inside-folder', 'up-to-root',
    'back-to-folder', 'forward-to-root', 'enter-search', 'search-results',
    'clear-search', 'restored-root',
]


def read_navigation(dom):
    """Never synthesize a successful navigation record when Chrome omits it."""
    match = re.search(r'<pre\b[^>]*\bid="' + EVIDENCE_ID + r'"[^>]*>(.*?)</pre>', dom, flags=re.S)
    if not match:
        return None
    try:
        result = json.loads(html.unescape(match.group(1)))
        return result if isinstance(result, dict) else None
    except (ValueError, TypeError):
        return None


@contextmanager
def interactive_fixture(root):
    """Keep all private HTTP state synthetic and restore original fixture hooks."""
    original_probe = connected.connected_probe
    original_response = connected.response_for
    requests = []
    driver = SCRIPT.read_text(encoding='utf-8')
    if '</script' in driver.lower() or 'https://' in driver or 'http://' in driver:
        raise ValueError('Injected fixture driver must not contain script-closing or network URLs')

    def probe():
        return original_probe() + '\n<script>\n' + driver + '\n</script>'

    def response(method, path, query):
        requests.append({'method': method, 'path': path,
                         'query': {key: values[:] for key, values in query.items()}})
        return original_response(method, path, query)

    connected.connected_probe = probe
    connected.response_for = response
    try:
        with connected.serve_connected(root) as (url, hits):
            yield url, hits, requests
    finally:
        connected.response_for = original_response
        connected.connected_probe = original_probe


def observed_request(requests, key=None, value=None):
    for request in requests:
        if request.get('method') != 'GET' or request.get('path') != '/api/files/v1/items':
            continue
        query = request.get('query')
        if not isinstance(query, dict):
            continue
        if key is None and not query:
            return True
        if key is not None and any(str(v).casefold() == str(value).casefold() for v in query.get(key, [])):
            return True
    return False


def accepted(native_probe, navigation, requests, exit_code):
    """Require native state changes and provider HTTP evidence, not a green job."""
    if exit_code != 0 or not connected.connected_visible(native_probe):
        return False
    if not isinstance(navigation, dict) or navigation.get('status') != 'PASS' or navigation.get('failure'):
        return False
    if navigation.get('completed') != STEPS or navigation.get('realGoogleDriveAcceptance') != 'NOT RUN':
        return False
    observations = navigation.get('observations')
    if not isinstance(observations, list) or len(observations) != len(STEPS):
        return False
    if [o.get('step') for o in observations if isinstance(o, dict)] != STEPS:
        return False
    if not all(isinstance(o.get('controls'), list) for o in observations if isinstance(o, dict)):
        return False
    nested = observations[2]['controls']
    filtered = observations[7]['controls']
    restored = observations[9]['controls']
    if not ('inside.txt' in nested and 'draft-notes.txt' not in nested
            and 'Project Atlas' in filtered and 'draft-notes.txt' not in filtered
            and 'Project Atlas' in restored and 'draft-notes.txt' in restored):
        return False
    if not isinstance(requests, list) or any(r.get('method') != 'GET' for r in requests):
        return False
    return observed_request(requests) and observed_request(requests, 'parentId', connected.FOLDER_ID) \
        and observed_request(requests, 'query', 'Atlas')


def run(root, chrome, budget_ms=45000, timeout=110):
    with tempfile.TemporaryDirectory(prefix='9to1-files-navigation-chrome-') as profile:
        with interactive_fixture(root) as (url, hits, requests):
            command = [chrome, '--headless=new', '--disable-gpu', '--disable-dev-shm-usage',
                       '--no-sandbox', '--no-first-run', '--no-default-browser-check',
                       f'--user-data-dir={profile}', f'--virtual-time-budget={budget_ms}',
                       '--dump-dom', url]
            try:
                process = subprocess.run(command, capture_output=True, text=True,
                                         errors='replace', timeout=timeout, check=False)
                native = connected.smoke.read_probe(process.stdout)
                journey = read_navigation(process.stdout)
                passed = accepted(native, journey, requests, process.returncode)
                return {
                    'status': 'PASS' if passed else 'FAIL',
                    'runner': 'actual sealed WebAssembly + native accessibility actions in Chrome',
                    'fixture': 'synthetic localhost connected read-only; no account/OAuth/Google',
                    'bootComplete': connected.smoke.boot_complete(native),
                    'connectedRootVisible': connected.connected_visible(native),
                    'journey': journey,
                    'providerRequests': requests[:40],
                    'resourceRequests': dict(hits),
                    'exitCode': process.returncode,
                    'chromeErrors': process.stderr[-3000:],
                    'publishedAssetsUntouched': True,
                    'realGoogleDriveAcceptance': 'NOT RUN',
                    'realAuthentication': 'NOT RUN',
                    'deployment': 'NOT RUN',
                }
            except subprocess.TimeoutExpired:
                return {'status': 'FAIL', 'error': 'Chrome timed out',
                        'fixture': 'synthetic connected read-only',
                        'providerRequests': requests[:40], 'resourceRequests': dict(hits),
                        'realGoogleDriveAcceptance': 'NOT RUN'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--wwwroot', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--chrome', default=None)
    parser.add_argument('--timeout-seconds', type=int, default=110)
    parser.add_argument('--virtual-time-budget-ms', type=int, default=45000)
    args = parser.parse_args()
    root = connected.smoke.verified_root(args.wwwroot)
    chrome = args.chrome or shutil.which('google-chrome') or shutil.which('chromium')
    result = run(root, chrome, args.virtual_time_budget_ms, args.timeout_seconds) if chrome else {
        'status': 'NOT_RUN', 'reason': 'Chrome unavailable', 'realGoogleDriveAcceptance': 'NOT RUN'}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'chromeErrors'}, indent=2))
    return 0 if result['status'] == 'PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
