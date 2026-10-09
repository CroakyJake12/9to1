#!/usr/bin/env python3
"""Fetch the nested source/LFS gaps observed by the real donor-source verifier."""
from __future__ import annotations
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / 'source-acquisition'))
import acquire as source


def safe_relative(text: str) -> str:
    path = PurePosixPath(text)
    source.require(bool(text) and not path.is_absolute() and '..' not in path.parts
                   and '\\' not in text and '.git' not in path.parts, 'Unsafe dependency path')
    return text


def parse_pointer(data: bytes) -> dict:
    match = re.fullmatch(rb'version https://git-lfs.github.com/spec/v1\noid sha256:([a-f0-9]{64})\nsize ([0-9]+)\n?', data)
    source.require(match is not None, 'Invalid/unsupported LFS pointer')
    return {'oid': match[1].decode(), 'size': int(match[2])}


def check_object(path: Path, pointer: dict) -> None:
    source.require(path.is_file() and not path.is_symlink(), 'Missing LFS payload')
    source.require(path.stat().st_size == pointer['size'], 'LFS object size mismatch')
    source.require(source.sha256_file(path) == pointer['oid'], 'LFS object hash mismatch')


def hydrate(source_root: Path, result: dict, entries: list[dict], url: str) -> list[dict]:
    paths = result['lfs_payloads_not_fetched']
    if not paths:
        return []
    mapping = {e['path']: e for e in entries}
    pointers = []
    for text in paths:
        safe_relative(text)
        pointer = parse_pointer((source_root / text).read_bytes())
        pointers.append({'path': text, **pointer, 'pointer_git_blob': mapping[text]['git_blob']})
    total = sum(p['size'] for p in pointers)
    source.require(total <= 5 * 1024**3, f'LFS size exceeds 5 GiB per-job budget: {total}')
    source.require(shutil.disk_usage(source_root).free > total * 3 + 1024**3,
                   'Insufficient free disk for LFS objects, checkout and recovery archive')
    print(json.dumps({'lfs_paths': len(paths), 'lfs_payload_bytes': total, 'repository': url}), flush=True)
    source.git(source_root, 'remote', 'add', 'payload-upstream', url)
    # Explicit upstream and ref, no recent-history sweep and no donor-defined transfers.
    source.git(source_root, '-c', f'lfs.url={url}/info/lfs', '-c', 'lfs.basictransfersonly=true',
               '-c', 'lfs.fetchrecentalways=false', 'lfs', 'fetch', '--include=', '--exclude=',
               'payload-upstream', result['commit'], timeout=1200)
    for pointer in pointers:
        oid = pointer['oid']
        payload = source_root / '.git' / 'lfs' / 'objects' / oid[:2] / oid[2:4] / oid
        check_object(payload, pointer)
        target = source_root / pointer['path']
        source.require(target.parent.resolve().is_relative_to(source_root.resolve()), 'LFS path escapes checkout')
        mode = target.stat().st_mode
        shutil.copyfile(payload, target)
        target.chmod(mode)
        check_object(target, pointer)
        # The recovery hash now describes hydrated bytes, not the original pointer blob.
        mapping[pointer['path']]['git_blob'] = source.blob_hash(target, mapping[pointer['path']]['mode'])[0]
    result['lfs_payloads_verified'] = pointers
    result['lfs_payloads_not_fetched'] = []
    result['hydrated_source_note'] = 'LFS payload hashes/sizes match pinned pointers; canonical Git blob checks happened before hydration'
    return pointers


def clone_child(root: Path, child: dict) -> None:
    safe_relative(child['path'])
    source.require(re.fullmatch(r'[a-f0-9]{40}', child['commit']) is not None, 'Invalid nested commit')
    entry = source.git(root, 'ls-tree', '-z', 'HEAD', '--', child['path']).rstrip('\0')
    source.require(entry == f"160000 commit {child['commit']}\t{child['path']}", 'Nested source pin differs from parent')
    original = source.git(root, 'config', '-f', '.gitmodules', '--get',
                          f"submodule.{child['path']}.url").strip()
    source.require(original == child['declared_url'], 'Nested donor URL changed')
    target = root / child['path']
    source.require(target.resolve().is_relative_to(root.resolve()), 'Nested path escapes parent')
    source.require(not target.exists() or not any(target.iterdir()), 'Refusing to overwrite nested source')
    target.mkdir(parents=True, exist_ok=True)
    source.git(target, 'init')
    source.raw_checkout(target)
    source.git(target, 'remote', 'add', 'origin', child['url'])
    source.git(target, 'fetch', '--depth=1', '--no-tags', 'origin', child['commit'], timeout=1200)
    source.git(target, 'checkout', '--detach', child['commit'], timeout=300)
    source.git(target, 'fsck', '--strict', '--no-reflogs', timeout=300)


def execute(name: str, output: Path) -> int:
    plans = json.loads((HERE / 'dependencies.lock.json').read_text())
    source.require(name in plans, 'Unknown dependency plan')
    plan = plans[name]
    donor = next(d for d in source.load_lock() if d['id'] == name)
    output.mkdir(parents=True, exist_ok=True)
    evidence = {'status': 'WORKING', 'donor': name, 'source_commit': donor['commit'],
                'parent_commit': source.git(source.ROOT, 'rev-parse', 'HEAD').strip(),
                'checked_at_utc': datetime.now(timezone.utc).isoformat(),
                'nested_dependencies': {}, 'lfs_payloads_verified': [],
                'restore_parent_paths': donor['paths'],
                'compiled_binary': 'not built', 'runtime_integration': 'not assessed'}
    try:
        source.fetch(source.ROOT, {**donor, 'paths': donor['paths'][:1]})
        root = source.ROOT / donor['paths'][0]
        parent, parent_entries = source.verify_source(root, donor['commit'])
        expected = {c['path']: c['commit'] for c in plan['children']}
        source.require(parent['nested_git_dependencies_not_fetched'] == expected, 'Unplanned nested source dependency')
        # Keep publication independent for each child; failures never claim earlier children complete.
        packed = []
        failures = []
        for child in plan['children']:
            try:
                clone_child(root, child)
                data, entries = source.verify_source(root / child['path'], child['commit'])
                source.require(not data['nested_git_dependencies_not_fetched'], 'Further nested dependencies need acquisition')
                hydrate(root / child['path'], data, entries, child['url'])
                data['source_url'] = child['url']
                data['parent_declared_url'] = child['declared_url']
                evidence['nested_dependencies'][child['path']] = data
                packed.extend({**entry, 'path': child['path'] + '/' + entry['path']} for entry in entries)
            except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                evidence['nested_dependencies'][child['path']] = {'status': 'STALLED', 'error': str(error)}
                if isinstance(error, subprocess.CalledProcessError):
                    evidence['nested_dependencies'][child['path']]['stderr'] = str(error.stderr)[-3000:]
                failures.append(child['path'])
        if plan.get('lfs_url'):
            source.require(len(parent['lfs_payloads_not_fetched']) == plan['expected_lfs_paths'], 'LFS inventory changed')
            assets = hydrate(root, parent, parent_entries, plan['lfs_url'])
            evidence['lfs_payloads_verified'] = assets
            included = {x['path'] for x in assets}
            packed.extend(e for e in parent_entries if e['path'] in included)
        source.require(bool(packed), 'No dependency source acquired')
        evidence['recovery'] = source.package(root, {**donor, 'id': name + '-dependencies'}, packed, output)
        recovery_path = output / 'recovery.json'
        recovery = json.loads(recovery_path.read_text())
        recovery.update(full_parent_donor_included=False,
                        nested_git_dependencies_included=list(evidence['nested_dependencies']),
                        hydrated_lfs_included=bool(evidence['lfs_payloads_verified']))
        recovery_path.write_text(json.dumps(recovery, indent=2) + '\n')
        (output / 'DEPENDENCY-RECOVERY.txt').write_text(
            'This archive supplies only the nested repositories and hydrated LFS assets in evidence.json.\n'
            'First verify and restore the matching parent donor source archive from run 37925619682.\n'
            'Validate every checksum, then extract this archive into a separate empty staging directory using safe tar extraction.\n'
            'Copy each nested tree/asset into the documented parent-relative path only after checking for local edits.\n'
            'Original pointer Git hashes and actual LFS SHA256/size proofs are preserved in evidence.json.\n'
            'This is not an installed SDK, compiled app or runtime integration.\n', encoding='utf-8')
        source.require(not failures, f'Failed nested dependencies: {failures}')
        evidence['status'] = 'CHECKPOINT'
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        evidence['status'] = 'STALLED'
        evidence['error'] = str(error)
        if isinstance(error, subprocess.CalledProcessError):
            evidence['stderr'] = str(error.stderr)[-6000:]
        return 1
    finally:
        evidence['finished_at_utc'] = datetime.now(timezone.utc).isoformat()
        (output / 'evidence.json').write_text(json.dumps(evidence, indent=2) + '\n')
        print(json.dumps(evidence, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--donor', required=True)
    parser.add_argument('--output', type=Path, required=True)
    arguments = parser.parse_args()
    sys.exit(execute(arguments.donor, arguments.output.resolve()))
