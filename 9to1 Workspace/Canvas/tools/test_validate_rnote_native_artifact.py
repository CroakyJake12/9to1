"""Synthetic binary/source controls; none execute a native library or imply ARM runtime success."""
import contextlib
import hashlib
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

import validate_rnote_native_artifact as check


def elf(path, machine=62, exports=(), needed=(), rpath=None):
    strings = bytearray(b"\0")

    def add(text):
        offset = len(strings)
        strings.extend(text.encode() + b"\0")
        return offset

    symbols = bytearray(24)  # undefined null symbol must never count as an export
    for name in exports:
        symbols.extend(struct.pack("<IBBHQQ", add(name), 0x12, 0, 1, 0, 0))
    dynamic = b"".join(struct.pack("<qQ", 1, add(name)) for name in needed)
    if rpath is not None:
        dynamic += struct.pack("<qQ", 29, add(rpath))
    dynamic += struct.pack("<qQ", 0, 0)
    body = bytearray(64 + 4 * 64)
    body[:16] = b"\x7fELF\x02\x01\x01" + bytes(9)
    struct.pack_into("<HHIQQQIHHHHHH", body, 16, 3, machine, 1, 0, 0, 64, 0, 64, 0, 0, 64, 4, 0)
    for index, kind, content, link, entry_size in ((1, 3, strings, 0, 0), (2, 11, symbols, 1, 24), (3, 6, dynamic, 1, 16)):
        struct.pack_into("<IIQQQQIIQQ", body, 64 + index * 64, 0, kind, 0, 0, len(body), len(content), link, 0, 8, entry_size)
        body.extend(content)
    path.write_bytes(body)


class NativeArtifactTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def test_core_exports_read_on_both_elf_architectures_without_execution(self):
        for machine in (62, 183):
            path = self.root / f"native-{machine}.so"
            elf(path, machine, check.CORE)
            self.assertEqual(check.CORE, set(check.inspect_elf(path)["exports"]))
            self.assertEqual(machine, check.inspect_elf(path)["machine"])

    def test_malformed_header_and_truncated_sections_refuse(self):
        path = self.root / "bad.so"
        for data in (b"MZ", b"\x7fELF\x02\x02\x01" + bytes(100)):
            path.write_bytes(data)
            with self.assertRaises(ValueError):
                check.inspect_elf(path)
        elf(path)
        path.write_bytes(path.read_bytes()[:-1])
        with self.assertRaises(ValueError):
            check.inspect_elf(path)

    def test_nonregular_input_and_invalid_header_size_refuse_before_data_use(self):
        with self.assertRaisesRegex(ValueError, "regular file"):
            check.inspect_elf(self.root)
        path = self.root / "bad-header.so"
        elf(path)
        data = bytearray(path.read_bytes())
        struct.pack_into("<H", data, 52, 32)
        path.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "header size"):
            check.inspect_elf(path)

    def test_undefined_import_with_core_name_is_not_a_native_export(self):
        path = self.root / "undefined.so"
        elf(path, exports=["cake_canvas_engine_new"])
        data = bytearray(path.read_bytes())
        symbol_offset = struct.unpack_from("<Q", data, 64 + 2 * 64 + 24)[0]
        struct.pack_into("<H", data, symbol_offset + 24 + 6, 0)  # SHN_UNDEF
        path.write_bytes(data)
        self.assertNotIn("cake_canvas_engine_new", check.inspect_elf(path)["exports"])

    def test_full_transitive_closure_retains_bytes_and_hashes(self):
        root, middle, leaf = (self.root / name for name in ("bridge.so", "middle.so", "leaf.so"))
        elf(root, exports=check.CORE, needed=[middle.name])
        elf(middle, needed=[leaf.name])
        elf(leaf)
        closure = check.dependency_closure(root, 62, [self.root], self.root / "retained")
        self.assertEqual(3, len(closure))
        for item in closure:
            self.assertEqual(Path(item["path"]).read_bytes(), Path(item["retained_copy"]).read_bytes())
            self.assertEqual(check.receipt(Path(item["retained_copy"]))["sha256"], item["sha256"])

    def assert_same_path_replacement_preserves_snapshot(self, stage, machine, exports):
        library = self.root / "bridge.so"
        elf(library, exports=check.CORE)
        parsed_bytes = library.read_bytes()
        replaced = False
        actual = getattr(check, stage)

        def replace_once(path, **kwargs):
            nonlocal replaced
            if stage == "inspect_elf":
                result = actual(path, **kwargs)
            if path == library and not replaced:
                replaced = True
                elf(library, machine=machine, exports=exports)
            return result if stage == "inspect_elf" else actual(path, **kwargs)

        with patch.object(check, stage, side_effect=replace_once):
            item, = check.dependency_closure(library, 62, [self.root], self.root / "retained")
        self.assertTrue(replaced, "The real inspection boundary must actually replace the input")
        self.assertNotEqual(parsed_bytes, library.read_bytes())
        self.assertEqual(sorted(check.CORE), item["exports"])
        self.assertEqual(62, item["machine"])
        self.assertEqual(len(parsed_bytes), item["bytes"])
        self.assertEqual(hashlib.sha256(parsed_bytes).hexdigest(), item["sha256"])
        retained = Path(item["retained_copy"])
        self.assertEqual(parsed_bytes, retained.read_bytes())
        retained_inspection = check.inspect_elf(retained)
        self.assertEqual(item["exports"], retained_inspection["exports"])
        self.assertEqual(item["machine"], retained_inspection["machine"])

    def test_same_path_no_core_replacement_cannot_rebind_parsed_receipt(self):
        self.assert_same_path_replacement_preserves_snapshot("receipt", 62, ())

    def test_same_path_wrong_arch_replacement_cannot_rebind_parsed_receipt(self):
        self.assert_same_path_replacement_preserves_snapshot("receipt", 183, check.CORE)

    def test_same_path_no_core_replacement_after_inspection_retains_snapshot(self):
        self.assert_same_path_replacement_preserves_snapshot("inspect_elf", 62, ())

    def test_same_path_wrong_arch_replacement_after_inspection_retains_snapshot(self):
        self.assert_same_path_replacement_preserves_snapshot("inspect_elf", 183, check.CORE)

    def test_missing_or_wrong_arch_dependency_refuses_including_transitive(self):
        path, dependency = self.root / "bridge.so", self.root / "other.so"
        elf(path, needed=[dependency.name])
        with self.assertRaisesRegex(ValueError, "Unresolved"):
            check.dependency_closure(path, 62, [self.root])
        elf(dependency, machine=183)
        with self.assertRaisesRegex(ValueError, "architecture"):
            check.dependency_closure(path, 62, [self.root])

    def test_rpath_and_path_in_dependency_refuse_unmodelled_resolution(self):
        path = self.root / "bridge.so"
        elf(path, rpath="$ORIGIN/private")
        with self.assertRaisesRegex(ValueError, "RPATH"):
            check.dependency_closure(path, 62, [self.root])
        elf(path, needed=["../escape.so"])
        with self.assertRaisesRegex(ValueError, "DT_NEEDED"):
            check.dependency_closure(path, 62, [self.root])

    def source_fixture(self, selected="1" * 40, locked="8" * 40):
        canvas = self.root / check.CANVAS
        (canvas / "rnote-poc").mkdir(parents=True)
        (self.root / "eng").mkdir()
        (self.root / "eng/donor-sources.json").write_text(json.dumps({"upstreams": {"rnote": {"commit": selected}}}))
        manifest, lock = "[dependencies]\n", 'version = 4\n'
        for name in ("rnote-engine", "rnote-compose"):
            manifest += f'{name} = {{git="https://github.com/flxzt/rnote", rev="{locked}"}}\n'
            lock += f'[[package]]\nname="{name}"\nsource="git+https://github.com/flxzt/rnote?rev={locked}#{locked}"\n'
        (canvas / "rnote-poc/Cargo.toml").write_text(manifest)
        (canvas / "rnote-poc/Cargo.lock").write_text(lock)
        imports = sorted(check.CORE | {"cake_canvas_engine_from_xopp"})
        (canvas / "RnoteCanvasEngine.cs").write_text("\n".join(f'[DllImport(Library, EntryPoint = "{name}")]' for name in imports))

    def test_selected_vs_locked_conflict_is_explicit_not_relabelled(self):
        self.source_fixture()
        policy = check.source_policy(self.root)
        self.assertFalse(policy["selected_matches_locked"])
        self.assertEqual("8" * 40, policy["locked_sources"]["rnote-engine"])

    def test_source_receipts_hash_exact_parsed_snapshot_when_manifest_replaced(self):
        self.source_fixture()
        manifest = self.root / check.CANVAS / "rnote-poc/Cargo.toml"
        parsed_bytes = manifest.read_bytes()
        actual_receipt = check.receipt
        replaced = False

        def replace_manifest(path, **kwargs):
            nonlocal replaced
            if path == manifest and not replaced:
                replaced = True
                manifest.write_bytes(parsed_bytes.replace(b"8" * 40, b"9" * 40))
            return actual_receipt(path, **kwargs)

        with patch.object(check, "receipt", side_effect=replace_manifest):
            policy = check.source_policy(self.root)
        self.assertTrue(replaced)
        self.assertNotEqual(parsed_bytes, manifest.read_bytes())
        self.assertEqual("8" * 40, policy["locked_sources"]["rnote-engine"])
        item = next(item for item in policy["source_inputs"] if item["path"] == str(manifest))
        self.assertEqual(len(parsed_bytes), item["bytes"])
        self.assertEqual(hashlib.sha256(parsed_bytes).hexdigest(), item["sha256"])

    def test_lock_tampering_and_missing_registry_checksum_refuse(self):
        self.source_fixture()
        lock = self.root / check.CANVAS / "rnote-poc/Cargo.lock"
        saved = lock.read_text()
        lock.write_text(saved.replace("#" + "8" * 40, "#" + "9" * 40))
        with self.assertRaisesRegex(ValueError, "source mismatch"):
            check.source_policy(self.root)
        lock.write_text(saved + '\n[[package]]\nname="unverified"\nsource="registry+https://example.invalid"\n')
        with self.assertRaisesRegex(ValueError, "checksum"):
            check.source_policy(self.root)

    def test_missing_full_exports_separate_from_core_and_release_always_blocked(self):
        self.source_fixture()
        library, output = self.root / "bridge.so", self.root / "report.json"
        elf(library, exports=check.CORE)
        arguments = ["--repo", str(self.root), "--library", str(library), "--target", "x86_64-unknown-linux-gnu",
                     "--revision", "a" * 40, "--output", str(output)]
        with patch.object(check.subprocess, "check_output", return_value="a" * 40), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, check.main(arguments))  # export inspection, never release success
            self.assertEqual(2, check.main(arguments + ["--require-release-ready"]))
        report = json.loads(output.read_text())
        self.assertFalse(report["release_ready"])
        self.assertFalse(report["native_environment_reproducible"])
        self.assertEqual([], report["missing_core"])
        self.assertEqual(["cake_canvas_engine_from_xopp"], report["missing_full"])

    def test_missing_core_or_mismatched_revision_fails_and_keeps_report(self):
        self.source_fixture()
        library, output = self.root / "bridge.so", self.root / "report.json"
        elf(library)
        arguments = ["--repo", str(self.root), "--library", str(library), "--target", "x86_64-unknown-linux-gnu",
                     "--revision", "a" * 40, "--output", str(output)]
        with patch.object(check.subprocess, "check_output", return_value="a" * 40), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, check.main(arguments))
        self.assertEqual(sorted(check.CORE), json.loads(output.read_text())["missing_core"])
        with patch.object(check.subprocess, "check_output", return_value="b" * 40), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, check.main(arguments))
        self.assertIn("Checkout revision", json.loads(output.read_text())["inspection_failure"]["message"])

    def test_dirty_inputs_cannot_be_described_as_the_immutable_candidate(self):
        self.source_fixture()
        output = self.root / "report.json"
        arguments = ["--repo", str(self.root), "--library", str(self.root / "unbuilt.so"),
                     "--target", "x86_64-unknown-linux-gnu", "--revision", "a" * 40, "--output", str(output)]
        failure = check.subprocess.CalledProcessError(1, ["git", "diff", "--exit-code"])
        with patch.object(check.subprocess, "check_output", side_effect=["a" * 40, failure]), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, check.main(arguments))
        self.assertEqual("CalledProcessError", json.loads(output.read_text())["inspection_failure"]["type"])


if __name__ == "__main__":
    unittest.main()
