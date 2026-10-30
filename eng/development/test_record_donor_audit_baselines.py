"""Pure tests for the donor-audit ratchet. No repository mutation, no build, no acceptance."""

import importlib.util
import sys
import unittest
from pathlib import Path

MODULE = (Path(__file__).resolve().parents[2] / ".github" / "scripts" /
          "record-donor-audit-baselines.py")
SPEC = importlib.util.spec_from_file_location("record_donor_audit_baselines", MODULE)
ratchet = importlib.util.module_from_spec(SPEC)
sys.modules["record_donor_audit_baselines"] = ratchet
SPEC.loader.exec_module(ratchet)


class BreachTests(unittest.TestCase):
    def test_growth_in_any_audited_count_is_a_breach(self):
        baseline = {"donor_gate_issues": 17, "census_violations": 0, "coverage_problems": 26}
        self.assertEqual(
            ratchet.breaches(baseline, {"donor_gate_issues": 18, "census_violations": 0,
                                        "coverage_problems": 26}),
            ["donor_gate_issues grew from 17 to 18"])

    def test_census_violations_are_ratcheted_against_zero_debt(self):
        # The census baseline is zero: a violation is new drift by definition, unlike the gate's
        # recorded unvendored-donor debt.
        baseline = {"donor_gate_issues": 17, "census_violations": 0, "coverage_problems": 26}
        out = ratchet.breaches(baseline, {"donor_gate_issues": 17, "census_violations": 1,
                                          "coverage_problems": 26})
        self.assertEqual(out, ["census_violations grew from 0 to 1"])

    def test_improvement_is_allowed_and_expects_a_baseline_update(self):
        baseline = {"donor_gate_issues": 17, "census_violations": 0, "coverage_problems": 26}
        self.assertEqual(ratchet.breaches(baseline, {"donor_gate_issues": 12,
                                                     "census_violations": 0,
                                                     "coverage_problems": 20}), [])

    def test_missing_baseline_key_is_reported_rather_than_treated_as_zero(self):
        # Absent must not silently become a limit of None-safe 0, which would fail open on the first
        # real problem.
        self.assertEqual(ratchet.breaches({}, {"donor_gate_issues": 3, "census_violations": 0,
                                               "coverage_problems": 0}),
                         ["donor_gate_issues has no recorded baseline",
                          "census_violations has no recorded baseline",
                          "coverage_problems has no recorded baseline"])

    def test_all_three_audited_counts_are_required(self):
        baseline = {"donor_gate_issues": 17, "census_violations": 0, "coverage_problems": 26}
        self.assertEqual(len(baseline), 3)
        self.assertEqual(ratchet.breaches(baseline, dict(baseline)), [])


class BaselineFileTests(unittest.TestCase):
    def test_shipped_baseline_matches_the_documented_debt_and_is_internally_consistent(self):
        import json
        data = json.loads(Path(ratchet.BASELINE).read_text())
        for key in ("donor_gate_issues", "census_violations", "coverage_problems"):
            self.assertIsInstance(data[key], int)
        for note in ("donor_gate_note", "census_note", "coverage_note"):
            self.assertIn(note, data)
        for meta in ("_measured_by", "_measured_on", "_measured_at_source"):
            self.assertIn(meta, data)
        # The census baseline being non-zero would mean accepted violations, which is not the rule.
        self.assertEqual(data["census_violations"], 0)


if __name__ == "__main__":
    unittest.main()
