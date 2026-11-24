"""Application-owned v8 worker binding and a local peer-credential lease channel.

A registered supervisor calls run_owned_binding() in the supervised worker
(or its broker-owned bootstrap). Bare CLI/SSH cannot create a binding. The
application never decides that an unobserved GPU or a missing Flash socket
is safe. Local grant server takes the live authority, not client flags.
"""
from contextlib import contextmanager
from contextvars import ContextVar
from dataclasses import dataclass, replace
import math
import socket
import struct
from time import monotonic
from uuid import UUID

from .artifacts import CustodyError, canonical_id
from .rank_stream import _read_exact, _write_all
from .v8_joint_registration import JointRegistration, RankProcessReceipt
from .v8_linux_process_owner import LinuxProcess as LinuxOwnedProcess, _read_process

_REQ = struct.Struct("!4s16sBQQ")  # op, attempt, logical rank, actual step, sequence
_ACK = struct.Struct("!4sQd")      # op, sequence, same-machine monotonic deadline
_MAX_GRANT_SECONDS = .5
_CURRENT = ContextVar("registered joint v8 worker", default=None)


def _identity(attempt):
    value = UUID(canonical_id(attempt))
    if value.int == 0:
        raise CustodyError("Invalid zero v8 owner attempt.")
    return value.bytes


def observe_process(pid):
    if type(pid) is not int or pid < 2:
        raise CustodyError("Invalid local owned process PID.")
    return _read_process(pid)


def _peer(connection):
    if type(connection) is not socket.socket or connection.family != socket.AF_UNIX:
        raise CustodyError("A private, already-owned local Unix connection is required.")
    pid, uid, _ = struct.unpack("3i", connection.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12))
    observed = observe_process(pid)
    if observed.uid != uid:
        raise CustodyError("The local process UID changed during owner binding.")
    return observed


def serve_local_rank_grants(connection, *, attempt, rank, expected_worker, authorize_live,
                            clock=monotonic):
    """Run by the local independent weight/thermal authority, not training rank.

    authorize_live(worker, attempt, rank, step) is the installed current
    cutoff, active guarded lease and both-model Flash absence proof, arriving
    through the supervisor's control channel as an absolute local expiry.
    It can return None to deny and must not use another machine's clock.
    This method grants ≤0.5s and never restores/reloads anything.
    """
    candidate = _peer(connection)
    if type(expected_worker) is not LinuxOwnedProcess or expected_worker != candidate:
        raise CustodyError("The request does not come from the original owned worker.")
    if rank not in (0, 1) or not callable(authorize_live):
        raise CustodyError("No independently owned live admission policy.")
    identity = _identity(attempt)
    with connection.makefile("rb") as inbound, connection.makefile("wb") as outbound:
        seq = 0
        while prefix := inbound.read(1):
            op, got, target, step, ordinal = _REQ.unpack(prefix + _read_exact(inbound, _REQ.size - 1))
            if (op != b"DVR1" or got != identity or target != rank or ordinal != seq or step == 0
                    or observe_process(candidate.pid) != candidate):
                raise CustodyError("The live v8 grant request is stale or outside its assigned rank.")
            authorized_end = authorize_live(candidate, attempt, rank, step)
            now = clock()
            if (type(authorized_end) not in (int, float) or not math.isfinite(authorized_end)
                    or authorized_end <= now):
                _write_all(outbound, _ACK.pack(b"DVE1", seq, 0))
                outbound.flush()
                return
            _write_all(outbound, _ACK.pack(b"DVA1", seq, min(authorized_end, now + _MAX_GRANT_SECONDS)))
            outbound.flush()
            seq += 1


class LocalRankGrant:
    """Worker client. Unix peer PID/UID/start and expiry are never remote claims."""

    def __init__(self, connection, *, attempt, rank, expected_authority, clock=monotonic):
        if type(expected_authority) is not LinuxOwnedProcess or _peer(connection) != expected_authority:
            raise CustodyError("The rank grant peer is not the pinned local supervisory owner.")
        self._sock, self._authority = connection, expected_authority
        self._in, self._out = connection.makefile("rb"), connection.makefile("wb")
        self._identity, self.rank, self._clock = _identity(attempt), rank, clock
        self._seq, self._step, self._expiry = 0, None, 0.0

    def close(self):
        self._in.close()
        self._out.close()
        self._sock.close()

    def _request(self):
        self._expiry = 0.0
        if self._step is None or observe_process(self._authority.pid) != self._authority:
            raise CustodyError("The original live grant authority is no longer available.")
        _write_all(self._out, _REQ.pack(b"DVR1", self._identity, self.rank, self._step, self._seq))
        self._out.flush()
        op, seq, end = _ACK.unpack(_read_exact(self._in, _ACK.size))
        if (op != b"DVA1" or seq != self._seq
                or not 0 < end - self._clock() <= _MAX_GRANT_SECONDS):
            raise CustodyError("No current original joint compute grant.")
        self._seq += 1
        self._expiry = end
        return end

    def check_admission(self, rank, step):
        if type(rank) is not int or rank != self.rank or type(step) is not int or not 0 < step < 2**64:
            raise CustodyError("Request outside the owned v8 rank step.")
        if self._step != step:
            self._step, self._expiry = step, 0.0
        if self._clock() >= self._expiry:
            self._request()
        return True

    def mentor_quiesced(self):
        return self._step is not None and self._clock() < self._expiry

    def wait_for_compute(self):
        return self._request()


@dataclass(frozen=True, slots=True)
class BoundRankWorker:
    registration: JointRegistration
    receipt: RankProcessReceipt
    admission: object
    grant: LocalRankGrant
    attest_process: object
    publish_completion: object

    def verify_owner(self, admission):
        return (self.admission is admission and self.attest_process(self.receipt) is True
                and self.grant.check_admission(self.receipt.logical_rank, admission.step) is True)


@contextmanager
def bind_worker(role, attempt, gpu_uuid):
    binding = _CURRENT.get()
    if (type(binding) is not BoundRankWorker or binding.admission.role != role
            or binding.receipt.attempt_id != attempt or binding.receipt.gpu_uuid != gpu_uuid
            or binding.verify_owner(binding.admission) is not True):
        raise CustodyError("This executable has no immutable registered joint owner.")
    yield binding


def run_owned_binding(binding):
    """Entry invoked from the exact pinned native/guest supervisor.

    Uses the actual prepared publisher inputs, RNG and owned channel ports
    carried by RegisteredRankRun. Applies the authenticated same-machine
    grant client to every rank gate, then calls the rank application.
    """
    from .v8_registered_rank import RegisteredRankRun, main

    if (type(binding) is not BoundRankWorker or type(binding.registration) is not JointRegistration
            or type(binding.receipt) is not RankProcessReceipt or type(binding.admission) is not RegisteredRankRun
            or type(binding.grant) is not LocalRankGrant or not callable(binding.attest_process)
            or not callable(binding.publish_completion)):
        raise CustodyError("No supervised joint rank binding.")
    admission, receipt = binding.admission, binding.receipt
    rank = 0 if admission.role == "early" else 1
    _, _, _, gpu, runtime, bundle = binding.registration.runtime_bindings[rank]
    if (admission.role not in ("early", "tail") or receipt.logical_rank != rank
            or receipt.gpu_uuid != admission.gpu_uuid or receipt.gpu_uuid != gpu
            or receipt.runtime_sha256 != runtime or receipt.bundle_sha256 != bundle
            or receipt.cuda_capability != ((7, 0), (12, 0))[rank] or receipt.visible_cuda_count != 1
            or receipt.attempt_id != admission.attempt_id or binding.grant.rank != rank):
        raise CustodyError("The process grant does not own the admitted rank or GPU.")
    ports = replace(admission.ports, check_admission=binding.grant.check_admission,
                    mentor_quiesced=binding.grant.mentor_quiesced)
    admission = replace(admission, ports=ports, local_control=binding.grant)
    scoped = replace(binding, admission=admission)
    token = _CURRENT.set(scoped)
    try:
        return main([admission.role, "--attempt", admission.attempt_id, "--gpu-uuid", admission.gpu_uuid])
    finally:
        _CURRENT.reset(token)
