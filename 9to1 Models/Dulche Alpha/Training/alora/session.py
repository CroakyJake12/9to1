"""Bounded adapter-only optimizer updates and exact checkpoint continuation.

The model is supplied by its independently admitted loader. This module neither
loads weights, obtains devices, starts services nor promotes a trained candidate.
"""
from __future__ import annotations

import copy
import hashlib
import json
import math
import re
import threading

import torch

from .adapters import SelectedExperts, adapter_plan, trainable_adapters
from .checkpoints import CheckpointPublisher
from .data import TrainingDataset
from .profile import ActivationProfiler

_SHA = re.compile(r"[a-f0-9]{64}")


class ComputeInterrupted(RuntimeError):
    """No optimizer update was committed; checkpoint the current completed state."""


def _copy_cpu(value):
    if isinstance(value, torch.Tensor):
        return value.detach().cpu().clone()
    if isinstance(value, dict):
        return {k: _copy_cpu(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_copy_cpu(v) for v in value]
    if isinstance(value, tuple):
        return tuple(_copy_cpu(v) for v in value)
    return copy.deepcopy(value)


def _finite(value):
    if isinstance(value, torch.Tensor):
        return bool(torch.isfinite(value).all())
    if isinstance(value, dict):
        return all(_finite(v) for v in value.values())
    if isinstance(value, (tuple, list)):
        return all(_finite(v) for v in value)
    return not isinstance(value, float) or math.isfinite(value)


def _digest(parameters):
    digest = hashlib.sha256()
    for name, parameter in parameters.items():
        value = parameter.detach().cpu().contiguous()
        digest.update(json.dumps([name, str(value.dtype), list(value.shape)]).encode())
        digest.update(memoryview(value.reshape(-1).view(torch.uint8).numpy()))
    return digest.hexdigest()


class TrainingSession:
    def __init__(self, model, *, identity, purpose, learning_rate=1e-4, max_grad_norm=1.0,
                 profile=None, expert_budget_per_layer=None):
        if purpose not in {"real_training", "software_test_only"}:
            raise ValueError("An explicit execution purpose is required")
        if (type(identity) is not dict or set(identity) != {
                "base_model_sha256", "dataset_manifest_sha256", "profile_sha256"}
                or any(type(v) is not str or not _SHA.fullmatch(v) for v in identity.values())):
            raise ValueError("Bind the admitted base, dataset manifest and observed profile digests")
        for value in (learning_rate, max_grad_norm):
            if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
                raise ValueError("Optimizer settings must be finite and positive")
        if purpose == "real_training" and type(profile) is not ActivationProfiler:
            raise ValueError("Real training requires its actual dataset-bound activation profile")
        if profile is not None:
            if type(profile) is not ActivationProfiler:
                raise ValueError("Unsupported activation profile")
            record = profile.to_record()
            source = profile.source_dataset or {}
            if (profile.purpose != purpose or profile.base_model_sha256 != identity["base_model_sha256"]
                    or source.get("manifest_sha256") != identity["dataset_manifest_sha256"]
                    or record["sha256"] != identity["profile_sha256"]):
                raise ValueError("The profile, base, dataset and session purpose do not match")
            expected = profile.select(expert_budget_per_layer, require_all_categories=True)
            actual = {n: tuple(m.expert_ids) for n, m in model.named_modules() if isinstance(m, SelectedExperts)}
            if any(tuple(sorted(expected[n])) != actual.get(n) for n in expected) or set(actual) != set(expected):
                raise ValueError("Installed adapters differ from the observed expert selection")
        self.model = model
        self.parameters = trainable_adapters(model)
        devices = {p.device for p in model.parameters()}
        if len(devices) != 1 or next(iter(devices)).type not in {"cpu", "cuda"}:
            raise ValueError("This backend requires one materialized, independently admitted device")
        self.device = next(iter(devices))
        self.purpose, self.max_grad_norm = purpose, float(max_grad_norm)
        self.optimizer = torch.optim.AdamW(list(self.parameters.values()), lr=learning_rate, weight_decay=0.0)
        self._optimizer_groups = copy.deepcopy(self.optimizer.state_dict()["param_groups"])
        self.identity = {
            **identity,
            "plan": adapter_plan({n: m for n, m in model.named_modules() if isinstance(m, SelectedExperts)}),
            "parameter_order": list(self.parameters),
            "expert_budget_per_layer": expert_budget_per_layer,
            "optimizer": {"kind": "AdamW", "learning_rate": float(learning_rate),
                          "weight_decay": 0.0, "max_grad_norm": self.max_grad_norm},
            "torch_version": str(torch.__version__),
            "device": str(self.device),
        }
        self.updates, self.cursor, self.failed = 0, 0, False
        self._lock = threading.Lock()
        self.last_record = None
        self._owning_thread = None

    def _rng(self):
        result = {"torch_cpu_rng": torch.get_rng_state().cpu().clone()}
        if self.device.type == "cuda":
            result["torch_device_rng"] = torch.cuda.get_rng_state(self.device).cpu().clone()
        return result

    def _restore_rng(self, state):
        torch.set_rng_state(state["torch_cpu_rng"])
        if self.device.type == "cuda":
            torch.cuda.set_rng_state(state["torch_device_rng"], self.device)

    def snapshot(self):
        """Return an owned state; refuse concurrent reads during an update."""
        if self._owning_thread == threading.get_ident():
            return self._snapshot()
        if not self._lock.acquire(blocking=False):
            raise RuntimeError("Cannot read a partially updated training state")
        try:
            return self._snapshot()
        finally:
            self._lock.release()

    def _snapshot(self):
        if self.device.type == "cuda":
            torch.cuda.synchronize(self.device)
        slots = {}
        for name, parameter in self.parameters.items():
            if parameter not in self.optimizer.state:
                continue
            try:
                slots[name] = int(self.optimizer.state[parameter]["step"].item())
            except (KeyError, TypeError, ValueError, OverflowError, AttributeError, RuntimeError):
                if not self.failed:
                    raise
                # Retain raw failed optimizer tensors even when a summary cannot
                # be interpreted. Failed snapshots can never be restored.
                slots[name] = None
        return {
            "schema": 1, "kind": "alora-training", "purpose": self.purpose,
            "identity": copy.deepcopy(self.identity), "updates": self.updates,
            "cursor": self.cursor, "failed": self.failed,
            "adapters": _copy_cpu(self.parameters),
            "optimizer": _copy_cpu(self.optimizer.state_dict()), "optimizer_slots": slots, **self._rng(),
        }

    def record(self, status=None):
        return {
            "schema": 1, "purpose": self.purpose,
            "status": status or ("failed" if self.failed else "completed" if self.updates else "initial"),
            "updates": self.updates, "identity": copy.deepcopy(self.identity),
        }

    def publish_current(self, publisher):
        self._check_publisher(publisher)
        if not self._lock.acquire(blocking=False):
            raise RuntimeError("Cannot publish during an active update")
        self._owning_thread = threading.get_ident()
        try:
            return publisher.publish(self.snapshot(), self.record())
        finally:
            self._owning_thread = None
            self._lock.release()

    def _check_publisher(self, publisher):
        if type(publisher) is not CheckpointPublisher or publisher.purpose != self.purpose:
            raise ValueError("A matching immutable checkpoint publisher is required")

    def step_row(self, dataset, row, publisher, *, before_compute=None):
        if type(dataset) is not TrainingDataset or dataset.purpose != self.purpose:
            raise ValueError("Training must use a dataset with the exact session purpose")
        if dataset.manifest_sha256 != self.identity["dataset_manifest_sha256"]:
            raise ValueError("The training dataset differs from the pinned session identity")
        self._check_publisher(publisher)
        embedding = self.model.get_input_embeddings()
        if dataset.vocab_size != embedding.weight.shape[0]:
            raise ValueError("The dataset vocabulary bound differs from the admitted model")
        checked = dataset.snapshot(row)
        # Device allocation happens only after the admitted compute permit.
        # The base loader supplies exact vocabulary/context bounds to TrainingDataset.
        def loss():
            batch = {field: torch.tensor([checked[field]], dtype=torch.long, device=self.device)
                     for field in ("input_ids", "attention_mask", "labels")}
            return self.model(**batch, use_cache=False).loss
        return self._step(loss, publisher, before_compute, source=checked["_source"])

    def step_software(self, loss_function, *, publisher=None, before_compute=None):
        if self.purpose != "software_test_only":
            raise ValueError("Arithmetic fixtures cannot enter a real training session")
        if publisher is not None:
            self._check_publisher(publisher)
        return self._step(loss_function, publisher, before_compute, source=None)

    def _step(self, loss_function, publisher, before_compute, *, source):
        if not self._lock.acquire(blocking=False):
            raise RuntimeError("Concurrent or reentrant optimizer updates are refused")
        if self.failed:
            self._lock.release()
            raise RuntimeError("This session stopped after a failed update; restore a verified checkpoint")
        self._owning_thread = threading.get_ident()
        attempted_update = self.updates + 1
        optimizer_started = False
        rng_before = None
        try:
            actual = trainable_adapters(self.model)
            if list(actual) != list(self.parameters) or any(actual[n] is not p for n, p in self.parameters.items()):
                raise ValueError("The model's trainable parameter identities changed")
            optimized = [p for group in self.optimizer.param_groups for p in group["params"]]
            expected_parameters = list(self.parameters.values())
            if (self.optimizer.state_dict()["param_groups"] != self._optimizer_groups
                    or len(optimized) != len(expected_parameters)
                    or any(actual is not expected for actual, expected in zip(optimized, expected_parameters))):
                raise ValueError("Optimizer parameters or settings changed after admission")
            rng_before = self._rng()
            if before_compute:
                before_compute()
            before = {name: p.detach().clone() for name, p in self.parameters.items()}
            before_sha = _digest(before)
            self.optimizer.zero_grad(set_to_none=True)
            self.model.train()
            with torch.enable_grad():
                loss = loss_function()
                if not isinstance(loss, torch.Tensor) or loss.ndim != 0 or not torch.isfinite(loss).item():
                    raise ValueError("The training loss must be one finite scalar")
                if before_compute:
                    before_compute()
                loss.backward()
            gradients = [p.grad for p in self.parameters.values() if p.grad is not None]
            if not gradients or any(not torch.isfinite(g).all().item() for g in gradients):
                raise ValueError("The update has absent or non-finite adapter gradients")
            grad_norm = torch.nn.utils.clip_grad_norm_(
                list(self.parameters.values()), self.max_grad_norm, error_if_nonfinite=True)
            if before_compute:
                before_compute()
            optimizer_started = True
            self.optimizer.step()
            if not _finite(self.parameters) or not _finite(self.optimizer.state_dict()):
                raise ValueError("The optimizer produced non-finite state")
            changed = []
            for name, parameter in self.parameters.items():
                delta = torch.linalg.vector_norm((parameter.detach() - before[name]).double()).item()
                if delta > 0:
                    changed.append({"name": name, "l2_delta": delta,
                                    "max_abs_update": float((parameter.detach() - before[name]).abs().max())})
            if not changed:
                raise ValueError("No selected adapter changed; this is not a completed training update")
            self.updates += 1
            self.cursor += 1
            self.optimizer.zero_grad(set_to_none=True)
            record = {
                **self.record("completed"), "loss": float(loss.detach()),
                "grad_norm": float(grad_norm), "changed_parameters": changed,
                "parameter_before_sha256": before_sha, "parameter_after_sha256": _digest(self.parameters),
                "source": source,
            }
            self.last_record = record
            publication = publisher.publish(self.snapshot(), record) if publisher else None
            return record, publication
        except ComputeInterrupted:
            if optimizer_started:
                self.failed = True
                raise RuntimeError("Compute interruption after optimizer start cannot be retried")
            if rng_before is not None:
                self._restore_rng(rng_before)
            self.optimizer.zero_grad(set_to_none=True)
            raise
        except BaseException as original:
            self.failed = True
            self.optimizer.zero_grad(set_to_none=True)
            record = {**self.record("failed"), "attempted_update": attempted_update,
                      "error_type": type(original).__name__, "optimizer_started": optimizer_started}
            self.last_record = record
            if publisher is not None:
                try:
                    publisher.publish(self.snapshot(), record)
                except BaseException as publication_error:
                    raise BaseExceptionGroup("Update failed; failure publication also failed",
                                             [original, publication_error])
            raise
        finally:
            self._owning_thread = None
            self._lock.release()

    def restore(self, snapshot):
        """Validate a complete snapshot before committing adapter/Adam/RNG state."""
        if not self._lock.acquire(blocking=False):
            raise RuntimeError("Cannot restore while an update is active")
        self._owning_thread = threading.get_ident()
        try:
            if (type(snapshot) is not dict or type(snapshot.get("schema")) is not int or snapshot["schema"] != 1
                    or snapshot.get("kind") != "alora-training" or snapshot.get("purpose") != self.purpose
                    or snapshot.get("identity") != self.identity or snapshot.get("failed") is not False
                    or type(snapshot.get("updates")) is not int or snapshot["updates"] < 0
                    or type(snapshot.get("cursor")) is not int or snapshot["cursor"] != snapshot["updates"]):
                raise ValueError("Checkpoint identity, progress or successful-state marker differs")
            incoming = snapshot.get("adapters")
            if type(incoming) is not dict or set(incoming) != set(self.parameters):
                raise ValueError("Checkpoint adapter mapping differs")
            for name, p in self.parameters.items():
                value = incoming[name]
                if (not isinstance(value, torch.Tensor) or value.device.type != "cpu"
                        or value.shape != p.shape or value.dtype != p.dtype or not torch.isfinite(value).all().item()):
                    raise ValueError("Checkpoint adapter tensor is incompatible")
            rng = snapshot.get("torch_cpu_rng")
            generator = torch.Generator(device="cpu")
            if not isinstance(rng, torch.Tensor) or rng.device.type != "cpu" or rng.dtype != torch.uint8:
                raise ValueError("Checkpoint CPU RNG is invalid")
            generator.set_state(rng)  # Validation without mutating process RNG.
            if self.device.type == "cuda":
                gpu_rng = snapshot.get("torch_device_rng")
                gpu_generator = torch.Generator(device=self.device)
                gpu_generator.set_state(gpu_rng)
            state = snapshot.get("optimizer")
            current_groups = self._optimizer_groups
            if (type(state) is not dict or set(state) != {"state", "param_groups"}
                    or state["param_groups"] != current_groups or not _finite(state)):
                raise ValueError("Checkpoint optimizer configuration differs or is non-finite")
            if type(state["state"]) is not dict:
                raise ValueError("Checkpoint optimizer state is malformed")
            for index, item in state["state"].items():
                if type(index) is not int or not 0 <= index < len(self.parameters) or type(item) is not dict:
                    raise ValueError("Checkpoint optimizer parameter mapping is invalid")
                parameter = list(self.parameters.values())[index]
                if set(item) != {"step", "exp_avg", "exp_avg_sq"}:
                    raise ValueError("Checkpoint Adam state fields differ")
                for key in ("exp_avg", "exp_avg_sq"):
                    value = item[key]
                    if (not isinstance(value, torch.Tensor) or value.shape != parameter.shape
                            or value.dtype != parameter.dtype or value.device.type != "cpu"):
                        raise ValueError("Checkpoint Adam tensor is incompatible")
                if (item["exp_avg_sq"] < 0).any().item():
                    raise ValueError("Checkpoint Adam second moment cannot be negative")
                step = item["step"]
                if (not isinstance(step, torch.Tensor) or step.ndim != 0
                        or step.device.type != "cpu" or not step.is_floating_point()
                        or not 1 <= step.item() <= snapshot["updates"]
                        or step.item() != int(step.item())):
                    raise ValueError("Checkpoint Adam step is invalid")
            slots = {list(self.parameters)[i]: int(v["step"].item()) for i, v in state["state"].items()}
            if (snapshot.get("optimizer_slots") != slots
                    or (snapshot["updates"] > 0 and not slots)
                    or (snapshot["updates"] == 0 and slots)):
                raise ValueError("Checkpoint optimizer slot coverage differs from recorded progress")
            previous = self.snapshot()
            try:
                with torch.no_grad():
                    for name, value in incoming.items():
                        self.parameters[name].copy_(value)
                self.optimizer.load_state_dict(_copy_cpu(state))
                self._restore_rng(snapshot)
                self.updates, self.cursor, self.failed = snapshot["updates"], snapshot["cursor"], False
                self.last_record = None
                self.optimizer.zero_grad(set_to_none=True)
            except BaseException:
                # The destination is never silently reusable after uncertain commit.
                self.failed = True
                with torch.no_grad():
                    for name, value in previous["adapters"].items():
                        self.parameters[name].copy_(value)
                self.optimizer.load_state_dict(previous["optimizer"])
                self._restore_rng(previous)
                raise
        finally:
            self._owning_thread = None
            self._lock.release()
