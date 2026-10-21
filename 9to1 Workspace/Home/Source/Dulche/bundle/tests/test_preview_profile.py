from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2]))

from bundle.preview_profile import PreviewProfileBuilder  # noqa: E402
from bundle.tests.helpers import build_gguf, make_elf, write_fixture  # noqa: E402


def _valid_gguf() -> bytes:
    # A structurally valid GGUF: one F32[1] tensor, aligned data section.
    return build_gguf(tensors=[("w", (1,), 0, 0, b"\x00" * 4)], alignment=32)


def _q4k_family_gguf() -> bytes:
    # A GGUF whose single tensor uses a k-quant ggml type outside the supported
    # set (not Q6_K). It must be rejected as unsupported, never relabelled.
    return build_gguf(tensors=[("w", (256,), 22, 0, b"\x00" * 210)], alignment=32)


class PreviewProfileTests(unittest.TestCase):
    def test_missing_asset_is_blocked_and_no_hash_fabricated(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="model",
                path=str(root / "missing.gguf"),
                declared_format="gguf",
                license_id="Apache-2.0",
                source="urn:local",
                redistribution="review-required",
            )
            profile = builder.build()
            self.assertFalse(profile["claimedReady"])
            asset = profile["assets"][0]
            self.assertEqual("absent", asset["state"])
            self.assertIsNone(asset["sha256"])
            self.assertEqual("BLOCKED", asset["admissionState"])

    def test_supplied_valid_gguf_admitted_with_real_identity(self) -> None:
        data = _valid_gguf()
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", data)
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="model",
                path=str(model_path),
                declared_format="gguf",
                license_id="Apache-2.0",
                source="urn:local",
                redistribution="allowed",
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])
            self.assertTrue(asset["deploymentAdmission"])
            self.assertEqual("ADMITTED", asset["admissionState"])
            self.assertEqual(len(data), asset["size"])
            self.assertEqual(asset["sha256"], __import__("hashlib").sha256(data).hexdigest())

    def test_blank_target_platform_is_not_confirmed_and_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", _valid_gguf())
            builder = PreviewProfileBuilder()  # no target OS/arch
            builder.add_asset(
                logical="model",
                path=str(model_path),
                declared_format="gguf",
                license_id="Apache-2.0",
                source="urn:local",
                redistribution="review-required",
            )
            profile = builder.build()
            self.assertFalse(profile["targetPlatform"]["confirmed"])
            self.assertTrue(any("blank/not a known OS/architecture" in b for b in profile["blocked"]))
            # Even a structurally valid asset is not deployment-admitted without a
            # confirmed target platform.
            self.assertEqual("BLOCKED", profile["assets"][0]["admissionState"])

    def test_wrong_elf_machine_is_rejected_not_renamed(self) -> None:
        # A structurally valid ELF64 header that is x86_64 (machine 62) supplied
        # into an aarch64 target must be rejected: never renamed/relabelled.
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            bin_path = write_fixture(root, "llama-server", make_elf(62))
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="runtime-engine",
                path=str(bin_path),
                declared_format="elf",
                kind="runtime.binary",
                license_id="MIT",
                source="urn:local",
                redistribution="allowed",
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])  # format is fine
            self.assertFalse(asset["deploymentAdmission"])  # machine mismatch
            self.assertTrue(any("architecture-incompatible" in b for b in profile["blocked"]))

    def test_elf_machine_matches_confirmed_target(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            bin_path = write_fixture(root, "llama-server", make_elf(183))  # aarch64
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="runtime-engine",
                path=str(bin_path),
                declared_format="elf",
                kind="runtime.binary",
                license_id="MIT",
                source="urn:local",
                redistribution="allowed",
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])
            self.assertTrue(asset["deploymentAdmission"])
            self.assertEqual("ADMITTED", asset["admissionState"])

    def test_testonly_fixture_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", _valid_gguf())
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="model",
                path=str(model_path),
                declared_format="gguf",
                license_id="Apache-2.0",
                source="urn:local",
                redistribution="review-required",
                testonly=True,
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertEqual("rejected", asset["state"])
            self.assertEqual("BLOCKED", asset["admissionState"])
            self.assertIn("TESTONLY", asset["blocker"])
            self.assertEqual(1, profile["summary"]["rejectedTestOnly"])

    def test_absent_license_source_evidence_is_blocked(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", _valid_gguf())
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            # No license/source evidence supplied.
            builder.add_asset(logical="model", path=str(model_path), declared_format="gguf")
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])
            self.assertFalse(asset["deploymentAdmission"])
            self.assertTrue(any("licence/source/redistribution" in b for b in profile["blocked"]))

    def test_unknown_quantized_ggml_type_is_rejected_not_relabelled(self) -> None:
        # Guard for the Q4_K_M reconciliation gap: a tensor whose ggml type is
        # outside the supported set (a k-quant family tag that is not Q6_K) must
        # fail closed as unsupported and MUST NOT be admitted or relabelled.
        data = _q4k_family_gguf()
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", data)
            builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
            builder.add_asset(
                logical="model",
                path=str(model_path),
                declared_format="gguf",
                license_id="Apache-2.0",
                source="urn:local",
                redistribution="review-required",
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertFalse(asset["structuralAdmission"])
            self.assertEqual("BLOCKED", asset["admissionState"])
            self.assertIn("unsupported", asset["blocker"].lower())

    def test_garbage_os_is_not_confirmed_and_blocked(self) -> None:
        # A non-real OS must never confer deployment admission even when the ELF
        # machine matches and evidence is otherwise complete.
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            bin_path = write_fixture(root, "llama-server", make_elf(183))
            builder = PreviewProfileBuilder(target_os="garbage", target_arch="aarch64")
            builder.add_asset(
                logical="runtime-engine",
                path=str(bin_path),
                declared_format="elf",
                kind="runtime.binary",
                license_id="MIT",
                source="https://github.com/CroakyJake12/9to1/actions/runs/39562111673",
                redistribution="allowed",
            )
            profile = builder.build()
            self.assertFalse(profile["targetPlatform"]["confirmed"])
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])  # format is intact
            self.assertFalse(asset["deploymentAdmission"])  # garbage OS blocks
            self.assertEqual("BLOCKED", asset["admissionState"])

    def test_prohibited_redistribution_stays_blocked_retains_structural(self) -> None:
        # prohibited/unknown/review-required must stay BLOCKED for deployment
        # without denying the independent structural identity.
        import itertools
        for redistribution in ("prohibited", "unknown", "review-required"):
            with self.subTest(redistribution=redistribution):
                with tempfile.TemporaryDirectory() as td:
                    root = pathlib.Path(td)
                    model_path = write_fixture(root, "m.gguf", _valid_gguf())
                    builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
                    builder.add_asset(
                        logical="model",
                        path=str(model_path),
                        declared_format="gguf",
                        license_id="Apache-2.0",
                        source="urn:local",
                        redistribution=redistribution,
                    )
                    profile = builder.build()
                    asset = profile["assets"][0]
                    self.assertTrue(asset["structuralAdmission"])
                    self.assertFalse(asset["deploymentAdmission"])
                    self.assertEqual("BLOCKED", asset["admissionState"])

    def test_blank_unknown_license_cannot_confer_deployment_admission(self) -> None:
        # Blank/unknown licence cannot confer deployment admission even when the
        # file is structurally valid and the target is confirmed.
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            model_path = write_fixture(root, "m.gguf", _valid_gguf())
            for license_id in (None, "", "Not-A-Real-SPDX-ID"):
                with self.subTest(license_id=license_id):
                    builder = PreviewProfileBuilder(target_os="linux", target_arch="aarch64")
                    builder.add_asset(
                        logical="model",
                        path=str(model_path),
                        declared_format="gguf",
                        license_id=license_id,
                        source="urn:local",
                        redistribution="allowed",
                    )
                    profile = builder.build()
                    asset = profile["assets"][0]
                    self.assertTrue(asset["structuralAdmission"])
                    self.assertFalse(asset["deploymentAdmission"])
                    self.assertEqual("BLOCKED", asset["admissionState"])

    def test_foreign_os_elf_rejected_while_linux_stays_admitted(self) -> None:
        # A Linux ELF must NOT be deployment-admitted for a foreign known OS
        # (Windows). Structural identity stays separate and is retained; linux/
        # aarch64 remains admitted when the redistribution policy is allowed.
        with tempfile.TemporaryDirectory() as td:
            root = pathlib.Path(td)
            bin_path = write_fixture(root, "llama-server", make_elf(183))
            builder = PreviewProfileBuilder(target_os="windows", target_arch="aarch64")
            builder.add_asset(
                logical="runtime-engine",
                path=str(bin_path),
                declared_format="elf",
                kind="runtime.binary",
                license_id="MIT",
                source="urn:local",
                redistribution="allowed",
            )
            profile = builder.build()
            asset = profile["assets"][0]
            self.assertTrue(asset["structuralAdmission"])
            self.assertFalse(asset["deploymentAdmission"])
            self.assertEqual("BLOCKED", asset["admissionState"])
            self.assertTrue(any("not compatible with target OS" in b for b in profile["blocked"]))


if __name__ == "__main__":
    unittest.main()
