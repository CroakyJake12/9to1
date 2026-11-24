"""Admitted upper-only RTX tail wired to exact deployed v8 pager and masked loss."""
from dataclasses import dataclass
from functools import partial
import hashlib
import math
from pathlib import Path
from types import ModuleType

from .artifacts import CustodyError, _open_regular
from .qwen35_range import Qwen35TextRange
from .qwen35_rank_assembly import AssembledQwen35Rank
from .v8_rank_pager import _TARGETS, _original_name, _owned_v8_lora

_PAGED_SHA256 = "2c1a0b615f935a8da7af9669cc99d7a7bc1e8833a5613e63e512f1bb870f2d21"


def _verified_pager_bytes(path):
    path = Path(path)
    with _open_regular(path) as f:
        raw = f.read(32 * 1024 + 1)
    if path.name != "paged_training.py" or hashlib.sha256(raw).hexdigest() != _PAGED_SHA256:
        raise CustodyError("The v8 pager source does not match the admitted deployed file.")
    return raw


def _load_verified_pager(path):
    # Deliberately compile the exact bytes checked from one descriptor, not
    # import a mutable path or an arbitrary same-name module from sys.path.
    source = _verified_pager_bytes(path)
    module = ModuleType("_dulche_reviewed_v8_paged")
    exec(compile(source, "<reviewed v8 paged_training>", "exec"), module.__dict__)
    return module


def _require_tail_lane(segment, precision, device, capability, count):
    if (segment != "upper" or precision != "bfloat16" or device != "cuda:0"
            or capability != (12, 0) or count != 1):
        raise CustodyError("RTX v8 tail requires approved upper-only BF16 on one admitted SM120.")


@dataclass(frozen=True, slots=True)
class PlacedV8Tail:
    text: Qwen35TextRange
    lm_head: object
    parameters: tuple
    optimizer: object
    duty: object
    _masked_head_loss: object
    _device: object

    def causal_loss(self, hidden, labels):
        """The shifted, masked loss from v8 after native tail final norm."""
        import torch

        if (not isinstance(hidden, torch.Tensor) or hidden.device != self._device
                or hidden.ndim != 3 or not isinstance(labels, torch.Tensor)
                or labels.ndim != 2 or labels.shape != hidden.shape[:2] or labels.dtype != torch.long):
            raise CustodyError("The v8 tail loss needs the original batch-aligned token labels.")
        with torch.autocast(device_type="cuda", dtype=torch.bfloat16):
            target = labels[..., 1:].reshape(-1).to(self._device)
            selected = target != -100
            if not bool(selected.any()):
                raise ValueError("owner-provided example has no supervised target tokens")
            state = hidden[..., :-1, :].reshape(-1, hidden.shape[-1])[selected]
            return self._masked_head_loss.apply(state, target[selected], self.lm_head)


def place_upper_rtx_v8(assembled, *, device, approved_segment, approved_precision,
                       paged_source, control, initial_lora, learning_rate):
    """Return an ordered, ready tail and original configured AdamW.

    Caller owns registered runtime/dependency pins, physical UUID and lease
    admission, FP16/BF16 boundary cast, original adapter/optimizer provenance
    and resume, and autocast around the tail decoder. This only places a fresh
    in-memory rank; discard it on any failure without changing the base.
    """
    import torch
    from torch import nn
    from torch.utils.checkpoint import checkpoint
    from peft.tuners.lora.layer import Linear as PeftLinear

    if (type(assembled) is not AssembledQwen35Rank or type(assembled.text) is not Qwen35TextRange
            or assembled.text._rank != 1 or type(assembled.lm_head) is not nn.Linear
            or not callable(getattr(control, "wait_for_compute", None))
            or type(learning_rate) is not float or not math.isfinite(learning_rate) or learning_rate <= 0):
        raise CustodyError("Invalid original v8 RTX tail input.")
    device = torch.device(device)
    _require_tail_lane(approved_segment, approved_precision, str(device), (12, 0), 1)
    count = torch.cuda.device_count()
    capability = torch.cuda.get_device_capability(device) if count == 1 else None
    _require_tail_lane(approved_segment, approved_precision, str(device), capability, count)
    if not torch.cuda.is_bf16_supported(including_emulation=False):
        raise CustodyError("The admitted RTX runtime does not provide native BF16.")
    text, head = assembled.text, assembled.lm_head
    if (text._config.num_hidden_layers != 64 or len(text._layers) != 32
            or any(getattr(layer, "layer_idx", i) != i or not hasattr(layer, "gradient_checkpointing")
                   for i, (_, layer) in enumerate(text._layers, 32))):
        raise CustodyError("Unsupported v8 tail decoder range or checkpointing ABI.")
    modules = tuple(layer for _, layer in text._layers)
    for root in (head, text._edge, *modules):
        if any(p.device.type != "cpu" or p.dtype != torch.bfloat16 for p in root.parameters()):
            raise CustodyError("The v8 tail needs complete admitted CPU BF16 backing.")
    rows = [(index, name, child) for index, layer in enumerate(modules, 32)
            for name, child in layer.named_modules() if name and type(child) is nn.Linear]
    original = _owned_v8_lora(rows, initial_lora, torch)
    pager = _load_verified_pager(paged_source)
    duty = pager.Duty(control, device)
    parameters = []
    for index, name, child in rows:
        parent_name, _, attribute = name.rpartition(".")
        parent = modules[index - 32].get_submodule(parent_name)
        current = pager.PagedLinear(child, duty, True, index)
        if attribute in _TARGETS:
            path = _original_name(index, name)
            with torch.random.fork_rng(devices=[]):
                current = PeftLinear(current, "default", r=16, lora_alpha=32, lora_dropout=0.05,
                                     init_lora_weights=False)
            with torch.no_grad():
                for component in ("A", "B"):
                    key = f"{path}.lora_{component}.default.weight"
                    adapter = getattr(current, f"lora_{component}")["default"].to(dtype=torch.float32)
                    adapter.weight.copy_(original[key])
        setattr(parent, attribute, current)

    # Match v8 prepare_model_for_kbit_training and place_active, without ever
    # moving unregistered BF16 paged source backing onto the GPU.
    head = pager.PagedLinear(head, duty, False, None)
    for root in (*modules, text._edge, head):
        root.requires_grad_(False)
        for module in root.modules():
            for name, param in tuple(module.named_parameters(recurse=False)):
                module.register_parameter(name, nn.Parameter(param.detach().to(device, torch.float32),
                                                            requires_grad=False))
        root.to(device=device)
        root.train()
    for index, module in enumerate(modules, 32):
        module.gradient_checkpointing = True
        module._gradient_checkpointing_func = partial(checkpoint, use_reentrant=False)
        # Native traversal order is also the original optimiser group order.
        for relative, param in module.named_parameters():
            key = _original_name(index, relative)
            if key in original:
                param.requires_grad_(True)
                parameters.append((key, param))
    if set(original) != {key for key, _ in parameters}:
        raise CustodyError("The v8 tail did not place exactly the original upper adapter state.")
    text._rotary.to(device=device)
    optimizer = torch.optim.AdamW([param for _, param in parameters], lr=learning_rate)
    return PlacedV8Tail(text, head, tuple(parameters), optimizer, duty, pager.MaskedHeadLoss, device)
