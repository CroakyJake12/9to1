"""Software-only numerical and continuation evidence for paged routed experts."""
import copy
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install
GUARD = install()
import numpy as np
import torch
from torch import nn

from alora.adapters import SelectedExperts, adapter_plan, trainable_adapters
from alora.capture import profile_row
from alora.gguf_store import MatrixRef
from alora.paged_experts import PagedExperts, attach_paged_experts
from alora.paging import PagedEmbedding
from alora.profile import ActivationProfiler
from alora.session import TrainingSession
from alora.test_support import NumericModel, IDENTITY, numeric_loss
import alora.test_data as data_fixtures


class ArrayStore:
    """Owned float32 numeric fixtures; never training data or a model publisher."""
    def __init__(self, model):
        self.arrays = {}
        for layer, block in enumerate(model.blocks):
            gu = block.experts.gate_up_proj.detach().float().cpu().numpy()
            down = block.experts.down_proj.detach().float().cpu().numpy()
            for name, value in (("gate", gu[:, :4]), ("up", gu[:, 4:]), ("down", down)):
                self.arrays[f"blk.{layer}.ffn_{name}_exps.weight"] = value.copy()
        self.arrays["token_embd.weight"] = model.embedding.weight.detach().float().numpy().copy()
        self.source_identity = "a" * 64
        self.reads = 0

    def tensor_info(self, name):
        return {"shape": tuple(self.arrays[name].shape), "qtype": "F32"}

    def matrix_ref(self, name, *, expert=None, row_start=0, row_count=None):
        value = self.arrays[name] if expert is None else self.arrays[name][expert]
        count = value.shape[0] - row_start if row_count is None else row_count
        return MatrixRef(name, expert, row_start, count, (count, value.shape[1]),
                         "F32", self.source_identity)

    def read(self, ref):
        if ref.source_identity != self.source_identity:
            raise ValueError("Software fixture source changed")
        self.reads += 1
        value = self.arrays[ref.tensor_name]
        if ref.expert is not None:
            value = value[ref.expert]
        return value[ref.row_start:ref.row_start + ref.row_count].copy()

    def gather_rows(self, name, indices):
        return self.arrays[name][indices].copy()


def paged_model(base, *, selected=True):
    model = copy.deepcopy(base)
    store = ArrayStore(base)
    model.requires_grad_(False)
    model.embedding = PagedEmbedding(store, "token_embd.weight")
    for layer, block in enumerate(model.blocks):
        block.experts = PagedExperts(store, layer, [0, 1, 2, 3] if selected and layer == 0 else (),
                                    rank=2, alpha=4)
    return model, store


class PagedExpertTests(unittest.TestCase):
    def setUp(self):
        torch.set_num_threads(1)
        torch.manual_seed(519)
        self.base = NumericModel().float()

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(GUARD.dulche_guard_calls, 0)

    def test_zero_adapters_match_native_output_input_and_router_gradients(self):
        store = ArrayStore(self.base)
        native = self.base.blocks[0].experts
        paged = PagedExperts(store, 0, [0, 2], rank=2, alpha=4)
        x = torch.randn(4, 8, requires_grad=True)
        routing = torch.tensor([[.4, .6], [.7, .3], [1., 0.], [0., 0.]], requires_grad=True)
        indices = torch.tensor([[0, 3], [1, 2], [2, 4], [4, 4]])
        expected = native(x, indices, routing)
        expected_grads = torch.autograd.grad(expected.square().sum(), (x, routing))
        y, weights = x.detach().clone().requires_grad_(), routing.detach().clone().requires_grad_()
        actual = paged(y, indices, weights)
        before = store.reads
        actual_grads = torch.autograd.grad(actual.square().sum(), (y, weights))
        self.assertGreater(store.reads, before)
        torch.testing.assert_close(actual, expected, rtol=2e-5, atol=2e-7)
        for a, b in zip(actual_grads, expected_grads):
            torch.testing.assert_close(a, b, rtol=3e-5, atol=3e-7)
        self.assertTrue(all("adapters." in name for name, _ in paged.named_parameters()))

    def test_nonzero_merge_matches_paged_output_and_preserves_source(self):
        store = ArrayStore(self.base)
        source = {name: value.copy() for name, value in store.arrays.items()}
        paged = PagedExperts(store, 0, [0, 2], rank=2, alpha=4)
        with torch.no_grad():
            for parameter in paged.adapters.parameters():
                parameter.uniform_(-.2, .2)
        native = copy.deepcopy(self.base.blocks[0].experts)
        with torch.no_grad():
            for expert, values in paged.merged_slices():
                native.gate_up_proj[expert].copy_(values["gate_up_proj"])
                native.down_proj[expert].copy_(values["down_proj"])
        x = torch.randn(3, 8)
        indices = torch.tensor([[0, 1], [2, 3], [0, 2]])
        routing = torch.full((3, 2), .5)
        torch.testing.assert_close(paged(x, indices, routing), native(x, indices, routing),
                                   rtol=3e-5, atol=3e-7)
        for name in source:
            np.testing.assert_array_equal(source[name], store.arrays[name])
        with torch.no_grad():
            paged.adapters["0"].down.b.fill_(float("nan"))
        with self.assertRaises(ValueError):
            list(paged.merged_slices())

    def test_earlier_adapters_train_through_later_frozen_paged_block_and_resume(self):
        first, source = paged_model(self.base)
        other, _ = paged_model(self.base)
        session = TrainingSession(first, identity=IDENTITY, purpose="software_test_only", learning_rate=.03)
        session.step_software(lambda: numeric_loss(session))
        snapshot = session.snapshot()
        resumed = TrainingSession(other, identity=IDENTITY, purpose="software_test_only", learning_rate=.03)
        resumed.restore(snapshot)
        session.step_software(lambda: numeric_loss(session))
        resumed.step_software(lambda: numeric_loss(resumed))
        self.assertEqual(session.updates, 2)
        self.assertEqual(resumed.updates, 2)
        self.assertEqual(session.last_record["parameter_after_sha256"],
                         resumed.last_record["parameter_after_sha256"])
        self.assertTrue(session.last_record["changed_parameters"])
        self.assertTrue(all(".adapters." in name for name in trainable_adapters(first)))
        self.assertEqual(session.identity["plan"]["blocks.1.experts"]["experts"], [])
        self.assertEqual(session.identity["plan"]["blocks.0.experts"]["base_source_identity"],
                         source.source_identity)

    def test_source_change_is_refused_between_forward_and_backward(self):
        store = ArrayStore(self.base)
        paged = PagedExperts(store, 0, [0], rank=2, alpha=4)
        x = torch.randn(2, 8, requires_grad=True)
        result = paged(x, torch.zeros(2, 1, dtype=torch.long), torch.ones(2, 1))
        store.source_identity = "b" * 64
        with self.assertRaisesRegex(ValueError, "source changed"):
            result.sum().backward()

    def test_pre_forward_source_change_and_session_base_substitution_are_refused(self):
        model, store = paged_model(self.base)
        wrong_identity = {**IDENTITY, "base_model_sha256": "b" * 64}
        with self.assertRaisesRegex(ValueError, "base identity"):
            TrainingSession(model, identity=wrong_identity, purpose="software_test_only")
        store.source_identity = "c" * 64
        with self.assertRaisesRegex(ValueError, "source changed"):
            model.blocks[0].experts(torch.ones(1, 8), torch.zeros(1, 1, dtype=torch.long),
                                    torch.ones(1, 1))
        self.assertEqual(store.reads, 0)
        with self.assertRaisesRegex(ValueError, "base identity"):
            TrainingSession(model, identity=IDENTITY, purpose="software_test_only")

    def test_calibration_capture_binds_source_then_selects_once(self):
        fixture = data_fixtures.TrainingDatasetTests(
            "test_only_training_rows_are_exposed_with_pinned_source_and_owned_snapshot")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        dataset = fixture.load()
        model, store = paged_model(self.base, selected=False)
        topology = {f"blocks.{layer}.experts": 4 for layer in range(2)}
        profiler = ActivationProfiler(topology, base_model_sha256=store.source_identity)
        staged = profile_row(model, dataset, next(dataset.rows()), profiler)
        self.assertEqual(staged.source_dataset, dataset.identity)
        for path in topology:
            self.assertEqual(staged.to_record()["layers"][path]["coding"]["valid_tokens"], 3)
        wrong = ActivationProfiler(topology, base_model_sha256="d" * 64)
        with self.assertRaisesRegex(ValueError, "exact admitted"):
            profile_row(model, dataset, next(dataset.rows()), wrong)
        wrappers = attach_paged_experts(model, {p: [1] for p in topology}, rank=2, alpha=4)
        self.assertEqual(set(wrappers), set(topology))
        self.assertEqual(len(trainable_adapters(model)), 8)
        with self.assertRaisesRegex(ValueError, "already been selected"):
            attach_paged_experts(model, {p: [2] for p in topology}, rank=2, alpha=4)
        with self.assertRaisesRegex(ValueError, "unchanged frozen base"):
            profile_row(model, dataset, next(dataset.rows()), profiler)

    def test_failed_selection_does_not_mutate_frozen_graph(self):
        model, _ = paged_model(self.base, selected=False)
        plan = {f"blocks.{layer}.experts": [0] for layer in range(2)}
        prior = adapter_plan({p: model.get_submodule(p) for p in plan})
        from alora.paged_experts import _ExpertAdapter
        calls = 0
        def fail_second(*args, **kwargs):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise MemoryError("software allocation fault")
            return _ExpertAdapter(*args, **kwargs)
        with patch("alora.paged_experts._ExpertAdapter", side_effect=fail_second):
            with self.assertRaises(MemoryError):
                attach_paged_experts(model, plan, rank=2, alpha=4)
        self.assertEqual(prior, adapter_plan({p: model.get_submodule(p) for p in plan}))
        self.assertFalse(any(p.requires_grad for p in model.parameters()))

    def test_invalid_dispatch_and_plans_fail_before_reads(self):
        store = ArrayStore(self.base)
        for ids in ([True], [-1], [4], [0, 0]):
            with self.assertRaises(ValueError):
                PagedExperts(store, 0, ids, rank=2, alpha=4)
        paged = PagedExperts(store, 0, [0], rank=2, alpha=4)
        for index, weight in ((-1, 1.), (5, 1.), (0, -.1), (0, float("nan"))):
            with self.subTest(index=index, weight=weight), self.assertRaises(ValueError):
                paged(torch.ones(1, 8), torch.tensor([[index]]), torch.tensor([[weight]]))
        self.assertEqual(store.reads, 0)
