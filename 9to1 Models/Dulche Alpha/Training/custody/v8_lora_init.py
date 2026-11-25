"""CPU-only, attested initialization of v8's original PEFT LoRA parameters.

This does not qualify a distributed training RNG. In particular, the original
single CUDA dropout stream is not reproduced by two independently seeded GPUs.
"""

from dataclasses import dataclass
import hashlib
from pathlib import Path
import re

from .artifacts import CustodyError
from .v8_sampler import V8_SOURCE_SHA256

_PEFT = "0.18.1"
_PEFT_LAYER_SHA = "e8a47a49cf69f92ded68bf7ab286aeaf068f0ac809ab3465ad16f78a8f9f8b63"
_PEFT_MODEL_SHA = "f619f6fccd6f2abc2ad51e4fde3f28a77a6b8104b7a94e1b0c18e341dafb48c7"
_TARGETS = ("q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj")
_PATH = re.compile(r"model\.language_model\.layers\.(0|[1-9][0-9]?)\.[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*")
_HASH = re.compile(r"[0-9a-f]{64}")


@dataclass(frozen=True, slots=True)
class V8LoraTarget:
    path: str
    in_features: int
    out_features: int


@dataclass(frozen=True, slots=True)
class V8LoraProof:
    """Trusted proof pins exact shapes/order and pre/post-injection CPU RNG bytes."""
    source_sha256: str
    torch_version: str
    seed: int
    targets: tuple[V8LoraTarget, ...]
    cpu_before_sha256: str
    cpu_after_sha256: str
    adapter_sha256: str


def _sha(value):
    return hashlib.sha256(value).hexdigest()


def _validate(proof):
    if (type(proof) is not V8LoraProof or proof.source_sha256 != V8_SOURCE_SHA256
            or type(proof.torch_version) is not str or not proof.torch_version
            or type(proof.seed) is not int or not -(2**63) <= proof.seed < 2**64
            or type(proof.targets) is not tuple or not 0 < len(proof.targets) <= 1024
            or any(type(h) is not str or not _HASH.fullmatch(h) for h in
                   (proof.cpu_before_sha256, proof.cpu_after_sha256, proof.adapter_sha256))):
        raise CustodyError("Exact original v8 LoRA CPU initialization proof is required.")
    seen, last_layer, adapter_bytes = set(), -1, 0
    for target in proof.targets:
        if (type(target) is not V8LoraTarget or type(target.path) is not str or len(target.path) > 256
                or target.path in seen or target.path.rsplit(".", 1)[-1] not in _TARGETS
                or not (match := _PATH.fullmatch(target.path))
                or not last_layer <= int(match[1]) < 64
                or any(type(n) is not int or not 0 < n <= 65536
                       for n in (target.in_features, target.out_features))):
            raise CustodyError("The v8 LoRA target proof is not an exact original ordered text-layer inventory.")
        last_layer = int(match[1])
        seen.add(target.path)
        adapter_bytes += 16 * 4 * (target.in_features + target.out_features)
        if adapter_bytes > 2**31:
            raise CustodyError("The v8 LoRA replay exceeds its CPU adapter byte budget.")


def _raw(torch, tensor):
    # CPU-only FP32/uint8, exact backing bytes without numerical conversion.
    import ctypes
    value = tensor.detach().cpu().contiguous()
    return ctypes.string_at(value.data_ptr(), value.numel() * value.element_size())


def _receipt(torch, rows):
    digest = hashlib.sha256()
    for name, value in rows.items():
        digest.update(name.encode("ascii") + b"\0")
        digest.update(_raw(torch, value))
    return digest.hexdigest()


def _replay(torch, PeftLinear, targets):
    from torch import nn

    class PagedShape(nn.Linear):
        def __init__(self, target):
            nn.Module.__init__(self)
            self.in_features, self.out_features = target.in_features, target.out_features
            # Exact v8 k-bit prep upcasts the original empty BF16 placeholder.
            self.weight = nn.Parameter(torch.empty(0, dtype=torch.float32, device="cpu"), requires_grad=False)
            self.bias = None

    values = {}
    for target in targets:  # native replacement order, NOT a sort by target suffix
        layer = PeftLinear(PagedShape(target), "default", r=16, lora_alpha=32,
                           lora_dropout=0.05, init_lora_weights=True)
        for component in ("A", "B"):
            value = getattr(layer, f"lora_{component}")["default"].weight.detach().float()
            shape = (16, target.in_features) if component == "A" else (target.out_features, 16)
            if value.device.type != "cpu" or tuple(value.shape) != shape or not torch.isfinite(value).all().item():
                raise CustodyError("Native PEFT produced an incompatible v8 adapter tensor.")
            values[f"{target.path}.lora_{component}.default.weight"] = value
    return values


def initialize_attested_v8_lora(proof: V8LoraProof, pre_injection_cpu_rng: bytes):
    """Replay in the pinned runtime only; require exact proof before exposing weights.

    Returns all original adapter tensors in replacement order and the post-
    injection CPU RNG bytes. CPU RNG of the caller is restored before return.
    Launcher is responsible for verifying the proof and device RNG separately.
    """
    _validate(proof)
    if (type(pre_injection_cpu_rng) is not bytes or not 0 < len(pre_injection_cpu_rng) <= 65536
            or _sha(pre_injection_cpu_rng) != proof.cpu_before_sha256):
        raise CustodyError("The v8 CPU RNG prefix differs from the attested original.")
    import torch
    import peft
    from peft.tuners.lora import layer, model

    if str(torch.__version__) != proof.torch_version or peft.__version__ != _PEFT:
        raise CustodyError("The native v8 LoRA initializer requires its pinned runtime and PEFT 0.18.1.")
    if any(_sha(Path(module.__file__).read_bytes()) != digest
           for module, digest in ((layer, _PEFT_LAYER_SHA), (model, _PEFT_MODEL_SHA))):
        raise CustodyError("The installed PEFT layer or dispatch source differs from the v8 runtime.")
    # No torch.manual_seed call (which also seeds device runtime). A trusted
    # CPU-prefix receipt was captured after exactly the original seeded load/pager.
    with torch.random.fork_rng(devices=[]), torch.device("cpu"):
        torch.set_rng_state(torch.frombuffer(bytearray(pre_injection_cpu_rng), dtype=torch.uint8))
        rows = _replay(torch, layer.Linear, proof.targets)
        after = _raw(torch, torch.get_rng_state())
        if _sha(after) != proof.cpu_after_sha256 or _receipt(torch, rows) != proof.adapter_sha256:
            raise CustodyError("Native v8 CPU RNG or initialized adapter bytes differ from the original proof.")
    return rows, after
