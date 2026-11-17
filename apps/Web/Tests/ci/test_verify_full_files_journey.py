"""Fail-closed contract tests for the actual Workerd + Files browser journey receipt."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).parent
SPEC = importlib.util.spec_from_file_location('files_journey_verifier', HERE / 'verify-full-files-journey.py')

STEPS = [
    'boot-connected-root', 'folder-search', 'grid-details-toggle', 'create-folder',
    'rename-folder', 'upload-file', 'download-file', 'export-google-doc',
    'move-into-folder', 'trash-welcome', 'trash-view-and-restore', 'thumbnail-route',
    'reload-persistence', 'disconnect-drive', 'reconnect-and-verify',
]
DETAILS = [
    {'controls': 'Files root,Local Fixture Folder,welcome.txt'},
    {'query': 'Fixture'}, {'view': 'round-trip'}, {'row': 'Journey Folder'},
    {'row': 'Journey Renamed'}, {'viaChooserEvent': False, 'sha256': 'a' * 64},
    {'file': 'local-upload.txt', 'bytes': 1500}, {'bytes': 42},
    {'destination': 'Local Fixture Folder'}, {'trashed': 'welcome.txt'},
    {'restoredTo': 'Local Fixture Folder', 'driveState': True},
    {'bytes': 240, 'folderRefused': True, 'nonOwnerRefused': 403},
    {'controls': 'Journey Renamed,local-upload.txt'},
    {'revokeCalls': 1}, {'context': {'connected': True}, 'tokenCalls': 2},
]


class Verification(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='9to1-full-journey-test-')
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.shots = self.root / 'evidence' / 'screenshots'
        self.shots.mkdir(parents=True)
        self.receipt = self.root / 'journey.json'
        for index, step in enumerate(STEPS, 1):
            (self.shots / f'{index:02d}-{step}.png').write_bytes(b'\x89PNG\r\n\x1a\n' + bytes(200))
        self.data = {
            'status': 'PASS', 'failedStep': None, 'failureDetail': None,
            'steps': [{'name': name, 'ok': True, 'detail': detail, 'ms': index + 2}
                      for index, (name, detail) in enumerate(zip(STEPS, DETAILS))],
            'screenshots': str(self.shots), 'downloads': str(self.root / 'evidence' / 'downloads'),
            'providerCalls': 85, 'unexpectedProviderHosts': [],
            'runner': 'real sealed WebAssembly + genuine Files Worker + Workerd in Chrome (local fixture)',
            'realGoogleDriveAcceptance': 'NOT RUN', 'realAuthentication': 'NOT RUN', 'deployment': 'NOT RUN',
        }

    def verify(self, data=None):
        module = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(module)
        return module.verify(self.data if data is None else data, self.root)

    def test_valid_realistic_receipt(self):
        self.assertEqual(self.verify(), [])

    def test_missing_or_reordered_or_duplicate_steps_refused(self):
        for bad in [self.data['steps'][:-1], self.data['steps'][1:] + self.data['steps'][:1],
                    self.data['steps'][:1] * 2 + self.data['steps'][2:]]:
            with self.subTest(bad=bad[0]['name']):
                data = dict(self.data, steps=bad)
                self.assertTrue(self.verify(data))

    def test_false_green_failed_or_skipped_action_refused(self):
        for change in [{'status': 'FAIL'}, {'failedStep': 'rename-folder'}, {'failureDetail': 'denied'},
                       {'steps': [*self.data['steps'][:5],
                                  dict(self.data['steps'][5], ok='true'), *self.data['steps'][6:]]}]:
            with self.subTest(change=change):
                self.assertTrue(self.verify(dict(self.data, **change)))

    def test_unauthorised_host_and_fake_live_claims_refused(self):
        for change in [{'unexpectedProviderHosts': [{'host': 'drive.google.com'}]},
                       {'realGoogleDriveAcceptance': 'PASS'}, {'realAuthentication': True},
                       {'deployment': 'YES'}, {'runner': 'mocked app'}, {'providerCalls': 0}]:
            with self.subTest(change=change):
                self.assertTrue(self.verify(dict(self.data, **change)))

    def test_missing_corrupt_or_symlinked_screenshot_refused(self):
        target = self.shots / f'01-{STEPS[0]}.png'
        target.unlink()
        self.assertTrue(self.verify())
        target.write_bytes(b'not a screenshot')
        self.assertTrue(self.verify())
        target.unlink()
        outside = self.root / 'outside.png'
        outside.write_bytes(b'\x89PNG\r\n\x1a\n' + bytes(200))
        target.symlink_to(outside)
        self.assertTrue(self.verify())

    def test_out_of_root_evidence_rejected(self):
        self.assertTrue(self.verify(dict(self.data, screenshots='/etc')))
        self.assertTrue(self.verify(dict(self.data, screenshots='../../escape')))

    def test_transfer_and_protection_proofs_required(self):
        cases = [(5, {'sha256': 'not-a-hash'}), (6, {'bytes': 0}), (7, {'bytes': 0}),
                 (10, {'driveState': False}), (11, {'folderRefused': False}),
                 (11, {'nonOwnerRefused': 200}), (13, {'revokeCalls': 0}),
                 (14, {'context': {'connected': False}})]
        for index, change in cases:
            with self.subTest(index=index, change=change):
                steps = [dict(x) for x in self.data['steps']]
                steps[index]['detail'] = {**steps[index]['detail'], **change}
                self.assertTrue(self.verify(dict(self.data, steps=steps)))

    def test_cli_success_and_failure(self):
        self.receipt.write_text(json.dumps(self.data))
        command = [sys.executable, str(HERE / 'verify-full-files-journey.py'),
                   '--report', str(self.receipt), '--evidence-root', str(self.root)]
        good = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(good.returncode, 0, good.stdout + good.stderr)
        self.assertIn('15/15', good.stdout)
        self.receipt.write_text('{"status": "PASS"}')
        bad = subprocess.run(command, capture_output=True, text=True)
        self.assertNotEqual(bad.returncode, 0)
        self.assertIn('FAIL', bad.stdout)


if __name__ == '__main__':
    unittest.main()
