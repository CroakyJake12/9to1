"""Synthetic dependency/LFS tests, not donor builds or production runtime tests."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('dependencies', Path(__file__).with_name('acquire_dependencies.py'))
d = importlib.util.module_from_spec(spec)
spec.loader.exec_module(d)


class DependencyChecks(unittest.TestCase):
    def test_pointer_roundtrip(self):
        pointer = b'version https://git-lfs.github.com/spec/v1\noid sha256:' + b'a' * 64 + b'\nsize 42\n'
        self.assertEqual({'oid': 'a'*64, 'size': 42}, d.parse_pointer(pointer))

    def test_malformed_pointer_rejected(self):
        with self.assertRaises(RuntimeError):
            d.parse_pointer(b'version https://git-lfs.github.com/spec/v1\noid sha256:../../etc/passwd\nsize 2\n')

    def test_unsupported_pointer_extension_rejected(self):
        with self.assertRaises(RuntimeError):
            d.parse_pointer(b'not a pointer')

    def test_paths_are_parent_relative(self):
        self.assertEqual('a/b', d.safe_relative('a/b'))
        for path in ('../secret', '/etc/passwd', '.git/config', 'x/../../x', 'x\\y', ''):
            with self.assertRaises(RuntimeError):
                d.safe_relative(path)

    def test_payload_digest_and_size(self):
        with tempfile.TemporaryDirectory() as temp:
            p = Path(temp) / 'payload'
            p.write_bytes(b'actual bytes')
            pointer = {'oid': hashlib.sha256(p.read_bytes()).hexdigest(), 'size': p.stat().st_size}
            d.check_object(p, pointer)
            with self.assertRaisesRegex(RuntimeError, 'hash mismatch'):
                d.check_object(p, {**pointer, 'oid': '0'*64})
            with self.assertRaisesRegex(RuntimeError, 'size mismatch'):
                d.check_object(p, {**pointer, 'size': 1000})

    def test_missing_payload_is_not_success(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(RuntimeError, 'Missing LFS'):
                d.check_object(Path(temp)/'missing', {'size': 0, 'oid':'0'*64})

    def test_lock_has_exact_observed_dependencies(self):
        plans = json.loads((d.HERE/'dependencies.lock.json').read_text())
        self.assertEqual(7, sum(len(p['children']) for p in plans.values()))
        self.assertEqual(98, plans['vscode']['expected_lfs_paths'])
        for plan in plans.values():
            for child in plan['children']:
                d.safe_relative(child['path'])
                self.assertTrue(child['url'].startswith('https://'))
                self.assertEqual(40, len(child['commit']))

    def test_actual_nested_fetch_fixture_and_pin_mismatch(self):
        with tempfile.TemporaryDirectory() as temp:
            home = Path(temp)
            donor, parent = home/'upstream', home/'parent'
            for path in (donor, parent):
                path.mkdir()
                d.source.git(path,'init')
                d.source.git(path,'config','user.email','test@example.invalid')
                d.source.git(path,'config','user.name','Fixture')
            (donor/'LICENSE').write_text('fixture\n')
            (donor/'source.c').write_text('int fixture;\n')
            d.source.git(donor,'add','.')
            d.source.git(donor,'commit','-m','fixture')
            pin = d.source.git(donor,'rev-parse','HEAD').strip()
            (parent/'.gitmodules').write_text('[submodule "child"]\n path = child\n url = ../child\n')
            d.source.git(parent,'add','.')
            d.source.git(parent,'update-index','--add','--cacheinfo',f'160000,{pin},child')
            d.source.git(parent,'commit','-m','fixture parent')
            child = {'path':'child','commit':pin,'url':str(donor),'declared_url':'../child'}
            with self.assertRaisesRegex(RuntimeError,'pin differs'):
                d.clone_child(parent,{**child,'commit':'0'*40})
            d.clone_child(parent,child)
            result, entries = d.source.verify_source(parent/'child',pin)
            self.assertEqual(2,result['tracked_blobs_verified'])
            with self.assertRaisesRegex(RuntimeError,'overwrite'):
                d.clone_child(parent,child)


if __name__ == '__main__':
    unittest.main()
