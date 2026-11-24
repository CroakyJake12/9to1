from concurrent.futures import ThreadPoolExecutor
from dataclasses import replace
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from custody.artifacts import ArtifactStore, CustodyError, write_record
from custody.rank_checkpoint import RankStageReceipt, TwoRankCheckpoint


class TwoRankCheckpointTests(unittest.TestCase):
    def setUp(self):
        tmp = TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.store = ArtifactStore(Path(tmp.name) / "private")
        run = self.store.create_run("CPU custody fixture", {})
        origin = self.store.begin_attempt(run, {})
        self.base = self.store.publish_bytes(origin, "previous", "checkpoint", b"old")
        self.attempt = self.store.begin_attempt(run, {}, resume_from=(self.base,))
        self.owners = ("native-owner", "guest-owner")
        self.receipts = self.stage(1, self.base)

    def stage(self, step, base):
        return tuple(RankStageReceipt(self.attempt, step, base, rank, owner,
                     self.store.publish_bytes(self.attempt, f"fragment-{rank}", "segment_checkpoint",
                                              f"synthetic {step}/{rank}".encode(), inputs=(base,)))
                     for rank, owner in enumerate(self.owners))

    def coordinator(self, step=1, base=None):
        return TwoRankCheckpoint(self.store, self.attempt, step, base or self.base, self.owners)

    def test_no_partial_result_and_both_links_verify_after_owner_restart(self):
        first = self.coordinator()
        self.assertIsNone(first.committed())
        result = first.commit(self.receipts)
        self.assertEqual(self.coordinator().committed(), result)
        second = self.coordinator(2, result)
        self.assertEqual(second.commit(self.stage(2, result)), second.committed())

    def test_missing_stale_swapped_duplicate_and_out_of_order_have_no_result(self):
        origin = self.coordinator()
        a, b = self.receipts
        for receipts in ((a,), (a, replace(b, step=2)), (b, a), (a, a),
                         (a, replace(b, fragment=a.fragment)), (a, replace(b, ownership_id="stale"))):
            with self.subTest(receipts=receipts), self.assertRaises(CustodyError):
                origin.commit(receipts)
        with self.assertRaisesRegex(CustodyError, "preceding step"):
            self.coordinator(2).commit(self.stage(2, self.base))
        self.assertFalse(origin.claim.exists())
        self.assertIsNone(origin.committed())
        self.assertEqual(self.store.verify(self.base).read_bytes(), b"old")

    def test_bad_fragment_stops_before_commit(self):
        self.store.verify(self.receipts[1].fragment).write_bytes(b"corrupt")
        first = self.coordinator()
        with self.assertRaises(CustodyError):
            first.commit(self.receipts)
        self.assertFalse(first.claim.exists())
        self.assertIsNone(first.committed())

    def test_predecessor_kind_and_exact_declared_step_one_base(self):
        original = self.base
        for kind, declare, expected in (("log", True, "artifact kind"),
                                         ("checkpoint", False, "sole declared")):
            with self.subTest(kind=kind, declare=declare):
                base = self.store.publish_bytes(original.attempt, "alternate", kind, b"alternate")
                self.attempt = self.store.begin_attempt(original.run_id, {}, resume_from=(base if declare else original,))
                first = self.coordinator(base=base)
                with self.assertRaisesRegex(CustodyError, expected):
                    first.commit(self.stage(1, base))
                self.assertFalse(first.claim.exists())

    def test_segment_kind_and_sealed_input_are_required_for_each_rank(self):
        a, b = self.receipts
        for kind, inputs, expected in (("log", (self.base,), "artifact kind"),
                                       ("segment_checkpoint", (), "artifact inputs")):
            with self.subTest(kind=kind, inputs=inputs):
                other = self.store.publish_bytes(self.attempt, "alternate", kind, b"alternate", inputs=inputs)
                first = self.coordinator()
                with self.assertRaisesRegex(CustodyError, expected):
                    first.commit((a, replace(b, fragment=other)))
                self.assertFalse(first.claim.exists())

    def test_result_of_wrong_kind_never_becomes_visible(self):
        coordinator = self.coordinator()
        publish = self.store.publish_bytes
        def mislabeled(attempt, name, kind, data, **kwargs):
            return publish(attempt, name, "log", data, **kwargs)
        with patch.object(self.store, "publish_bytes", side_effect=mislabeled):
            with self.assertRaisesRegex(CustodyError, "outcome is uncertain") as caught:
                coordinator.commit(self.receipts)
        self.assertRegex(str(caught.exception.__cause__), "artifact kind")
        self.assertTrue(coordinator.claim.exists())
        self.assertIsNone(coordinator.committed())

    def test_failure_after_artifact_seal_does_not_expose_or_replay(self):
        live = self.coordinator()
        def fail_commit_pointer(path, record):
            if path == live.commit_record:
                raise OSError("simulated publisher interruption")
            write_record(path, record)
        with patch("custody.rank_checkpoint.write_record", side_effect=fail_commit_pointer):
            with self.assertRaisesRegex(CustodyError, "outcome is uncertain"):
                live.commit(self.receipts)
        self.assertIsNone(self.coordinator().committed())
        self.assertEqual(self.store.verify(self.base).read_bytes(), b"old")
        with self.assertRaisesRegex(CustodyError, "do not replay"):
            self.coordinator().commit(self.receipts)

    def test_uncertain_ack_after_pointer_recovers_result_without_replay(self):
        live = self.coordinator()
        def lose_ack(path, record):
            write_record(path, record)
            if path == live.commit_record:
                raise OSError("simulated lost acknowledgement")
        with patch("custody.rank_checkpoint.write_record", side_effect=lose_ack):
            with self.assertRaisesRegex(CustodyError, "outcome is uncertain"):
                live.commit(self.receipts)
        self.assertIsNotNone(self.coordinator().committed())
        with self.assertRaisesRegex(CustodyError, "do not replay"):
            self.coordinator().commit(self.receipts)

    def test_restart_accepts_only_the_committed_joint_evidence(self):
        first = self.coordinator()
        real_write = write_record

        def fail_pointer(path, record):
            if path == first.commit_record:
                raise OSError("pointer unavailable")
            real_write(path, record)

        with patch("custody.rank_checkpoint.write_record", side_effect=fail_pointer):
            with self.assertRaisesRegex(CustodyError, "outcome is uncertain"):
                first.commit(self.receipts)
        sealed = set(self.store.verify_attempt(self.attempt).artifacts)
        orphan, = sealed - {receipt.fragment for receipt in self.receipts}
        self.attempt = self.store.begin_attempt(orphan.run_id, {}, resume_from=(orphan,))
        with self.assertRaisesRegex(CustodyError, "exact committed pointer"):
            self.coordinator(base=orphan).commit(self.stage(1, orphan))

        self.attempt = self.store.begin_attempt(orphan.run_id, {}, resume_from=(self.base,))
        complete = self.coordinator().commit(self.stage(1, self.base))
        self.attempt = self.store.begin_attempt(complete.run_id, {}, resume_from=(complete,))
        retry = self.coordinator(base=complete)
        self.assertEqual(retry.commit(self.stage(1, complete)), retry.committed())

    def test_concurrent_owners_cannot_create_two_results(self):
        def publish():
            try:
                return self.coordinator().commit(self.receipts)
            except CustodyError:
                return None
        with ThreadPoolExecutor(max_workers=2) as pool:
            outcomes = tuple(pool.map(lambda _: publish(), range(2)))
        self.assertEqual([r for r in outcomes if r is not None], [self.coordinator().committed()])
