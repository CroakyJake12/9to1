from __future__ import annotations

import json
import pathlib
import sys
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = RUNTIME.parents[4]
sys.path.insert(0, str(RUNTIME))


class PackagingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.manifest_path = REPOSITORY / "9to1 OS/packaging/llamacpp/package-manifest.json"
        cls.manifest = json.loads(cls.manifest_path.read_text(encoding="utf-8"))

    def test_package_is_model_free_and_does_not_auto_enable_service(self) -> None:
        self.assertFalse(self.manifest["containsModels"])
        self.assertFalse(self.manifest["autoEnableUserService"])
        for path in self.manifest["installedPaths"]:
            self.assertNotIn("/models/", path)
            self.assertFalse(path.endswith(".gguf"))

    def test_package_carries_exact_upstream_provenance_and_license_path(self) -> None:
        upstream = self.manifest["upstream"]
        self.assertEqual("v0.4.0", upstream["tag"])
        self.assertEqual("5266f24da75dc449bd56cbed7addb9c8e4a6a73e", upstream["commit"])
        self.assertEqual("MIT", upstream["license"])
        license_file = RUNTIME / "licenses/llama.cpp-LICENSE"
        text = license_file.read_text(encoding="utf-8")
        self.assertIn("Copyright (c) 2023-2026 The ggml authors", text)
        self.assertIn("permission notice shall be included", text)

    def test_runtime_dependencies_cover_observed_cpu_binary_libraries(self) -> None:
        dependencies = set(self.manifest["runtimeDependencies"])
        self.assertTrue({"python3", "libc6", "libstdc++6", "libgcc-s1", "libgomp1"}.issubset(dependencies))

    def test_runtime_package_rejects_model_weight_arguments(self) -> None:
        builder = (REPOSITORY / "9to1 OS/packaging/llamacpp/build-deb.sh").read_text(encoding="utf-8")
        self.assertIn("gguf", builder)
        # The runtime package accepts a real native llama-server binary input only; it must
        # never accept/embed a model weight path or GGUF as a substitute for weights.
        self.assertIn('exit 68', builder)

    def test_home_model_manifest_example_is_a_zero_placeholder_not_an_asset(self) -> None:
        manifest = json.loads((RUNTIME / "model-manifest.example.json").read_text(encoding="utf-8"))
        self.assertEqual("0000000000000000000000000000000000000000000000000000000000000000", manifest["sha256"])
        self.assertEqual(0, manifest["size"])

    def test_ci_model_lock_is_proof_only_and_bans_packaging_that_model(self) -> None:
        lock_data = json.loads((RUNTIME / "test-model.lock.json").read_text(encoding="utf-8"))
        self.assertEqual("ci-real-inference-proof-only", lock_data["purpose"])
        policy = lock_data["distributionPolicy"]
        self.assertFalse(policy["commitModel"])
        self.assertFalse(policy["packageModel"])
        self.assertFalse(policy["uploadModelArtifact"])

    def test_builder_has_no_package_install_or_service_enable_step(self) -> None:
        builder = (REPOSITORY / "9to1 OS/packaging/llamacpp/build-deb.sh").read_text(encoding="utf-8")
        self.assertNotIn("dpkg -i", builder)
        self.assertNotIn("apt install", builder)
        self.assertNotIn("systemctl enable", builder)
        self.assertNotIn("systemctl start", builder)
        self.assertIn("libgcc-s1", builder)

    def test_builder_normalizes_debian_archive_root_mode(self) -> None:
        builder = (REPOSITORY / "9to1 OS/packaging/llamacpp/build-deb.sh").read_text(encoding="utf-8")
        self.assertIn('chmod 0755 "$STAGE"', builder)
        verifier = (REPOSITORY / "9to1 OS/packaging/llamacpp/verify-installable-deb.sh").read_text(encoding="utf-8")
        self.assertIn('drwxr-xr-x', verifier)
        self.assertIn('Package root directory must be mode 0755', verifier)


if __name__ == "__main__":
    unittest.main()
