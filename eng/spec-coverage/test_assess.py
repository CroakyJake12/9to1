"""Tests for the requirement-evidence instrument.

These pin what the probe may and may not claim, including the two measured failure modes found while building
it on 31 October 2026:

  * with no frequency guard, it reported Games at 89% evidence-credited and 72% TEST-ASSERTED although no
    Games source exists anywhere in this repository - the recorded roots were the whole workspace;
  * with the strictest guard (every credited tier needing an identifier-like term), it fell to about 10%
    credited across the board and missed implementations that plainly exist (Boards dropped to 2 TEST-ASSERTED
    of 138 even though Haven.Desktop.Tests carries hundreds of tests).

So the tiers are CANDIDATE evidence with a measured error bar, not verdicts, and these tests keep the guards
and the honesty rules in place.
"""
from __future__ import annotations

import json
import subprocess
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import assess


class ProbeTermTests(unittest.TestCase):
    def test_identifier_and_quoted_terms_are_the_distinctive_ones(self):
        terms = assess.probe_terms("The Board grip menu MUST open `BoardGripHost` with a Context Menu.")
        self.assertIn("BoardGripHost", terms)
        # capitalized ordinary words are not identifiers, and treating them as such credited arbitrary files
        self.assertNotIn("Board", terms)
        self.assertNotIn("Context", terms)

    def test_common_specification_words_are_never_probe_terms(self):
        for word in ("user", "app", "surface", "implementation", "permission", "settings"):
            self.assertNotIn(word, assess.probe_terms(
                f"The user app surface must keep the {word} implementation permission settings consistent "
                "without the implementation."))

    def test_a_requirement_with_no_distinctive_vocabulary_reports_that_rather_than_a_guess(self):
        # Empty roots means nothing to search: the row must say so, never invent a tier.
        for text in ("It MUST be simple, easy and pleasant for the typical everyday user.",
                     "The layout MUST feel clean and comfortable throughout the experience."):
            row = {"text": text, "app": "Cards"}
            result = assess.classify_row(row, assess.probe_terms(text), {}, {})
            self.assertIn(result["tier"], ("NO-PROBE-TERMS", "NO-DISTINCTIVE-TERM", "NO-EVIDENCE"), text)
            self.assertIsNone(result["pointer"], text)


class ClassifyTests(unittest.TestCase):
    """Synthetic corpus under eng/spec-coverage/_fixtures: the tiers must behave on known inputs, not on hope.

    Fixtures stay inside this tool directory so a test run can never leave files in a workspace app directory.
    """

    FIXTURES = {
        "eng/spec-coverage/_fixtures/applocal/Surface.cui":
            '<Button id="grip" text="board grip host" grip-overflow="true" />',
        "eng/spec-coverage/_fixtures/shared-src/BoardGripHost.cs":
            "class BoardGripHost { public void OpenGripMenu() { gripOverflow = true; } }",
        "eng/spec-coverage/_fixtures/tests/BoardGripMenuTests.cs":
            "public void BoardGripMenuOpens() { Assert.True(host.OpenGripMenu(gripOverflow)); }",
        "eng/spec-coverage/_fixtures/tests/NoAssertionsHere.cs":
            "public void BoardGripMenuHelper() { var host = new BoardGripHost(gripOverflow); }",
        "eng/spec-coverage/_fixtures/mirror/grip-donormirror.cui":
            '<Button id="grip" text="board grip host" />',
    }

    def setUp(self):
        assess.corpus = assess.Corpus()
        self.owned = {}
        for rel, body in self.FIXTURES.items():
            path = Path(assess.ROOT) / rel
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(body)
            fid = assess.corpus.index(rel)
            if "mirror" in rel:
                self.owned[fid] = "mirror"
            elif "/tests/" in rel:
                self.owned[fid] = "tests"
            elif "/applocal/" in rel:
                self.owned[fid] = "app-local"
            else:
                self.owned[fid] = "shared"

    def tearDown(self):
        for rel in self.FIXTURES:
            (Path(assess.ROOT) / rel).unlink(missing_ok=True)

    def classify(self, text):
        row = {"text": text, "app": "Boards"}
        return assess.classify_row(row, assess.probe_terms(text), self.owned, assess.corpus.postings)

    def test_two_rare_terms_in_first_party_source_are_credited_as_code_present(self):
        result = self.classify("The grip surface MUST expose `BoardGripHost` and `gripOverflow` together.")
        self.assertEqual(result["tier"], "CODE-PRESENT", result)
        self.assertIn("gripOverflow".lower(), [t.lower() for t in result["pointer"]["terms"]])

    def test_a_test_file_with_assertions_reaches_test_asserted(self):
        result = self.classify("BoardGripHost MUST OpenGripMenu, and BoardGripMenuOpens covers `gripOverflow`.")
        self.assertEqual(result["tier"], "TEST-ASSERTED", result)

    def test_a_test_file_without_assertions_cannot_claim_test_asserted(self):
        # NoAssertionsHere.cs carries the same vocabulary but asserts nothing, so it cannot prove a test.
        result = self.classify("BoardGripHost gripOverflow BoardGripMenuHelper `gripOverflow`.")
        self.assertNotEqual(result["tier"], "TEST-ASSERTED", result)

    def test_a_mirror_only_match_never_becomes_code_present(self):
        result = self.classify("The grip uses donor wording `gripOverflow` inside `grip-overflow`.")
        self.assertIn(result["tier"], ("MIRROR-ONLY", "WEAK-SINGLE-TERM"), result)
        if result["tier"] == "MIRROR-ONLY":
            self.assertEqual(result["pointer"]["root"], "mirror")

    def test_a_capitalised_ordinary_word_alone_cannot_credit_a_requirement(self):
        # Measured regression: capitalized sentence words used to count as identifiers, which credited
        # arbitrary files with "Board", "Context" and "Space".
        result = self.classify("Board Context Space Surface Menu Button MUST appear together.")
        self.assertNotEqual(result["tier"], "CODE-PRESENT", result)

    def test_nothing_matching_is_reported_as_no_evidence_and_keeps_its_terms(self):
        result = self.classify("Every Space MUST publish `ZebraFrameCount` and `QuiltLedgerHost`.")
        self.assertEqual(result["tier"], "NO-EVIDENCE")
        self.assertIn("ZebraFrameCount", result["terms"])
        self.assertIsNone(result["pointer"])


class RootHygieneTests(unittest.TestCase):
    """The measured Games/Play failure: no app may be scored against the whole workspace."""

    def setUp(self):
        self.config = json.loads((HERE / "app-roots.json").read_text())

    def test_no_app_is_scored_against_the_entire_workspace(self):
        for app, roots in self.config["apps"].items():
            for _root_class, prefix in roots:
                self.assertNotEqual(prefix.rstrip("/"), "9to1 Workspace",
                                    f"{app} is scored against every other app's source")

    def test_apps_with_no_first_party_source_get_no_roots_rather_than_a_generous_default(self):
        self.assertEqual(self.config["apps"]["Games"], [])
        self.assertEqual(self.config["apps"]["Play"], [])

    def test_every_registered_root_prefix_exists_or_is_recorded_as_expected_absent(self):
        missing = []
        for app, roots in self.config["apps"].items():
            for _root_class, prefix in roots:
                if not (Path(assess.ROOT) / prefix).exists():
                    missing.append(f"{app}: {prefix}")
        self.assertEqual(missing, [], "recorded roots must exist, so an app cannot silently lose its evidence")

    def test_root_classes_are_the_ones_the_instrument_understands(self):
        allowed = {"app-local", "shared", "tests", "framework", "models", "mirror", "owner-review"}
        for app, roots in self.config["apps"].items():
            for root_class, _prefix in roots:
                self.assertIn(root_class, allowed, f"{app} uses an unknown root class {root_class}")

    def test_donor_material_is_excluded_by_the_donor_registry_not_by_a_name_guess(self):
        prefixes = assess.donor_prefixes()
        self.assertGreater(len(prefixes), 30)
        self.assertTrue(any("LibreOffice" in p for p in prefixes))
        self.assertTrue(any("AppFlowy" in p for p in prefixes))
        candidates = assess.candidates()
        for prefix in prefixes:
            self.assertFalse([c for c in candidates if c.startswith(prefix + "/")],
                             f"donor material leaked into the evidence corpus: {prefix}")


class MirrorClassificationTests(unittest.TestCase):
    def test_a_stage_a_mirror_is_classified_as_mirror_even_inside_an_app_directory(self):
        rel = "9to1 Workspace/Boards/CUI/DonorMirror/appflowy/board.cui"
        self.assertTrue(assess.is_mirror(rel))
        self.assertTrue(assess.is_test("9to1 Workspace/shared/tests/Haven.Core.Tests/X.cs"))
        self.assertFalse(assess.is_test("9to1 Workspace/shared/src/Haven.Core/X.cs"))


class AuditAndOverrideTests(unittest.TestCase):
    def test_sample_selection_is_reproducible_for_a_fixed_seed(self):
        payload = {"assessments": [{"id": f"X{i}", "app": "Boards", "tier": "CODE-PRESENT"} for i in range(50)]}
        first = [r["id"] for r in assess.sample_rows(payload, 10, 7, ["Boards"], ("CODE-PRESENT",))]
        second = [r["id"] for r in assess.sample_rows(payload, 10, 7, ["Boards"], ("CODE-PRESENT",))]
        self.assertEqual(first, second)
        self.assertEqual(len(set(first)), 10)

    def test_an_override_without_evidence_is_refused(self):
        target = assess.OVERRIDE_DIR / "ZZTestApp.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            target.write_text(json.dumps({"assessments": [{"id": "A1", "state": "IMPLEMENTED"}]}))
            with self.assertRaises(SystemExit) as caught:
                assess.load_overrides("ZZTestApp")
            self.assertIn("has no evidence", str(caught.exception))
        finally:
            target.unlink(missing_ok=True)

    def test_an_override_with_evidence_and_reason_is_used(self):
        target = assess.OVERRIDE_DIR / "ZZTestApp.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            target.write_text(json.dumps({"assessments": [
                {"id": "A1", "state": "IMPLEMENTED", "evidence": "owner-reported on the running copy",
                 "reason": "seen in the live development instance"}]}))
            overrides = assess.load_overrides("ZZTestApp")
            self.assertEqual(overrides["A1"]["state"], "IMPLEMENTED")
        finally:
            target.unlink(missing_ok=True)

    def test_the_committed_coverage_summary_matches_a_fresh_scoring_run(self):
        summary_path = HERE / "coverage-summary.json"
        if not summary_path.is_file():
            self.skipTest("coverage-summary.json is written by `assess.py score`")
        stored = json.loads(summary_path.read_text())
        payload = assess.score(write_artifacts=False)
        self.assertGreater(payload["_meta"]["files_indexed"], 3000,
                           "a coverage comparison against an empty corpus is not evidence")
        self.assertEqual(payload["summary"], stored["summary"],
                         "the committed coverage summary is stale against the current source")
        self.assertEqual(payload["_meta"]["files_indexed"], stored["_meta"]["files_indexed"])
        self.assertIn("DENOMINATOR ONLY", json.loads((HERE / "register.json").read_text())["_meta"]["meaning"])

    def test_pointer_verification_reports_a_path_that_no_longer_resolves(self):
        payload = {"assessments": [{"id": "A1", "pointer": {"root": "app-local", "path": "no/such/file.cui",
                                                            "terms": ["anything"]}}]}
        out = HERE / "assessment.json"
        existed = out.is_file()
        backup = out.read_text() if existed else None
        try:
            out.write_text(json.dumps(payload))
            problems = assess.verify_pointers()
            self.assertTrue(any("pointer path absent" in p for p in problems), problems)
        finally:
            if existed:
                out.write_text(backup)
            else:
                out.unlink(missing_ok=True)

    def test_pointer_verification_reports_terms_that_are_not_actually_in_the_pointed_file(self):
        marker = HERE / "_pointer_probe.txt"
        marker.write_text("only zebra appears here")
        payload = {"assessments": [{"id": "A1", "pointer": {"root": "app-local",
                                                            "path": marker.relative_to(assess.ROOT).as_posix(),
                                                            "terms": ["zebra", "giraffe"]}}]}
        out = HERE / "assessment.json"
        existed = out.is_file()
        backup = out.read_text() if existed else None
        try:
            out.write_text(json.dumps(payload))
            problems = assess.verify_pointers()
            self.assertTrue(any("giraffe" in p for p in problems), problems)
        finally:
            marker.unlink()
            if existed:
                out.write_text(backup)
            else:
                out.unlink(missing_ok=True)


class ArtifactSafetyTests(unittest.TestCase):
    """A measurement run inside a test must never move the published evidence record."""

    def test_scoring_without_publishing_leaves_both_committed_artifacts_byte_identical(self):
        targets = [HERE / "coverage-summary.json"]
        if (HERE / "assessment.json").is_file():
            targets.append(HERE / "assessment.json")
        if not targets:
            self.skipTest("no committed artifacts yet")
        before = {path: path.read_bytes() for path in targets}
        self.assertTrue(before, "expected at least coverage-summary.json to be committed")
        assess.score(write_artifacts=False)
        for path, blob in before.items():
            self.assertEqual(path.read_bytes(), blob,
                             f"{path.name} was modified by a non-publishing score")

    def test_a_publishing_score_is_deterministic_for_an_unchanged_tree(self):
        # The opposite of what I first wrote here. My first attempt asserted that a publishing score must CHANGE
        # the summary, which would itself be an instrument defect: identical input must produce identical bytes.
        # Measured: two publishing runs on an unchanged tree gave byte-identical records.
        before = (HERE / "coverage-summary.json").read_bytes()
        subprocess.run([sys.executable, "-B", str(HERE / "assess.py"), "score"], check=True,
                       capture_output=True, cwd=assess.ROOT)
        after = (HERE / "coverage-summary.json").read_bytes()
        self.assertEqual(after, before, "the same tree must measure to the same record")

class HandStateTests(unittest.TestCase):
    """A hand state pass must be complete, typed, evidenced, and it must beat the probe."""

    def test_every_boards_must_row_has_an_explicit_hand_state(self):
        register = json.loads((HERE / "register.json").read_text())
        must_ids = {r["id"] for r in register["requirements"] if r["level"] == "MUST" and r["app"] == "Boards"}
        hand = json.loads((HERE / "assessments" / "Boards.json").read_text())
        self.assertEqual({row["id"] for row in hand["assessments"]}, must_ids,
                         "a partial hand pass must not leave rows implicitly inheriting the probe")
        self.assertEqual(hand["_rows"], len(hand["assessments"]))

    def test_hand_states_use_only_the_allowed_vocabulary(self):
        allowed = {"IMPLEMENTED", "PARTIAL", "NOT-IMPLEMENTED", "NOT-ASSESSED"}
        for path in (HERE / "assessments").glob("*.json"):
            for row in json.loads(path.read_text()).get("assessments", []):
                self.assertIn(row["state"], allowed, f"{path.name}:{row.get('id')}")
                self.assertTrue(row.get("reason"), f"{path.name}:{row.get('id')} has no reason")
                self.assertIn(row.get("basis"), ("row", "cluster"), f"{path.name}:{row.get('id')} basis")

    def test_unassessed_rows_are_reported_separately_and_neva_flattered_into_a_percentage(self):
        payload = assess.score(["Boards"], write_artifacts=False)
        row = payload["summary"]["Boards"]
        states = row["hand_states"]
        self.assertEqual(sum(states.values()), row["must_rows"])
        self.assertEqual(row["must_rows"], 138)
        self.assertAlmostEqual(row["state_coverage_pct"],
                               round(100 * (row["must_rows"] - states["NOT-ASSESSED"]) / row["must_rows"], 1))
        self.assertAlmostEqual(row["implemented_pct"], round(100 * states["IMPLEMENTED"] / 138, 1))
        self.assertLessEqual(row["implemented_pct"], row["implemented_or_partial_pct"])

    def test_a_hand_state_replaces_the_probe_tier_for_the_same_row(self):
        payload = assess.score(["Boards"], write_artifacts=False)
        tiers = [r["tier"] for r in payload["assessments"] if r["app"] == "Boards"]
        self.assertFalse(any(t in ("WEAK-SINGLE-TERM", "CODE-PRESENT", "TEST-ASSERTED", "NO-EVIDENCE")
                             for t in tiers),
                         "the probe must not also be counted once a hand state exists")
        self.assertTrue(all(t in ("IMPLEMENTED", "PARTIAL", "NOT-IMPLEMENTED", "NOT-ASSESSED") for t in tiers))
        hand = json.loads((HERE / "assessments" / "Boards.json").read_text())
        counts = {}
        for row in hand["assessments"]:
            counts[row["state"]] = counts.get(row["state"], 0) + 1
        self.assertEqual(counts, {"PARTIAL": 95, "IMPLEMENTED": 15, "NOT-IMPLEMENTED": 22, "NOT-ASSESSED": 6},
                         "the Boards measured state pass changed: re-measure deliberately and say why")

    def test_committed_hand_evidence_all_resolves_or_states_a_measured_absence(self):
        problems = assess.verify_hand_evidence()
        self.assertEqual(problems, [], "hand evidence must name a real path or a stated measured absence")

    def test_the_boards_pass_records_the_measured_absences_it_claims(self):
        hand = json.loads((HERE / "assessments" / "Boards.json").read_text())
        absent = [row for row in hand["assessments"] if row["state"] == "NOT-IMPLEMENTED"]
        self.assertGreaterEqual(len(absent), 20)
        for row in absent:
            # a NOT-IMPLEMENTED claim is allowed two kinds of support: a recorded measured absence, or a named
            # artifact whose own text supports the gap (the Boards manifest saying the canonical schema is not
            # yet established is the clearest example)
            supported = ("measured absence" in row["evidence"] or "absence:" in row["reason"]
                         or bool(assess.PATHISH.search(row["evidence"])))
            self.assertTrue(supported, f"{row['id']} claims NOT-IMPLEMENTED without recorded support")
        self.assertGreaterEqual(sum(1 for r in absent if "measured absence" in r["evidence"]), 10,
                                "at least most gaps must be grep-checkable absences")


class MeasuredNumberTests(unittest.TestCase):
    """Numbers that came from a real run, pinned so a change has to be deliberate."""

    def test_the_registered_denominator_and_scope_split(self):
        register = json.loads((HERE / "register.json").read_text())
        summary = register["summary"]
        self.assertEqual(summary["by_level"]["MUST"], 3982)
        self.assertEqual(summary["must_by_scope"]["in"], 2659)
        self.assertEqual(summary["must_by_scope"]["provisional"], 66)
        self.assertEqual(summary["must_by_scope"]["out"], 392)
        self.assertEqual(summary["must_by_scope"]["UNRESOLVED"], 865)

    def _assessment(self):
        """Score in-process rather than reading the generated file.

        Two earlier suite runs failed for exactly this reason and I could not name the test at the time: the
        measurement tests were reading assessment.json, which was stale against a classification change made
        minutes earlier. The failure was deterministic staleness, not a flake - 13 consecutive clean runs after
        a re-score showed that. Scoring in the test removes the fragility instead of working around it, and the
        corpus-size assertion below stops an empty index from being mistaken for a result.
        """
        payload = assess.score(write_artifacts=False)
        self.assertGreater(payload["_meta"]["files_indexed"], 3000,
                           "assessment against an empty corpus is not evidence")
        return payload

    def test_apps_the_owner_scoped_out_measure_zero_evidence_not_a_percentage_of_nothing(self):
        payload = self._assessment()
        for app in ("Games", "Play"):
            row = payload["summary"][app]
            self.assertEqual(row["CODE-PRESENT"], 0, app)
            self.assertEqual(row["TEST-ASSERTED"], 0, app)
            self.assertEqual(row["WEAK-SINGLE-TERM"], 0, app)
            # with no recorded roots, every row is either unsearchable prose or has no evidence at all
            self.assertEqual(row["NO-EVIDENCE"] + row["NO-PROBE-TERMS"] + row["NO-DISTINCTIVE-TERM"],
                             row["must_rows"], app)

    def test_specific_measured_tier_counts_are_pinned_so_a_flake_names_itself(self):
        # Measured 31 Oct 2026 from a full 7,811-file index. Pinned exactly so that if this ever moves, the
        # failure names the app and the tier instead of an unexplained red run. Any move must be a deliberate
        # change to source or roots, and the number must be re-measured, not edited to make a run pass.
        payload = self._assessment()
        # Re-measured 31 Oct 2026 after two deliberate corpus changes, each with a measured cause: markdown and
        # .json were removed as evidence classes (4 of 12 audited false positives were app gap-manifest docs and
        # my own donor-registry metadata), and snake_case test names began contributing their sub-tokens (the
        # old tokeniser swallowed `Matching_challenge_commits_site_bound_ownership` into one unfindable token).
        # Boards is excluded here because it now has a hand state pass, which replaces the probe tiers for that
        # app entirely; its measured state pin is in HandStateTests.test_a_hand_state_replaces_the_probe_tier...
        expected = {
            "Spaces": {"must_rows": 218, "TEST-ASSERTED": 2, "CODE-PRESENT": 17, "WEAK-SINGLE-TERM": 120,
                       "NO-EVIDENCE": 35},
            "Haven": {"must_rows": 240, "TEST-ASSERTED": 3, "CODE-PRESENT": 10, "WEAK-SINGLE-TERM": 143,
                      "NO-EVIDENCE": 27},
        }
        for app, tiers in expected.items():
            for tier, value in tiers.items():
                self.assertEqual(payload["summary"][app][tier], value, f"{app}/{tier}")

    def test_an_instrument_with_no_indexed_corpus_refuses_instead_of_publishing_zeroes(self):
        # The measured near-miss: a re-score with an empty corpus overwrote the committed summary with
        # NO-EVIDENCE for everything, then compared that against the file it had just corrupted.
        original = assess.candidates
        assess.CANDIDATES = ["nonexistent/path.cui"]
        empty = assess.Corpus()
        saved = assess.corpus
        try:
            assess.corpus = empty
            with self.assertRaises(SystemExit) as caught:
                assess.score(write_artifacts=True)
            self.assertIn("no files were indexed", str(caught.exception))
        finally:
            assess.corpus = saved
            assess.CANDIDATES = []
            assess.candidates = original

    def test_the_assessment_carries_a_row_for_every_must_in_the_register(self):
        payload = self._assessment()
        register = json.loads((HERE / "register.json").read_text())
        must_ids = {r["id"] for r in register["requirements"] if r["level"] == "MUST"}
        self.assertEqual({r["id"] for r in payload["assessments"]}, must_ids)


if __name__ == "__main__":
    unittest.main()
