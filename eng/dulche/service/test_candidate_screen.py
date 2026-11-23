import copy
import json
import tempfile
import unittest
from pathlib import Path

from candidate_screen import EVIDENCE, candidate_report, observation


class CandidateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.dir = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.aliases = {"qwen3.8-27b-v100": "Qwen/Qwen3.8-27B"}
        ticket = {
            "job": 0,
            "task": "sql_ledger",
            "complete": True,
            "correct": True,
            "verified": True,
            "context_qualified": True,
            "finish_reason": "stop",
            "status": 200,
            "generated_tokens": 100,
            "ttft_s": 0.8,
            "generation_tps_server": 27.3,
        }
        self.cell = {
            "status": "completed",
            "model": "qwen3.8-27b-v100",
            "gpu": "v100",
            "id": "C01",
            "start_utc": "2026-11-22T16:00:00Z",
            "slots": 1,
            "offered_clients": 1,
            "wall_s": 5.0,
            "tickets": [ticket],
            "jobs_expected": 1,
            "returned_tickets": 1,
            "verified": 1,
            "completed_streams": 1,
            "aggregate_generated_tps": 20,
            "verified_tasks_h": 720,
            "context_budget": 0,
            "completion_budget": 768,
            "temperature": 0,
            "seed": 4703,
        }

    def test_immutable_artifacts_match_complete_shard_groups(self):
        data = json.loads(EVIDENCE.read_text())
        self.assertGreaterEqual(len(data["items"]), 14)
        for candidate in data["items"]:
            self.assertRegex(candidate["revision"], r"^[0-9a-f]{40}$")
            self.assertTrue(candidate["license"])
            self.assertTrue(candidate["source"].startswith("https://huggingface.co/"))
            if candidate["parameters"] > 500_000_000_000:
                self.assertEqual([], candidate["quantizations"])
            for option in candidate["quantizations"]:
                self.assertEqual(
                    option["bytes"], sum(f["bytes"] for f in option["files"])
                )
                self.assertRegex(option["revision"], r"^[0-9a-f]{40}$")
                for f in option["files"]:
                    self.assertRegex(f["sha256"], r"^[0-9a-f]{64}$")
                    self.assertGreater(f["bytes"], 0)
                if len(option["files"]) > 1:
                    for i, f in enumerate(
                        sorted(option["files"], key=lambda x: x["path"]), 1
                    ):
                        self.assertIn(
                            f"-{i:05}-of-{len(option['files']):05}.gguf", f["path"]
                        )

    def test_new_ingest_updates_leaderboards_and_rates_without_double_counting(self):
        folder = self.dir / "benchmarks"
        folder.mkdir()
        self.assertEqual([], candidate_report(self.dir)["leaderboards"])
        file = folder / "local.json"
        file.write_text(json.dumps({"schema_version": 1, "cells": [self.cell]}))
        r = candidate_report(self.dir, [file])
        ranked = r["leaderboards"][0]["ranked_by_verified_tasks_per_hour"]
        self.assertEqual(1, len(ranked))
        self.assertEqual(720, ranked[0]["verified_tasks_per_hour"])
        self.assertEqual(20, ranked[0]["aggregate_generated_tps"])
        self.assertEqual("unverified", ranked[0]["sustained_independent_pair_gate"])

    def test_quality_losses_and_different_tasks_remain_separate(self):
        failed = copy.deepcopy(self.cell)
        failed.update(id="C02", verified=0, verified_tasks_h=0)
        failed["tickets"][0].update(
            verified=False, correct=False, finish_reason="length"
        )
        other = copy.deepcopy(self.cell)
        other["id"] = "C03"
        other["context_budget"] = 32768
        file = self.dir / "explicit.json"
        file.write_text(
            json.dumps({"schema_version": 1, "cells": [self.cell, other, failed]})
        )
        r = candidate_report(self.dir, [file])
        self.assertEqual(2, len(r["leaderboards"]))
        self.assertEqual("C02", r["quality_incomplete_trials"][0]["cell"])
        self.assertIsNone(r["specialist_role_results"]["best_ui_designer"])

    def test_invalid_counters_and_unknown_models_do_not_rank(self):
        for change in [
            {"verified": 2},
            {"completed_streams": 2},
            {"aggregate_generated_tps": 200},
            {"verified_tasks_h": float("nan")},
            {"jobs_expected": 2},
            {"model": "unknown"},
            {"status": "partial"},
        ]:
            with self.subTest(change=change):
                self.assertIsNone(
                    observation(self.cell | change, self.aliases, "test.json")
                )

    def test_service_native_receipts_are_recomputed_and_never_added_across_gpus(self):
        run = {
            "id": "run-1",
            "model": "qwen3.8-27b-v100",
            "state": "completed",
            "issued": 2,
            "passed": 2,
            "wall_s": 10,
            "max_tokens": 768,
            "quality_gate_passed": True,
            "accepted_output_tps": 22,
            "verified_tasks_per_hour": 720,
            "gpu": "Tesla V100",
            "context": 32768,
            "tickets": [
                {
                    "task": "boundary",
                    "correct": True,
                    "accepted_output_tokens": 120,
                    "first_token_s": 1,
                },
                {
                    "task": "contract",
                    "correct": True,
                    "accepted_output_tokens": 100,
                    "first_token_s": 2,
                },
            ],
        }
        other = copy.deepcopy(run)
        other.update(
            id="different-id",
            model="qwen3.8-flash-next-unsloth-ud-iq4_xs",
            gpu="RTX 5080",
        )
        report = candidate_report(self.dir, service_runs=[run, other, run])
        rows = report["leaderboards"][0]["ranked_by_verified_tasks_per_hour"]
        self.assertEqual(2, len(rows))
        self.assertEqual([22, 22], [r["aggregate_generated_tps"] for r in rows])
        self.assertTrue(
            all(r["sustained_independent_pair_gate"] == "unverified" for r in rows)
        )
        damaged = candidate_report(
            self.dir,
            service_runs=[run | {"passed": 1}, run | {"accepted_output_tps": 120}],
        )
        self.assertEqual([], damaged["leaderboards"])
        self.assertEqual(2, damaged["unranked_service_runs"])

    def test_service_budget_and_task_order_are_part_of_cohort(self):
        run = {
            "id": "r1",
            "model": "qwen3.8-27b-v100",
            "state": "completed",
            "issued": 2,
            "passed": 2,
            "wall_s": 10,
            "quality_gate_passed": True,
            "accepted_output_tps": 1,
            "verified_tasks_per_hour": 720,
            "tickets": [
                {"task": "boundary", "correct": True, "accepted_output_tokens": 5},
                {"task": "contract", "correct": True, "accepted_output_tokens": 5},
            ],
        }
        report = candidate_report(
            self.dir, service_runs=[run, run | {"id": "r2", "max_tokens": 1536}]
        )
        self.assertEqual(2, len(report["leaderboards"]))
        self.assertEqual(
            [],
            candidate_report(
                self.dir,
                service_runs=[run | {"tickets": list(reversed(run["tickets"]))}],
            )["leaderboards"],
        )

    def test_installed_flash_receipt_is_distinct_from_current_quant(self):
        row = next(
            r
            for r in json.loads(EVIDENCE.read_text())["items"]
            if r["id"] == "Qwen/Qwen3.8-Flash-Next"
        )
        self.assertEqual(3, len(row["quantizations"][0]["files"]))
        self.assertEqual(4, len(row["quantizations"][1]["files"]))
        self.assertNotEqual(
            row["quantizations"][0]["revision"], row["quantizations"][1]["revision"]
        )
