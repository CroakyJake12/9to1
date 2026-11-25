import hashlib
from pathlib import Path
import sys
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from .artifacts import ArtifactStore, CustodyError, _encode_record, _read_record
from .v8_sampler import V8_SOURCE_SHA256
from .v8_start_custody import _PAGER, seal_original_v8_start


class StartCustodyTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.store = ArtifactStore(Path(tmp.name) / "custody")
        self.origin = self.store.begin_attempt(self.store.create_run("synthetic step zero", {}), {})
        self.pin, self.seed = "c" * 64, 20261123
        self.before, self.after = b"before rng", b"after rng"
        self.raw = {
            "schema": 1, "source_sha256": V8_SOURCE_SHA256, "pager_sha256": _PAGER,
            "model_pin_manifest_sha256": self.pin, "seed": self.seed, "grad_checkpointing": True,
            "torch_version": "2.13.0+cu130", "peft_version": "0.18.1", "adapter_sha256": "f" * 64,
            "targets": [{"path": "model.language_model.layers.1.self_attn.q_proj",
                         "in_features": 8, "out_features": 8}],
            "cpu_before_sha256": hashlib.sha256(self.before).hexdigest(), "cpu_before_hex": self.before.hex(),
            "cpu_after_sha256": hashlib.sha256(self.after).hexdigest(), "cpu_after_hex": self.after.hex(),
        }

    def produce(self, kind="evidence"):
        return self.store.publish_bytes(self.origin, "original-reference.json", kind, _encode_record(self.raw))

    def seal(self, evidence, **options):
        args = {"trusted_reference_sha256": evidence.sha256, "model_pins_sha256": self.pin,
                "original_seed": self.seed} | options
        return seal_original_v8_start(self.store, self.origin, evidence, **args)

    def test_seals_step_zero_without_progress(self):
        row0 = "model.language_model.layers.0.self_attn.q_proj.lora_A.default.weight"
        row1 = "model.language_model.layers.32.self_attn.q_proj.lora_A.default.weight"
        save = Mock(side_effect=lambda row: next(iter(row)).encode())
        ref = self.produce()
        with patch("custody.v8_start_custody.initialize_attested_v8_lora",
                   return_value=({row0: object(), row1: object()}, self.after)), \
                patch.dict(sys.modules, {"safetensors.torch": SimpleNamespace(save=save)}):
            start = self.seal(ref)
        self.assertEqual(self.store.verify(start.early).read_bytes(), row0.encode())
        self.assertEqual(self.store.verify(start.tail).read_bytes(), row1.encode())
        self.assertEqual(_read_record(self.store.verify(start.base))["steps"], 0)
        attempt = self.store.begin_attempt(self.origin.run_id, {}, resume_from=(start.base,))
        self.assertNotEqual(attempt, self.origin)

    def test_no_replay_act_for_unapproved_lineage_or_changed_rng(self):
        with patch("custody.v8_start_custody.initialize_attested_v8_lora") as replay:
            with self.assertRaisesRegex(CustodyError, "sealed as runtime evidence"):
                self.seal(self.produce("checkpoint"))
            self.raw["cpu_before_hex"] = self.after.hex()
            ref = self.produce()
            with self.assertRaisesRegex(CustodyError, "RNG evidence"):
                self.seal(ref)
            with self.assertRaisesRegex(CustodyError, "registered"):
                self.seal(ref, model_pins_sha256="0" * 64)
            replay.assert_not_called()
