"""Worker executable for the separately registered v8 RTX/V100 owner.

No path, socket or GPU is selected by CLI alone. The already-owned supervisor
supplies a one-shot admission with input/lease/channel bindings. On an
unregistered launch this entrypoint exits before loading Torch or model bytes.
"""
import argparse
from dataclasses import dataclass, replace
import hashlib
import json
from pathlib import Path

from .artifacts import CustodyError, _open_regular, canonical_id
from .qwen35_shard_plan import RankPlan, TensorHeader, TensorPlacement
from .qwen35_shard_reader import RankShardManifest, ShardPin, TensorPin
from .qwen35_tensor_fetch import RankTensorFetcher
from .rank_grad_clip import NormReceipt, decide_global_clip
from .v8_frozen_guest_step import HostFrozenTranscriptPort, run_frozen_guest_step
from .v8_frozen_receipt import stage_frozen_v100
from .v8_guest_main_channel import FrozenGuestMainChannel
from .v8_joint_step import JointV8Ports, RankV8Update, prepare_v8_rank, run_v8_rank_sft_step
from .v8_owned_endpoints import _RTX, _V100


@dataclass(frozen=True, slots=True)
class PinnedRankInputs:
    config: Path
    config_sha256: str
    rank_plan: Path
    rank_plan_sha256: str
    rank_manifest: Path
    rank_manifest_sha256: str
    adapter: Path
    adapter_sha256: str


def _pinned_bytes(path, sha, limit):
    with _open_regular(Path(path)) as source:
        data = source.read(limit + 1)
    if not 0 < len(data) <= limit or hashlib.sha256(data).hexdigest() != sha:
        raise CustodyError("Registered rank input differs from the exact admitted bytes.")
    return data


def _json_input(path, sha, limit):
    def pairs(items):
        value = {}
        for k, v in items:
            if k in value:
                raise CustodyError("Duplicate registered rank input field.")
            value[k] = v
        return value
    admitted = _pinned_bytes(path, sha, limit)
    try:
        return json.loads(admitted, object_pairs_hook=pairs)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as e:
        raise CustodyError("Unreadable registered rank metadata.") from e


def rank_plan_inputs(paths, rank):
    """Read code-independent, signed model metadata, never discover a model."""
    if type(paths) is not PinnedRankInputs or type(rank) is not int or rank not in (0, 1):
        raise CustodyError("No registered original model range inputs.")
    raw = _json_input(paths.rank_plan, paths.rank_plan_sha256, 8 * 1024**2)
    pins = _json_input(paths.rank_manifest, paths.rank_manifest_sha256, 8 * 1024**2)
    try:
        if (set(raw) != {"start_layer", "stop_layer", "tensors", "shards", "frozen_backing_bytes"}
                or set(pins) != {"index_sha256", "shards", "tensors"}
                or (raw["start_layer"], raw["stop_layer"]) != (32 * rank, 32 * (rank + 1))
                or type(raw["tensors"]) is not list or not 0 < len(raw["tensors"]) <= 16384):
            raise CustodyError("Admitted metadata does not match this exact v8 layer range.")
        tensors = tuple(TensorPlacement(t["key"], t["shard"],
                                        TensorHeader(t["header"]["dtype"], tuple(t["header"]["shape"]),
                                                     t["header"]["start"], t["header"]["end"]))
                        for t in raw["tensors"])
        plan = RankPlan(raw["start_layer"], raw["stop_layer"], tensors, tuple(raw["shards"]),
                        raw["frozen_backing_bytes"])
        manifest = RankShardManifest(pins["index_sha256"], tuple(ShardPin(**s) for s in pins["shards"]),
                                     tuple(TensorPin(**t) for t in pins["tensors"]))
    except (ValueError, KeyError, TypeError) as e:
        raise CustodyError("Invalid signed v8 rank records.") from e
    return plan, manifest


@dataclass(frozen=True, slots=True)
class RegisteredRankRun:
    """Produced only by the already-owned registered runtime, not deserialized."""
    attempt_id: str
    step: int
    owner: str
    role: str
    gpu_uuid: str
    inputs: PinnedRankInputs
    ports: JointV8Ports
    local_control: object
    frame_budget: int
    main_channel: FrozenGuestMainChannel
    host_sampler: object | None = None
    authenticate_guest_completion: object | None = None
    weight_limits: dict | None = None
    weight_streams: tuple | None = None
    weights_reader: object | None = None
    paged_source: object | None = None
    restore_rng_before_step: object | None = None
    checkpoint: object | None = None
    clip_plan: object | None = None


def _cuda_identity(torch, rank, gpu_uuid):
    from .dual_gpu_smoke import _cuda_device

    # Physical topology ordinals are reversed from this logical model order.
    return _cuda_device(torch, 1 - rank, gpu_uuid)


def _main_ports(admission, rank):
    channel = admission.main_channel
    if (type(channel) is not FrozenGuestMainChannel
            or (channel._attempt, channel._step, channel._rank, channel._limit) != (
                admission.attempt_id, admission.step, rank, 8)):
        raise CustodyError("Owned v8 main channel does not match the admitted rank or accumulation.")
    if rank == 0:
        if channel._owner != admission.owner:
            raise CustodyError("Guest main channel belongs to a different owner.")
        return replace(admission.ports, read_microbatch=channel.read_batch,
                       open_frame_streams=channel.frames), []
    checkpoint, plan = admission.checkpoint, admission.clip_plan
    if (checkpoint is None or plan is None or plan.names(0) != () or checkpoint.owners != plan.owners
            or checkpoint.owners != (channel._owner, admission.owner)
            or getattr(channel._batch, "_sampler", None) is not admission.host_sampler
            or not callable(admission.authenticate_guest_completion)):
        raise CustodyError("Missing independently owned frozen V100 and native RTX custody.")
    transcript = HostFrozenTranscriptPort(admission.host_sampler, channel.frames,
                                          attempt_id=admission.attempt_id, step=admission.step, grad_accum=8)
    early_result = []

    def finish_joint_accumulation(current_rank, native_norm):
        if current_rank != 1 or early_result:
            raise CustodyError("A frozen guest completion may authorize one native update only.")
        guest, cuda_rng, cpu_rng = channel.receive_done()
        def authenticate(receipt, cuda, cpu):
            return (transcript.matches(receipt)
                    and admission.authenticate_guest_completion(receipt, cuda, cpu) is True)

        early_norm = NormReceipt(plan.attempt_id, plan.step, 0, plan.owners[0], ())
        norms = (early_norm, native_norm)
        decision = decide_global_clip(plan, norms)
        update = stage_frozen_v100(checkpoint, plan, decision, receipt=guest, cuda_rng=cuda_rng,
                                   cpu_rng=cpu_rng, grad_accum=8, authenticate_completion=authenticate)
        early_result.append(RankV8Update(norms, update, None))
        # This is the only route returning empty early slots. The original
        # guest process, complete transcript and both real RNGs already passed.
        return norms

    return replace(admission.ports, read_microbatch=channel.read_batch,
                   open_frame_streams=transcript, exchange_norms=finish_joint_accumulation), early_result


def execute_owned_rank(admission, *, verify_owner):
    """Run one real guest early or native tail operation through existing ports."""
    if (type(admission) is not RegisteredRankRun or admission.role not in ("early", "tail")
            or type(admission.step) is not int or admission.step <= 0 or type(admission.ports) is not JointV8Ports
            or canonical_id(admission.attempt_id) == "00000000-0000-0000-0000-000000000000"
            or not callable(verify_owner) or verify_owner(admission) is not True
            or not callable(admission.restore_rng_before_step)):
        raise CustodyError("No externally admitted, original v8 rank launch or RNG provenance.")
    import torch
    from safetensors.torch import load
    from transformers.models.qwen3_5.configuration_qwen3_5 import Qwen3_5TextConfig

    rank = 0 if admission.role == "early" else 1
    if admission.gpu_uuid != (_V100, _RTX)[rank]:
        raise CustodyError("Registered logical and physical v8 ranks disagree.")
    _cuda_identity(torch, rank, admission.gpu_uuid)
    ports, early = _main_ports(admission, rank)
    plan, manifest = rank_plan_inputs(admission.inputs, rank)
    raw_config = _json_input(admission.inputs.config, admission.inputs.config_sha256, 1024**2)
    text = raw_config.get("text_config")
    if (raw_config.get("architectures") != ["Qwen3_5ForConditionalGeneration"] or type(text) is not dict):
        raise CustodyError("Pinned config is not the original 27B Qwen3.5 architecture.")
    config = Qwen3_5TextConfig.from_dict(text)
    config._attn_implementation = "eager"
    adapters = load(_pinned_bytes(admission.inputs.adapter, admission.inputs.adapter_sha256, 256 * 1024**2))
    options = dict(config=config, plan=plan, rank=rank, approved_segment="upper",
                   approved_precision=("float16", "bfloat16")[rank], lora_state=adapters,
                   ports=ports, first_step=admission.step, local_control=admission.local_control)
    if rank == 0:
        if type(admission.weight_streams) is not tuple or len(admission.weight_streams) != 2:
            raise CustodyError("No separately owned pinned demand-weight channel.")
        inbound, outbound = admission.weight_streams
        fetcher = RankTensorFetcher(inbound, outbound, attempt_id=admission.attempt_id,
                                    plan=plan, manifest=manifest, **admission.weight_limits)

        def small_retained(placement):
            # All stock linears/embedding are deferred. Only remaining
            # non-linear native slots may take the bounded direct CPU path.
            payload = fetcher._fetch(placement)
            return torch.frombuffer(payload, dtype=torch.bfloat16).reshape(placement.header.shape)

        prepared = prepare_v8_rank(**options, verified_tensor_reader=small_retained,
                                   v100_fetch_weight=fetcher.materialize_v100)
        admission.restore_rng_before_step(prepared)
        out = run_frozen_guest_step(prepared=prepared, attempt_id=admission.attempt_id, step=admission.step,
                                    owner=admission.owner, grad_accum=8, seq_len=2048,
                                    frame_budget=admission.frame_budget)
        fetcher.finish()
        admission.main_channel.send_done(*out)
        return out
    from .qwen35_shard_reader import RankTensorReader

    reader = admission.weights_reader
    if (type(reader) is not RankTensorReader or reader._stack is None or reader._index != manifest.index_sha256
            or reader._placements != {p.key: p for p in plan.tensors}
            or reader._pins != {pin.name: pin for pin in manifest.shards}
            or reader._digests != {pin.key: pin.sha256 for pin in manifest.tensors}):
        raise CustodyError("Native tail needs its original open publisher-verified model reader.")
    prepared = prepare_v8_rank(**options, verified_tensor_reader=reader,
                               paged_source=admission.paged_source, learning_rate=0.0001)
    admission.restore_rng_before_step(prepared)
    tail = run_v8_rank_sft_step(admission.checkpoint, admission.clip_plan, prepared=prepared,
                                grad_accum=8, original_seq_len=2048, frame_budget=admission.frame_budget)
    if len(early) != 1:
        raise CustodyError("The native update has no independently verified original V100 completion.")
    return early[0], tail


def main(argv=None, *, installed_owner=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("role", choices=("early", "tail", "weights"))
    parser.add_argument("--attempt", required=True)
    parser.add_argument("--gpu-uuid", required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    expected = _RTX if args.role == "tail" else _V100
    if (canonical_id(args.attempt) == "00000000-0000-0000-0000-000000000000" or args.gpu_uuid != expected):
        raise CustodyError("Worker PID route does not match the exact registered GPU role.")
    if args.dry_run:
        print("v8 joint rank bootstrap syntax verified; no launch, weights or CUDA.")
        return 0
    if installed_owner is None:
        # This is a fixed module name inside the same sealed code archive.
        # No arbitrary Python adapter supplied by request, path or environment.
        try:
            from . import v8_installed_joint_owner as installed_owner
        except ImportError as e:
            raise CustodyError("No signed independent daemon/RPC owner for both v8 ranks.") from e
    with installed_owner.bind_worker(args.role, args.attempt, args.gpu_uuid) as binding:
        if args.role == "weights":
            return binding.relay_owned_weight_channel()
        if (binding.admission.role, binding.admission.attempt_id, binding.admission.gpu_uuid) != (
                args.role, args.attempt, args.gpu_uuid):
            raise CustodyError("Owner binding does not match the exact worker bootstrap identity.")
        result = execute_owned_rank(binding.admission, verify_owner=binding.verify_owner)
        # Guest binary done was sent once on the ordered channel above.
        # This notification is for the independent lifecycle supervisor.
        binding.publish_completion(result)
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
