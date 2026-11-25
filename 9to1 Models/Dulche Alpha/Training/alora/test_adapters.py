import copy
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install
CUDA_REFUSAL = install()

import torch
from alora.adapters import SelectedExperts, attach_selected_experts, trainable_adapters
from alora.test_support import NumericModel, make_session, numeric_loss


class AdapterTests(unittest.TestCase):
    def setUp(self):
        torch.manual_seed(420)
        torch.set_num_threads(1)
        self.assertFalse(torch.cuda.is_initialized())

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def test_zero_adapter_matches_native_output_input_and_routing_gradients(self):
        native = NumericModel().blocks[0].experts
        wrapped = SelectedExperts(copy.deepcopy(native), [0, 2], rank=2, alpha=4)
        indices = torch.tensor([[0, 3], [1, 2], [2, 4], [4, 4]], dtype=torch.long)
        x = torch.randn(4, 8, dtype=torch.float64, requires_grad=True)
        weights = torch.tensor([[.4, .6], [.7, .3], [1., 0.], [0., 0.]], dtype=torch.float64, requires_grad=True)
        reference = native(x, indices, weights)
        reference_grads = torch.autograd.grad(reference.square().sum(), (x, weights))
        other_x, other_weights = x.detach().clone().requires_grad_(), weights.detach().clone().requires_grad_()
        actual = wrapped(other_x, indices, other_weights)
        actual_grads = torch.autograd.grad(actual.square().sum(), (other_x, other_weights))
        torch.testing.assert_close(actual, reference, atol=1e-13, rtol=1e-12)
        for actual_grad, reference_grad in zip(actual_grads, reference_grads):
            torch.testing.assert_close(actual_grad, reference_grad, atol=1e-13, rtol=1e-12)

    def test_sparse_parameter_and_optimizer_allocation_only_selected_experts(self):
        model = NumericModel()
        base = {n: p.clone() for n, p in model.named_parameters()}
        original_refs = dict(model.named_parameters())
        attach_selected_experts(model, {"blocks.0.experts": [0, 2]}, rank=2, alpha=4)
        parameters = trainable_adapters(model)
        self.assertEqual(len(parameters), 8)
        self.assertEqual(sum(p.numel() for p in parameters.values()), 2 * 2 * (2 * 8 + 3 * 4))
        optimizer = torch.optim.AdamW(parameters.values(), lr=.02, weight_decay=0)
        # Route only expert0. Selected expert2 is dormant and needs no Adam slots.
        module = model.blocks[0].experts
        output = module(torch.randn(3, 8, dtype=torch.float64),
                        torch.zeros(3, 1, dtype=torch.long), torch.ones(3, 1, dtype=torch.float64))
        output.square().sum().backward()
        optimizer.step()
        self.assertTrue(any(torch.count_nonzero(p).item() for n, p in parameters.items() if ".0." in n and n.endswith(".b")))
        for name, parameter in parameters.items():
            self.assertEqual(parameter in optimizer.state, ".adapters.0." in name)
        for name, parameter in original_refs.items():
            self.assertTrue(torch.equal(parameter, base[name]), name)
            self.assertIsNone(parameter.grad)
            self.assertFalse(parameter.requires_grad)

    def test_earlier_adapter_gradients_cross_later_frozen_experts_and_router(self):
        session = make_session()
        original = {n: p.clone() for n, p in session.model.named_parameters() if not p.requires_grad}
        record, _ = session.step_software(lambda: numeric_loss(session))
        self.assertGreater(len(record["changed_parameters"]), 0)
        self.assertNotEqual(record["parameter_before_sha256"], record["parameter_after_sha256"])
        for name, p in session.model.named_parameters():
            if name in original:
                self.assertTrue(torch.equal(p, original[name]), name)
                self.assertIsNone(p.grad)

    def test_nonzero_merged_slices_match_native_without_mutating_base(self):
        original = NumericModel().blocks[0].experts
        native = copy.deepcopy(original)
        wrapped = SelectedExperts(original, [1, 3], rank=2, alpha=3)
        with torch.no_grad():
            for adapter in wrapped.adapters.values():
                adapter.gate_up.b.normal_(0, .1)
                adapter.down.b.normal_(0, .1)
        before = {n: p.clone() for n, p in original.named_parameters()}
        slices = list(wrapped.merged_slices())
        self.assertEqual([e for e, _ in slices], [1, 3])
        with torch.no_grad():
            for expert, weights in slices:
                native.gate_up_proj[expert].copy_(weights["gate_up_proj"])
                native.down_proj[expert].copy_(weights["down_proj"])
        indices = torch.tensor([[0, 1], [2, 3], [1, 3]], dtype=torch.long)
        weights = torch.tensor([[.3, .7], [.4, .6], [.8, .2]], dtype=torch.float64)
        x = torch.randn(3, 8, dtype=torch.float64, requires_grad=True)
        other_x = x.detach().clone().requires_grad_()
        a, b = wrapped(x, indices, weights), native(other_x, indices, weights)
        torch.testing.assert_close(a, b, rtol=1e-11, atol=1e-13)
        torch.testing.assert_close(torch.autograd.grad(a.sum(), x)[0],
                                   torch.autograd.grad(b.sum(), other_x)[0], rtol=1e-11, atol=1e-13)
        for name, p in original.named_parameters():
            self.assertTrue(torch.equal(p, before[name]))
        with torch.no_grad():
            wrapped.adapters["1"].down.b.fill_(float("nan"))
        with self.assertRaises(ValueError):
            list(wrapped.merged_slices())

    def test_finite_adapter_values_cannot_export_overflowed_merged_slices(self):
        for dtype, magnitude in ((torch.float32, 1e20), (torch.float16, 1e4)):
            native = NumericModel().blocks[0].experts.to(dtype=dtype)
            wrapper = SelectedExperts(native, [0], rank=2, alpha=4)
            with torch.no_grad():
                for parameter in wrapper.adapters.parameters():
                    parameter.fill_(magnitude)
            self.assertTrue(all(torch.isfinite(p).all().item() for p in wrapper.adapters.parameters()))
            with self.subTest(dtype=dtype), self.assertRaises(ValueError):
                list(wrapper.merged_slices())

    def test_invalid_plan_does_not_freeze_or_replace_any_module(self):
        for selection in ({"blocks.0.experts": [0], "missing": [1]},
                          {"blocks.0.experts": [0, 0]}, {"blocks.0.experts": [4]}):
            model = NumericModel()
            prior = dict(model.named_parameters())
            with self.assertRaises(ValueError):
                attach_selected_experts(model, selection, rank=2, alpha=4)
            self.assertFalse(any(isinstance(m, SelectedExperts) for m in model.modules()))
            self.assertTrue(all(p.requires_grad for p in prior.values()))

    def test_failed_staging_and_repeat_attachment_preserve_model(self):
        model = NumericModel()
        # Keep isinstance using the actual class; fault at adapter allocation instead.
        from alora import adapters
        real_adapter = adapters._ExpertAdapter
        calls = 0
        def fail_second(*args, **kwargs):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise MemoryError("software allocation fault")
            return real_adapter(*args, **kwargs)
        with patch("alora.adapters._ExpertAdapter", side_effect=fail_second):
            with self.assertRaises(MemoryError):
                attach_selected_experts(model, {"blocks.0.experts": [0], "blocks.1.experts": [1]},
                                        rank=2, alpha=4)
        self.assertTrue(all(p.requires_grad for p in model.parameters()))
        self.assertFalse(any(isinstance(m, SelectedExperts) for m in model.modules()))
        attach_selected_experts(model, {"blocks.0.experts": [0]}, rank=2, alpha=4)
        parameters = {n: p.requires_grad for n, p in model.named_parameters()}
        with self.assertRaises(ValueError):
            attach_selected_experts(model, {"blocks.1.experts": [1]}, rank=2, alpha=4)
        self.assertEqual(parameters, {n: p.requires_grad for n, p in model.named_parameters()})

    def test_inference_hooks_aliases_and_bad_dispatch_are_refused(self):
        model = NumericModel()
        model._hf_hook = object()
        with self.assertRaises(ValueError):
            attach_selected_experts(model, {"blocks.0.experts": [0]}, rank=2, alpha=4)
        del model._hf_hook
        model.other = model.blocks[0].experts
        with self.assertRaises(ValueError):
            attach_selected_experts(model, {"blocks.0.experts": [0]}, rank=2, alpha=4)
        wrapper = SelectedExperts(NumericModel().blocks[0].experts, [0], rank=2, alpha=4)
        for index, weight in ((-1, 1.), (5, 1.), (0, float("nan")), (0, -.1)):
            with self.subTest(index=index, weight=weight), self.assertRaises(ValueError):
                wrapper(torch.ones(1, 8, dtype=torch.float64), torch.tensor([[index]]),
                        torch.tensor([[weight]], dtype=torch.float64))
