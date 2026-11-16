#!/usr/bin/env python3
"""Preparation only: strict safety policy and fixed CPU lifecycle fixtures.

There is deliberately no GPU launcher or configurable subprocess command here.
The CPU ledger below is NOT the real cross-OS proof authorization authority.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import math
import os
from pathlib import Path
import selectors
import signal
import struct
import subprocess
import sys
import time
import uuid

SAMPLE_NS = 250_000_000
STALE_NS = 750_000_000
WORK_NS = 25_000_000_000
TOTAL_NS = 30_000_000_000


class Refusal(RuntimeError):
    pass


def require_real_proof() -> None:
    """No argument, configuration file or environment variable enables this."""
    raise Refusal("REAL_PROOF_DISABLED: cross-OS custody, guest telemetry and stop gates are unverified")


class SafetyPolicy:
    """Validate samples already translated to this supervisor's monotonic clock.

    A guest's monotonic timestamp cannot be passed directly. A future trusted
    acquisition protocol must bound collection/transport delay; receiving old
    measurements and assigning a new host timestamp is NOT sufficient.
    """
    def __init__(self, session: str, start_ns: int, identities: dict[str, str]):
        if not isinstance(session, str) or not session or set(identities) != {"v100", "rtx5080"}:
            raise ValueError("session and exact GPU identities are required")
        if (not all(isinstance(identity, str) and identity for identity in identities.values())
                or len(set(identities.values())) != 2):
            raise ValueError("GPU identities must be nonempty and distinct")
        if type(start_ns) is not int or start_ns < 0:
            raise ValueError("start time must be a nonnegative monotonic integer")
        self.session = session
        self.start_ns = start_ns
        self.identities = dict(identities)
        self.last_sample_ns = None
        self.sequence = -1
        self.initial_v100 = None
        self.stopped = None
        self.last_tick_ns = start_ns

    def refuse(self, reason: str) -> None:
        self.stopped = reason
        raise Refusal(reason)

    def tick(self, now_ns: int) -> None:
        if self.stopped:
            raise Refusal(self.stopped)
        if type(now_ns) is not int or now_ns < self.last_tick_ns:
            self.refuse("monotonic_clock_invalid")
        self.last_tick_ns = now_ns
        if now_ns - self.start_ns >= WORK_NS:
            self.refuse("work_deadline")
        reference = self.last_sample_ns if self.last_sample_ns is not None else self.start_ns
        if now_ns - reference > STALE_NS:
            self.refuse("telemetry_stale")

    def accept(self, sample: object, now_ns: int) -> None:
        self.tick(now_ns)
        if not isinstance(sample, dict) or set(sample) != {"session", "sequence", "acquired_ns", "gpus"}:
            self.refuse("malformed_telemetry")
        if sample["session"] != self.session:
            self.refuse("wrong_session")
        sequence = sample["sequence"]
        acquired = sample["acquired_ns"]
        if type(sequence) is not int or sequence != self.sequence + 1:
            self.refuse("replayed_or_missing_sample")
        if type(acquired) is not int or acquired < self.start_ns or acquired > now_ns:
            self.refuse("invalid_sample_time")
        if self.last_sample_ns is not None and acquired <= self.last_sample_ns:
            self.refuse("replayed_sample_time")
        if now_ns - acquired > STALE_NS:
            self.refuse("telemetry_stale")
        gpus = sample["gpus"]
        if not isinstance(gpus, dict) or set(gpus) != set(self.identities):
            self.refuse("missing_gpu_telemetry")
        temperatures = {}
        for role, identity in self.identities.items():
            gpu = gpus[role]
            if not isinstance(gpu, dict) or set(gpu) != {"uuid", "temperature_c"} or gpu["uuid"] != identity:
                self.refuse("wrong_gpu_identity")
            temperature = gpu["temperature_c"]
            if (type(temperature) not in (int, float) or not 0 <= temperature <= 150
                    or not math.isfinite(temperature)):
                self.refuse("invalid_temperature")
            temperatures[role] = temperature
        v100 = temperatures["v100"]
        if self.initial_v100 is None:
            if v100 > 40:
                self.refuse("v100_start_too_hot")
            self.initial_v100 = v100
        if v100 >= 55 or v100 - self.initial_v100 >= 10:
            self.refuse("v100_thermal_limit")
        if temperatures["rtx5080"] >= 75:
            self.refuse("rtx5080_thermal_limit")
        self.last_sample_ns = acquired
        self.sequence = sequence


def consume_fixture_record(directory: Path, session: str) -> str:
    """Durable exclusive CPU fixture record, written BEFORE fixture dispatch.

    Existing, partial or unreadable records are never reset. File creation and
    fsync provide crash/concurrency properties, not tamper-proof immutability.
    This function MUST NOT be used for the real allowance or a copied authority.
    """
    directory = Path(directory)
    if directory.is_symlink() or not directory.is_dir():
        raise Refusal("fixture ledger must be an existing real directory")
    directory_fd = os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    payload = json.dumps({"schema": "dulche.cpu-fixture-consumed/v1", "session": session,
                          "real_proof": False, "consumed_before_dispatch": True},
                         sort_keys=True).encode() + b"\n"
    try:
        fd = os.open("CPU-FIXTURE-CONSUMED.json", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                     0o400, dir_fd=directory_fd)
        try:
            view = memoryview(payload)
            while view:
                written = os.write(fd, view)
                if not written:
                    raise OSError("short ledger write")
                view = view[written:]
            os.fsync(fd)
        finally:
            os.close(fd)
        os.fsync(directory_fd)
    except FileExistsError as error:
        raise Refusal("fixture_allowance_already_consumed") from error
    finally:
        os.close(directory_fd)
    return hashlib.sha256(payload).hexdigest()


def _parent_death(expected_parent: int) -> None:
    """Linux fixture only: kill this child if its owning supervisor dies."""
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(1, signal.SIGKILL, 0, 0, 0) != 0 or os.getppid() != expected_parent:
        os._exit(126)


def _emit(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}), flush=True)


def _fixture_environment() -> dict[str, str]:
    """No inherited Python hooks, loader injection or accelerator settings."""
    return {"PATH": "/usr/bin:/bin", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
            "CUDA_VISIBLE_DEVICES": "-1", "NVIDIA_VISIBLE_DEVICES": "none"}


def _primary(session: str, parent: int, scenario: str) -> None:
    _parent_death(parent)
    sequence = 0
    while True:
        sample = {"session": session, "sequence": sequence, "acquired_ns": time.monotonic_ns(),
                  "gpus": {"v100": {"uuid": "CPU-FIXTURE-V100", "temperature_c": 35},
                           "rtx5080": {"uuid": "CPU-FIXTURE-5080", "temperature_c": 45}}}
        if sequence > 0:
            if scenario == "malformed":
                print("not-json", flush=True)
                time.sleep(60)
            if scenario == "replay":
                sample["sequence"] = 0
            if scenario == "hot":
                sample["gpus"]["v100"]["temperature_c"] = 55
            if scenario == "silence":
                time.sleep(60)
        print(json.dumps(sample), flush=True)
        sequence += 1
        time.sleep(SAMPLE_NS / 1e9)


def _worker(parent: int, heartbeat_fd: int, start_ns: int) -> None:
    """Fixed CPU sleeper with its own absolute and parent-liveness deadlines.

    This models a future guest-side lease. It does NOT contain/stop a GPU or VM.
    Heartbeats carry this host's acquisition timestamp, never just arrival time.
    """
    _parent_death(parent)
    last_acquired = start_ns
    buffer = b''
    with selectors.DefaultSelector() as selector:
        selector.register(heartbeat_fd, selectors.EVENT_READ)
        while True:
            now = time.monotonic_ns()
            if now - start_ns >= WORK_NS or now - last_acquired > STALE_NS:
                return
            for _, _ in selector.select(timeout=0.025):
                data = os.read(heartbeat_fd, 4096)
                if not data:
                    return
                buffer += data
                if len(buffer) > 4096:
                    return
                while len(buffer) >= 8:
                    acquired, = struct.unpack('!Q', buffer[:8])
                    buffer = buffer[8:]
                    now = time.monotonic_ns()
                    if (acquired <= last_acquired or acquired > now
                            or now - acquired > STALE_NS):
                        return
                    last_acquired = acquired


def _stop_owned(child: subprocess.Popen | None, total_deadline_ns: int) -> bool:
    if child is None:
        return True
    # These are direct fixed fixture children. No arbitrary PID/name selection.
    if child.poll() is None:
        child.terminate()
        remaining = max(0, (total_deadline_ns - time.monotonic_ns()) / 1e9)
        try:
            child.wait(timeout=min(0.2, remaining))
        except subprocess.TimeoutExpired:
            child.kill()
    remaining = max(0, (total_deadline_ns - time.monotonic_ns()) / 1e9)
    try:
        child.wait(timeout=remaining)
        return True
    except subprocess.TimeoutExpired:
        return False


def run_cpu_fixture(directory: Path, scenario: str) -> int:
    """Exercise real Linux child lifecycle with no GPU/library/network access."""
    session = str(uuid.uuid4())
    receipt_hash = consume_fixture_record(directory, session)
    start_ns = time.monotonic_ns()  # Before primary/worker dispatch; never reset.
    deadline_ns = start_ns + TOTAL_NS
    policy = SafetyPolicy(session, start_ns, {"v100": "CPU-FIXTURE-V100", "rtx5080": "CPU-FIXTURE-5080"})
    primary = worker = None
    heartbeat_read = heartbeat_write = None
    selector = selectors.DefaultSelector()
    reason = "fixture_error"
    status = 1
    try:
        primary = subprocess.Popen([sys.executable, "-I", "-S", "-B", str(Path(__file__).resolve()), "__primary",
                                    session, str(os.getpid()), scenario], stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, start_new_session=True,
                                   env=_fixture_environment())
        _emit("primary", pid=primary.pid, session=session, receipt_sha256=receipt_hash)
        os.set_blocking(primary.stdout.fileno(), False)
        selector.register(primary.stdout, selectors.EVENT_READ)
        buffer = b""
        while True:
            policy.tick(time.monotonic_ns())
            if worker is not None and worker.poll() is not None:
                policy.tick(time.monotonic_ns())
                raise Refusal("worker_exited")
            for key, _ in selector.select(timeout=0.025):
                data = os.read(key.fd, 4096)
                if not data:
                    raise Refusal("primary_guard_lost")
                buffer += data
                if len(buffer) > 8192:
                    raise Refusal("telemetry_frame_too_large")
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    try:
                        sample = json.loads(line)
                    except (ValueError, UnicodeError):
                        raise Refusal("malformed_telemetry") from None
                    policy.accept(sample, time.monotonic_ns())
                    if worker is None:
                        # Fixed CPU sleeper only; there is no user-supplied command.
                        heartbeat_read, heartbeat_write = os.pipe2(os.O_CLOEXEC | os.O_NONBLOCK)
                        worker = subprocess.Popen([sys.executable, "-I", "-S", "-B", str(Path(__file__).resolve()),
                                                   "__worker", str(os.getpid()), str(heartbeat_read), str(start_ns)],
                                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                                                  start_new_session=True, pass_fds=(heartbeat_read,),
                                                  env=_fixture_environment())
                        os.close(heartbeat_read)
                        heartbeat_read = None
                        _emit("worker", pid=worker.pid)
                    # Never block the supervisor on a frozen worker. Failure to
                    # deliver an atomic, bounded heartbeat enters cleanup.
                    if os.write(heartbeat_write, struct.pack('!Q', policy.last_sample_ns)) != 8:
                        raise Refusal('worker_heartbeat_failed')
    except Refusal as error:
        reason = str(error)
        status = 0
    finally:
        for fd in (heartbeat_read, heartbeat_write):
            if fd is not None:
                os.close(fd)
        worker_stopped = _stop_owned(worker, deadline_ns)
        primary_stopped = _stop_owned(primary, deadline_ns)
        stopped = worker_stopped and primary_stopped
        selector.close()
        if primary is not None and primary.stdout is not None:
            primary.stdout.close()
        elapsed = time.monotonic_ns() - start_ns
        _emit("stopped", reason=reason, stopped=stopped, elapsed_ns=elapsed,
              within_total_deadline=elapsed <= TOTAL_NS, real_proof=False)
        if not stopped or elapsed > TOTAL_NS:
            status = 1
    return status


def main() -> int:
    if len(sys.argv) > 1 and sys.argv[1] == "__worker":
        _worker(int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]))
        return 0
    if len(sys.argv) > 1 and sys.argv[1] == "__primary":
        _primary(sys.argv[2], int(sys.argv[3]), sys.argv[4])
        return 1
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cpu-fixture", type=Path)
    parser.add_argument("--scenario", choices=("normal", "hot", "malformed", "replay", "silence"), default="normal")
    args = parser.parse_args()
    try:
        if args.cpu_fixture is None:
            require_real_proof()
        return run_cpu_fixture(args.cpu_fixture, args.scenario)
    except (Refusal, OSError) as error:
        print(str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
