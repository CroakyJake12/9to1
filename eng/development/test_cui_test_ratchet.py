"""Rules of the CUI framework test ratchet. Pure logic; no dotnet, no network, no acceptance."""
from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import cui_test_ratchet as ratchet


BASELINE = {"projects": [{"project": "P", "total": 100, "passed": 96, "failed": 4,
                          "known_failures": ["P.A", "P.B", "P.C", "P.D"]}]}


class RatchetRuleTests(unittest.TestCase):
    def test_a_failure_outside_the_recorded_baseline_is_a_breach(self):
        results = [{"project": "P", "ran": True, "total": 100, "passed": 95, "failed": 5,
                    "failure_names": ["P.A", "P.B", "P.C", "P.D", "P.brand-new"]}]
        problems, fixed = ratchet.evaluate(BASELINE, results)
        self.assertTrue(any("outside the recorded baseline" in p and "P.brand-new" in p for p in problems),
                        problems)
        self.assertEqual(fixed, [])

    def test_the_recorded_flaky_failures_may_move_between_runs_without_breaching(self):
        # The runtime suite reports different subsets of the same flaky family run to run; the union is
        # recorded so a reshuffle is not treated as progress or regression.
        results = [{"project": "P", "ran": True, "total": 100, "passed": 98, "failed": 2,
                    "failure_names": ["P.A", "P.B"]}]
        problems, fixed = ratchet.evaluate(BASELINE, results)
        self.assertEqual(problems, [])
        self.assertEqual(len(fixed), 2, "passing known-failures must be surfaced, not silently absorbed")

    def test_losing_tests_is_a_breach_even_when_what_remains_passes(self):
        results = [{"project": "P", "ran": True, "total": 80, "passed": 80, "failed": 0,
                    "failure_names": []}]
        problems, _ = ratchet.evaluate(BASELINE, results)
        self.assertTrue(any("test count fell from 100 to 80" in p for p in problems), problems)

    def test_a_suite_that_cannot_run_is_a_breach_not_an_absence_of_evidence(self):
        results = [{"project": "P", "ran": False, "detail": "build error CS0246", "total": 0,
                    "passed": 0, "failed": 0, "failure_names": []}]
        problems, _ = ratchet.evaluate(BASELINE, results)
        self.assertTrue(any("did not run to a summary" in p for p in problems), problems)

    def test_dropping_a_project_from_the_ratchet_is_a_breach(self):
        problems, _ = ratchet.evaluate(BASELINE, [])
        self.assertTrue(any("recorded baseline project was not measured" in p for p in problems), problems)

    def test_summary_and_failure_names_are_parsed_from_real_vstest_output(self):
        text = ("  Failed P.SomeTest [12 ms]\n  Failed P.OtherTest [3 ms]\n"
                "Failed!  - Failed:     2, Passed:   737, Skipped:     0, Total:   749, Duration: 9 s\n")
        summary = ratchet.SUMMARY.search(text)
        self.assertEqual(tuple(int(x) for x in summary.groups()), (2, 737, 0, 749))
        self.assertEqual(ratchet.FAILED_NAME.findall(text), ["P.SomeTest", "P.OtherTest"])

    def test_every_cui_test_project_is_under_the_ratchet(self):
        joined = "\n".join(ratchet.PROJECTS)
        for needle in ("Markup.Tests", "Compiler.Tests", "AI.Tests", "DevTools.Tests", "Runtime.Tests"):
            self.assertIn(needle, joined)

    def test_the_committed_baseline_file_exists_and_states_it_is_not_acceptance(self):
        path = Path(ratchet.BASELINE)
        if not path.is_file():
            self.skipTest("baseline is recorded by a measured run, not invented here")
        data = json.loads(path.read_text())
        self.assertIn("_flakiness_note", data)
        self.assertTrue(any("compile/test" in row for row in [data["_purpose"]]))
        self.assertTrue(data["projects"], "at least one project recorded")


if __name__ == "__main__":
    unittest.main()
