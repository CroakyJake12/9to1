from dataclasses import replace
import hashlib
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import Mock, patch

from .artifacts import ArtifactStore, CustodyError, _read_record
from .rank_checkpoint import TwoRankCheckpoint
from .rank_grad_clip import ClipDecision, ClipPlan, GradientSlot, NormReceipt, NormSlot
from .rank_sft import _claim_path
from .v8_frozen_receipt import FrozenV100Receipt, stage_frozen_v100


class GuestReceiptTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        store = ArtifactStore(Path(tmp.name) / "custody")
        origin = store.begin_attempt(store.create_run("synthetic frozen rank", {}), {})
        base = store.publish_bytes(origin, "base", "checkpoint", b"prior")
        attempt = store.begin_attempt(origin.run_id, {}, resume_from=(base,))
        self.owners = ("guest-v100", "host-rtx")
        self.checkpoint = TwoRankCheckpoint(store, attempt, 1, base, self.owners)
        self.plan = ClipPlan(attempt.attempt_id, 1, self.owners, (GradientSlot(1, "adapter"),))
        self.rng = b"synthetic-cuda-rng"
        self.cpu_rng = b"synthetic-cpu-rng"
        self.receipt = FrozenV100Receipt(attempt.attempt_id, 1, self.owners[0], 3, "f" * 64,
                                         hashlib.sha256(self.rng).hexdigest(),
                                         hashlib.sha256(self.cpu_rng).hexdigest())
        self.decision = ClipDecision((
            NormReceipt(attempt.attempt_id, 1, 0, self.owners[0], ()),
            NormReceipt(attempt.attempt_id, 1, 1, self.owners[1], (NormSlot("adapter", "float32", 2.0),)),
        ), "float32", 2.0)
        self.auth = Mock(return_value=True)

    def stage(self, **overrides):
        kwargs = {"receipt": self.receipt, "cuda_rng": self.rng, "cpu_rng": self.cpu_rng, "grad_accum": 3,
                  "authenticate_completion": self.auth} | overrides
        with patch("custody.v8_frozen_receipt.decide_global_clip", return_value=self.decision):
            return stage_frozen_v100(self.checkpoint, self.plan, self.decision, **kwargs)

    def test_verified_guest_stage_is_not_progress_and_cannot_replay(self):
        staged = self.stage()
        self.auth.assert_called_once_with(self.receipt, self.rng, self.cpu_rng)
        self.assertEqual(staged.staged.rank, 0)
        self.assertIsNone(staged.updated_parameter)
        self.assertEqual(staged.max_abs_update, 0.0)
        data = _read_record(self.checkpoint.store.verify(staged.staged.fragment))
        self.assertEqual(data["evidence"]["cuda_rng_sha256"], self.receipt.cuda_rng_sha256)
        self.assertIsNone(self.checkpoint.committed())
        with self.assertRaisesRegex(CustodyError, "already claimed"):
            self.stage()

    def test_short_foreign_or_unauthenticated_evidence_never_claims(self):
        for receipt in (replace(self.receipt, step=2), replace(self.receipt, completed_microbatches=2),
                        replace(self.receipt, ownership_id="other")):
            with self.assertRaisesRegex(CustodyError, "invalid"):
                self.stage(receipt=receipt)
        with self.assertRaisesRegex(CustodyError, "invalid"):
            self.stage(cuda_rng=b"wrong")
        self.auth.assert_not_called()
        self.auth.return_value = 1
        with self.assertRaisesRegex(CustodyError, "not authenticated"):
            self.stage()
        self.assertFalse(_claim_path(self.checkpoint, self.plan, 0).exists())
        self.assertIsNone(self.checkpoint.committed())
