"""Bounded-head loss equivalence and cooperative interruption software tests."""
import copy
import math
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install
GUARD = install()
import torch
from torch.nn import functional as F
from alora.compute import compute_scope
from alora.paged_loss import streaming_cross_entropy, native_causal_loss
from alora.paging import PagedLinear
from alora.session import ComputeInterrupted
from alora.test_gguf_model import _MemoryStore, _fixture
from alora.gguf_model import load_text_model


class PagedLossTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(412)
        torch.set_num_threads(1)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(GUARD.dulche_guard_calls, 0)

    def head(self, weight, *, rows=5, dtype=torch.float32):
        store = _MemoryStore({"output.weight": weight}, {"fixture": "software_test_only"})
        return PagedLinear(store, "output.weight", chunk_rows=rows, dtype=dtype), store

    def test_masked_mean_and_hidden_gradients_match_native_cross_entropy(self):
        for dtype in (torch.float32, torch.float64):
            with self.subTest(dtype=dtype):
                weight = torch.randn(29, 8) * .2
                head, _ = self.head(weight, dtype=dtype)
                hidden = torch.randn(2, 7, 8, dtype=dtype, requires_grad=True)
                target = torch.tensor([[0, 4, -100, 28, 9, 2, -100],
                                       [3, 19, 8, -100, 17, 0, 28]])
                expected_hidden = hidden.detach().clone().requires_grad_()
                expected = F.cross_entropy(F.linear(expected_hidden, weight.to(dtype)).float().reshape(-1, 29),
                                           target.reshape(-1), ignore_index=-100)
                expected.backward()
                actual = streaming_cross_entropy(hidden, head, target, token_rows=3)
                actual.backward()
                torch.testing.assert_close(actual, expected, rtol=2e-6, atol=1e-6)
                torch.testing.assert_close(hidden.grad, expected_hidden.grad, rtol=3e-5, atol=2e-7)
                self.assertEqual(torch.count_nonzero(hidden.grad[target == -100]).item(), 0)

    def test_score_tiles_are_bounded_and_weights_are_read_once_per_vocab_chunk_per_pass(self):
        weight = torch.randn(1003, 8) * .1
        head, store = self.head(weight, rows=37)
        hidden = torch.randn(17, 8, requires_grad=True)
        labels = torch.arange(17) * 59
        sizes = []
        original = F.linear
        def observe(x, w, *args, **kwargs):
            output = original(x, w, *args, **kwargs)
            sizes.append(tuple(output.shape))
            return output
        with patch("alora.paged_loss.F.linear", side_effect=observe):
            loss = streaming_cross_entropy(hidden, head, labels, token_rows=3)
            loss.backward()
        self.assertTrue(sizes)
        self.assertTrue(all(rows <= 3 and vocabulary <= 37 for rows, vocabulary in sizes))
        self.assertEqual(len(store.reads), 2 * math.ceil(1003 / 37))
        self.assertTrue(torch.isfinite(hidden.grad).all())

    def test_extreme_finite_logits_remain_stable(self):
        weight = torch.tensor([[1000., 1000.], [-1000., -1000.], [500., 500.], [999., 999.]])
        head, _ = self.head(weight, rows=2)
        hidden = torch.tensor([[1., 1.], [-1., -1.]], requires_grad=True)
        labels = torch.tensor([1, 0])
        expected_hidden = hidden.detach().clone().requires_grad_()
        expected = F.cross_entropy(F.linear(expected_hidden, weight), labels)
        actual = streaming_cross_entropy(hidden, head, labels, token_rows=1)
        expected.backward();actual.backward()
        torch.testing.assert_close(actual, expected, rtol=1e-6, atol=1e-5)
        torch.testing.assert_close(hidden.grad, expected_hidden.grad, rtol=1e-6, atol=1e-5)

    def test_backward_restores_captured_permit_scope_and_refuses_source_drift(self):
        head, store = self.head(torch.randn(11, 8))
        hidden = torch.randn(4, 8, requires_grad=True)
        permits = []
        with compute_scope(lambda: permits.append("permit")):
            loss = streaming_cross_entropy(hidden, head, torch.tensor([0, 3, 8, 10]), token_rows=2)
        before = len(permits)
        loss.backward()
        self.assertGreater(len(permits), before)
        hidden = hidden.detach().requires_grad_()
        loss = streaming_cross_entropy(hidden, head, torch.tensor([0, 3, 8, 10]))
        store.source_identity = "d" * 64
        with self.assertRaisesRegex(ValueError, "reference mismatch"):
            loss.backward()

    def test_mid_loss_permit_refusal_prevents_complete_output(self):
        head, store = self.head(torch.randn(41, 8), rows=4)
        calls = []
        def stop():
            calls.append(1)
            if len(calls) == 5:
                raise ComputeInterrupted("software checkpoint request")
        with compute_scope(stop), self.assertRaises(ComputeInterrupted):
            streaming_cross_entropy(torch.randn(9, 8, requires_grad=True), head,
                                    torch.arange(9), token_rows=2)
        self.assertLess(len(store.reads), math.ceil(41 / 4))

    def test_invalid_targets_empty_supervision_and_tile_budget_refuse_before_reads(self):
        head, store = self.head(torch.randn(29, 8))
        hidden = torch.randn(2, 8)
        for labels in (torch.tensor([-100, -100]), torch.tensor([-1, 0]), torch.tensor([29, 0])):
            with self.assertRaises(ValueError):
                streaming_cross_entropy(hidden, head, labels)
        with self.assertRaises(ValueError):
            streaming_cross_entropy(hidden, head, torch.tensor([1, 2]), token_rows=True)
        large_head, large_store = self.head(torch.randn(2, 8), rows=4096)
        with self.assertRaisesRegex(ValueError, "tile"):
            streaming_cross_entropy(hidden, large_head, torch.tensor([0, 1]), token_rows=4096)
        self.assertEqual(store.reads, [])
        self.assertEqual(large_store.reads, [])
        double_head, double_store = self.head(torch.randn(2, 8), rows=4096, dtype=torch.float64)
        with self.assertRaisesRegex(ValueError, "tile"):
            streaming_cross_entropy(hidden.double(), double_head, torch.tensor([0, 1]), token_rows=1024)
        self.assertEqual(double_store.reads, [])

    def test_native_causal_loss_matches_full_logits_and_preserves_router_auxiliary(self):
        for with_aux in (False, True):
            with self.subTest(router_auxiliary=with_aux):
                _, raw, store = _fixture()
                raw["output_router_logits"] = with_aux
                paths = {"model.layers.0.mlp.experts": [0, 1, 2]}
                torch.manual_seed(515)
                reference = load_text_model(store, raw, selections=paths, rank=2, alpha=4, chunk_rows=3)
                torch.manual_seed(515)
                streamed = load_text_model(store, raw, selections=paths, rank=2, alpha=4, chunk_rows=3)
                ids = torch.tensor([[3, 5, 2, 7, 9]])
                mask = torch.ones_like(ids)
                labels = torch.tensor([[-100, 5, -100, 7, 9]])
                expected = reference(input_ids=ids, attention_mask=mask, labels=labels, use_cache=False).loss
                actual = native_causal_loss(streamed, input_ids=ids, attention_mask=mask,
                                           labels=labels, token_rows=2)
                expected.backward();actual.backward()
                torch.testing.assert_close(actual, expected, rtol=2e-5, atol=2e-6)
                reference_parameters = dict(reference.named_parameters())
                for name, parameter in streamed.named_parameters():
                    if parameter.requires_grad:
                        self.assertIsNotNone(parameter.grad, name)
                        torch.testing.assert_close(parameter.grad, reference_parameters[name].grad,
                                                   rtol=3e-4, atol=2e-7)
                    else:
                        self.assertIsNone(parameter.grad)

    def test_higher_order_derivatives_are_refused(self):
        head, _ = self.head(torch.randn(7, 8))
        hidden = torch.randn(3, 8, requires_grad=True)
        loss = streaming_cross_entropy(hidden, head, torch.tensor([0, 3, 6]))
        first = torch.autograd.grad(loss, hidden, create_graph=True)[0]
        with self.assertRaises(RuntimeError):
            torch.autograd.grad(first.sum(), hidden)

    def test_calibration_skips_output_head_and_session_uses_streamed_loss(self):
        from alora.capture import profile_row
        from alora.profile import ActivationProfiler
        from alora.session import TrainingSession
        from alora.paged_experts import attach_paged_experts
        import alora.test_data as data_fixtures
        import alora.test_checkpoints as checkpoint_fixtures
        fixture = data_fixtures.TrainingDatasetTests(
            "test_only_training_rows_are_exposed_with_pinned_source_and_owned_snapshot")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        dataset = fixture.load()
        checkpoint = checkpoint_fixtures.CheckpointPublisherTests(
            "test_initial_completed_and_failed_snapshots_round_trip_with_bound_evidence")
        checkpoint.setUp()
        self.addCleanup(checkpoint.doCleanups)
        _, raw, store = _fixture()
        model = load_text_model(store, raw, rank=2, alpha=4, chunk_rows=3)
        topology = {f"model.layers.{i}.mlp.experts": 3 for i in range(2)}
        profiler = ActivationProfiler(topology, base_model_sha256=store.source_identity)
        row = next(dataset.rows())
        before = len(store.reads)
        with patch.object(model.lm_head, "forward", side_effect=AssertionError("Full logits were requested")):
            observed = profile_row(model, dataset, row, profiler)
            self.assertEqual(observed.source_dataset, dataset.identity)
            self.assertFalse(any(entry[0] == "output.weight" for entry in store.reads[before:]))
            attach_paged_experts(model, {path: [0, 1, 2] for path in topology}, rank=2, alpha=4)
            session = TrainingSession(model, identity={
                "base_model_sha256":store.source_identity,
                "dataset_manifest_sha256":dataset.manifest_sha256,
                "profile_sha256":"c"*64}, purpose="software_test_only", loss_token_rows=2)
            record, publication = session.step_row(dataset, row, checkpoint.publisher)
        self.assertEqual(record["updates"], 1)
        self.assertTrue(record["changed_parameters"])
        self.assertEqual(session.identity["loss"], {"kind":"streaming-causal-ce-v1", "token_rows":2})
        restored = checkpoint.publisher.load(publication)
        self.assertEqual(restored["updates"], 1)
