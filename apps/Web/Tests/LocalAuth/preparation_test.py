#!/usr/bin/env python3
"""Public archive/preparation guards only; no private fixture, browser or issuer."""
import argparse
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('local_auth_prepare', Path(__file__).with_name('prepare-local-auth-plan.py'))
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


class PreparationControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='c2-public-archive-control-')
        self.root = Path(self.temporary.name)
        self.archive = self.root / 'public.zip'
        self.body = b'Public preflight asset only; never executed.'
        self.manifest = {'fileCount': 1, 'publishFiles': [{'path': 'nested/main.js', 'bytes': len(self.body), 'sha256': hashlib.sha256(self.body).hexdigest()}]}

    def tearDown(self):
        self.temporary.cleanup()

    def write_archive(self, name='nested/main.js', body=None, extra=False, symlink=False):
        with zipfile.ZipFile(self.archive, 'x', zipfile.ZIP_DEFLATED) as archive:
            info = zipfile.ZipInfo(name)
            if symlink:
                info.external_attr = (0o120777 << 16)
            archive.writestr(info, self.body if body is None else body)
            if extra:
                archive.writestr('unlisted.js', b'unlisted')

    def test_complete_original_archive_only_preserves_exact_body(self):
        self.write_archive()
        output = self.root / 'assets'
        prepare.receive_public_archive(self.archive, self.manifest, output)
        self.assertEqual((output / 'nested/main.js').read_bytes(), self.body)

    def test_traversal_refuses_before_extraction(self):
        self.write_archive('../escape')
        manifest = {'fileCount': 1, 'publishFiles': [{**self.manifest['publishFiles'][0], 'path': '../escape'}]}
        with self.assertRaises(RuntimeError):
            prepare.receive_public_archive(self.archive, manifest, self.root / 'assets')
        self.assertFalse((self.root / 'assets').exists())

    def test_unlisted_archive_member_refuses_before_extraction(self):
        self.write_archive(extra=True)
        with self.assertRaises(RuntimeError):
            prepare.receive_public_archive(self.archive, self.manifest, self.root / 'assets')
        self.assertFalse((self.root / 'assets').exists())

    def test_body_hash_and_symlink_member_refuse(self):
        self.write_archive(body=b'x' * len(self.body))
        with self.assertRaises(RuntimeError):
            prepare.receive_public_archive(self.archive, self.manifest, self.root / 'assets')
        self.archive.unlink()
        self.write_archive(symlink=True)
        with self.assertRaises(RuntimeError):
            prepare.receive_public_archive(self.archive, self.manifest, self.root / 'assets')

    def test_decompression_size_refuses_before_any_member_read(self):
        self.write_archive(body=b'larger-than-approved')
        with patch.object(zipfile.ZipFile, 'read', side_effect=AssertionError('Unapproved read must not occur')):
            with self.assertRaises(RuntimeError):
                prepare.receive_public_archive(self.archive, self.manifest, self.root / 'assets')
        self.assertFalse((self.root / 'assets').exists())

    def test_invalid_explicit_candidate_precedes_commands_and_all_file_access(self):
        args = argparse.Namespace(candidate='a' * 40 + '\n', manifest_sha='b' * 64, seal_sha='c' * 64, archive_sha='d' * 64)
        with patch.object(prepare, 'command', side_effect=AssertionError('No subprocess permitted')):
            with self.assertRaises(RuntimeError):
                prepare.prepare(args)

    def test_node_preload_and_pw_debug_are_disabled_before_custodian_spawns_node(self):
        inherited = {'DEBUG': 'pw:api', 'PWDEBUG': '1', 'NODE_DEBUG': '*', 'NODE_OPTIONS': '--require unreviewed-hook', 'PATH': 'original-public-tool-path'}
        safe = prepare.credential_safe_environment(inherited)
        for name in ['DEBUG', 'PWDEBUG', 'NODE_DEBUG', 'NODE_OPTIONS']:
            self.assertEqual(safe[name], '')
        self.assertEqual(safe['PATH'], inherited['PATH'])
        self.assertEqual(inherited['DEBUG'], 'pw:api')


if __name__ == '__main__':
    unittest.main()
