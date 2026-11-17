import importlib.util
import json
import tempfile
from pathlib import Path
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('files_nav', HERE / 'smoke-files-navigation-journey.py')
journey = importlib.util.module_from_spec(spec)
spec.loader.exec_module(journey)


def native():
    return {
        'markers': ['9to1:runtime:ready', '9to1:backend:ready'],
        'rootExists': True, 'hash': '#/app.files', 'status': 'Ready', 'errors': [],
        'filesControls': ['Disconnect Google Drive', 'Files privacy',
                          journey.connected.FOLDER_NAME, journey.connected.FILE_NAME],
    }


def evidence():
    stages = []
    for name in journey.STEPS:
        labels = ['Project Atlas', 'draft-notes.txt']
        if name in ('inside-folder', 'up-to-root'):
            labels = ['inside.txt']
        elif name == 'back-to-folder':
            labels = ['Project Atlas', 'draft-notes.txt']
        elif name == 'forward-to-root':
            labels = ['inside.txt']
        elif name == 'search-results' or name == 'clear-search':
            labels = ['Project Atlas']
        stages.append({'step': name, 'controls': labels})
    return {'status': 'PASS', 'completed': journey.STEPS[:],
            'observations': stages, 'failure': None, 'realGoogleDriveAcceptance': 'NOT RUN'}


def requests():
    return [
        {'method': 'GET', 'path': '/api/files/v1/context', 'query': {}},
        {'method': 'GET', 'path': '/api/files/v1/items', 'query': {}},
        {'method': 'GET', 'path': '/api/files/v1/items',
         'query': {'parentId': [journey.connected.FOLDER_ID]}},
        {'method': 'GET', 'path': '/api/files/v1/items', 'query': {}},
        {'method': 'GET', 'path': '/api/files/v1/items',
         'query': {'query': ['Atlas']}},
    ]


class NavigationJourneyTests(unittest.TestCase):
    def test_html_parser_is_strict_and_does_not_guess_success(self):
        self.assertIsNone(journey.read_navigation('<html><body></body></html>'))
        self.assertIsNone(journey.read_navigation('<pre id="files-navigation-smoke-evidence">broken</pre>'))
        self.assertIsNone(journey.read_navigation('<pre id="files-navigation-smoke-evidence">[]</pre>'))
        encoded = json.dumps(evidence()).replace('"', '&quot;')
        self.assertEqual(journey.read_navigation(
            '<pre hidden id="files-navigation-smoke-evidence">' + encoded + '</pre>'), evidence())

    def test_real_native_peers_and_all_http_stages_are_needed(self):
        self.assertTrue(journey.accepted(native(), evidence(), requests(), 0))
        self.assertFalse(journey.accepted(native(), evidence(), requests(), 1))
        self.assertFalse(journey.accepted(None, evidence(), requests(), 0))
        self.assertFalse(journey.accepted(native(), None, requests(), 0))
        self.assertFalse(journey.accepted(native(), evidence(), [], 0))

    def test_each_required_provider_read_is_enforced(self):
        base = requests()
        for key in (None, 'parentId', 'query'):
            if key is None:
                modified = [r for r in base if r.get('query')]
            else:
                modified = [r for r in base if key not in r.get('query', {})]
            with self.subTest(key=key):
                self.assertFalse(journey.accepted(native(), evidence(), modified, 0))
        forbidden = requests() + [{'method': 'POST', 'path': '/api/files/v1/operations', 'query': {}}]
        self.assertFalse(journey.accepted(native(), evidence(), forbidden, 0))

    def test_navigation_cannot_be_forged_by_static_root_or_filtered_screen(self):
        for idx, label in ((2, 'inside.txt'), (7, 'Project Atlas'),
                           (9, 'draft-notes.txt')):
            record = evidence()
            record['observations'][idx]['controls'].remove(label)
            with self.subTest(step=idx):
                self.assertFalse(journey.accepted(native(), record, requests(), 0))
        for key, value in [('status', 'RUNNING'), ('completed', []),
                           ('failure', 'a genuine error'), ('realGoogleDriveAcceptance', 'PASS')]:
            record = evidence()
            record[key] = value
            with self.subTest(field=key):
                self.assertFalse(journey.accepted(native(), record, requests(), 0))

    def test_script_loaded_from_separate_test_file_without_modifying_published_assets(self):
        original_probe = journey.connected.connected_probe
        original_response = journey.connected.response_for
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in journey.connected.smoke.REQUIRED:
                child = root / name
                child.parent.mkdir(parents=True, exist_ok=True)
                child.write_text('<html><body></body></html>' if name == 'index.html' else 'fixture')
            with journey.interactive_fixture(root) as (_url, _hits, _requests):
                # Read the exact text the fixture injects into served documents.
                # Calling connected_probe() again here would re-run the pristine
                # probe guard against the already-extended live probe, which is
                # the integrity check doing its job, not a product failure.
                contents = journey.connected.smoke.PROBE
                self.assertIn('<script>', contents)
                self.assertIn('files-navigation-smoke-evidence', contents)
                self.assertIn('PerformAccessibility', contents)
            for name in journey.connected.smoke.REQUIRED:
                with self.subTest(asset=name):
                    self.assertEqual((root / name).read_text(),
                                     '<html><body></body></html>' if name == 'index.html' else 'fixture')
        self.assertIs(journey.connected.connected_probe, original_probe)
        self.assertIs(journey.connected.response_for, original_response)

    def test_unauthorised_google_credentials_or_real_drive_urls_are_not_in_driver(self):
        data = journey.SCRIPT.read_text()
        for forbidden in ('googleapis.com', 'accounts.google.com',
                          '9to1.uk', 'accessToken', 'refreshToken',
                          'fetch(', 'XMLHttpRequest(', 'POST', 'DELETE'):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, data)


if __name__ == '__main__':
    unittest.main()
