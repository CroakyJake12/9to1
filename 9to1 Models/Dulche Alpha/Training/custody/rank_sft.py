"""v8 SFT step across admitted rank owners; deliberately does not launch workers.

Both rank processes need the same admitted custody store and a trusted control
channel for norm receipts and checkpoints. A failed or uncertain mutation
requires discarding both workers and restoring the last jointly committed base.
"""
from dataclasses import asdict, dataclass
import hashlib
import math

from .artifacts import CustodyError, _encode_record, _read_record, write_record
from .rank_checkpoint import RankStageReceipt, TwoRankCheckpoint
from .rank_grad_clip import ClipDecision, ClipPlan, PreparedRankClip, decide_global_clip


@dataclass(frozen=True, slots=True)
class RankSftUpdate:
    staged: RankStageReceipt
    updated_parameter: str | None
    max_abs_update: float


def _claim_record(plan, rank, decision):
    return {
        "schema": 1, "attempt_id": plan.attempt_id, "step": plan.step,
        "rank": rank, "owner": plan.owners[rank],
        "clip_sha256": hashlib.sha256(_encode_record({"schema": 1, **asdict(decision)})).hexdigest(),
    }


def _claim_path(checkpoint, plan, rank):
    return checkpoint.control / f"sft-step-{plan.step}-rank-{rank}.started.json"


class RankSftStep:
    """One-shot step; owns bookkeeping, not model, transport or optimizer setup."""

    def __init__(self, checkpoint, plan, *, rank, parameters, optimizer, grad_accum):
        import torch

        if (type(checkpoint) is not TwoRankCheckpoint or type(plan) is not ClipPlan
                or plan.attempt_id != checkpoint.attempt.attempt_id
                or plan.step != checkpoint.step or plan.owners != checkpoint.owners
                or type(rank) is not int or rank not in (0, 1)
                or type(grad_accum) is not int or grad_accum < 1
                or type(parameters) is not tuple
                or any(type(row) is not tuple or len(row) != 2
                       or not isinstance(row[1], torch.nn.Parameter) for row in parameters)
                or tuple(name for name, _ in parameters) != plan.names(rank)):
            raise CustodyError("Invalid rank SFT step binding.")
        ids = [id(param) for _, param in parameters]
        if optimizer is None:
            if any(p.requires_grad for _, p in parameters):
                raise CustodyError("An active SFT rank requires its configured optimizer.")
        else:
            if (not isinstance(optimizer, torch.optim.Optimizer)
                    or [id(p) for group in optimizer.param_groups for p in group["params"]] != ids):
                raise CustodyError("Optimizer parameters differ from the admitted rank order.")
        if len(set(ids)) != len(ids) or any(param.grad is not None for _, param in parameters):
            raise CustodyError("A new SFT step requires disjoint parameters and cleared gradients.")
        self._checkpoint, self._plan, self._rank = checkpoint, plan, rank
        self._params, self._optimizer, self._accum = parameters, optimizer, grad_accum
        self._next, self._prepared, self._failed = 0, None, False
        self._claim = _claim_path(checkpoint, plan, rank)
        self._require_unclaimed()
        checkpoint._validate_base()

    def _require_unclaimed(self):
        if self._failed or self._claim.exists() or self._checkpoint.claim.exists():
            raise CustodyError("The rank SFT step is no longer replayable.")

    def microbatch(self, index, run):
        """Run one admitted stage callback. Tail run receives the backward hook."""
        self._require_unclaimed()
        if (type(index) is not int or index != self._next or self._next >= self._accum
                or self._prepared is not None or not callable(run)):
            raise CustodyError("SFT microbatch is out of accumulation order.")
        used = False

        def backward(loss):
            nonlocal used
            if used:
                self._failed = True
                raise CustodyError("SFT loss backward may run only once per microbatch.")
            used = True
            (loss / self._accum).backward()
            return loss.detach()

        try:
            result = run(backward) if self._rank == 1 else run()
            if self._failed or (self._rank == 1 and not used):
                raise CustodyError("The tail did not complete exactly one scaled backward.")
        except BaseException:
            self._failed = True
            raise
        self._next += 1
        return result

    def prepare_clip(self):
        self._require_unclaimed()
        if self._next != self._accum or self._prepared is not None:
            raise CustodyError("Both ranks must finish the configured accumulation before clipping.")
        self._prepared = PreparedRankClip(self._plan, rank=self._rank, parameters=self._params)
        return self._prepared.receipt

    def apply_and_stage(self, decision, publish_fragment, *, before_optimizer=None):
        """Apply once and stage real optimizer state via the owner-supplied publisher."""
        import torch

        self._require_unclaimed()
        if (self._prepared is None or not callable(publish_fragment) or type(decision) is not ClipDecision
                or (before_optimizer is not None and not callable(before_optimizer))):
            raise CustodyError("The SFT rank has no prepared gradient or checkpoint publisher.")
        if not math.isfinite(decision.norm) or decision.norm <= 0:
            raise CustodyError("SFT requires nonzero finite accumulated gradients.")
        binding = self._checkpoint
        try:
            # Exclusive durable record BEFORE the first mutation, even across process loss.
            write_record(self._claim, _claim_record(self._plan, self._rank, decision))
            self._prepared.apply(decision)
            witness = next(((name, param) for name, param in self._params
                            if param.requires_grad and param.grad is not None
                            and torch.count_nonzero(param.grad).item() > 0), None)
            any_grad = any(param.grad is not None for _, param in self._params)
            if any_grad and self._optimizer is None:
                raise CustodyError("Gradient exists on an optimizer-free rank.")
            name, delta = None, 0.0
            if any_grad:
                before = witness[1].detach().clone() if witness else None
                if before_optimizer is not None:
                    before_optimizer()
                self._optimizer.step()
                if witness:
                    name, param = witness
                    delta = float((param.detach() - before).abs().max().item())
                    if not math.isfinite(delta) or delta <= 0:
                        raise CustodyError("No measured finite optimizer parameter change.")
                self._optimizer.zero_grad(set_to_none=True)
            staged = publish_fragment()
            if (type(staged) is not RankStageReceipt or staged.rank != self._rank
                    or staged.attempt != binding.attempt or staged.step != binding.step
                    or staged.base != binding.base or staged.ownership_id != binding.owners[self._rank]):
                raise CustodyError("Rank checkpoint fragment does not match the SFT update.")
            return RankSftUpdate(staged, name, delta)
        except BaseException:
            self._failed = True
            raise


def commit_sft_update(checkpoint, plan, receipts, updates):
    """Host commits only after both authenticated owners report their staged results."""
    if (type(checkpoint) is not TwoRankCheckpoint or type(plan) is not ClipPlan
            or plan.attempt_id != checkpoint.attempt.attempt_id or plan.step != checkpoint.step
            or plan.owners != checkpoint.owners or type(updates) is not tuple or len(updates) != 2
            or any(type(update) is not RankSftUpdate or type(update.max_abs_update) is not float
                   or not math.isfinite(update.max_abs_update) or update.max_abs_update < 0
                   or (update.updated_parameter is None) != (update.max_abs_update == 0)
                   or (update.updated_parameter is not None
                       and update.updated_parameter not in plan.names(rank))
                   for rank, update in enumerate(updates))
            or not any(update.max_abs_update > 0 for update in updates)):
        raise CustodyError("SFT commit requires two rank results and a verified finite update.")
    decision = decide_global_clip(plan, receipts)
    for rank, update in enumerate(updates):
        try:
            claim = _read_record(_claim_path(checkpoint, plan, rank))
        except FileNotFoundError as error:
            raise CustodyError("Both rank optimizer claims must exist before commit.") from error
        if claim != _claim_record(plan, rank, decision):
            raise CustodyError("A rank started with different global clip evidence.")
        if update.updated_parameter is not None and not any(
            slot.name == update.updated_parameter and slot.value is not None and slot.value > 0
            for slot in decision.receipts[rank].slots
        ):
            raise CustodyError("Measured SFT update does not match the rank's gradient evidence.")
    return checkpoint.commit(tuple(update.staged for update in updates))
