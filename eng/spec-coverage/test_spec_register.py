"""Tests for the specification requirement register: the denominator must be defensible.

These tests exist because a percentage is only as good as its denominator. They pin the parsing decisions
(sentence splitting, normative level, code-fence exclusion, stem+bullet grouping, app attribution) and the two
attribution regressions found while building this tool, so a later edit cannot quietly change what "of N"
means without a test failing.

Pure text logic: no dotnet, no network, no rendering, no acceptance.
"""
from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import spec_register as reg


class LevelTests(unittest.TestCase):
    def test_must_level_words_are_mandatory(self):
        for text in ("The app MUST persist drafts.", "It MUST NOT lose data.", "Values SHALL be stable.",
                     "The surface is REQUIRED.", "The caller is required to confirm."):
            self.assertEqual(reg.level_of(text), "MUST", text)

    def test_lowercase_normative_words_count_as_the_same_level(self):
        # The specification states this explicitly in "Normative requirement language".
        self.assertEqual(reg.level_of("The toolbar must collapse items by priority."), "MUST")
        self.assertEqual(reg.level_of("Search should stay reachable."), "SHOULD")
        self.assertEqual(reg.level_of("Widgets may be reordered."), "MAY")

    def test_should_and_may_are_never_merged_into_the_must_set(self):
        self.assertEqual(reg.level_of("Apps SHOULD provide a shortcut."), "SHOULD")
        self.assertEqual(reg.level_of("Apps MAY offer a beta channel."), "MAY")

    def test_a_non_normative_sentence_is_not_a_requirement(self):
        self.assertIsNone(reg.level_of("The Canvas app uses an ink engine."))
        self.assertIsNone(reg.level_of("Donor parity is the goal here."))


class SentenceTests(unittest.TestCase):
    def test_two_requirements_on_one_line_are_two_statements(self):
        text = "Folders MUST nest. Sheets MUST support formulas."
        self.assertEqual(len(reg.sentences(text)), 2)

    def test_decimals_and_abbreviations_do_not_split_a_requirement_in_two(self):
        one = reg.sentences("A budget of 1.5 GiB MUST be reserved, e.g. for the host process.")
        self.assertEqual(len(one), 1, one)
        self.assertTrue(one[0].endswith("host process."))

    def test_a_lettered_enumeration_stays_one_statement(self):
        text = "Step a. Then the app MUST save. Step b. It MUST notify."
        parts = reg.sentences(text)
        self.assertEqual(len(parts), 2, parts)

    def test_whitespace_is_normalised_in_the_stored_text(self):
        parts = reg.sentences("The  app\n   MUST   persist drafts.")
        self.assertEqual(parts, ["The app MUST persist drafts."])


class ParseStructureTests(unittest.TestCase):
    """Structural rules over a synthetic spec, so the counts have a stated cause."""

    SPEC = "\n".join([
        "# Book",
        "",
        "## Write",
        "",
        "### Editing",
        "",
        "The editor MUST save drafts.",
        "It MUST also recover after a crash.",
        "",
        "```cui",
        "<!-- inside a code sample: nothing MUST be parsed here -->",
        "```",
        "",
        "The surface MUST expose:",
        "* a Format control",
        "* an emoji control",
        "",
        "## Other independent apps and shared capabilities",
        "",
        "ADM-01 — Product role",
        "",
        "Admin MUST share the same identity.",
        "",
        "## Cards",
        "",
        "LE-10 — Packaging",
        "",
        "Cards MUST install through the normal catalogue.",
    ])

    def synthetic(self):
        return reg.parse(self.SPEC)

    def test_code_fence_content_is_never_counted_as_a_requirement(self):
        self.assertFalse([r for r in self.synthetic() if "code sample" in r["text"]])

    def test_a_stem_and_its_bullets_are_one_requirement_not_three(self):
        rows = [r for r in self.synthetic() if "expose" in r["text"]]
        self.assertEqual(len(rows), 1, rows)
        self.assertIn("Format control", rows[0]["text"])
        self.assertIn("emoji control", rows[0]["text"])

    def test_each_must_sentence_becomes_exactly_one_row(self):
        rows = [r for r in self.synthetic() if r["level"] == "MUST"]
        self.assertEqual(sorted(r["text"] for r in rows), sorted([
            "The editor MUST save drafts.",
            "It MUST also recover after a crash.",
            # the stem keeps its bullets inside one unit; the markers are dropped when the item is joined
            "The surface MUST expose: a Format control an emoji control",
            "Admin MUST share the same identity.",
            "Cards MUST install through the normal catalogue.",
        ]), [r["text"] for r in rows])

    def test_app_comes_from_the_enclosing_section(self):
        by_text = {r["text"]: r for r in self.synthetic()}
        self.assertEqual(by_text["The editor MUST save drafts."]["app"], "Write")
        self.assertEqual(by_text["It MUST also recover after a crash."]["app"], "Write")

    def test_a_group_id_is_recorded_as_metadata_on_the_requirement(self):
        by_text = {r["text"]: r for r in self.synthetic()}
        self.assertEqual(by_text["Cards MUST install through the normal catalogue."]["group"], "LE-10")

    def test_the_admin_family_is_attributed_because_its_section_names_no_product(self):
        by_text = {r["text"]: r for r in self.synthetic()}
        row = by_text["Admin MUST share the same identity."]
        self.assertEqual(row["app"], "Admin")
        self.assertEqual(row["scope"], "UNRESOLVED")

    def test_a_lifecycle_group_inside_an_app_section_does_not_move_the_requirement_off_that_app(self):
        # Regression pinned: the first version re-attributed by id family alone, which moved Cards' own LE-10
        # lifecycle requirements into a packaging bucket and made Cards look smaller than the specification is.
        by_text = {r["text"]: r for r in self.synthetic()}
        row = by_text["Cards MUST install through the normal catalogue."]
        self.assertEqual(row["app"], "Cards")

    def test_ids_are_stable_across_runs_on_identical_text(self):
        self.assertEqual([r["id"] for r in self.synthetic()], [r["id"] for r in self.synthetic()])

    def test_ids_are_unique(self):
        rows = self.synthetic()
        self.assertEqual(len({r["id"] for r in rows}), len(rows))


class RealSpecTests(unittest.TestCase):
    """Measurements against the actual canonical specification, so the published numbers are checkable."""

    @classmethod
    def setUpClass(cls):
        cls.all_rows, cls.line_counts = reg.measure()
        cls.summary = reg.summarise(cls.all_rows, cls.line_counts)

    def test_the_specification_still_hashes_to_the_pinned_authority(self):
        self.assertEqual(self.line_counts["spec_sha256"], reg.EXPECTED_SHA256)

    def test_no_requirement_is_left_unattributed(self):
        # Measured state: every statement lands in a named bucket, so a mapping gap cannot hide inside an app.
        self.assertEqual(self.summary["unattributed"], [])
        self.assertFalse([r for r in self.all_rows if r["app"] == "UNATTRIBUTED"])

    def test_totals_are_the_sum_of_the_per_app_buckets(self):
        per_app = self.summary["per_app"]
        self.assertEqual(sum(b["MUST"] for b in per_app.values()), self.summary["by_level"]["MUST"])
        self.assertEqual(sum(b["SHOULD"] for b in per_app.values()), self.summary["by_level"]["SHOULD"])
        self.assertEqual(sum(b["MAY"] for b in per_app.values()), self.summary["by_level"]["MAY"])

    def test_scope_classes_partition_the_must_set_exactly(self):
        split = self.summary["must_by_scope"]
        self.assertEqual(sum(split.values()), self.summary["by_level"]["MUST"])
        self.assertEqual(set(split), {"in", "provisional", "out", "UNRESOLVED"})

    def test_the_shipmas_in_scope_denominator_is_measured_not_assumed(self):
        # Pinned at the value measured on 31 October 2026 against the pinned specification digest. If this
        # moves, either the specification changed (the pin should refuse) or the parsing/attribution changed
        # (which must be a deliberate, explained edit).
        self.assertEqual(self.summary["by_level"]["MUST"], 3982)
        self.assertEqual(self.summary["must_by_scope"]["in"], 2659)
        self.assertEqual(self.summary["must_by_scope"]["out"], 392)

    def test_named_apps_own_a_plausible_number_of_requirements(self):
        per_app = self.summary["per_app"]
        for app in ("Haven", "Spaces", "Build", "Boards", "Canvas", "Write", "Present", "Data", "Files",
                    "Connect", "Automations", "AI Studio", "CUI"):
            self.assertGreater(per_app[app]["MUST"], 50, f"{app} looks under-attributed: {per_app[app]}")

    def test_out_of_scope_apps_are_counted_rather_than_deleted(self):
        per_app = self.summary["per_app"]
        self.assertEqual(per_app["Games"]["must_by_scope"], {"out": 100})
        self.assertEqual(per_app["Mini Computer"]["must_by_scope"], {"out": 35})

    def test_every_row_carries_the_evidence_a_reviewer_needs_to_check_it(self):
        for row in self.all_rows[:400]:
            self.assertIsInstance(row["line"], int)
            self.assertGreater(row["line"], 0)
            self.assertTrue(row["section"])
            self.assertTrue(row["text"])
            self.assertIn(row["level"], ("MUST", "SHOULD", "MAY"))
            self.assertIn(row["scope"], ("in", "provisional", "out", "UNRESOLVED"))

    def test_a_row_text_actually_appears_on_or_near_the_line_it_claims(self):
        lines = Path(reg.SPEC).read_text(encoding="utf-8").splitlines()
        for row in self.all_rows[:250]:
            window = " ".join(lines[max(0, row["line"] - 1): row["line"] + 3])
            head = row["text"][:32]
            self.assertIn(head, window.replace("\n", " "), f"line {row['line']} does not back {head!r}")

    def test_spui_family_requirements_stay_with_the_section_that_holds_them(self):
        # Second attribution regression: the first version invented a "cross-app UI (SPUI)" owner and pulled
        # 136 requirements out of Spaces, Dulche and shared-settings.
        apps = {r["app"] for r in self.all_rows if r.get("group", "").startswith("SPUI-")}
        self.assertNotIn("cross-app UI (SPUI)", apps)
        self.assertTrue(apps & {"Spaces", "Dulche", "shared-settings", "Haven"}, apps)

    def test_the_tf_family_is_not_invented_an_app_name(self):
        # The specification keeps the product name OPEN (TF-X05), so the bucket says so instead of guessing.
        self.assertIn("TF (spec names no product)", self.summary["per_app"])


class DriftTests(unittest.TestCase):
    def test_a_specification_that_no_longer_matches_the_pin_is_refused(self):
        original = reg.EXPECTED_SHA256
        try:
            reg.EXPECTED_SHA256 = "0" * 64
            with self.assertRaises(SystemExit) as caught:
                reg.measure()
            self.assertIn("does not match the pinned authority", str(caught.exception))
        finally:
            reg.EXPECTED_SHA256 = original

    def test_the_committed_register_matches_a_fresh_measurement(self):
        path = Path(__file__).resolve().parent / "register.json"
        if not path.is_file():
            self.skipTest("register.json is produced by `build`, not invented here")
        stored = json.loads(path.read_text())
        fresh, line_counts = reg.measure()
        self.assertEqual(len(stored["requirements"]), len(fresh))
        self.assertEqual([r["id"] for r in stored["requirements"]], [r["id"] for r in fresh])
        self.assertEqual(stored["_meta"]["specSha256"], line_counts["spec_sha256"])
        self.assertIn("DENOMINATOR ONLY", stored["_meta"]["meaning"])


if __name__ == "__main__":
    unittest.main()
