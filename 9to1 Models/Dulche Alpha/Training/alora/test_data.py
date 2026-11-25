"""Software-only numeric input fixtures; never a model training corpus."""

import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from custody.artifacts import CustodyError
from alora.data import CATEGORIES, TrainingDataset


def _hash(data):
    return hashlib.sha256(data).hexdigest()


def _tokens_hash(row):
    return _hash(json.dumps(
        {key: row[key] for key in ("input_ids", "attention_mask", "labels")},
        sort_keys=True, separators=(",", ":")).encode("ascii"))


def _row(sample_id, token, category="coding"):
    return {"id": sample_id, "category": category, "input_ids": [1, token, 9],
            "attention_mask": [1, 1, 1], "labels": [-100, token, 9]}


class TrainingDatasetTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="alora-data-software-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.manifest_path = self.root / "manifest.json"
        self.records = {
            "train": [_row("train-0", 3), _row("train-1", 4, "ui")],
            "validation": [_row("validation-0", 5)],
            "final_held_out": [_row("held-0", 6)]}

    def seal(self, mutate_manifest=None, payloads=None):
        files = []
        for split, rows in self.records.items():
            payload = b"".join((json.dumps(row) + "\n").encode() for row in rows)
            if payloads and split in payloads:
                payload = payloads[split]
            path = split + ".jsonl"
            (self.root / path).write_bytes(payload)
            files.append({
                "id": split + "-file", "split": split, "path": path,
                "sha256": _hash(payload), "size_bytes": len(payload),
                "lineage": [
                    {"role": role, "origin": "software_test_only",
                     "producer": "arithmetic-fixture",
                     "evidence": "Numeric parser software test; not training data"}
                    for role in ("examples", "labels")],
                "samples": [{"id": row["id"], "sha256": _tokens_hash(row)}
                            for row in rows]})
        manifest = {"schema": 1, "purpose": "software_test_only", "files": files}
        if mutate_manifest:
            mutate_manifest(manifest)
        payload = json.dumps(manifest, sort_keys=True).encode()
        self.manifest_path.write_bytes(payload)
        self.digest = _hash(payload)
        return manifest

    def load(self, **kwargs):
        kwargs.setdefault("purpose", "software_test_only")
        kwargs.setdefault("vocab_size", 32)
        return TrainingDataset(self.manifest_path, self.root,
                               expected_sha256=self.digest, **kwargs)

    def test_only_training_rows_are_exposed_with_pinned_source_and_owned_snapshot(self):
        self.seal()
        dataset = self.load()
        rows = list(dataset.rows())
        self.assertEqual([row["id"] for row in rows], ["train-0", "train-1"])
        self.assertEqual(rows[0]["_source"]["manifest_sha256"], self.digest)
        self.assertEqual(rows[0]["_source"]["sample_sha256"], _tokens_hash(rows[0]))
        snap = dataset.snapshot(rows[0])
        self.assertEqual(snap, rows[0])
        self.assertIsNot(snap, rows[0])
        self.assertIsNot(snap["input_ids"], rows[0]["input_ids"])
        rows[0]["input_ids"][1] = 20
        with self.assertRaises(CustodyError):
            dataset.snapshot(rows[0])
        self.assertEqual(next(dataset.rows())["input_ids"][1], 3)

    def test_all_five_owner_categories_and_masked_padding_are_supported(self):
        self.records["train"] = [_row("train-" + str(i), 10 + i, category)
                                 for i, category in enumerate(CATEGORIES)]
        for row in self.records["train"]:
            row["input_ids"].append(0)
            row["attention_mask"].append(0)
            row["labels"].append(-100)
        self.seal()
        dataset = self.load(max_tokens=4)
        self.assertEqual(tuple(row["category"] for row in dataset.rows()), CATEGORIES)

    def test_manifest_and_training_bytes_are_reverified_before_update(self):
        for target in ("manifest", "train"):
            with self.subTest(target=target):
                self.seal()
                dataset = self.load()
                row = next(dataset.rows())
                path = self.manifest_path if target == "manifest" else self.root / "train.jsonl"
                path.write_bytes(path.read_bytes() + b" ")
                with self.assertRaises(CustodyError):
                    dataset.snapshot(row)

    def test_evaluation_file_cannot_be_substituted_into_an_update(self):
        self.seal()
        dataset = self.load()
        for split in ("validation", "final_held_out"):
            with self.subTest(split=split):
                row = next(dataset.rows())
                row["_source"]["file_id"] = split + "-file"
                with self.assertRaises(CustodyError):
                    dataset.snapshot(row)

    def test_duplicate_tokens_across_splits_cannot_hide_behind_id_or_category(self):
        self.records["final_held_out"] = [_row("different-held-id", 3, "ux")]
        self.seal()
        with self.assertRaises(CustodyError):
            self.load()

    def test_reused_input_different_labels_are_rejected_across_splits(self):
        original = copy.deepcopy(self.records)
        for split in ("validation", "final_held_out"):
            for labels in ([-100, -100, 9], [-100, 7, 9]):
                with self.subTest(split=split, labels=labels):
                    self.records = copy.deepcopy(original)
                    reused = _row(split + "-reused", 3, "ux")
                    reused["labels"] = labels
                    self.records[split] = [reused]
                    self.assertNotEqual(_tokens_hash(self.records["train"][0]),
                                        _tokens_hash(reused))
                    self.seal()
                    with self.assertRaisesRegex(CustodyError, "Unmasked input"):
                        self.load()

    def test_padding_variant_cannot_disguise_cross_split_input_reuse(self):
        original = copy.deepcopy(self.records)
        variants = [
            ([1, 3, 9, 0], [1, 1, 1, 0], [-100, 3, 9, -100], False),
            ([31, 1, 3, 9, 30], [0, 1, 1, 1, 0], [-100, -100, 3, 9, -100], False),
            ([1, 3, 9, 31], [1, 1, 1, 0], [-100, 3, 9, -100], True),
        ]
        for ids, masks, labels, pad_train in variants:
            with self.subTest(ids=ids, pad_train=pad_train):
                self.records = copy.deepcopy(original)
                if pad_train:
                    train = self.records["train"][0]
                    train["input_ids"].append(0)
                    train["attention_mask"].append(0)
                    train["labels"].append(-100)
                reused = {"id": "held-padding-variant", "category": "ux",
                          "input_ids": ids, "attention_mask": masks, "labels": labels}
                self.records["final_held_out"] = [reused]
                self.assertNotEqual(_tokens_hash(self.records["train"][0]),
                                    _tokens_hash(reused))
                self.seal()
                with self.assertRaisesRegex(CustodyError, "Unmasked input"):
                    self.load()

    def test_fabricated_sample_hash_and_order_fail_against_actual_rows(self):
        def false_hash(manifest):
            manifest["files"][2]["samples"][0]["sha256"] = "a" * 64
        def reversed_ids(manifest):
            manifest["files"][0]["samples"].reverse()
        for mutation in (false_hash, reversed_ids):
            with self.subTest(mutation=mutation.__name__):
                self.seal(mutation)
                with self.assertRaises(CustodyError):
                    self.load()

    def test_hidden_split_rows_also_require_valid_shape_and_actual_hash(self):
        self.records["final_held_out"][0]["attention_mask"] = [1, 1]
        self.seal()
        with self.assertRaises(CustodyError):
            self.load()

    def test_strict_tokens_shapes_categories_and_shifted_targets(self):
        original = copy.deepcopy(self.records)
        changes = [
            lambda r: r["input_ids"].__setitem__(1, True),
            lambda r: r["input_ids"].__setitem__(1, 32),
            lambda r: r["labels"].__setitem__(1, -1),
            lambda r: r["attention_mask"].__setitem__(1, 2),
            lambda r: r["attention_mask"].__setitem__(1, False),
            lambda r: r.__setitem__("attention_mask", [1, 1]),
            lambda r: r.__setitem__("labels", [1, -100, -100]),
            lambda r: r["attention_mask"].__setitem__(2, 0),
            lambda r: r["attention_mask"].__setitem__(0, 0),
            lambda r: r.__setitem__("category", "unapproved"),
            lambda r: r.__setitem__("text", "not accepted"),
        ]
        for change in changes:
            with self.subTest(index=changes.index(change)):
                self.records = copy.deepcopy(original)
                change(self.records["train"][0])
                self.seal()
                with self.assertRaises(CustodyError):
                    self.load()

    def test_explicit_bounds_and_real_training_purpose_are_enforced(self):
        self.seal()
        for kwargs in ({"max_tokens": 2}, {"max_tokens": True},
                       {"max_tokens": 131073}, {"vocab_size": True},
                       {"vocab_size": 9}, {"purpose": "real_training"}):
            with self.subTest(kwargs=kwargs):
                with self.assertRaises(CustodyError):
                    self.load(**kwargs)

    def test_every_split_requires_label_lineage(self):
        for index in range(3):
            def remove_labels(manifest):
                manifest["files"][index]["lineage"] = manifest["files"][index]["lineage"][:1]
            self.seal(remove_labels)
            with self.subTest(split=index), self.assertRaises(CustodyError):
                self.load()

    def test_duplicate_json_keys_blank_records_and_extra_rows_are_rejected(self):
        normal = json.dumps(self.records["train"][0])
        cases = [
            normal.replace('"category": "coding"', '"category": "coding", "category": "ui"'),
            "",
            normal + "\n" + normal,
        ]
        for line in cases:
            payload = (line + "\n" + json.dumps(self.records["train"][1]) + "\n").encode()
            self.seal(payloads={"train": payload})
            with self.subTest(line=line[:40]), self.assertRaises(CustodyError):
                self.load()

    def test_manifest_digest_and_paths_cannot_be_redirected(self):
        self.seal()
        with self.assertRaises(CustodyError):
            TrainingDataset(self.manifest_path, self.root, expected_sha256="0" * 64,
                            purpose="software_test_only")
        alias = self.root / "alias.json"
        alias.symlink_to(self.manifest_path)
        with self.assertRaises(CustodyError):
            TrainingDataset(alias, self.root, expected_sha256=self.digest,
                            purpose="software_test_only")
        def traversal(manifest):
            manifest["files"][0]["path"] = "../train.jsonl"
        self.seal(traversal)
        with self.assertRaises(CustodyError):
            self.load()


if __name__ == "__main__":
    unittest.main()
