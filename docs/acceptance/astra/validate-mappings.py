#!/usr/bin/env python3
"""Check preparation mappings against pinned source text; does not test products."""
import argparse
import hashlib
import json
from pathlib import Path


def validate(mapping_root, spec_root):
    errors, count, seen = [], 0, set()
    for file in sorted(mapping_root.glob('*.requirements.json')):
        doc = json.loads(file.read_text())
        if doc.get('preparation_only') is not True or not doc.get('coverage_note'):
            errors.append(f'{file.name}: missing preparation/coverage declaration')
        for row in doc['requirements']:
            count += 1
            rid = row.get('id')
            if not rid or rid in seen:
                errors.append(f'{file.name}: missing/duplicate ID {rid}')
            seen.add(rid)
            for key in ('requirement', 'platforms', 'steps', 'expected', 'evidence_required', 'sources', 'reason'):
                if not row.get(key):
                    errors.append(f'{rid}: missing {key}')
            if row.get('status') != 'NOT EXECUTED':
                errors.append(f'{rid}: preparation cannot claim product result')
            for src in row.get('sources', []):
                name = src.get('document', '')
                if name not in ('development-specification.txt', 'post-release-updates.txt'):
                    errors.append(f'{rid}: unknown authority {name}')
                    continue
                lines = (spec_root / name).read_text().splitlines()
                start, end = src.get('line_start'), src.get('line_end')
                if not isinstance(start, int) or not isinstance(end, int) or not 1 <= start <= end <= len(lines):
                    errors.append(f'{rid}: invalid citation range')
                    continue
                text = '\n'.join(lines[start - 1:end])
                quote = src.get('quote')
                if not quote or quote not in text:
                    errors.append(f'{rid}: quote differs from cited original')
    if not count:
        errors.append('No requirement families found')
    return count, errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--spec-root', type=Path, required=True)
    parser.add_argument('--mapping-root', type=Path, default=Path(__file__).parent)
    args = parser.parse_args()
    count, errors = validate(args.mapping_root, args.spec_root)
    result = {'kind': 'preparation-mapping-integrity', 'families': count,
              'errors': errors, 'product_checks_executed': 0,
              'product_status': 'NOT EXECUTED',
              'mapping_sha256': {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                                 for p in sorted(args.mapping_root.glob('*.requirements.json'))}}
    print(json.dumps(result, indent=2))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
