from dataclasses import replace
import unittest
from uuid import uuid4

from .artifacts import CustodyError
from .topology import TrainingLane
from .v8_joint_flash import JointFlashRelease, RankExitReceipt, RegisteredRank, rank_exit_mac


class JointFlashReleaseTests(unittest.TestCase):
    def setUp(self):
        self.attempt = str(uuid4())
        self.ranks = (
            RegisteredRank(0, TrainingLane(1, "legacy_vm", "guest", f"GPU-{uuid4()}", "0" * 64),
                           "guest-owner", 1000, 17, 444, "/guest-job.scope"),
            RegisteredRank(1, TrainingLane(0, "native", "host", f"GPU-{uuid4()}", "1" * 64),
                           "host-owner", 1000, 19, 555, "/host-job.scope"),
        )
        self.keys = (b"x" * 32, b"y" * 32)
        self.now = 100.0
        self.window = JointFlashRelease(attempt_id=self.attempt, ranks=self.ranks,
                                        authority_keys=self.keys, clock=lambda: self.now)

    def receipts(self):
        nonce, sha = self.window.challenge(committed_checkpoint_sha256="a" * 64)
        return tuple(self.sign(i, RankExitReceipt(self.attempt, nonce, sha, rank, True, True, (), True))
                     for i, rank in enumerate(self.ranks))

    def sign(self, i, receipt):
        return replace(receipt, mac=rank_exit_mac(receipt, self.keys[i]))

    def test_joint_both_exact_and_single_use(self):
        proofs = self.receipts()
        self.assertEqual(self.window.authorize(proofs), "a" * 64)
        with self.assertRaisesRegex(CustodyError, "fresh"):
            self.window.authorize(proofs)

    def test_partial_or_swapped_replayed_different_checkpoints_fail(self):
        for mode in ("partial", "swapped", "old", "lineage"):
            a, b = self.receipts()
            if mode == "partial":
                items = (a,)
            elif mode == "swapped":
                items = (b, a)
            elif mode == "old":
                self.receipts()
                items = (a, b)
            else:
                items = (a, self.sign(1, replace(b, checkpoint_sha256="b" * 64)))
            with self.subTest(mode=mode), self.assertRaises(CustodyError):
                self.window.authorize(items)

    def test_different_signing_authority_held_pid_wrong_process_and_stale_refused(self):
        cases = ({"gpu_compute_pids": (821,)}, {"original_process_exited": False},
                 {"rank_cgroup_empty": False}, {"fresh_guard_verified": False},
                 {"rank": replace(self.ranks[1], start_ticks=999)})
        for change in cases:
            a, b = self.receipts()
            with self.subTest(change=change), self.assertRaises(CustodyError):
                self.window.authorize((a, self.sign(1, replace(b, **change))))
        a, b = self.receipts()
        with self.assertRaises(CustodyError):
            self.window.authorize((a, self.sign(0, b)))
        proof = self.receipts()
        self.now += 5
        with self.assertRaises(CustodyError):
            self.window.authorize(proof)
