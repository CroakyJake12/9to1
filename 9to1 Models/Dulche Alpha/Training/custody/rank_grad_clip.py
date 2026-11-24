"""Ordered two-rank evidence for v8's global L2 gradient clip (max_norm=1)."""

from dataclasses import dataclass
import math

from .artifacts import CustodyError, canonical_id

_DTYPES = {"float32", "float16", "bfloat16", "float64"}


@dataclass(frozen=True, slots=True)
class GradientSlot:
    rank: int
    name: str


@dataclass(frozen=True, slots=True)
class ClipPlan:
    attempt_id: str
    step: int
    owners: tuple[str, str]
    slots: tuple[GradientSlot, ...]  # original optimizer parameter order

    def __post_init__(self):
        if (canonical_id(self.attempt_id) == "00000000-0000-0000-0000-000000000000"
                or type(self.step) is not int or self.step <= 0
                or type(self.owners) is not tuple or len(self.owners) != 2
                or any(not _name(owner) for owner in self.owners) or self.owners[0] == self.owners[1]
                or type(self.slots) is not tuple or not 0 < len(self.slots) <= 16384
                or any(type(slot) is not GradientSlot or type(slot.rank) is not int or slot.rank not in (0, 1)
                       or not _name(slot.name) for slot in self.slots)
                or len({slot.name for slot in self.slots}) != len(self.slots)):
            raise CustodyError("Invalid global gradient clip plan.")

    def names(self, rank: int) -> tuple[str, ...]:
        return tuple(slot.name for slot in self.slots if slot.rank == rank)


def _name(value):
    return type(value) is str and 0 < len(value) <= 256 and value == value.strip()


@dataclass(frozen=True, slots=True)
class NormSlot:
    name: str
    dtype: str | None
    value: float | None


@dataclass(frozen=True, slots=True)
class NormReceipt:
    attempt_id: str
    step: int
    rank: int
    owner: str
    slots: tuple[NormSlot, ...]


@dataclass(frozen=True, slots=True)
class ClipDecision:
    receipts: tuple[NormReceipt, NormReceipt]
    dtype: str
    norm: float


def _ordered_norms(plan: ClipPlan, receipts: tuple[NormReceipt, NormReceipt]) -> tuple[NormSlot, ...]:
    if type(plan) is not ClipPlan or type(receipts) is not tuple or len(receipts) != 2:
        raise CustodyError("Both ordered rank receipts are required for clipping.")
    by_name = {}
    for rank, receipt in enumerate(receipts):
        if (type(receipt) is not NormReceipt or receipt.attempt_id != plan.attempt_id
                or type(receipt.step) is not int or receipt.step != plan.step
                or type(receipt.rank) is not int or receipt.rank != rank or receipt.owner != plan.owners[rank]
                or type(receipt.slots) is not tuple
                or any(type(slot) is not NormSlot for slot in receipt.slots)
                or tuple(slot.name for slot in receipt.slots) != plan.names(rank)):
            raise CustodyError("A gradient norm receipt does not belong to this attempt, step or rank.")
        for slot in receipt.slots:
            if slot.dtype is None and slot.value is None:
                continue
            if (type(slot.dtype) is not str or slot.dtype not in _DTYPES or type(slot.value) is not float
                    or not math.isfinite(slot.value) or slot.value < 0):
                raise CustodyError("A gradient norm was invalid or nonfinite.")
            by_name[slot.name] = slot
    return tuple(by_name[slot.name] for slot in plan.slots if slot.name in by_name)


def decide_global_clip(plan: ClipPlan, receipts: tuple[NormReceipt, NormReceipt]) -> ClipDecision:
    """Authenticated owners supply scalar evidence; no tensors cross this API."""
    slots = _ordered_norms(plan, receipts)
    import torch

    ordered = []
    for slot in slots:
        scalar = torch.tensor(slot.value, dtype=getattr(torch, slot.dtype), device="cpu")
        if scalar.item() != slot.value:
            raise CustodyError("A gradient norm does not match its claimed dtype.")
        ordered.append(scalar)
    try:
        # Scalar L2 is identity for these nonnegative values. This preserves
        # PyTorch's own dtype grouping/promotion and final vector norm.
        total = torch.nn.utils.get_total_norm(ordered, 2.0, error_if_nonfinite=True)
    except RuntimeError as error:
        raise CustodyError("The global gradient norm is nonfinite.") from error
    return ClipDecision(receipts, str(total.dtype).removeprefix("torch."), total.item())


class PreparedRankClip:
    """One rank's post-accumulation evidence and one-shot local clip application."""

    def __init__(self, plan: ClipPlan, *, rank: int, parameters: tuple):
        import torch

        if (type(plan) is not ClipPlan or type(rank) is not int or rank not in (0, 1)
                or type(parameters) is not tuple
                or any(type(row) is not tuple or len(row) != 2 or not _name(row[0])
                       or not isinstance(row[1], torch.nn.Parameter) for row in parameters)
                or tuple(name for name, _ in parameters) != plan.names(rank)
                or len({id(param) for _, param in parameters}) != len(parameters)):
            raise CustodyError("The local parameters do not match the admitted gradient order.")
        self._plan, self._rank = plan, rank
        self._prepared = tuple((param, param.grad, param.grad._version if param.grad is not None else None)
                               for _, param in parameters)
        self._consumed = False
        slots = []
        for (name, _), (_, grad, _) in zip(parameters, self._prepared):
            if grad is None:
                slots.append(NormSlot(name, None, None))
                continue
            dtype = str(grad.dtype).removeprefix("torch.")
            if grad.layout != torch.strided or dtype not in _DTYPES:
                raise CustodyError("Global clipping requires a supported dense floating gradient.")
            value = torch.linalg.vector_norm(grad.detach(), 2.0).item()
            if not math.isfinite(value):
                raise CustodyError("A local gradient norm is nonfinite.")
            slots.append(NormSlot(name, dtype, value))
        self.receipt = NormReceipt(plan.attempt_id, plan.step, rank, plan.owners[rank], tuple(slots))

    def apply(self, decision: ClipDecision) -> None:
        """Apply exactly the native max_norm=1 coefficient, never an optimizer step."""
        import torch

        if self._consumed:
            raise CustodyError("This local clip was already consumed; do not replay.")
        if (type(decision) is not ClipDecision
                or decide_global_clip(self._plan, decision.receipts) != decision
                or decision.receipts[self._rank] != self.receipt):
            raise CustodyError("The global clip decision does not bind this rank's gradient evidence.")
        if any(param.grad is not grad or (grad is not None and grad._version != version)
               for param, grad, version in self._prepared):
            raise CustodyError("A local gradient changed after its norm was prepared.")
        self._consumed = True
        total = torch.tensor(decision.norm, dtype=getattr(torch, decision.dtype), device="cpu")
        # Native clipping does max_norm/(total_norm+1e-6), clamp(max=1.0),
        # then multiplies only existing gradients. Holding partial local updates
        # after a runtime failure is caller recovery, not an atomic optimizer step.
        torch.nn.utils.clip_grads_with_norm_([param for param, _, _ in self._prepared], 1.0, total)
