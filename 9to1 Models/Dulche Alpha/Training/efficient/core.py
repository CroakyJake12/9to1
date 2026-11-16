"""Bounded dense CPU optimization with explicit block update semantics.

The supervisor owns data admission, resources and immutable artifact publication.
This module never reads a dataset, loads a checkpoint file or touches CUDA.
"""
from __future__ import annotations

import copy
import math
from dataclasses import asdict, dataclass
from typing import Callable

import torch
from torch import nn
from torch.nn import functional as F
from torch.utils.checkpoint import checkpoint


def _positive_number(value):
    # bool is an int subclass and must never silently become a learning rate.
    try:
        return type(value) in {int, float} and math.isfinite(value) and value > 0
    except OverflowError:
        return False


@dataclass(frozen=True)
class Config:
    mode: str = "conventional"
    optimizer: str = "sgd"
    learning_rate: float = 0.01
    max_grad_norm: float | None = None
    objective: str = "mse"
    purpose: str = "software_test_only"

    def __post_init__(self):
        if self.mode not in {"conventional", "block_coordinate", "recompute_equivalent"}:
            raise ValueError("Unsupported update semantics")
        if self.optimizer not in {"sgd", "adam"}:
            raise ValueError("Only conventional SGD and Adam are implemented")
        if self.objective not in {"mse", "cross_entropy"}:
            raise ValueError("Unsupported built-in objective")
        if self.purpose not in {"software_test_only", "independently_admitted_training"}:
            raise ValueError("Explicit data purpose required; supervisor must admit training data")
        if self.learning_rate is None:
            raise ValueError("Learning rate is required")
        for value in (self.learning_rate, self.max_grad_norm):
            if value is not None and not _positive_number(value):
                raise ValueError("Learning rate and clipping limit must be finite and positive")


class LoRALinear(nn.Module):
    """Frozen full-precision base plus alpha/rank * B A; no 4-bit claim."""

    def __init__(self, base: nn.Linear, rank: int, alpha: float):
        super().__init__()
        if type(base) is not nn.Linear or base.weight.device.type != "cpu":
            raise ValueError("LoRA requires an ordinary CPU Linear")
        if not isinstance(rank, int) or isinstance(rank, bool) or not 0 < rank <= min(base.weight.shape):
            raise ValueError("Invalid LoRA rank")
        if not _positive_number(alpha):
            raise ValueError("Invalid LoRA alpha")
        self.base = copy.deepcopy(base)
        self.base.requires_grad_(False)
        self.rank, self.alpha = rank, float(alpha)
        self.a = nn.Parameter(torch.empty(rank, base.in_features, dtype=base.weight.dtype, device="cpu"))
        self.b = nn.Parameter(torch.zeros(base.out_features, rank, dtype=base.weight.dtype, device="cpu"))
        nn.init.kaiming_uniform_(self.a, a=math.sqrt(5))

    def forward(self, value):
        return self.base(value) + F.linear(F.linear(value, self.a), self.b) * (self.alpha / self.rank)

    def extra_repr(self):
        return f"rank={self.rank}, alpha={self.alpha}"


@dataclass(frozen=True)
class StepRecord:
    logical_step: int
    update: int
    mode: str
    purpose: str
    selected: tuple[str, ...]
    with_gradient: tuple[str, ...]
    nonzero_gradient: tuple[str, ...]
    loss: float
    gradient_norm: float
    clipping_scope: str
    gradient_bytes_retained: int


def _clone(value):
    if isinstance(value, torch.Tensor):
        return value.detach().clone()
    if isinstance(value, dict):
        return {key: _clone(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return type(value)(_clone(item) for item in value)
    return copy.deepcopy(value)


class CpuTrainer:
    """Single-owner, synchronous reference executor, one update per advance().

    Coordinate mode advances one block per call; other modes advance one full
    logical step. Callers preserve snapshots at each update to resume mid-cycle.
    """

    def __init__(self, model: nn.Sequential, blocks: tuple[tuple[str, ...], ...], config: Config,
                 on_update: Callable[[dict, StepRecord], None] | None = None):
        if type(model) is not nn.Sequential:
            raise ValueError("Only ordinary Sequential models are admitted")
        allowed = {nn.Sequential, nn.Linear, nn.ReLU, nn.Tanh, nn.Sigmoid,
                   nn.GELU, nn.Identity, nn.Dropout, LoRALinear}
        for module in model.modules():
            if type(module) not in allowed or getattr(module, "inplace", False):
                raise ValueError("Unsupported/custom/stateful/in-place module")
            if module._forward_hooks or module._forward_pre_hooks or module._backward_hooks:
                raise ValueError("Module hooks are outside the correctness boundary")
        if list(model.buffers()):
            raise ValueError("Mutable buffers are unsupported; BatchNorm replay is not silently approximated")
        named = list(model.named_parameters(remove_duplicate=False))
        if len({id(p) for _, p in named}) != len(named):
            raise ValueError("Shared/tied parameters require a separate validated adapter")
        if any(p.device.type != "cpu" or p.dtype not in {torch.float32, torch.float64} for _, p in named):
            raise ValueError("Only CPU float32/float64 parameters are admitted")
        if any(p._backward_hooks for _, p in named):
            raise ValueError("Parameter hooks are outside the correctness boundary")
        if any(not bool(torch.isfinite(p).all()) for _, p in named):
            raise ValueError("Parameters must be finite")
        self.model, self.config, self.on_update = model, config, on_update
        self.parameters = {name: p for name, p in named if p.requires_grad}
        self.blocks = tuple(tuple(block) for block in blocks)
        flat = [name for block in self.blocks for name in block]
        if not flat or any(not block for block in self.blocks) or len(set(flat)) != len(flat):
            raise ValueError("Blocks must be nonempty and disjoint")
        if set(flat) != set(self.parameters):
            raise ValueError("Blocks must cover every trainable parameter exactly once")
        factory = torch.optim.SGD if config.optimizer == "sgd" else torch.optim.Adam
        self.optimizer = factory(list(self.parameters.values()), lr=config.learning_rate, foreach=False)
        self.logical_step = self.updates = self.coordinate_cursor = 0
        self.parameter_updates = {name: 0 for name in self.parameters}
        self.last_gradients: dict[str, torch.Tensor | None] = {}
        self._failed = False

    def _topology(self):
        return tuple((name, type(module).__name__, module.extra_repr())
                     for name, module in self.model.named_modules())

    def _forward(self, value, recompute):
        for layer in self.model:
            value = checkpoint(layer, value, use_reentrant=False, preserve_rng_state=True) if recompute else layer(value)
        return value

    def _gradients(self, names, inputs, targets, recompute):
        selected = set(names)
        self.optimizer.zero_grad(set_to_none=True)
        for name, parameter in self.parameters.items():
            parameter.requires_grad_(name in selected)
        prediction = self._forward(inputs, recompute)
        if self.config.objective == "mse":
            if prediction.shape != targets.shape:
                raise ValueError("MSE target shape must match; broadcasting is refused")
            loss = F.mse_loss(prediction, targets)
        else:
            loss = F.cross_entropy(prediction, targets)
        if not bool(torch.isfinite(loss)):
            raise FloatingPointError("Nonfinite loss; no update applied")
        if loss.requires_grad:
            loss.backward()
        gradients = {name: None if self.parameters[name].grad is None
                     else self.parameters[name].grad.detach().clone() for name in names}
        if any(g is not None and not bool(torch.isfinite(g).all()) for g in gradients.values()):
            raise FloatingPointError("Nonfinite gradients; no update applied")
        return float(loss.detach()), gradients

    def advance(self, inputs: torch.Tensor, targets: torch.Tensor, *,
                cancelled: Callable[[], bool] = lambda: False) -> StepRecord:
        if self._failed:
            raise RuntimeError("Executor failed; preserve state and restore an explicit checkpoint")
        if inputs.device.type != "cpu" or targets.device.type != "cpu":
            raise ValueError("CPU tensors required; no automatic device migration")
        if inputs.requires_grad or targets.requires_grad:
            raise ValueError("Data tensors must be detached")
        if inputs.numel() == 0 or targets.numel() == 0:
            raise ValueError("Empty batch")
        if not bool(torch.isfinite(inputs).all()) or not bool(torch.isfinite(targets).all()):
            raise ValueError("Nonfinite data")
        if cancelled():
            raise InterruptedError("Cancelled before gradient computation")
        start_rng = torch.get_rng_state().clone()
        end_rng = None
        update_applied = False
        gradients = {}
        coordinate = self.config.mode == "block_coordinate"
        recompute = self.config.mode == "recompute_equivalent"
        groups = (self.blocks[self.coordinate_cursor],) if coordinate else (
            self.blocks if recompute else (tuple(self.parameters),))
        try:
            for names in groups:
                if cancelled():
                    raise InterruptedError("Cancelled before update; parameters unchanged")
                if recompute:
                    torch.set_rng_state(start_rng)
                loss, block_gradients = self._gradients(names, inputs, targets, recompute)
                if end_rng is None:
                    end_rng = torch.get_rng_state().clone()
                gradients.update(block_gradients)
            if cancelled():
                raise InterruptedError("Cancelled before update; parameters unchanged")
            self.optimizer.zero_grad(set_to_none=True)
            for name, parameter in self.parameters.items():
                parameter.requires_grad_(True)
                parameter.grad = gradients.get(name)
            active = [self.parameters[name] for name, grad in gradients.items() if grad is not None]
            # Compute one complete L2 norm in double precision; never clip each
            # equivalent block separately. Coordinate norms are block-local.
            norm = math.sqrt(sum(float(p.grad.double().square().sum()) for p in active))
            if not math.isfinite(norm):
                raise FloatingPointError("Nonfinite gradient norm")
            scale = min(1.0, self.config.max_grad_norm / (norm + 1e-6)) if self.config.max_grad_norm else 1.0
            for parameter in active:
                parameter.grad.mul_(scale)
            self.last_gradients = _clone(gradients)
            # An exception during step/publication is a failed candidate, never
            # a retryable pre-update cancellation. Retain its state explicitly.
            update_applied = True
            self.optimizer.step()
            if any(not bool(torch.isfinite(p).all()) for p in self.parameters.values()):
                raise FloatingPointError("Nonfinite update; executor stopped, preserve failed state")
            self.updates += 1
            for name, gradient in gradients.items():
                if gradient is not None:
                    self.parameter_updates[name] += 1
            if coordinate:
                self.coordinate_cursor = (self.coordinate_cursor + 1) % len(self.blocks)
            if not coordinate or self.coordinate_cursor == 0:
                self.logical_step += 1
            if recompute:
                torch.set_rng_state(end_rng)
            record = StepRecord(self.logical_step, self.updates, self.config.mode, self.config.purpose,
                                tuple(gradients), tuple(n for n, g in gradients.items() if g is not None),
                                tuple(n for n, g in gradients.items() if g is not None and bool(g.count_nonzero())),
                                loss, norm, "active_block" if coordinate else "all_trainable",
                                sum(g.numel() * g.element_size() for g in gradients.values() if g is not None))
            if self.on_update is not None:
                self.on_update(self.snapshot(), record)
            return record
        except InterruptedError:
            if update_applied:
                self._failed = True
            else:
                torch.set_rng_state(start_rng)
            raise
        except BaseException:
            self._failed = True
            raise
        finally:
            for parameter in self.parameters.values():
                parameter.requires_grad_(True)
            self.optimizer.zero_grad(set_to_none=True)

    def snapshot(self) -> dict:
        """Owned detached copies; caller must publish without overwriting artifacts."""
        return {"schema": 1, "config": asdict(self.config), "blocks": self.blocks,
                "topology": self._topology(), "torch_version": str(torch.__version__),
                "model": _clone(self.model.state_dict()), "optimizer": _clone(self.optimizer.state_dict()),
                "rng": torch.get_rng_state().clone(), "logical_step": self.logical_step,
                "updates": self.updates, "coordinate_cursor": self.coordinate_cursor,
                "parameter_updates": dict(self.parameter_updates),
                "training_modes": tuple(m.training for m in self.model.modules()),
                "failed": self._failed}

    def restore(self, state: dict) -> None:
        """Restore trusted in-memory data onto the SAME architecture/configuration.

        This is not a serialization/security boundary. Supervisor validates stored
        artifact hashes and data types before constructing this mapping. All
        semantic validation and optimizer preparation happen before committing.
        """
        state = _clone(state)
        required = {"schema", "config", "blocks", "topology", "torch_version", "model",
                    "optimizer", "rng", "logical_step", "updates", "coordinate_cursor",
                    "training_modes", "parameter_updates", "failed"}
        if type(state) is not dict or state.keys() != required:
            raise ValueError("Checkpoint fields differ from schema")
        try:
            saved_config = Config(**state["config"])
        except (TypeError, ValueError) as error:
            raise ValueError("Invalid checkpoint configuration") from error
        if type(state["schema"]) is not int or state["schema"] != 1 or saved_config != self.config or tuple(map(tuple, state["blocks"])) != self.blocks:
            raise ValueError("Checkpoint configuration does not match")
        if tuple(map(tuple, state["topology"])) != self._topology() or state["torch_version"] != str(torch.__version__):
            raise ValueError("Checkpoint architecture/runtime differs")
        counters = (state["updates"], state["logical_step"], state["coordinate_cursor"])
        if any(type(value) is not int or value < 0 for value in counters):
            raise ValueError("Checkpoint counters must be nonnegative integers")
        updates, logical_step, cursor = counters
        expected_step, expected_cursor = (divmod(updates, len(self.blocks))
                                          if self.config.mode == "block_coordinate" else (updates, 0))
        if type(state["failed"]) is not bool or state["failed"] or (logical_step, cursor) != (expected_step, expected_cursor):
            raise ValueError("Failed or invalid checkpoint")
        current = self.model.state_dict()
        if not isinstance(state["model"], dict) or current.keys() != state["model"].keys() or any(
            type(t) is not torch.Tensor or current[n].shape != t.shape or current[n].dtype != t.dtype or t.device.type != "cpu"
            or not bool(torch.isfinite(t).all()) for n, t in state["model"].items()
        ):
            raise ValueError("Checkpoint model structure or numeric state differs")
        modes = tuple(state["training_modes"])
        if len(modes) != len(list(self.model.modules())) or any(type(mode) is not bool for mode in modes):
            raise ValueError("Checkpoint module structure differs")
        rng = state["rng"]
        if type(rng) is not torch.Tensor or rng.device.type != "cpu" or rng.dtype != torch.uint8 or rng.shape != torch.get_rng_state().shape:
            raise ValueError("Checkpoint RNG must match the CPU generator state shape/type")
        try:
            # Validates generator internals without changing the process RNG.
            torch.Generator(device="cpu").set_state(rng)
        except RuntimeError as error:
            raise ValueError("Invalid checkpoint RNG state") from error
        counts = state["parameter_updates"]
        if type(counts) is not dict or counts.keys() != self.parameters.keys():
            raise ValueError("Parameter update counts must cover every trainable parameter")
        for name, count in counts.items():
            maximum_steps = updates
            if self.config.mode == "block_coordinate":
                block_index = next(i for i, block in enumerate(self.blocks) if name in block)
                complete, current_cursor = divmod(updates, len(self.blocks))
                maximum_steps = complete + int(block_index < current_cursor)
            if type(count) is not int or not 0 <= count <= maximum_steps:
                raise ValueError("Parameter update count exceeds schedule")
        staged_optimizer = self._prepare_optimizer(state["optimizer"], counts)
        prior_model = _clone(current)
        prior_rng = torch.get_rng_state().clone()
        prior_modes = tuple(module.training for module in self.model.modules())
        try:
            self.model.load_state_dict(state["model"], strict=True)
            for module, mode in zip(self.model.modules(), modes):
                module.training = mode
            torch.set_rng_state(rng)
        except BaseException:
            try:
                self.model.load_state_dict(prior_model, strict=True)
                for module, mode in zip(self.model.modules(), prior_modes):
                    module.training = mode
                torch.set_rng_state(prior_rng)
            except BaseException as rollback_error:
                self._failed = True
                raise RuntimeError("Restore rollback failed; preserve state, executor stopped") from rollback_error
            raise
        # Only simple assignments remain after all fallible tensor operations.
        self.optimizer = staged_optimizer
        self.logical_step, self.updates, self.coordinate_cursor = logical_step, updates, cursor
        self.parameter_updates = counts
        self.last_gradients = {}
        self._failed = False

    def _prepare_optimizer(self, state, counts):
        factory = torch.optim.SGD if self.config.optimizer == "sgd" else torch.optim.Adam
        staged = factory(list(self.parameters.values()), lr=self.config.learning_rate, foreach=False)
        expected = staged.state_dict()
        if type(state) is not dict or state.keys() != expected.keys():
            raise ValueError("Invalid optimizer fields")
        groups = state["param_groups"]
        if type(groups) is not list or len(groups) != 1 or type(groups[0]) is not dict:
            raise ValueError("Invalid optimizer groups")
        # Strict type-aware comparison prevents bool/int coercion, alternate
        # hyperparameters, reordered IDs and extra silently accepted fields.
        def same(left, right):
            if type(left) is not type(right):
                return False
            if isinstance(right, dict):
                return left.keys() == right.keys() and all(same(left[k], right[k]) for k in right)
            if isinstance(right, (list, tuple)):
                return len(left) == len(right) and all(same(a, b) for a, b in zip(left, right))
            return left == right
        if not same(groups, expected["param_groups"]):
            raise ValueError("Optimizer settings/parameter order differ from configuration")
        slots = state["state"]
        if type(slots) is not dict or any(type(key) is not int or key not in expected["param_groups"][0]["params"] for key in slots):
            raise ValueError("Invalid optimizer parameter state")
        if self.config.optimizer == "sgd" and slots:
            raise ValueError("Plain SGD has no optimizer state")
        parameters = list(self.parameters.items())
        if self.config.optimizer == "adam" and set(slots) != {i for i, (name, _) in enumerate(parameters) if counts[name] > 0}:
            raise ValueError("Adam state presence differs from parameter update history")
        for identifier, slot in slots.items():
            if type(slot) is not dict or slot.keys() != {"step", "exp_avg", "exp_avg_sq"}:
                raise ValueError("Invalid Adam state fields")
            name, parameter = parameters[identifier]
            for key in ("exp_avg", "exp_avg_sq"):
                value = slot[key]
                if type(value) is not torch.Tensor or value.device.type != "cpu" or value.dtype != parameter.dtype or value.shape != parameter.shape or not bool(torch.isfinite(value).all()):
                    raise ValueError("Invalid Adam moment tensor")
                if key == "exp_avg_sq" and bool((value < 0).any()):
                    raise ValueError("Adam second moment cannot be negative")
            step = slot["step"]
            if type(step) is not torch.Tensor or step.device.type != "cpu" or step.ndim != 0 or step.dtype not in {torch.float32, torch.float64} or not bool(torch.isfinite(step)):
                raise ValueError("Invalid Adam step tensor")
            number = float(step)
            if number != counts[name] or not number.is_integer():
                raise ValueError("Adam step differs from parameter update history")
        # Loading can still perform PyTorch-version validation; it touches only
        # this staged optimizer, whose parameters reference the unchanged model.
        try:
            staged.load_state_dict(state)
        except (RuntimeError, ValueError, TypeError, KeyError) as error:
            raise ValueError("Optimizer state cannot be restored") from error
        return staged
