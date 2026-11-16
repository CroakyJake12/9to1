"""Arithmetic software fixtures ONLY. Never training/evaluation datasets or signals."""
import copy
import unittest

import torch
from torch import nn
from torch.nn import functional as F

from efficient import Config, CpuTrainer, LoRALinear


torch.set_num_threads(1)
PURPOSE = "software_test_only"


def fixture(dropout=False):
    # Shapes/arrays test the software's differentiation, not any learned task.
    torch.manual_seed(413)
    model = nn.Sequential(nn.Linear(3, 4), nn.Tanh(),
                          nn.Dropout(0.25) if dropout else nn.Identity(), nn.Linear(4, 2)).double()
    inputs = torch.arange(15, dtype=torch.float64).reshape(5, 3) / 13 - 0.4
    targets = torch.arange(10, dtype=torch.float64).reshape(5, 2) / 17
    return model, inputs, targets


def partition(model):
    return tuple((name,) for name, p in model.named_parameters() if p.requires_grad)


class CoreTests(unittest.TestCase):
    def assertTreeEqual(self, left, right):
        self.assertIs(type(left), type(right))
        if isinstance(left, torch.Tensor):
            self.assertEqual(left.dtype, right.dtype)
            self.assertTrue(torch.equal(left, right))
        elif isinstance(left, dict):
            self.assertEqual(left.keys(), right.keys())
            for key in left:
                self.assertTreeEqual(left[key], right[key])
        elif isinstance(left, (list, tuple)):
            self.assertEqual(len(left), len(right))
            for a, b in zip(left, right):
                self.assertTreeEqual(a, b)
        else:
            self.assertEqual(left, right)

    def assertModels(self, left, right):
        for l, r in zip(left.parameters(), right.parameters()):
            torch.testing.assert_close(l, r, atol=2e-12, rtol=2e-11)

    def test_conventional_matches_torch_sgd_and_adam(self):
        for optimizer in ("sgd", "adam"):
            with self.subTest(optimizer=optimizer):
                model, inputs, targets = fixture()
                reference = copy.deepcopy(model)
                trainer = CpuTrainer(model, partition(model), Config(optimizer=optimizer, purpose=PURPOSE))
                factory = torch.optim.SGD if optimizer == "sgd" else torch.optim.Adam
                opt = factory(reference.parameters(), lr=0.01, foreach=False)
                for _ in range(3):
                    opt.zero_grad(set_to_none=True)
                    F.mse_loss(reference(inputs), targets).backward()
                    expected = {n: p.grad.clone() for n, p in reference.named_parameters()}
                    opt.step()
                    trainer.advance(inputs, targets)
                    self.assertModels(model, reference)
                    for name, grad in expected.items():
                        torch.testing.assert_close(trainer.last_gradients[name], grad)

    def test_recomputed_gradients_updates_rng_and_global_clip_match(self):
        for optimizer in ("sgd", "adam"):
            for dropout in (False, True):
                for clip in (None, 0.03):
                    with self.subTest(optimizer=optimizer, dropout=dropout, clip=clip):
                        model, inputs, targets = fixture(dropout)
                        reference = copy.deepcopy(model)
                        baseline = CpuTrainer(reference, partition(reference), Config(optimizer=optimizer, max_grad_norm=clip))
                        streamed = CpuTrainer(model, partition(model), Config(mode="recompute_equivalent", optimizer=optimizer, max_grad_norm=clip))
                        for _ in range(3):
                            before = torch.get_rng_state().clone()
                            expected = baseline.advance(inputs, targets)
                            end = torch.get_rng_state().clone()
                            torch.set_rng_state(before)
                            actual = streamed.advance(inputs, targets)
                            self.assertTrue(torch.equal(torch.get_rng_state(), end))
                            self.assertModels(model, reference)
                            self.assertAlmostEqual(expected.gradient_norm, actual.gradient_norm, places=12)
                            self.assertEqual(actual.clipping_scope, "all_trainable")
                            for name, grad in baseline.last_gradients.items():
                                torch.testing.assert_close(streamed.last_gradients[name], grad, atol=2e-12, rtol=2e-11)

    def test_frozen_tail_keeps_early_layer_input_gradient(self):
        model, inputs, targets = fixture()
        reference = copy.deepcopy(model)
        F.mse_loss(reference(inputs), targets).backward()
        expected = reference[0].weight.grad.clone()
        trainer = CpuTrainer(model, partition(model), Config(mode="block_coordinate"))
        tail = model[3].weight.detach().clone()
        record = trainer.advance(inputs, targets)
        self.assertEqual(record.selected, ("0.weight",))
        self.assertGreater(float(expected.abs().sum()), 0)
        torch.testing.assert_close(trainer.last_gradients["0.weight"], expected)
        torch.testing.assert_close(model[3].weight, tail, rtol=0, atol=0)

    def test_coordinate_cycle_not_equal_to_full_update(self):
        model, inputs, targets = fixture()
        full = copy.deepcopy(model)
        coord = CpuTrainer(model, partition(model), Config(mode="block_coordinate", learning_rate=0.4))
        baseline = CpuTrainer(full, partition(full), Config(learning_rate=0.4))
        records = [coord.advance(inputs, targets) for _ in coord.blocks]
        baseline.advance(inputs, targets)
        self.assertEqual(coord.logical_step, 1)
        self.assertEqual(set(n for r in records for n in r.selected), set(coord.parameters))
        self.assertTrue(all(r.clipping_scope == "active_block" for r in records))
        self.assertTrue(any(not torch.allclose(p, q) for p, q in zip(model.parameters(), full.parameters())))

    def test_parameter_coverage_is_not_nonzero_gradient_coverage(self):
        model = nn.Sequential(nn.Linear(2, 2), nn.ReLU(), nn.Linear(2, 1)).double()
        with torch.no_grad():
            model[0].weight.zero_()
            model[0].bias.fill_(-1)
        trainer = CpuTrainer(model, partition(model), Config(mode="recompute_equivalent"))
        record = trainer.advance(torch.ones(2, 2, dtype=torch.float64), torch.ones(2, 1, dtype=torch.float64))
        self.assertIn("0.weight", record.selected)
        self.assertIn("0.weight", record.with_gradient)
        self.assertNotIn("0.weight", record.nonzero_gradient)
        self.assertIn("2.bias", record.nonzero_gradient)

    def test_lora_base_frozen_initial_equality_and_adapter_updates(self):
        torch.manual_seed(23)
        base = nn.Linear(3, 2).double()
        lora = LoRALinear(base, 2, 4)
        model = nn.Sequential(lora)
        inputs = torch.arange(12, dtype=torch.float64).reshape(4, 3) / 7
        target = torch.zeros(4, 2, dtype=torch.float64)
        torch.testing.assert_close(model(inputs), base(inputs), rtol=0, atol=0)
        trainer = CpuTrainer(model, partition(model), Config(mode="recompute_equivalent"))
        record = trainer.advance(inputs, target)
        self.assertEqual(set(record.selected), {"0.a", "0.b"})
        self.assertNotIn("0.a", record.nonzero_gradient)  # zero-B initialization
        self.assertIn("0.b", record.nonzero_gradient)
        torch.testing.assert_close(lora.base.weight, base.weight, rtol=0, atol=0)
        self.assertFalse(lora.base.weight.requires_grad)

    def test_resume_repeats_adam_dropout_coordinate_cursor(self):
        for mode in ("conventional", "recompute_equivalent", "block_coordinate"):
            model, inputs, targets = fixture(True)
            trainer = CpuTrainer(model, partition(model), Config(mode=mode, optimizer="adam"))
            trainer.advance(inputs, targets)
            snapshot = trainer.snapshot()
            expected = trainer.advance(inputs, targets)
            expected_parameters = copy.deepcopy(model)
            restored_model, _, _ = fixture(True)
            restored = CpuTrainer(restored_model, partition(restored_model), trainer.config)
            restored.restore(snapshot)
            actual = restored.advance(inputs, targets)
            self.assertEqual(actual, expected)
            self.assertModels(restored_model, expected_parameters)
            # Returned checkpoint does not alias current mutable optimizer/model state.
            self.assertEqual(snapshot["updates"], 1)

    def test_callback_receives_detached_snapshot_and_failure_stops(self):
        model, inputs, targets = fixture()
        seen = []
        trainer = CpuTrainer(model, partition(model), Config(), lambda state, record: seen.append((state, record)))
        trainer.advance(inputs, targets)
        original = model[0].weight.clone()
        seen[0][0]["model"]["0.weight"].zero_()
        torch.testing.assert_close(model[0].weight, original)
        self.assertEqual(seen[0][1].purpose, PURPOSE)
        def failure(state, record):
            raise IOError("immutable artifact publisher failed")
        trainer.on_update = failure
        with self.assertRaises(IOError):
            trainer.advance(inputs, targets)
        with self.assertRaises(RuntimeError):
            trainer.advance(inputs, targets)
        self.assertTrue(trainer.snapshot()["failed"])

    def test_cancellation_before_deferred_updates_restores_rng_and_flags(self):
        model, inputs, targets = fixture(True)
        unchanged = copy.deepcopy(model)
        trainer = CpuTrainer(model, partition(model), Config(mode="recompute_equivalent"))
        rng = torch.get_rng_state().clone()
        count = 0
        def cancelled():
            nonlocal count
            count += 1
            return count == 4
        with self.assertRaises(InterruptedError):
            trainer.advance(inputs, targets, cancelled=cancelled)
        self.assertModels(model, unchanged)
        self.assertTrue(torch.equal(rng, torch.get_rng_state()))
        self.assertTrue(all(p.requires_grad for p in model.parameters()))
        self.assertEqual(trainer.updates, 0)

    def test_reject_buffers_shared_parameters_missing_blocks_and_bad_config(self):
        model = nn.Sequential(nn.Linear(2, 2), nn.BatchNorm1d(2))
        with self.assertRaises(ValueError):
            CpuTrainer(model, partition(model), Config())
        shared = nn.Linear(2, 2)
        model = nn.Sequential(shared, shared)
        with self.assertRaises(ValueError):
            CpuTrainer(model, partition(model), Config())
        model, _, _ = fixture()
        with self.assertRaises(ValueError):
            CpuTrainer(model, (("0.weight",),), Config())
        for bad in (float("nan"), -1, 0):
            with self.assertRaises(ValueError):
                Config(learning_rate=bad)
        with self.assertRaises(ValueError):
            Config(optimizer="qlora")

    def test_nonfinite_loss_does_not_update(self):
        model, inputs, targets = fixture()
        unchanged = copy.deepcopy(model)
        trainer = CpuTrainer(model, partition(model), Config())
        with self.assertRaises(FloatingPointError):
            trainer.advance(inputs, targets + 1e308)
        self.assertModels(model, unchanged)

    def test_clipping_matches_independent_torch_reference(self):
        model, inputs, targets = fixture()
        reference = copy.deepcopy(model)
        opt = torch.optim.SGD(reference.parameters(), lr=0.01)
        F.mse_loss(reference(inputs), targets).backward()
        torch.nn.utils.clip_grad_norm_(reference.parameters(), max_norm=0.01, foreach=False)
        opt.step()
        trainer = CpuTrainer(model, partition(model), Config(mode="recompute_equivalent", max_grad_norm=0.01))
        trainer.advance(inputs, targets)
        self.assertModels(model, reference)

    def test_restore_rejects_same_shapes_but_different_activation_or_lora_scale(self):
        model, _, _ = fixture()
        trainer = CpuTrainer(model, partition(model), Config())
        state = trainer.snapshot()
        changed = copy.deepcopy(model)
        changed[1] = nn.Sigmoid()
        with self.assertRaises(ValueError):
            CpuTrainer(changed, partition(changed), Config()).restore(state)
        for alpha in (2, 3):
            model = nn.Sequential(LoRALinear(nn.Linear(2, 2).double(), 1, alpha))
            trainer = CpuTrainer(model, partition(model), Config())
            if alpha == 2:
                state = trainer.snapshot()
            else:
                with self.assertRaises(ValueError):
                    trainer.restore(state)

    def test_publication_interruption_does_not_allow_double_update(self):
        model, inputs, targets = fixture(True)
        def interrupt(state, record):
            raise InterruptedError("publisher interrupted after update")
        trainer = CpuTrainer(model, partition(model), Config(), interrupt)
        with self.assertRaises(InterruptedError):
            trainer.advance(inputs, targets)
        self.assertEqual(trainer.updates, 1)
        self.assertTrue(trainer.snapshot()["failed"])
        with self.assertRaises(RuntimeError):
            trainer.advance(inputs, targets)

    def test_scalar_configuration_rejects_bools_strings_tensors_and_objects(self):
        for value in (True, False, "0.01", object(), torch.tensor(0.1), float("inf"), 10**1000):
            with self.subTest(value=type(value).__name__):
                with self.assertRaises(ValueError):
                    Config(learning_rate=value)
                with self.assertRaises(ValueError):
                    Config(max_grad_norm=value)
                with self.assertRaises(ValueError):
                    LoRALinear(nn.Linear(2, 2), 1, value)

    def test_malformed_restore_is_transactional(self):
        model, inputs, targets = fixture(True)
        trainer = CpuTrainer(model, partition(model), Config(optimizer="adam"))
        trainer.advance(inputs, targets)
        saved = trainer.snapshot()
        # Distinct live weights/state ensure a partial restore would be detected.
        trainer.advance(inputs, targets)
        def field(name, value):
            return lambda state: state.__setitem__(name, value)
        def optimizer_group(key, value):
            return lambda state: state["optimizer"]["param_groups"][0].__setitem__(key, value)
        def moment(key, value):
            return lambda state: state["optimizer"]["state"][0].__setitem__(key, value)
        cases = {
            "rng_dtype": field("rng", saved["rng"].double()),
            "rng_shape": field("rng", saved["rng"][:-1]),
            "rng_contents": field("rng", torch.zeros_like(saved["rng"])),
            "counter_bool": field("updates", True),
            "counter_negative": field("updates", -1),
            "counter_fraction": field("logical_step", 0.5),
            "counter_inconsistent": field("logical_step", 0),
            "cursor_inconsistent": field("coordinate_cursor", 1),
            "mode_not_bool": field("training_modes", (1,) * len(saved["training_modes"])),
            "failed_not_bool": field("failed", 0),
            "group_lr": optimizer_group("lr", 123.0),
            "group_betas": optimizer_group("betas", (0.4, 0.5)),
            "group_reordered": optimizer_group("params", [3, 2, 1, 0]),
            "group_bool_coercion": optimizer_group("weight_decay", False),
            "group_extra": optimizer_group("unexpected", True),
            "moment_nan": moment("exp_avg", torch.full_like(saved["optimizer"]["state"][0]["exp_avg"], float("nan"))),
            "moment_shape": moment("exp_avg", torch.zeros(1, dtype=torch.float64)),
            "moment_negative_variance": moment("exp_avg_sq", -torch.ones_like(saved["optimizer"]["state"][0]["exp_avg_sq"])),
            "step_fraction": moment("step", torch.tensor(0.5)),
            "step_exceeds_schedule": moment("step", torch.tensor(3.0)),
            "step_wrong_shape": moment("step", torch.tensor([1.0])),
            "unknown_state": lambda state: state["optimizer"]["state"].__setitem__(999, {}),
            "missing_moment": lambda state: state["optimizer"]["state"][0].pop("exp_avg"),
            "missing_slot": lambda state: state["optimizer"]["state"].pop(0),
            "count_bool": lambda state: state["parameter_updates"].__setitem__("0.weight", True),
            "count_exceeds_schedule": lambda state: state["parameter_updates"].__setitem__("0.weight", 2),
            "count_zero_slot_present": lambda state: state["parameter_updates"].__setitem__("0.weight", 0),
        }
        for name, mutate in cases.items():
            with self.subTest(name=name):
                before = trainer.snapshot()
                before_gradients = copy.deepcopy(trainer.last_gradients)
                malformed = copy.deepcopy(saved)
                mutate(malformed)
                with self.assertRaises(ValueError):
                    trainer.restore(malformed)
                self.assertTreeEqual(before, trainer.snapshot())
                self.assertTreeEqual(before_gradients, trainer.last_gradients)

    def test_coordinate_restore_rejects_impossible_progress(self):
        model, inputs, targets = fixture(True)
        trainer = CpuTrainer(model, partition(model), Config(mode="block_coordinate", optimizer="adam"))
        trainer.advance(inputs, targets)
        for field, value in (("coordinate_cursor", 0), ("coordinate_cursor", 4), ("logical_step", 1)):
            before = trainer.snapshot()
            malformed = copy.deepcopy(before)
            malformed[field] = value
            with self.assertRaises(ValueError):
                trainer.restore(malformed)
            self.assertTreeEqual(before, trainer.snapshot())

    def test_restore_rolls_back_on_unexpected_model_commit_failure(self):
        from unittest.mock import patch
        model, inputs, targets = fixture()
        trainer = CpuTrainer(model, partition(model), Config(optimizer="adam"))
        saved = trainer.snapshot()
        trainer.advance(inputs, targets)
        before = trainer.snapshot()
        load = trainer.model.load_state_dict
        count = 0
        def fail_once(state, strict):
            nonlocal count
            count += 1
            if count == 1:
                with torch.no_grad():
                    trainer.model[0].weight.zero_()
                raise RuntimeError("injected partial commit failure")
            return load(state, strict=strict)
        with patch.object(trainer.model, "load_state_dict", side_effect=fail_once):
            with self.assertRaisesRegex(RuntimeError, "partial commit"):
                trainer.restore(saved)
        self.assertTreeEqual(before, trainer.snapshot())

    def test_failed_rollback_stops_executor(self):
        from unittest.mock import patch
        model, inputs, targets = fixture()
        trainer = CpuTrainer(model, partition(model), Config())
        saved = trainer.snapshot()
        with patch.object(model, "load_state_dict", side_effect=RuntimeError("unrecoverable copy failure")):
            with self.assertRaisesRegex(RuntimeError, "rollback failed"):
                trainer.restore(saved)
        self.assertTrue(trainer.snapshot()["failed"])
        with self.assertRaises(RuntimeError):
            trainer.advance(inputs, targets)

    def test_legitimate_missing_gradient_adam_state_roundtrips(self):
        model, inputs, targets = fixture()
        model[0].register_parameter("unused", nn.Parameter(torch.ones(1, dtype=torch.float64)))
        trainer = CpuTrainer(model, partition(model), Config(optimizer="adam"))
        trainer.advance(inputs, targets)
        saved = trainer.snapshot()
        self.assertEqual(saved["parameter_updates"]["0.unused"], 0)
        trainer.restore(saved)
        self.assertTreeEqual(saved, trainer.snapshot())


if __name__ == "__main__":
    unittest.main(verbosity=2)
