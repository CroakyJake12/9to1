#!/usr/bin/env python3
"""Acquire pinned donor source, preserving every tracked blob. Not a build test."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys

HERE = Path(__file__).resolve().parent
LOCK = HERE / 'remaining-sources.lock.json'
ROOT = HERE.parents[1]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def git(root: Path, *args: str, timeout: int = 180) -> str:
    result = subprocess.run(['git', '-C', str(root), *args], check=True,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            timeout=timeout)
    return result.stdout.decode('utf-8', 'surrogateescape')


def load_lock(path: Path = LOCK) -> list[dict]:
    lock = json.loads(path.read_text(encoding='utf-8'))
    require(lock.get('schema') == 1, 'Unsupported source lock schema')
    donors = lock['donors']
    ids, paths = set(), set()
    require(bool(donors), 'Empty source lock')
    for donor in donors:
        name = donor['id']
        require(bool(re.fullmatch(r'[a-z0-9][a-z0-9.-]*', name)), 'Invalid donor ID')
        require(name not in ids, f'Duplicate donor: {name}')
        ids.add(name)
        require(bool(re.fullmatch(r'[0-9a-f]{40}', donor['commit'])), 'Invalid source pin')
        require(bool(re.fullmatch(r'https://github\.com/CroakyJake12/[A-Za-z0-9_.-]+\.git', donor['url'])), 'Unexpected donor URL')
        require(bool(donor['paths']), 'Donor has no target path')
        for text in donor['paths']:
            path = PurePosixPath(text)
            require(not path.is_absolute() and '..' not in path.parts and '\\' not in text,
                    'Unsafe donor path')
            require(text.startswith(('9to1 OS/', '9to1 Workspace/')), 'Donor path outside app roots')
            require(text not in paths, f'Duplicate source path: {text}')
            paths.add(text)
    return donors


def check_parent(root: Path, donor: dict) -> None:
    for text in donor['paths']:
        require((root / text).resolve().is_relative_to(root.resolve()), 'Source path escapes root')
        entry = git(root, 'ls-tree', '-z', 'HEAD', '--', text).rstrip('\0')
        require(entry == f"160000 commit {donor['commit']}\t{text}", f'Missing/mismatched gitlink: {text}')
        url = git(root, 'config', '-f', '.gitmodules', '--get', f'submodule.{text}.url').strip()
        require(url == donor['url'], f'Submodule URL differs from locked URL: {text}')


def raw_checkout(source: Path) -> None:
    # Keep canonical blobs, including LFS pointers, without running donor filters.
    info = source / '.git' / 'info'
    info.mkdir(parents=True, exist_ok=True)
    (info / 'attributes').write_text('* -text -filter -ident\n', encoding='utf-8')
    git(source, 'config', 'core.autocrlf', 'false')


def fetch(root: Path, donor: dict) -> None:
    check_parent(root, donor)
    primary = root / donor['paths'][0]
    require(not primary.exists() or not any(primary.iterdir()), 'Refusing to replace a nonempty source checkout')
    primary.mkdir(parents=True, exist_ok=True)
    git(primary, 'init')
    raw_checkout(primary)
    git(primary, 'remote', 'add', 'origin', donor['url'])
    git(primary, '-c', 'protocol.version=2', 'fetch', '--depth=1', '--no-tags', 'origin', donor['commit'], timeout=1500)
    git(primary, 'checkout', '--detach', donor['commit'], timeout=300)
    git(primary, 'fsck', '--strict', '--no-reflogs', timeout=600)
    for text in donor['paths'][1:]:
        destination = root / text
        require(not destination.exists() or not any(destination.iterdir()), 'Refusing to replace nonempty donor target')
        destination.parent.mkdir(parents=True, exist_ok=True)
        git(root, 'clone', '--no-checkout', '--no-hardlinks', str(primary), str(destination), timeout=600)
        raw_checkout(destination)
        git(destination, 'remote', 'set-url', 'origin', donor['url'])
        git(destination, 'checkout', '--detach', donor['commit'], timeout=300)


def blob_hash(path: Path, mode: str) -> tuple[str, bytes]:
    info = path.lstat()
    if mode == '120000':
        require(stat.S_ISLNK(info.st_mode), f'Expected a symlink: {path.name}')
        data = os.fsencode(os.readlink(path))
        h = hashlib.sha1(f'blob {len(data)}\0'.encode() + data)
        return h.hexdigest(), data[:512]
    require(stat.S_ISREG(info.st_mode), f'Expected regular source file: {path.name}')
    require(bool(info.st_mode & 0o111) == (mode == '100755'), f'Executable mode mismatch: {path.name}')
    h = hashlib.sha1(f'blob {info.st_size}\0'.encode())
    prefix = b''
    with path.open('rb') as stream:
        while chunk := stream.read(1024 * 1024):
            if not prefix:
                prefix = chunk[:512]
            h.update(chunk)
    return h.hexdigest(), prefix


def verify_source(source: Path, pin: str) -> tuple[dict, list[dict]]:
    require((source / '.git').exists(), 'Source checkout has not been fetched')
    require(Path(git(source, 'rev-parse', '--show-toplevel').strip()).resolve() == source.resolve(), 'Not an independent donor checkout')
    require(git(source, 'rev-parse', 'HEAD').strip() == pin, 'Wrong donor revision')
    flags = git(source, 'ls-files', '-v', '-z').split('\0')
    require(not any(x and (x[0].islower() or x[0] == 'S') for x in flags), 'Hidden or sparse index entries')
    require(not git(source, 'diff', '--cached', '--name-only', 'HEAD').strip(), 'Staged donor modifications')
    entries, nested, lfs, licenses = [], {}, [], []
    for record in git(source, 'ls-tree', '-r', '-z', 'HEAD').split('\0'):
        if not record:
            continue
        metadata, text = record.split('\t', 1)
        mode, kind, sha = metadata.split()
        require('..' not in PurePosixPath(text).parts and not text.startswith('/'), 'Unsafe tracked source path')
        if mode == '160000':
            nested[text] = sha
            continue
        require(kind == 'blob' and mode in ('100644', '100755', '120000'), 'Unsupported source tree entry')
        path = source / text
        require(path.parent.resolve().is_relative_to(source.resolve()), 'File parent escapes source checkout')
        actual, prefix = blob_hash(path, mode)
        require(actual == sha, f'Missing/modified source blob: {text}')
        entries.append({'path': text, 'mode': mode, 'git_blob': sha})
        if prefix.startswith(b'version https://git-lfs.github.com/spec/v1\n'):
            lfs.append(text)
        if '/' not in text and text.upper().startswith(('LICENSE', 'COPYING', 'COPYRIGHT')):
            licenses.append(text)
    require(bool(entries), 'Empty source tree')
    return {'commit': pin, 'tree': git(source, 'rev-parse', 'HEAD^{tree}').strip(),
            'tracked_blobs_verified': len(entries), 'every_blob_matches_git_object': True,
            'top_level_source_checkout_verified': True,
            'nested_git_dependencies_not_fetched': nested,
            'lfs_payloads_not_fetched': lfs, 'license_files_preserved': licenses,
            'build_dependencies': 'not acquired by this source-only job',
            'compiled_binary': 'not built', 'runtime_integration': 'not assessed'}, entries


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open('rb') as stream:
        while chunk := stream.read(4 * 1024 * 1024):
            h.update(chunk)
    return h.hexdigest()


def package(source: Path, donor: dict, entries: list[dict], output: Path) -> dict:
    output.mkdir(parents=True, exist_ok=True)
    filelist = output / 'tracked-files.nul'
    filelist.write_bytes(b''.join(os.fsencode(e['path']) + b'\0' for e in entries))
    archive = output / f"{donor['id']}-{donor['commit']}.tar.gz"
    with archive.open('wb') as stream:
        tar = subprocess.Popen(['tar', '--create', '--file=-', '--mtime=@0', '--owner=0', '--group=0', '--numeric-owner', '--no-recursion', '--null', '--verbatim-files-from', '-C', str(source), '-T', str(filelist.resolve())], stdout=subprocess.PIPE)
        try:
            zipped = subprocess.run(['gzip', '-1n'], stdin=tar.stdout, stdout=stream, timeout=600)
            tar.stdout.close()
            tar.wait(timeout=30)
            require(tar.returncode == 0 and zipped.returncode == 0, 'Source archive creation failed')
        finally:
            if tar.poll() is None:
                tar.kill()
                tar.wait()
    archive_hash = sha256_file(archive)
    parts = []
    with archive.open('rb') as stream:
        for number in range(8):
            first = stream.read(4 * 1024 * 1024)
            if not first:
                break
            part = output / f'source.part{number:02d}'
            size = len(first)
            with part.open('wb') as dest:
                dest.write(first)
                while size < 400 * 1024 * 1024:
                    chunk = stream.read(min(4 * 1024 * 1024, 400 * 1024 * 1024 - size))
                    if not chunk:
                        break
                    dest.write(chunk)
                    size += len(chunk)
            parts.append({'file': part.name, 'bytes': size, 'sha256': sha256_file(part)})
        require(not stream.read(1), 'Archive exceeds the eight-part publication budget')
    checksums = ''.join(f"{p['sha256']}  {p['file']}\n" for p in parts)
    (output / 'SHA256SUMS.parts').write_text(checksums, encoding='utf-8')
    (output / 'SHA256SUMS.archive').write_text(f'{archive_hash}  {archive.name}\n', encoding='utf-8')
    manifest = {'archive': archive.name, 'sha256': archive_hash, 'parts': parts,
                'restore_paths': donor['paths'], 'tracked_blobs': entries,
                'nested_git_dependencies_included': False}
    (output / 'recovery.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    (output / 'RECOVERY.txt').write_text(
        'Download the evidence artifact and all numbered source-part artifacts for this donor and run into one empty directory.\n'
        'Verify: sha256sum --check SHA256SUMS.parts\n'
        f'Reconstruct: cat source.part* > {archive.name}\n'
        'Verify: sha256sum --check SHA256SUMS.archive\n'
        'The archive contains EVERY tracked blob, including export-ignore files, without donor filters. It has no .git directory.\n'
        'Extract into a NEW EMPTY directory only, then restore to the exact app paths in recovery.json. Never overwrite an existing worktree.\n'
        'Nested gitlinks, LFS payloads and package/build dependencies are separately recorded, not included.\n', encoding='utf-8')
    archive.unlink()
    filelist.unlink()
    return {'archive_name': archive.name, 'archive_sha256': archive_hash, 'parts': parts}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix', action='store_true')
    parser.add_argument('--donor')
    parser.add_argument('--output', type=Path, default=Path('artifacts/donor'))
    args = parser.parse_args()
    donors = load_lock()
    if args.matrix:
        for donor in donors:
            check_parent(ROOT, donor)
        print(json.dumps({'donor': [d['id'] for d in donors]}, separators=(',', ':')))
        return 0
    require(args.donor in [d['id'] for d in donors], 'Unknown donor')
    donor = next(d for d in donors if d['id'] == args.donor)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    evidence = {'donor': donor, 'checked_at_utc': datetime.now(timezone.utc).isoformat(),
                'parent_commit': git(ROOT, 'rev-parse', 'HEAD').strip(), 'status': 'WORKING',
                'scope': 'Actual pinned source acquisition, not compilation/runtime acceptance'}
    try:
        fetch(ROOT, donor)
        paths, first_entries = {}, None
        evidence['verified_paths'] = paths
        for text in donor['paths']:
            result, entries = verify_source(ROOT / text, donor['commit'])
            paths[text] = result
            first_entries = entries if first_entries is None else first_entries
        evidence['verified_paths'] = paths
        evidence['recovery'] = package(ROOT / donor['paths'][0], donor, first_entries, output)
        evidence['status'] = 'CHECKPOINT'
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        evidence['status'] = 'STALLED'
        evidence['error'] = str(error)
        if isinstance(error, subprocess.CalledProcessError):
            evidence['error_detail'] = error.stderr.decode('utf-8', 'replace')[-6000:] if isinstance(error.stderr, bytes) else error.stderr
        print(json.dumps(evidence, indent=2), file=sys.stderr)
        return 1
    finally:
        evidence['finished_at_utc'] = datetime.now(timezone.utc).isoformat()
        (output / 'evidence.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({k: v for k, v in evidence.items() if k != 'recovery'}, indent=2))


if __name__ == '__main__':
    sys.exit(main())
