import json
import unittest
from uuid import uuid4

from custody.artifacts import CustodyError
from custody.dual_qwen_smoke import _MARKER, _receipts


class TinyQwenReceiptTests(unittest.TestCase):
    def test_two_rank_receipts_fail_closed_for_stale_identity_or_nonfinite(self):
        attempt = str(uuid4())
        uuids = ("GPU-00000000-0000-0000-0000-000000000001", "GPU-00000000-0000-0000-0000-000000000002")
        rows = [{"attempt": attempt, "rank": rank, "gpu_uuid": uuid, "compute_capability": [cc, 0],
                 "fixture": "tiny-native-qwen35-v1", "fixture_sha256": "a" * 64, "dtype": "float32", "torch": "test-only",
                 "owned_gradients": 47, "max_abs_gradient_error": 0.0, "loss": None if rank == 0 else 1.25}
                for rank, (uuid, cc) in enumerate(zip(uuids, (12, 7)))]

        def outcome(row):
            return 0, _MARKER + json.dumps(row).encode()

        self.assertEqual(_receipts(attempt, uuids, tuple(map(outcome, rows))), rows)
        for changes in ({"gpu_uuid": uuids[0]}, {"attempt": str(uuid4())}, {"rank": 0},
                        {"fixture_sha256": "b" * 64}, {"loss": float("nan")}, {"owned_gradients": 0},
                        {"max_abs_gradient_error": float("inf")}):
            with self.assertRaises(CustodyError):
                _receipts(attempt, uuids, (outcome(rows[0]), outcome(rows[1] | changes)))
