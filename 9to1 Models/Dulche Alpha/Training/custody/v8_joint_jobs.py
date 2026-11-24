"""Owning Linux application supervisor for the separate v8 joint endpoint.

This owns Popen lifetimes and all six pipe ends. It does not mistake the SSH
client PID for a guest process, and it never authorizes Flash restoration.
The registered daemon supplies independent guest supervision and the original
signed job/deadline/guard facts. No use from the scalar TrainingJobs launcher.
"""
from contextlib import ExitStack
from dataclasses import asdict, dataclass
import json
import os
import selectors
import struct
import subprocess
from time import monotonic

from .artifacts import CustodyError
from .rank_stream import _read_exact, _write_all
from .v8_joint_registration import JointRegistration, RankProcessReceipt, verify_live_joint_processes
from .v8_linux_process_owner import LinuxRankHandle
from .v8_owned_endpoints import WORKER_ID, owned_endpoint_argv
from .v8_scoped_linux import ApprovedScopeLimits, pin_scoped_rank, scoped_worker_argv

_LENGTH = struct.Struct("!I")
_MAX_HELLO = 8192


def _read_bounded(stream, count, deadline):
    with selectors.DefaultSelector() as selector:
        selector.register(stream.fileno(), selectors.EVENT_READ)
        data = bytearray()
        while len(data) < count:
            remaining = deadline - monotonic()
            if remaining <= 0 or not selector.select(remaining):
                raise CustodyError("Joint owned child did not identify itself before deadline.")
            part = os.read(stream.fileno(), count - len(data))
            if not part:
                raise CustodyError("Joint owned child closed before its identity receipt.")
            data.extend(part)
        return bytes(data)


def _hello(child, deadline):
    n, = _LENGTH.unpack(_read_bounded(child.stdout, _LENGTH.size, deadline))
    if not 0 < n <= _MAX_HELLO:
        raise CustodyError("Owned rank startup receipt is oversized.")
    try:
        row = json.loads(_read_bounded(child.stdout, n, deadline))
        if type(row) is not dict or type(row.get("cuda_capability")) is not list:
            raise ValueError
        row["cuda_capability"] = tuple(row["cuda_capability"])
        return RankProcessReceipt(**row)
    except (TypeError, ValueError, RecursionError) as e:
        raise CustodyError("Malformed registered joint worker receipt.") from e


def identify_owned_worker(receipt, inbound, outbound):
    """Child sends real broker-derived identity, then waits before model use."""
    if type(receipt) is not RankProcessReceipt:
        raise CustodyError("No assigned owned rank identity for its startup channel.")
    data = json.dumps(asdict(receipt), separators=(",", ":"), allow_nan=False).encode("ascii")
    if not 0 < len(data) <= _MAX_HELLO:
        raise CustodyError("Owned rank identity is not bounded.")
    _write_all(outbound, _LENGTH.pack(len(data)))
    _write_all(outbound, data)
    outbound.flush()
    if _read_exact(inbound, 1) != b"G":
        raise CustodyError("Both jointly owned ranks have not been authorized.")


@dataclass(frozen=True, slots=True)
class OwnedJointChannels:
    native: subprocess.Popen
    guest: subprocess.Popen
    guest_weights: subprocess.Popen
    native_handle: LinuxRankHandle
    receipts: tuple[RankProcessReceipt, RankProcessReceipt]


def _launch(argv, log):
    return subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=log,
                            bufsize=0, close_fds=True, start_new_session=True, shell=False)


def _close_owned_child(child):
    # Terminating our SSH Popen is cleanup, never proof that a guest exited.
    for stream in (child.stdin, child.stdout):
        if stream is not None:
            stream.close()
    if child.poll() is None:
        child.terminate()
        try:
            child.wait(timeout=2)
        except subprocess.TimeoutExpired:
            child.kill()
            child.wait(timeout=2)


class JointTrainingJobs:
    def __init__(self, registration, *, native, guest, owner_home, known_hosts, native_uid,
                 native_scope_limits, live_admission, observe_guest, verify_weight_peer, native_log, guest_log,
                 weight_log):
        if (type(registration) is not JointRegistration or type(native_uid) is not int or native_uid < 0
                or type(native_scope_limits) is not ApprovedScopeLimits
                or not callable(live_admission) or not callable(observe_guest)
                or not callable(verify_weight_peer)):
            raise CustodyError("The signed v8 joint supervisor is not installed.")
        self.registry = registration
        self.native, self.guest, self.owner_home, self.known_hosts = native, guest, owner_home, known_hosts
        self.native_uid, self.native_scope_limits = native_uid, native_scope_limits
        self.live_admission, self.observe_guest, self.verify_weight_peer = live_admission, observe_guest, verify_weight_peer
        self.logs = native_log, guest_log, weight_log

    def run(self, attempt, *, launch_deadline, serve_owned_step, independent_teardown):
        """Own startup, send no step/data until both receipts, then run and stop.

        The registered worker bootstrap must send its bounded identity on its
        owner-held pipe, then wait for the exact one-byte proceed marker.
        independent_teardown is the daemon's host *and guest* teardown path;
        returning from this function is not a GPU release receipt.
        """
        if (not callable(serve_owned_step) or not callable(independent_teardown)
                or not monotonic() < launch_deadline):
            raise CustodyError("The joint first-start cutoff or supervisor is unavailable.")
        argv = owned_endpoint_argv(registration_id=WORKER_ID, attempt_id=attempt, native=self.native,
                                   guest=self.guest, approved_home=self.owner_home, known_hosts=self.known_hosts,
                                   verified_joint_launch=lambda a, n, g: self.live_admission(a, self.registry))
        argv = (scoped_worker_argv(argv[0], attempt=attempt, rank=1, limits=self.native_scope_limits), *argv[1:])
        children = []
        try:
            with ExitStack() as cleanup:
                for args, log in zip(argv, self.logs):
                    child = _launch(args, log)
                    children.append(child)
                    cleanup.callback(_close_owned_child, child)
                host, guest, weight = children
                host_receipt, guest_receipt = _hello(host, launch_deadline), _hello(guest, launch_deadline)
                handle = cleanup.enter_context(pin_scoped_rank(
                    host_receipt, host, expected_uid=self.native_uid, limits=self.native_scope_limits))
                receipts = verify_live_joint_processes(
                    self.registry, attempt, (guest_receipt, host_receipt),
                    observe_owned_process=lambda receipt: (
                        handle.confirms(receipt) if receipt.logical_rank == 1
                        else self.observe_guest(receipt, guest) is True))
                if (self.verify_weight_peer(weight, guest_receipt, launch_deadline) is not True
                        or self.live_admission(attempt, self.registry) is not True):
                    raise CustodyError("The original V100 weight channel or current compute admission is not verified.")
                for process in (host, guest):
                    _write_all(process.stdin, b"G")
                    process.stdin.flush()
                owned = OwnedJointChannels(host, guest, weight, handle, receipts)
                return serve_owned_step(owned)
        finally:
            if children:
                independent_teardown(attempt, tuple(children))
