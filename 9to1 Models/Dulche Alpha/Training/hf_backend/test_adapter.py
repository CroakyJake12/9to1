"""Tiny CPU arithmetic/token fixtures: software_test_only, never training data."""
import copy
import json
import os
from pathlib import Path
import unittest
from uuid import uuid4

from hf_backend.cpu_guard import install, assert_cpu

CUDA_REFUSAL = install()  # Must precede torch-dependent HF/PEFT imports below.

import torch
from torch.nn import functional as F

from custody.artifacts import ArtifactStore, CustodyError
from custody.snapshot import publish_snapshot
from custody.provenance import DatasetManifest
from hf_backend.adapter import LocalSession, _parse_batch, attach_lora, make_software_model


torch.set_num_threads(1)


def software_batch():
    ids = torch.tensor([[1, 3, 8, 2, 0], [1, 5, 4, 9, 2]], dtype=torch.long)
    mask = (ids != 0).long()
    labels = ids.clone()
    labels[mask == 0] = -100
    return {"input_ids": ids, "attention_mask": mask, "labels": labels}


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def preserve(self, model, phase, optimizer=None):
        if not hasattr(self, "fixture_store"):
            root = Path(os.environ["DULCHE_HF_TEST_ARTIFACT_ROOT"]) / ("hf-algebra-" + str(uuid4()))
            root.mkdir()
            self.fixture_store = ArtifactStore(root / "store")
            run = self.fixture_store.create_run("Software-only HF algebra fixture", {"purpose": "software_test_only"})
            self.fixture_attempt = self.fixture_store.begin_attempt(run, {"purpose": "software_test_only", "test": self.id()})
        state = {"schema": 1, "purpose": "software_test_only", "phase": phase,
                 "model": copy.deepcopy(model.state_dict()),
                 "optimizer": None if optimizer is None else copy.deepcopy(optimizer.state_dict()),
                 "rng": torch.get_rng_state().clone()}
        publish_snapshot(self.fixture_store, self.fixture_attempt, phase, state, max_bytes=8 * 1024 * 1024)

    def test_standard_loss_gradients_and_update_match_independent_shifted_ce(self):
        torch.manual_seed(881)
        model = make_software_model()
        reference = copy.deepcopy(model)
        self.preserve(model, "original")
        batch = software_batch()
        assert_cpu(model, batch)
        assert_cpu(reference, batch)
        actual = model(**batch).loss
        logits = reference(input_ids=batch["input_ids"], attention_mask=batch["attention_mask"]).logits
        expected = F.cross_entropy(logits[:, :-1, :].reshape(-1, 32).float(), batch["labels"][:, 1:].reshape(-1), ignore_index=-100)
        torch.testing.assert_close(actual, expected, rtol=1e-6, atol=1e-7)
        actual.backward()
        expected.backward()
        for (name, parameter), (_, other) in zip(model.named_parameters(), reference.named_parameters()):
            with self.subTest(parameter=name):
                self.assertIsNotNone(parameter.grad)
                torch.testing.assert_close(parameter.grad, other.grad, atol=1e-9, rtol=1e-6)
        for index, candidate in enumerate((model, reference)):
            assert_cpu(candidate, batch)
            optimizer = torch.optim.SGD(candidate.parameters(), lr=0.02, foreach=False)
            optimizer.step()
            self.preserve(candidate, f"updated-{index}", optimizer)
        for parameter, other in zip(model.parameters(), reference.parameters()):
            torch.testing.assert_close(parameter, other, atol=1e-9, rtol=1e-6)
        self.assertFalse(torch.cuda.is_initialized())

    def test_peft_initial_equivalence_base_frozen_and_adapter_gradient_algebra(self):
        torch.manual_seed(211)
        model = make_software_model()
        self.preserve(model, "original")
        batch = software_batch()
        assert_cpu(model, batch)
        original = model(**batch).logits.detach()
        adapted = attach_lora(model)
        assert_cpu(adapted, batch)
        torch.testing.assert_close(adapted(**batch).logits, original, atol=1e-10, rtol=1e-9)
        trainable = [name for name, p in adapted.named_parameters() if p.requires_grad]
        self.assertTrue(trainable)
        self.assertTrue(all("lora_A" in name or "lora_B" in name for name in trainable))
        target = next(module for module in adapted.modules() if hasattr(module, "lora_A") and "default" in module.lora_A)
        # Nonzero B exercises gradients into A too, independent of zero-init.
        with torch.no_grad():
            target.lora_B["default"].weight.fill_(0.03)
        self.preserve(adapted, "modified-lora-fixture")
        a = target.lora_A["default"].weight.detach().clone().requires_grad_()
        b = target.lora_B["default"].weight.detach().clone().requires_grad_()
        base = target.base_layer.weight.detach().clone()
        x = torch.arange(32, dtype=base.dtype).reshape(2, 16) / 31
        actual = target(x).square().mean()
        manual = (F.linear(x, base) + F.linear(F.linear(x.to(a.dtype), a), b).to(base.dtype) * target.scaling["default"]).square().mean()
        actual.backward()
        manual.backward()
        torch.testing.assert_close(target.lora_A["default"].weight.grad, a.grad)
        torch.testing.assert_close(target.lora_B["default"].weight.grad, b.grad)
        self.assertIsNone(target.base_layer.weight.grad)
        frozen = {name: p.detach().clone() for name, p in adapted.named_parameters() if not p.requires_grad}
        adapted.zero_grad(set_to_none=True)
        assert_cpu(adapted, batch)
        adapted(**batch).loss.backward()
        optimizer = torch.optim.Adam([p for p in adapted.parameters() if p.requires_grad], lr=0.001, foreach=False)
        assert_cpu(adapted, batch)
        optimizer.step()
        self.preserve(adapted, "updated-adapter", optimizer)
        for name, p in adapted.named_parameters():
            if name in frozen:
                torch.testing.assert_close(p, frozen[name], atol=0, rtol=0)
        self.assertFalse(torch.cuda.is_initialized())

    def test_batch_parser_refuses_invalid_shapes_tokens_padding_and_unlabelled(self):
        batch = {key: value.tolist() for key, value in software_batch().items()}
        parsed = _parse_batch(json.dumps(batch).encode(), 32, 32)
        self.assertEqual(parsed["labels"].shape, (2, 5))
        for key, value in (("input_ids", [[True] * 5] * 2), ("input_ids", [[99] * 5] * 2),
                           ("attention_mask", [[2] * 5] * 2), ("labels", [[-100] * 5] * 2),
                           ("labels", [[1] * 5] * 2)):
            malformed = dict(batch, **{key: value})
            with self.assertRaises(CustodyError):
                _parse_batch(json.dumps(malformed).encode(), 32, 32)

    def test_real_session_rejects_software_manifest_before_loading(self):
        with self.assertRaisesRegex(CustodyError, "Dataset purpose"):
            LocalSession(None, None, DatasetManifest("software_test_only", ()), None,
                         purpose="real_training", on_update=lambda state, record: None,
                         on_failed_update=lambda state, record: None)

    def test_real_training_requires_verified_checkpoint_publisher_before_loading(self):
        for options in ({}, {"on_update": lambda state, record: None},
                        {"on_failed_update": lambda state, record: None},
                        {"on_update": lambda state, record: None, "on_failed_update": lambda state, record: None}):
            with self.assertRaisesRegex(CustodyError, "verified immutable checkpoint publisher"):
                LocalSession(None, None, None, None, purpose="real_training", **options)

    def test_lora_rejects_ambiguous_settings(self):
        for value in (True, 0, -1, 65, "2"):
            with self.assertRaises(ValueError):
                attach_lora(make_software_model(), rank=value)


if __name__ == "__main__":
    unittest.main(verbosity=2)
