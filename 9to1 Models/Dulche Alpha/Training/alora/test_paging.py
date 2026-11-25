"""CPU-only numeric paging proofs; no model, user corpus or training signal."""

from dataclasses import dataclass
import gc
import math
import unittest
import weakref

import numpy as np

from hf_backend.cpu_guard import install

_GUARD = install()

import torch
from torch.nn import functional as F

from alora.paging import PagedEmbedding, PagedLinear, frozen_linear


@dataclass(frozen=True)
class _Ref:
    tensor_name: str
    expert: int | None
    row_start: int
    row_count: int
    shape: tuple[int, int]
    source_identity: str
    qtype: str = "F32"


class _Store:
    """Deterministic protocol spy with disposable decoded buffers, not a GGUF reader."""

    def __init__(self):
        self.shapes = {"linear": (7, 5), "experts": (3, 6, 5),
                       "embedding": (1_000_000_000, 6)}
        self.identity = "software-test-source-1"
        self.read_requests = []
        self.gather_requests = []
        self.reference_requests = []
        self.buffers = []
        self.cache = {}
        self.corruption = None
        self.max_ref_bytes = 16 * 1024 * 1024
        self.evictions = 0

    def tensor_info(self, name):
        shape = self.shapes[name]
        return {"shape": shape, "qtype": "F32", "bytes": math.prod(shape) * 4,
                "shard": "software-only-no-file"}

    def matrix_ref(self, name, expert=None, row_start=0, row_count=None):
        shape = self.shapes[name]
        if len(shape) == 3:
            if type(expert) is not int or not 0 <= expert < shape[0]:
                raise ValueError("invalid expert")
            rows, columns = shape[1:]
        elif len(shape) == 2 and expert is None:
            rows, columns = shape
        else:
            raise ValueError("invalid matrix selection")
        row_count = rows - row_start if row_count is None else row_count
        if (type(row_start) is not int or type(row_count) is not int
                or row_start < 0 or row_count <= 0 or row_start + row_count > rows):
            raise ValueError("invalid slice")
        if row_count * columns * 4 > self.max_ref_bytes:
            raise ValueError("decoded matrix budget exceeded")
        ref = _Ref(name, expert, row_start, row_count, (row_count, columns), self.identity)
        self.reference_requests.append(ref)
        return ref

    def expected(self, name, *, expert=None, row_start=0, row_count=None):
        shape = self.shapes[name]
        rows, columns = shape[-2:]
        count = rows - row_start if row_count is None else row_count
        ids = np.arange(row_start, row_start + count, dtype=np.int64)
        return self._values(ids, columns, expert)

    @staticmethod
    def _values(indices, columns, expert=None):
        rows = (np.asarray(indices, dtype=np.int64) % 97).astype(np.float32)[:, None] * 0.25
        cols = np.arange(columns, dtype=np.float32)[None, :] * 0.125
        return np.array(rows + cols + (0 if expert is None else expert * 0.5),
                        dtype=np.float32, order="C", copy=True)

    def _returned(self, array, key):
        if self.corruption == "source-nonfinite":
            raise ValueError("verified source contains nonfinite values")
        if self.corruption == "dtype":
            array = array.astype(np.float64)
        elif self.corruption == "shape":
            array = array[:, :-1].copy()
        elif self.corruption == "borrowed":
            array = array.view()
        elif self.corruption == "strides":
            array = array[:, ::-1]
        elif self.corruption == "unverified-nonfinite":
            array[0, 0] = np.nan
        elif self.corruption == "half-overflow":
            array[0, 0] = 65536.0
        self.cache[key] = array
        self.buffers.append(weakref.ref(array))
        return array

    def read(self, ref):
        if ref.source_identity != self.identity:
            raise ValueError("source identity changed")
        self.read_requests.append(ref)
        return self._returned(self.expected(
            ref.tensor_name, expert=ref.expert, row_start=ref.row_start, row_count=ref.row_count), ref)

    def gather_rows(self, name, indices):
        if not isinstance(indices, np.ndarray) or indices.dtype != np.int64 or indices.ndim != 1:
            raise ValueError("gather interface needs flat CPU int64 IDs")
        rows, columns = self.shapes[name]
        if np.any(indices < 0) or np.any(indices >= rows):
            raise ValueError("invalid embedding row")
        ids = tuple(int(index) for index in indices)
        self.gather_requests.append((name, ids))
        return self._returned(self._values(indices, columns), (name, ids))

    def evict(self):
        self.cache.clear()
        self.evictions += 1
        gc.collect()


class PagingTests(unittest.TestCase):
    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        state = torch.get_rng_state()
        self.addCleanup(torch.set_rng_state, state)
        torch.manual_seed(2409)
        self.store = _Store()

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def test_frozen_forward_and_input_gradient_match_native_linear(self):
        for dtype in (torch.float32, torch.float64):
            with self.subTest(dtype=dtype):
                store = _Store()
                ref = store.matrix_ref("linear")
                x = torch.randn(2, 3, 5, dtype=dtype, requires_grad=True)
                reference_x = x.detach().clone().requires_grad_()
                weight = torch.from_numpy(store.expected("linear")).to(dtype=dtype)
                actual = frozen_linear(x, store, ref)
                expected = F.linear(reference_x, weight)
                gradient = torch.randn_like(actual)
                torch.testing.assert_close(actual, expected)
                actual.backward(gradient)
                expected.backward(gradient)
                torch.testing.assert_close(x.grad, reference_x.grad)
                self.assertEqual(store.read_requests, [ref, ref])

    def test_context_retains_no_input_or_weight_and_backward_survives_eviction(self):
        ref = self.store.matrix_ref("linear")
        x = torch.randn(3, 5, requires_grad=True)
        result = frozen_linear(x, self.store, ref)
        context = result.grad_fn
        self.assertEqual(set(context.__dict__), {"store", "ref", "input_dtype", "input_device"})
        self.assertEqual(context.saved_tensors, ())
        self.assertFalse(any(isinstance(value, torch.Tensor) for value in context.__dict__.values()))
        self.store.evict()
        self.assertTrue(all(reference() is None for reference in self.store.buffers))
        result.sum().backward()
        expected = torch.from_numpy(self.store.expected("linear")).sum(0).expand_as(x)
        torch.testing.assert_close(x.grad, expected)
        self.assertEqual(len(self.store.read_requests), 2)
        self.store.evict()
        self.assertTrue(all(reference() is None for reference in self.store.buffers))

    def test_backward_refuses_changed_source_before_publishing_input_gradient(self):
        ref = self.store.matrix_ref("linear")
        x = torch.randn(2, 5, requires_grad=True)
        result = frozen_linear(x, self.store, ref)
        self.store.evict()
        self.store.identity = "different-software-source"
        with self.assertRaisesRegex(ValueError, "source identity changed"):
            result.sum().backward()
        self.assertIsNone(x.grad)

    def test_higher_order_derivatives_are_explicitly_unsupported(self):
        x = torch.randn(2, 5, requires_grad=True)
        result = frozen_linear(x, self.store, self.store.matrix_ref("linear"))
        gradient = torch.autograd.grad(result.square().sum(), x, create_graph=True)[0]
        with self.assertRaises(RuntimeError):
            torch.autograd.grad(gradient.sum(), x)

    def test_chunked_projection_preserves_order_expert_selection_and_input_gradients(self):
        layer = PagedLinear(self.store, "experts", expert=2, chunk_rows=2, dtype=torch.float64)
        x = torch.randn(2, 4, 5, dtype=torch.float64, requires_grad=True)
        reference_x = x.detach().clone().requires_grad_()
        weight = torch.from_numpy(self.store.expected("experts", expert=2)).double()
        actual, expected = layer(x), F.linear(reference_x, weight)
        torch.testing.assert_close(actual, expected)
        gradient = torch.randn_like(actual)
        self.store.evict()
        actual.backward(gradient)
        expected.backward(gradient)
        torch.testing.assert_close(x.grad, reference_x.grad, atol=1e-12, rtol=1e-12)
        self.assertEqual([(r.row_start, r.row_count) for r in self.store.read_requests[:3]],
                         [(0, 2), (2, 2), (4, 2)])
        self.assertEqual(len(self.store.read_requests), 6)
        self.assertTrue(all(r.expert == 2 and r.row_count <= 2 for r in self.store.read_requests))

    def test_constructor_never_requests_a_whole_matrix_over_the_store_budget(self):
        self.store.shapes["large"] = (1000, 512)
        self.store.max_ref_bytes = 16 * 1024
        with self.assertRaisesRegex(ValueError, "budget"):
            self.store.matrix_ref("large")
        layer = PagedLinear(self.store, "large", chunk_rows=4)
        self.assertEqual((layer.out_features, layer.in_features), (1000, 512))
        self.assertEqual(len(self.store.reference_requests), 250)
        self.assertTrue(all(ref.row_count <= 4 for ref in self.store.reference_requests))
        self.assertEqual(self.store.read_requests, [])

    def test_upstream_low_rank_parameters_update_through_the_frozen_paged_block(self):
        layer = PagedLinear(self.store, "linear", chunk_rows=3)
        a = torch.nn.Parameter(torch.randn(2, 5) * 0.1)
        b = torch.nn.Parameter(torch.randn(5, 2) * 0.1)
        optimizer = torch.optim.AdamW((a, b), lr=0.01, weight_decay=0)
        before = (a.detach().clone(), b.detach().clone())
        frozen = self.store.expected("linear")
        x = torch.randn(4, 5)
        hidden = x + F.linear(F.linear(x, a), b)
        loss = layer(hidden).square().mean()
        self.store.evict()
        loss.backward()
        for parameter in (a, b):
            self.assertIsNotNone(parameter.grad)
            self.assertTrue(torch.isfinite(parameter.grad).all())
            self.assertGreater(torch.count_nonzero(parameter.grad).item(), 0)
        optimizer.step()
        self.assertFalse(torch.equal(a, before[0]))
        self.assertFalse(torch.equal(b, before[1]))
        self.assertEqual(set(optimizer.state), {a, b})
        self.assertEqual(list(layer.parameters()), [])
        np.testing.assert_array_equal(self.store.expected("linear"), frozen)

    def test_only_zero_size_device_anchors_are_registered(self):
        for layer in (PagedLinear(self.store, "linear"), PagedEmbedding(self.store, "embedding")):
            self.assertEqual(list(layer.parameters()), [])
            buffers = list(layer.buffers())
            self.assertEqual(len(buffers), 1)
            self.assertEqual(buffers[0].numel(), 0)
            self.assertFalse(hasattr(layer, "weight"))
            self.assertEqual(layer.state_dict(), {})
            layer.to(dtype=torch.float64)
            self.assertEqual(layer.dtype, torch.float64)
            self.assertEqual(layer.device, torch.device("cpu"))
        self.assertEqual(self.store.read_requests, [])
        self.assertEqual(self.store.gather_requests, [])

    def test_linear_bad_inputs_refuse_before_source_reads(self):
        layer = PagedLinear(self.store, "linear")
        bad = (torch.randn(2, 4), torch.ones(2, 5, dtype=torch.int64),
               torch.randn(2, 5, dtype=torch.float64), torch.tensor(1.0),
               torch.empty(2, 5, device="meta"))
        for value in bad:
            with self.subTest(shape=value.shape, dtype=value.dtype), self.assertRaises(ValueError):
                layer(value)
        self.assertEqual(self.store.read_requests, [])

    def test_invalid_configuration_does_not_materialize_weights(self):
        for chunks in (0, -1, True, 2.0):
            with self.subTest(chunks=chunks), self.assertRaises(ValueError):
                PagedLinear(self.store, "linear", chunk_rows=chunks)
        for args in (
            {"name": "experts"}, {"name": "experts", "expert": True},
            {"name": "experts", "expert": 3}, {"name": "linear", "expert": 0},
            {"name": "linear", "dtype": torch.int64},
            {"name": "linear", "device": "meta"}, {"name": "linear", "device": 0},
        ):
            with self.subTest(args=args), self.assertRaises(ValueError):
                PagedLinear(self.store, **args)
        with self.assertRaises(ValueError):
            PagedEmbedding(self.store, "experts")
        self.assertEqual(self.store.read_requests, [])

    def test_huge_embedding_gathers_only_requested_rows_preserving_order_and_duplicates(self):
        layer = PagedEmbedding(self.store, "embedding", dtype=torch.float64)
        indices = torch.tensor([[999_999_999, 0], [999_999_999, 12345]], dtype=torch.int64)
        result = layer(indices)
        expected = torch.from_numpy(self.store._values(indices.numpy().reshape(-1), 6)).double().reshape(2, 2, 6)
        torch.testing.assert_close(result, expected)
        self.assertEqual(self.store.gather_requests,
                         [("embedding", (999_999_999, 0, 999_999_999, 12345))])
        self.assertEqual(layer.num_embeddings, 1_000_000_000)
        self.assertEqual(layer.embedding_dim, 6)
        self.assertFalse(result.requires_grad)
        self.assertEqual(self.store.read_requests, [])

    def test_scalar_and_empty_embedding_shapes_are_preserved(self):
        layer = PagedEmbedding(self.store, "embedding")
        scalar = layer(torch.tensor(7, dtype=torch.int32))
        self.assertEqual(tuple(scalar.shape), (6,))
        empty = layer(torch.empty((0, 2), dtype=torch.int64))
        self.assertEqual(tuple(empty.shape), (0, 2, 6))
        self.assertEqual(self.store.gather_requests, [("embedding", (7,))])

    def test_embedding_bad_ids_and_device_refuse_before_gather(self):
        layer = PagedEmbedding(self.store, "embedding")
        bad = (torch.tensor([-1]), torch.tensor([1_000_000_000]),
               torch.tensor([1.0]), torch.tensor([True]), torch.empty(2, dtype=torch.int64, device="meta"))
        for indices in bad:
            with self.subTest(dtype=indices.dtype), self.assertRaises(ValueError):
                layer(indices)
        self.assertEqual(self.store.gather_requests, [])

    def test_source_failures_and_invalid_decode_buffers_are_not_used(self):
        for corruption in ("source-nonfinite", "dtype", "shape", "borrowed", "strides", "unverified-nonfinite"):
            with self.subTest(corruption=corruption):
                store = _Store()
                store.corruption = corruption
                x = torch.randn(2, 5, requires_grad=True)
                with self.assertRaises(ValueError):
                    frozen_linear(x, store, store.matrix_ref("linear"))
                self.assertIsNone(x.grad)
                embedding = PagedEmbedding(store, "embedding")
                with self.assertRaises(ValueError):
                    embedding(torch.tensor([0, 1]))

    def test_finite_source_values_that_overflow_compute_dtype_are_refused(self):
        self.store.corruption = "half-overflow"
        layer = PagedLinear(self.store, "linear", dtype=torch.float16)
        with self.assertRaisesRegex(ValueError, "represented"):
            layer(torch.ones(2, 5, dtype=torch.float16))
        embedding = PagedEmbedding(self.store, "embedding", dtype=torch.float16)
        with self.assertRaisesRegex(ValueError, "represented"):
            embedding(torch.tensor([0, 1]))


if __name__ == "__main__":
    unittest.main()
