from copy import deepcopy
from dataclasses import replace
import hashlib
import unittest
from uuid import uuid4

from .artifacts import CustodyError, _encode_record
from .v8_joint_registration import (
    DATASET_MANIFEST_SHA256, LEGACY_RECIPE_SHA256, PROTOCOL, SCHEMA, WORKER_ID,
    RankProcessReceipt, _GPU_ROWS, _SETTINGS, verify_joint_registration, verify_live_joint_processes,
)


def digest(record):
    return hashlib.sha256(_encode_record(record)).hexdigest()


class V8JointRegistrationTests(unittest.TestCase):
    def setUp(self):
        self.document = dict(schema=SCHEMA, purpose="real_training", id=WORKER_ID, protocol=PROTOCOL,
                             source_recipe_sha256=LEGACY_RECIPE_SHA256,
                             dataset_manifest_sha256=DATASET_MANIFEST_SHA256,
                             pager_sha256="c"*64, cutoff_sha256="d"*64, tokenizer_sha256="e"*64,
                             rank_manifest_sha256=["a"*64, "b"*64], settings=dict(_SETTINGS),
                             ranks=[dict(logical_rank=r, physical_lane=p, kind=k, gpu_uuid=g,
                                         runtime_sha256=f"{r}"*64, bundle_sha256=f"{p}"*64)
                                    for r, p, k, g in _GPU_ROWS])

    def test_legacy_swapped_unpinned_or_changed_approved_procedure_rejected(self):
        original = digest(self.document)
        self.assertEqual(verify_joint_registration(self.document, trusted_record_sha256=original).digest, original)
        for mode in ("device field", "swapped", "wrong digest", "training setting"):
            record = deepcopy(self.document)
            if mode == "device field":
                record["device"] = "GPU-legacy"
            elif mode == "swapped":
                record["ranks"].reverse()
            elif mode == "training setting":
                record["settings"]["grad_accum"] = 1
            with self.subTest(mode=mode), self.assertRaises(CustodyError):
                verify_joint_registration(record, trusted_record_sha256=(
                    "0" * 64 if mode == "wrong digest" else digest(record)))

    def test_authenticate_actual_guest_and_native_not_wrapper_or_scalar(self):
        admission = verify_joint_registration(self.document, trusted_record_sha256=digest(self.document))
        attempt = str(uuid4())
        receipts = tuple(RankProcessReceipt(attempt, r, f"host-{r}", f"owner-{r}", g, runtime, bundle,
                                           r + 10, 99, f"/v8-rank{r}", capability, 1)
                         for (r, _, _, g, runtime, bundle), capability
                         in zip(admission.runtime_bindings, ((7, 0), (12, 0))))
        self.assertEqual(verify_live_joint_processes(admission, attempt, receipts,
                                                    observe_owned_process=lambda r: r in receipts), receipts)
        for wrong in ((receipts[0],), receipts[::-1], (receipts[0], replace(receipts[1], host_id="host-0"))):
            with self.assertRaises(CustodyError):
                verify_live_joint_processes(admission, attempt, wrong, observe_owned_process=lambda _: True)
        with self.assertRaises(CustodyError):
            verify_live_joint_processes(admission, attempt, receipts,
                                        observe_owned_process=lambda r: r.logical_rank == 1)
