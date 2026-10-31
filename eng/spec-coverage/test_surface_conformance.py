"""Tests for the CUI-versus-foreign surface measurement.

The owner's rule under test, restated 31 October 2026: "the main distinction is replace any foreign ui
rendering. main ui structure is rendered in cui", with `.xaml`, `.axaml` and `.hui` all foreign. This tool makes
that a watchable count, so these tests pin the counting rules and, above all, that the baseline can only move in
the honest direction.

Static file measurement: nothing here renders or runs anything.
"""
from __future__ import annotations

import json
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import surface_conformance as sc


class MeasureRuleTests(unittest.TestCase):
    def test_donor_markup_is_not_counted_as_our_violation(self):
        """Upstream Avalonia/WPF markup inside a pinned donor checkout is not a 9to1 surface."""
        prefixes = sc.donor_prefixes()
        self.assertGreater(len(prefixes), 30)
        now = sc.measure()
        for rel in now["foreign_files"]:
            self.assertFalse(any(rel == p or rel.startswith(p + "/") for p in prefixes),
                             f"donor material counted as ours: {rel}")

    def test_stage_a_mirror_documents_are_counted_separately_from_first_party_cui(self):
        now = sc.measure()
        self.assertGreater(now["mirror_cui_total"], 1700)
        self.assertNotEqual(now["own_cui_total"], now["mirror_cui_total"])
        # Boards measured: 879 stage-A mirror documents, and its own count pinned from the recorded baseline.
        # Conflating the two would make donor translation look like product conformance. The mirror figure stays
        # exact because generation changes must be seen; the own figure is a one-way floor, because adding a
        # first-party CUI document (measured: Boards rose 11 -> 12 when AppFlowyBoardStageOne.cui landed) is the
        # improvement this ratchet exists to record. A drop below the floor still fails the gate.
        self.assertEqual(now["mirror_cui_per_app"].get("Boards", 0), 879)
        recorded = json.loads(sc.BASELINE.read_text())["own_cui_per_app"]["Boards"]
        self.assertGreaterEqual(now["own_cui_per_app"].get("Boards", 0), recorded)
        self.assertLess(now["own_cui_per_app"].get("Boards", 0), recorded + 200)

    def test_test_fixtures_are_not_counted_as_shipping_surfaces(self):
        now = sc.measure()
        self.assertFalse([f for f in now["foreign_files"] if "/tests/" in f.lower()],
                         "a test fixture is not a shipping surface")

    def test_shell_surfaces_are_named_not_buried_in_a_total(self):
        now = sc.measure()
        self.assertGreater(now["foreign_shell_total"], 30)
        joined = " ".join(now["foreign_shell_files"]).lower()
        for marker in ("mainview", "mainwindow", "sidebar", "/views/pages/"):
            self.assertIn(marker, joined, marker)

    def test_every_counted_file_is_a_real_first_party_path(self):
        now = sc.measure()
        for rel in now["foreign_files"]:
            self.assertTrue((sc.ROOT / rel).is_file(), rel)


class BaselineDirectionTests(unittest.TestCase):
    """The ratchet must be able to go one way only."""

    BASE = {"foreign_total": 10, "own_cui_total": 40, "mirror_cui_total": 500,
            "foreign_files": [f"9to1 Workspace/X/a{i}.axaml" for i in range(10)]}

    def now(self, **changes):
        base = {"foreign_total": 10, "own_cui_total": 40, "mirror_cui_total": 500,
                "foreign_files": [f"9to1 Workspace/X/a{i}.axaml" for i in range(10)]}
        base.update(changes)
        return base

    def test_no_change_is_clean(self):
        self.assertEqual(sc.compare(self.BASE, self.now()), [])

    def test_a_new_foreign_view_is_a_breach(self):
        problems = sc.compare(self.BASE, self.now(foreign_total=11,
                                                  foreign_files=self.BASE["foreign_files"]
                                                  + ["9to1 Workspace/X/NewPage.axaml"]))
        self.assertTrue(any("foreign rendering grew 10 -> 11" in p for p in problems), problems)
        self.assertTrue(any("NewPage.axaml" in p for p in problems), problems)

    def test_deleting_a_cui_document_to_improve_a_ratio_is_a_breach(self):
        problems = sc.compare(self.BASE, self.now(own_cui_total=30))
        self.assertTrue(any("must not be deleted to improve a ratio" in p for p in problems), problems)

    def test_losing_mirror_documents_is_a_breach_that_must_be_explained(self):
        problems = sc.compare(self.BASE, self.now(mirror_cui_total=499))
        self.assertTrue(any("lost mirror must be explained" in p for p in problems), problems)

    def test_removing_foreign_markup_and_adding_cui_is_allowed(self):
        self.assertEqual(sc.compare(self.BASE, self.now(foreign_total=8, own_cui_total=52,
                                                        foreign_files=self.BASE["foreign_files"][:8])), [])


class CliTests(unittest.TestCase):
    def run_tool(self, *argv):
        return subprocess.run([sys.executable, "-B", str(HERE / "surface_conformance.py"), *argv],
                              capture_output=True, text=True, cwd=sc.ROOT)

    def test_the_committed_baseline_exists_and_declares_itself_a_violation_level(self):
        if not sc.BASELINE.is_file():
            self.skipTest("baseline is recorded by a measured run")
        data = json.loads(sc.BASELINE.read_text())
        self.assertIn("_reason", data)
        # Matched on a machine-readable field, not on prose: re-recording a baseline with a different
        # human-readable _reason must not break the gate that proves the record is a ratchet baseline.
        self.assertEqual(sc.NATURE, data["_nature"])
        self.assertIn("static", data["_evidence_level"].lower())

    def test_check_passes_against_the_committed_baseline(self):
        result = self.run_tool("check")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("no new foreign rendering", result.stdout)

    def test_recording_without_a_reason_is_refused(self):
        result = self.run_tool("record")
        self.assertEqual(result.returncode, 2)
        self.assertIn("needs a reason", result.stderr)


if __name__ == "__main__":
    unittest.main()
