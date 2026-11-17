import importlib.util
import json
import shutil
import subprocess
from pathlib import Path
import tempfile
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

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

    def test_anonymous_account_landing_uses_test_only_evidence_without_claiming_login(self):
        with smoke.serve(self.root) as (url, hits):
            with urlopen(url.replace('/files/', '/account?notice=signed-out'), timeout=2) as response:
                page = response.read().decode('utf-8')
                self.assertEqual(response.status, 200)
                self.assertIn(smoke.SNAPSHOT_ID, page)
                self.assertIn('sessionStorage', page)
                self.assertNotIn('Access granted', page)
            self.assertEqual(hits['/account'], 1)

    def test_redirect_probe_is_decoded_with_no_invented_success(self):
        empty = '<pre id="files-runtime-smoke-evidence">{"markers":[],"redirectObserved":true,"landedAt":"/account"}</pre>'
        result = smoke.read_probe(empty)
        self.assertEqual(result['landedAt'], '/account')
        self.assertFalse(smoke.boot_complete(result))
        ready = '<pre id="files-runtime-smoke-evidence">{"markers":["9to1:runtime:ready","9to1:backend:ready"],"redirectObserved":true}</pre>'
        self.assertTrue(smoke.boot_complete(smoke.read_probe(ready)))
        runtime_only = '<pre id="files-runtime-smoke-evidence">{"markers":["9to1:runtime:ready"],"redirectObserved":true}</pre>'
        self.assertFalse(smoke.boot_complete(smoke.read_probe(runtime_only)))

    @unittest.skipUnless(shutil.which('node'), 'The CI browser publisher installs Node.js')
    def test_injected_js_preserves_observed_markers_across_signout_navigation(self):
        # Executes the actual test-only injected JavaScript in Node's VM with
        # disposable DOM primitives. This is not a substitute for real Chrome.
        harness = r"""
const fs = require('node:fs');
const vm = require('node:vm');
const input = JSON.parse(fs.readFileSync(0,'utf8'));
const script = source => source.match(/<script>([\s\S]*?)<\/script>/)[1];
const events = new Map(), values = new Map(), marks = [];
const elements = new Map();
const document = {
    body: { append(el) { if(el.id) elements.set(el.id,el); } },
    createElement() { return { id: '', textContent: '', hidden: false }; },
    querySelector(selector) { return selector === '#browser-status'
        ? { dataset: { code: 'Loading' }, textContent: 'Opening 9to1' }
        : selector === '#nine-to-one-root' ? {} : null; },
    getElementById(id) { return elements.get(id); },
    querySelectorAll() { return []; }
};
const context = {
    document,
    location: { hash: '#/app.files', pathname: '/files/' },
    performance: { getEntriesByType: () => marks.map(name => ({name})) },
    sessionStorage: { setItem: (k,v) => values.set(k,v), getItem: k => values.get(k)||null },
    window: { addEventListener: (name,callback) => events.set(name,callback) },
    PerformanceObserver: class { observe() {} },
    setInterval: () => 1,
    JSON,
    Array
};
vm.runInNewContext(script(input.probe), context);
marks.push('9to1:host','9to1:runtime:ready','9to1:backend:ready');
events.get('pagehide')();
context.location.pathname='/account';
const destination = { id: input.id, textContent: '' };
elements.set(input.id, destination);
vm.runInNewContext(script(input.account), context);
console.log(destination.textContent);
"""
        result = subprocess.run([shutil.which('node'), '-e', harness],
            input=json.dumps({'probe': smoke.PROBE, 'account': smoke.ACCOUNT_LANDING, 'id':smoke.SNAPSHOT_ID}),
            text=True, capture_output=True, timeout=5, check=True)
        evidence = json.loads(result.stdout)
        self.assertTrue(evidence['redirectObserved'])
        self.assertEqual(evidence['landedAt'], '/account')
        self.assertTrue(smoke.boot_complete(evidence))
        self.assertEqual(evidence['markers'], ['9to1:host', '9to1:runtime:ready', '9to1:backend:ready'])

    def test_disconnected_fixture_context_is_bounded_and_cannot_mutate(self):
        with smoke.serve(self.root, scenario='disconnected') as (url, hits):
            context_url = url.replace('/files/', '/api/files/v1/context')
            with urlopen(context_url, timeout=2) as response:
                self.assertEqual(response.status, 200)
                payload = json.load(response)
                self.assertEqual(payload['connected'], False)
                self.assertEqual(payload['location']['kind'], 'ConnectedCloud')
                self.assertEqual(payload['location']['capabilities'], 'None')
                self.assertTrue(payload['location']['isAvailable'])
                self.assertEqual(payload['accountId'], 'disposable-first-use-only')
                self.assertNotIn('accessToken', payload)
                self.assertNotIn('refreshToken', payload)
            for endpoint in ('items', 'usage', 'connect', 'operations'):
                with self.assertRaises(HTTPError) as denied:
                    urlopen(context_url.replace('/context', '/' + endpoint), timeout=2)
                self.assertEqual(denied.exception.code, 403)
            self.assertEqual(hits['/api/files/v1/context'], 1)
            self.assertEqual(hits['/api/files/v1/items'], 1)
            with self.assertRaises(HTTPError) as denied_post:
                urlopen(Request(context_url, data=b'{}', method='POST'), timeout=2)
            self.assertEqual(denied_post.exception.code, 405)
        with self.assertRaises(ValueError):
            with smoke.serve(self.root, scenario='authenticated-real'):
                pass

    def test_disconnected_first_use_requires_observed_actual_cui_peers(self):
        observed = {'markers':['9to1:runtime:ready', '9to1:backend:ready'],
                    'hash':'#/app.files', 'rootExists': True, 'status': 'Ready', 'errors': [],
                    'filesControls':['Connect Google Drive', 'Files privacy', 'Refresh connection']}
        self.assertTrue(smoke.first_use_visible(observed))
        for value in ([], ['Files privacy'], ['Connect Google Drive'], ['Connect Google Drive', 'Files privacy', 'Disconnect Google Drive']):
            self.assertFalse(smoke.first_use_visible({**observed, 'filesControls': value}))
        self.assertFalse(smoke.first_use_visible({**observed, 'hash':'#/app.wave'}))
        self.assertFalse(smoke.first_use_visible({**observed, 'markers':['9to1:runtime:ready']}))
        self.assertFalse(smoke.first_use_visible({**observed, 'errors':['CUI failed']}))
        self.assertFalse(smoke.first_use_visible(None))

    @unittest.skipUnless(shutil.which('node'), 'The CI browser publisher installs Node.js')
    def test_probe_captures_only_real_accessibility_nodes(self):
        harness = r"""
const fs = require('node:fs'); const vm = require('node:vm');
const script = fs.readFileSync(0,'utf8').match(/<script>([\s\S]*?)<\/script>/)[1];
const marks = ['9to1:runtime:ready', '9to1:backend:ready'];
const labels = ['Connect Google Drive', 'Files privacy'];
const nodes = labels.map(label => ({ getAttribute: name => name === 'aria-label' ? label : null }));
const document = {
  body: { append(el) { document.bodyCapture=el; } },
  createElement() {return {textContent:'',hidden:false,id:''};},
  querySelector(sel) { return sel === '#nine-to-one-root' ? {} : {dataset:{code:'Ready'},textContent:'Ready'}; },
  querySelectorAll() {return nodes;}
};
const events = new Map();
const context = { document, location:{hash:'#/app.files'}, performance:{getEntriesByType:()=>marks.map(name=>({name}))},
  sessionStorage:{setItem(){}},window:{addEventListener:(name,fn)=>events.set(name,fn)},
  PerformanceObserver:class{observe(){}},setInterval:()=>1,JSON };
vm.runInNewContext(script, context);
const first = JSON.parse(document.bodyCapture.textContent);
nodes.pop(); events.get('pagehide')();
const second = JSON.parse(document.bodyCapture.textContent);
console.log(JSON.stringify({first,second}));
"""
        result = subprocess.run([shutil.which('node'), '-e', harness],
            input=smoke.PROBE, text=True, capture_output=True, timeout=5, check=True)
        evidence = json.loads(result.stdout)
        self.assertEqual(evidence['first']['filesControls'], ['Connect Google Drive', 'Files privacy'])
        self.assertEqual(evidence['second']['filesControls'], ['Connect Google Drive'])

if __name__ == '__main__':
    unittest.main()
