"""Synthetic Git-fixture tests; these are not donor build or runtime tests."""
import importlib.util
import json
import os
from pathlib import Path
import tarfile
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('acquire', Path(__file__).with_name('acquire.py'))
a = importlib.util.module_from_spec(spec)
spec.loader.exec_module(a)


class SourceChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / 'donor'
        self.source.mkdir()
        a.git(self.source, 'init')
        a.git(self.source, 'config', 'user.name', 'Source test')
        a.git(self.source, 'config', 'user.email', 'test@example.invalid')
        a.raw_checkout(self.source)
        for name, text in {'LICENSE':'Fixture license\n', 'main.c':'int main(void) {return 0;}\n',
                           '.gitattributes':'hidden.txt export-ignore\nmain.c text eol=crlf\n',
                           'hidden.txt':'Must be archived.\n'}.items():
            (self.source / name).write_text(text)
        os.symlink('main.c', self.source / 'link')
        a.git(self.source, 'add', '.')
        a.git(self.source, 'commit', '-m', 'fixture')
        self.pin = a.git(self.source, 'rev-parse', 'HEAD').strip()

    def tearDown(self):
        self.temp.cleanup()

    def verify(self):
        return a.verify_source(self.source, self.pin)

    def test_every_blob_and_symlink_checked(self):
        result, entries = self.verify()
        self.assertEqual(5, result['tracked_blobs_verified'])
        self.assertTrue(result['every_blob_matches_git_object'])
        self.assertIn('LICENSE', result['license_files_preserved'])

    def test_missing_file_fails(self):
        (self.source / 'main.c').unlink()
        with self.assertRaises(OSError):
            self.verify()

    def test_dirty_blob_fails(self):
        (self.source / 'main.c').write_text('changed')
        with self.assertRaisesRegex(RuntimeError, 'modified source blob'):
            self.verify()

    def test_wrong_pin_fails(self):
        with self.assertRaisesRegex(RuntimeError, 'Wrong donor revision'):
            a.verify_source(self.source, '0'*40)

    def test_assume_unchanged_fails(self):
        a.git(self.source, 'update-index', '--assume-unchanged', 'main.c')
        with self.assertRaisesRegex(RuntimeError, 'Hidden or sparse'):
            self.verify()

    def test_skip_worktree_fails(self):
        a.git(self.source, 'update-index', '--skip-worktree', 'main.c')
        with self.assertRaisesRegex(RuntimeError, 'Hidden or sparse'):
            self.verify()

    def test_staged_changes_fail(self):
        (self.source / 'main.c').write_text('changed')
        a.git(self.source, 'add', 'main.c')
        with self.assertRaisesRegex(RuntimeError, 'Staged donor modifications'):
            self.verify()

    def test_mode_change_fails(self):
        (self.source / 'main.c').chmod(0o755)
        with self.assertRaisesRegex(RuntimeError, 'Executable mode'):
            self.verify()

    def test_symlink_replacement_fails(self):
        (self.source / 'main.c').unlink()
        os.symlink('/etc/passwd', self.source / 'main.c')
        with self.assertRaisesRegex(RuntimeError, 'Expected regular source'):
            self.verify()

    def test_archive_keeps_export_ignored_blobs(self):
        result, entries = self.verify()
        out = self.root / 'out'
        result = a.package(self.source, {'id':'test', 'commit':self.pin, 'paths':['target']}, entries, out)
        archive = out / 'restored.tar.gz'
        archive.write_bytes(b''.join((out / p['file']).read_bytes() for p in result['parts']))
        self.assertEqual(result['archive_sha256'], a.sha256_file(archive))
        with tarfile.open(archive) as tar:
            self.assertEqual({e['path'] for e in entries}, set(tar.getnames()))
            self.assertEqual(b'Must be archived.\n', tar.extractfile('hidden.txt').read())
            self.assertTrue(tar.getmember('link').issym())

    def test_nested_gitlink_and_lfs_are_not_claimed_fetched(self):
        pointer = self.source / 'asset.bin'
        pointer.write_text('version https://git-lfs.github.com/spec/v1\noid sha256:'+'a'*64+'\nsize 1024\n')
        a.git(self.source, 'add', 'asset.bin')
        a.git(self.source, 'update-index', '--add', '--cacheinfo', '160000,'+'b'*40+',nested')
        a.git(self.source, 'commit', '-m', 'dependency fixtures')
        self.pin = a.git(self.source, 'rev-parse', 'HEAD').strip()
        result, _ = self.verify()
        self.assertEqual({'nested':'b'*40}, result['nested_git_dependencies_not_fetched'])
        self.assertEqual(['asset.bin'], result['lfs_payloads_not_fetched'])

    def test_empty_directory_is_not_a_checkout(self):
        empty = self.root / 'empty'
        empty.mkdir()
        with self.assertRaisesRegex(RuntimeError, 'not been fetched'):
            a.verify_source(empty, self.pin)

    def test_all_locked_paths_are_unique(self):
        donors = a.load_lock()
        self.assertEqual(18, len(donors))
        self.assertEqual(23, sum(len(d['paths']) for d in donors))

    def test_unsafe_lock_rejected(self):
        lock = {'schema':1, 'donors':[{'id':'test','commit':'a'*40,
                 'url':'https://github.com/CroakyJake12/test.git','paths':['9to1 OS/../../escape']}]}
        path = self.root / 'lock.json'
        path.write_text(json.dumps(lock))
        with self.assertRaisesRegex(RuntimeError, 'Unsafe donor path'):
            a.load_lock(path)


if __name__ == '__main__':
    unittest.main()
