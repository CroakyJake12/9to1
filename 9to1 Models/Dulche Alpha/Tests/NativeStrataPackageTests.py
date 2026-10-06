"""Actual packaging predicate controls; no binary build or CUDA proof."""
import importlib.util
import pathlib
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
