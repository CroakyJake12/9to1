from __future__ import annotations

import pathlib
import sys
import unittest

RUNTIME = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RUNTIME))

import budget  # noqa: E402


class BudgetTests(unittest.TestCase):
    def test_transformer_kv_payload_formula(self) -> None:
        value = budget.transformer_kv_payload_bytes(
            layers=2, context=4, kv_heads=3, head_dim=5, k_type="f16", v_type="f16"
        )
        self.assertEqual(2 * 4 * 3 * 5 * 4, value)

    def test_cpu_only_budget_is_explicit(self) -> None:
        result = budget.estimate_budget(
            weights_bytes=1000, kv_bytes=200, gpu_offload_fraction=0,
            kv_location="cpu", runtime_overhead_fraction=0.10, safety_fraction=0.10,
        )
        self.assertEqual(1200, result["cpu"]["planningFloorBytes"])
        self.assertEqual(1452, result["cpu"]["recommendedBudgetBytes"])
        self.assertEqual(0, result["gpu"]["recommendedBudgetBytes"])
        self.assertEqual("planning-estimate-not-runtime-measurement", result["method"])

    def test_gpu_split_keeps_weight_bytes_conserved(self) -> None:
        result = budget.estimate_budget(
            weights_bytes=1001, kv_bytes=100, gpu_offload_fraction=0.5,
            kv_location="gpu", runtime_overhead_fraction=0, safety_fraction=0,
        )
        self.assertEqual(500, result["cpu"]["planningFloorBytes"])
        self.assertEqual(601, result["gpu"]["planningFloorBytes"])


if __name__ == "__main__":
    unittest.main()
