import copy
import importlib.util
import json
import multiprocessing
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("workflow", Path(__file__).with_name("workflow.py"))
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)


def racing_writer(path, queue):
    try:
        with w.transaction(path, 0) as state:
            state["next_actions"].append(str(os.getpid()))
        queue.put("updated")
    except w.Refusal:
        queue.put("stale")


def racing_initializer(path, value, queue):
    try:
        w.initialize(path, value)
        queue.put("created")
    except w.Refusal:
        queue.put("preserved")


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "source"
        self.repo.mkdir()
        subprocess.run(["git", "init", "-q", str(self.repo)], check=True)
        for name in ("AGENTS.md", "global.json", "NuGet.Config", "docs/specification/9to1-development-specification-v2.md", "src/logic.txt"):
            p = self.repo / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text("initial\n")
        subprocess.run(["git", "-C", str(self.repo), "add", "."], check=True)
        subprocess.run(["git", "-C", str(self.repo), "-c", "user.name=Workflow test", "-c", "user.email=workflow@example.invalid", "commit", "-qm", "fixture"], check=True)
        self.root_checkpoint = self.root / "ROOT-LIVE-existing.json"
        self.root_checkpoint.write_text('{"fixture": "existing programme checkpoint"}\n')
        self.state = w.initial_state(self.repo, str(self.root_checkpoint))
        self.state_path = self.root / "projection.json"
        w.atomic(self.state_path, self.state)
        self.candidate_path = self.root / "candidate.json"
        self.candidate = w.snapshot(self.repo, self.candidate_path)

    def test_stale_writer_does_not_overwrite_checkpoint(self):
        with w.transaction(self.state_path, 0) as state:
            state["next_actions"] = ["new progress"]
        with self.assertRaisesRegex(w.Refusal, "Stale"):
            with w.transaction(self.state_path, 0) as state:
                state["next_actions"] = ["old progress"]
        self.assertEqual(["new progress"], w.read(self.state_path)["next_actions"])

    def test_concurrent_writers_only_one_generation_wins(self):
        queue = multiprocessing.Queue()
        processes = [multiprocessing.Process(target=racing_writer, args=(self.state_path, queue)) for _ in range(2)]
        for process in processes: process.start()
        for process in processes: process.join(10); self.assertEqual(0, process.exitcode)
        self.assertEqual(["stale", "updated"], sorted(queue.get(timeout=2) for _ in processes))
        self.assertEqual(1, w.read(self.state_path)["generation"])

    def test_concurrent_initialisers_publish_one_complete_checkpoint(self):
        path = self.root / "new-state.json"
        queue = multiprocessing.Queue()
        processes = [multiprocessing.Process(target=racing_initializer, args=(path, self.state, queue)) for _ in range(2)]
        for process in processes: process.start()
        for process in processes: process.join(10); self.assertEqual(0, process.exitcode)
        self.assertEqual(["created", "preserved"], sorted(queue.get(timeout=2) for _ in processes))
        self.assertEqual(self.state, w.read(path))

    def test_failed_transaction_leaves_original_intact(self):
        before = self.state_path.read_bytes()
        with self.assertRaises(RuntimeError):
            with w.transaction(self.state_path, 0) as state:
                state["next_actions"] = ["must not leak"]
                raise RuntimeError("interrupted")
        self.assertEqual(before, self.state_path.read_bytes())

    def recovery_update(self):
        path = self.root / "current-checkpoint.json"
        path.write_text('{"next_action":"resume existing programme"}\n')
        asset = {"path": str(path), **w.file_identity(path)}
        return {"last_root_checkpoint_observed": {"path": str(path), "sha256": asset["sha256"]},
                "recovery_assets": {"latest-root-checkpoint": asset}}

    def assert_checkpoint_refused_unchanged(self, patch):
        before = self.state_path.read_bytes()
        memory = copy.deepcopy(self.state)
        with self.assertRaises(w.Refusal): w.apply_checkpoint(memory, patch)
        self.assertEqual(self.state, memory)
        with self.assertRaises(w.Refusal):
            with w.transaction(self.state_path, 0) as state: w.apply_checkpoint(state, patch)
        self.assertEqual(before, self.state_path.read_bytes())
        self.assertEqual(0, w.read(self.state_path)["generation"])

    def test_checkpoint_rejects_writer24_string_locator_and_pathless_assets(self):
        patch = self.recovery_update()
        patch["last_root_checkpoint_observed"] = patch["last_root_checkpoint_observed"]["path"]
        del patch["recovery_assets"]["latest-root-checkpoint"]["path"]
        patch["next_actions"] = ["must not leak from refused checkpoint"]
        self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_rejects_writer25_repaired_asset_but_string_locator(self):
        patch = self.recovery_update()
        patch["last_root_checkpoint_observed"] = patch["last_root_checkpoint_observed"]["path"]
        self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_rejects_missing_asset_identity_fields_before_mutation(self):
        original = self.recovery_update()
        for field in ("path", "kind", "bytes", "mode", "sha256"):
            with self.subTest(field=field):
                patch = copy.deepcopy(original)
                del patch["recovery_assets"]["latest-root-checkpoint"][field]
                self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_rejects_malformed_asset_identity_fields(self):
        original = self.recovery_update()
        mutations = [("path", "relative.json"), ("path", None), ("path", "/invalid\0path"), ("kind", "symlink"),
                     ("bytes", True), ("bytes", -1), ("mode", True), ("mode", 0o1000),
                     ("sha256", "invalid"), ("sha256", None)]
        for field, value in mutations:
            with self.subTest(field=field, value=value):
                patch = copy.deepcopy(original)
                patch["recovery_assets"]["latest-root-checkpoint"][field] = value
                self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_rejects_null_or_nonmapping_recovery_records(self):
        for patch in ({"recovery_assets": None}, {"recovery_assets": []},
                      {"recovery_assets": {"new": None}}, {"last_root_checkpoint_observed": None},
                      {"last_root_checkpoint_observed": []}):
            with self.subTest(patch=patch): self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_rejects_missing_or_changed_new_asset_bytes(self):
        patch = self.recovery_update()
        path = Path(patch["last_root_checkpoint_observed"]["path"])
        original = path.read_bytes()
        for mutation in ("missing", "tampered", "mode", "declared-size"):
            with self.subTest(mutation=mutation):
                path.write_bytes(original); path.chmod(0o600)
                update = self.recovery_update()
                if mutation == "missing": path.unlink()
                elif mutation == "tampered": path.write_bytes(b"tampered")
                elif mutation == "mode": path.chmod(0o644)
                else: update["recovery_assets"]["latest-root-checkpoint"]["bytes"] += 1
                self.assert_checkpoint_refused_unchanged(update)

    def test_checkpoint_rejects_nonregular_new_asset_without_fifo_hang(self):
        for kind in ("symlink", "directory", "fifo"):
            with self.subTest(kind=kind):
                patch = self.recovery_update(); path = Path(patch["last_root_checkpoint_observed"]["path"])
                path.unlink()
                if kind == "symlink": path.symlink_to(self.root_checkpoint)
                elif kind == "directory": path.mkdir()
                else: os.mkfifo(path)
                self.assert_checkpoint_refused_unchanged(patch)
                if kind == "directory": path.rmdir()
                else: path.unlink()

    def test_checkpoint_newest_pair_must_be_complete_and_match(self):
        original = self.recovery_update()
        variants = [copy.deepcopy(original) for _ in range(5)]
        del variants[0]["recovery_assets"]
        del variants[1]["last_root_checkpoint_observed"]
        del variants[2]["last_root_checkpoint_observed"]["path"]
        variants[3]["last_root_checkpoint_observed"]["path"] = str(self.root_checkpoint)
        variants[4]["last_root_checkpoint_observed"]["sha256"] = "0" * 64
        for patch in variants:
            with self.subTest(patch=patch): self.assert_checkpoint_refused_unchanged(patch)

    def test_checkpoint_current_pair_changes_need_one_atomic_update(self):
        old = self.recovery_update()
        with w.transaction(self.state_path, 0) as state: w.apply_checkpoint(state, old)
        current = w.read(self.state_path)
        newer = self.root / "next-checkpoint.json"; newer.write_text('{"newer":true}\n')
        new_asset = {"path": str(newer), **w.file_identity(newer)}
        for patch in ({"last_root_checkpoint_observed": {"path": str(newer), "sha256": new_asset["sha256"]}},
                      {"recovery_assets": {"latest-root-checkpoint": new_asset}}):
            with self.subTest(patch=patch):
                before = self.state_path.read_bytes()
                with self.assertRaises(w.Refusal):
                    with w.transaction(self.state_path, 1) as state: w.apply_checkpoint(state, patch)
                self.assertEqual(before, self.state_path.read_bytes())
        with w.transaction(self.state_path, 1) as state:
            w.apply_checkpoint(state, {"last_root_checkpoint_observed": {"path": str(newer), "sha256": new_asset["sha256"]},
                                       "recovery_assets": {"latest-root-checkpoint": new_asset}})
        result = w.recovery(w.read(self.state_path), self.repo)
        self.assertEqual("CHECKPOINT", result["recovery_status"])
        self.assertEqual(str(newer), result["read_first"][-1])
        self.assertEqual(2, w.read(self.state_path)["generation"])
        self.assertEqual(current["tasks"], w.read(self.state_path)["tasks"])

    def test_checkpoint_can_repair_previously_persisted_malformed_recovery_pair(self):
        patch = self.recovery_update()
        self.state["last_root_checkpoint_observed"] = patch["last_root_checkpoint_observed"]["path"]
        self.state["recovery_assets"]["latest-root-checkpoint"] = {k: v for k, v in patch["recovery_assets"]["latest-root-checkpoint"].items() if k != "path"}
        w.atomic(self.state_path, self.state)
        with w.transaction(self.state_path, 0) as state: w.apply_checkpoint(state, patch)
        result = w.recovery(w.read(self.state_path), self.repo)
        self.assertEqual("CHECKPOINT", result["recovery_status"])
        self.assertEqual(patch["last_root_checkpoint_observed"]["path"], result["read_first"][-1])

    def test_checkpoint_task_only_update_does_not_rehash_missing_historical_assets(self):
        self.root_checkpoint.unlink()
        with w.transaction(self.state_path, 0) as state:
            w.apply_checkpoint(state, {"tasks": {"repair": {"status": "WORKING"}}, "next_actions": ["restore retained checkpoint"]})
        current = w.read(self.state_path)
        self.assertEqual(1, current["generation"])
        self.assertEqual("WORKING", current["tasks"]["repair"]["status"])
        self.assertEqual("BLOCKED", w.recovery(current, self.repo)["recovery_status"])

    def test_checkpoint_unrelated_new_asset_does_not_rehash_historical_assets(self):
        self.root_checkpoint.unlink()
        path = self.root / "new-source.zip"; path.write_bytes(b"retained actual source fixture")
        with w.transaction(self.state_path, 0) as state:
            w.apply_checkpoint(state, {"recovery_assets": {"source": {"path": str(path), **w.file_identity(path)}}})
        self.assertEqual(1, w.read(self.state_path)["generation"])

    def test_checkpoint_observation_of_existing_asset_checks_its_current_bytes(self):
        self.observe_new_root_checkpoint()
        w.atomic(self.state_path, self.state)
        locator = self.state["last_root_checkpoint_observed"]
        Path(locator["path"]).write_bytes(b"changed after prior acceptance")
        self.assert_checkpoint_refused_unchanged({"last_root_checkpoint_observed": locator})

    def test_scope_claim_rejects_parent_child_collision(self):
        w.claim(self.state, "framework", ["framework/CUI/**"], "implementation")
        with self.assertRaisesRegex(w.Refusal, "framework"):
            w.claim(self.state, "other", ["framework/CUI/runtime/Parser.cs"], "implementation")

    def test_scope_claim_allows_unrelated_work(self):
        w.claim(self.state, "framework", ["framework/CUI/**"], "implementation")
        w.claim(self.state, "arm", [".github/workflows/havenos-linux-package.yml"], "CI")
        self.assertEqual(2, len(self.state["workers"]))

    def test_scope_prefix_is_not_a_false_collision(self):
        self.assertFalse(w.scope_overlap("apps/Build/**", "apps/Builder/Editor.cs"))

    def test_released_claim_no_longer_blocks_new_owner(self):
        w.claim(self.state, "old", ["apps/Build/**"], "implementation")
        self.state["workers"]["old"]["status"] = "CHECKPOINT"
        w.claim(self.state, "new", ["apps/Build/Editor.cs"], "implementation")

    def test_path_escape_and_unsupported_glob_are_rejected(self):
        for path in ("../source/**", "/etc/**", "apps/*/Editor.cs", "apps/**/x/**"):
            with self.subTest(path=path), self.assertRaises(w.Refusal):
                w.scope_overlap(path, "apps/Build/Editor.cs")

    def test_immutable_snapshot_cannot_be_replaced(self):
        (self.repo / "src/logic.txt").write_text("later\n")
        with self.assertRaisesRegex(w.Refusal, "Immutable"):
            w.snapshot(self.repo, self.candidate_path)

    def test_snapshot_repetition_is_idempotent(self):
        self.assertEqual(self.candidate, w.snapshot(self.repo, self.candidate_path))

    def test_snapshot_includes_initialized_donor_contents_and_pin(self):
        donor = self.root / "donor-source"
        donor.mkdir()
        subprocess.run(["git", "init", "-q", str(donor)], check=True)
        (donor / "Engine.cs").write_text("pinned donor fixture")
        subprocess.run(["git", "-C", str(donor), "add", "."], check=True)
        subprocess.run(["git", "-C", str(donor), "-c", "user.name=Workflow test", "-c", "user.email=workflow@example.invalid", "commit", "-qm", "fixture"], check=True)
        subprocess.run(["git", "-C", str(self.repo), "-c", "protocol.file.allow=always", "submodule", "add", "-q", str(donor), "vendor/Donor"], check=True)
        candidate = w.snapshot(self.repo, self.root / "with-donor.json")
        self.assertEqual("gitlink", candidate["source"]["vendor/Donor"]["kind"])
        self.assertIn("vendor/Donor/Engine.cs", candidate["source"])
        (self.repo / "vendor/Donor/Engine.cs").write_text("different cold source")
        with self.assertRaises(w.Refusal): w.validate_candidate(candidate, self.repo)

    def test_snapshot_preserves_intentional_tracked_deletion(self):
        (self.repo / "src/logic.txt").unlink()
        candidate = w.snapshot(self.repo, self.root / "deletion.json")
        self.assertEqual({"kind": "deleted"}, candidate["source"]["src/logic.txt"])

    def test_unresolved_index_conflict_blocks_integration_snapshot(self):
        oid = w.git(self.repo, "rev-parse", "HEAD:src/logic.txt").decode().strip()
        entries = "0 " + "0" * 40 + "\tsrc/logic.txt\n" + "".join("100644 " + oid + " " + str(stage) + "\tsrc/logic.txt\n" for stage in (1, 2, 3))
        subprocess.run(["git", "-C", str(self.repo), "update-index", "--index-info"], input=entries.encode(), check=True)
        with self.assertRaisesRegex(w.Refusal, "Unresolved integration conflict"):
            w.snapshot(self.repo, self.root / "conflicted.json")

    def test_same_size_and_mtime_changes_still_invalidate_candidate(self):
        source = self.repo / "src/logic.txt"
        stat = source.stat()
        source.write_text("changed\n")
        os.utime(source, ns=(stat.st_atime_ns, stat.st_mtime_ns))
        with self.assertRaisesRegex(w.Refusal, "changed"):
            w.validate_candidate(self.candidate, self.repo)

    def test_new_untracked_input_invalidates_candidate(self):
        (self.repo / "src/new.txt").write_text("untracked code\n")
        with self.assertRaises(w.Refusal): w.validate_candidate(self.candidate, self.repo)

    def test_changed_manifest_is_rejected_even_without_repo(self):
        bad = copy.deepcopy(self.candidate)
        bad["source"]["src/logic.txt"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(w.Refusal, "identity"):
            w.validate_candidate(bad)

    def test_accepted_fix_requires_actual_unchanged_content(self):
        fix = {"id": "close-crash", "reviewer": "independent", "evidence": "close-receipt.json", "files": {"src/logic.txt": w.file_identity(self.repo / "src/logic.txt")}}
        good = w.snapshot(self.repo, self.root / "accepted.json", [fix])
        (self.repo / "src/logic.txt").write_text("regressed\n")
        with self.assertRaisesRegex(w.Refusal, "content changed"):
            w.snapshot(self.repo, self.root / "successor.json", [fix], good["accepted_fixes"])

    def test_accepted_fix_id_cannot_silently_disappear(self):
        with self.assertRaisesRegex(w.Refusal, "missing"):
            w.snapshot(self.repo, self.root / "successor.json", [], {"close-crash": {}})

    def test_accepted_fix_changes_need_reviewed_predecessor(self):
        fix = {"id": "close-crash", "reviewer": "reviewer", "evidence": "first", "files": {"src/logic.txt": w.file_identity(self.repo / "src/logic.txt")}}
        changed = {**fix, "evidence": "second"}
        with self.assertRaisesRegex(w.Refusal, "predecessor"):
            w.snapshot(self.repo, self.root / "successor.json", [changed], {fix["id"]: fix})
        changed["replaces"] = w.digest(w.encoded(fix))
        self.assertIn(fix["id"], w.snapshot(self.repo, self.root / "successor.json", [changed], {fix["id"]: fix})["accepted_fixes"])

    def test_rc_remains_pinned_when_development_changes(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        pinned = copy.deepcopy(self.state["release_candidate"])
        self.state["development_candidates"]["product"] = {"revision": "later"}
        self.assertEqual(pinned, self.state["release_candidate"])
        with self.assertRaisesRegex(w.Refusal, "already pinned"):
            w.pin_rc(self.state, self.candidate, self.candidate_path)

    def test_dirty_development_checkpoint_cannot_be_pinned_for_release(self):
        (self.repo / "src/logic.txt").write_text("uncommitted work\n")
        path = self.root / "dirty-checkpoint.json"
        candidate = w.snapshot(self.repo, path)
        self.assertFalse(candidate["worktree_clean"])
        with self.assertRaisesRegex(w.Refusal, "committed clean source"):
            w.pin_rc(self.state, candidate, path)

    def test_reviewed_rc_fix_retains_predecessor_and_requires_backport(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        (self.repo / "src/logic.txt").write_text("fixed\n")
        subprocess.run(["git", "-C", str(self.repo), "-c", "user.name=Workflow test", "-c", "user.email=workflow@example.invalid", "commit", "-qam", "reviewed RC fix fixture"], check=True)
        successor_path = self.root / "successor.json"
        successor = w.snapshot(self.repo, successor_path)
        patch = {"id": "release-fix", "reviewer": "QA", "implementer": "worker", "reason": "actual crash", "files": ["src/logic.txt"],
                 "source_candidate": self.candidate["identity"], "target_candidate": successor["identity"], "backport_targets": ["development"]}
        w.advance_rc(self.state, successor, successor_path, patch)
        self.assertEqual(self.candidate["identity"], self.state["release_history"][0]["identity"])
        self.assertIn("backport:release-fix", w.release_ready(self.state)["missing"])
        (self.repo / "src/logic.txt").write_text("stale development\n")
        with self.assertRaisesRegex(w.Refusal, "differs"):
            w.verify_backport(self.state, "release-fix", "development", self.repo)
        (self.repo / "src/logic.txt").write_text("fixed\n")
        w.verify_backport(self.state, "release-fix", "development", self.repo)
        self.assertEqual("VERIFIED", self.state["pending_changes"]["release-fix"]["status"])
        self.assertNotIn("backport:release-fix", w.release_ready(self.state)["missing"])
        (self.repo / "src/logic.txt").write_text("accepted fix overwritten later\n")
        self.assertIn("backport:release-fix", w.release_ready(self.state)["missing"])

    def test_generic_checkpoint_cannot_forge_verified_rc_backport(self):
        self.state["pending_changes"]["release-fix"] = {"kind": "rc-backport", "status": "CHECKPOINT", "verified_targets": {}}
        with self.assertRaisesRegex(w.Refusal, "never a generic checkpoint"):
            w.apply_checkpoint(self.state, {"pending_changes": {"release-fix": {"kind": "rc-backport", "status": "VERIFIED", "verified_targets": {}}}})
        self.assertEqual("CHECKPOINT", self.state["pending_changes"]["release-fix"]["status"])
        w.apply_checkpoint(self.state, {"pending_changes": {"unrelated-product": {"status": "WORKING"}}})
        self.assertIn("unrelated-product", self.state["pending_changes"])

    def test_rc_fix_cannot_include_undeclared_unrelated_changes(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        (self.repo / "src/logic.txt").write_text("fixed\n")
        (self.repo / "AGENTS.md").write_text("unrelated\n")
        successor_path = self.root / "successor.json"
        successor = w.snapshot(self.repo, successor_path)
        patch = {"id": "release-fix", "reviewer": "QA", "implementer": "worker", "reason": "actual crash", "files": ["src/logic.txt"],
                 "source_candidate": self.candidate["identity"], "target_candidate": successor["identity"], "backport_targets": ["development"]}
        with self.assertRaisesRegex(w.Refusal, "scope differs"):
            w.advance_rc(self.state, successor, successor_path, patch)
        self.assertEqual(self.candidate["identity"], self.state["release_candidate"]["identity"])

    def test_recovery_detects_replaced_rc_manifest(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        changed = copy.deepcopy(self.candidate)
        changed["revision"] = "tampered provenance"
        self.candidate_path.write_bytes(w.encoded(changed))
        self.assertTrue(w.recovery(self.state, self.repo)["discrepancies"])
        self.assertFalse(w.release_ready(self.state)["ready"])

    def test_build_success_never_satisfies_release_gates(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        self.state["evidence"]["green-build"] = {"result": "PASS", "layer": 1, "candidate": self.candidate["identity"]}
        self.assertFalse(w.release_ready(self.state)["ready"])
        self.assertEqual(set(w.MANDATORY), set(w.release_ready(self.state)["missing"]))

    def test_release_rejects_same_reviewer_or_missing_real_artifact(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        for gate in w.MANDATORY:
            self.state["evidence"][gate] = {"gate": gate, "candidate": self.candidate["identity"], "result": "PASS", "layer": 4,
                                            "reviewer": "same", "implementer": "same", "platform": "test", "steps": ["claimed"]}
        self.assertFalse(w.release_ready(self.state)["ready"])

    def test_release_requires_exact_candidate_and_retained_independent_evidence(self):
        w.pin_rc(self.state, self.candidate, self.candidate_path)
        artifact = self.root / "native-evidence.txt"
        artifact.write_text("fixture exercising validation ONLY, not product acceptance")
        for gate in w.MANDATORY:
            self.state["evidence"][gate] = {"gate": gate, "candidate": self.candidate["identity"], "result": "PASS", "layer": 4,
                                            "reviewer": "reviewer", "implementer": "implementer", "platform": "test fixture", "steps": ["validator fixture"],
                                            "quality": list(w.QUALITY), "artifact": {"path": str(artifact), **w.file_identity(artifact)}}
        self.assertTrue(w.release_ready(self.state)["ready"])
        self.state["evidence"]["cui-conformance"]["candidate"] = "obsolete"
        self.assertFalse(w.release_ready(self.state)["ready"])
        self.state["evidence"]["cui-conformance"]["candidate"] = self.candidate["identity"]
        artifact.write_text("changed evidence")
        self.assertFalse(w.release_ready(self.state)["ready"])

    def test_zero_test_green_trx_does_not_pass(self):
        trx = self.root / "empty.trx"
        trx.write_text('<TestRun><ResultSummary><Counters total="0" executed="0" passed="0" failed="0"/></ResultSummary></TestRun>')
        self.assertEqual("FAIL", w.trx(trx)["result"])

    def test_green_counters_without_actual_test_results_do_not_pass(self):
        trx = self.root / "green-summary.trx"
        trx.write_text('<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary></TestRun>')
        self.assertEqual("FAIL", w.trx(trx)["result"])

    def test_skipped_or_failed_tests_do_not_pass(self):
        for counts in ('total="2" executed="1" passed="1" failed="0"', 'total="2" executed="2" passed="1" failed="1"'):
            trx = self.root / "bad.trx"
            trx.write_text('<TestRun><ResultSummary><Counters ' + counts + '/></ResultSummary></TestRun>')
            self.assertEqual("FAIL", w.trx(trx)["result"])

    def test_test_exit_zero_without_execution_evidence_is_unverified(self):
        receipt = w.run_check(self.repo, self.candidate, "no-proof", 1, [sys.executable, "-c", "pass"], self.root / "run")
        self.assertEqual("UNVERIFIED", receipt["result"])
        self.assertTrue(Path(receipt["log"]["path"]).is_file())

    def test_failure_log_is_retained_with_measured_duration(self):
        receipt = w.run_check(self.repo, self.candidate, "failing", 1, [sys.executable, "-c", "print('real failure');raise SystemExit(3)"], self.root / "run")
        self.assertEqual(3, receipt["exit_code"])
        self.assertEqual("FAIL", receipt["result"])
        self.assertIn("real failure", Path(receipt["log"]["path"]).read_text())
        self.assertGreater(receipt["duration_seconds"], 0)
        self.assertEqual("UNTRIAGED", receipt["classification"])

    def test_missing_executable_is_infrastructure_blocker(self):
        receipt = w.run_check(self.repo, self.candidate, "missing", 1, [str(self.root / "absent")], self.root / "run")
        self.assertEqual(("BLOCKED", "INFRASTRUCTURE"), (receipt["result"], receipt["classification"]))

    def test_actual_process_cwd_and_pwd_agree_after_wrong_inherited_pwd(self):
        command = [sys.executable, "-c", "import os; print(os.getcwd()); print(os.environ['PWD'])"]
        with patch.dict(os.environ, {"PWD": "/wrong/inherited/root"}):
            receipt = w.run_check(self.repo, self.candidate, "working-directory", 1, command, self.root / "run")
        self.assertEqual([str(self.repo), str(self.repo)], Path(receipt["log"]["path"]).read_text().splitlines())

    def test_bounded_execution_stops_only_its_actual_owned_process_group(self):
        command = [sys.executable, "-c", "import subprocess,sys,time; subprocess.Popen([sys.executable,'-c','import time;time.sleep(60)']); print('started',flush=True);time.sleep(60)"]
        receipt = w.run_check(self.repo, self.candidate, "timeout", 1, command, self.root / "run", timeout_seconds=0.3)
        self.assertTrue(receipt["timed_out"])
        self.assertEqual("FAIL", receipt["result"])
        identity = receipt["process"]
        self.assertEqual(identity["pid"], identity["pgid"])
        self.assertEqual(identity["pid"], identity["session"])
        self.assertEqual([], receipt["remaining_group"])
        current = w.process_identity(identity["pid"])
        self.assertTrue(current is None or current["state"] == "Z")

    def test_stale_trx_is_never_reused_as_new_run(self):
        old = self.root / "old.trx"
        old.write_text("old")
        with self.assertRaisesRegex(w.Refusal, "already exists"):
            w.run_check(self.repo, self.candidate, "stale", 1, [sys.executable, "-c", "pass"], self.root / "run", str(old))
        self.assertEqual("old", old.read_text())

    def test_malformed_trx_retains_blocked_receipt_not_process_success(self):
        trx = self.root / "malformed.trx"
        command = [sys.executable, "-c", "from pathlib import Path;Path(" + repr(str(trx)) + ").write_text('<broken')"]
        receipt = w.run_check(self.repo, self.candidate, "malformed-evidence", 1, command, self.root / "run", str(trx))
        self.assertEqual(0, receipt["exit_code"])
        self.assertEqual(("BLOCKED", "INFRASTRUCTURE"), (receipt["result"], receipt["classification"]))
        self.assertEqual("BLOCKED", w.read(self.root / "run/receipt.json")["result"])

    def test_reuse_requires_exact_toolchain_and_evidence_inputs(self):
        trx = self.root / "real.trx"
        code = "from pathlib import Path;Path(" + repr(str(trx)) + ").write_text(" + repr('<TestRun><Results><UnitTestResult outcome="Passed" testName="validator-fixture"/></Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary></TestRun>') + ")"
        tool = self.root / "toolchain-pin"
        tool.write_text("pinned-runtime")
        environment = self.root / "environment.json"
        w.atomic(environment, {"reviewer": "independent", "files": {str(tool): w.file_identity(tool)}})
        command = [sys.executable, "-c", code]
        receipt = w.run_check(self.repo, self.candidate, "fixture-counter-validation", 1, command, self.root / "run", str(trx), environment=str(environment))
        self.assertTrue(w.reusable(receipt, self.candidate, command, receipt["platform"]))
        self.assertFalse(w.reusable(receipt, self.candidate, command + ["changed"], receipt["platform"]))
        tool.write_text("different-runtime")
        self.assertFalse(w.reusable(receipt, self.candidate, command, receipt["platform"]))

    def test_reuse_without_reviewed_environment_is_refused(self):
        self.assertFalse(w.reusable({"candidate": self.candidate["identity"], "command": [], "platform": {}, "result": "PASS"}, self.candidate, [], {}))

    def test_unknown_dependency_selects_all_registered_checks(self):
        registry = {"checks": {"home": {"paths": ["Home/**"]}, "spaces": {"paths": ["Spaces/**"]}}}
        result = w.plan(["shared/unknown.props"], registry)
        self.assertEqual({"home", "spaces"}, set(result["checks"]))
        self.assertEqual(["shared/unknown.props"], result["unknown_paths"])

    def test_dependency_selection_is_transitive(self):
        registry = {"checks": {"spaces": {"paths": ["Spaces/**"], "depends_on": ["home"]}, "home": {"paths": ["Home/**"], "depends_on": ["cui"]}, "cui": {"paths": ["framework/**"]}}}
        self.assertEqual({"spaces", "home", "cui"}, set(w.plan(["Spaces/Chat.cs"], registry)["checks"]))

    def test_release_plan_never_waives_mandatory_gates(self):
        registry = {"checks": {"home": {"paths": ["Home/**"]}, "spaces": {"paths": ["Spaces/**"]}}}
        result = w.plan([], registry, release=True)
        self.assertEqual(set(w.MANDATORY), set(result["mandatory_release_gates"]))
        self.assertEqual(2, len(result["checks"]))

    def test_invalid_status_or_unproven_verified_revision_refused(self):
        self.state["verified_revision"] = self.candidate["revision"]
        with self.assertRaises(w.Refusal): w.validate_state(self.state)
        self.state["verified_revision"] = None
        self.state["tasks"]["x"] = {"status": "GREEN"}
        with self.assertRaises(w.Refusal): w.validate_state(self.state)

    def test_verified_status_requires_exact_revision_and_retained_independent_artifact(self):
        artifact = self.root / "independent-review.json"
        artifact.write_text('{"fixture_only":true}')
        source = "f" * 40
        self.state["tasks"]["scope"] = {"status": "VERIFIED", "source_revision": source, "verified_revision": source, "evidence": ["review"]}
        self.state["evidence"]["review"] = {"result": "PASS", "source_revision": source, "reviewer": "reviewer", "implementer": "worker", "steps": ["evidence validator fixture"],
                                               "artifact": {"path": str(artifact), **w.file_identity(artifact)}}
        self.state["verified_revision"] = source
        w.validate_state(self.state)  # positive control: structural validator fixture, not real app acceptance
        self.state["evidence"]["review"]["source_revision"] = "e" * 40
        with self.assertRaisesRegex(w.Refusal, "different source"):
            w.validate_state(self.state)
        self.state["evidence"]["review"]["source_revision"] = source
        artifact.unlink()
        with self.assertRaisesRegex(w.Refusal, "artifact unavailable"):
            w.validate_state(self.state)

    def test_recovery_retains_ownership_and_flags_unverified_liveness(self):
        w.claim(self.state, "worker", ["src/**"], "product")
        self.state["blockers"]["native"] = {"status": "BLOCKED", "reason": "no endpoint"}
        self.state["next_actions"] = ["verify existing endpoint"]
        result = w.recovery(self.state, self.repo)
        self.assertEqual(["src/**"], result["workers"]["worker"]["paths"])
        self.assertIn("NOT CHECKED", result["liveness"])
        self.assertFalse(result["release_readiness"]["ready"])
        self.assertEqual(self.state["next_actions"], result["next_actions"])

    def observe_new_root_checkpoint(self):
        # Deliberately not named/timed "latest": only the recorded coordinator selection counts.
        latest = self.root / "ROOT-LIVE-earlier-label.json"
        latest.write_text('{"next_action":"resume existing task, not the old checkpoint"}\n')
        asset = {"path": str(latest), **w.file_identity(latest)}
        self.state["last_root_checkpoint_observed"] = {"path": str(latest), "sha256": asset["sha256"]}
        self.state["recovery_assets"]["latest-root-checkpoint"] = asset
        return latest

    def recover_in_fresh_process(self):
        w.atomic(self.state_path, self.state)
        before = self.state_path.read_bytes()
        result = subprocess.run([sys.executable, "-B", str(Path(w.__file__).resolve()),
                                 "--state", str(self.state_path), "recover", "--repo", str(self.repo)],
                                capture_output=True, text=True, timeout=10)
        self.assertEqual(before, self.state_path.read_bytes())
        self.assertEqual("", result.stderr)
        return result, json.loads(result.stdout)

    def test_recovery_legacy_state_uses_retained_initial_checkpoint(self):
        result = w.recovery(self.state, self.repo)
        self.assertEqual("CHECKPOINT", result["recovery_status"])
        self.assertEqual(str(self.root_checkpoint), result["read_first"][-1])
        self.assertEqual(str(self.root_checkpoint), result["projection_origin"])
        self.assertIn("legacy", result["root_checkpoint"]["selection"])

    def test_fresh_recovery_prioritises_recorded_new_root_not_origin_or_filename(self):
        latest = self.observe_new_root_checkpoint()
        result, recovered = self.recover_in_fresh_process()
        self.assertEqual(0, result.returncode)
        self.assertEqual(str(latest), recovered["read_first"][-1])
        self.assertNotIn(str(self.root_checkpoint), recovered["read_first"])
        self.assertEqual(str(self.root_checkpoint), recovered["projection_origin"])
        self.assertEqual("last_root_checkpoint_observed", recovered["root_checkpoint"]["selection"])
        self.assertEqual(self.state["generation"], recovered["root_checkpoint"]["recorded_in_generation"])

    def test_recovery_requires_newest_locator_and_asset_together(self):
        self.observe_new_root_checkpoint()
        saved = copy.deepcopy(self.state)
        for missing in ("locator", "asset"):
            with self.subTest(missing=missing):
                self.state = copy.deepcopy(saved)
                if missing == "locator": del self.state["last_root_checkpoint_observed"]
                else: del self.state["recovery_assets"]["latest-root-checkpoint"]
                result = w.recovery(self.state, self.repo)
                self.assertEqual("BLOCKED", result["recovery_status"])
                self.assertIsNone(result["root_checkpoint"])
                self.assertNotIn(str(self.root_checkpoint), result["read_first"])

    def test_recovery_rejects_newest_locator_path_or_hash_disagreement(self):
        self.observe_new_root_checkpoint()
        saved = copy.deepcopy(self.state)
        for field, value in (("path", str(self.root_checkpoint)), ("sha256", "0" * 64)):
            with self.subTest(field=field):
                self.state = copy.deepcopy(saved)
                self.state["last_root_checkpoint_observed"][field] = value
                result = w.recovery(self.state, self.repo)
                self.assertEqual("BLOCKED", result["recovery_status"])
                self.assertIsNone(result["root_checkpoint"])
                self.assertNotIn(str(self.root_checkpoint), result["read_first"])

    def test_fresh_recovery_missing_or_tampered_newest_never_falls_back(self):
        latest = self.observe_new_root_checkpoint()
        original = latest.read_bytes()
        for mutation in ("tamper", "missing"):
            with self.subTest(mutation=mutation):
                latest.write_bytes(original)
                if mutation == "tamper": latest.write_bytes(b"changed current instructions")
                else: latest.unlink()
                result, recovered = self.recover_in_fresh_process()
                self.assertEqual(2, result.returncode)
                self.assertEqual("BLOCKED", recovered["recovery_status"])
                self.assertIsNone(recovered["root_checkpoint"])
                self.assertNotIn(str(self.root_checkpoint), recovered["read_first"])

    def test_fresh_recovery_fifo_replacement_is_bounded_blocked(self):
        latest = self.observe_new_root_checkpoint()
        latest.unlink()
        os.mkfifo(latest)
        result, recovered = self.recover_in_fresh_process()
        self.assertEqual(2, result.returncode)
        self.assertEqual("BLOCKED", recovered["recovery_status"])
        self.assertIsNone(recovered["root_checkpoint"])
        self.assertNotIn(str(self.root_checkpoint), recovered["read_first"])

    def test_recovery_malformed_newest_identity_blocks_without_stale_read_order(self):
        self.observe_new_root_checkpoint()
        self.state["recovery_assets"]["latest-root-checkpoint"] = []
        result, recovered = self.recover_in_fresh_process()
        self.assertEqual(2, result.returncode)
        self.assertEqual("BLOCKED", recovered["recovery_status"])
        self.assertIsNone(recovered["root_checkpoint"])
        self.assertNotIn(str(self.root_checkpoint), recovered["read_first"])

    def test_recovery_explicit_empty_newest_record_cannot_select_legacy(self):
        for field in ("locator", "asset"):
            with self.subTest(field=field):
                state = copy.deepcopy(self.state)
                if field == "locator": state["last_root_checkpoint_observed"] = None
                else: state["recovery_assets"]["latest-root-checkpoint"] = None
                result = w.recovery(state, self.repo)
                self.assertEqual("BLOCKED", result["recovery_status"])
                self.assertIsNone(result["root_checkpoint"])
                self.assertNotIn(str(self.root_checkpoint), result["read_first"])

    def test_recovery_newest_symlink_cannot_redirect_current_instructions(self):
        latest = self.observe_new_root_checkpoint()
        latest.unlink()
        latest.symlink_to(self.root_checkpoint)
        result = w.recovery(self.state, self.repo)
        self.assertEqual("BLOCKED", result["recovery_status"])
        self.assertIsNone(result["root_checkpoint"])
        self.assertNotIn(str(self.root_checkpoint), result["read_first"])

    def test_recovery_reports_revision_disagreement(self):
        self.state["observed_revision"] = "stale"
        self.assertTrue(w.recovery(self.state, self.repo)["discrepancies"])

    def test_recovery_revalidates_old_verified_labels_against_evidence(self):
        self.state["tasks"]["old"] = {"status": "VERIFIED", "source_revision": "f" * 40, "verified_revision": "f" * 40, "evidence": ["old-pass"]}
        self.state["evidence"]["old-pass"] = {"result": "PASS", "source_revision": "e" * 40}
        self.state["verified_revision"] = "f" * 40
        result = w.recovery(self.state, self.repo)
        self.assertEqual("BLOCKED", result["recovery_status"])
        self.assertTrue(any("Ledger evidence/schema" in x for x in result["discrepancies"]))

    def test_recovery_missing_temporary_asset_is_blocked(self):
        asset = self.root / "temporary-source.zip"
        asset.write_bytes(b"retained source fixture")
        self.state["recovery_assets"]["source"] = {"path": str(asset), **w.file_identity(asset)}
        self.assertEqual("CHECKPOINT", w.recovery(self.state, self.repo)["recovery_status"])
        asset.unlink()  # exactly the restart failure: historic labels remain, required bytes disappear
        recovered = w.recovery(self.state, self.repo)
        self.assertEqual("BLOCKED", recovered["recovery_status"])
        self.assertTrue(any("Recovery asset unavailable" in x for x in recovered["discrepancies"]))

    def test_symlink_cannot_hide_missing_required_recovery_payload(self):
        payload = self.root / "lost-payload.zip"
        payload.write_bytes(b"payload")
        link = self.root / "durable-looking-link"
        link.symlink_to(payload)
        self.state["recovery_assets"]["source"] = {"path": str(link), **w.file_identity(link)}
        payload.unlink()
        recovered = w.recovery(self.state, self.repo)
        self.assertEqual("BLOCKED", recovered["recovery_status"])
        self.assertTrue(any("retained regular files" in x for x in recovered["discrepancies"]))

    def test_initialisation_never_invents_missing_programme_checkpoint(self):
        with self.assertRaisesRegex(w.Refusal, "Existing ROOT-LIVE"):
            w.initial_state(self.repo, self.root / "nonexistent.json")

    def test_missing_git_alternate_is_infrastructure_blocker(self):
        alternate = self.repo / ".git/objects/info/alternates"
        alternate.write_text(str(self.root / "lost-tmp-object-store") + "\n")
        check = w.git_closure(self.repo)
        self.assertEqual("BLOCKED", check["result"])
        self.assertTrue(any("Missing Git object alternate" in x for x in check["problems"]))

    def test_missing_source_donor_is_detected_before_cold_build(self):
        trigger = self.repo / "Loader.props"
        trigger.write_text("consumes XamlX sources")
        registry = {"donors": {"xamlx": {"path": "vendor/XamlX", "revision": "0" * 40, "when_any": ["Loader.props"],
                                            "files": {"vendor/XamlX/Ast.cs": {"kind": "file", "bytes": 1, "sha256": "0" * 64, "mode": 420}}}}}
        result = w.preflight(self.repo, donors=registry)
        donor = next(c for c in result["checks"] if c["check"].startswith("donor-source"))
        self.assertEqual(("BLOCKED", "INFRASTRUCTURE"), (donor["result"], donor["classification"]))
        self.assertTrue(any("Missing pinned donor input" in p for p in donor["problems"]))
        self.assertFalse(result["ready"])

    def test_preflight_missing_production_configuration_is_blocked(self):
        (self.repo / "NuGet.Config").unlink()
        self.assertFalse(w.preflight(self.repo)["ready"])

    def test_retired_provider_refused_before_execution(self):
        for command in (["opencode", "run", "--model", "inception/mercury-3"], ["run-isolated-mercury-worker.py"], ["curl", "https://api.inceptionlabs.ai"]):
            with self.subTest(command=command), self.assertRaisesRegex(w.Refusal, "revoked"):
                w.provider_allowed(command)
        w.provider_allowed(["opencode", "run", "--model", "opencode/beam"])
        w.provider_allowed(["dotnet", "build", "/workspace/9to1-mercury-profile10-first-main-close-20261021/Tests.csproj"])


if __name__ == "__main__":
    unittest.main()
