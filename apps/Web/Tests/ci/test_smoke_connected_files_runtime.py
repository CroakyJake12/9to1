import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen

PATH = Path(__file__).parent
spec = importlib.util.spec_from_file_location('connected', PATH / 'smoke-connected-files-runtime.py')
connected = importlib.util.module_from_spec(spec)
spec.loader.exec_module(connected)


class ConnectedFilesSmokeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for name in connected.smoke.REQUIRED:
            path = self.root / name
            path.parent.mkdir(exist_ok=True)
            path.write_text('<html><body><main id="nine-to-one-root"></main></body></html>'
                            if name.endswith('.html') else 'safe')

    def test_context_and_canonical_root_items_through_real_http(self):
        with connected.serve_connected(self.root) as (url, hits):
            api = url.replace('/files/', '/api/files/v1/')
            with urlopen(api + 'context', timeout=3) as response:
                self.assertEqual(response.status, 200)
                account = json.load(response)
            self.assertTrue(account['connected'])
            self.assertEqual(account['accountId'], connected.OWNER)
            self.assertIn('Read', account['location']['capabilities'])
            for key in ('accessToken', 'refreshToken', 'cookie', 'secret'):
                self.assertNotIn(key, json.dumps(account))
            with urlopen(api + 'items', timeout=3) as response:
                listing = json.load(response)
            self.assertIsNone(listing['nextPageToken'])
            self.assertEqual([item['name'] for item in listing['items']],
                             [connected.FOLDER_NAME, connected.FILE_NAME])
            for item in listing['items']:
                self.assertEqual(item['locationId']['value'], connected.LOCATION_ID)
                self.assertEqual(item['ownerPrincipalId'], connected.OWNER)
                self.assertIsNone(item['parentId'])
                self.assertEqual(item['metadataSchemaVersion'], 1)
                self.assertTrue(item['currentRevisionId']['value'])
            self.assertEqual(hits['/api/files/v1/context'], 1)
            self.assertEqual(hits['/api/files/v1/items'], 1)

    def test_nested_list_get_search_and_identity_survive_repeated_reads(self):
        with connected.serve_connected(self.root) as (url, _):
            api = url.replace('/files/', '/api/files/v1/')
            first = json.load(urlopen(api + 'items', timeout=3))
            second = json.load(urlopen(api + 'items?query=PROJECT', timeout=3))
            self.assertEqual(second['items'], [connected.FOLDER])
            nested = json.load(urlopen(api + 'items?parentId=' + connected.FOLDER_ID, timeout=3))
            self.assertEqual(len(nested['items']), 1)
            self.assertEqual(nested['items'][0]['parentId']['value'], connected.FOLDER_ID)
            got = json.load(urlopen(api + 'items/' + connected.FOLDER_ID, timeout=3))
            self.assertIsNone(got['error'])
            self.assertEqual(got['value'], first['items'][0])
            self.assertEqual(first, json.load(urlopen(api + 'items', timeout=3)))
            self.assertEqual(json.load(urlopen(api + 'items?query=missing', timeout=3))['items'], [])

    def test_all_provider_writes_and_nonfixture_reads_are_denied(self):
        with connected.serve_connected(self.root) as (url, hits):
            api = url.replace('/files/', '/api/files/v1/')
            for method, path in [('POST', 'operations'), ('PUT', 'uploads/' + connected.FOLDER_ID + '/content'),
                                 ('POST', 'connect'), ('POST', 'disconnect'), ('POST', 'uploads'),
                                 ('GET', 'downloads'), ('GET', 'changes'), ('GET', 'usage')]:
                with self.subTest(method=method, path=path):
                    request = Request(api + path, method=method,
                                      data=b'{}' if method != 'GET' else None)
                    with self.assertRaises(HTTPError) as denied:
                        urlopen(request, timeout=3)
                    self.assertIn(denied.exception.code, (403, 405))
            self.assertEqual(hits['/api/files/v1/operations'], 1)
            for bad in ('items?pageToken=not-authorized', 'items?trash=true',
                        'items?parentId=00000000-0000-4000-8000-000000000000'):
                with self.assertRaises(HTTPError) as denied:
                    urlopen(api + bad, timeout=3)
                self.assertIn(denied.exception.code, (400, 404))

    def test_only_served_document_is_instrumented(self):
        original = (self.root / 'index.html').read_bytes()
        with connected.serve_connected(self.root) as (url, _):
            page = urlopen(url, timeout=3).read().decode('utf-8')
            self.assertIn('files-runtime-smoke-evidence', page)
            self.assertIn(connected.FOLDER_NAME, page)
            self.assertIn(connected.FILE_NAME, page)
            self.assertIn('document.querySelectorAll', page)
        self.assertEqual((self.root / 'index.html').read_bytes(), original)

    def test_real_native_peer_validator_refuses_false_positive(self):
        accepted = {'markers': ['9to1:runtime:ready', '9to1:backend:ready'],
                    'rootExists': True, 'hash': '#/app.files', 'errors': [], 'status': 'Ready',
                    'filesControls': ['Disconnect Google Drive', 'Files privacy',
                                      connected.FOLDER_NAME, connected.FILE_NAME]}
        self.assertTrue(connected.connected_visible(accepted))
        for absent in ('Disconnect Google Drive', 'Files privacy', connected.FOLDER_NAME, connected.FILE_NAME):
            self.assertFalse(connected.connected_visible({**accepted,
                'filesControls': [x for x in accepted['filesControls'] if x != absent]}))
        self.assertFalse(connected.connected_visible({**accepted,
            'filesControls': accepted['filesControls'] + ['Connect Google Drive']}))
        for key, value in [('hash', '#/app.other'), ('errors', ['native render failed']),
                           ('rootExists', False), ('status', 'Loading'), ('markers', ['9to1:runtime:ready'])]:
            self.assertFalse(connected.connected_visible({**accepted, key: value}))
        self.assertFalse(connected.connected_visible(None))

    @unittest.skipUnless(shutil.which('node'), 'Node is needed for real injected-JavaScript execution')
    def test_extended_probe_reads_actual_dom_peers_only(self):
        script = r'''
const fs = require('node:fs'), vm = require('node:vm');
const html = fs.readFileSync(0, 'utf8');
const js = html.match(/<script>([\s\S]*?)<\/script>/)[1];
const labels = ['Disconnect Google Drive', 'Files privacy', 'Project Atlas', 'draft-notes.txt'];
const document = {
 body: {append(el) {this.record = el;}}, createElement() {return {id:'', hidden:false, textContent:''};},
 querySelector(sel) {return sel === '#nine-to-one-root' ? {} : {dataset:{code:'Ready'}, textContent:'Ready'};},
 querySelectorAll() {return labels.map(label=>({getAttribute:key=>key==='aria-label'?label:null}));}
};
const context={document,performance:{getEntriesByType:()=>['9to1:runtime:ready','9to1:backend:ready'].map(name=>({name}))},
 location:{hash:'#/app.files'},window:{addEventListener(){}},sessionStorage:{setItem(){}},
 PerformanceObserver: class {observe(){}}, setInterval(){},JSON};
vm.runInNewContext(js,context);
const recorded=JSON.parse(document.body.record.textContent);
labels.pop();
console.log(JSON.stringify({recorded,afterRemoval:labels}));
'''
        result = subprocess.run([shutil.which('node'), '-e', script], input=connected.connected_probe(),
                                capture_output=True, text=True, timeout=5, check=True)
        observed = json.loads(result.stdout)['recorded']
        self.assertTrue(connected.connected_visible(observed))
        self.assertEqual(observed['filesControls'], ['Disconnect Google Drive', 'Files privacy',
                                                    connected.FOLDER_NAME, connected.FILE_NAME])


if __name__ == '__main__':
    unittest.main()
