import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("workflow", Path(__file__).with_name("workflow.py"))
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)


class RecoveryActionLineageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        subprocess.run(["git", "init", "-q", str(self.repo)], check=True)
        (self.repo / "AGENTS.md").write_text("fixture instructions\n")
        (self.repo / "spec.md").write_text("fixture scope\n")
        subprocess.run(["git", "-C", str(self.repo), "add", "."], check=True)
        subprocess.run(["git", "-C", str(self.repo), "-c", "user.name=Fixture", "-c",
                        "user.email=fixture@example.invalid", "commit", "-qm", "fixture"], check=True)
        self.original_checkpoint = self.root / "ROOT-LIVE-origin.json"
        self.original_checkpoint.write_text('{"status":"historical"}\n')
        self.state = w.initial_state(self.repo, self.original_checkpoint)
        self.state["next_actions"] = ["stale projection instruction"]

    def observe(self, payload):
        path = self.root / "ROOT-LIVE-observed.json"
        path.write_text(json.dumps(payload) + "\n")
        asset = {"path": str(path), **w.file_identity(path)}
        self.state["generation"] = 9
        self.state["last_root_checkpoint_observed"] = {"path": str(path), "sha256": asset["sha256"]}
        self.state["recovery_assets"]["latest-root-checkpoint"] = asset
        return path, asset

    def test_uses_actions_from_exact_pinned_root_checkpoint_and_preserves_projection_history(self):
        path, asset = self.observe({"next_actions": ["current ROOT action A", "current ROOT action B"]})
        result = w.recovery(self.state, self.repo)
        self.assertEqual("CHECKPOINT", result["recovery_status"])
        self.assertEqual(["current ROOT action A", "current ROOT action B"], result["next_actions"])
        self.assertEqual(["stale projection instruction"], result["projection_next_actions"])
        self.assertEqual({"source": "root-live-checkpoint", "path": str(path),
                          "sha256": asset["sha256"], "selection": "last_root_checkpoint_observed",
                          "recorded_in_generation": 9}, result["next_actions_provenance"])

    def test_invalid_pinned_root_actions_block_without_falling_back(self):
        path, asset = self.observe({"next_actions": "malformed action payload"})
        result = w.recovery(self.state, self.repo)
        self.assertEqual("BLOCKED", result["recovery_status"])
        self.assertEqual([], result["next_actions"])
        self.assertEqual(["stale projection instruction"], result["projection_next_actions"])
        self.assertEqual("invalid-pinned-root-checkpoint", result["next_actions_provenance"]["source"])
        self.assertEqual(str(path), result["next_actions_provenance"]["path"])
        self.assertTrue(any("next_actions" in item for item in result["discrepancies"]))

    def test_old_checkpoint_without_actions_keeps_projection_compatibility(self):
        self.observe({"historical_status": "checkpoint without actions"})
        result = w.recovery(self.state, self.repo)
        self.assertEqual("CHECKPOINT", result["recovery_status"])
        self.assertEqual(["stale projection instruction"], result["next_actions"])
        self.assertEqual("projection-history", result["next_actions_provenance"]["source"])
        self.assertIn("legacy ROOT-LIVE checkpoint", result["next_actions_provenance"]["reason"])


if __name__ == "__main__":
    unittest.main()
