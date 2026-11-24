"""Application entrypoints for one registered two-rank v8 SFT step.

Inert on import. Deployment supplies authenticated ports, the original sampler
and checkpoint lineage, initial adapters and any verified optimiser resume.
No unauthenticated launcher, model fallback, or external input discovery.
"""
from contextlib import contextmanager
from dataclasses import dataclass
from threading import Lock
from typing import Callable

from .artifacts import CustodyError
from .qwen35_rank_assembly import assemble_qwen35_rank
from .qwen35_stage import Qwen35RankStage
from .rank_autograd import AutogradBoundary
from .rank_grad_clip import ClipPlan, decide_global_clip
from .rank_sft import RankSftStep, commit_sft_update
from .rank_stream import BoundaryFrame
from .v8_rank_pager import place_frozen_early_v8
from .v8_tail_pager import place_upper_rtx_v8


@dataclass(frozen=True, slots=True)
class JointV8Ports:
    # Both checks must return True; False or unknown cannot be treated as run.
    check_admission: Callable
    mentor_quiesced: Callable
    # Same original sampler coordinates on each admitted rank. Early reader
    # returns IDs, tail reader returns labels; both return a single [1, seq].
    read_microbatch: Callable
    open_frame_streams: Callable
    exchange_norms: Callable
    publish_fragment: Callable


@dataclass(frozen=True, slots=True)
class RankV8Update:
    receipts: tuple
    update: object
    mean_loss: float | None


def _admit(ports, *, rank, step):
    if ports.check_admission(rank, step) is not True or ports.mentor_quiesced() is not True:
        raise CustodyError("Joint compute lease or Flash quiescence is not verified.")


class _AdmittedControl:
    """One owned rank; no pager grant outside the current ordered step."""

    def __init__(self, ports, local, rank, first_step):
        self.ports, self.local, self.rank = ports, local, rank
        self._next, self._active = first_step, None
        self._lock = Lock()

    @contextmanager
    def running(self, step):
        if not self._lock.acquire(blocking=False):
            raise CustodyError("A prepared v8 rank is already running.")
        try:
            if self._next is None or step != self._next:
                raise CustodyError("Prepared v8 rank has stale or out-of-order step ownership.")
            _admit(self.ports, rank=self.rank, step=step)
            self._active = step
            try:
                yield
            except BaseException:
                self._next = None
                raise
            else:
                self._next = step + 1
        finally:
            self._active = None
            self._lock.release()

    def wait_for_compute(self):
        step = self._active
        if step is None:
            raise CustodyError("Pager has no active v8 step lease.")
        _admit(self.ports, rank=self.rank, step=step)
        expires = self.local.wait_for_compute()
        _admit(self.ports, rank=self.rank, step=step)
        return expires


@dataclass(frozen=True, slots=True)
class PreparedV8Rank:
    text: object
    tail: object
    control: _AdmittedControl


def prepare_v8_rank(*, config, plan, rank, verified_tensor_reader, approved_segment,
                    approved_precision, lora_state, ports, first_step, local_control,
                    paged_source=None, learning_rate=None):
    """Create one placement using only the supplied authenticated rank reader."""
    if (type(ports) is not JointV8Ports or type(rank) is not int or rank not in (0, 1)
            or type(first_step) is not int or first_step < 1 or approved_segment != "upper"
            or approved_precision != ("float16", "bfloat16")[rank]):
        raise CustodyError("Unsupported registered v8 rank or SFT precision.")
    _admit(ports, rank=rank, step=first_step)
    assembled = assemble_qwen35_rank(config, plan, split=32, rank=rank, read_tensor=verified_tensor_reader)

    control = _AdmittedControl(ports, local_control, rank, first_step)
    if rank == 0:
        from torch import cuda

        def duty():
            cuda.synchronize("cuda:0")
            control.wait_for_compute()

        place_frozen_early_v8(assembled, device="cuda:0", approved_segment=approved_segment,
                              approved_precision=approved_precision, duty=duty, frozen_lora=lora_state)
        return PreparedV8Rank(assembled.text, None, control)
    tail = place_upper_rtx_v8(assembled, device="cuda:0", approved_segment=approved_segment,
                              approved_precision=approved_precision, paged_source=paged_source,
                              control=control, initial_lora=lora_state, learning_rate=learning_rate)
    return PreparedV8Rank(assembled.text, tail, control)


def _batch_identity(tensor, config, sequence_cap, torch):
    if (type(tensor) is not torch.Tensor or tensor.device.type != "cpu" or tensor.ndim != 2
            or tensor.dtype != torch.long or tensor.shape[0] != 1
            or not 2 <= tensor.shape[1] <= min(config.max_position_embeddings, sequence_cap)):
        raise CustodyError("Batch does not match the original v8 SFT rank lane.")
    return 1, tensor.shape[1], config.hidden_size


def run_v8_rank_sft_step(checkpoint, clip_plan, *, prepared, grad_accum, original_seq_len, frame_budget):
    """Stage one owned rank, binding every pager lease to this actual step."""
    if type(prepared) is not PreparedV8Rank or type(clip_plan) is not ClipPlan:
        raise CustodyError("A prepared v8 rank and admitted clip plan are required.")
    control = prepared.control
    with control.running(clip_plan.step):
        if prepared.tail is not None:
            # Native v8 Duty caches the slice; never carry it across steps.
            prepared.tail.duty.expires = 0.0
        return _run_owned_step(checkpoint, clip_plan, rank=control.rank, text=prepared.text,
                               tail=prepared.tail, ports=control.ports, grad_accum=grad_accum,
                               original_seq_len=original_seq_len, frame_budget=frame_budget)


def _run_owned_step(checkpoint, clip_plan, *, rank, text, tail, ports,
                    grad_accum, original_seq_len, frame_budget):
    """Compute and stage this rank. Host must call commit_v8_sft_step with both results."""
    import math
    import torch

    if (type(ports) is not JointV8Ports or type(clip_plan) is not ClipPlan
            or type(rank) is not int or rank not in (0, 1) or text._rank != rank
            or (tail is None) != (rank == 0) or type(original_seq_len) is not int
            or original_seq_len < 2 or (tail is not None and tail.text is not text)):
        raise CustodyError("Invalid v8 SFT step ports or rank.")
    step, attempt_id = clip_plan.step, clip_plan.attempt_id
    device = torch.device("cuda:0")
    local = RankSftStep(checkpoint, clip_plan, rank=rank, parameters=() if tail is None else tail.parameters,
                        optimizer=None if tail is None else tail.optimizer, grad_accum=grad_accum)
    losses = []
    for micro in range(grad_accum):
        _admit(ports, rank=rank, step=step)
        batch = ports.read_microbatch(rank, step, micro)
        shape = _batch_identity(batch, text._config, original_seq_len, torch)
        frame = BoundaryFrame(attempt_id, step, micro, 0, "activation", "float16", shape)
        stage = Qwen35RankStage(text, AutogradBoundary(frame, frame_budget))
        with ports.open_frame_streams(rank, frame) as (inbound, outbound):
            if rank == 0:
                def early():
                    return stage.early(batch.to(device), inbound, outbound, step=step,
                                       microbatch=micro, forward_fp16=True)

                local.microbatch(micro, early)
            else:
                def tail_micro(backward):
                    def loss(hidden):
                        result = tail.causal_loss(hidden, batch)
                        if not bool(torch.isfinite(result.detach())):
                            raise CustodyError("The original v8 tail loss was nonfinite.")
                        _admit(ports, rank=rank, step=step)
                        tail.duty()
                        return backward(result)

                    return stage.tail(inbound, outbound, step=step, microbatch=micro, device=device,
                                      backward_loss=loss, hidden_dtype=torch.bfloat16)

                measured = float(local.microbatch(micro, tail_micro))
                if not math.isfinite(measured):
                    raise CustodyError("The original v8 tail loss was nonfinite.")
                losses.append(measured)
        torch.cuda.synchronize(device)
    _admit(ports, rank=rank, step=step)
    if tail is not None:
        tail.duty()
    receipts = ports.exchange_norms(rank, local.prepare_clip())
    decision = decide_global_clip(clip_plan, receipts)

    def before_optimizer():
        _admit(ports, rank=rank, step=step)
        tail.duty()
        _admit(ports, rank=rank, step=step)

    _admit(ports, rank=rank, step=step)

    def publish():
        torch.cuda.synchronize(device)
        return ports.publish_fragment(rank, checkpoint, tail)

    update = local.apply_and_stage(decision, publish, before_optimizer=before_optimizer if tail else None)
    return RankV8Update(receipts, update, sum(losses) / len(losses) if losses else None)


def commit_v8_sft_step(checkpoint, clip_plan, *, early, tail):
    """One durable progress point only after both admitted rank results."""
    import math

    if (type(early) is not RankV8Update or type(tail) is not RankV8Update
            or early.receipts != tail.receipts or early.mean_loss is not None
            or type(tail.mean_loss) is not float or not math.isfinite(tail.mean_loss)):
        raise CustodyError("Joint v8 step has missing or divergent rank results.")
    return commit_sft_update(checkpoint, clip_plan, tail.receipts, (early.update, tail.update))
