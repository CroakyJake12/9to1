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


class SnapshotPinTests(unittest.TestCase):
    """A donor vendored as an extracted snapshot is pinned by digest, per consuming app binding."""

    def setUp(self):
        self.registry = json.loads((Path(gate.ROOT) / "eng" / "donor-sources.json").read_text())

    def test_every_snapshot_binding_carries_its_own_reproducible_pin(self):
        # Without ref+commit the tree cannot be re-extracted; without digest+count a silent in-place
        # edit of donor source would go unnoticed.
        rows = [b for b in self.registry["bindings"] if b.get("scope") == "snapshot"]
        self.assertTrue(rows, "Stage A materialises snapshot donors")
        for row in rows:
            snap = row.get("snapshot") or {}
            with self.subTest(binding=f"{row['app']}/{row['donor']}"):
                self.assertRegex(snap.get("ref", ""), r"^refs/(heads|tags)/")
                self.assertRegex(snap.get("commit", ""), r"^[0-9a-f]{40}$")
                self.assertRegex(snap.get("contentDigest", ""), r"^[0-9a-f]{64}$")
                self.assertGreater(int(snap.get("fileCount", 0)), 0)
                self.assertTrue(snap.get("reason"), "why this is a snapshot rather than a submodule")

    def test_a_donor_shared_by_several_apps_is_pinned_per_binding_not_on_the_upstream(self):
        # One shared block cannot describe three different app trees; an earlier run of the recorder
        # wrote one and the three LibreOffice bindings clobbered each other's digest.
        consumers = [b for b in self.registry["bindings"] if b.get("donor") == "libreoffice"]
        self.assertEqual(len(consumers), 3)
        self.assertNotIn("snapshot", self.registry["upstreams"]["libreoffice"],
                         "the shared upstream entry must not carry a snapshot block")
        digests = {b["snapshot"]["contentDigest"] for b in consumers}
        self.assertEqual(len(digests), 3, "each app's tree has its own digest")

    def test_a_shared_snapshot_block_is_rejected_when_several_apps_use_it(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree = Path(tmp)
            (tree / "a.ui").write_text("<interface/>")
            problems = gate.snapshot_verification_problems(
                {"app": "Write", "donor": "libreoffice"},
                {"snapshot": {"commit": FULL, "contentDigest": "d" * 64, "fileCount": 1,
                              "ref": "refs/heads/master", "fetched": "2026-10-31"}},
                "9to1 Workspace/Write/Source/LibreOffice", tree)
        self.assertTrue(any("shared upstream entry" in x for x in problems), problems)

    def test_donor_trees_left_in_place_are_hashed_and_disagreement_is_reported(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            root = Path(tmp)
            (root / "a.ui").write_text("<interface/>")
            (root / "sub").mkdir()
            (root / "sub" / "b.ui").write_text("<interface/>")
            digest = gate.snapshot_digest(root)
            self.assertRegex(digest, r"^[0-9a-f]{64}$")
            # Re-ordering the traversal must not change the digest, or the pin would break at random.
            self.assertEqual(digest, gate.snapshot_digest(root))
            (root / "a.ui").write_text("<interface changed/>")
            self.assertNotEqual(digest, gate.snapshot_digest(root),
                                "editing donor source in place must break the recorded digest")
            problems = gate.snapshot_verification_problems(
                {"app": "X", "donor": "d", "snapshot": {"commit": FULL, "ref": "refs/heads/main",
                                                        "fetched": "2026-10-31", "contentDigest": "0" * 64,
                                                        "fileCount": 99}},
                {}, str(root), root)
            self.assertTrue(any("content digest disagrees" in x for x in problems), problems)
            self.assertTrue(any("file count disagrees" in x for x in problems), problems)

    def test_a_committed_gitlink_must_match_the_recorded_snapshot_pin(self):
        # If a snapshot is later converted into a submodule, an unchanged pin would silently describe
        # a different tree; the gitlink is compared, not trusted.
        self.assertEqual(gate.snapshot_gitlink_problems("", FULL), [])
        self.assertTrue(gate.snapshot_gitlink_problems("100644 abc 0\tsome/file", FULL))
        self.assertTrue(gate.snapshot_gitlink_problems(f"160000 {OTHER} 0\tpath", FULL))
        self.assertEqual(gate.snapshot_gitlink_problems(f"160000 {FULL} 0\tpath", FULL), [])


class DependencyLockPinTests(unittest.TestCase):
    """A donor consumed as a pinned build (llama.cpp) is checked against its lock, not waved through."""

    def setUp(self):
        self.registry = json.loads((Path(gate.ROOT) / "eng" / "donor-sources.json").read_text())
        self.entry = next(b for b in self.registry["bindings"] if b.get("donor") == "llama-cpp")
        self.record = self.registry["upstreams"]["llama-cpp"]

    def test_the_lock_and_the_registry_name_the_same_revision(self):
        self.assertEqual(gate.dependency_lock_problems(self.entry, self.record, "Dulche", "llama-cpp"), [])

    def test_a_registry_pin_the_build_does_not_use_is_a_failure(self):
        # Measured history: the registry named a newer commit that nothing in this repository builds
        # from, while the lock, the host cache and CI all used an older one.
        record = dict(self.record, commit="0" * 40, forkCommit="0" * 40)
        problems = gate.dependency_lock_problems(self.entry, record, "Dulche", "llama-cpp")
        self.assertTrue(any("dependency lock pins" in x for x in problems), problems)

    def test_a_missing_lock_file_is_a_failure_not_a_pass(self):
        entry = dict(self.entry, binaryLock="does/not/exist.json")
        problems = gate.dependency_lock_problems(entry, self.record, "Dulche", "llama-cpp")
        self.assertTrue(any("does not exist" in x for x in problems), problems)

    def test_no_lock_recorded_at_all_is_a_failure(self):
        entry = {k: v for k, v in self.entry.items() if k != "binaryLock"}
        self.assertTrue(gate.dependency_lock_problems(entry, self.record, "Dulche", "llama-cpp"))

    def test_the_consumed_revision_is_reachable_on_the_controlled_fork(self):
        # Not a gitlink claim: object reachability, recorded when measured, and the gate says so too.
        self.assertEqual(self.record.get("forkCommit"), self.record.get("commit"))
        self.assertTrue(self.record.get("consumedEvidence", {}).get("lockSha256"))
        self.assertIn("NOT RUN", self.record.get("upgradeStatus", ""),
                      "the newer declared revision is an owed upgrade, not an applied one")


class DeferredDonorClassificationTests(unittest.TestCase):
    """Donors the owner scoped out keep their designation and pin, and say why they have no source."""

    def setUp(self):
        self.registry = json.loads((Path(gate.ROOT) / "eng" / "donor-sources.json").read_text())

    def test_deferred_rows_state_status_verdict_reason_and_reference(self):
        rows = [b for b in self.registry["bindings"]
                if str(b.get("status", "")).startswith(("out-of-scope", "provisional", "superseded"))]
        self.assertGreaterEqual(len(rows), 10, "the scoped-out donors are classified, not deleted")
        for row in rows:
            with self.subTest(binding=f"{row['app']}/{row['donor']}"):
                self.assertEqual(gate.aspirational_binding(row), [])
                self.assertTrue(row["reason"].lower().startswith(("owner", "terminal", "planner")),
                                "the reason must name who decided, not just that work is missing")
                self.assertTrue(row.get("requirementKind"),
                                "a spec line and a donor-plan reference are different claims")

    def test_a_deferred_donor_keeps_its_pin_so_a_later_delivery_starts_from_an_exact_revision(self):
        for row in self.registry["bindings"]:
            if str(row.get("status", "")).startswith("out-of-scope"):
                record = self.registry["upstreams"][row["donor"]]
                self.assertRegex(record.get("commit", ""), r"^[0-9a-f]{40}$")


class SourceLockEngineTests(unittest.TestCase):
    """A maintained engine fork vendored under a source lock is checked harder, not waived."""

    ENGINE = "9to1 Models/Dulche Alpha/InferenceEngines/NativeStrata"

    def setUp(self):
        self.registry = json.loads((Path(gate.ROOT) / "eng" / "donor-sources.json").read_text())
        self.record = self.registry["upstreams"]["strata"]
        self.entry = next(b for b in self.registry["bindings"] if b.get("donor") == "strata")

    def _copy_engine(self, tmp, mutate=None):
        import shutil
        tree = Path(tmp) / "engine"
        shutil.copytree(Path(gate.ROOT) / self.ENGINE, tree)
        lock = tree / "strata-source-lock.json"
        data = json.loads(lock.read_text())
        if mutate:
            mutate(data)
        lock.write_text(json.dumps(data))
        doc = tree / "MAINTAINED-FORK.md"
        return tree, doc, lock

    def test_the_real_engine_passes_every_source_lock_check(self):
        problems = gate.vendored_source_lock_problems(self.entry, self.record, self.ENGINE,
                                                      Path(gate.ROOT) / self.ENGINE)
        self.assertEqual(problems, [])

    def test_a_lock_that_disagrees_with_the_registry_pin_is_a_failure(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree, _doc, _lock = self._copy_engine(tmp, lambda d: d.update(commit="f" * 40))
            record = dict(self.record)
            problems = gate.vendored_source_lock_problems({"path": str(tree)}, record, str(tree), tree)
        self.assertTrue(any("source lock commit" in x for x in problems), problems)

    def test_a_manifest_whose_rows_do_not_match_its_own_counts_is_a_failure(self):
        import tempfile
        def break_counts(d):
            d["blobCount"] = d["blobCount"] + 5
            d["sourceBytes"] = d["sourceBytes"] + 1
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree, _doc, _lock = self._copy_engine(tmp, break_counts)
            problems = gate.vendored_source_lock_problems({}, self.record, str(tree), tree)
        self.assertTrue(any("declares" in x and "rows" in x for x in problems), problems)
        self.assertTrue(any("byte total" in x for x in problems), problems)

    def test_a_row_without_a_digest_is_a_failure(self):
        import tempfile
        def strip(d):
            d["rows"][0]["sha256"] = "not-a-digest"
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree, _doc, _lock = self._copy_engine(tmp, strip)
            problems = gate.vendored_source_lock_problems({}, self.record, str(tree), tree)
        self.assertTrue(any("lack a sha256" in x for x in problems), problems)

    def test_a_missing_licence_notice_in_the_engine_tree_is_a_failure(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree, _doc, _lock = self._copy_engine(tmp)
            (tree / "LICENSE.strata").unlink()
            problems = gate.vendored_source_lock_problems({}, self.record, str(tree), tree)
        self.assertTrue(any("licence notice absent" in x for x in problems), problems)

    def test_an_engine_vendored_outside_the_model_tree_is_refused(self):
        import tempfile
        with tempfile.TemporaryDirectory(dir="/tmp/opencode") as tmp:
            tree, _doc, _lock = self._copy_engine(tmp)
            problems = gate.vendored_source_lock_problems({}, self.record, "9to1 Workspace/Boards/Source/Strata",
                                                          tree)
        self.assertTrue(any("must sit in the model/engine tree" in x for x in problems), problems)

    def test_the_lock_binds_upstream_blobs_without_claiming_they_are_all_vendored_here(self):
        # The registry says plainly which half was verified; a claim of a complete upstream checkout would
        # be false, because this repository carries the fork's bridge files, not all 303 upstream blobs.
        evidence = self.record["provenanceEvidence"]
        self.assertGreater(evidence["sourceLockBlobs"], 0)
        vendored = [x for x in (Path(gate.ROOT) / self.ENGINE).rglob("*") if x.is_file()]
        self.assertLess(len(vendored), evidence["sourceLockBlobs"])
