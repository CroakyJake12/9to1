"""Retained calibration lifecycle using numeric CPU software fixtures only."""
import copy
import hashlib
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from hf_backend.cpu_guard import install
GUARD = install()

import torch

from alora import test_data
from alora.adapters import attach_selected_experts
from alora.calibration import calibrate
from alora.curriculum import CATEGORY_WEIGHTS
from alora.gguf_model import load_text_model
from alora.profile import ActivationProfiler
from alora.read_limits import FingerprintCapacityExceeded
from alora.session import ComputeInterrupted
from alora.test_capture import _TokenModel
from alora.test_gguf_model import _fixture

CATEGORIES = tuple(CATEGORY_WEIGHTS)


class CalibrationTests(unittest.TestCase):
    def setUp(self):
        rng = torch.get_rng_state()
        self.addCleanup(torch.set_rng_state, rng)
        torch.manual_seed(9917)
        torch.set_num_threads(1)
        fixture = test_data.TrainingDatasetTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        self.fixture = fixture
        tokens = iter(token for token in range(32) if token not in (5, 6))
        rows = []
        for category in CATEGORIES:
            for index in range(5):
                row = test_data._row("sample-" + category + "-" + str(index), next(tokens), category)
                if index == 0:
                    row["input_ids"].append(0)
                    row["attention_mask"].append(0)
                    row["labels"].append(-100)
                rows.append(row)
        # Selection must be explicit ID order, not the file's first rows.
        fixture.records["train"] = list(reversed(rows))
        fixture.seal()
        self.dataset = fixture.load(max_tokens=4)
        self.base_sha = "a" * 64
        self.model = _TokenModel(two_layers=True)
        self.model.requires_grad_(False)
        self.model.train()
        self.model.embedding.eval()
        self.model.block.shared_expert.eval()

    def tearDown(self):
        self.assertFalse(torch.cuda.is_initialized())
        self.assertEqual(GUARD.dulche_guard_calls, 0)

    def output(self, name):
        return self.fixture.root / (name + ".json")

    def run_calibration(self, name, *, model=None, **kwargs):
        return calibrate(self.model if model is None else model, self.dataset,
                         base_model_sha256=self.base_sha, output_path=self.output(name), **kwargs)

    def record(self, result):
        payload = Path(result["profile_path"]).read_bytes()
        self.assertEqual(hashlib.sha256(payload).hexdigest(), result["raw_sha256"])
        receipt_bytes = Path(result["receipt_path"]).read_bytes()
        self.assertEqual(hashlib.sha256(receipt_bytes).hexdigest(), result["receipt_sha256"])
        self.assertEqual(json.loads(receipt_bytes),
                         {key: value for key, value in result.items()
                          if key not in {"receipt_sha256", "resume"}})
        record = json.loads(payload)
        self.assertEqual(record["sha256"], result["profile_sha256"])
        restored = ActivationProfiler.from_record(record, expected_sha256=result["profile_sha256"])
        self.assertEqual(restored.to_record(), record)
        return record

    def partial(self, name="partial"):
        completed_forwards = []
        handle = self.model.register_forward_hook(lambda *args: completed_forwards.append("complete"))

        def permit():
            # Dispatch may check permission repeatedly inside one forward. Only
            # interrupt after the entire prior model call has actually returned.
            if completed_forwards:
                raise ComputeInterrupted("numeric software permit stopped")

        try:
            result = self.run_calibration(name, samples_per_category=1, check_permit=permit)
        finally:
            handle.remove()
        self.assertEqual(completed_forwards, ["complete"])
        self.assertFalse(result["complete"])
        self.assertEqual(result["completed_samples"], 1)
        return result

    def test_default_balanced_twenty_profiles_real_train_dispatch_and_publishes_exact_pins(self):
        flags = {name: module.training for name, module in self.model.named_modules()}
        hooks = {name: tuple(module._forward_pre_hooks) for name, module in self.model.named_modules()}
        parameters = {name: parameter.detach().clone() for name, parameter in self.model.named_parameters()}
        visited = []
        original_read = self.dataset._read_entry

        def training_entry_only(entry):
            self.assertEqual(entry.split, "train")
            visited.append(entry.split)
            return original_read(entry)

        with patch.object(self.dataset, "_read_entry", side_effect=training_entry_only):
            result = self.run_calibration("complete")
        self.assertTrue(visited)
        self.assertTrue(result["complete"])
        self.assertEqual(result["phase"], "completed")
        self.assertIsNone(result["interruption"])
        self.assertEqual(result["completed_samples"], 20)
        self.assertEqual(self.model.calls, 20)
        self.assertEqual(result["completed_counts"], {category: 4 for category in CATEGORIES})
        self.assertEqual(result["selected_sample_ids"], {
            category: ["sample-" + category + "-" + str(index) for index in range(4)]
            for category in CATEGORIES})
        self.assertEqual(result["profile_path"], str(self.output("complete")))
        record = self.record(result)
        self.assertEqual(record["base_model_sha256"], self.base_sha)
        self.assertEqual(record["source_dataset"], self.dataset.identity)
        self.assertEqual(record["purpose"], "software_test_only")
        self.assertEqual(set(record["topology"]), {"block.experts", "second.experts"})
        for layer in record["layers"].values():
            for category in CATEGORIES:
                self.assertEqual(layer[category]["samples"], 4)
                self.assertEqual(layer[category]["valid_tokens"], 12)
        self.assertEqual(flags, {name: module.training for name, module in self.model.named_modules()})
        self.assertEqual(hooks, {name: tuple(module._forward_pre_hooks)
                                 for name, module in self.model.named_modules()})
        for name, parameter in self.model.named_parameters():
            self.assertTrue(torch.equal(parameter, parameters[name]), name)
            self.assertIsNone(parameter.grad)

    def test_missing_category_unfrozen_adapted_and_aliased_graphs_fail_before_forward(self):
        self.fixture.records["train"] = [row for row in self.fixture.records["train"]
                                         if row["category"] != "coding"]
        self.fixture.seal()
        missing = self.fixture.load(max_tokens=4)
        with self.assertRaisesRegex(ValueError, "shortfall"):
            calibrate(self.model, missing, base_model_sha256=self.base_sha,
                      output_path=self.output("missing"), samples_per_category=1)
        self.assertEqual(self.model.calls, 0)
        self.assertFalse(self.output("missing").exists())

        for kind in ("unfrozen", "adapted", "alias"):
            model = copy.deepcopy(self.model)
            if kind == "unfrozen":
                next(model.parameters()).requires_grad_(True)
            elif kind == "adapted":
                attach_selected_experts(model, {"block.experts": [0]}, rank=2, alpha=4)
                model.requires_grad_(False)
            else:
                model.alias = model.block.experts
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                calibrate(model, missing, base_model_sha256=self.base_sha,
                          output_path=self.output(kind), samples_per_category=1)
            self.assertEqual(model.calls, 0)
            self.assertFalse(self.output(kind).exists())

    def test_cooperative_interrupt_retains_whole_rows_and_never_occupies_complete_path(self):
        result = self.partial()
        self.assertEqual(result["phase"], "interrupted")
        self.assertEqual(result["interruption"], "ComputeInterrupted")
        self.assertFalse(self.output("partial").exists())
        self.assertEqual(result["profile_path"], str(self.output("partial")) + ".partial.json")
        record = self.record(result)
        self.assertEqual(len(record["observations"]), 2)
        self.assertEqual(result["completed_counts"],
                         {category: int(index == 0) for index, category in enumerate(CATEGORIES)})
        profiler = ActivationProfiler.from_record(record, expected_sha256=result["profile_sha256"])
        with self.assertRaises(ValueError):
            profiler.select(1)
        self.assertEqual(self.model.calls, 1)

    def test_empty_interruption_still_binds_source_and_noncooperative_failure_is_not_success(self):
        class Deadline(ComputeInterrupted):
            pass

        def stop():
            raise Deadline("software deadline")
        result = self.run_calibration("empty", samples_per_category=1, check_permit=stop)
        self.assertFalse(result["complete"])
        self.assertEqual(result["completed_samples"], 0)
        self.assertEqual(result["interruption"], "ComputeInterrupted")
        self.assertEqual(self.record(result)["source_dataset"], self.dataset.identity)
        self.assertEqual(self.record(result)["observations"], [])
        self.assertEqual(self.model.calls, 0)
        self.model.behavior = "fail"
        with self.assertRaisesRegex(RuntimeError, "software forward failure"):
            self.run_calibration("failed", samples_per_category=1)
        self.assertFalse(self.output("failed").exists())
        self.assertFalse(Path(str(self.output("failed")) + ".partial.json").exists())

    def test_fresh_paged_model_resumes_capacity_partial_without_duplicate_observations(self):
        _, raw, store = _fixture()
        model = load_text_model(store, raw, rank=2, alpha=4, chunk_rows=3)
        forwards = []
        handle = model.model.register_forward_hook(lambda *args: forwards.append("complete"))
        self.addCleanup(handle.remove)
        with self.assertRaisesRegex(ValueError, "base identity"):
            calibrate(model, self.dataset, base_model_sha256="f" * 64,
                      output_path=self.output("wrong-base"), samples_per_category=1)
        self.assertEqual(forwards, [])

        def limited():
            if forwards:
                raise FingerprintCapacityExceeded("software pin capacity")
        partial = calibrate(model, self.dataset, base_model_sha256=store.source_identity,
                            output_path=self.output("paged-partial"), samples_per_category=1,
                            check_permit=limited)
        self.assertEqual(partial["interruption"], "FingerprintCapacityExceeded")
        self.assertEqual(partial["completed_samples"], 1)
        self.record(partial)

        fresh_store = copy.deepcopy(store)
        fresh_store.reads = []
        resumed_model = load_text_model(fresh_store, raw, rank=2, alpha=4, chunk_rows=3)
        remaining = []
        handle = resumed_model.model.register_forward_hook(lambda *args: remaining.append("complete"))
        self.addCleanup(handle.remove)
        resumed = calibrate(resumed_model, self.dataset, base_model_sha256=fresh_store.source_identity,
                            output_path=self.output("paged-resumed"), samples_per_category=1,
                            resume=partial["resume"])
        self.assertEqual(len(remaining), 4)
        self.assertTrue(resumed["complete"])
        self.assertEqual(resumed["completed_samples"], 5)
        resumed_record = self.record(resumed)

        reference_store = copy.deepcopy(store)
        reference = load_text_model(reference_store, raw, rank=2, alpha=4, chunk_rows=3)
        uninterrupted = calibrate(reference, self.dataset, base_model_sha256=reference_store.source_identity,
                                  output_path=self.output("paged-reference"), samples_per_category=1)
        self.assertEqual(resumed_record, self.record(uninterrupted))
        self.assertEqual(len(resumed_record["observations"]), 5 * len(resumed_record["topology"]))

    def reseal_receipt(self, result, name, mutation):
        receipt = json.loads(Path(result["receipt_path"]).read_bytes())
        profile = self.output(name + "-source")
        profile.write_bytes(Path(result["profile_path"]).read_bytes())
        receipt_path = self.output(name + "-receipt")
        receipt["profile_path"], receipt["receipt_path"] = str(profile), str(receipt_path)
        mutation(receipt)
        payload = (json.dumps(receipt, sort_keys=True) + "\n").encode()
        receipt_path.write_bytes(payload)
        return {"profile": {"path": str(profile), "sha256": result["raw_sha256"]},
                "receipt": {"path": str(receipt_path), "sha256": hashlib.sha256(payload).hexdigest()}}

    def test_resume_rejects_resealed_lineage_count_and_progress_changes_before_compute(self):
        partial = self.partial("resume-source")

        def false_progress(receipt):
            rows = receipt["selected_rows"][:2]
            receipt["completed_rows"] = rows
            receipt["completed_samples"] = 2
            receipt["completed_sample_ids"] = {
                category: [row["id"] for row in rows if row["category"] == category]
                for category in CATEGORIES}
            receipt["completed_counts"] = {
                category: len(ids) for category, ids in receipt["completed_sample_ids"].items()}

        mutations = [
            lambda receipt: receipt.update(base_model_sha256="b" * 64),
            lambda receipt: receipt.update(purpose="real_training"),
            lambda receipt: receipt["completed_counts"].update({CATEGORIES[0]: True}),
            lambda receipt: receipt["completed_rows"].append(copy.deepcopy(receipt["completed_rows"][0])),
            false_progress,
        ]
        for index, mutation in enumerate(mutations):
            resume = self.reseal_receipt(partial, "mutation-" + str(index), mutation)
            model = copy.deepcopy(self.model)
            model.calls = 0
            with self.subTest(mutation=index), self.assertRaises(ValueError):
                self.run_calibration("rejected-" + str(index), model=model,
                                     samples_per_category=1, resume=resume)
            self.assertEqual(model.calls, 0)
            self.assertFalse(self.output("rejected-" + str(index)).exists())

        changed_topology = _TokenModel()
        changed_topology.requires_grad_(False)
        with self.assertRaisesRegex(ValueError, "topology"):
            self.run_calibration("changed-topology", model=changed_topology,
                                 samples_per_category=1, resume=partial["resume"])
        self.assertEqual(changed_topology.calls, 0)
        with self.assertRaises(ValueError):
            self.run_calibration("changed-budget", samples_per_category=2, resume=partial["resume"])
        Path(partial["profile_path"]).write_bytes(b"changed software evidence")
        with self.assertRaises(ValueError):
            self.run_calibration("changed-file", samples_per_category=1, resume=partial["resume"])

    def test_publication_refuses_existing_files_and_invalid_budgets_before_compute(self):
        target = self.output("existing")
        target.write_bytes(b"retained")
        with self.assertRaises(FileExistsError):
            self.run_calibration("existing", samples_per_category=1)
        self.assertEqual(target.read_bytes(), b"retained")
        for value in (0, -1, True, 1.0, 65):
            with self.subTest(budget=value), self.assertRaises(ValueError):
                self.run_calibration("invalid", samples_per_category=value)
        self.assertEqual(self.model.calls, 0)
