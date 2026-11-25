"""Software-only wide-vocabulary loss reduction regression.

The synthetic frozen head occupies less than 2 MiB. Its FP32 projections are
exactly one logit of 17 and 248,319 logits of zero for each of two hidden rows.
This isolates loss/probability accumulation from model, dataset and GEMM error.
Native FP32 CE error is retained as a diagnostic, never used as the truth oracle
and never required to be larger or smaller than the streamed implementation.
"""
from dataclasses import dataclass
import hashlib
import json
import math
import unittest

from hf_backend.cpu_guard import install
_GUARD = install()

import numpy as np
import torch
from torch.nn import functional as F

from alora.paged_loss import streaming_cross_entropy
from alora.paging import PagedLinear

_VOCABULARY = 248_320
_WIDTH = 2
_CHUNK_ROWS = 256
_LOSS_ATOL = 2e-6
_GRADIENT_ATOL = 2e-6
_ORACLE_ATOL = 1e-9  # FP64 reduction rounding, far below the FP32 candidate bound.


@dataclass(frozen=True)
class _Reference:
    tensor_name: str
    row_start: int
    shape: tuple[int, int]
    source_identity: str


class _SoftwareHead:
    """Owned numeric fixture implementing only the bounded matrix-store protocol."""
    purpose = "software_test_only"

    def __init__(self, dominant):
        self.weights = np.zeros((_VOCABULARY, _WIDTH), dtype=np.float32)
        self.weights[dominant, 0] = 17.0
        self.source_identity = hashlib.sha256(self.weights.tobytes()).hexdigest()
        self.read_count = 0
        self.max_decoded_bytes = 0

    def tensor_info(self, name):
        if name != "output.weight":
            raise ValueError("Unknown software-test tensor")
        return {"shape": tuple(self.weights.shape), "qtype": "F32",
                "bytes": self.weights.nbytes}

    def matrix_ref(self, name, *, expert=None, row_start=0, row_count=None):
        self.tensor_info(name)
        if expert is not None:
            raise ValueError("This software head has no experts")
        count = self.weights.shape[0] - row_start if row_count is None else row_count
        if (type(row_start) is not int or type(count) is not int
                or row_start < 0 or count < 1 or row_start + count > self.weights.shape[0]):
            raise ValueError("Invalid software-test row interval")
        return _Reference(name, row_start, (count, _WIDTH), self.source_identity)

    def read(self, reference):
        if (type(reference) is not _Reference
                or reference.source_identity != self.source_identity
                or reference.tensor_name != "output.weight"):
            raise ValueError("Software head reference changed")
        start, rows = reference.row_start, reference.shape[0]
        result = self.weights[start:start + rows].copy(order="C")
        if result.shape != reference.shape:
            raise ValueError("Software head shape changed")
        self.read_count += 1
        self.max_decoded_bytes = max(self.max_decoded_bytes, result.nbytes)
        return result


class WideVocabularyLossTests(unittest.TestCase):
    def setUp(self):
        torch.set_num_threads(1)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(_GUARD.dulche_guard_calls, 0)

    def test_wide_loss_and_hidden_gradient_against_fixed_score_fp64_oracle(self):
        # Dominant-first stresses repeated addition of tiny mass to an existing
        # large maximum. Dominant-last changes the maximum only in the last tile.
        for dominant in (0, _VOCABULARY - 1):
            with self.subTest(dominant_index=dominant):
                store = _SoftwareHead(dominant)
                head = PagedLinear(store, "output.weight", device="cpu",
                                   dtype=torch.float32, chunk_rows=_CHUNK_ROWS)
                weight = torch.from_numpy(store.weights)
                hidden = torch.tensor([[1.0, 0.0], [1.0, 1.0]],
                                      dtype=torch.float32, device="cpu", requires_grad=True)
                ordinary = 1 if dominant == 0 else 0
                targets = torch.tensor([dominant, ordinary], dtype=torch.long, device="cpu")

                # The oracle keeps the exact FP32 projection values fixed; only
                # CE/softmax and the final linear-Jacobian product use FP64.
                fixed_scores = F.linear(hidden.detach(), weight)
                self.assertEqual(fixed_scores.dtype, torch.float32)
                self.assertEqual(int(torch.count_nonzero(fixed_scores)), 2)
                self.assertTrue(torch.equal(fixed_scores[:, dominant],
                                            torch.full((2,), 17.0, device="cpu")))
                oracle_scores = fixed_scores.detach().double().requires_grad_()
                oracle_loss = F.cross_entropy(oracle_scores, targets)
                oracle_score_gradient, = torch.autograd.grad(oracle_loss, oracle_scores)
                oracle_hidden_gradient = oracle_score_gradient @ weight.double()

                # A closed-form check makes the FP64 oracle independently
                # inspectable for this fixture, including the mean reduction.
                small_mass = (_VOCABULARY - 1) * math.exp(-17.0)
                dominant_probability = 1.0 / (1.0 + small_mass)
                analytic_loss = 8.5 + math.log1p(small_mass)
                analytic_gradient = torch.tensor(
                    [[8.5 * (dominant_probability - 1.0), 0.0],
                     [8.5 * dominant_probability, 0.0]],
                    dtype=torch.float64, device="cpu")
                self.assertAlmostEqual(float(oracle_loss.detach()), analytic_loss, delta=_ORACLE_ATOL)
                torch.testing.assert_close(oracle_hidden_gradient, analytic_gradient,
                                           rtol=0, atol=_ORACLE_ATOL)

                native_hidden = hidden.detach().clone().requires_grad_()
                native_loss = F.cross_entropy(F.linear(native_hidden, weight), targets)
                native_loss.backward()
                streamed_loss = streaming_cross_entropy(hidden, head, targets, token_rows=2)
                streamed_loss.backward()

                self.assertTrue(torch.isfinite(native_loss))
                self.assertTrue(torch.isfinite(native_hidden.grad).all())
                self.assertTrue(torch.isfinite(streamed_loss))
                self.assertTrue(torch.isfinite(hidden.grad).all())
                oracle_value = float(oracle_loss.detach())
                native_error = abs(float(native_loss.detach()) - oracle_value)
                streamed_error = abs(float(streamed_loss.detach()) - oracle_value)
                native_gradient_error = float((native_hidden.grad.double() - oracle_hidden_gradient).abs().max())
                streamed_gradient_error = float((hidden.grad.double() - oracle_hidden_gradient).abs().max())
                diagnostic = {
                    "schema": 1, "purpose": "software_test_only",
                    "kind": "wide-vocabulary-fixed-fp32-score-loss",
                    "vocabulary": _VOCABULARY, "width": _WIDTH, "hidden_rows": 2,
                    "dominant_index": dominant, "dominant_logit": 17.0, "other_logit": 0.0,
                    "chunk_rows": _CHUNK_ROWS, "weight_bytes": store.weights.nbytes,
                    "oracle": "FP64 cross_entropy on fixed FP32 scores, checked against log1p closed form",
                    "oracle_loss": oracle_value, "analytic_loss": analytic_loss,
                    "oracle_closed_form_absolute_tolerance": _ORACLE_ATOL,
                    "native_fp32_loss": float(native_loss.detach()),
                    "streamed_loss": float(streamed_loss.detach()),
                    "native_fp32_loss_absolute_error": native_error,
                    "streamed_loss_absolute_error": streamed_error,
                    "native_fp32_hidden_gradient_max_absolute_error": native_gradient_error,
                    "streamed_hidden_gradient_max_absolute_error": streamed_gradient_error,
                    "candidate_loss_absolute_tolerance": _LOSS_ATOL,
                    "candidate_hidden_gradient_absolute_tolerance": _GRADIENT_ATOL,
                    "accuracy_ranking_assumed": False,
                    "source_reads": store.read_count, "max_decoded_bytes": store.max_decoded_bytes,
                    "cuda_initialization_attempts": _GUARD.dulche_guard_calls,
                }
                # Retained by the root's guarded unittest log even if the
                # candidate oracle tolerance fails and requires a math fix.
                print("ALORA_WIDE_LOSS_DIAGNOSTIC " + json.dumps(
                    diagnostic, sort_keys=True, allow_nan=False), flush=True)

                self.assertLess(store.weights.nbytes, 2 * 1024 * 1024)
                self.assertEqual(store.read_count, 2 * math.ceil(_VOCABULARY / _CHUNK_ROWS))
                self.assertLessEqual(store.max_decoded_bytes, _CHUNK_ROWS * _WIDTH * 4)
                self.assertLessEqual(streamed_error, _LOSS_ATOL,
                                     "Streamed loss exceeded the fixed-score FP64 oracle tolerance")
                torch.testing.assert_close(hidden.grad.double(), oracle_hidden_gradient,
                                           rtol=0, atol=_GRADIENT_ATOL)


if __name__ == "__main__":
    unittest.main()
