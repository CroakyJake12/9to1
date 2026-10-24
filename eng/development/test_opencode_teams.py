"""Focused scheduling/admission/receipt tests; no inference, server or acceptance."""
import copy
import tempfile
import unittest
import json
import time
import urllib.error
from unittest.mock import patch
from pathlib import Path
import opencode_teams as teams


class API:
    def __init__(self):
        self.active = {}; self.inbox = []; self.messages = {}; self.posts = []; self.fail = False
        self.sessions = {"ses_platform": {"id": "ses_platform", "model": {"id": "verified-sol-id", "providerID": "verified-provider"}, "projectID": "project1", "location": {"directory": "/assigned/worktree"}}}
    def request(self, method, path, body=None):
        if method == "POST":
            self.posts.append(copy.deepcopy(body))
            if self.fail: raise OSError("lost response")
            if path == "/api/session":
                info = {"id": body["id"], "model": body["model"], "projectID": "project1", "location": body["location"]}
                if "parentID" in body: info["parentID"] = body["parentID"]
                if "metadata" in body: info["metadata"] = body["metadata"]
                self.sessions[body["id"]] = info
                return {"data": info}
            return {"data": {"id": body["id"], "sessionID": path.split("/")[3], "delivery": "queue", "type": "user"}}
        if path == "/api/model": return {"data": [{"id": "verified-sol-id", "providerID": "verified-provider", "name": "Actual native Sol name", "enabled": True, "status": "active"}]}
        if path == "/api/session/active": return {"data": self.active}
        if path.endswith("/inbox"): return {"data": self.inbox}
        if "/message/" in path:
            return {"data": self.messages[path.rsplit("/", 1)[1]]}
        sid = path.rsplit("/", 1)[1]
        if sid not in self.sessions: raise urllib.error.HTTPError(path, 404, "missing", {}, None)
        return {"data": self.sessions[sid]}


class TeamsTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(dir="/dev/shm")
        self.addCleanup(self.tmp.cleanup)
        evidence = Path(self.tmp.name) / "entitlement.json"
        workspace = Path(self.tmp.name) / "workspace.json"
        worktree = Path(self.tmp.name) / "worktree"; worktree.mkdir()
        stat = worktree.stat()
        workspace.write_text(json.dumps({"project_id": "project1", "location": {"directory": str(worktree)}, "revision": "exact-revision", "tree": "exact-tree", "generation": "generation1", "root_writer_handoff_ack": True, "claimed_paths": ["src/platform/**"], "directory_identity": {"device": stat.st_dev, "inode": stat.st_ino}}))
        git = patch.object(teams.workflow, "git", side_effect=lambda repo, *args: b"exact-tree" if args[-1] == "HEAD^{tree}" else b"exact-revision")
        git.start(); self.addCleanup(git.stop)
        self.state = {"schema": 1, "generation": 0, "workers": {}, "tasks": {}, "pending_changes": {},
                      "blockers": {}, "evidence": {}, "artifacts": {}, "recovery_assets": {}, "development_candidates": {},
                      "provider_policy": {"allowed": ["Codex", "Beam"]},
                      "owner_implementation_window": {"acceptance_paused": True}}
        self.api = API()
        self.binding = {"role": "leader", "label": "GPT-6.1 Sol", "provider_family": "Codex", "entitlement": "VERIFIED",
                        "paid_fallback": False, "session_id": "ses_platform", "allowed_paths": ["src/platform/**"],
                        "model": {"id": "verified-sol-id", "providerID": "verified-provider"},
                        "project_id": "project1", "location": {"directory": str(worktree)},
                        "phase": 1,
                        "workspace_identity": {"path": str(workspace), **teams.workflow.file_identity(workspace)}}
        proof = {k: self.binding[k] for k in ("label", "model", "provider_family", "entitlement", "paid_fallback")}
        proof.update(schema="9to1.native-model-entitlement/v1", verified_by="existing-coordinator", verified_at=time.time()-1, expires_at=time.time()+600, native_name="Actual native Sol name")
        evidence.write_text(json.dumps(proof))
        self.binding["evidence"] = {"path": str(evidence), **teams.workflow.file_identity(evidence)}
        self.api.sessions["ses_platform"]["location"] = self.binding["location"]
        teams.configure(self.state, {"teams": teams.TEAMS, "phases": [{"id": i + 1, "surfaces": surfaces, "requirements": []} for i, surfaces in enumerate(teams.PHASES)]})
        teams.bind(self.state, "platform", self.binding, self.api)
        teams.extension(self.state)["paused"] = False

    def prepare(self):
        return teams.prepare(self.state, "platform", "implement exact scope", ["src/platform/a.py"], "implementation", 3, self.api, 300)

    def test_tick_boundary_and_astra_period(self):
        teams.tick(self.state, 299)
        clocks = teams.extension(self.state)["clocks"]
        for c in clocks.values(): c["pending"] = False
        teams.tick(self.state, 300)
        self.assertTrue(clocks["platform"]["pending"])
        self.assertFalse(clocks["astra"]["pending"])
        teams.tick(self.state, 1800)
        self.assertTrue(clocks["astra"]["pending"])

    def test_busy_coalesces_without_duplicate_admission(self):
        teams.tick(self.state, 300); self.api.active = {"ses_platform": {"type": "running"}}
        self.assertIsNone(self.prepare())
        teams.tick(self.state, 900); self.api.active = {}
        mid = self.prepare(); self.assertTrue(mid)
        teams.tick(self.state, 1200)
        self.assertIsNone(self.prepare())
        self.assertEqual(len(teams.extension(self.state)["outbox"]), 1)

    def test_owner_conflict_and_scope_escape(self):
        teams.tick(self.state, 300)
        teams.workflow.claim(self.state, "other", ["src/platform/**"], "implementation")
        with self.assertRaises(teams.Refusal): self.prepare()
        self.state["workers"] = {}
        with self.assertRaises(teams.Refusal):
            teams.prepare(self.state, "platform", "x", ["src/other/a"], "implementation", 3, self.api, 300)

    def test_pause_unknown_provider_and_acceptance_guard(self):
        teams.tick(self.state, 300)
        teams.extension(self.state)["paused"] = True
        with self.assertRaises(teams.Refusal): self.prepare()
        teams.extension(self.state)["paused"] = False
        with self.assertRaises(teams.Refusal):
            teams.prepare(self.state, "platform", "x", ["src/platform/a"], "acceptance", 3, self.api, 300)
        teams.extension(self.state)["bindings"]["platform"]["provider_family"] = "unknown"
        with self.assertRaises(teams.Refusal): self.prepare()

    def test_unknown_delivery_never_blindly_retried(self):
        teams.tick(self.state, 300); mid = self.prepare()
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        self.api.fail = True
        self.assertEqual(teams.send_once(path, 0, mid, self.api)["status"], "UNKNOWN")
        with self.assertRaises(teams.Refusal): teams.send_once(path, 1, mid, self.api)
        self.assertEqual(len(self.api.posts), 1)
        self.assertEqual(self.api.posts[0]["delivery"], "queue")
        record = teams.workflow.read(path)["opencode_teams"]["outbox"][mid]
        self.assertEqual(teams.reconcile(record, self.api), "UNKNOWN")
        self.api.messages[mid] = {"id": mid, "sessionID": "ses_platform"}
        self.assertEqual(teams.reconcile(record, self.api), "DELIVERED")

    def test_pause_is_rechecked_before_send(self):
        teams.tick(self.state, 300); mid = self.prepare()
        teams.extension(self.state)["paused"] = True
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        with self.assertRaises(teams.Refusal): teams.send_once(path, 0, mid, self.api)
        self.assertEqual(self.api.posts, [])

    def test_report_delivery_waits_for_upstream_cadence(self):
        self.astra()
        self.state["tasks"]["leader-task"] = {"owner": "ses_platform", "source_revision": "exact-revision", "status": "WORKING"}
        result = Path(self.tmp.name) / "leader-result.json"; result.write_text(json.dumps({"task_id": "leader-task", "source_revision": "exact-revision", "summary": "Leader factual aggregate; acceptance paused"}))
        rid = teams.report(self.state, "platform", "leader-task", {"path": str(result), **teams.workflow.file_identity(result)}, 1801)
        teams.extension(self.state)["bindings"]["astra"]["report_paths"] = ["src/platform/report.py"]
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        teams.dispatch(path, 0, [], 3, self.api, 3599)
        self.assertEqual(self.api.posts, [])
        generation = teams.workflow.read(path)["generation"]
        teams.dispatch(path, generation, [], 3, self.api, 3600)
        self.assertEqual(len(self.api.posts), 1)
        self.assertEqual(self.api.posts[0]["metadata"]["9to1_reports"], [rid])
        self.assertEqual(self.api.posts[0]["delivery"], "queue")

    def test_prepared_binding_cannot_be_repointed(self):
        teams.tick(self.state, 300); self.prepare()
        with self.assertRaises(teams.Refusal): teams.bind(self.state, "platform", self.binding, self.api)

    def test_live_session_identity_is_rechecked(self):
        teams.tick(self.state, 300); mid = self.prepare()
        self.api.sessions["ses_platform"]["location"] = {"directory": "/wrong"}
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        with self.assertRaises(teams.Refusal): teams.send_once(path, 0, mid, self.api)
        self.assertEqual(self.api.posts, [])

    def test_worker_clock_and_model_mapping_refusal(self):
        teams.extension(self.state)["bindings"]["worker-one"] = dict(self.binding, role="worker", label="GPT-6 Luna")
        teams.tick(self.state, 300)
        self.assertTrue(teams.extension(self.state)["clocks"]["worker-one"]["pending"])
        with self.assertRaises(teams.Refusal): teams.model_allowed(self.state, teams.extension(self.state)["bindings"]["worker-one"])

    def test_bootstrap_reconciles_actual_session(self):
        binding = dict(self.binding, session_id="ses_new")
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        self.assertEqual(teams.bootstrap(path, 0, "platform", binding, self.api)["status"], "BOUND")
        self.assertEqual(len(self.api.posts), 1)
        self.assertIn("ses_new", teams.tab_links(teams.workflow.read(path), "actual-key")["platform"])

    def test_uncertain_bootstrap_never_reposts(self):
        binding = dict(self.binding, session_id="ses_new")
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        self.api.fail = True
        self.assertEqual(teams.bootstrap(path, 0, "platform", binding, self.api)["status"], "UNKNOWN")
        with self.assertRaises(teams.Refusal): teams.bootstrap(path, 1, "platform", binding, self.api)
        self.assertEqual(len(self.api.posts), 1)

    def test_invalid_bootstrap_name_causes_zero_posts(self):
        binding = dict(self.binding, session_id="ses_new")
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        with self.assertRaises(teams.Refusal): teams.bootstrap(path, 0, "platfrom-typo", binding, self.api)
        self.assertEqual(self.api.posts, [])

    def test_paused_new_creation_refused_but_existing_bind_read_allowed(self):
        teams.extension(self.state)["paused"] = True
        teams.bind(self.state, "platform", self.binding, self.api)
        binding = dict(self.binding, session_id="ses_new")
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        with self.assertRaises(teams.Refusal): teams.bootstrap(path, 0, "platform", binding, self.api)
        self.assertEqual(self.api.posts, [])

    def test_new_creation_obeys_existing_capacity(self):
        teams.extension(self.state)["catalogue"]["capacity"] = 1
        binding = dict(self.binding, session_id="ses_new")
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        with self.assertRaises(teams.Refusal): teams.bootstrap(path, 0, "platform", binding, self.api)
        self.assertEqual(self.api.posts, [])

    def watch_inputs(self):
        path = Path(self.tmp.name) / "watch-state.json"; teams.workflow.atomic(path, self.state)
        jobs = Path(self.tmp.name) / "jobs.json"; jobs.write_text(json.dumps([{ "name": "platform", "text": "implement", "paths": ["src/platform/a.py"], "kind": "implementation"}]))
        capacity = Path(self.tmp.name) / "capacity.json"; capacity.write_text(json.dumps({"capacity": 3}))
        return path, jobs, capacity

    def test_watch_bounded_cycles_coalesce_without_duplicate_post(self):
        path, jobs, capacity = self.watch_inputs(); waits = []
        result = teams.watch(path, jobs, capacity, self.api, max_cycles=2, clock=lambda: 300, wait=waits.append)
        self.assertEqual(result, {"status": "CYCLE_BOUND", "cycles": 2})
        self.assertEqual(waits, [30])
        self.assertEqual(len(self.api.posts), 1)

    def test_watch_singleton_refusal(self):
        path, jobs, capacity = self.watch_inputs()
        with teams.invoker_lock(path):
            with self.assertRaises(teams.Refusal): teams.watch(path, jobs, capacity, self.api, max_cycles=1)
        self.assertEqual(self.api.posts, [])

    def test_watch_sigint_leaves_native_work_queued(self):
        path, jobs, capacity = self.watch_inputs()
        def interrupted(_): raise KeyboardInterrupt()
        result = teams.watch(path, jobs, capacity, self.api, clock=lambda: 300, wait=interrupted)
        self.assertEqual(result, {"status": "INTERRUPTED", "cycles": 1})
        self.assertEqual(len(self.api.posts), 1)
        record = next(iter(teams.workflow.read(path)["opencode_teams"]["outbox"].values()))
        self.assertEqual(record["status"], "QUEUED")

    def test_watch_pause_and_explicit_stop_never_posts(self):
        teams.extension(self.state)["paused"] = True
        path, jobs, capacity = self.watch_inputs()
        self.assertEqual(teams.watch(path, jobs, capacity, self.api, max_cycles=1)["status"], "PAUSED")
        teams.extension(self.state)["paused"] = False; teams.workflow.atomic(path, self.state)
        stop = Path(self.tmp.name) / "stop"; stop.touch()
        self.assertEqual(teams.watch(path, jobs, capacity, self.api, max_cycles=1, stop_path=stop)["status"], "STOP_REQUESTED")
        self.assertEqual(self.api.posts, [])

    def test_later_phase_does_not_advance_on_percentages(self):
        binding = dict(self.binding, phase=10)
        self.state["implementation_percent"] = 100
        with self.assertRaises(teams.Refusal): teams.phase_allowed(self.state, binding)

    def test_catalogue_cannot_narrow_scope(self):
        catalog = copy.deepcopy(teams.extension(self.state)["catalogue"])
        catalog["phases"][13]["surfaces"] = ["Create"]
        with self.assertRaises(teams.Refusal): teams.configure(self.state, catalog)

    def test_phase_coverage_uses_current_custody_not_historical_observed_revision(self):
        current = "395a312170f9ae9900996b8dc82d050f7778c340"
        self.state["observed_revision"] = "historical-origin"
        manifest = Path(self.tmp.name) / "candidate.json"; manifest.write_text(json.dumps({"candidate_revision": current}))
        self.state["execution_custody"] = {"cloud-owning-v2": {"revision": current, "source": {"manifest": {"path": str(manifest), **teams.workflow.file_identity(manifest)}}}}
        coverage = Path(self.tmp.name) / "coverage.json"; coverage.write_text(json.dumps({"source_revision": current, "applicable_scope_complete": True, "requirements": [{"requirement_id": "SPEC-EXACT", "implemented": True, "source_evidence": "exact/source.py:5"}]}))
        self.state["tasks"]["coverage-task"] = {"status": "COMPLETED", "source_revision": current, "evidence": ["coverage"]}
        self.state["evidence"]["coverage"] = {"kind": "implementation-coverage", "result": "PASS", "source_revision": current, "artifact": {"path": str(coverage), **teams.workflow.file_identity(coverage)}}
        self.assertTrue(teams.phase_complete(self.state, {"requirements": ["coverage-task"]}))

    def test_one_shot_dispatch_uses_real_queue_contract(self):
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        result = teams.dispatch(path, 0, [{"name": "platform", "text": "implement", "paths": ["src/platform/a.py"], "kind": "implementation"}], 3, self.api, 300)
        self.assertEqual(result["status"], "ONE_SHOT_COMPLETE")
        self.assertEqual(result["deliveries"][0]["status"], "QUEUED")

    def astra(self):
        b = dict(self.binding, role="supervisor", label="GPT-6 Astra", session_id="ses_astra")
        evidence = Path(self.tmp.name) / "astra-proof.json"
        proof = teams.workflow.read(self.binding["evidence"]["path"]); proof["label"] = b["label"]
        evidence.write_text(json.dumps(proof)); b["evidence"] = {"path": str(evidence), **teams.workflow.file_identity(evidence)}
        self.api.sessions[b["session_id"]] = dict(self.api.sessions["ses_platform"], id=b["session_id"])
        teams.bind(self.state, "astra", b, self.api)

    def test_astra_deadline_stamped_before_normal_send(self):
        self.astra(); teams.tick(self.state, 1800)
        mid = teams.prepare(self.state, "astra", "review", ["src/platform/a.py"], "supervision", 3, self.api, 1800)
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state)
        now = int(time.time())
        with patch.object(teams.time, "time", return_value=now): teams.send_once(path, 0, mid, self.api)
        record = teams.workflow.read(path)["opencode_teams"]["outbox"][mid]
        self.assertEqual((record["sent_at"], record["checkpoint_due_at"]), (now, now + 300))

    def test_lost_receipt_never_resets_astra_deadline(self):
        self.astra(); teams.tick(self.state, 1800)
        mid = teams.prepare(self.state, "astra", "review", ["src/platform/a.py"], "supervision", 3, self.api, 1800)
        path = Path(self.tmp.name) / "state.json"; teams.workflow.atomic(path, self.state); self.api.fail = True
        now = int(time.time())
        with patch.object(teams.time, "time", return_value=now): teams.send_once(path, 0, mid, self.api)
        record = teams.workflow.read(path)["opencode_teams"]["outbox"][mid]
        with patch.object(teams.time, "time", return_value=5000):
            teams.reconcile(record, self.api); teams.reconcile(record, self.api)
        self.assertEqual(record["checkpoint_due_at"], now + 300)

    def test_root_worker_uses_isolated_files_and_native_team_metadata(self):
        b = dict(self.binding, role="worker", label="GPT-6 Luna", session_id="ses_worker", team="platform", leader_session_id="ses_platform", allowed_paths=["src/worker/**"])
        directory = Path(self.tmp.name) / "worker-tree"; directory.mkdir(); stat = directory.stat()
        b["location"] = {"directory": str(directory)}
        workspace = teams.workflow.read(self.binding["workspace_identity"]["path"])
        workspace.update(location=b["location"], claimed_paths=b["allowed_paths"], directory_identity={"device": stat.st_dev, "inode": stat.st_ino})
        artifact = Path(self.tmp.name) / "worker-workspace.json"; artifact.write_text(json.dumps(workspace)); b["workspace_identity"] = {"path": str(artifact), **teams.workflow.file_identity(artifact)}
        proof = teams.workflow.read(self.binding["evidence"]["path"]); proof["label"] = b["label"]
        artifact = Path(self.tmp.name) / "worker-proof.json"; artifact.write_text(json.dumps(proof)); b["evidence"] = {"path": str(artifact), **teams.workflow.file_identity(artifact)}
        self.api.sessions[b["session_id"]] = {"id": b["session_id"], "model": b["model"], "projectID": b["project_id"], "location": b["location"], "metadata": {"9to1_team": "platform", "9to1_leader_session": "ses_platform"}}
        teams.bind(self.state, "worker", b, self.api)
        (directory / "isolated.txt").write_text("worker")
        leader = Path(self.binding["location"]["directory"]); (leader / "isolated.txt").write_text("leader")
        self.assertEqual((directory / "isolated.txt").read_text(), "worker")
        self.assertEqual((leader / "isolated.txt").read_text(), "leader")
        teams.workflow.claim(self.state, "ses_worker", ["src/worker/a.py"], "worker")
        with self.assertRaises(teams.Refusal): teams.workflow.claim(self.state, "other", ["src/worker/a.py"], "worker")
        self.state["tasks"]["worker-task"] = {"owner": "ses_worker", "source_revision": "exact-revision", "status": "WORKING"}
        result = Path(self.tmp.name) / "result.json"; result.write_text(json.dumps({"task_id": "worker-task", "source_revision": "exact-revision", "summary": "Factual result; compiler NOT RUN"}))
        rid = teams.report(self.state, "worker", "worker-task", {"path": str(result), **teams.workflow.file_identity(result)}, 301)
        queued = teams.extension(self.state)["reports"][rid]
        self.assertEqual((queued["target"], queued["ready_slot"], queued["status"]), ("platform", 2, "PENDING"))
        self.assertEqual(self.api.posts, [])


if __name__ == "__main__": unittest.main()
