"""Real tiny ARM/AMD DEBs and immutable files; no app/native/provider execution."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("test_manifest", Path(__file__).with_name("test_build_manifest.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class TestBuildManifest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.root.chmod(0o700)
        self.revision, self.patch = "a" * 40, "b" * 64
        self.package = self.make_deb("arm64")
        self.source_file = self.root / "product-source.tar.gz"
        self.source_file.write_bytes(b"fixture corresponding source only")
        self.native_source = self.root / "rnote-source.tar.gz"
        self.native_source.write_bytes(b"fixture native source and licence only")
        self.native_id = {"bytes": 64, "sha256": "c" * 64}
        self.native = {"schema": "9to1.rnote-native-stage/v1", "source_head": self.revision,
            "status": "STAGED_STATICALLY_VERIFIED_NOT_RUNTIME_ACCEPTED", "release_ready": False,
            "inspection": {"library": self.native_id, "dependencies": ["libfixture (>= 1.2)"]},
            "corresponding_source": {"path": str(self.native_source), "identity": m.identity(self.native_source)}}
        self.native_path = self.write("native.json", self.native)
        self.package_receipt = {"source_head": self.revision, "source_patch_sha256": self.patch,
            "package_status": "ASSEMBLED_NOT_NATIVE_ACCEPTED", "architecture": "arm64", "package": str(self.package),
            **m.identity(self.package), "rnote_native": {"status": "PACKAGE_NATIVE_BYTES_AND_DEPENDENCIES_VERIFIED_NOT_EXECUTED",
                "dependencies": self.native["inspection"]["dependencies"], "members": {"opt/9to1-preview/libcakeos_canvas_rnote_poc.so": self.native_id}}}
        self.build = {"source_head": self.revision, "source_patch_sha256": self.patch,
            "status": "PASS_BUILD_AND_PACKAGE_ONLY_NOT_NATIVE_APP_ACCEPTED", "package": self.package_receipt,
            "stages": [{"name": "fixture-only", "exit_code": 0}],
            "rnote_native_stage": {"sha256": m.identity(self.native_path)["sha256"]}}
        self.smoke = {"source_revision": self.revision, "result": "PASS", "exit_code": 0,
            "command": ["fixture owning test"], "tests": {"total": 2, "passed": 2, "failed": 0, "skipped": 0}}
        self.scope = {"source_revision": self.revision, "status": "ALL_REQUESTED_BOARDS_UI_APPLIED",
                      "requirements": ["fixture toolbar scope"], "reviewer": "fixture Root"}
        self.source = {"source_commit": self.revision, "archive": self.source_file.name, **m.identity(self.source_file)}
        self.arguments = {"package_receipt": self.write("package.json", self.package_receipt),
            "build_receipt": self.write("build.json", self.build), "smoke_receipt": self.write("smoke.json", self.smoke),
            "scope_evidence": self.write("scope.json", self.scope), "native_receipt": self.native_path,
            "source_receipt": self.write("source.json", self.source), "drive_id": "fixture-drive",
            "folder_id": "fixture-existing-folder", "limitations": ["ARM/GNOME application execution NOT RUN"]}

    def write(self, name, value):
        path = self.root / name
        path.write_text(json.dumps(value))
        return path

    def make_deb(self, architecture):
        tree = self.root / architecture
        (tree / "DEBIAN").mkdir(parents=True)
        (tree / "DEBIAN").chmod(0o755)
        (tree / "DEBIAN/control").write_text(f"Package: 9to1-preview\nVersion: 0.1~fixture\nArchitecture: {architecture}\nMaintainer: Fixture <fixture@example.invalid>\nDescription: Manifest fixture only\n")
        package = self.root / (architecture + ".deb")
        subprocess.run(["dpkg-deb", "--build", "--root-owner-group", str(tree), str(package)], check=True, capture_output=True)
        return package

    def test_manifest_binds_actual_bytes_but_never_grants_release_or_delivery(self):
        result = m.make_manifest(**self.arguments)
        self.assertEqual("TEST_BUILD_NOT_ACCEPTED", result["status"])
        self.assertFalse(result["release_ready"])
        self.assertEqual("PAUSED", result["acceptance"]["full_independent"])
        self.assertEqual("NOT RUN", result["private_delivery"]["laptop_receipt"])
        self.assertEqual(m.identity(self.package)["sha256"], result["package"]["sha256"])
        output = self.root / "immutable.json"
        m.write_new(output, result)
        self.assertEqual(0o600, output.stat().st_mode & 0o777)
        before = output.read_bytes()
        with self.assertRaises(FileExistsError):
            m.write_new(output, {"status": "READY"})
        self.assertEqual(before, output.read_bytes())

    def test_changed_package_receipt_source_and_native_sources_are_refused(self):
        self.package.write_bytes(b"tampered")
        with self.assertRaisesRegex(ValueError, "DEB differs"):
            m.make_manifest(**self.arguments)
        self.package_receipt.update(m.identity(self.package))
        self.write("package.json", self.package_receipt)
        with self.assertRaises(subprocess.CalledProcessError):
            m.make_manifest(**self.arguments)

    def test_actual_wrong_architecture_refused_even_when_receipts_agree(self):
        wrong = self.make_deb("amd64")
        self.package_receipt.update(package=str(wrong), **m.identity(wrong))
        self.write("package.json", self.package_receipt)
        self.build["package"] = self.package_receipt
        self.write("build.json", self.build)
        with self.assertRaisesRegex(ValueError, "name/architecture"):
            m.make_manifest(**self.arguments)

    def test_full_thread_ui_review_and_successful_exact_source_smoke_required(self):
        for key, value in (("scope.json", {**self.scope, "status": "NEWEST_CHANGE_ONLY"}),
                           ("scope.json", {**self.scope, "source_revision": "d" * 40}),
                           ("smoke.json", {**self.smoke, "source_revision": "d" * 40}),
                           ("smoke.json", {**self.smoke, "tests": {"total": 0, "passed": 0, "failed": 0, "skipped": 0}})):
            with self.subTest(key=key, value=value):
                self.write(key, value)
                with self.assertRaises(ValueError):
                    m.make_manifest(**self.arguments)
                self.write("scope.json", self.scope)
                self.write("smoke.json", self.smoke)

    def test_missing_native_corresponding_source_and_unknown_limitations_refused(self):
        self.native_source.unlink()
        with self.assertRaises(OSError):
            m.make_manifest(**self.arguments)
        self.native_source.write_bytes(b"fixture native source and licence only")
        with self.assertRaisesRegex(ValueError, "limitations"):
            m.make_manifest(**{**self.arguments, "limitations": []})

    def test_bundled_components_require_their_own_sources(self):
        self.package_receipt["inference"] = {"bundled": True}
        self.write("package.json", self.package_receipt)
        self.build["package"] = self.package_receipt
        self.write("build.json", self.build)
        with self.assertRaisesRegex(ValueError, "engine_source"):
            m.make_manifest(**self.arguments)
        result = m.make_manifest(**self.arguments, engine_source=self.source_file)
        self.assertIn("engine_source", result["supporting_files"])

    def test_public_output_directory_and_symlink_refused(self):
        result = m.make_manifest(**self.arguments)
        directory = self.root / "shared"
        directory.mkdir(mode=0o755)
        directory.chmod(0o755)
        with self.assertRaisesRegex(ValueError, "owner-only"):
            m.write_new(directory / "manifest.json", result)
        link = self.root / "link.json"
        link.symlink_to(self.arguments["package_receipt"])
        with self.assertRaises(OSError):
            m.load(link)


if __name__ == "__main__":
    unittest.main()
