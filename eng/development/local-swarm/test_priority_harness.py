#!/usr/bin/env python3
"""Focused contract checks for priority dispatch; no broker or device mutation."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("priority_under_test", ROOT / "priority_harness.py")
priority = importlib.util.module_from_spec(spec)
spec.loader.exec_module(priority)

def task(task_id, group, source="9to1 Workspace/App/Owner.cs", test="9to1 Workspace/App/Tests.cs"):
    return {"ticket_id": "Q39", "group": group, "order": 1,
            "task": [task_id, source, test, "9to1 Workspace/App/Tests.csproj", "OwnerTests", "9to1 Workspace/App/"]}

class PriorityContracts(unittest.TestCase):
    def read(self, config):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.json"
            path.write_text(json.dumps(config))
            return priority.read_config(path)

    def test_mission_groups_sort_before_per_group_order(self):
        files = task("files", "P0 Files")
        spaces = task("spaces", "P0 Spaces and Assistants",
                      "9to1 Workspace/Spaces/Owner.cs", "9to1 Workspace/Spaces/Tests.cs")
        result = self.read({"schema": 1, "tasks": [files, spaces]})
        self.assertEqual(["spaces", "files"], [x["task"][0] for x in result["tasks"]])

    def test_duplicate_write_ownership_is_refused_even_across_priority_groups(self):
        with self.assertRaisesRegex(ValueError, "share owned write paths"):
            self.read({"schema": 1, "tasks": [task("a", "P0 Files"), task("b", "P0 Build")]})

    def test_escaping_task_path_is_refused(self):
        unsafe = task("bad", "P0 Files")
        unsafe["task"][1] = "9to1 Workspace/../outside.cs"
        with self.assertRaisesRegex(ValueError, "approved source tree"):
            self.read({"schema": 1, "tasks": [unsafe]})

    def test_existing_candidate_is_never_requeued_and_pause_remains_resumable(self):
        item = task("owned", "P0 Files")
        state = {"tasks": {"owned": {"state": "verified_candidate", "commit": "retained"}}}
        before = copy.deepcopy(state)
        self.assertFalse(priority.task_ready(item, state))
        self.assertEqual(before, state)
        self.assertTrue(priority.task_ready(item, {"tasks": {"owned": {"state": "paused"}}}))

    def test_failed_work_needs_an_explicit_repair_before_retry(self):
        item = task("owned", "P0 Files")
        self.assertFalse(priority.task_ready(item, {"tasks": {"owned": {"state": "blocked"}}}))

    def test_manifest_uses_only_allocated_requirement_ids(self):
        with tempfile.TemporaryDirectory() as directory:
            queue = Path(directory) / "queue.json"
            queue.write_text(json.dumps({"tickets": [{"ticket_id": "Q39", "row_ids": ["IX-SPACES-018"],
                                                        "first_action": {"id": "IX-SPACES-018"}}]}))
            h = types.SimpleNamespace(QUEUE=queue)
            item = task("owned", "P0 Spaces and Assistants")
            item["requirement_id"] = "UNALLOCATED"
            with self.assertRaisesRegex(ValueError, "does not belong"):
                priority.mapped_tickets(h, {"tasks": [item]})

    def test_scoped_clone_preserves_canonical_acceptance(self):
        with tempfile.TemporaryDirectory() as directory:
            queue = Path(directory) / "queue.json"
            requirement = {"id": "IX-SPACES-018", "acceptance": "Keep the canonical draft."}
            queue.write_text(json.dumps({"tickets": [{"ticket_id": "Q39", "row_ids": ["IX-SPACES-018"],
                                                        "first_action": requirement}]}))
            item = task("owned", "P0 Spaces and Assistants")
            item["focused_slice"] = "Test a rejected queued send."
            mapped = priority.mapped_tickets(types.SimpleNamespace(QUEUE=queue), {"tasks": [item]})
            self.assertEqual(requirement, mapped["Q39"]["first_action"])
            self.assertEqual("Keep the canonical draft.", mapped["owned"]["first_action"]["acceptance"])
            self.assertEqual("Test a rejected queued send.", mapped["owned"]["first_action"]["scoped_work"])

    def inkling_admission(self, *, row_changes=None, receipt_changes=None, run_changes=None):
        # Captured native status contract: managed engine fields, no backend key.
        row = {
            "id": priority.INKLING, "state": "loaded", "ownership": "dulche",
            "engine": "Native Strata / Inkling-2-Mini", "gpu": "RTX 5080",
            "context": 32768, "slots": 1,
        }
        receipt = {
            "model": priority.INKLING, "passed": True, "context_size": 32768,
            "generation_run": "qualification-run", "coexistence_v100": True,
        }
        run = {
            "id": "qualification-run", "state": "completed",
            "request": {"model": priority.INKLING},
            "completed_steps": [{"choices": [{"message": {"content": "LOCAL_READY"}}]}],
        }
        row.update(row_changes or {})
        receipt.update(receipt_changes or {})
        run.update(run_changes or {})
        calls = []

        def safe_model(model):
            calls.append(("safe_model", model))
            self.assertEqual(priority.INKLING, model)
            return copy.deepcopy(row)

        def api(method, path):
            calls.append((method, path))
            self.assertEqual(("GET", "/v1/agents/qualification-run"), (method, path))
            return copy.deepcopy(run)

        broker = types.SimpleNamespace(safe_model=safe_model, api=api)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "inkling-qualification.json").write_text(json.dumps(receipt))
            with patch.object(priority, "ROOT", path):
                admitted = priority.qualified_inkling(broker)
        return admitted, calls

    def test_actual_native_status_without_backend_admits_matching_completed_generation(self):
        admitted, calls = self.inkling_admission()
        self.assertEqual({
            "model": priority.INKLING, "context_budget": 30000, "slots": 1,
            "coexistence_v100": True,
        }, admitted)
        self.assertEqual([
            ("safe_model", priority.INKLING),
            ("GET", "/v1/agents/qualification-run"),
        ], calls)

    def test_native_admission_rejects_wrong_engine_owner_or_model_identity(self):
        for changes in (
            {"engine": "llama.cpp"},
            {"ownership": "external"},
            {"id": "local-c1f51015c3da7bed0e122ff3"},
            {"gpu": "Tesla V100"},
            {"context": 16384},
            {"slots": 2},
        ):
            with self.subTest(status=changes):
                admitted, calls = self.inkling_admission(row_changes=changes)
                self.assertIsNone(admitted)
                self.assertEqual([("safe_model", priority.INKLING)], calls)

    def test_native_admission_requires_passed_receipt_and_matching_completed_output(self):
        for changes in (
            {"passed": False},
            {"passed": "true"},
            {"model": "different-model"},
            {"context_size": 4096},
        ):
            with self.subTest(receipt=changes):
                admitted, calls = self.inkling_admission(receipt_changes=changes)
                self.assertIsNone(admitted)
                self.assertEqual([], calls)
        for changes in (
            {"state": "running"},
            {"state": "failed"},
            {"request": {"model": "different-model"}},
            {"request": {}},
            {"completed_steps": []},
            {"completed_steps": [{"choices": [{"message": {"content": "  "}}]}]},
        ):
            with self.subTest(generation=changes):
                admitted, calls = self.inkling_admission(run_changes=changes)
                self.assertIsNone(admitted)
                self.assertEqual(("GET", "/v1/agents/qualification-run"), calls[-1])

if __name__ == "__main__":
    unittest.main(verbosity=2)
