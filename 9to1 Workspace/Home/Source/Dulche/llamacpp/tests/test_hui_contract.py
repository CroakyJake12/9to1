from __future__ import annotations

import json
import pathlib
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
REPOSITORY = RUNTIME.parents[4]


class LegacyHuiProviderContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.hui = json.loads(
            (REPOSITORY / "9to1 OS/HUI/llamacpp-provider.json").read_text(encoding="utf-8")
        )
        cls.runtime = json.loads(
            (RUNTIME / "provider-contract.json").read_text(encoding="utf-8")
        )

    def test_hui_registry_matches_runtime_identity_and_transport(self) -> None:
        self.assertEqual("llamacpp", self.hui["providerId"])
        self.assertEqual(self.runtime["providerId"], self.hui["providerId"])
        self.assertEqual(self.runtime["displayName"], self.hui["displayName"])
        self.assertEqual("local", self.hui["kind"])
        self.assertEqual("http-over-unix", self.hui["transport"]["type"])
        self.assertFalse(self.hui["transport"]["networkRequired"])

    def test_discovery_and_status_use_authoritative_broker_endpoints(self) -> None:
        self.assertEqual("GET", self.hui["discovery"]["probe"]["method"])
        self.assertEqual("/v1/provider", self.hui["discovery"]["probe"]["path"])
        self.assertEqual("/health", self.hui["discovery"]["health"]["path"])
        self.assertEqual("GET /v1/provider", self.hui["status"]["provider"])
        self.assertEqual("GET /health", self.hui["status"]["health"])
        self.assertEqual("GET /v1/models", self.hui["status"]["models"])
        self.assertEqual(2, self.runtime["schemaVersion"])
        self.assertTrue({
            "GET /health",
            "GET /v1/provider",
            "GET /v1/models",
            "POST /v1/models/{id}/load",
            "POST /v1/models/{id}/unload",
            "POST /v1/chat/completions",
            "POST /v1/requests/{request_id}/cancel",
        }.issubset(self.runtime["endpoints"]))

    def test_runtime_adds_slot_and_permission_contracts_without_changing_legacy_paths(self) -> None:
        self.assertEqual("single-broker-owned-slot-workers", self.runtime["runtime"]["topology"])
        self.assertEqual("next available slot", self.runtime["embeddingApi"]["blankSlot"])
        self.assertEqual("after the current turn completes", self.runtime["embeddingApi"]["contextApplication"])
        self.assertEqual("permission requests only", self.runtime["tools"]["modelRuntimeAccess"])
        self.assertEqual(10, self.runtime["tools"]["approvalTimeoutSeconds"])
        self.assertIn("GET /v1/permissions/events", self.runtime["endpoints"])

    def test_model_key_compatibility_and_execution_contract_are_explicit(self) -> None:
        self.assertEqual("llamacpp:<model-id>", self.hui["modelKeys"]["qualified"])
        self.assertEqual("ollama", self.hui["modelKeys"]["legacyUnqualifiedProvider"])
        self.assertEqual("ollama", self.hui["modelKeys"]["unqualifiedResolution"])
        self.assertTrue(self.hui["execution"]["requiredRequestId"])
        self.assertTrue(self.hui["execution"]["streaming"])
        self.assertEqual(
            "POST /v1/requests/{request_id}/cancel",
            self.hui["execution"]["cancelEndpoint"],
        )
        self.assertTrue(self.hui["status"]["brokerReadOnlyModelStore"])
        self.assertIn("approved local GGUF", self.hui["evidenceBoundaries"]["modelExecution"])


if __name__ == "__main__":
    unittest.main()
