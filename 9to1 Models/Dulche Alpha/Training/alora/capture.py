"""Capture real Qwen4-Exp dispatch for one admitted training sample.

This module loads no model and grants no compute admission. The caller supplies
an independently admitted native model and (when required) a permit callback.
Calibration uses eval/no-grad; training adapters are handled by a separate path.
"""

from __future__ import annotations

import copy
from contextlib import ExitStack

import torch

from .data import TrainingDataset
from .profile import ActivationProfiler
from .paged_experts import PagedExperts
from .paging import PagedEmbedding
from .compute import compute_scope, check_compute


def _input_device(model):
    getter = getattr(model, "get_input_embeddings", None)
    if callable(getter):
        embedding = getter()
        weight = getattr(embedding, "weight", None)
        if isinstance(weight, torch.Tensor):
            device = weight.device
        elif type(embedding) is PagedEmbedding:
            device = embedding.device
        else:
            raise ValueError("The model has no materialized or explicitly paged input embedding.")
    else:
        devices = {parameter.device for parameter in model.parameters()}
        if len(devices) != 1:
            raise ValueError("A model without input embeddings must have one explicit device.")
        device = next(iter(devices))
    if device.type == "meta":
        raise ValueError("Profiling requires materialized model inputs.")
    return device


def profile_row(model, dataset, row, profiler, *, before_compute=None) -> ActivationProfiler:
    """Return a staged profile after every expected native layer ran exactly once.

    Source profile and supplied row remain unchanged. An exception during input
    verification, forward, capture or staging removes all temporary hooks and
    restores each module's exact prior training flag. Final/validation rows are
    refused by TrainingDataset.snapshot before model execution.
    """
    if not isinstance(dataset, TrainingDataset):
        raise TypeError("Profiling requires an admitted TrainingDataset.")
    if not isinstance(profiler, ActivationProfiler):
        raise TypeError("Profiling requires an ActivationProfiler.")
    if not isinstance(model, torch.nn.Module):
        raise TypeError("Profiling requires a native Torch model.")
    if before_compute is not None and not callable(before_compute):
        raise TypeError("before_compute must be callable when supplied.")
    snapshot = dataset.snapshot(row)
    if (dataset.purpose not in {"software_test_only", "real_training"}
            or dataset.purpose != dataset.manifest.purpose
            or snapshot["_source"]["purpose"] != dataset.purpose):
        raise ValueError("Profiling input has an unsupported or conflicting purpose.")
    dataset_identity = copy.deepcopy(dataset.identity)
    if any(snapshot["_source"].get(key) != value for key, value in dataset_identity.items()):
        raise ValueError("The profiling snapshot differs from its dataset identity.")
    if dataset.purpose == "real_training" and profiler.base_model_sha256 is None:
        raise ValueError("Real-training capture requires the exact base model SHA-256.")
    if profiler.source_dataset is not None and profiler.source_dataset != dataset_identity:
        raise ValueError("A profile cannot mix dataset identities or execution purposes.")

    from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpTextExperts

    modules = dict(model.named_modules())
    targets = {}
    for path, expert_count in profiler.topology.items():
        module = modules.get(path)
        if type(module) is PagedExperts:
            if module.expert_ids or len(module.adapters):
                raise ValueError("Paged calibration requires the unchanged frozen base.")
            if (module.num_experts != expert_count or profiler.base_model_sha256 is None
                    or profiler.base_model_sha256 != module.source_identity):
                raise ValueError("Paged profiling must bind the exact admitted GGUF source and topology.")
        elif type(module) is Qwen4ExpTextExperts:
            if (module.num_experts != expert_count
                    or tuple(module.gate_up_proj.shape) != (
                        expert_count, 2 * module.intermediate_dim, module.hidden_dim)
                    or tuple(module.down_proj.shape) != (
                        expert_count, module.hidden_dim, module.intermediate_dim)):
                raise ValueError(f"{path} does not match the declared expert topology.")
        else:
            raise ValueError(f"{path} is not a supported frozen Qwen4-Exp expert layer.")
        targets[path] = module
    if len({id(module) for module in targets.values()}) != len(targets):
        raise ValueError("Profiling paths must not alias the same expert layer.")
    device = _input_device(model)
    token_count = len(snapshot["input_ids"])
    observations = {}

    def capture(path):
        def hook(module, args, kwargs):
            if path in observations:
                raise ValueError(f"Expert layer {path} executed more than once for one sample.")
            names = ("hidden_states", "top_k_index", "top_k_weights")
            if len(args) > len(names) or set(kwargs) - set(names):
                raise ValueError("Native expert dispatch arguments differ from the supported contract.")
            arguments = dict(zip(names, args))
            if arguments.keys() & kwargs.keys():
                raise ValueError("Native expert dispatch repeats an argument.")
            arguments.update(kwargs)
            if set(arguments) != set(names):
                raise ValueError("Native expert dispatch omitted an argument.")
            hidden, indices, weights = (arguments[name] for name in names)
            if (not all(isinstance(value, torch.Tensor) for value in (hidden, indices, weights))
                    or hidden.ndim != 2 or hidden.shape != (token_count, module.hidden_dim)
                    or indices.ndim != 2 or indices.shape[0] != token_count
                    or indices.shape[1] == 0 or weights.shape != indices.shape
                    or indices.dtype not in (torch.int32, torch.int64)
                    or not weights.is_floating_point()):
                raise ValueError("Dispatch must contain exactly this single sample's flattened tokens.")
            check_compute(device=device)
            observations[path] = (
                indices.detach().to(device="cpu").tolist(),
                weights.detach().to(device="cpu").tolist(),
            )
        return hook

    with ExitStack() as cleanup:
        # Direct restoration preserves deliberately mixed parent/child modes.
        for module in model.modules():
            cleanup.callback(setattr, module, "training", module.training)
        for path, module in targets.items():
            handle = module.register_forward_pre_hook(capture(path), with_kwargs=True)
            cleanup.callback(handle.remove)
        model.eval()
        with torch.no_grad(), compute_scope(before_compute, device=device):
            check_compute(device=device)
            inputs = {
                name: torch.tensor([snapshot[name]], dtype=torch.long, device=device)
                for name in ("input_ids", "attention_mask")
            }
            check_compute(device=device)
            if getattr(model, "alora_loader_report", None) is not None:
                from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpForCausalLM
                if type(model) is not Qwen4ExpForCausalLM:
                    raise ValueError("Paged calibration requires the actual native text model")
                model.model(**inputs, use_cache=False, output_router_logits=False, return_dict=True)
            else:
                model(**inputs, use_cache=False)
        missing = sorted(set(targets) - set(observations))
        if missing:
            raise ValueError(f"Expected expert layers did not execute: {', '.join(missing)}")
        staged = copy.deepcopy(profiler)
        staged.bind_dataset(dataset_identity)
        for path in profiler.topology:
            indices, weights = observations[path]
            staged.record(path, snapshot["category"], snapshot["id"],
                          indices, weights, snapshot["attention_mask"])
        return staged
