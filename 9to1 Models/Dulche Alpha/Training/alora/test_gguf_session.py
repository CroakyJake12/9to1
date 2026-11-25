"""Native GGUF loader identity remains bound through adapter checkpoints."""
import copy
import hashlib
import json
import unittest

from hf_backend.cpu_guard import install
GUARD = install()
import torch
from alora.gguf_model import load_text_model
from alora.session import TrainingSession
from alora.test_gguf_model import _fixture


class GGUFSessionTests(unittest.TestCase):
    def setUp(self):
        _, self.raw, self.store = _fixture()
        self.model = load_text_model(self.store, self.raw, rank=2, alpha=4, chunk_rows=3,
                                     selections={"model.layers.0.mlp.experts": [0, 1, 2]})
        self.identity = {"base_model_sha256":self.store.source_identity,
                         "dataset_manifest_sha256":"b"*64, "profile_sha256":"c"*64}

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(GUARD.dulche_guard_calls, 0)

    def test_execution_config_drift_is_refused_before_loss_or_update(self):
        session = TrainingSession(self.model, identity=self.identity, purpose="software_test_only")
        self.assertEqual(session.identity["loader"]["config_sha256"],
                         self.model.alora_loader_report["config_sha256"])
        self.model.config.router_aux_loss_coef += .01
        called = []
        with self.assertRaisesRegex(ValueError, "configuration changed"):
            session.step_software(lambda: called.append("must not compute"))
        self.assertEqual(called, [])
        self.assertEqual(session.updates, 0)
        self.assertTrue(session.failed)

    def test_changed_loader_config_or_runtime_identity_cannot_restore_checkpoint(self):
        first = TrainingSession(self.model, identity=self.identity, purpose="software_test_only")
        snapshot = first.snapshot()
        other = load_text_model(self.store, self.raw, rank=2, alpha=4, chunk_rows=3,
                                selections={"model.layers.0.mlp.experts": [0, 1, 2]})
        other.alora_loader_report["native_modeling_sha256"] = "d" * 64
        second = TrainingSession(other, identity=self.identity, purpose="software_test_only")
        with self.assertRaisesRegex(ValueError, "identity"):
            second.restore(snapshot)
        self.assertEqual(second.updates, 0)
        changed = copy.deepcopy(snapshot)
        changed["identity"]["loader"]["config_sha256"] = "e" * 64
        with self.assertRaisesRegex(ValueError, "identity"):
            first.restore(changed)

    def test_changed_backend_mapping_or_dequantizer_cannot_restore_checkpoint(self):
        first = TrainingSession(self.model, identity=self.identity, purpose="software_test_only")
        snapshot = first.snapshot()
        other = load_text_model(self.store, self.raw, rank=2, alpha=4, chunk_rows=3,
                                selections={"model.layers.0.mlp.experts": [0, 1, 2]})
        report = other.alora_loader_report
        manifest = report["backend_source_manifest"]
        self.assertIn("gguf_model.py", manifest["alora_sources"])
        self.assertIn("session.py", manifest["alora_sources"])
        self.assertIn("quants.py", manifest["gguf_sources"])
        self.assertFalse(any(n.startswith("test_") for n in manifest["alora_sources"]))
        manifest["gguf_sources"]["quants.py"] = "f" * 64
        with self.assertRaisesRegex(ValueError, "manifest digest"):
            TrainingSession(other, identity=self.identity, purpose="software_test_only")
        report["backend_source_sha256"] = hashlib.sha256(json.dumps(
            manifest, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
        second = TrainingSession(other, identity=self.identity, purpose="software_test_only")
        with self.assertRaisesRegex(ValueError, "identity"):
            second.restore(snapshot)
