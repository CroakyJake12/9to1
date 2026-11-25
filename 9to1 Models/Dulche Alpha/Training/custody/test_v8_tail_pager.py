from contextlib import nullcontext
import hashlib
from importlib.util import find_spec
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from .artifacts import CustodyError
from .v8_tail_pager import PlacedV8Tail, _load_verified_pager, _require_tail_lane


class V8TailPagerTests(unittest.TestCase):
    def test_guard_rejects_fallback_and_unadmitted_modes(self):
        args = ("upper", "bfloat16", "cuda:0", (12, 0), 1)
        _require_tail_lane(*args)
        for i, bad in ((0, "rotate"), (0, "lower"), (1, "float16"), (2, "cpu"),
                       (2, "cuda:1"), (3, (7, 0)), (4, 2)):
            changed = list(args)
            changed[i] = bad
            with self.subTest(args=changed), self.assertRaises(CustodyError):
                _require_tail_lane(*changed)

    def test_unadmitted_source_will_never_execute(self):
        with TemporaryDirectory() as raw:
            path = Path(raw) / "paged_training.py"
            original = b"value = 123\n"
            path.write_bytes(original)
            with self.assertRaisesRegex(CustodyError, "does not match"):
                _load_verified_pager(path)
            with patch("custody.v8_tail_pager._PAGED_SHA256", hashlib.sha256(original).hexdigest()):
                self.assertEqual(_load_verified_pager(path).value, 123)
                path.write_bytes(b"raise AssertionError('must not execute')\n")
                with self.assertRaisesRegex(CustodyError, "does not match"):
                    _load_verified_pager(path)

    @unittest.skipUnless(find_spec("torch"), "requires installed PyTorch")
    def test_original_shift_mask_shape_and_gradient_selection(self):
        import torch

        class HeadSpy:
            @staticmethod
            def apply(hidden, labels, head):
                seen.append((labels.tolist(), head))
                return hidden.sum()

        seen, head = [], object()
        hidden = torch.arange(20, dtype=torch.float32).reshape(2, 5, 2).requires_grad_()
        labels = torch.tensor([[-100, -100, 3, 7, -100], [-100, 4, -100, -100, 5]])
        tail = PlacedV8Tail(None, head, (), None, None, HeadSpy, torch.device("cpu"))
        with patch.object(torch, "autocast", return_value=nullcontext()):
            tail.causal_loss(hidden, labels).backward()
            with self.assertRaisesRegex(ValueError, "no supervised"):
                tail.causal_loss(hidden, torch.full_like(labels, -100))
        self.assertEqual(seen, [([3, 7, 4, 5], head)])
        self.assertEqual(hidden.grad.ne(0).any(dim=-1).tolist(),
                         [[False, True, True, False, False], [True, False, False, True, False]])
