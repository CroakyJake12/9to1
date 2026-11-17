#!/usr/bin/env python3
"""Read-only proof that live Files JavaScript assets match a sealed local candidate.

This tool NEVER deploys, sends cookies, follows HTTP redirects, or checks Google
Drive. It validates the candidate through the existing offline release gate
before any network request, then GETs three bounded public browser scripts.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib import request, error, parse
import zipfile

from files_candidate_release_gate import verify, RejectedCandidate

PRODUCTION_ORIGIN = 'https://9to1.uk'
ASSETS = ('main.js', 'files-browser.js', 'browser-files-session.js')
MAX_ASSET = 2 * 1024 * 1024
MAX_REPORT = 1024 * 1024
DEFAULT_TIMEOUT = 10
SHA = re.compile(r'[a-f0-9]{64}\Z')
VERSION = re.compile(r'[A-Za-z0-9._-]{1,64}\Z')
ALLOWED_JS = {'application/javascript', 'text/javascript', 'application/x-javascript'}


class ParityError(ValueError):
    pass


def assert_origin(origin: str, *, allow_loopback: bool = False) -> str:
    try:
        value = parse.urlsplit(origin)
        if value.username or value.password or value.query or value.fragment or value.path not in ('', '/'):  # no bearer URL
            raise ParityError('Origin must not contain credentials, path, query or fragment')
        if origin.rstrip('/') == PRODUCTION_ORIGIN:
            return PRODUCTION_ORIGIN
        if allow_loopback and value.scheme == 'http' and value.hostname in ('127.0.0.1', '::1') and value.port in range(1, 65536):
            return origin.rstrip('/')
    except ValueError as exc:
        raise ParityError('Invalid or unapproved origin') from exc
    raise ParityError('Only the fixed https://9to1.uk origin is supported outside explicit local tests')


def sealed_asset_rows(archive: Path, commit: str) -> dict[str, tuple[int, str]]:
    # The owning verifier checks source/manifest custody and hashes of every
    # extracted member; never trust self-reported manifest rows in isolation.
    try:
        verify(archive, commit)
        with zipfile.ZipFile(archive) as outer:
            manifest_bytes = outer.read('publish-manifest.json')
        if len(manifest_bytes) > MAX_REPORT:
            raise ParityError('Candidate manifest exceeds parity report bound')
        manifest = json.loads(manifest_bytes)
        rows = {entry['path']: entry for entry in manifest['publishFiles']}
        expected = {}
        for name in ASSETS:
            row = rows.get(name)
            if not isinstance(row, dict) or type(row.get('bytes')) is not int or not (0 < row['bytes'] <= MAX_ASSET) or not SHA.fullmatch(row.get('sha256', '')):
                raise ParityError('Required public Files JavaScript asset missing or unbounded: ' + name)
            expected[name] = (row['bytes'], row['sha256'])
        return expected
    except (RejectedCandidate, OSError, zipfile.BadZipFile, KeyError, TypeError, ValueError) as exc:
        if isinstance(exc, ParityError):
            raise
        raise ParityError('Sealed candidate could not be independently verified: ' + type(exc).__name__) from exc


class RefuseRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, request_, fp, code, msg, headers, newurl):
        return None


def readonly_opener():
    # No environment proxy redirects, persisted cookies, authentication
    # handlers, credential stores or implicit redirect requests.
    return request.build_opener(request.ProxyHandler({}), RefuseRedirect())


def safe_version(headers) -> str | None:
    value = headers.get('x-nine-files-version')
    return value if isinstance(value, str) and VERSION.fullmatch(value) else None


def check_asset(opener, origin: str, asset: str, size: int, expected_sha: str, *, timeout: int) -> dict:
    uri = origin + '/files/' + asset
    req = request.Request(uri, method='GET', headers={
        'User-Agent': '9to1-files-release-parity/1.0',
        'Accept': 'application/javascript',
        'Accept-Encoding': 'identity',
        'Cache-Control': 'no-cache',
    })
    row = {'asset': asset, 'expectedBytes': size, 'expectedSha256': expected_sha,
           'httpStatus': None, 'observedBytes': None, 'observedSha256': None,
           'result': 'UNVERIFIED', 'workerVersion': None}
    try:
        with opener.open(req, timeout=timeout) as response:
            row['httpStatus'] = response.status
            row['workerVersion'] = safe_version(response.headers)
            if response.status != 200:
                row['result'] = 'HTTP_ERROR'
                return row
            encoding = response.headers.get('content-encoding', 'identity').strip().lower()
            media_type = response.headers.get('content-type', '').split(';', 1)[0].strip().lower()
            if encoding != 'identity' or media_type not in ALLOWED_JS:
                row['result'] = 'UNEXPECTED_CONTENT_TYPE_OR_ENCODING'
                return row
            length = response.headers.get('content-length')
            if length is not None and (not length.isdecimal() or int(length) > MAX_ASSET):
                row['result'] = 'OVERSIZED_RESPONSE'
                return row
            # Never download or hash an unbounded response. Reading one excess
            # byte detects a server that lies about Content-Length.
            body = response.read(MAX_ASSET + 1)
            row['observedBytes'] = len(body)
            if len(body) > MAX_ASSET:
                row['result'] = 'OVERSIZED_RESPONSE'
                return row
            row['observedSha256'] = hashlib.sha256(body).hexdigest()
            row['result'] = 'MATCH' if len(body) == size and row['observedSha256'] == expected_sha else 'STALE_OR_MISMATCHED'
    except error.HTTPError as exc:
        row['httpStatus'] = exc.code
        row['workerVersion'] = safe_version(exc.headers)
        if exc.code in (401, 403, 302, 303, 307, 308):
            row['result'] = 'ACCESS_GATE_OR_REDIRECT'
        elif exc.code == 404:
            row['result'] = 'NOT_FOUND'
        else:
            row['result'] = 'HTTP_ERROR'
        exc.close()
    except (error.URLError, OSError, TimeoutError):
        row['result'] = 'NETWORK_UNAVAILABLE'
    return row


def inspect(archive: Path, commit: str, origin: str, *, allow_loopback=False, timeout=DEFAULT_TIMEOUT, opener=None) -> dict:
    if not isinstance(timeout, int) or not 1 <= timeout <= 30:
        raise ParityError('HTTP timeout must be an integer between 1 and 30 seconds')
    checked_origin = assert_origin(origin, allow_loopback=allow_loopback)
    expected = sealed_asset_rows(archive, commit)
    opener = readonly_opener() if opener is None else opener
    rows = [check_asset(opener, checked_origin, name, size, digest, timeout=timeout)
            for name, (size, digest) in expected.items()]
    outcomes = {r['result'] for r in rows}
    if outcomes == {'MATCH'}:
        status = 'CURRENT_ASSETS_VERIFIED'
    elif 'STALE_OR_MISMATCHED' in outcomes or 'NOT_FOUND' in outcomes:
        status = 'STALE_OR_MISSING'
    elif 'ACCESS_GATE_OR_REDIRECT' in outcomes:
        status = 'ACCESS_GATED_UNVERIFIABLE'
    else:
        status = 'UNVERIFIABLE'
    return {
        'status': status, 'candidateSourceCommit': commit,
        'origin': checked_origin, 'assets': rows,
        'sealedArtifactVerified': True,
        'publicAssetsMatched': status == 'CURRENT_ASSETS_VERIFIED',
        'filesAppAuthenticated': 'NOT_TESTED', 'googleOAuth': 'NOT_TESTED',
        'liveGoogleDrive': 'NOT_TESTED', 'deploymentPerformed': False,
        'readyToUseAsGoogleDrive': False,
    }


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifact', required=True, type=Path, help='Sealed GitHub Actions files-web-candidate.zip')
    parser.add_argument('--expected-commit', required=True, help='Exact 40-hex source revision in the seal')
    parser.add_argument('--origin', default=PRODUCTION_ORIGIN)
    parser.add_argument('--timeout', type=int, default=DEFAULT_TIMEOUT)
    parser.add_argument('--allow-loopback-fixture', action='store_true', help='Test-only HTTP on 127.0.0.1 or ::1')
    args = parser.parse_args(argv)
    try:
        result = inspect(args.artifact, args.expected_commit, args.origin,
                         allow_loopback=args.allow_loopback_fixture, timeout=args.timeout)
    except ParityError as exc:
        print(json.dumps({'status': 'INVALID_CANDIDATE_OR_TARGET', 'reason': str(exc),
                          'deploymentPerformed': False, 'readyToUseAsGoogleDrive': False}), file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0 if result['status'] == 'CURRENT_ASSETS_VERIFIED' else 2


if __name__ == '__main__':
    raise SystemExit(main())
