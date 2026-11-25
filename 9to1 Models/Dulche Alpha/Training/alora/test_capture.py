"""CPU-only integration fixtures for native Qwen4-Exp dispatch capture."""

import copy
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install

_GUARD = install()

import torch
from torch import nn
from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpTextSparseMoeBlock

from custody.artifacts import CustodyError
from alora.capture import profile_row
from alora.profile import ActivationProfiler
import alora.test_data as data_fixtures


class _TokenModel(nn.Module):
    """Tiny random native sparse blocks; never an input model or training corpus."""

    def __init__(self, *, two_layers=False, behavior="normal"):
        super().__init__()
        config = SimpleNamespace(
            hidden_size=8, intermediate_size=12, moe_intermediate_size=4,
            shared_expert_intermediate_size=4, num_experts=3,
            num_experts_per_tok=2, norm_topk_prob=True, hidden_act="silu",
            _experts_implementation="eager",
        )
        self.embedding = nn.Embedding(32, 8)
        self.block = Qwen4ExpTextSparseMoeBlock(config)
        self.second = Qwen4ExpTextSparseMoeBlock(config) if two_layers else None
        with torch.no_grad():
            for parameter in self.parameters():
                parameter.uniform_(-0.1, 0.1)
        self.behavior = behavior
        self.calls = 0
        self.observed = None

    def get_input_embeddings(self):
        return self.embedding

    def forward(self, input_ids, attention_mask, use_cache):
        self.calls += 1
        self.observed = {
            "input_ids": input_ids.detach().cpu().tolist(),
            "attention_mask": attention_mask.detach().cpu().tolist(),
            "use_cache": use_cache, "grad_enabled": torch.is_grad_enabled(),
            "all_eval": all(not module.training for module in self.modules()),
        }
        if self.behavior == "fail":
            raise RuntimeError("software forward failure")
        hidden = self.embedding(input_ids)
        if self.behavior == "extra-sample":
            hidden = torch.cat((hidden, hidden), dim=0)
        if self.behavior == "kwargs":
            flat = hidden.reshape(-1, hidden.shape[-1])
            _, weights, indices = self.block.gate(flat)
            hidden = self.block.experts(hidden_states=flat, top_k_index=indices,
                                        top_k_weights=weights)
        elif self.behavior != "skip-all":
            hidden = self.block(hidden)
            if self.behavior == "repeat":
                hidden = self.block(hidden)
        if self.second is not None and self.behavior != "skip-second":
            hidden = self.second(hidden)
        return hidden


class CaptureTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        state = torch.get_rng_state()
        self.addCleanup(torch.set_rng_state, state)
        torch.manual_seed(1307)
        fixture = data_fixtures.TrainingDatasetTests(
            "test_only_training_rows_are_exposed_with_pinned_source_and_owned_snapshot")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.records["train"][0]["input_ids"].append(0)
        fixture.records["train"][0]["attention_mask"].append(0)
        fixture.records["train"][0]["labels"].append(-100)
        fixture.seal()
        self.fixture = fixture
        self.dataset = fixture.load(max_tokens=4)
        self.row = next(self.dataset.rows())
        self.model = _TokenModel()
        self.model.train()
        self.model.embedding.eval()
        self.model.block.shared_expert.eval()
        self.profiler = ActivationProfiler({"block.experts": 3})

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def modes(self, model=None):
        return {name: module.training for name, module in (model or self.model).named_modules()}

    def hooks(self, model=None):
        return {name: tuple(module._forward_pre_hooks)
                for name, module in (model or self.model).named_modules()}

    def test_real_dispatch_is_staged_padding_masked_and_native_state_preserved(self):
        before_profile = self.profiler.to_record()
        before_row = copy.deepcopy(self.row)
        modes, hooks = self.modes(), self.hooks()
        weights = {name: value.detach().clone() for name, value in self.model.named_parameters()}
        permits = []
        result = profile_row(self.model, self.dataset, self.row, self.profiler,
                             before_compute=lambda: permits.append("permitted"))
        self.assertIsNot(result, self.profiler)
        self.assertEqual(self.profiler.to_record(), before_profile)
        self.assertEqual(self.row, before_row)
        self.assertEqual(permits, ["permitted", "permitted"])
        self.assertEqual(result.source_dataset, self.dataset.identity)
        self.assertEqual(result.purpose, "software_test_only")
        self.assertEqual(result.to_record()["purpose"], "software_test_only")
        self.assertEqual(self.modes(), modes)
        self.assertEqual(self.hooks(), hooks)
        stats = result.to_record()["layers"]["block.experts"]["coding"]
        self.assertEqual(stats["samples"], 1)
        self.assertEqual(stats["valid_tokens"], 3)
        self.assertAlmostEqual(sum(stats["mean_routing_mass"]), 1.0)
        self.assertEqual(self.model.observed, {
            "input_ids": [[1, 3, 9, 0]], "attention_mask": [[1, 1, 1, 0]],
            "use_cache": False, "grad_enabled": False, "all_eval": True,
        })
        for name, value in self.model.named_parameters():
            self.assertTrue(torch.equal(value, weights[name]), name)
            self.assertIsNone(value.grad, name)

    def test_keyword_dispatch_uses_the_same_native_capture_contract(self):
        self.model.behavior = "kwargs"
        result = profile_row(self.model, self.dataset, self.row, self.profiler)
        self.assertEqual(result.to_record()["layers"]["block.experts"]["coding"]["valid_tokens"], 3)

    def test_forward_failure_repeated_missing_and_extra_tokens_restore_everything(self):
        for behavior in ("fail", "repeat", "skip-all", "extra-sample"):
            with self.subTest(behavior=behavior):
                self.model.behavior = behavior
                modes, hooks, before = self.modes(), self.hooks(), self.profiler.to_record()
                with self.assertRaises((ValueError, RuntimeError)):
                    profile_row(self.model, self.dataset, self.row, self.profiler)
                self.assertEqual(self.profiler.to_record(), before)
                self.assertEqual(self.modes(), modes)
                self.assertEqual(self.hooks(), hooks)

    def test_compute_refusal_precedes_forward_and_restores_temporary_state(self):
        before, modes, hooks = self.profiler.to_record(), self.modes(), self.hooks()
        def refuse():
            self.assertTrue(all(not module.training for module in self.model.modules()))
            raise RuntimeError("permit declined")
        with patch("alora.capture.torch.tensor") as tensor:
            with self.assertRaisesRegex(RuntimeError, "permit declined"):
                profile_row(self.model, self.dataset, self.row, self.profiler, before_compute=refuse)
            tensor.assert_not_called()
        self.assertEqual(self.model.calls, 0)
        self.assertEqual(self.profiler.to_record(), before)
        self.assertEqual(self.modes(), modes)
        self.assertEqual(self.hooks(), hooks)

    def test_stale_dataset_is_refused_before_any_model_execution(self):
        path = self.fixture.root / "train.jsonl"
        path.write_bytes(path.read_bytes() + b" ")
        callbacks = []
        with self.assertRaises(CustodyError):
            profile_row(self.model, self.dataset, self.row, self.profiler,
                        before_compute=lambda: callbacks.append(1))
        self.assertEqual(callbacks, [])
        self.assertEqual(self.model.calls, 0)
        self.assertEqual(self.profiler.to_record()["observations"], [])

    def test_evaluation_and_conflicting_purpose_cannot_supply_profiles(self):
        for replacement in ({"file_id": "validation-file"},
                            {"file_id": "final_held_out-file"},
                            {"purpose": "real_training"}):
            with self.subTest(replacement=replacement):
                row = copy.deepcopy(self.row)
                row["_source"].update(replacement)
                with self.assertRaises(CustodyError):
                    profile_row(self.model, self.dataset, row, self.profiler)
                self.assertEqual(self.model.calls, 0)
                self.assertEqual(self.profiler.to_record()["observations"], [])

    def test_unknown_non_native_or_mismatched_topology_refuses_before_forward(self):
        for topology in ({"missing": 3}, {"embedding": 3}, {"block.experts": 4}):
            with self.subTest(topology=topology):
                profiler = ActivationProfiler(topology)
                before = profiler.to_record()
                with self.assertRaises(ValueError):
                    profile_row(self.model, self.dataset, self.row, profiler)
                self.assertEqual(profiler.to_record(), before)
                self.assertEqual(self.model.calls, 0)

    def test_missing_second_layer_cannot_publish_first_layer_only(self):
        model = _TokenModel(two_layers=True, behavior="skip-second")
        profiler = ActivationProfiler({"block.experts": 3, "second.experts": 3})
        before, hooks, modes = profiler.to_record(), self.hooks(model), self.modes(model)
        with self.assertRaisesRegex(ValueError, "did not execute"):
            profile_row(model, self.dataset, self.row, profiler)
        self.assertEqual(profiler.to_record(), before)
        self.assertEqual(self.hooks(model), hooks)
        self.assertEqual(self.modes(model), modes)

    def test_staging_failure_leaves_all_original_layer_records_unchanged(self):
        model = _TokenModel(two_layers=True)
        profiler = ActivationProfiler({"block.experts": 3, "second.experts": 3})
        profiler.bind_dataset(self.dataset.identity)
        profiler.record("second.experts", self.row["category"], self.row["id"],
                        [[0]], [[1.0]], [True])
        before, hooks, modes = profiler.to_record(), self.hooks(model), self.modes(model)
        with self.assertRaisesRegex(ValueError, "already been profiled"):
            profile_row(model, self.dataset, self.row, profiler)
        self.assertEqual(profiler.to_record(), before)
        self.assertEqual(self.hooks(model), hooks)
        self.assertEqual(self.modes(model), modes)

    def test_unadmitted_dataset_and_invalid_callback_refuse(self):
        with self.assertRaises(TypeError):
            profile_row(self.model, object(), self.row, self.profiler)
        with self.assertRaises(TypeError):
            profile_row(self.model, self.dataset, self.row, self.profiler, before_compute=1)
        self.assertEqual(self.model.calls, 0)

    def test_bound_foreign_dataset_or_real_purpose_is_rejected_before_compute(self):
        for change in ({"dataset_sha256": "e" * 64}, {"purpose": "real_training"}):
            profiler = ActivationProfiler({"block.experts": 3}, base_model_sha256="c" * 64)
            profiler.bind_dataset(dict(self.dataset.identity, **change))
            before = profiler.to_record()
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, "cannot mix"):
                profile_row(self.model, self.dataset, self.row, profiler)
            self.assertEqual(profiler.to_record(), before)
            self.assertEqual(self.model.calls, 0)

    def test_real_purpose_requires_base_pin_before_any_model_operation(self):
        # Forged in-memory metadata only: the on-disk arithmetic fixture remains
        # software_test_only, and the mandatory rejection precedes all forward work.
        snapshot = copy.deepcopy(self.row)
        snapshot["_source"]["purpose"] = "real_training"
        with patch.object(self.dataset, "purpose", "real_training"), \
                patch.object(self.dataset, "manifest", SimpleNamespace(purpose="real_training")), \
                patch.object(self.dataset, "snapshot", return_value=snapshot):
            with self.assertRaisesRegex(ValueError, "base model SHA-256"):
                profile_row(self.model, self.dataset, self.row, self.profiler)
        self.assertEqual(self.model.calls, 0)
        self.assertIsNone(self.profiler.source_dataset)

    def test_dataset_purpose_cannot_override_its_immutable_manifest(self):
        with patch.object(self.dataset, "purpose", "real_training"), \
                patch.object(self.dataset, "snapshot", return_value=self.row):
            with self.assertRaisesRegex(ValueError, "conflicting purpose"):
                profile_row(self.model, self.dataset, self.row, self.profiler)
        self.assertEqual(self.model.calls, 0)


if __name__ == "__main__":
    unittest.main()
