"""Capacity pauses preserve completed CPU updates and resume the exact next row.

All rows and GGUF bytes here are numeric software fixtures, never training data.
"""
import copy
import os
from pathlib import Path
import unittest
from unittest.mock import patch
from uuid import uuid4

from hf_backend.cpu_guard import install
GUARD = install()

import numpy as np
import torch

from custody import ArtifactStore
from custody.artifacts import create_directory
from alora import test_data, test_gguf_store
from alora.adapters import attach_selected_experts
from alora.checkpoints import CheckpointPublisher
from alora.paging import PagedEmbedding
from alora.read_limits import FingerprintCapacityExceeded
from alora.session import ComputeInterrupted, TrainingSession
from alora.test_support import NumericModel


class _RandomNumericModel(NumericModel):
    def forward(self, *args, **kwargs):
        # Consume RNG before the row lookup, including a lookup that cannot fit.
        scale = 0.5 + torch.rand((), dtype=torch.float64)
        result = super().forward(*args, **kwargs)
        if result.loss is not None:
            result.loss = result.loss * scale
        return result


class CapacityPauseTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(8813)
        torch.set_num_threads(1)
        self.base = _RandomNumericModel()
        root = Path(os.environ["DULCHE_ALORA_TEST_ARTIFACT_ROOT"]) / ("capacity-pause-" + uuid4().hex)
        create_directory(root, exclusive=True)
        self.artifacts = ArtifactStore(root / "checkpoints")

        self.gguf = test_gguf_store.GGUFWeightStoreTests()
        self.gguf.setUp()
        self.addCleanup(self.gguf.doCleanups)
        embedding = np.array(self.base.embedding.weight.detach().cpu().numpy(),
                             dtype=np.float32, copy=True, order="C")
        self.path = self.gguf.write(tensors={"token_embd.weight": (embedding, None)})

        fixture = test_data.TrainingDatasetTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        self.dataset = fixture.load()
        self.rows = list(self.dataset.rows())
        self.publisher = self.publisher_for("capacity pause")

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(GUARD.dulche_guard_calls, 0)

    def publisher_for(self, name):
        run = self.artifacts.create_run("ALORA numeric " + name,
                                        {"purpose": "software_test_only"})
        attempt = self.artifacts.begin_attempt(run, {"purpose": "software_test_only"})
        return CheckpointPublisher(self.artifacts, attempt, purpose="software_test_only",
                                   max_bytes=2_000_000)

    def session(self, capacity):
        store = self.gguf.store([self.path], max_fingerprints=capacity)
        model = copy.deepcopy(self.base)
        model.embedding = PagedEmbedding(store, "token_embd.weight", dtype=torch.float64)
        attach_selected_experts(model, {"blocks.0.experts": [0, 1, 2, 3]}, rank=2, alpha=4)
        identity = {"base_model_sha256": store.source_identity,
                    "dataset_manifest_sha256": self.dataset.manifest_sha256,
                    "profile_sha256": "c" * 64}
        return TrainingSession(model, identity=identity, purpose="software_test_only",
                               learning_rate=0.03), store

    def assert_tree_equal(self, actual, expected):
        if isinstance(expected, torch.Tensor):
            self.assertTrue(torch.equal(actual, expected))
        elif isinstance(expected, dict):
            self.assertEqual(actual.keys(), expected.keys())
            for key in expected:
                self.assert_tree_equal(actual[key], expected[key])
        elif isinstance(expected, (list, tuple)):
            self.assertEqual(len(actual), len(expected))
            for left, right in zip(actual, expected):
                self.assert_tree_equal(left, right)
        else:
            self.assertEqual(actual, expected)

    def test_second_row_capacity_pause_preserves_checkpoint_and_fresh_store_resumes_once(self):
        session, full_store = self.session(capacity=3)
        first_record, first_publication = session.step_row(
            self.dataset, self.rows[0], self.publisher)
        first = self.publisher.load(first_publication)
        self.assertEqual(first_record["updates"], 1)
        self.assertEqual(full_store.stats["fingerprint_count"], 3)
        pins_before = dict(full_store._slice_hashes)
        last_record = copy.deepcopy(session.last_record)

        with patch.object(session.optimizer, "step", wraps=session.optimizer.step) as update:
            with self.assertRaises(ComputeInterrupted) as paused:
                session.step_row(self.dataset, self.rows[1], self.publisher)
            update.assert_not_called()
        self.assertIsInstance(paused.exception.__cause__, FingerprintCapacityExceeded)
        self.assertFalse(session.failed)
        self.assertEqual((session.updates, session.cursor), (1, 1))
        self.assertEqual(full_store._slice_hashes, pins_before)
        self.assertEqual(session.last_record, last_record)
        self.assertTrue(all(parameter.grad is None for parameter in session.parameters.values()))
        self.assert_tree_equal(session.snapshot(), first)
        self.assert_tree_equal(self.publisher.load(first_publication), first)

        # The worker can seal the most recent completed cursor without advancing it.
        pause_publication = session.publish_current(self.publisher)
        resume_snapshot = self.publisher.load(pause_publication)
        self.assert_tree_equal(resume_snapshot, first)
        admitted_source = full_store.source_identity
        full_store.close()

        reference, reference_store = self.session(capacity=4)
        reference.restore(first)
        expected_record, _ = reference.step_row(
            self.dataset, self.rows[1], self.publisher_for("uninterrupted reference"))
        expected = reference.snapshot()
        reference_store.close()

        resumed, fresh_store = self.session(capacity=3)
        self.assertEqual(fresh_store.source_identity, admitted_source)
        self.assertEqual(fresh_store.stats["fingerprint_count"], 0)
        resumed.restore(resume_snapshot)
        actual_record, second_publication = resumed.step_row(
            self.dataset, self.rows[resumed.cursor], self.publisher)
        self.assertEqual((resumed.updates, resumed.cursor), (2, 2))
        self.assertEqual(actual_record["source"], self.rows[1]["_source"])
        self.assertEqual(actual_record["loss"], expected_record["loss"])
        self.assertEqual(fresh_store.stats["fingerprint_count"], 3)
        self.assert_tree_equal(resumed.snapshot(), expected)
        self.assert_tree_equal(self.publisher.load(second_publication), expected)
        self.assert_tree_equal(self.publisher.load(first_publication), first)
