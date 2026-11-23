import copy
import unittest

from swarm_metrics import comparisons, metrics


class MetricsTests(unittest.TestCase):
    def doc(self):
        return {
            "id": "first",
            "config": {
                "objective": "Compare",
                "completion_criteria": "accepted artifact",
                "mentor": {"model": "m"},
                "workers": [{"model": "w"}],
                "max_workers": 2,
                "limits": {},
                "check_in": {},
                "strategy": "resident",
            },
            "state": "completed",
            "created": 1,
            "started_mono": 100,
            "ended_mono": 110,
            "samples": [[105, "a", 30], [106, "b", 40]],
            "decisions": [{"exclusive_check_in_s": 2}],
            "tasks": {
                "a": {
                    "id": "a",
                    "role": "developer",
                    "instruction": "A",
                    "acceptance": "check",
                    "depends_on": [],
                    "verified": True,
                }
            },
            "jobs": {
                "a": {
                    "run": "a",
                    "kind": "worker",
                    "worker": "a",
                    "model": "w",
                    "role": "developer",
                    "handled": True,
                    "usage_tokens": 90,
                    "response_s": 3,
                    "ended_mono": 108,
                    "live_counts": True,
                },
                "b": {
                    "run": "b",
                    "kind": "worker",
                    "worker": "b",
                    "model": "w",
                    "role": "developer",
                    "handled": True,
                    "usage_tokens": 100,
                    "response_s": 2,
                    "ended_mono": 109,
                    "live_counts": True,
                },
            },
        }

    def test_common_window_and_individual_speeds_are_distinct(self):
        out = metrics(self.doc())
        self.assertEqual(19, out["finished"]["aggregate_tps"])
        self.assertEqual(
            [30, 50], [r["individual_tps"] for r in out["finished"]["workers"]]
        )
        self.assertEqual(7, out["rolling"]["aggregate_tps"])
        self.assertEqual([3, 4], [r["tps"] for r in out["rolling"]["workers"]])

    def test_missing_rolling_or_final_token_counts_are_visible(self):
        doc = self.doc()
        del doc["jobs"]["a"]["usage_tokens"]
        doc["jobs"]["b"]["live_counts"] = False
        out = metrics(doc)
        self.assertIsNone(out["finished"]["aggregate_tps"])
        self.assertEqual(1, out["finished"]["missing_usage"])
        self.assertIsNone(out["rolling"]["aggregate_tps"])
        self.assertEqual(1, out["rolling"]["missing_usage"])

    def test_comparison_uses_distinct_strategies_and_matching_fully_verified_work(self):
        a = self.doc()
        b = copy.deepcopy(a)
        b["id"] = "second"
        b["config"]["strategy"] = "alternating"
        b["ended_mono"] = 111
        b["decisions"][0]["exclusive_check_in_s"] = 5
        x = comparisons([a, b])
        self.assertEqual(2, len(x["trials"]))
        self.assertEqual(1, len(x["matched_break_even_estimates"]))
        self.assertGreater(
            x["matched_break_even_estimates"][0]["estimated_break_even_cycle_seconds"],
            5,
        )
        b["tasks"]["a"]["verified"] = False
        self.assertEqual([], comparisons([a, b])["matched_break_even_estimates"])
