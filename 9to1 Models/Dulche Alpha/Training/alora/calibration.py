"""Retained calibration from admitted training rows and an unchanged frozen model.

This module selects no evaluation rows, loads no model and grants no compute
admission. A caller supplies the model, dataset, source identity and permit.
Each invocation publishes fresh files; a verified partial receipt can continue
in a fresh worker/store without repeating completed observations.
"""
from __future__ import annotations

import copy
import io
from pathlib import Path

import torch

from custody.artifacts import _encode_record, _write_atomic, checked_path
from .adapters import SelectedExperts
from .capture import profile_row
from .curriculum import CATEGORY_WEIGHTS
from .data import TrainingDataset, _json, _verified_bytes
from .paged_experts import PagedExperts
from .paging import PagedEmbedding, PagedLinear
from .profile import ActivationProfiler
from .read_limits import FingerprintCapacityExceeded
from .session import ComputeInterrupted, _loader_identity

_CATEGORIES = tuple(CATEGORY_WEIGHTS)
_SELECTION = "sample-id-order/category-round-robin-v1"
_PROFILE_LIMIT = 64 * 1024 * 1024
_RECEIPT_LIMIT = 4 * 1024 * 1024
_RECEIPT_FIELDS = {
    "schema", "purpose", "base_model_sha256", "source_dataset", "loader_identity",
    "selection", "samples_per_category", "selected_rows", "completed_rows",
    "selected_sample_ids", "completed_sample_ids", "selected_counts", "completed_counts",
    "total_samples", "completed_samples", "phase", "complete", "interruption",
    "profile_path", "receipt_path", "raw_sha256", "profile_sha256",
}


def _fresh_path(path):
    checked_path(path.parent, directory=True)
    try:
        path.lstat()
    except FileNotFoundError:
        return
    raise FileExistsError("Calibration publication requires a fresh path: " + str(path))


def _topology(model, base_model_sha256):
    from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpTextExperts

    if not isinstance(model, torch.nn.Module):
        raise TypeError("Calibration requires an independently admitted native model.")
    if any(parameter.requires_grad for parameter in model.parameters()):
        raise ValueError("Calibration requires an entirely frozen, unchanged model.")
    topology, seen = {}, set()
    for path, module in model.named_modules(remove_duplicate=False):
        if isinstance(module, SelectedExperts) and (
                type(module) is not PagedExperts or module.expert_ids or len(module.adapters)):
            raise ValueError("Calibration refuses an adapted expert graph.")
        if type(module) in (PagedLinear, PagedEmbedding, PagedExperts):
            if module.store.source_identity != base_model_sha256:
                raise ValueError("A paged model component differs from the admitted base identity.")
        if isinstance(module, (Qwen4ExpTextExperts, PagedExperts)):
            if type(module) not in (Qwen4ExpTextExperts, PagedExperts):
                raise ValueError("Calibration requires supported native expert classes.")
            if id(module) in seen:
                raise ValueError("Calibration expert paths cannot alias the same layer.")
            seen.add(id(module))
            topology[path] = module.num_experts
            if type(module) is PagedExperts and module.source_identity != base_model_sha256:
                raise ValueError("Paged expert provenance differs from the admitted base identity.")
    loader = _loader_identity(model)
    if loader is not None and loader["source_identity"] != base_model_sha256:
        raise ValueError("The loaded model differs from the admitted base identity.")
    return topology, loader


def _select_rows(dataset, budget):
    chosen = {category: [] for category in _CATEGORIES}
    seen = set()
    for row in dataset.rows():
        if row["id"] in seen:
            raise ValueError("Calibration sample IDs must be unique across training files.")
        seen.add(row["id"])
        bucket = chosen[row["category"]]
        bucket.append(row)
        bucket.sort(key=lambda item: item["id"])
        del bucket[budget:]
    short = {category: len(rows) for category, rows in chosen.items() if len(rows) != budget}
    if short:
        raise ValueError("Calibration category shortfall before compute: " + str(short))
    return [chosen[category][index] for index in range(budget) for category in _CATEGORIES]


def _row_identity(row):
    return {"id": row["id"], "category": row["category"], "source": copy.deepcopy(row["_source"])}


def _ids_by_category(rows):
    return {category: [row["id"] for row in rows if row["category"] == category]
            for category in _CATEGORIES}


def _pinned_json(pin, limit):
    if (type(pin) is not dict or set(pin) != {"path", "sha256"}
            or type(pin["path"]) is not str):
        raise ValueError("Resume requires exact raw-file path and SHA-256 pins.")
    return _json(_verified_bytes(Path(pin["path"]), pin["sha256"], limit))


def _restore_partial(resume, *, expected, topology):
    if type(resume) is not dict or set(resume) != {"profile", "receipt"}:
        raise ValueError("Resume requires both the retained profile and receipt pins.")
    receipt = _pinned_json(resume["receipt"], _RECEIPT_LIMIT)
    record = _pinned_json(resume["profile"], _PROFILE_LIMIT)
    if type(receipt) is not dict or set(receipt) != _RECEIPT_FIELDS:
        raise ValueError("The calibration receipt has an unsupported shape.")
    for field in ("selected_counts", "completed_counts"):
        counts = receipt[field]
        if (type(counts) is not dict or set(counts) != set(_CATEGORIES)
                or any(type(value) is not int or value < 0 for value in counts.values())):
            raise ValueError("Calibration category counts must be exact nonnegative integers.")
    if type(receipt["samples_per_category"]) is not int or type(receipt["total_samples"]) is not int:
        raise ValueError("Calibration selection counts must be exact integers.")
    for key in (
        "schema", "purpose", "base_model_sha256", "source_dataset", "loader_identity",
        "selection", "samples_per_category", "selected_rows", "selected_sample_ids",
        "selected_counts", "total_samples",
    ):
        if receipt[key] != expected[key]:
            raise ValueError("The partial calibration differs from the admitted selection: " + key)
    count = receipt["completed_samples"]
    if (type(count) is not int or not 0 <= count < expected["total_samples"]
            or receipt["phase"] != "interrupted" or receipt["complete"] is not False
            or receipt["interruption"] not in {"ComputeInterrupted", "FingerprintCapacityExceeded"}):
        raise ValueError("Only an incomplete, cooperatively interrupted calibration can resume.")
    completed = expected["selected_rows"][:count]
    completed_ids = _ids_by_category(completed)
    if (receipt["completed_rows"] != completed
            or receipt["completed_sample_ids"] != completed_ids
            or receipt["completed_counts"] != {key: len(ids) for key, ids in completed_ids.items()}
            or receipt["profile_path"] != resume["profile"]["path"]
            or receipt["receipt_path"] != resume["receipt"]["path"]
            or receipt["raw_sha256"] != resume["profile"]["sha256"]):
        raise ValueError("Partial calibration rows, counts or raw-file identity differ.")
    profiler = ActivationProfiler.from_record(record, expected_sha256=receipt["profile_sha256"])
    if (profiler.base_model_sha256 != expected["base_model_sha256"]
            or profiler.source_dataset != expected["source_dataset"]
            or dict(profiler.topology) != topology):
        raise ValueError("The retained profile differs from its admitted base, dataset or topology.")
    actual = {(item["layer"], item["sample_id"], item["category"]) for item in record["observations"]}
    required = {(layer, row["id"], row["category"]) for layer in topology for row in completed}
    if actual != required:
        raise ValueError("The retained profile does not contain exactly the completed whole rows.")
    return profiler, count


def calibrate(model, dataset, *, base_model_sha256, output_path,
              samples_per_category=4, check_permit=None, resume=None):
    """Profile balanced training rows, then publish immutable profile and receipt.

    Selection takes the smallest sample IDs in each category and processes them
    in category round-robin order. The default is four per category (20 rows).
    All five budgets must be available before any forward computation.

    output_path must be absolute with an existing checked parent. Complete JSON
    is published there; interrupted JSON uses output_path + ".partial.json".
    Each receipt appends ".receipt.json" to the actual profile path. No existing
    file is replaced. The returned mapping adds receipt_sha256 and a two-file
    resume descriptor to the exact receipt stored on disk.

    Resume pins both raw files, validates the canonical profile and whole-row
    progress against the same deterministic selection, then skips only those
    completed rows. A fresh output path is required for every invocation.
    """
    if type(dataset) is not TrainingDataset:
        raise TypeError("Calibration requires an already-admitted TrainingDataset.")
    if type(samples_per_category) is not int or not 1 <= samples_per_category <= 64:
        raise ValueError("samples_per_category must be an integer from 1 to 64.")
    if check_permit is not None and not callable(check_permit):
        raise TypeError("check_permit must be callable when supplied.")
    output = Path(output_path)
    partial = Path(str(output) + ".partial.json")
    for path in (output, partial):
        _fresh_path(path)
        _fresh_path(Path(str(path) + ".receipt.json"))
    topology, loader = _topology(model, base_model_sha256)
    profiler = ActivationProfiler(topology, base_model_sha256=base_model_sha256)
    profiler.bind_dataset(dataset.identity)
    selected = _select_rows(dataset, samples_per_category)
    identities = [_row_identity(row) for row in selected]
    selected_ids = _ids_by_category(identities)
    common = {
        "schema": "alora-calibration-receipt-v1", "purpose": dataset.purpose,
        "base_model_sha256": base_model_sha256, "source_dataset": dataset.identity,
        "loader_identity": loader, "selection": _SELECTION,
        "samples_per_category": samples_per_category, "selected_rows": identities,
        "selected_sample_ids": selected_ids,
        "selected_counts": {key: len(ids) for key, ids in selected_ids.items()},
        "total_samples": len(selected),
    }
    completed = 0
    if resume is not None:
        profiler, completed = _restore_partial(resume, expected=common, topology=dict(profiler.topology))
    interruption = None
    try:
        for row in selected[completed:]:
            profiler = profile_row(model, dataset, row, profiler, before_compute=check_permit)
            completed += 1
    except (ComputeInterrupted, FingerprintCapacityExceeded) as error:
        interruption = ("FingerprintCapacityExceeded" if isinstance(error, FingerprintCapacityExceeded)
                        else "ComputeInterrupted")
    complete = completed == len(selected)
    if complete:
        profiler.select(1, require_all_categories=True)
    record = profiler.to_record()
    # A retained profile must pass the same parser used by a fresh worker.
    ActivationProfiler.from_record(record, expected_sha256=record["sha256"])
    actual_path = output if complete else partial
    raw_sha256, _ = _write_atomic(actual_path, io.BytesIO(_encode_record(record)),
                                  max_bytes=_PROFILE_LIMIT)
    receipt_path = Path(str(actual_path) + ".receipt.json")
    completed_rows = identities[:completed]
    completed_ids = _ids_by_category(completed_rows)
    receipt = {
        **common, "completed_rows": completed_rows, "completed_sample_ids": completed_ids,
        "completed_counts": {key: len(ids) for key, ids in completed_ids.items()},
        "completed_samples": completed, "phase": "completed" if complete else "interrupted",
        "complete": complete, "interruption": interruption,
        "profile_path": str(actual_path), "receipt_path": str(receipt_path),
        "raw_sha256": raw_sha256, "profile_sha256": record["sha256"],
    }
    receipt_sha256, _ = _write_atomic(receipt_path, io.BytesIO(_encode_record(receipt)),
                                     max_bytes=_RECEIPT_LIMIT)
    return {**receipt, "receipt_sha256": receipt_sha256, "resume": {
        "profile": {"path": str(actual_path), "sha256": raw_sha256},
        "receipt": {"path": str(receipt_path), "sha256": receipt_sha256},
    }}
