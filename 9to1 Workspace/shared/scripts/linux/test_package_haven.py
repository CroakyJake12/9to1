"""Small packaging-contract checks; the ELF fixtures are not a runnable .NET app."""
import hashlib
import json
import os
import pathlib
import shutil
import struct
import subprocess
import tempfile
import unittest


SCRIPT = pathlib.Path(__file__).with_name("package-haven.sh")


@unittest.skipUnless(all(shutil.which(tool) for tool in ("cc", "dpkg", "dpkg-deb")), "requires cc and Debian package tools")
class PackageHavenTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = tempfile.TemporaryDirectory(prefix="haven-elf-fixture-")
        cls.fixture_root = pathlib.Path(cls.fixture.name)
        source = cls.fixture_root / "host.c"
        source.write_text('const char entry[] = "Haven.dll"; int main(void) { return entry[0] != \'H\'; }\n')
        subprocess.run(["cc", str(source), "-o", str(cls.fixture_root / "Haven")], check=True)
        source.write_text("int fixture(void) { return 0; }\n")
        subprocess.run(["cc", "-shared", "-fPIC", str(source), "-o", str(cls.fixture_root / "runtime.so")], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.fixture.cleanup()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="haven-package-smoke-")
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.runtime = self.root / "publish"
        self.runtime.mkdir()
        shutil.copy2(self.fixture_root / "Haven", self.runtime / "Haven")
        for name in ("libcoreclr.so", "libhostfxr.so", "libhostpolicy.so"):
            shutil.copy2(self.fixture_root / "runtime.so", self.runtime / name)
        for name in ("Haven.dll", "System.Private.CoreLib.dll"):
            (self.runtime / name).write_bytes(b"fixture metadata only")
        self.config = self.runtime / "Haven.runtimeconfig.json"
        self.config.write_text(json.dumps({"runtimeOptions": {"tfm": "net10.0", "includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.12"}]}}))
        (self.runtime / "Haven.deps.json").write_text(json.dumps({"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-x64"}}))
        self.output = self.root / "packages"
        self.version = "0.1.0+git1234"

    def run_package(self, version=None, output=None):
        return subprocess.run(["bash", str(SCRIPT), str(self.runtime), str(output or self.output), version or self.version], capture_output=True, text=True, preexec_fn=lambda: os.umask(0o077))

    def assert_refused(self, result, message):
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn(message, result.stderr)

    def test_real_elf_fixture_builds_amd64_package_with_preserved_identity_and_no_hooks(self):
        result = self.run_package()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        package = self.output / ("haven_" + self.version + "_amd64.deb")
        fields = subprocess.check_output(["dpkg-deb", "-f", str(package), "Package", "Version", "Architecture", "Depends"], text=True)
        self.assertIn("Package: haven", fields)
        self.assertIn("Version: " + self.version, fields)
        self.assertIn("Architecture: amd64", fields)
        self.assertIn("libfontconfig1", fields)
        self.assertIn("libssl3t64 | libssl3", fields)
        self.assertNotIn("chromium", fields)
        extracted = self.root / "extracted"
        subprocess.run(["dpkg-deb", "-x", str(package), str(extracted)], check=True)
        self.assertEqual((self.runtime / "Haven").read_bytes(), (extracted / "usr/lib/haven/Haven").read_bytes())
        self.assertIn("exec", (extracted / "usr/bin/haven").read_text())
        stage = next(self.output.glob(".haven-package-stage-*"))
        self.assertEqual(["control"], sorted(path.name for path in (stage / "DEBIAN").iterdir()))

    def test_existing_package_is_preserved(self):
        self.output.mkdir()
        package = self.output / ("haven_" + self.version + "_amd64.deb")
        package.write_bytes(b"existing owner package")
        digest = hashlib.sha256(package.read_bytes()).hexdigest()
        self.assert_refused(self.run_package(), "Refusing to overwrite")
        self.assertEqual(digest, hashlib.sha256(package.read_bytes()).hexdigest())
        self.assertEqual([], list(self.output.glob(".haven-package-stage-*")))

    def test_foreign_elf_is_refused(self):
        host = self.runtime / "Haven"
        data = bytearray(host.read_bytes())
        struct.pack_into("<H", data, 18, 183)
        host.write_bytes(data)
        self.assert_refused(self.run_package(), "x86-64 Linux ELF")

    def test_framework_dependent_payload_is_refused(self):
        self.config.write_text(json.dumps({"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.12"}}}))
        self.assert_refused(self.run_package(), "framework-dependent")

    def test_missing_runtime_file_is_refused(self):
        (self.runtime / "libcoreclr.so").unlink()
        self.assert_refused(self.run_package(), "missing self-contained runtime file: libcoreclr.so")

    def test_invalid_version_cannot_inject_control_fields_or_paths(self):
        for version in ("1.0\nDepends: injected", "1/../other", "not-a-version"):
            with self.subTest(version=version):
                self.assert_refused(self.run_package(version=version), "Invalid Debian package version")
        self.assertFalse(self.output.exists())

    def test_recursive_output_is_refused_before_staging(self):
        output = self.runtime / "packages"
        self.assert_refused(self.run_package(output=output), "outside the published runtime")
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
