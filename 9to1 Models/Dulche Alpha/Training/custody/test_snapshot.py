import os
from pathlib import Path
import unittest
from uuid import uuid4

import torch
from torch import nn

from custody import ArtifactStore, CustodyError
from custody.artifacts import create_directory
from custody.snapshot import load_snapshot, publish_snapshot
from efficient import Config, CpuTrainer


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        configured = os.environ.get("DULCHE_TEST_ARTIFACT_ROOT")
        if not configured:
            self.skipTest("Run the registered CPU fixture with a durable artifact location.")
        self.assertEqual(os.environ.get("CUDA_VISIBLE_DEVICES"), "-1")
        self.assertFalse(torch.cuda.is_initialized())
        parent = Path(configured)
        create_directory(parent)
        evidence = parent / str(uuid4())
        create_directory(evidence, exclusive=True)
        print(f"Snapshot fixture evidence: {evidence}", flush=True)
        self.store = ArtifactStore(evidence / "store")
        run = self.store.create_run("CPU data-only snapshot fixture", {"purpose": "software_test_only"})
        self.attempt = self.store.begin_attempt(run, {"mode": "block_coordinate", "purpose": "software_test_only"})
        self.prior_threads = torch.get_num_threads()
        self.addCleanup(torch.set_num_threads, self.prior_threads)
        torch.set_num_threads(1)

    @staticmethod
    def trainer(callback=None):
        model = nn.Sequential(nn.Linear(2, 3), nn.Tanh(), nn.Linear(3, 1)).double()
        blocks = (("0.weight", "0.bias"), ("2.weight", "2.bias"))
        return CpuTrainer(model, blocks, Config(mode="block_coordinate", optimizer="adam"), on_update=callback)

    def test_actual_trainer_checkpoint_reopens_with_integer_slots_and_tuple_state(self):
        captured = []

        def save(state, record):
            captured.append(publish_snapshot(self.store, self.attempt, f"segment-{record.update}", state,
                                             kind="segment_checkpoint", max_bytes=100000))

        original = self.trainer(save)
        inputs = torch.tensor([[1, 2], [2, 3]], dtype=torch.float64)
        targets = torch.tensor([[0], [1]], dtype=torch.float64)
        original.advance(inputs, targets)
        self.assertEqual(len(captured), 1)
        reopened = ArtifactStore(self.store.root)
        state = load_snapshot(reopened, captured[0], max_bytes=100000)
        self.assertTrue(all(type(key) is int for key in state["optimizer"]["state"]))
        self.assertIs(type(state["optimizer"]["param_groups"][0]["betas"]), tuple)
        self.assertIs(type(state["blocks"]), tuple)

        resumed = self.trainer()
        resumed.restore(state)
        original.advance(inputs, targets)
        resumed.advance(inputs, targets)
        continued = original.snapshot()
        restored = resumed.snapshot()
        publish_snapshot(self.store, self.attempt, "continued", continued, max_bytes=100000)
        publish_snapshot(self.store, self.attempt, "restored", restored, max_bytes=100000)
        for key, expected in continued["model"].items():
            torch.testing.assert_close(restored["model"][key], expected, rtol=0, atol=0)

    def test_refuses_oversized_or_malformed_records(self):
        state = self.trainer().snapshot()
        publish_snapshot(self.store, self.attempt, "fixture-input", state, max_bytes=100000)
        with self.assertRaises(CustodyError):
            publish_snapshot(self.store, self.attempt, "small", state, max_bytes=32)
        malformed = self.store.publish_bytes(self.attempt, "old-pickle", "checkpoint", b"untrusted bytes")
        with self.assertRaises(CustodyError):
            load_snapshot(self.store, malformed, max_bytes=100000)
