"""Tiny real files/DEBs and fake tool observations; no Rust/SDK/library execution."""
import copy
import importlib.util
import json
from pathlib import Path
import struct
import shutil
import hashlib
import subprocess
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("rnote_stage", Path(__file__).parent / "rnote-native/rnote_native_stage.py")
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)


def elf(machine=183):
    value = bytearray(64)
    value[:6] = b"\x7fELF\x02\x01"
    struct.pack_into("<HH", value, 16, 3, machine)
    return value


class RnoteStage(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.library = self.root / native.LIBRARY
        self.library.write_bytes(elf())
        self.provider = self.root / "provider.so"
        self.provider.write_bytes(elf())
        self.symbols = " 1: 00000100 4 FUNC GLOBAL DEFAULT 12 cake_canvas_abi_version\n"
        self.licence = self.root / "LICENSE"
        self.licence.write_text("Tiny fixture licence bytes, not a product legal verdict")
        self.provenance = self.root / "PROVENANCE.md"
        self.provenance.write_text("Tiny static fixture provenance")
        self.arch = "arm64"
        self.missing_owner = False

    def execute(self, argv, cwd=None):
        if argv[0] == "readelf":
            if "--dyn-syms" in argv:
                return self.symbols
            return " 0x1 (NEEDED) Shared library: [libc.so.6]"
        if argv[:2] == ["ldconfig", "-p"]:
            return "\n".join(f" {name} (libc6,AArch64) => {self.provider}" for name in ("libc.so.6", *native.DLOPEN))
        if argv[:2] == ["dpkg-query", "-S"]:
            if self.missing_owner:
                return ""
            return "libfixture:arm64: " + argv[-1]
        if argv[:2] == ["dpkg-query", "-W"]:
            return "libfixture:arm64\t1.2-3\t" + self.arch
        raise AssertionError(argv)

    def inspect(self):
        return native.inspect_library(self.library, ["cake_canvas_abi_version"], self.execute)

    def test_defined_public_symbol_and_real_provider_identity_are_required(self):
        result = self.inspect()
        self.assertEqual(["libfixture (>= 1.2-3)"], result["dependencies"])
        self.assertEqual(native.identity(self.provider), {k: result["providers"][0][k] for k in ("bytes", "sha256")})
        self.assertEqual("NOT RUN", result["runtime_execution"])

    def test_undefined_hidden_and_local_symbols_do_not_satisfy_managed_abi(self):
        for row in ("1: 0 4 FUNC GLOBAL DEFAULT UND cake_canvas_abi_version",
                    "1: 1 4 FUNC GLOBAL HIDDEN 12 cake_canvas_abi_version",
                    "1: 1 4 FUNC LOCAL DEFAULT 12 cake_canvas_abi_version"):
            with self.subTest(row=row):
                self.symbols = row
                with self.assertRaisesRegex(ValueError, "missing defined"):
                    self.inspect()

    def test_wrong_machine_refused_before_tool_invocation(self):
        self.library.write_bytes(elf(62))
        with self.assertRaisesRegex(ValueError, "ARM64 shared object"):
            native.inspect_library(self.library, [], lambda *_: self.fail("No host tool should run"))

    def test_unowned_and_other_arch_provider_refused(self):
        self.missing_owner = True
        with self.assertRaisesRegex(ValueError, "Unowned"):
            self.inspect()
        self.missing_owner = False
        self.arch = "amd64"
        with self.assertRaisesRegex(ValueError, "not ARM64"):
            self.inspect()

    def test_provider_bytes_must_be_arm64(self):
        self.provider.write_bytes(elf(62))
        with self.assertRaisesRegex(ValueError, "provider"):
            self.inspect()

    def test_source_contract_binds_managed_native_header_and_all_crate_inputs(self):
        crate = self.root / native.CRATE
        (crate / "src").mkdir(parents=True)
        (crate / "include").mkdir()
        (crate / "Cargo.toml").write_text('[package]\nname="cakeos-canvas-rnote-poc"\nlicense="GPL-3.0-or-later"\nrust-version="1.92"\n[lib]\ncrate-type=["cdylib"]\n')
        (crate / "Cargo.lock").write_text('# parser fixture lock\n')
        (crate / "src/ffi.rs").write_text('pub const CAKE_CANVAS_ABI_VERSION: u32 = 3; pub extern "C" fn cake_canvas_abi_version() {}')
        (crate / "include/cakeos_canvas.h").write_text('#define CAKE_CANVAS_ABI_VERSION 3')
        managed = self.root / native.MANAGED
        managed.write_text('Native.AbiVersion() != 3; EntryPoint = "cake_canvas_abi_version"')
        (self.root / native.RASTERIZER).write_text(' '.join('"' + name + '"' for name in native.DLOPEN))
        contract = native.source_contract(self.root)
        self.assertEqual(3, contract["abi_version"])
        self.assertIn(str(native.CRATE / "Cargo.lock"), contract["source_inputs"])
        managed.write_text(managed.read_text() + '; EntryPoint = "cake_canvas_missing"')
        with self.assertRaisesRegex(ValueError, "absent"):
            native.source_contract(self.root)
        managed.write_text('Native.AbiVersion() != 2; EntryPoint = "cake_canvas_abi_version"')
        with self.assertRaisesRegex(ValueError, "version mismatch"):
            native.source_contract(self.root)

    def make_deb(self, omit=False, change=False, dependencies=True):
        tree = self.root / ("deb" + str(len(list(self.root.glob("deb*")))))
        (tree / "DEBIAN").mkdir(parents=True)
        (tree / "DEBIAN").chmod(0o755)
        (tree / "DEBIAN/control").write_text("Package: rnote-fixture\nVersion: 0.1\nArchitecture: arm64\nMaintainer: Fixture <fixture@example.invalid>\nDescription: Static fixture\n" + ("Depends: libfixture (>= 1.2-3)\n" if dependencies else ""))
        if not omit:
            member = tree / "opt/9to1-preview" / native.LIBRARY
            member.parent.mkdir(parents=True)
            member.write_bytes(b"wrong" if change else self.library.read_bytes())
        doc = tree / "usr/share/doc/9to1-preview"
        doc.mkdir(parents=True)
        self.receipt_path = self.root / "native.json"
        self.receipt_path.write_text(json.dumps({"fixture": "static only"}))
        (doc / "rnote-native-stage.json").write_bytes(self.receipt_path.read_bytes())
        (doc / "rnote-LICENSE-GPL-3.0-or-later.txt").write_bytes(self.licence.read_bytes())
        (doc / "rnote-PROVENANCE.md").write_bytes(self.provenance.read_bytes())
        package = tree.with_suffix(".deb")
        subprocess.run(["dpkg-deb", "--build", "--root-owner-group", str(tree), str(package)], check=True, capture_output=True)
        return package

    def test_actual_deb_contains_exact_library_receipt_and_dependencies(self):
        package = self.make_deb()
        receipt = {"inspection": self.inspect(), "licence": {"identity": native.identity(self.licence)}, "provenance": {"identity": native.identity(self.provenance)}}
        result = native.verify_package(package, receipt, self.receipt_path)
        self.assertEqual("PACKAGE_NATIVE_BYTES_AND_DEPENDENCIES_VERIFIED_NOT_EXECUTED", result["status"])
        self.assertEqual("NOT RUN", result["native_runtime"])

    def test_actual_deb_missing_changed_library_or_missing_dependency_refuses(self):
        for arguments in ({"omit": True}, {"change": True}, {"dependencies": False}):
            with self.subTest(arguments=arguments):
                package = self.make_deb(**arguments)
                with self.assertRaises(ValueError):
                    native.verify_package(package, {"inspection": self.inspect(), "licence": {"identity": native.identity(self.licence)}, "provenance": {"identity": native.identity(self.provenance)}}, self.receipt_path)


class ExactSourcePatches(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location("source_patches", Path(__file__).parent / "rnote-native/rnote_source_patches.py")
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.vendor = self.root / "vendor"
        self.engine = self.vendor / "rnote-engine-0.14.2"
        repository = Path(__file__).resolve().parents[3]
        native = self.root / self.module.CRATE
        native.mkdir(parents=True)
        shutil.copyfile(repository / self.module.CRATE / "Cargo.lock", native / "Cargo.lock")
        shutil.copytree(repository / self.module.CRATE / "patches", native / "patches")
        shutil.copytree(native / "patches/fixtures/upstream-8cac558", self.engine)
        (self.engine / "Cargo.toml").write_text('[package]\nname="rnote-engine"\nversion="0.14.2"\n')
        self.checksum = self.engine / ".cargo-checksum.json"
        self.checksum.write_text(json.dumps({"package": None, "files": {
            p.relative_to(self.engine).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in self.engine.rglob("*") if p.is_file()}}))

    def apply(self):
        return self.module.apply_vendor_patches(self.root, self.vendor)

    def test_actual_two_patch_chain_and_cargo_checksums_match_exact_postimages(self):
        proof = self.apply()
        self.assertEqual("PATCHED_LOCKED_DONOR_SOURCE_NOT_RUNTIME_ACCEPTED", proof["status"])
        self.assertEqual(2, len(proof["patches"]))
        checksum = json.loads(self.checksum.read_text())
        for step in proof["patches"]:
            for row in step["files"]:
                # Later steps may intentionally replace an earlier postimage.
                last = next(item for later in reversed(proof["patches"]) for item in later["files"] if item["path"] == row["path"])
                self.assertEqual(last["after"], self.module.identity(self.engine / row["path"]))
                self.assertEqual(last["after"]["sha256"], checksum["files"][row["path"]])
        with self.assertRaisesRegex(ValueError, "preimage"):
            self.apply()

    def test_wrong_lock_source_refuses_before_source_changes(self):
        lock = self.root / self.module.CRATE / "Cargo.lock"
        lock.write_text(lock.read_text().replace(self.module.REVISION, "0" * 40))
        before = (self.engine / "src/engine/mod.rs").read_bytes()
        with self.assertRaisesRegex(ValueError, "locked Rnote"):
            self.apply()
        self.assertEqual(before, (self.engine / "src/engine/mod.rs").read_bytes())

    def test_mutated_vendor_checksum_or_preimage_never_partially_publishes(self):
        path = self.engine / "src/store/trash_comp.rs"
        path.write_bytes(path.read_bytes() + b"\n// changed fixture\n")
        with self.assertRaisesRegex(ValueError, "checksum"):
            self.apply()
        checksum = json.loads(self.checksum.read_text())
        checksum["files"]["src/store/trash_comp.rs"] = self.module.identity(path)["sha256"]
        self.checksum.write_text(json.dumps(checksum))
        original = (self.engine / "src/engine/mod.rs").read_bytes()
        with self.assertRaisesRegex(ValueError, "preimage"):
            self.apply()
        self.assertEqual(original, (self.engine / "src/engine/mod.rs").read_bytes())

    def test_changed_patch_and_unsafe_target_refuse(self):
        folder = self.root / self.module.CRATE / "patches"
        path = folder / "0001-keyed-stroke-operations.patch"
        path.write_bytes(path.read_bytes() + b"\n")
        with self.assertRaisesRegex(ValueError, "Patch bytes"):
            self.apply()
        manifest = json.loads((folder / "source-patches.json").read_text())
        manifest["patches"][0].update(self.module.identity(path))
        manifest["patches"][0]["files"][0]["path"] = "../escape.rs"
        (folder / "source-patches.json").write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.apply()

    def test_vendor_symlink_cannot_redirect_patch_writes(self):
        path = self.engine / "src/engine/mod.rs"
        outside = self.root / "outside.rs"
        outside.write_bytes(path.read_bytes())
        path.unlink()
        path.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, "symlink"):
            self.apply()
        self.assertNotIn("settle_headless_render_tasks", outside.read_text())


if __name__ == "__main__":
    unittest.main()
