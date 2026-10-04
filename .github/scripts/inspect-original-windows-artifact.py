"""Read original package-probe observations; never run downloaded code."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import urllib.request


REPO = Path(__file__).resolve().parents[2]
CATALOG = json.loads((REPO / '.github/validation/windows-original-artifacts.json').read_text())
NAMES = ('result.json', 'launch.stdout.log', 'launch.stderr.log',
         'sdk-info.log', 'cui-tasks-release.log', 'publish-release-win-x64.log')
PER_FILE_LIMIT = 8 * 1024 * 1024
TOTAL_LIMIT = 24 * 1024 * 1024


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def api(path):
    request = urllib.request.Request(
        'https://api.github.com/repos/CroakyJake12/9to1/' + path,
        headers={'Authorization': 'Bearer ' + os.environ['GH_TOKEN'],
                 'Accept': 'application/vnd.github+json',
                 'X-GitHub-Api-Version': '2022-11-28'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def regular_file(path):
    if not stat.S_ISREG(path.lstat().st_mode):
        raise ValueError('Selected original member is not a regular file: ' + path.name)


def validate_metadata(entry, output):
    if output.exists():
        raise ValueError('Metadata output directory must be new.')
    output.mkdir(parents=True)
    run = api('actions/runs/' + str(CATALOG['runId']))
    if (run['head_sha'] != CATALOG['workflowCommit'] or
            run['run_attempt'] != CATALOG['runAttempt'] or run['status'] != 'completed'):
        raise ValueError('Original run identity/attempt/completion mismatch.')
    original = api('actions/artifacts/' + str(entry['id']))
    if (original['id'] != entry['id'] or original['name'] != entry['name'] or
            original['size_in_bytes'] != entry['bytes'] or
            original['digest'] != entry['digest'] or original['expired'] or
            original['workflow_run']['id'] != CATALOG['runId'] or
            original['workflow_run']['head_sha'] != CATALOG['workflowCommit']):
        raise ValueError('Original artifact identity, size, digest or source mismatch.')
    write_json(output / 'original-artifact-metadata.json', original)


def collect(entry, source, output):
    if source.is_symlink() or not source.is_dir():
        raise ValueError('Downloaded artifact root must be a real directory.')
    metadata_path = output / 'original-artifact-metadata.json'
    regular_file(metadata_path)
    metadata = json.loads(metadata_path.read_text())
    if metadata['id'] != entry['id'] or metadata['digest'] != entry['digest']:
        raise ValueError('Metadata custody mismatch.')
    paths = [source / name for name in NAMES if (source / name).exists()]
    if not (source / 'result.json').exists():
        raise ValueError('Original result.json is missing.')
    sizes = []
    for path in paths:
        regular_file(path)
        size = path.stat().st_size
        if size > PER_FILE_LIMIT:
            raise ValueError('Original observation exceeds the declared per-file bound.')
        sizes.append(size)
    if sum(sizes) > TOTAL_LIMIT:
        raise ValueError('Original observations exceed the declared compact bound.')
    original = json.loads((source / 'result.json').read_text(encoding='utf-8-sig'))
    if (original['target'] != entry['target'] or
            original['workflowCommit'] != CATALOG['workflowCommit'] or
            original['sourceBasis'] != CATALOG['nativeSourceBasis'] or
            original['architecture'] != 'win-x64' or
            original['acceptanceVerified'] is not False):
        raise ValueError('Original producer identity or acceptance qualification mismatch.')
    receipt = {'schemaVersion': 1, 'runId': CATALOG['runId'],
               'workflowCommit': CATALOG['workflowCommit'], 'target': entry['target'],
               'artifactId': entry['id'], 'originalArchiveDigestDeclaration': entry['digest'],
               'archiveByteVerification': 'Not independently hashed by collector; the hosted download-artifact action owns archive verification. Original package and selected-file hashes are separate.',
               'executablesRun': False, 'newWindowsObservations': False,
               'acceptanceVerified': False, 'requiredWindowsProductGroups': 29,
               'verifiedWindowsProductGroups': 0, 'selectedFiles': [],
               'originalMissingMembers': [name for name in NAMES if not (source / name).exists()],
               'originalPackageSha256Verified': False}
    package = original['package']
    if package is not None:
        expected = entry['target'] + '-win-x64-unaccepted.zip'
        if package['file'] != expected:
            raise ValueError('Original package filename is outside the declared target.')
        package_path = source / expected
        regular_file(package_path)
        if package_path.stat().st_size != package['bytes'] or sha256(package_path) != package['sha256']:
            raise ValueError('Original retained package bytes differ from producer receipt.')
        receipt['originalPackageSha256Verified'] = True
        receipt['originalPackage'] = package
    for path in paths:
        destination = output / path.name
        if destination.exists():
            raise ValueError('Do not overwrite original compact observation members.')
        before = sha256(path)
        shutil.copyfile(path, destination)
        after = sha256(destination)
        if before != after:
            raise ValueError('Original member copy changed bytes.')
        receipt['selectedFiles'].append({'path': path.name, 'bytes': destination.stat().st_size,
                                        'sha256': after, 'originalCopyVerified': True})
    write_json(output / 'read-only-inspection-receipt.json', receipt)
    print(json.dumps({'target': entry['target'], 'originalStatus': original['status'],
                      'originalStage': original['stage'], 'originalLaunch': original['launch'],
                      'originalFailure': original['failure'], 'acceptanceVerified': False}))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--target', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--source')
    parser.add_argument('--validate-metadata', action='store_true')
    args = parser.parse_args()
    entries = [entry for entry in CATALOG['artifacts'] if entry['target'] == args.target]
    if len(entries) != 1:
        raise ValueError('Select one exact original artifact.')
    output = Path(args.output).resolve()
    if output == REPO or REPO in output.parents:
        raise ValueError('Keep generated observations outside the checkout.')
    try:
        if args.validate_metadata:
            validate_metadata(entries[0], output)
        else:
            if not args.source:
                raise ValueError('An exact downloaded original artifact root is required.')
            collect(entries[0], Path(args.source).absolute(), output)
    except Exception as error:
        if output.is_dir():
            write_json(output / 'inspection-failure.json',
                       {'status': 'ORIGINAL_OBSERVATION_INSPECTION_FAILED',
                        'type': type(error).__name__, 'message': str(error),
                        'acceptanceVerified': False})
        raise


if __name__ == '__main__':
    main()
