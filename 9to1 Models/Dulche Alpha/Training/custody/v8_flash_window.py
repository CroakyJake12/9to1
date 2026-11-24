"""Exact installed Flash request wrapper and opt-in server restore hook.

Client wire formats are unchanged. The legacy same-UID endpoint cannot
authorize a two-rank release until both server hooks are installed. Server
registration, observers, lock, lineage and capacity guards are not client JSON.
"""
from dataclasses import dataclass
import re

from .artifacts import CustodyError
from .v8_joint_flash import JointFlashRelease

FLASH_ID = "qwen3.8-flash-next-unsloth-ud-iq4_xs"
_OWNER = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}")
_SHA = re.compile(r"[0-9a-f]{64}")
CLIENT_SHA256 = "416baadf51c1d12afa675d6e6cb90ce9bcabd903687ece91bc0f00f4161bc3c5"


def _owner(owner):
    if type(owner) is not str or not _OWNER.fullmatch(owner):
        raise CustodyError("The joint Flash window requires its exact registered lease owner.")


def _owned_reply(reply, owner):
    if type(reply) is not dict or reply.get("owner") != owner:
        raise CustodyError("Flash window response does not belong to this lease owner.")
    return reply


def acquire_flash(request, owner, *, socket_path=None):
    """Request the installed asynchronous park, never treat acceptance as held."""
    _owner(owner)
    return _owned_reply(request("acquire", owner=owner, models=[FLASH_ID], socket_path=socket_path), owner)


class FlashParkPending(CustodyError):
    """A verified existing owner is still in the installed releasing phase."""


def verify_flash_held(request, owner, *, socket_path=None):
    _owner(owner)
    result = _owned_reply(request("status", owner=owner, socket_path=socket_path), owner)
    if result.get("phase") == "releasing":
        raise FlashParkPending("The admitted Flash window is asynchronously releasing VRAM.")
    models = result.get("models")
    if (result.get("phase") != "held" or type(models) is not list or len(models) != 1
            or type(models[0]) is not dict or models[0].get("id") != FLASH_ID
            or models[0].get("prior_state") != "loaded"):
        raise CustodyError("Flash is not proven parked from this lease's required trained baseline.")
    released = models[0].get("release")
    if (type(released) is not dict or released.get("release_verified") is not True
            or type(released.get("remaining_owned_vram_mib")) is not int
            or released["remaining_owned_vram_mib"] != 0):
        raise CustodyError("Flash's original RTX allocation is not independently released.")
    return result


def verify_flash_restored(request, owner, *, socket_path=None):
    """202 is not a restore; verify the exact owner and model's final receipt."""
    _owner(owner)
    result = _owned_reply(request("status", owner=owner, socket_path=socket_path), owner)
    models = result.get("models")
    if (result.get("phase") != "restored" or type(models) is not list or len(models) != 1
            or type(models[0]) is not dict or models[0].get("id") != FLASH_ID
            or type(models[0].get("restore")) is not dict or models[0]["restore"].get("restored") is not True):
        raise CustodyError("The exact Flash model is not yet verified restored.")
    return result


@dataclass(frozen=True, slots=True)
class VerifiedTerminalLineage:
    # selected internally from the authenticated artifact store + terminal
    # outcome. At zero no optimiser checkpoint exists: prior sealed base.
    committed_step: int
    outcome: str
    recovery_sha256: str


def guard_joint_flash_transition(*, transition_lock, owner, proof, validate_owned_lease,
                                 require_no_local_training, read_terminal_lineage, collect_stop_receipts,
                                 restore_capacity, transition, phase):
    """Hook under the same registered owner/start lock used by ResourceWindows.

    Call before /release queues its asynchronous job and call independently
    from _run immediately before park.restore_native. Each call consumes fresh
    independent stop attestations, not client-submitted flags or an old proof.
    If the existing adapter owns a nonreentrant lock, call from outside it.
    All callbacks are installed authority methods; none originate in request
    JSON. No additional process can be admitted during the actual transition.
    """
    _owner(owner)
    if type(proof) is not JointFlashRelease or phase not in ("queue_release", "restore_native"):
        raise CustodyError("The installed endpoint has no registered joint Flash release capability.")
    with transition_lock:
        validate_owned_lease(owner)
        require_no_local_training()
        lineage = read_terminal_lineage(owner)
        if (type(lineage) is not VerifiedTerminalLineage or type(lineage.committed_step) is not int
                or lineage.committed_step < 0 or type(lineage.recovery_sha256) is not str
                or not _SHA.fullmatch(lineage.recovery_sha256)
                or (lineage.committed_step == 0 and lineage.outcome != "verified_zero_step_terminal")
                or (lineage.committed_step > 0 and lineage.outcome != "verified_joint_commit_terminal")):
            raise CustodyError("The server has no verified joint recovery point or zero-step terminal.")
        nonce, sha = proof.challenge(committed_checkpoint_sha256=lineage.recovery_sha256)
        proof.authorize(collect_stop_receipts(owner, nonce, sha))
        if phase == "restore_native":
            # The original fresh driver/thermal, free VRAM and >=70GiB host
            # guard still runs; do not replace or relax its installed policy.
            restore_capacity()
        return transition()


def request_joint_flash_release(request, owner, *, verify_installed_joint_binding, socket_path=None):
    """No release on an unpatched daemon; accepted does not mean restored.

    Binding must pin the registered server runtime and this exact lease. The
    endpoint independently takes both fresh proof gates above on its own. The
    callback here attests the endpoint installation, not process termination.
    """
    _owner(owner)
    if verify_installed_joint_binding(owner) is not True:
        raise CustodyError("Legacy Flash release cannot prove both training ranks have stopped.")
    return _owned_reply(request("release", owner=owner, socket_path=socket_path), owner)
