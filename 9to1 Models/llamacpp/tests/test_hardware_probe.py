from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import hardware_probe  # noqa: E402


class HardwareProbeTests(unittest.TestCase):
    def test_meminfo_units_are_bytes(self) -> None:
        parsed = hardware_probe.parse_meminfo("MemTotal: 1024 kB\nMemAvailable: 512 kB\n")
        self.assertEqual(1024 * 1024, parsed["MemTotal"])
        self.assertEqual(512 * 1024, parsed["MemAvailable"])

    def test_cgroup_max_is_unknown_not_infinity(self) -> None:
        self.assertIsNone(hardware_probe.parse_cgroup_limit("max"))
        self.assertEqual(4096, hardware_probe.parse_cgroup_limit("4096"))

    def test_cpu_flags_are_filtered_to_relevant_features(self) -> None:
        flags = hardware_probe.cpu_flags("flags : fpu sse4_2 avx avx2 randomflag\n")
        self.assertEqual(["avx", "avx2", "sse4_2"], flags)

    def test_drm_vendor_mapping_does_not_claim_runtime_support(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            drm = pathlib.Path(tmp)
            device = drm / "card0/device"
            device.mkdir(parents=True)
            (device / "vendor").write_text("0x10de\n", encoding="utf-8")
            (device / "device").write_text("0x1234\n", encoding="utf-8")
            gpus = hardware_probe.probe_gpus(drm)
            self.assertEqual("nvidia", gpus[0]["vendorName"])
            self.assertIsNone(gpus[0]["vramBytes"])


if __name__ == "__main__":
    unittest.main()
