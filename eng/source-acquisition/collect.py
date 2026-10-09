#!/usr/bin/env python3
"""Collect real source job evidence; fail if a path, donor, or upload job is missing."""
import argparse
import json
from pathlib import Path
import sys
from acquire import load_lock


def collect(directory: Path, parent: str, jobs_result: str) -> dict:
    donors = load_lock()
    found = {}
    errors = []
    for path in directory.glob('*/evidence.json'):
        item = json.loads(path.read_text())
        name = item['donor']['id']
        if name in found:
            errors.append(f'Duplicate evidence for {name}')
        found[name] = item
    records = []
    for donor in donors:
        name = donor['id']
        evidence = found.get(name, {})
        paths = evidence.get('verified_paths', {})
        ok = (evidence.get('status') == 'CHECKPOINT'
              and evidence.get('parent_commit') == parent
              and evidence.get('donor') == donor
              and set(paths) == set(donor['paths'])
              and bool(evidence.get('recovery', {}).get('parts')))
        for data in paths.values():
            ok = (ok and data.get('commit') == donor['commit']
                  and data.get('every_blob_matches_git_object') is True
                  and data.get('tracked_blobs_verified', 0) > 0)
        if not ok:
            errors.append(f"{name}: {evidence.get('error', 'source/evidence/recovery verification incomplete')}")
        records.append({'id':name, 'verified':bool(ok), 'commit':donor['commit'],
                        'paths':paths, 'recovery':evidence.get('recovery'),
                        'error':evidence.get('error'), 'error_detail':evidence.get('error_detail')})
    if set(found) - {d['id'] for d in donors}:
        errors.append('Unexpected donor evidence')
    if jobs_result != 'success':
        errors.append(f'Acquisition/publication matrix result: {jobs_result}')
    return {'status':'CHECKPOINT' if not errors else 'STALLED', 'parent_commit':parent,
            'expected_unique_donors':len(donors), 'verified_unique_donors':sum(r['verified'] for r in records),
            'expected_source_paths':sum(len(d['paths']) for d in donors),
            'verified_source_paths':sum(len(r['paths']) for r in records if r['verified']),
            'distinct_tracked_source_blobs':sum(next(iter(r['paths'].values()))['tracked_blobs_verified'] for r in records if r['verified']),
            'errors':errors, 'donors':records,
            'scope':'The 23 missing declared source locations, not all package/build dependencies or all V2 donor obligations',
            'compiled_binary':'not built', 'runtime_integration':'not assessed'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--parent', required=True)
    parser.add_argument('--jobs-result', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = collect(args.directory, args.parent, args.jobs_result)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k != 'donors'},indent=2))
    return 0 if not result['errors'] else 1


if __name__ == '__main__':
    sys.exit(main())
