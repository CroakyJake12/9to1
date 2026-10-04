"""Prepare a pinned static-only Cloudflare package; never upload or deploy."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import uuid


def inventory(root):
    rows = []
    for current, directories, files in os.walk(root, followlinks=False):
        for name in directories + files:
            if (Path(current) / name).is_symlink():
                raise ValueError('Artifact symlinks are unsupported')
        for name in files:
            path = Path(current) / name
            if not path.is_file():
                raise ValueError('Artifact contains a nonregular file')
            data = path.read_bytes()
            if len(data) > 25 * 1024 * 1024:
                raise ValueError('Artifact file exceeds conservative 25MiB allowance')
            rows.append({'path': path.relative_to(root).as_posix(), 'bytes': len(data),
                         'sha256': hashlib.sha256(data).hexdigest()})
    rows.sort(key=lambda row: row['path'])
    if not rows or len(rows) > 20000:
        raise ValueError('Artifact requires 1..20000 files')
    body = json.dumps(rows, sort_keys=True, separators=(',', ':')).encode()
    return rows, hashlib.sha256(body).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifact', required=True, type=Path)
    parser.add_argument('--sha256', required=True, help='SHA256 of canonical inventory JSON (see README)')
    parser.add_argument('--rollback-version', required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if not args.artifact.is_absolute() or args.artifact.is_symlink() or not args.artifact.is_dir():
        raise ValueError('Absolute actual artifact directory required')
    if args.artifact.resolve() != args.artifact:
        raise ValueError('Artifact path must be canonical and contain no symlink parents')
    uuid.UUID(args.rollback_version)
    if args.rollback_version != '967782fb-0c10-4f58-95a9-6f9cb0f1af51':
        raise ValueError('Rollback version differs from reviewed actual preview version; refresh contract first')
    rows, digest = inventory(args.artifact)
    if digest != args.sha256:
        raise ValueError('Artifact differs from independently reviewed inventory hash')
    args.output.mkdir(parents=True, exist_ok=False)
    config = {'name': 'cakemods-migration-preview', 'account_id': '8aea00ca3c4635410861efcc6a5f593b',
              'compatibility_date': '2026-10-01', 'workers_dev': True, 'preview_urls': False,
              'assets': {'directory': str(args.artifact)}}
    rollback = {'strategy': 'percentage', 'versions': [{'version_id': args.rollback_version, 'percentage': 100}],
                'annotations': {'workers/message': 'Restore pre-pass migration preview version'}}
    receipt = {'status': 'PREPARED_NOT_UPLOADED_NOT_DEPLOYED', 'artifact': str(args.artifact),
               'inventorySHA256': digest, 'files': rows, 'rollbackVersion': args.rollback_version,
               'publicURL': 'https://cakemods-migration-preview.jcbailey008.workers.dev',
               'rootPreflight': 'Recheck exact artifact inventory and active/version metadata immediately before upload'}
    for name, value in [('wrangler.json', config), ('rollback.json', rollback), ('receipt.json', receipt)]:
        with (args.output / name).open('x') as stream:
            json.dump(value, stream, indent=2)
            stream.write('\n')
    print(json.dumps({'status': receipt['status'], 'files': len(rows), 'inventorySHA256': digest}))


if __name__ == '__main__':
    main()
