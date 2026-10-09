"""Acquire an official, exact-revision Chromium snapshot; never substitute another build.
This is an upstream-built binary. It does not claim local DEPS sync or compilation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import urllib.request
import zipfile

COMMIT = 'e23cdf4ab7b386d7e5fc58506b2d77f3f4a4817d'
POSITION = '1715423'


def acquire(output: Path, evidence: Path) -> None:
    system = platform.system()
    if system not in ('Linux', 'Windows') or platform.machine().lower() not in ('x86_64', 'amd64'):
        raise RuntimeError('This pinned snapshot probe supports x64 Windows/Linux only.')
    bucket, archive, executable = ('Linux_x64', 'chrome-linux.zip', 'chrome-linux/chrome') if system == 'Linux' else ('Win_x64', 'chrome-win.zip', 'chrome-win/chrome.exe')
    base = f'https://storage.googleapis.com/chromium-browser-snapshots/{bucket}/{POSITION}/'
    record = {'source_commit': COMMIT, 'commit_position': POSITION, 'platform': bucket, 'binary_url': base + archive, 'acquisition': 'not completed', 'dependency_checkout': 'not run', 'local_native_build': 'not run', 'runtime_revision_check': 'not run', 'visual_browse_integration': 'not run'}
    evidence.parent.mkdir(parents=True, exist_ok=True)
    try:
        output.mkdir(parents=True, exist_ok=True)
        with urllib.request.urlopen(base + 'REVISIONS', timeout=60) as response:
            revisions = json.load(response)
        record['upstream_revisions'] = revisions
        if revisions.get('got_revision') != COMMIT or revisions.get('chromium_revision') != POSITION:
            raise RuntimeError('Snapshot metadata does not match the acquired Chromium donor revision.')
        archive_path = output / archive
        with urllib.request.urlopen(base + archive, timeout=120) as response, archive_path.open('wb') as target:
            shutil.copyfileobj(response, target)
        record['archive_sha256'] = hashlib.file_digest(archive_path.open('rb'), 'sha256').hexdigest()
        with zipfile.ZipFile(archive_path) as z:
            for item in z.infolist():
                target = (output / item.filename).resolve()
                if not target.is_relative_to(output.resolve()):
                    raise RuntimeError('Unsafe path in upstream snapshot archive.')
                z.extract(item, output)
                mode = (item.external_attr >> 16) & 0o777
                if mode and not item.is_dir():
                    target.chmod(mode)
        binary = output / executable
        if not binary.is_file():
            raise RuntimeError('Upstream archive did not contain the expected executable.')
        record['executable_sha256'] = hashlib.file_digest(binary.open('rb'), 'sha256').hexdigest()
        record['executable'] = str(binary.resolve())
        record['acquisition'] = 'succeeded: upstream snapshot, not a local source build'
        if 'GITHUB_ENV' in os.environ:
            with open(os.environ['GITHUB_ENV'], 'a', encoding='utf-8') as env:
                env.write(f'BROWSE_CHROMIUM_EXE={binary.resolve()}\nBROWSE_CHROMIUM_SOURCE_SHA={COMMIT}\nBROWSE_CHROMIUM_EXE_SHA256={record["executable_sha256"]}\n')
    except Exception as exc:
        record['error'] = f'{type(exc).__name__}: {exc}'
        raise
    finally:
        evidence.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(record, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    args = parser.parse_args()
    acquire(args.output, args.evidence)
