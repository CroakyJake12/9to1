"""Host application entrypoint for the first approved joint v8 optimiser step.

The registered joint launcher owns worker identity, parsers, runtime, thermal
leases, pipes and teardown. This module wires the exact installed Flash-window
request and original pinned host sampler into that launcher; it never restores
Flash. An async park response alone is never proof that training can start.
"""
from dataclasses import dataclass
import math
from time import monotonic, sleep

from .artifacts import CustodyError
from .rank_checkpoint import TwoRankCheckpoint
from .rank_grad_clip import ClipPlan
from .v8_flash_window import FlashParkPending, acquire_flash, verify_flash_held
from .v8_joint_step import commit_v8_sft_step
from .v8_sampler import V8HostSampler


@dataclass(frozen=True, slots=True)
class RegisteredV8Sampler:
    dataset_root: object
    manifest_sha256: str
    seed: int
    seq_len: int
    grad_accum: int
    start_step: int
    resumed_python_rng: object = None


def run_first_owned_step(*, checkpoint, clip_plan, settings, owner, tokenizer,
                         verify_registered_launch, permit, request_window,
                         run_owned_ranks, park_deadline, socket_path=None,
                         clock=monotonic, wait=sleep):
    """Park Flash, prove expected owner and actual VRAM release, dispatch and commit.

    verify_registered_launch must attest pinned inputs/runtime, prior RNG and
    exactly-owned host/guest supervisor wiring. permit is the original
    signed cutoff/thermal admission, not a new deadline. run_owned_ranks takes
    a single unchanged original sampler on host, passes only input IDs to
    guest, keeps labels on RTX host, and returns exactly two owned receipts.
    """
    if (type(checkpoint) is not TwoRankCheckpoint or type(clip_plan) is not ClipPlan
            or type(settings) is not RegisteredV8Sampler or type(settings.start_step) is not int
            or checkpoint.step != settings.start_step + 1 or checkpoint.step != clip_plan.step
            or checkpoint.attempt.attempt_id != clip_plan.attempt_id or checkpoint.owners != clip_plan.owners
            or not callable(permit) or permit() is not True
            or not callable(verify_registered_launch) or verify_registered_launch() is not True
            or not callable(run_owned_ranks) or not callable(request_window)
            or type(park_deadline) not in (int, float) or not math.isfinite(park_deadline)
            or not clock() < park_deadline):
        raise CustodyError("The original SFT inputs or registered joint launch are not admitted.")
    checkpoint._validate_base()
    sampler = V8HostSampler(settings.dataset_root, tokenizer, manifest_sha256=settings.manifest_sha256,
                            seed=settings.seed, seq_len=settings.seq_len, grad_accum=settings.grad_accum,
                            start_step=settings.start_step, resumed_python_rng=settings.resumed_python_rng)

    def permitted():
        if permit() is not True:
            raise CustodyError("The original training cutoff or guarded run lease elapsed.")

    permitted()
    acquire_flash(request_window, owner, socket_path=socket_path)
    while True:
        permitted()
        if clock() >= park_deadline:
            raise CustodyError("Flash park was not verified before its admitted deadline.")
        try:
            verify_flash_held(request_window, owner, socket_path=socket_path)
            break
        except FlashParkPending:
            wait(min(0.25, max(0.0, park_deadline - clock())))

    def current_flash_release():
        permitted()
        verify_flash_held(request_window, owner, socket_path=socket_path)
        return True

    current_flash_release()
    results = run_owned_ranks(checkpoint=checkpoint, clip_plan=clip_plan,
                              read_original_batch=sampler.microbatch,
                              grad_accum=settings.grad_accum, seq_len=settings.seq_len,
                              mentor_quiesced=current_flash_release)
    if type(results) is not tuple or len(results) != 2:
        raise CustodyError("Joint launcher did not return both owned checkpoint receipts.")
    # Even if permit is revoked after the mutation, keep a valid existing
    # paired checkpoint. This only records evidence; no neural compute here.
    return commit_v8_sft_step(checkpoint, clip_plan, early=results[0], tail=results[1])
