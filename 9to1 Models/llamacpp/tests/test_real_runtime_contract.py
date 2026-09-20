from __future__ import annotations

import json
import pathlib
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
REPO = RUNTIME.parents[1]
LOCK = json.loads((RUNTIME / "test-model.lock.json").read_text(encoding="utf-8"))
WORKFLOW = (REPO / ".github/workflows/llamacpp-slice0.yml").read_text(encoding="utf-8")
PROOF = (RUNTIME / "tests/real_runtime_proof.py").read_text(encoding="utf-8")


class RealRuntimeProofContractTests(unittest.TestCase):
    def test_ci_model_is_exactly_pinned_and_not_distributable_by_build(self) -> None:
        self.assertEqual("ci-real-inference-proof-only", LOCK["purpose"])
        self.assertEqual("unsloth/SmolLM2-135M-Instruct-GGUF", LOCK["repository"])
        self.assertEqual("8637628433dc3df4a2d363adc89e4c3917f299dc", LOCK["revision"])
        self.assertEqual("SmolLM2-135M-Instruct-Q4_K_M.gguf", LOCK["filename"])
        self.assertEqual(105454144, LOCK["size"])
        self.assertEqual("ed5fa30c487b282ec156c29062f1222e5c20875a944ac98289dbd242e947f747", LOCK["sha256"])
        self.assertEqual("Apache-2.0", LOCK["license"])
        self.assertEqual("llamacpp:ci-smollm2-135m", LOCK["providerKey"])
        self.assertNotIn("/main/", LOCK["downloadUrl"])
        self.assertIn(LOCK["revision"], LOCK["downloadUrl"])
        policy = LOCK["distributionPolicy"]
        self.assertFalse(policy["commitModel"])
        self.assertFalse(policy["packageModel"])
        self.assertFalse(policy["uploadModelArtifact"])

    def test_base_model_license_evidence_is_explicit(self) -> None:
        evidence = LOCK["sourceModelLicenseEvidence"]
        self.assertEqual("HuggingFaceTB/SmolLM2-135M-Instruct", evidence["repository"])
        self.assertEqual("Apache-2.0", evidence["license"])
        self.assertEqual("83212e1e2b3cfd6958f3707877bb878945dea8ee", evidence["revision"])
        self.assertIn(evidence["revision"], evidence["url"])

    def test_runtime_proof_consumes_local_bytes_and_has_no_downloader(self) -> None:
        self.assertIn("HAVEN_REAL_MODEL_PATH", PROOF)
        self.assertIn("_hash_regular_file(model_path)", PROOF)
        self.assertIn("modelctl.py", PROOF)
        self.assertNotIn("urllib.request", PROOF)
        self.assertNotIn("requests.get", PROOF)
        self.assertNotIn("requests.post", PROOF)
        self.assertNotIn("curl ", PROOF)
        self.assertNotIn("huggingface_hub", PROOF)

    def test_workflow_verifies_model_before_proof_and_never_uploads_gguf(self) -> None:
        verify_index = WORKFLOW.index("sha256sum -c -")
        proof_index = WORKFLOW.index("run: python cakeos/runtime/llamacpp/tests/real_runtime_proof.py")
        self.assertLess(verify_index, proof_index)
        self.assertIn("stat -c '%s'", WORKFLOW)
        self.assertIn("rm -f \"$MODEL_PATH\"", WORKFLOW)
        self.assertIn("name: haven-llamacpp-real-cpu-proof", WORKFLOW)
        upload_section = WORKFLOW.split("name: haven-llamacpp-real-cpu-proof", 1)[1]
        self.assertIn("real-runtime-evidence.json", upload_section)
        self.assertNotIn("*.gguf", upload_section)


if __name__ == "__main__":
    unittest.main()
