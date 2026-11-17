import importlib.util
from pathlib import Path
import tempfile
import unittest
from urllib.error import HTTPError
from urllib.request import urlopen

spec = importlib.util.spec_from_file_location('smoke', Path(__file__).parent / 'smoke-public-files-runtime.py')
smoke = importlib.util.module_from_spec(spec)
spec.loader.exec_module(smoke)

class SmokeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for name in smoke.REQUIRED:
            path = self.root / name
            path.parent.mkdir(exist_ok=True)
            path.write_text('<html><body><main id="nine-to-one-root"></main></body></html>' if name.endswith('.html') else 'safe')

    def test_anonymous_context_is_denied_and_document_has_probe(self):
        with smoke.serve(self.root) as (url, hits):
            with urlopen(url, timeout=2) as r:
                text = r.read().decode()
                self.assertIn(smoke.SNAPSHOT_ID, text)
            with self.assertRaises(HTTPError) as caught:
                urlopen(url.replace('/files/', '/api/files/v1/context'), timeout=2)
            self.assertEqual(caught.exception.code, 401)
            self.assertEqual((self.root / 'index.html').read_text(), '<html><body><main id="nine-to-one-root"></main></body></html>')
            self.assertEqual(hits['/files/'], 1)

    def test_scripts_and_wasm_are_served_from_actual_root_only(self):
        (self.root / '_framework/dotnet.wasm').write_bytes(b'\0asm')
        with smoke.serve(self.root) as (url, _):
            for name, expected_mime in [('main.js', 'text/javascript'), ('_framework/dotnet.wasm','application/wasm')]:
                with urlopen(url+name,timeout=2) as r:
                    self.assertTrue(r.headers['Content-Type'].startswith(expected_mime))
            for name in ('../etc/passwd', '%2e%2e/%2e%2e/private','/secret'):
                with self.assertRaises(HTTPError):
                    urlopen(url+name,timeout=2)

    def test_probe_parser(self):
        doc='<pre id="files-runtime-smoke-evidence">{"markers":["9to1:runtime:ready"],"status":"Ready"}</pre>'
        self.assertIn('9to1:runtime:ready', smoke.read_probe(doc)['markers'])
        self.assertIsNone(smoke.read_probe('<div>none</div>'))

if __name__ == '__main__':
    unittest.main()
