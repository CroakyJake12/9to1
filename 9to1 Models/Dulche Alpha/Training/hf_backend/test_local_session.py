"""Retained software-only model/data artifacts; no real training corpus."""
import os
from hf_backend.cpu_guard import install, assert_cpu

CUDA_REFUSAL = install()

import copy
import hashlib
import json
from pathlib import Path
import shutil
import unittest
from unittest.mock import patch
from uuid import uuid4

import torch
from tokenizers import Tokenizer
from tokenizers.models import WordLevel
from transformers import PreTrainedTokenizerFast

from custody.artifacts import ArtifactStore, CustodyError
from custody.provenance import validate_dataset_manifest
from custody.snapshot import publish_snapshot, load_snapshot
from hf_backend.adapter import LocalBundle, LocalSession, make_software_model
from hf_backend.checkpoints import VerifiedCheckpointPublisher
from custody.artifacts import ArtifactRef


torch.set_num_threads(1)


class LocalSessionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        parent = Path(os.environ["DULCHE_HF_TEST_ARTIFACT_ROOT"])
        if not parent.is_absolute():
            raise RuntimeError("Operator must supply an absolute retained test artifact root")
        cls.root = parent / ("hf-software-" + str(uuid4()))
        cls.root.mkdir(parents=True, exist_ok=False)
        print("retained_software_artifacts=" + str(cls.root), flush=True)
        cls.store = ArtifactStore(cls.root / "custody")
        cls.run_id = cls.store.create_run("HF software correctness fixtures only", {"purpose": "software_test_only"})
        cls.source_attempt = cls.store.begin_attempt(cls.run_id, {"phase": "fixture_source"})
        cls.model_dir = cls.root / "tiny-random-llama-software-only"
        cls.model_dir.mkdir()
        torch.manual_seed(213)
        make_software_model().save_pretrained(cls.model_dir, safe_serialization=True)
        tokenizer = Tokenizer(WordLevel({"[UNK]": 0, **{f"tok{i}": i for i in range(1, 32)}}, unk_token="[UNK]"))
        PreTrainedTokenizerFast(tokenizer_object=tokenizer, unk_token="[UNK]").save_pretrained(cls.model_dir)
        cls.bundle = cls.publish_bundle(cls.model_dir)
        cls.data_dir = cls.root / "software-token-arrays"
        cls.data_dir.mkdir()
        entries = []
        for index, split in enumerate(("train", "validation", "final_held_out")):
            ids = [[1, 3 + index, 9 + index, 2]]
            raw = json.dumps({"input_ids": ids, "attention_mask": [[1, 1, 1, 1]], "labels": ids}).encode()
            path = cls.data_dir / (split + ".json")
            path.write_bytes(raw)
            digest = hashlib.sha256(raw).hexdigest()
            row = {"input_ids": ids[0], "attention_mask": [1, 1, 1, 1], "labels": ids[0]}
            sample_digest = hashlib.sha256(json.dumps(row, sort_keys=True, separators=(",", ":")).encode("ascii")).hexdigest()
            entries.append({"id": split, "split": split, "path": path.name, "sha256": digest,
                            "size_bytes": len(raw), "lineage": [{"role": "examples", "origin": "software_test_only",
                            "producer": "software arithmetic fixture", "evidence": "test_local_session.py"},
                            {"role": "labels", "origin": "software_test_only", "producer": "software arithmetic fixture",
                             "evidence": "test_local_session.py"}], "samples": [{"id": split, "sha256": sample_digest}]})
            cls.store.publish_bytes(cls.source_attempt, path.name, "dataset", raw)
        record = {"schema": 1, "purpose": "software_test_only", "files": entries}
        cls.manifest = validate_dataset_manifest(record, purpose="software_test_only")
        cls.store.publish_bytes(cls.source_attempt, "dataset-manifest.json", "dataset_manifest", json.dumps(record).encode())

    @classmethod
    def publish_bundle(cls, directory):
        refs = {path.name: cls.store.publish_bytes(cls.source_attempt, path.name, "model" if path.suffix == ".safetensors" else "config", path.read_bytes())
                for path in directory.iterdir()}
        return LocalBundle(directory, refs, 4 * 1024 * 1024)

    def session(self, **kwargs):
        dataset_root = kwargs.pop("dataset_root", self.data_dir)
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "test": self.id()})
        def preserve(state, record):
            return publish_snapshot(self.store, attempt, "failed-update", state, max_bytes=8 * 1024 * 1024)
        kwargs.setdefault("on_failed_update", preserve)
        session = LocalSession(self.store, self.bundle, self.manifest, dataset_root,
                               purpose="software_test_only", **kwargs)
        self.addCleanup(lambda: publish_snapshot(self.store, attempt, "final-test-state", session.snapshot(),
                                                max_bytes=8 * 1024 * 1024))
        return session

    def setUp(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(CUDA_REFUSAL.dulche_guard_calls, 0)

    def test_actual_local_cpu_full_and_lora_load_step_and_durable_checkpoint(self):
        for method in ("full", "lora"):
            attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "method": method})
            refs = []
            def publish(state, record):
                refs.append(publish_snapshot(self.store, attempt, f"update-{record['update']}", state, max_bytes=8 * 1024 * 1024))
            session = self.session(method=method, on_update=publish)
            assert_cpu(session.model, {})
            record = session.step_file("train")
            self.assertEqual(record["purpose"], "software_test_only")
            self.assertEqual(record["loss_target_tokens"], 3)
            self.assertFalse(record["nf4_execution"])
            self.assertTrue(record["nonzero_gradient"])
            if method == "lora":
                self.assertTrue(all("lora_" in name for name in record["selected"]))
            self.assertEqual(len(refs), 1)
            saved = load_snapshot(self.store, refs[0], max_bytes=8 * 1024 * 1024)
            self.assertEqual(saved["purpose"], "software_test_only")
            if method == "lora":
                self.assertEqual(saved["adapter_config"]["rank"], 2)
            for name, tensor in session.model.state_dict().items():
                torch.testing.assert_close(saved["model"][name], tensor.cpu(), atol=0, rtol=0)

    def test_held_out_and_unknown_files_cannot_update(self):
        session = self.session()
        for name in ("validation", "final_held_out", "absent"):
            with self.assertRaises(CustodyError):
                session.step_file(name)
        self.assertEqual(session.updates, 0)

    def test_preupdate_cancellation_preserves_parameters_and_rng(self):
        session = self.session()
        before = session.snapshot()
        calls = 0
        def cancel():
            nonlocal calls
            calls += 1
            return calls == 4
        with self.assertRaises(InterruptedError):
            session.step_file("train", cancelled=cancel)
        self.assertEqual(session.updates, 0)
        self.assertFalse(session.failed)
        self.assertTrue(torch.equal(before["rng"], torch.get_rng_state()))
        for name, tensor in session.model.state_dict().items():
            torch.testing.assert_close(tensor, before["model"][name], atol=0, rtol=0)

    def test_failed_publisher_cannot_repeat_update(self):
        def fail(state, record):
            raise IOError("publication failure fixture")
        session = self.session(on_update=fail)
        with self.assertRaises(IOError):
            session.step_file("train")
        self.assertEqual(session.updates, 1)
        with self.assertRaises(RuntimeError):
            session.step_file("train")
        self.assertTrue(session.snapshot()["failed"])

    def test_nonfinite_post_update_stops_candidate(self):
        retained = []
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "phase": "nonfinite-candidate"})
        def preserve(state, record):
            retained.append((publish_snapshot(self.store, attempt, "failed-update", state,
                                              max_bytes=8 * 1024 * 1024), record))
        session = self.session(on_failed_update=preserve)
        def break_update():
            with torch.no_grad():
                next(session.model.parameters()).fill_(float("nan"))
        with patch.object(session.optimizer, "step", side_effect=break_update):
            with self.assertRaises(FloatingPointError):
                session.step_file("train")
        with self.assertRaises(RuntimeError):
            session.step_file("train")
        self.assertEqual(len(retained), 1)
        ref, record = retained[0]
        saved = load_snapshot(self.store, ref, max_bytes=8 * 1024 * 1024)
        self.assertTrue(saved["failed"])
        self.assertTrue(any(torch.isnan(t).any() for t in saved["model"].values() if t.is_floating_point()))
        self.assertEqual(record["phase"], "optimizer_update")
        self.assertEqual(record["attempted_update"], 1)
        self.assertEqual(record["completed_updates"], 0)

    def test_failed_publication_preserves_candidate_without_repeating_update(self):
        retained = []
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "phase": "publication-failure"})
        def fail(state, record):
            raise OSError("software publication fault")
        def preserve(state, record):
            retained.append((publish_snapshot(self.store, attempt, "failed-update", state,
                                              max_bytes=8 * 1024 * 1024), record))
        session = self.session(on_update=fail, on_failed_update=preserve)
        with patch.object(session.optimizer, "step", wraps=session.optimizer.step) as update:
            with self.assertRaises(OSError):
                session.step_file("train")
            with self.assertRaises(RuntimeError):
                session.step_file("train")
            self.assertEqual(update.call_count, 1)
        self.assertEqual(retained[0][1]["phase"], "update_observer")
        self.assertEqual(retained[0][1]["completed_updates"], 1)
        saved = load_snapshot(self.store, retained[0][0], max_bytes=8 * 1024 * 1024)
        for name, tensor in session.model.state_dict().items():
            torch.testing.assert_close(saved["model"][name], tensor.cpu(), atol=0, rtol=0)

    def test_preservation_failure_keeps_both_causes_and_stops_session(self):
        def fail(state, record):
            raise OSError("software publication fault")
        session = self.session(on_update=fail, on_failed_update=fail)
        with self.assertRaises(ExceptionGroup) as raised:
            session.step_file("train")
        self.assertEqual(len(raised.exception.exceptions), 2)
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 1)

    def test_labels_and_actual_sample_digests_are_required_before_loading(self):
        for mode, message in (("labels", "label provenance"), ("sample", "sample digest")):
            record = self.manifest.as_record()
            record = json.loads(json.dumps(record))
            if mode == "labels":
                for entry in record["files"]:
                    entry["lineage"] = [trace for trace in entry["lineage"] if trace["role"] != "labels"]
            else:
                record["files"][0]["samples"][0]["sha256"] = "a" * 64
            manifest = validate_dataset_manifest(record, purpose="software_test_only")
            with patch("hf_backend.adapter.LlamaForCausalLM.from_pretrained") as loader:
                with self.assertRaisesRegex(CustodyError, message):
                    LocalSession(self.store, self.bundle, manifest, self.data_dir, purpose="software_test_only")
                loader.assert_not_called()

    def test_failed_observer_does_not_erase_verified_publication(self):
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "phase": "observer-fault"})
        publisher = VerifiedCheckpointPublisher(self.store, attempt, purpose="software_test_only", max_bytes=8 * 1024 * 1024)
        def observe(state, record):
            raise OSError("software success observer")
        observed = []
        def observe_failure(state, record):
            observed.append(record)
            raise OSError("software failure observer")
        session = self.session(checkpoint_publisher=publisher, on_update=observe, on_failed_update=observe_failure)
        with self.assertRaises(ExceptionGroup) as raised:
            session.step_file("train")
        self.assertIn("failure observer", raised.exception.message)
        self.assertNotIn("could not be preserved", raised.exception.message)
        receipt = session.last_publication
        self.assertIsNotNone(receipt)
        self.store.verify(receipt.checkpoint)
        self.store.verify(receipt.evidence)
        self.assertEqual(observed[0]["phase"], "update_observer")
        self.assertEqual(observed[0]["publication"]["checkpoint"]["sha256"], receipt.checkpoint.sha256)
        self.assertEqual(len(self.store.verify_attempt(attempt).artifacts), 4)

    def test_local_session_returns_verified_snapshot_and_linked_evidence(self):
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "phase": "verified-session"})
        publisher = VerifiedCheckpointPublisher(self.store, attempt, purpose="software_test_only", max_bytes=8 * 1024 * 1024)
        session = self.session(checkpoint_publisher=publisher)
        record = session.step_file("train")
        receipt = record["publication"]
        checkpoint, evidence = (ArtifactRef(**receipt[key]) for key in ("checkpoint", "evidence"))
        saved = load_snapshot(self.store, checkpoint, max_bytes=8 * 1024 * 1024)
        linked = json.loads(self.store.verify(evidence).read_bytes())
        self.assertEqual(linked["checkpoint"], receipt["checkpoint"])
        self.assertEqual(linked["record"]["update"], record["update"])
        self.assertEqual(saved["updates"], 1)
        for name, tensor in session.model.state_dict().items():
            torch.testing.assert_close(saved["model"][name], tensor, rtol=0, atol=0)

    def test_callback_cannot_reenter_or_repeat_optimizer_mutation(self):
        session = None
        def publish(state, record):
            session.step_file("train")
        session = self.session(on_update=publish)
        with patch.object(session.optimizer, "step", wraps=session.optimizer.step) as update:
            with self.assertRaisesRegex(RuntimeError, "already in progress"):
                session.step_file("train")
            self.assertEqual(update.call_count, 1)
        self.assertEqual(session.updates, 1)
        self.assertTrue(session.failed)

    def test_cleanup_failure_after_update_retains_exact_model_and_adam_state(self):
        retained = []
        attempt = self.store.begin_attempt(self.run_id, {"purpose": "software_test_only", "phase": "cleanup-failure"})
        def preserve(state, record):
            retained.append((publish_snapshot(self.store, attempt, "failed-update", state,
                                              max_bytes=8 * 1024 * 1024), record))
        session = self.session(on_failed_update=preserve)
        real_cleanup, calls = session.optimizer.zero_grad, 0
        def cleanup(**kwargs):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("software gradient cleanup fault")
            return real_cleanup(**kwargs)
        with patch.object(session.optimizer, "zero_grad", side_effect=cleanup):
            with self.assertRaisesRegex(OSError, "gradient cleanup"):
                session.step_file("train")
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 1)
        self.assertEqual(retained[0][1]["phase"], "gradient_cleanup")
        saved = load_snapshot(self.store, retained[0][0], max_bytes=8 * 1024 * 1024)
        actual = session.snapshot()
        for name, tensor in actual["model"].items():
            torch.testing.assert_close(saved["model"][name], tensor, atol=0, rtol=0)
        for key, slots in actual["optimizer"]["state"].items():
            for name, tensor in slots.items():
                torch.testing.assert_close(saved["optimizer"]["state"][key][name], tensor, atol=0, rtol=0)

    def test_partial_optimizer_cleanup_and_publication_errors_are_all_retained(self):
        def publication(state, record):
            raise OSError("software failed publication")
        session = self.session(on_failed_update=publication)
        real_cleanup, calls = session.optimizer.zero_grad, 0
        def cleanup(**kwargs):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("software cleanup")
            return real_cleanup(**kwargs)
        def partial_step():
            with torch.no_grad():
                next(session.model.parameters()).add_(1)
            raise ArithmeticError("software partial optimizer")
        with patch.object(session.optimizer, "step", side_effect=partial_step), \
             patch.object(session.optimizer, "zero_grad", side_effect=cleanup):
            with self.assertRaises(ExceptionGroup) as raised:
                session.step_file("train")
        group = raised.exception
        self.assertIsInstance(group.exceptions[0], ExceptionGroup)
        self.assertIsInstance(group.exceptions[0].exceptions[0], ArithmeticError)
        self.assertEqual(str(group.exceptions[0].exceptions[1]), "software cleanup")
        self.assertEqual(str(group.exceptions[1]), "software failed publication")
        self.assertTrue(session.failed)
        self.assertEqual(session.updates, 0)

    def variant(self):
        directory = self.root / ("rejected-bundle-" + str(uuid4()))
        shutil.copytree(self.model_dir, directory)
        return directory

    def test_extra_files_and_tampering_refused(self):
        for filename in ("unexpected.py", "model.safetensors"):
            directory = self.variant()
            (directory / filename).write_bytes(b"software tamper fixture")
            bundle = LocalBundle(directory, self.bundle.artifacts, self.bundle.maximum_weight_bytes)
            with self.assertRaises(CustodyError):
                bundle.verify(self.store)

    def test_remote_code_quantized_base_and_wrong_architecture_refused(self):
        for key, value in (("auto_map", {}), ("quantization_config", {}), ("model_type", "other")):
            directory = self.variant()
            config_path = directory / "config.json"
            config = json.loads(config_path.read_text())
            config[key] = value
            config_path.write_text(json.dumps(config))
            with self.assertRaises(CustodyError):
                self.publish_bundle(directory).verify(self.store)

    def test_software_purpose_cannot_become_real_or_nf4(self):
        with self.assertRaisesRegex(CustodyError, "Dataset purpose"):
            LocalSession(self.store, self.bundle, self.manifest, self.data_dir,
                         purpose="real_training", on_update=lambda state, record: None,
                         on_failed_update=lambda state, record: None)
        with self.assertRaises(CustodyError):
            self.session(method="nf4_lora")

    def test_changed_training_file_is_reverified_before_update(self):
        directory = self.root / ("rejected-data-" + str(uuid4()))
        shutil.copytree(self.data_dir, directory)
        session = self.session(dataset_root=directory)
        (directory / "train.json").write_text("{}")
        with self.assertRaises(CustodyError):
            session.step_file("train")
        self.assertEqual(session.updates, 0)

    def test_tokenizer_redirects_are_rejected_before_any_outside_read(self):
        import builtins
        import io
        sentinel = self.root / ("outside-sentinel-" + str(uuid4()) + ".json")
        sentinel.write_text('{"software_only_sentinel":true}')
        cases = []
        for key in ("tokenizer_file", "vocab_file", "merges_file", "fast_tokenizer_files"):
            for path in (str(sentinel), "../" + sentinel.name):
                cases.append((key, [path] if key == "fast_tokenizer_files" else path))
        cases += [("auto_map", {"AutoTokenizer": [str(sentinel), None]}),
                  ("tokenizer_class", "UnreviewedTokenizer"),
                  ("tokenizer_class", "LlamaTokenizer"), ("chat_template_file", str(sentinel))]
        for key, value in cases:
            with self.subTest(key=key, value=value):
                directory = self.variant()
                path = directory / "tokenizer_config.json"
                config = json.loads(path.read_text())
                config[key] = value
                path.write_text(json.dumps(config))
                bundle = self.publish_bundle(directory)
                accesses = []
                original_open, original_io_open = builtins.open, io.open
                def guard_open(original):
                    def opened(file, *args, **kwargs):
                        if isinstance(file, (str, os.PathLike)) and Path(file).resolve() == sentinel.resolve():
                            accesses.append(str(file))
                            raise AssertionError("Tokenizer tried to access outside sentinel")
                        return original(file, *args, **kwargs)
                    return opened
                with patch("builtins.open", side_effect=guard_open(original_open)), \
                     patch("io.open", side_effect=guard_open(original_io_open)), \
                     patch("hf_backend.adapter.LlamaForCausalLM.from_pretrained") as model_loader, \
                     patch.object(LocalBundle, "load_tokenizer") as tokenizer_loader:
                    with self.assertRaises(CustodyError):
                        LocalSession(self.store, bundle, self.manifest, self.data_dir, purpose="software_test_only")
                    self.assertEqual(accesses, [])
                    model_loader.assert_not_called()
                    tokenizer_loader.assert_not_called()


if __name__ == "__main__":
    unittest.main(verbosity=2)
