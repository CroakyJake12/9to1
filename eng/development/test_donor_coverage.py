"""Real tiny fixtures only; no SDK, network, native or release acceptance."""

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import donor_coverage as coverage


class CoverageFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="donor-coverage-", dir="/tmp/opencode")
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        (self.repo / "eng").mkdir()
        (self.repo / "docs/specification").mkdir(parents=True)
        self.spec = self.repo / coverage.SPEC
        self.spec.write_text("line one\n"
                             "Primary donor: Gitea.\n"
                             "CUI is derived from a pinned Avalonia source fork\n"
                             "nothing else here\n")
        (self.repo / coverage.GITMODULES).write_text(
            '[submodule "gitea"]\n\tpath = x/Source/Gitea\n\turl = https://github.com/o/gitea.git\n')

    def designations(self):
        return [
            {"app": "Build", "donor": "gitea", "line": 2, "fragment": "Primary donor: Gitea."},
            {"app": "CUI framework", "donor": "avalonia", "line": 3,
             "fragment": "pinned Avalonia source fork"},
        ]

    def registry(self, bindings, upstreams=("gitea", "avalonia")):
        (self.repo / coverage.REGISTRY).write_text(json.dumps(
            {"upstreams": {u: {} for u in upstreams},
             "bindings": [{"app": a, "donor": d, "path": f"{a}/Source/{d}"}
                          for a, d in bindings]}) + "\n")

    def audit(self, designations):
        with mock.patch.object(coverage, "DESIGNATIONS", designations):
            return coverage.audit(self.repo)

    def test_designated_but_unbound_donor_is_reported(self):
        self.registry([("Build", "gitea")])
        result = self.audit(self.designations())
        self.assertTrue(any("avalonia" in p and "no binding" in p for p in result["problems"]),
                        result["problems"])
        row = [r for r in result["designations"] if r["donor"] == "avalonia"][0]
        self.assertFalse(row["declared_binding"])

    def test_binding_without_upstream_record_is_reported(self):
        self.registry([("Build", "gitea"), ("Maps", "comaps")], upstreams=("gitea", "avalonia"))
        result = self.audit(self.designations())
        self.assertTrue(any("binding without upstream record" in p for p in result["problems"]),
                        result["problems"])

    def test_designated_donor_without_a_gitmodules_entry_is_reported(self):
        self.registry([("Build", "gitea"), ("CUI framework", "avalonia")])
        result = self.audit(self.designations())
        self.assertTrue(any("no .gitmodules entry" in p and "avalonia" in p
                            for p in result["problems"]), result["problems"])

    def test_wording_that_moved_is_noted_but_the_designation_still_counts(self):
        # The specification is amended over time, so a recorded line number is not trustworthy. The
        # audit follows the wording instead, and says where it moved to.
        self.registry([("Build", "gitea")])
        moved = [{"app": "Build", "donor": "gitea", "line": 4, "fragment": "Primary donor: Gitea."},
                 {"app": "CUI framework", "donor": "avalonia", "line": 3,
                  "fragment": "pinned Avalonia source fork"}]
        result = self.audit(moved)
        self.assertEqual([r for r in result["designations"] if r["donor"] == "gitea"][0]
                         ["wording_found_at"], "2")
        self.assertTrue(any("wording is now on line 2, recorded 4" in n for n in result["notes"]),
                        result["notes"])
        self.assertFalse(any("gitea" in p and "no binding" in p for p in result["problems"]))

    def test_designation_wording_that_disappeared_entirely_is_a_problem(self):
        self.registry([("Build", "gitea")])
        gone = [{"app": "Build", "donor": "gitea", "line": 2,
                 "fragment": "this exact sentence no longer exists"},
                {"app": "CUI framework", "donor": "avalonia", "line": 3,
                 "fragment": "pinned Avalonia source fork"}]
        result = self.audit(gone)
        self.assertTrue(any("no longer in" in p for p in result["problems"]), result["problems"])

    def test_registry_binding_the_audit_cannot_match_is_flagged_for_verification(self):
        self.registry([("Build", "gitea"), ("CUI framework", "avalonia"), ("Planner", "libical")])
        result = self.audit(self.designations())
        self.assertTrue(any("libical" in p and "no specification designation" in p
                            for p in result["problems"]), result["problems"])

    def test_counts_come_from_the_data_not_from_a_hardcoded_number(self):
        self.registry([("Build", "gitea"), ("CUI framework", "avalonia")])
        result = self.audit(self.designations())
        self.assertEqual(result["counts"]["designations"], 2)
        self.assertEqual(result["counts"]["registry_bindings"], 2)

    def test_cli_refuses_to_touch_an_existing_report(self):
        self.registry([("Build", "gitea")])
        out = self.repo / "audit.json"
        out.write_text("keep\n")
        self.assertEqual(coverage.main(["audit", "--repo", str(self.repo), "--out", str(out)]), 2)
        self.assertEqual(out.read_text(), "keep\n")

    def test_cli_exits_non_zero_when_problems_exist(self):
        self.registry([("Build", "gitea")])
        with mock.patch.object(coverage, "DESIGNATIONS", self.designations()):
            code = coverage.main(["audit", "--repo", str(self.repo), "--json"])
        self.assertEqual(code, 1)


if __name__ == "__main__":
    unittest.main()
