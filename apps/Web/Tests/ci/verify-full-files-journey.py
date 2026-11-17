#!/usr/bin/env python3
"""Validate actual 15-stage sealed Files + Workerd + Chrome fixture evidence.

This validator cannot assert Google sign-in, live Drive access or deployment.
It rejects missing browser evidence and misleadingly green synthetic reports.
"""
import argparse
import json
from pathlib import Path
import re
import sys

STEPS = (
    'boot-connected-root', 'folder-search', 'grid-details-toggle', 'create-folder',
    'rename-folder', 'upload-file', 'download-file', 'export-google-doc',
    'move-into-folder', 'trash-welcome', 'trash-view-and-restore', 'thumbnail-route',
    'reload-persistence', 'disconnect-drive', 'reconnect-and-verify',
)
RUNNER = 'real sealed WebAssembly + genuine Files Worker + Workerd in Chrome (local fixture)'
PNG = b'\x89PNG\r\n\x1a\n'
HEX256 = re.compile(r'^[0-9a-f]{64}$')


def verify(report, evidence_root):
    """Return all issues; empty means complete synthetic test evidence only."""
    errors = []
    if not isinstance(report, dict):
        return ['Report is not a JSON object.']
    if report.get('status') != 'PASS' or report.get('failedStep') is not None or report.get('failureDetail') is not None:
        errors.append('Journey itself did not report unambiguous PASS.')
    if report.get('runner') != RUNNER:
        errors.append('Journey was not attributed to the genuine sealed Workerd/Chrome runtime.')
    for field in ('realGoogleDriveAcceptance', 'realAuthentication', 'deployment'):
        if report.get(field) != 'NOT RUN':
            errors.append(f'{field} must remain explicitly NOT RUN for this fixture.')
    calls = report.get('providerCalls')
    if not isinstance(calls, int) or isinstance(calls, bool) or calls <= 0:
        errors.append('No genuine synthetic provider requests were recorded.')
    if report.get('unexpectedProviderHosts') != []:
        errors.append('Unexpected provider host or missing egress evidence.')
    steps = report.get('steps')
    if not isinstance(steps, list) or len(steps) != len(STEPS):
        errors.append('Expected exactly fifteen ordered journey steps.')
        steps = []
    names = [step.get('name') if isinstance(step, dict) else None for step in steps]
    if names != list(STEPS):
        errors.append('A required journey step is missing, duplicated, reordered or renamed.')
    for index, step in enumerate(steps):
        if not isinstance(step, dict) or step.get('ok') is not True or not isinstance(step.get('detail'), dict):
            errors.append(f'Journey step {index + 1} lacks a real successful action/detail.')
        elif (not isinstance(step.get('ms'), int) or isinstance(step['ms'], bool) or step['ms'] < 0):
            errors.append(f'Journey step {index + 1} lacks a duration.')
    by_name = {step.get('name'): step.get('detail') for step in steps if isinstance(step, dict)}
    def detail(name):
        value = by_name.get(name)
        return value if isinstance(value, dict) else {}
    def positive(value):
        return isinstance(value, int) and not isinstance(value, bool) and value > 0
    if not isinstance(detail('boot-connected-root').get('controls'), str) or 'Local Fixture Folder' not in detail('boot-connected-root').get('controls', ''):
        errors.append('The connected root lacked observed native controls.')
    if detail('folder-search').get('query') != 'Fixture' or detail('grid-details-toggle').get('view') != 'round-trip':
        errors.append('Search or grid/details round trip lacks confirmation.')
    if detail('create-folder').get('row') != 'Journey Folder' or detail('rename-folder').get('row') != 'Journey Renamed':
        errors.append('Folder create/rename canonical rows not confirmed.')
    if not HEX256.fullmatch(str(detail('upload-file').get('sha256', ''))):
        errors.append('Upload lacked a valid content SHA-256.')
    if not positive(detail('download-file').get('bytes')) or detail('download-file').get('file') != 'local-upload.txt':
        errors.append('Chrome download evidence missing.')
    if not positive(detail('export-google-doc').get('bytes')):
        errors.append('Workspace export evidence missing.')
    if detail('move-into-folder').get('destination') != 'Local Fixture Folder':
        errors.append('Canonical move destination missing.')
    if detail('trash-welcome').get('trashed') != 'welcome.txt' or detail('trash-view-and-restore').get('driveState') is not True:
        errors.append('Recoverable Trash/restore was not verified.')
    thumb = detail('thumbnail-route')
    if not positive(thumb.get('bytes')) or thumb.get('folderRefused') is not True or thumb.get('nonOwnerRefused') not in (401, 403):
        errors.append('Thumbnail owner/folder denial requirements not met.')
    if not isinstance(detail('reload-persistence').get('controls'), str) or 'Journey Renamed' not in detail('reload-persistence').get('controls', ''):
        errors.append('Reload persistence lacked observed native rows.')
    if not positive(detail('disconnect-drive').get('revokeCalls')):
        errors.append('Disconnect did not call the fixture revocation endpoint.')
    reconnect = detail('reconnect-and-verify')
    if not isinstance(reconnect.get('context'), dict) or reconnect['context'].get('connected') is not True or not positive(reconnect.get('tokenCalls')):
        errors.append('Reconnection was not verified.')

    root = Path(evidence_root)
    shots_name = report.get('screenshots')
    if not root.is_dir() or not isinstance(shots_name, str) or not shots_name:
        return errors + ['Screenshot evidence root/path is missing.']
    shots = Path(shots_name)
    try:
        trusted = root.resolve(strict=True)
        resolved = shots.resolve(strict=True)
        if not resolved.is_relative_to(trusted) or shots.is_symlink() or not resolved.is_dir():
            return errors + ['Screenshot evidence is outside the temporary trusted directory.']
        # Do not trust renamed/symlinked parent paths either.
        for parent in (shots, *shots.parents):
            if parent == root:
                break
            if parent.is_symlink():
                return errors + ['Screenshot evidence traverses a symlink.']
        for index, name in enumerate(STEPS, 1):
            path = shots / f'{index:02d}-{name}.png'
            if path.is_symlink() or not path.is_file() or path.stat().st_size < 100:
                errors.append(f'Missing or incomplete screenshot for {name}.')
                continue
            with path.open('rb') as file:
                if file.read(len(PNG)) != PNG:
                    errors.append(f'Invalid screenshot signature for {name}.')
    except (OSError, ValueError):
        errors.append('Screenshot evidence path is invalid or missing.')
    return errors


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--evidence-root', required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        if args.report.stat().st_size > 1048576:
            raise ValueError('Report exceeds 1 MiB.')
        report = json.loads(args.report.read_text(encoding='utf-8'))
        issues = verify(report, args.evidence_root)
    except (OSError, ValueError, UnicodeError) as error:
        issues = [f'Could not safely read JSON report: {error}.']
    if issues:
        print('FAIL — Files full Worker browser acceptance NOT VERIFIED')
        for issue in issues:
            print('- ' + issue)
        return 1
    print('PASS — 15/15 actual native CUI steps through sealed WebAssembly + local Workerd fixture')
    print('Evidence tier: SYNTHETIC PROVIDER ONLY; real Google Drive, owner OAuth and deployment NOT RUN')
    return 0


if __name__ == '__main__':
    sys.exit(main())
