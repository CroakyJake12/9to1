"""Real-behaviour tests for the donor pin resolution used by eng/check-donor-sources.py.

The gate itself audits this repository, so these tests target the pure decision function that decides
what revision an app is held to. No network, no SDK, no native execution.
"""

import importlib.util
import sys
import unittest
from pathlib import Path

MODULE = Path(__file__).resolve().parents[1] / "check-donor-sources.py"
SPEC = importlib.util.spec_from_file_location("check_donor_sources", MODULE)
gate = importlib.util.module_from_spec(SPEC)
sys.modules["check_donor_sources"] = gate
SPEC.loader.exec_module(gate)

FULL = "8cac558fa73ee63d958cec05124ea2fdbd7d608d"
OTHER = "1a728d6a85db3528f9c79dc0990700e91b22696f"


class ResolvedPinTests(unittest.TestCase):
    def test_shared_record_commit_is_the_default(self):
        commit, fork, problems = gate.resolved_pin({"app": "Boards", "donor": "rnote"},
                                                   {"commit": OTHER, "forkCommit": OTHER,
                                                    "fork": "https://github.com/CroakyJake12/rnote"})
        self.assertEqual((commit, fork), (OTHER, OTHER))
        self.assertEqual(problems, [])

    def test_per_app_override_needs_a_recorded_reason(self):
        # Deliberate divergence between apps is allowed; undocumented divergence is not.
        commit, _, problems = gate.resolved_pin({"app": "Canvas", "donor": "rnote", "commit": FULL},
                                                {"commit": OTHER, "forkCommit": OTHER,
                                                 "fork": "https://github.com/CroakyJake12/rnote"})
        self.assertEqual(commit, FULL)
        self.assertIn("per-app donor pin has no recorded commitReason", problems)

    def test_justified_per_app_divergence_is_accepted(self):
        _, _, problems = gate.resolved_pin(
            {"app": "Canvas", "donor": "rnote", "commit": FULL, "forkCommit": FULL,
             "commitReason": "rnote-poc builds against v0.14.2"},
            {"commit": OTHER, "forkCommit": OTHER,
             "fork": "https://github.com/CroakyJake12/rnote"})
        self.assertEqual(problems, [])

    def test_a_forked_donor_whose_fork_and_upstream_disagree_still_fails(self):
        _, _, problems = gate.resolved_pin({"app": "Data", "donor": "duckdb"},
                                           {"commit": FULL, "forkCommit": OTHER,
                                            "fork": "https://github.com/CroakyJake12/duckdb"})
        self.assertIn("fork and upstream revisions disagree", problems)

    def test_donors_imported_as_plain_source_have_no_controlled_fork_to_agree_with(self):
        # Files is imported as source files with verified tree hashes, and the original gate never
        # applied the fork-agreement check to it. Moving that check into the shared resolver must not
        # start failing Files for having no forkCommit.
        _, _, problems = gate.resolved_pin({"app": "Files", "donor": "files"},
                                           {"commit": FULL, "forkCommit": None})
        self.assertEqual(problems, [])

    def test_partial_or_missing_commits_are_rejected(self):
        _, _, problems = gate.resolved_pin({"app": "X", "donor": "y"}, {"commit": "deadbeef"})
        self.assertIn("no exact immutable upstream commit", problems)


if __name__ == "__main__":
    unittest.main()
