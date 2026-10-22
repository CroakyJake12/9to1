"""Data-only recovery tests; no package installation or inference execution."""
import hashlib
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("provider_recovery", Path(__file__).with_name("recover-pinned-provider.py"))
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class ExactRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.data = b"unit test bytes only; not an engine"
        self.spec = {"assets": [{"path": "bin/llama-server", "installPath": "llama.cpp/llama-server",
                                 "expectedSize": len(self.data), "expectedSha256": hashlib.sha256(self.data).hexdigest()}]}

    def archive(self, name, data, symbolic=False):
        stream = io.BytesIO()
        with tarfile.open(fileobj=stream, mode="w") as archive:
            member = tarfile.TarInfo(name)
            member.size = len(data)
            if symbolic:
                member.type = tarfile.SYMTYPE
                member.linkname = "/outside"
                member.size = 0
            archive.addfile(member, None if symbolic else io.BytesIO(data))
        stream.seek(0)
        return tarfile.open(fileobj=stream, mode="r|")

    def test_recovers_only_pinned_identical_bytes(self):
        with self.archive("./usr/lib/haven/llama.cpp/llama-server", self.data) as archive:
            rows = recovery.recover_assets(archive, self.root / "recovered", self.spec)
        self.assertEqual(len(rows), 1)
        self.assertEqual((self.root / "recovered/bin/llama-server").read_bytes(), self.data)
        self.assertEqual((self.root / "recovered/bin/llama-server").stat().st_mode & 0o777, 0o755)

    def test_wrong_content_missing_or_symbolic_assets_are_refused(self):
        for name, data, symbolic in [("usr/lib/haven/llama.cpp/llama-server", b"wrong bytes", False),
                                      ("unrelated", self.data, False),
                                      ("usr/lib/haven/llama.cpp/llama-server", self.data, True)]:
            with self.subTest(name=name, symbolic=symbolic):
                with self.archive(name, data, symbolic) as archive, self.assertRaises(ValueError):
                    recovery.recover_assets(archive, self.root / ("out" + str(len(data)) + str(symbolic)), self.spec)

    def test_deterministic_inner_preserves_bytes_and_executable_mode(self):
        source = self.root / "assets"
        (source / "bin").mkdir(parents=True)
        (source / "bin/llama-server").write_bytes(self.data)
        first, second = self.root / "one.tar.gz", self.root / "two.tar.gz"
        recovery.deterministic_inner(source, first)
        recovery.deterministic_inner(source, second)
        self.assertEqual(recovery.digest(first), recovery.digest(second))
        with tarfile.open(first) as archive:
            self.assertEqual(archive.getmember("bin/llama-server").mode, 0o755)
            self.assertEqual(archive.extractfile("bin/llama-server").read(), self.data)

    def test_zip_member_must_match_identity_and_remain_bounded(self):
        archive = self.root / "source.zip"
        with zipfile.ZipFile(archive, "w") as z:
            z.writestr("source.tar.gz", self.data)
        with self.assertRaises(ValueError):
            recovery.extract_exact_zip_member(archive, "source.tar.gz", self.root / "bad", "0" * 64, len(self.data))
        recovery.extract_exact_zip_member(archive, "source.tar.gz", self.root / "good", hashlib.sha256(self.data).hexdigest(), len(self.data))


if __name__ == "__main__":
    unittest.main()
