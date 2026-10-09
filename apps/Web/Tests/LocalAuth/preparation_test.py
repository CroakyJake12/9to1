#!/usr/bin/env python3
"""Public archive/preparation guards only; no private fixture, browser or issuer."""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import tempfile
import subprocess
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
        args = argparse.Namespace(candidate='a' * 40 + '\n', test_source_commit='e' * 40, fixture_source_commit='f' * 40, manifest_sha='b' * 64, seal_sha='c' * 64, archive_sha='d' * 64)
        with patch.object(prepare, 'command', side_effect=AssertionError('No subprocess permitted')):
            with self.assertRaises(RuntimeError):
                prepare.prepare(args)

    def test_invalid_separate_identity_precedes_commands_and_all_file_access(self):
        for field in ['test_source_commit', 'fixture_source_commit']:
            args = argparse.Namespace(candidate='a' * 40, test_source_commit='e' * 40, fixture_source_commit='f' * 40, manifest_sha='b' * 64, seal_sha='c' * 64, archive_sha='d' * 64)
            setattr(args, field, getattr(args, field) + '\n')
            with patch.object(prepare, 'command', side_effect=AssertionError('No subprocess permitted')):
                with self.assertRaises(RuntimeError):
                    prepare.prepare(args)

    def test_actual_git_head_and_original_body_are_both_required(self):
        def git(*args):
            return subprocess.check_output(['git', '-C', str(self.root), *args], env=prepare.credential_safe_environment(dict(os.environ)), stderr=subprocess.DEVNULL).decode().strip()
        git('init', '--quiet')
        source = self.root / 'public-source.js'
        source.write_bytes(self.body)
        git('add', '--', source.name)
        git('-c', 'user.name=Public preflight control', '-c', 'user.email=preflight@example.test', 'commit', '--quiet', '-m', 'Public source only')
        head = git('rev-parse', 'HEAD')
        row = {'path': source.name, 'bytes': len(self.body), 'sha256': hashlib.sha256(self.body).hexdigest()}
        self.assertEqual(prepare.reviewed_checkout(self.root, head, [row])[0]['sha256'], row['sha256'])
        with self.assertRaises(RuntimeError):
            prepare.reviewed_checkout(self.root, '0' * 40, [row])
        source.write_bytes(self.body + b' alternate')
        changed = {'path': source.name, 'bytes': source.stat().st_size, 'sha256': prepare.sha(source)}
        with self.assertRaises(RuntimeError):
            prepare.reviewed_checkout(self.root, head, [changed])

    def test_node_preload_and_pw_debug_are_disabled_before_custodian_spawns_node(self):
        inherited = {'DEBUG': 'pw:api', 'PWDEBUG': '1', 'NODE_DEBUG': '*', 'NODE_OPTIONS': '--require unreviewed-hook', 'PATH': 'original-public-tool-path'}
        safe = prepare.credential_safe_environment(inherited)
        for name in ['DEBUG', 'PWDEBUG', 'NODE_DEBUG', 'NODE_OPTIONS']:
            self.assertEqual(safe[name], '')
        self.assertEqual(safe['PATH'], inherited['PATH'])
        self.assertEqual(inherited['DEBUG'], 'pw:api')

    def test_socket_temp_path_boundary_and_unicode_refuse_before_creation(self):
        # execution/tmp62 bytes leaves45 bytes for Chromium's actual singleton suffix.
        base = len(os.fsencode(self.root)) + 1 + len('/tmp')
        safe = self.root / ('a' * (62 - base))
        long = self.root / ('b' * (63 - base))
        self.assertEqual(prepare.chromium_socket_temp_root(safe)['socketPathBytes'], 107)
        with self.assertRaises(RuntimeError):
            prepare.chromium_socket_temp_root(long)
        unicode = self.root / ('é' * ((63 - base + 1) // 2))
        self.assertLess(len(str(unicode / 'tmp')), 63)
        with self.assertRaises(RuntimeError):
            prepare.chromium_socket_temp_root(unicode)
        self.assertFalse(safe.exists())
        self.assertFalse(long.exists())
        self.assertFalse(unicode.exists())

    def test_overlength_setup_refuses_before_commands_tools_or_private_paths(self):
        args = argparse.Namespace(candidate='a' * 40, test_source_commit='e' * 40, fixture_source_commit='f' * 40, manifest_sha='b' * 64, seal_sha='c' * 64, archive_sha='d' * 64, execution_root=str(self.root / ('z' * 70)))
        with patch.object(prepare, 'command', side_effect=AssertionError('No subprocess permitted')):
            with self.assertRaises(RuntimeError):
                prepare.prepare(args)
        self.assertFalse(Path(args.execution_root).exists())


if __name__ == '__main__':
    unittest.main()
