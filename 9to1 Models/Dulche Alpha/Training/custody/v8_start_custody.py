"""Seal the original fresh v8 adapter initialization as step zero, not progress."""

from dataclasses import asdict, dataclass
import hashlib
from pathlib import Path
import re

from .artifacts import ArtifactRef, ArtifactStore, AttemptRef, CustodyError, _encode_record, _read_record
from .v8_lora_init import V8LoraProof, V8LoraTarget, initialize_attested_v8_lora
from .v8_sampler import V8_SOURCE_SHA256

_PAGER = "2c1a0b615f935a8da7af9669cc99d7a7bc1e8833a5613e63e512f1bb870f2d21"
_HASH = re.compile(r"[a-f0-9]{64}")
_RNG = re.compile(r"(?:[a-f0-9]{2}){1,65536}")
_NAME = re.compile(r"model\.language_model\.layers\.(\d+)\.")


@dataclass(frozen=True, slots=True)
class SealedV8Start:
    base: ArtifactRef
    early: ArtifactRef
    tail: ArtifactRef
    reference: ArtifactRef


def _validated_reference(raw, expected_model_pins, expected_seed):
    keys = {"schema", "source_sha256", "pager_sha256", "model_pin_manifest_sha256", "seed",
            "torch_version", "peft_version", "targets", "cpu_before_sha256", "cpu_before_hex",
            "cpu_after_sha256", "cpu_after_hex", "adapter_sha256", "grad_checkpointing"}
    if (type(raw) is not dict or raw.keys() != keys or type(raw["schema"]) is not int or raw["schema"] != 1
            or raw["source_sha256"] != V8_SOURCE_SHA256 or raw["pager_sha256"] != _PAGER
            or type(expected_model_pins) is not str or not _HASH.fullmatch(expected_model_pins)
            or raw["model_pin_manifest_sha256"] != expected_model_pins or raw["grad_checkpointing"] is not True
            or type(expected_seed) is not int or type(raw["seed"]) is not int or raw["seed"] != expected_seed
            or raw["torch_version"] != "2.13.0+cu130" or raw["peft_version"] != "0.18.1"
            or type(raw["targets"]) is not list or not 0 < len(raw["targets"]) <= 1024
            or any(type(raw[k]) is not str or not _RNG.fullmatch(raw[k])
                   for k in ("cpu_before_hex", "cpu_after_hex"))):
        raise CustodyError("Original v8 CPU reference does not match the registered first launch.")
    before = bytes.fromhex(raw["cpu_before_hex"])
    after = bytes.fromhex(raw["cpu_after_hex"])
    if (hashlib.sha256(before).hexdigest() != raw["cpu_before_sha256"]
            or hashlib.sha256(after).hexdigest() != raw["cpu_after_sha256"]):
        raise CustodyError("Original v8 CPU reference RNG evidence was changed.")
    try:
        targets = tuple(V8LoraTarget(**row) for row in raw["targets"])
    except TypeError as error:
        raise CustodyError("Invalid v8 adapter target evidence.") from error
    return (V8LoraProof(raw["source_sha256"], raw["torch_version"], raw["seed"], targets,
                        raw["cpu_before_sha256"], raw["cpu_after_sha256"], raw["adapter_sha256"]),
            before, after)


def seal_original_v8_start(store, origin, original_reference, *,
                           trusted_reference_sha256, model_pins_sha256, original_seed):
    """Use a fresh setup attempt in HOST custody; do not launch or update a model.

    The independent native reference must already have been authenticated and
    sealed as an evidence artifact by the original supervised runtime. Callers
    may not supply an arbitrary same-file digest as registration authority.
    """
    if (type(store) is not ArtifactStore or type(origin) is not AttemptRef
            or type(original_reference) is not ArtifactRef or original_reference.attempt != origin
            or type(trusted_reference_sha256) is not str or original_reference.sha256 != trusted_reference_sha256):
        raise CustodyError("Exact sealed runtime reference needed for fresh v8 preparation.")
    published = store.verify(original_reference)
    seal = _read_record(published.parent / "seal.json")
    manifest = _read_record(published.parent / "manifest.json", seal["sha256"])
    if manifest.get("kind") != "evidence":
        raise CustodyError("Native reference must already be sealed as runtime evidence.")
    raw = _read_record(Path(published), trusted_reference_sha256)
    proof, before, expected_after = _validated_reference(raw, model_pins_sha256, original_seed)
    state, after = initialize_attested_v8_lora(proof, before)
    if after != expected_after:
        raise CustodyError("Native v8 replay did not reproduce the original CPU state.")
    rows = ({}, {})
    for name, tensor in state.items():
        match = _NAME.match(name)
        if match is None or not 0 <= int(match[1]) < 64:
            raise CustodyError("A native v8 adapter does not belong to either original model range.")
        rows[int(int(match[1]) >= 32)][name] = tensor
    if not all(rows):
        raise CustodyError("Both original v8 adapter ranges must be complete.")
    from safetensors.torch import save

    # Serialize both BEFORE publishing either; a malformed rank cannot produce
    # the setup record. Only the final checkpoint marks a valid step-zero base.
    packed = tuple(save(rank) for rank in rows)
    if any(not 0 < len(data) <= 256 * 1024**2 for data in packed):
        raise CustodyError("The original v8 initialization exceeds its admitted adapter budget.")
    fragments = tuple(
        store.publish_bytes(origin, f"v8-original-initial-rank{rank}.safetensors", "segment_checkpoint",
                            data, inputs=(original_reference,))
        for rank, data in enumerate(packed))
    base = store.publish_bytes(origin, "v8-original-step-zero.json", "checkpoint", _encode_record({
        "schema": 1, "format": "v8-original-step-zero-v1", "steps": 0,
        "model_pins_sha256": model_pins_sha256, "reference": asdict(original_reference),
        "rank_fragments": [asdict(fragment) for fragment in fragments],
    }), inputs=(original_reference, *fragments))
    return SealedV8Start(base, fragments[0], fragments[1], original_reference)
