import hashlib
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("rust_notices", Path(__file__).parents[1] / "collect-linux-rust-notices.py")
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


class RustNoticeEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.registry = self.root / "registry/src"
        self.package = self.registry / "fixture-index/donor-1.0.0"
        self.archive = self.root / "registry/cache/fixture-index/donor-1.0.0.crate"
        self.lock = self.root / "Cargo.lock"

    def create(self, extra=""):
        files = {"Cargo.toml": ('[package]\nname="donor"\nversion="1.0.0"\nlicense="MIT"\n' + extra).encode(),
                 "LICENSE": b"Exact fictional license fixture text\n"}
        self.package.mkdir(parents=True)
        self.archive.parent.mkdir(parents=True)
        with tarfile.open(self.archive, "w:gz") as packed:
            for name, data in files.items():
                (self.package / name).write_bytes(data)
                member = tarfile.TarInfo("donor-1.0.0/" + name)
                member.size = len(data)
                packed.addfile(member, io.BytesIO(data))
        checksum = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.lock.write_text('version=4\n[[package]]\nname="donor"\nversion="1.0.0"\n'
                             'source="registry+https://github.com/rust-lang/crates.io-index"\nchecksum="' + checksum + '"\n')

    def test_exact_archive_and_cached_text_are_both_verified(self):
        self.create()
        entries, texts = collector.collect([self.lock], self.registry)
        self.assertEqual("available-verified-text", entries[0]["status"])
        self.assertEqual("MIT", entries[0]["license"])
        self.assertEqual([b"Exact fictional license fixture text\n"], list(texts.values()))

    def test_mutated_extracted_notice_is_not_accepted_from_declared_identity(self):
        self.create()
        (self.package / "LICENSE").write_text("Changed local bytes")
        with self.assertRaisesRegex(ValueError, "differs from the checksum-pinned crate archive"):
            collector.collect([self.lock], self.registry)

    def test_mutated_archive_cannot_supply_verified_notice(self):
        self.create()
        with self.archive.open("ab") as stream:
            stream.write(b"mutation")
        entries, texts = collector.collect([self.lock], self.registry)
        self.assertEqual("matching-cached-source-or-pinned-archive-missing", entries[0]["status"])
        self.assertFalse(texts)

    def test_license_file_cannot_escape_source_root(self):
        self.create('license-file="../../outside"\n')
        with self.assertRaisesRegex(ValueError, "escapes its source directory"):
            collector.collect([self.lock], self.registry)

    def test_workspace_source_is_explicitly_unresolved(self):
        self.lock.write_text('version=4\n[[package]]\nname="local"\nversion="1.0.0"\n')
        entries, texts = collector.collect([self.lock], self.registry)
        self.assertEqual("owning-source-license-required", entries[0]["status"])
        self.assertFalse(texts)


if __name__ == "__main__":
    unittest.main()
