"""Synthetic binary/source controls; none execute a native library or imply ARM runtime success."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace

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


class BuildPreflightTests(unittest.TestCase):
    """Prerequisite controls use synthetic metadata, never claim a native build."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        NativeArtifactTests.source_fixture(self)
        self.manifest = self.root / check.CANVAS / "rnote-poc/Cargo.toml"
        self.lock = self.manifest.with_name("Cargo.lock")
        self.manifest.write_text('[package]\nname="bridge"\nversion="0.1.0"\nrust-version="1.92"\nedition="2024"\n' + self.manifest.read_text())
        lock = self.lock.read_text()
        for name in ("rnote-engine", "rnote-compose"):
            lock = lock.replace(f'name="{name}"\n', f'name="{name}"\nversion="0.14.2"\n')
        self.lock.write_text(lock + '\n[[package]]\nname="bridge"\nversion="0.1.0"\n')
        source = "git+https://github.com/flxzt/rnote?rev=" + "8" * 40 + "#" + "8" * 40
        self.metadata = {"version": 1, "packages": [], "resolve": {"nodes": []}}
        for name, version, origin in (("bridge", "0.1.0", None), ("rnote-engine", "0.14.2", source), ("rnote-compose", "0.14.2", source)):
            manifest = self.manifest if name == "bridge" else self.root / "cache" / name / "Cargo.toml"
            if name != "bridge":
                manifest.parent.mkdir(parents=True)
                manifest.write_text(f'[package]\nname="{name}"\nversion="{version}"\nrust-version="1.89"\n')
            self.metadata["packages"].append({"id": name, "name": name, "version": version, "source": origin,
                "manifest_path": str(manifest), "rust_version": "1.92" if name == "bridge" else "1.89"})
            self.metadata["resolve"]["nodes"].append({"id": name, "features": []})
        self.metadata_path = self.root / "cargo-metadata.json"
        self.write_metadata()
        self.blobs = {str(path.relative_to(self.root)): path.read_bytes() for path in (
            self.manifest, self.lock, self.root / check.CANVAS / "RnoteCanvasEngine.cs", self.root / "eng/donor-sources.json")}
        self.args = SimpleNamespace(repo=self.root, revision="a" * 40, target="x86_64-unknown-linux-gnu",
                                    rust_toolchain="1.92.0", rustc=None, cargo=None, cargo_metadata=self.metadata_path)

    def write_metadata(self):
        self.metadata_path.write_text(json.dumps(self.metadata))

    def git_observation(self, command, **kwargs):
        if command[3] == "show":
            return self.blobs[command[4].split(":", 1)[1]]
        return "a" * 40 if command[3] == "rev-parse" else ""

    @staticmethod
    def tool_observation(path, tool):
        return {"exit_code": 0, "stdout": "release: 1.92.0\nhost: x86_64-unknown-linux-gnu\n" if tool == "rustc" else "cargo 1.92.0 (fixture)\n"}

    @staticmethod
    def native_observation(command):
        versions = {"cairo": "1.18.2", "pango": "1.56.0"}
        return {"command": command, "exit_code": 0, "stdout": versions.get(command[-1], "2.84.0") + "\n", "stderr": ""}

    def run_preflight(self, native=None, tools=None):
        with patch.object(check.subprocess, "check_output", side_effect=self.git_observation), \
                patch.object(check, "installed_tool", side_effect=tools or self.tool_observation), \
                patch.object(check, "observe", side_effect=native or self.native_observation):
            return check.build_preflight(self.args)

    def cairo_feature_fixture(self, features):
        # cairo-sys-rs 0.21.5's real system-deps declarations from failed CI
        # run39984463251. Only the surrounding package identity is synthetic.
        manifest = Path(self.metadata["packages"][1]["manifest_path"])
        manifest.write_text(manifest.read_text() + '''
[package.metadata.system-deps.cairo]
name="cairo"
version="1.14"
[package.metadata.system-deps.cairo.v1_16]
version="1.16"
[package.metadata.system-deps.cairo.v1_18]
version="1.18"
[package.metadata.system-deps.cairo-gobject]
name="cairo-gobject"
version="1.14"
feature="use_glib"
[package.metadata.system-deps.cairo-gobject.v1_16]
version="1.16"
[package.metadata.system-deps.cairo-gobject.v1_18]
version="1.18"
''')
        self.metadata["resolve"]["nodes"][1]["features"] = features
        self.write_metadata()
        return manifest

    def test_actual_cairo_scalar_selector_observes_all_manifests_and_highest_floor(self):
        self.cairo_feature_fixture(["use_glib", "v1_16", "v1_18"])
        report, code = self.run_preflight()
        self.assertEqual(0, code, report["blockers"])
        observed = report["declared_requirements"]
        self.assertEqual((1, 18, 0), observed["native_requirements"]["cairo-gobject"])
        self.assertEqual((1, 18, 0), observed["native_requirements"]["cairo"])
        self.assertEqual(3, observed["locked_packages"])
        self.assertEqual(3, len(observed["package_manifests"]))
        self.assertFalse(report["release_ready"])
        self.assertTrue(report["release_blockers"])

    def test_selected_scalar_feature_cannot_hide_insufficient_native_version(self):
        self.cairo_feature_fixture(["use_glib", "v1_18"])

        def old_gobject(command):
            result = self.native_observation(command)
            if command[-1] == "cairo-gobject":
                result["stdout"] = "1.16.0\n"
            return result

        report, code = self.run_preflight(native=old_gobject)
        self.assertEqual(1, code)
        self.assertEqual((1, 18, 0), report["declared_requirements"]["native_requirements"]["cairo-gobject"])
        self.assertTrue(any("Native module cairo-gobject" in blocker for blocker in report["blockers"]))

    def test_unselected_scalar_feature_omits_only_its_module_requirement(self):
        self.cairo_feature_fixture(["v1_18"])
        report, code = self.run_preflight()
        self.assertEqual(0, code, report["blockers"])
        observed = report["declared_requirements"]
        self.assertNotIn("cairo-gobject", observed["native_requirements"])
        self.assertEqual((1, 18, 0), observed["native_requirements"]["cairo"])
        self.assertEqual(3, len(observed["package_manifests"]))

    def test_absent_scalar_selector_keeps_unconditional_native_requirement(self):
        manifest = self.cairo_feature_fixture(["v1_18"])
        manifest.write_text(manifest.read_text().replace('feature="use_glib"\n', ''))
        report, code = self.run_preflight()
        self.assertEqual(0, code, report["blockers"])
        self.assertEqual((1, 18, 0), report["declared_requirements"]["native_requirements"]["cairo-gobject"])

    def test_scalar_selector_uses_cargo_feature_environment_normalization(self):
        # system-deps 7.0.8 has_feature uppercases and maps '-' to '_'.
        self.cairo_feature_fixture(["use-glib", "v1_18"])
        report, code = self.run_preflight()
        self.assertEqual(0, code, report["blockers"])
        self.assertEqual((1, 18, 0), report["declared_requirements"]["native_requirements"]["cairo-gobject"])

    def test_malformed_scalar_selectors_refuse_even_without_selected_features(self):
        manifest = self.cairo_feature_fixture([])
        original = manifest.read_text()
        for value in ('true', '42', '[]', '{version="1.18"}', '""', '"use glib"'):
            with self.subTest(selector=value):
                manifest.write_text(original.replace('feature="use_glib"', 'feature=' + value))
                report, code = self.run_preflight()
                self.assertEqual(1, code)
                self.assertTrue(any("Unsupported system-deps feature selector" in blocker for blocker in report["blockers"]))

    def test_unselected_scalar_selector_still_rejects_unknown_spec_shapes(self):
        manifest = self.cairo_feature_fixture([])
        original = manifest.read_text()
        for change in ('feature="use_glib"\ncfg="unmodelled"',
                       'feature="use_glib"\nv1_20={version="1.20",name="other"}'):
            with self.subTest(change=change):
                manifest.write_text(original.replace('feature="use_glib"', change))
                report, code = self.run_preflight()
                self.assertEqual(1, code)
                self.assertTrue(any("Unsupported feature-conditioned" in blocker for blocker in report["blockers"]))

    def test_unselected_scalar_selector_still_checks_other_package_msrv(self):
        self.cairo_feature_fixture([])
        package = self.metadata["packages"][2]
        manifest = Path(package["manifest_path"])
        manifest.write_text(manifest.read_text().replace('"1.89"', '"1.93"'))
        package["rust_version"] = "1.93"
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertIn("Locked dependency declares a Rust requirement above the selected toolchain", report["blockers"])
        self.assertEqual(3, len(report["declared_requirements"]["package_manifests"]))

    def test_selected_scalar_feature_retains_base_and_intermediate_version_floors(self):
        self.cairo_feature_fixture(["use_glib"])
        for selected, floor in ((["use_glib"], (1, 14, 0)),
                                (["use_glib", "v1_16"], (1, 16, 0))):
            with self.subTest(features=selected):
                self.metadata["resolve"]["nodes"][1]["features"] = selected
                self.write_metadata()
                report, code = self.run_preflight()
                self.assertEqual(0, code, report["blockers"])
                self.assertEqual(floor, report["declared_requirements"]["native_requirements"]["cairo-gobject"])

    def librsvg_system_fixture(self):
        # Exact system-deps table from locked librsvg 2.61.4 metadata in
        # run40013291193/artifact12527018441; surrounding package is synthetic.
        manifest = Path(self.metadata["packages"][1]["manifest_path"])
        manifest.write_text(manifest.read_text() + '''
[package.metadata.system-deps]
cairo = "1.18"
cairo-gobject = "1.18"
cairo-png = "1.18"
freetype2 = "20.0.14"
harfbuzz = "2.0"
pangocairo = "1.50"
cairo-pdf = { optional = true, version = "1.18" }
cairo-ps = { optional = true, version = "1.18" }
cairo-svg = { optional = true, version = "1.18" }
gio = { name = "gio-2.0", version = "2.24" }
glib = { name = "glib-2.0", version = "2.50" }
libxml2 = { fallback-names = ["libxml2"], name = "libxml-2.0", version = "2.9" }
[package.metadata.system-deps.'cfg(all(not(target_os = "macos"), not(target_os = "windows")))']
fontconfig = { version = "1.7" }
pangoft2 = { version = "1.50" }
[package.metadata.system-deps.'cfg(any(target_os = "macos", target_os = "windows"))']
fontconfig = { optional = true, version = "1.7" }
pangoft2 = { optional = true, version = "1.50" }
''')
        return manifest

    @staticmethod
    def librsvg_native_observation(command):
        result = BuildPreflightTests.native_observation(command)
        if command[-1] == "freetype2":
            result["stdout"] = "24.3.18\n"
        return result

    def test_actual_librsvg_static_forms_observe_all_floors_on_both_linux_targets(self):
        self.librsvg_system_fixture()
        expected = {"cairo": (1, 18, 0), "cairo-gobject": (1, 18, 0),
                    "cairo-png": (1, 18, 0), "freetype2": (20, 0, 14),
                    "harfbuzz": (2, 0, 0), "pangocairo": (1, 50, 0),
                    "cairo-pdf": (1, 18, 0), "cairo-ps": (1, 18, 0),
                    "cairo-svg": (1, 18, 0), "gio-2.0": (2, 24, 0),
                    "glib-2.0": (2, 50, 0), "libxml-2.0": (2, 9, 0),
                    "fontconfig": (1, 7, 0), "pangoft2": (1, 50, 0)}
        for target in ("x86_64-unknown-linux-gnu", "aarch64-unknown-linux-gnu"):
            with self.subTest(target=target):
                self.args.target = target

                def tools(path, tool):
                    result = self.tool_observation(path, tool)
                    if tool == "rustc":
                        result["stdout"] = f"release: 1.92.0\nhost: {target}\n"
                    return result

                report, code = self.run_preflight(native=self.librsvg_native_observation, tools=tools)
                self.assertEqual(0, code, report["blockers"])
                requirements = report["declared_requirements"]
                self.assertEqual(expected, requirements["native_requirements"])
                self.assertEqual({"libxml-2.0": ["libxml2"]}, requirements["declared_fallback_names"])
                self.assertEqual(3, len(requirements["package_manifests"]))
                self.assertFalse(report["release_ready"])
                self.assertEqual("NOT RUN", report["native_build_and_tests"])

    def test_actual_librsvg_shorthand_floor_and_missing_module_still_block(self):
        self.librsvg_system_fixture()
        for version, exit_code in (("1.17.0\n", 0), ("", 1)):
            with self.subTest(version=version, exit_code=exit_code):
                def native(command):
                    result = self.librsvg_native_observation(command)
                    if command[-1] == "cairo-gobject":
                        result.update(stdout=version, exit_code=exit_code)
                    return result

                report, code = self.run_preflight(native=native)
                self.assertEqual(1, code)
                self.assertEqual((1, 18, 0), report["declared_requirements"]["native_requirements"]["cairo-gobject"])
                self.assertTrue(any("Native module cairo-gobject" in item for item in report["blockers"]))

    def test_cfg_groups_select_linux_without_importing_foreign_target_floor(self):
        manifest = self.librsvg_system_fixture()
        manifest.write_text(manifest.read_text().replace(
            'fontconfig = { optional = true, version = "1.7" }',
            'foreign-platform-library = { optional = true, version = "99.0" }'))
        report, code = self.run_preflight(native=self.librsvg_native_observation)
        self.assertEqual(0, code, report["blockers"])
        observed = report["declared_requirements"]["native_requirements"]
        self.assertEqual((1, 7, 0), observed["fontconfig"])
        self.assertNotIn("foreign-platform-library", observed)

    def test_inactive_cfg_group_still_rejects_invalid_dependency_declaration(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        for fields, reason in (('optional = "true", version = "1.7"', "optional"),
                               ('version = "1.7", v1_20 = { version = true }', "numeric version"),
                               ('version = "1.7", v1_20 = { version = "^1.20" }', "numeric version")):
            with self.subTest(fields=fields):
                manifest.write_text(original.replace(
                    'fontconfig = { optional = true, version = "1.7" }',
                    'fontconfig = { ' + fields + ' }'))
                report, code = self.run_preflight(native=self.librsvg_native_observation)
                self.assertEqual(1, code)
                self.assertTrue(any(reason in item for item in report["blockers"]))

    def test_default_module_names_never_replace_explicit_invalid_name(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        for value in ('false', '42', '[]', '""', '"bad module"'):
            with self.subTest(name=value):
                manifest.write_text(original.replace('cairo = "1.18"',
                    'cairo = { version = "1.18", name = ' + value + ' }'))
                report, code = self.run_preflight(native=self.librsvg_native_observation)
                self.assertEqual(1, code)
                self.assertTrue(any("module name" in item for item in report["blockers"]))

    def test_malformed_shorthand_and_missing_version_remain_unverified(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        for value in ('false', '42', '[]', '{}', '""', '"^1.18"', '{ name = "cairo" }'):
            with self.subTest(spec=value):
                manifest.write_text(original.replace('cairo = "1.18"', 'cairo = ' + value))
                report, code = self.run_preflight(native=self.librsvg_native_observation)
                self.assertEqual(1, code)
                self.assertTrue(any("UNVERIFIED" in item for item in report["blockers"]))

    def test_fallback_names_validate_without_waiving_primary_floor(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        for value in ('"libxml2"', '[42]', '[""]', '["bad module"]'):
            with self.subTest(fallback=value):
                manifest.write_text(original.replace('fallback-names = ["libxml2"]', 'fallback-names = ' + value))
                report, code = self.run_preflight(native=self.librsvg_native_observation)
                self.assertEqual(1, code)
                self.assertTrue(any("fallback-names" in item for item in report["blockers"]))
        manifest.write_text(original)

        def absent_primary(command):
            result = self.librsvg_native_observation(command)
            if command[-1] == "libxml-2.0":
                result.update(stdout="", exit_code=1)
            return result

        report, code = self.run_preflight(native=absent_primary)
        self.assertEqual(1, code)
        self.assertEqual((2, 9, 0), report["declared_requirements"]["native_requirements"]["libxml-2.0"])
        self.assertTrue(any("Native module libxml-2.0" in item for item in report["blockers"]))

    def test_unknown_or_malformed_cfg_predicates_and_nested_groups_refuse(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        marker = 'cfg(all(not(target_os = "macos"), not(target_os = "windows")))'
        for predicate in ('cfg(target_arch = "x86_64")', 'cfg(not())',
                          'cfg(target_os = "plan9")',
                          'cfg(not(target_os = "macos", target_os = "windows"))',
                          'cfg(target_os = "linux") trailing', 'cfg(all(target_os = "linux")'):
            with self.subTest(predicate=predicate):
                manifest.write_text(original.replace(marker, predicate))
                report, code = self.run_preflight(native=self.librsvg_native_observation)
                self.assertEqual(1, code)
                self.assertTrue(any("UNVERIFIED" in item for item in report["blockers"]))
        manifest.write_text(original + '''
[package.metadata.system-deps.'cfg(target_os = "linux")'.'cfg(target_os = "linux")']
bad = "1.0"
''')
        self.assertEqual(1, self.run_preflight(native=self.librsvg_native_observation)[1])
        self.args.target = "x86_64-pc-windows-msvc"
        report, code = self.run_preflight(native=self.librsvg_native_observation)
        self.assertEqual(1, code)
        self.assertTrue(any("Unsupported native preflight target" in item for item in report["blockers"]))

    def test_linux_cfg_declared_floor_and_package_msrv_cannot_be_hidden(self):
        manifest = self.librsvg_system_fixture()
        manifest.write_text(manifest.read_text().replace('fontconfig = { version = "1.7" }',
                                                       'fontconfig = { version = "99.0" }'))
        report, code = self.run_preflight(native=self.librsvg_native_observation)
        self.assertEqual(1, code)
        self.assertTrue(any("Native module fontconfig" in item for item in report["blockers"]))
        package = self.metadata["packages"][2]
        other = Path(package["manifest_path"])
        other.write_text(other.read_text().replace('"1.89"', '"1.93"'))
        package["rust_version"] = "1.93"
        self.write_metadata()
        report, code = self.run_preflight(native=self.librsvg_native_observation)
        self.assertEqual(1, code)
        self.assertIn("Locked dependency declares a Rust requirement above the selected toolchain", report["blockers"])

    def test_unknown_optional_fields_and_conditions_still_refuse(self):
        manifest = self.librsvg_system_fixture()
        original = manifest.read_text()
        for fields in ('version="1.18", optional="yes"',
                       'version="1.18", unknown=true',
                       'version="1.18", cfg="unmodelled"',
                       'version="1.18", v1_20={name="other",version="1.20"}'):
            with self.subTest(fields=fields):
                manifest.write_text(original.replace('cairo = "1.18"', 'cairo = {' + fields + '}'))
                self.assertEqual(1, self.run_preflight(native=self.librsvg_native_observation)[1])

    def test_complete_manifest_observations_allow_engine_check_but_do_not_qualify_release(self):
        report, code = self.run_preflight()
        self.assertEqual(0, code)
        self.assertEqual("DECLARED_PREREQUISITES_OBSERVED", report["preflight_status"])
        self.assertEqual(3, report["declared_requirements"]["locked_packages"])
        self.assertEqual(3, len(report["declared_requirements"]["package_manifests"]))
        self.assertFalse(report["release_ready"])
        self.assertFalse(report["native_environment_reproducible"])
        self.assertTrue(any("Selected donor" in blocker for blocker in report["release_blockers"]))
        self.assertTrue(report["exact_transitive_msrv"].startswith("UNVERIFIED"))
        self.assertEqual("NOT RUN", report["native_build_and_tests"])

    def test_lockfile_without_all_cached_manifests_cannot_supply_transitive_msrv(self):
        self.args.cargo_metadata = None
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("lockfile alone" in blocker for blocker in report["blockers"]))
        self.args.cargo_metadata = self.metadata_path
        self.metadata["packages"].pop()
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("every locked package" in blocker for blocker in report["blockers"]))

    def test_declared_dependency_msrv_above_installed_toolchain_blocks(self):
        package = self.metadata["packages"][1]
        manifest = Path(package["manifest_path"])
        manifest.write_text(manifest.read_text().replace('"1.89"', '"1.93"'))
        package["rust_version"] = "1.93"
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertIn("Locked dependency declares a Rust requirement above the selected toolchain", report["blockers"])

    def test_metadata_cannot_understate_a_real_package_rust_requirement(self):
        self.metadata["packages"][1]["rust_version"] = "1.50"
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("Rust requirement mismatch" in blocker for blocker in report["blockers"]))

    def test_real_workspace_inheritance_is_read_and_retained(self):
        package = self.metadata["packages"][1]
        manifest = Path(package["manifest_path"])
        manifest.write_text('[package]\nname="rnote-engine"\nversion.workspace=true\nrust-version.workspace=true\n')
        workspace = manifest.parent.parent / "Cargo.toml"
        workspace.write_text('[workspace.package]\nversion="0.14.2"\nrust-version="1.89"\n')
        report, code = self.run_preflight()
        self.assertEqual(0, code)
        record = report["declared_requirements"]["package_manifests"][1]
        self.assertEqual(check.receipt(workspace), record["workspace_manifest"])
        workspace.unlink()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("actual workspace manifest" in blocker for blocker in report["blockers"]))

    def test_active_native_feature_floor_and_missing_module_fail_closed(self):
        package = self.metadata["packages"][1]
        manifest = Path(package["manifest_path"])
        manifest.write_text(manifest.read_text() + '\n[package.metadata.system-deps.pango]\nname="pango"\nversion="1.50"\n[package.metadata.system-deps.pango.v1_60]\nversion="1.60"\n')
        self.metadata["resolve"]["nodes"][1]["features"] = ["v1_60"]
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("Native module pango" in blocker for blocker in report["blockers"]))
        self.metadata["resolve"]["nodes"][1]["features"] = []
        self.write_metadata()
        self.assertEqual(0, self.run_preflight()[1])

        def missing_xml(command):
            result = self.native_observation(command)
            if command[-1] == "libxml-2.0":
                result.update(exit_code=1, stdout="")
            return result

        report, code = self.run_preflight(native=missing_xml)
        self.assertEqual(1, code)
        self.assertTrue(any("Native module libxml-2.0" in blocker for blocker in report["blockers"]))

    def test_unknown_native_conditions_and_malformed_feature_nodes_refuse(self):
        manifest = Path(self.metadata["packages"][1]["manifest_path"])
        manifest.write_text(manifest.read_text() + '\n[package.metadata.system-deps.pango]\nname="pango"\nversion="1.50"\ncfg="unmodelled"\n')
        self.assertEqual(1, self.run_preflight()[1])
        self.metadata["resolve"] = None
        self.write_metadata()
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("resolved feature nodes" in blocker for blocker in report["blockers"]))

    def test_nonobject_metadata_or_omitted_and_duplicate_resolved_nodes_refuse(self):
        for malformed in ([], {**self.metadata, "resolve": {"nodes": []}},
                          {**self.metadata, "resolve": {"nodes": self.metadata["resolve"]["nodes"] * 2}}):
            with self.subTest(metadata=malformed):
                self.metadata_path.write_text(json.dumps(malformed))
                report, code = self.run_preflight()
                self.assertEqual(1, code)
                self.assertTrue(any("UNVERIFIED" in blocker for blocker in report["blockers"]))

    def test_wrong_installed_toolchain_or_native_host_refuses(self):
        def wrong(path, tool):
            result = self.tool_observation(path, tool)
            if tool == "rustc":
                result["stdout"] = "release: 1.93.0\nhost: aarch64-unknown-linux-gnu\n"
            return result

        report, code = self.run_preflight(tools=wrong)
        self.assertEqual(1, code)
        self.assertIn("Installed Rust host differs from the native target", report["blockers"])
        self.assertIn("Observed rustc does not match the exact selected toolchain", report["blockers"])

    def test_source_bytes_must_match_revision_and_cannot_change_between_reads(self):
        self.blobs[str(self.lock.relative_to(self.root))] += b"\n# different committed source\n"
        report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("immutable revision" in blocker for blocker in report["blockers"]))
        self.blobs[str(self.lock.relative_to(self.root))] = self.lock.read_bytes()
        original = check.source_policy

        def replace_after_snapshot(repo):
            result = original(repo)
            self.lock.write_bytes(self.lock.read_bytes() + b"\n# changed during inspection\n")
            return result

        with patch.object(check, "source_policy", side_effect=replace_after_snapshot):
            report, code = self.run_preflight()
        self.assertEqual(1, code)
        self.assertTrue(any("changed during inspection" in blocker for blocker in report["blockers"]))

    def test_rustup_proxy_and_missing_explicit_binary_refuse_without_execution(self):
        rustup = self.root / "rustup"
        rustup.write_bytes(b"unexecuted fixture")
        proxy = self.root / "rustc"
        os.link(rustup, proxy)
        with patch.object(check.shutil, "which", return_value=str(rustup)), patch.object(check, "observe") as observe:
            with self.assertRaisesRegex(ValueError, "proxy"):
                check.installed_tool(proxy, "rustc")
            with self.assertRaisesRegex(ValueError, "Explicit installed"):
                check.installed_tool(None, "cargo")
            observe.assert_not_called()

    def test_version_query_timeout_is_retained_as_unavailable(self):
        failure = check.subprocess.TimeoutExpired(["cc", "--version"], 10)
        with patch.object(check.subprocess, "run", side_effect=failure):
            result = check.observe(["cc", "--version"])
        self.assertIsNone(result["exit_code"])
        self.assertEqual("TimeoutExpired", result["error"]["type"])

    def test_sibling_rustup_hardlinks_outside_path_refuse_before_safe_marker_execution(self):
        marker = self.root / "proxy-was-executed.marker"
        rustup = self.root / "rustup"
        rustup.write_text(f"#!/bin/sh\nprintf executed > '{marker}'\n")
        rustup.chmod(0o700)
        for tool in ("rustc", "cargo"):
            proxy = self.root / tool
            os.link(rustup, proxy)
            self.assertTrue(os.path.samefile(proxy, rustup))
            with patch.dict(os.environ, {"PATH": ""}), patch.object(check, "observe", wraps=check.observe) as observe:
                self.assertIsNone(check.shutil.which("rustup"))
                with self.assertRaisesRegex(ValueError, "Refusing rustup proxy"):
                    check.installed_tool(proxy, tool)
                observe.assert_not_called()
            self.assertFalse(marker.exists())

    def test_supplied_symlink_directory_proxy_identity_is_checked_before_execution(self):
        supplied = self.root / "proxy-bin"
        resolved = self.root / "elsewhere"
        supplied.mkdir()
        resolved.mkdir()
        rustup = supplied / "rustup"
        rustup.write_bytes(b"unexecuted proxy fixture")
        alias = resolved / "compiler-alias"
        os.link(rustup, alias)
        proxy = supplied / "rustc"
        proxy.symlink_to(alias)
        with patch.dict(os.environ, {"PATH": ""}), patch.object(check, "observe") as observe:
            with self.assertRaisesRegex(ValueError, "Refusing rustup proxy"):
                check.installed_tool(proxy, "rustc")
            observe.assert_not_called()

    def test_explicit_distinct_binary_identity_can_be_observed_with_empty_path(self):
        binary = self.root / "compiler"
        binary.write_bytes(b"synthetic direct-binary identity; never executed")
        (self.root / "rustup").write_bytes(b"a different inode")
        observation = {"exit_code": 0, "stdout": "release: 1.92.0\n"}
        with patch.dict(os.environ, {"PATH": ""}), patch.object(check, "observe", return_value=observation) as observe:
            result = check.installed_tool(binary, "rustc")
        observe.assert_called_once_with([str(binary), "-Vv"])
        self.assertEqual(check.receipt(binary), result["binary"])

    def test_preflight_cli_retains_blocked_report_without_library_or_toolchain_acquisition(self):
        output = self.root / "preflight.json"
        arguments = ["--build-preflight", "--repo", str(self.root), "--target", self.args.target,
                     "--revision", self.args.revision, "--rust-toolchain", "1.92.0", "--output", str(output)]
        with patch.object(check.subprocess, "check_output", side_effect=self.git_observation), \
                patch.object(check, "observe", side_effect=self.native_observation), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, check.main(arguments))
        report = json.loads(output.read_text())
        self.assertEqual("build-preflight", report["mode"])
        self.assertEqual("BLOCKED", report["preflight_status"])
        self.assertEqual(self.args.revision, report["revision"])
        self.assertTrue(any("Explicit installed" in blocker for blocker in report["blockers"]))
        self.assertFalse(report["release_ready"])


if __name__ == "__main__":
    unittest.main()
