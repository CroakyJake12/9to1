"""Tiny real files/DEBs and fake tool observations; no Rust/SDK/library execution."""
import copy
import importlib.util
import json
from pathlib import Path
import struct
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
        package = tree.with_suffix(".deb")
        subprocess.run(["dpkg-deb", "--build", "--root-owner-group", str(tree), str(package)], check=True, capture_output=True)
        return package

    def test_actual_deb_contains_exact_library_receipt_and_dependencies(self):
        package = self.make_deb()
        receipt = {"inspection": self.inspect()}
        result = native.verify_package(package, receipt, self.receipt_path)
        self.assertEqual("PACKAGE_NATIVE_BYTES_AND_DEPENDENCIES_VERIFIED_NOT_EXECUTED", result["status"])
        self.assertEqual("NOT RUN", result["native_runtime"])

    def test_actual_deb_missing_changed_library_or_missing_dependency_refuses(self):
        for arguments in ({"omit": True}, {"change": True}, {"dependencies": False}):
            with self.subTest(arguments=arguments):
                package = self.make_deb(**arguments)
                with self.assertRaises(ValueError):
                    native.verify_package(package, {"inspection": self.inspect()}, self.receipt_path)


if __name__ == "__main__":
    unittest.main()
