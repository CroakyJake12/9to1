"""Real-behaviour tests for the donor pin resolution used by eng/check-donor-sources.py.

The gate itself audits this repository, so these tests target the pure decision functions that decide
what revision an app is held to and whether a framework donor's vendored tree still matches its fork.
No network, no SDK, no native execution.
"""

import importlib.util
import json
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


class AspirationalBindingTests(unittest.TestCase):
    def test_annotation_needs_every_field_or_the_binding_still_fails(self):
        # Marking a donor 'aspirational' must never be a way to silence the gate: a half-annotation is
        # worse than none, because it looks like a resolved decision.
        entry = {"app": "Games", "donor": "godot", "status": "aspirational"}
        self.assertEqual(gate.aspirational_binding(entry), ["plannedVerdict", "reason", "specLine"])
        complete = dict(entry, plannedVerdict="FORK", reason="no Games app source exists yet",
                        specLine=14523)
        self.assertEqual(gate.aspirational_binding(complete), [])

    def test_an_unannotated_missing_source_stays_a_hard_failure(self):
        # The 17 pre-existing unvendored bindings carry no annotation, so they keep failing. The
        # annotation exists for future donors nothing consumes yet, not as an escape hatch.
        self.assertEqual(gate.aspirational_binding({"app": "Write", "donor": "libreoffice"}),
                         ["status", "plannedVerdict", "reason", "specLine"])


class FrameworkEquivalenceTests(unittest.TestCase):
    def setUp(self):
        import tempfile
        from unittest import mock
        self.temp = tempfile.TemporaryDirectory(prefix="framework-equiv-", dir="/tmp/opencode")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "eng").mkdir()
        self.git_patch = mock.patch.object(gate, "ROOT", self.root)
        self.git_patch.start()
        self.addCleanup(self.git_patch.stop)
        self.git_out = mock.patch.object(gate, "git", return_value="0" * 40)
        self.git_out.start()
        self.addCleanup(self.git_out.stop)
        self.entry = {"app": "CUI", "donor": "avalonia", "owner": "framework/CUI",
                      "path": "framework/CUI/vendor/Avalonia", "scope": "framework",
                      "equivalenceReceipt": "eng/receipt.json",
                      "wrapperPaths": [".revision", "SOURCE.md"]}
        self.record = {"fork": "https://github.com/CroakyJake12/Avalonia", "commit": "0" * 40}

    def write(self, receipt):
        (self.root / "eng/receipt.json").write_text(json.dumps(receipt) + "\n")

    def test_receipt_matching_the_recorded_tree_is_clean(self):
        self.write({"path": self.entry["path"], "repository_tree": "0" * 40,
                    "fork_ref": "b", "fork_commit": "0" * 40,
                    "equivalence": {"differing_paths": [], "only_in_repository_tree": [".revision"],
                                    "only_in_fork_branch": ["docs/x.md"], "shared_paths": 3,
                                    "identical_blobs": 3}})
        self.assertEqual(gate.framework_verification_problems(self.entry, self.record,
                                                              self.entry["path"]), [])

    def test_a_moved_vendored_tree_is_reported(self):
        self.write({"path": self.entry["path"], "repository_tree": "1" * 40, "fork_commit": "0" * 40,
                    "equivalence": {"differing_paths": [], "only_in_repository_tree": []}})
        problems = gate.framework_verification_problems(self.entry, self.record, self.entry["path"])
        self.assertTrue(any("vendored tree changed since the equivalence receipt" in p for p in problems),
                        problems)

    def test_differing_paths_and_undeclared_wrapper_files_are_reported(self):
        self.write({"path": self.entry["path"], "repository_tree": "0" * 40, "fork_commit": "0" * 40,
                    "equivalence": {"differing_paths": ["src/A.cs"],
                                    "only_in_repository_tree": ["src/Undeclared.cs"]}})
        problems = gate.framework_verification_problems(self.entry, self.record, self.entry["path"])
        self.assertTrue(any("differ from the fork branch" in p for p in problems), problems)
        self.assertTrue(any("does not declare as wrapper records" in p for p in problems), problems)

    def test_receipt_pointing_at_another_path_is_refused(self):
        self.write({"path": "somewhere/else", "repository_tree": "0" * 40, "fork_commit": "0" * 40,
                    "equivalence": {"differing_paths": [], "only_in_repository_tree": []}})
        problems = gate.framework_verification_problems(self.entry, self.record, self.entry["path"])
        self.assertTrue(any("does not match the binding path" in p for p in problems), problems)

    def test_no_receipt_at_all_is_refused_rather_than_treated_as_verified(self):
        self.entry = dict(self.entry, equivalenceReceipt="eng/absent.json")
        problems = gate.framework_verification_problems(self.entry, self.record, self.entry["path"])
        self.assertTrue(any("equivalenceReceipt missing" in p for p in problems), problems)
        self.entry = dict(self.entry, equivalenceReceipt=None)
        self.assertTrue(any("no equivalenceReceipt recorded" in p
                            for p in gate.framework_verification_problems(self.entry, self.record,
                                                                          self.entry["path"])))


if __name__ == "__main__":
    unittest.main()
