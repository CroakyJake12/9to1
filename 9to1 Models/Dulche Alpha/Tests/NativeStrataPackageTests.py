"""Actual packaging predicate controls; no binary build or CUDA proof."""
import importlib.util
import json
import pathlib
import struct
import unittest

class NativeTargetControls(unittest.TestCase):
    def test_actual_target_refuses_other_architectures(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        s=importlib.util.spec_from_file_location("strata_package",p)
        owner=importlib.util.module_from_spec(s);s.loader.exec_module(owner)
        h=bytearray(64);h[:7]=b"\x7fELF\x02\x01\x01"
        h[16:18]=(2).to_bytes(2,"little");h[18:20]=(62).to_bytes(2,"little");h[20:24]=(1).to_bytes(4,"little")
        self.assertEqual(62,owner.verify_linux_x64_elf(bytes(h))["machine"])
        for offset,value in [(4,1),(5,2),(18,183),(7,9)]:
            b=bytearray(h);b[offset]=value
            with self.subTest(offset=offset),self.assertRaises(ValueError):owner.verify_linux_x64_elf(bytes(b))
        with self.assertRaises(ValueError):owner.verify_linux_x64_elf(b"not ELF")

    def test_windows_pe_check_accepts_pe32p_x64(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        s=importlib.util.spec_from_file_location("strata_package",p)
        owner=importlib.util.module_from_spec(s);s.loader.exec_module(owner)
        def make_pe(machine=0x8664, magic=0x20B, mz=True):
            pe_off=0x80
            body=bytearray(pe_off+64)
            if mz: body[0:2]=b"MZ"
            struct.pack_into("<I", body, 0x3C, pe_off)
            body[pe_off:pe_off+4]=b"PE\x00\x00"
            struct.pack_into("<H", body, pe_off+4, machine)
            struct.pack_into("<H", body, pe_off+24, magic)
            return bytes(body)
        r=owner.verify_windows_x64_pe(make_pe())
        self.assertEqual(r["machine"],0x8664); self.assertEqual(r["targetSystem"],"windows-x64")
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(machine=0x14C))  # i386
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(magic=0x10B))   # PE32
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(make_pe(mz=False))
        # a Linux worker must never pass as a Windows target and vice versa
        with self.assertRaises(ValueError): owner.verify_windows_x64_pe(b"\x7fELF"+b"\0"*100)
        with self.assertRaises(ValueError): owner.verify_linux_x64_elf(make_pe())

    def test_build_package_supports_profile_lock_target_selection(self):
        p=pathlib.Path(__file__).resolve().parents[1]/"InferenceEngines"/"NativeStrata"/"build-package.py"
        src=p.read_text()
        self.assertIn("--profile", src); self.assertIn("candidate", src); self.assertIn("legacy", src)
        self.assertIn("--lock", src); self.assertIn("--target", src)
        self.assertIn("windows-x64", src); self.assertIn("verify_windows_x64_pe", src)
        self.assertIn("lockSha256", src); self.assertIn("toolchain", src)
        self.assertIn("workerSha256", src)

if __name__ == "__main__":
    unittest.main()
