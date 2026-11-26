"""Fail-closed health and bounded cleanup for a two-device inference runtime.

This supplements the existing thermal guard; it never replaces it or kills a
process itself. Callers supply identity-pinned, independently timeout-aware
callbacks. A timed-out callback may still finish in its daemon thread, so its
process/unit identity must remain pinned even after this function returns.
"""
from __future__ import annotations

from dataclasses import dataclass
import math
import queue
import threading
import time
from collections.abc import Callable, Mapping, Sequence
from typing import Any


def _number(value: Any) -> bool:
    return type(value) in (int, float) and math.isfinite(value)


@dataclass(frozen=True)
class DevicePolicy:
    key: str
    expected_fields: tuple[tuple[str, str], ...]
    stop_c: float
    require_unthrottled: bool
    memory_stop_c: float | None = None
    power_limit_w: float | None = None

    def validate(self) -> None:
        if (not isinstance(self.key, str) or not self.key
                or not self.expected_fields
                or len(dict(self.expected_fields)) != len(self.expected_fields)
                or any(not isinstance(k, str) or not k or not isinstance(v, str)
                       or not v for k, v in self.expected_fields)
                or not _number(self.stop_c) or self.stop_c <= 0
                or type(self.require_unthrottled) is not bool):
            raise ValueError("Invalid device health policy")
        for value in (self.memory_stop_c, self.power_limit_w):
            if value is not None and (not _number(value) or value <= 0):
                raise ValueError("Invalid optional device health limit")


@dataclass(frozen=True)
class Health:
    reasons: tuple[str, ...]

    @property
    def ok(self) -> bool:
        return not self.reasons


def evaluate(snapshot: Any, policies: Sequence[DevicePolicy], *,
             now: float, max_age: float) -> Health:
    """Validate the published guard snapshot without reading or mutating state.

Reason codes are stable strings, prefixed by the relevant device key. Both
snapshot time and each device's last good sample must be within max_age.
"""
    if len(policies) != 2 or len({p.key for p in policies}) != 2:
        raise ValueError("A pool requires two distinct device policies")
    for policy in policies:
        policy.validate()
    if not _number(now) or not _number(max_age) or max_age <= 0:
        raise ValueError("Invalid health clock or age limit")
    if not isinstance(snapshot, Mapping):
        return Health(("snapshot.invalid",))
    failures: list[str] = []

    def timestamp(value: Any, prefix: str) -> None:
        if not _number(value):
            failures.append(prefix + ".timestamp_invalid")
        elif value > now:
            failures.append(prefix + ".timestamp_future")
        elif now - value >= max_age:
            failures.append(prefix + ".stale")

    timestamp(snapshot.get("ts"), "snapshot")
    if snapshot.get("halted") is not False:
        failures.append("guard.halted_or_unknown")
    devices = snapshot.get("devices")
    if not isinstance(devices, Mapping):
        return Health(tuple(failures + ["devices.invalid"]))
    for policy in policies:
        prefix = policy.key
        state = devices.get(prefix)
        if not isinstance(state, Mapping):
            failures.append(prefix + ".missing")
            continue
        if state.get("telemetry_ok") is not True:
            failures.append(prefix + ".telemetry_unavailable")
        if state.get("stopped") is not False:
            failures.append(prefix + ".stopped_or_unknown")
        # Older guard snapshots may omit these optional booleans. Once
        # present, an unknown or malformed value must never grant admission.
        if state.get("standby", False) is not False:
            failures.append(prefix + ".standby")
        timestamp(state.get("last_telemetry_ts"), prefix)
        reading = state.get("reading_full")
        if not isinstance(reading, Mapping):
            failures.append(prefix + ".reading_missing")
            continue
        if reading.get("simulated", False) is not False:
            failures.append(prefix + ".simulated")
        if any(reading.get(k) != v for k, v in policy.expected_fields):
            failures.append(prefix + ".identity_mismatch")
        if reading.get("driver_ok") is not True:
            failures.append(prefix + ".driver_unhealthy")
        if policy.require_unthrottled and reading.get("throttled") is not False:
            failures.append(prefix + ".throttled_or_unknown")
        for field, ceiling in (("temp", policy.stop_c),
                               ("temp_mem", policy.memory_stop_c)):
            if ceiling is not None:
                value = reading.get(field)
                if not _number(value):
                    failures.append(prefix + "." + field + "_invalid")
                elif value < 0 or value >= ceiling:
                    failures.append(prefix + "." + field + "_outside_limit")
        if policy.power_limit_w is not None:
            value = reading.get("power_limit_active")
            if not _number(value) or value != policy.power_limit_w:
                failures.append(prefix + ".power_policy_mismatch")
    return Health(tuple(failures))


@dataclass(frozen=True)
class Outcome:
    trigger: str
    health: Health | None
    stop_verified: bool
    failures: Mapping[str, BaseException]


def _bounded(callbacks: Mapping[str, Callable[[float], Any]], timeout: float
             ) -> tuple[dict[str, Any], dict[str, BaseException]]:
    """Invoke every callback once; wait at most one shared timeout."""
    completed: queue.Queue = queue.Queue()
    deadline = time.monotonic() + timeout

    def invoke(name: str, callback: Callable[[float], Any]) -> None:
        try:
            completed.put((name, True, callback(max(0.0, deadline - time.monotonic()))))
        except BaseException as exc:
            completed.put((name, False, exc))

    for name, callback in callbacks.items():
        threading.Thread(target=invoke, args=(name, callback), daemon=True).start()
    results: dict[str, Any] = {}
    errors: dict[str, BaseException] = {}
    pending = set(callbacks)
    while pending:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            break
        try:
            name, success, value = completed.get(timeout=remaining)
        except queue.Empty:
            break
        pending.remove(name)
        (results if success else errors)[name] = value
    errors.update({name: TimeoutError("Callback deadline exceeded") for name in pending})
    return results, errors


def supervise(*, policies: Sequence[DevicePolicy], max_age: float,
              read_snapshot: Callable[[float], Any],
              process_alive: Callable[[float], bool],
              stop: Mapping[str, Callable[[float], Any]],
              verify_stopped: Mapping[str, Callable[[float], bool]],
              cancelled: threading.Event, run_timeout: float, interval: float,
              callback_timeout: float, cleanup_timeout: float) -> Outcome:
    """Watch both devices; on every exit, stop and verify both owned endpoints.

The endpoint callback keys must match the two policy keys. Callbacks accept
their remaining timeout in seconds. Stops run concurrently, exactly once each;
failed or hung stops cannot skip their peer. Total cleanup wait is bounded by
    cleanup_timeout. stop_verified requires two explicit True verification results
    and no cleanup callback failures, including stop timeouts. An earlier read
    error remains in failures even when cleanup is subsequently proven.
"""
    evaluate({}, policies, now=time.time(), max_age=max_age)  # Validate policy first.
    required = {p.key for p in policies}
    if set(stop) != required or set(verify_stopped) != required:
        raise ValueError("Stop and verification must cover both devices exactly")
    for value in (run_timeout, interval, callback_timeout, cleanup_timeout):
        if not _number(value) or value <= 0:
            raise ValueError("Supervisor durations must be finite and positive")
    deadline = time.monotonic() + run_timeout
    errors: dict[str, BaseException] = {}
    health = None
    trigger = "deadline"
    try:
        while time.monotonic() < deadline:
            if cancelled.is_set():
                trigger = "cancelled"
                break
            values, failed = _bounded({"read": read_snapshot, "alive": process_alive},
                                      min(callback_timeout, deadline - time.monotonic()))
            if failed:
                errors.update(failed)
                expired = time.monotonic() >= deadline
                trigger = ("deadline" if expired and all(isinstance(e, TimeoutError)
                           for e in failed.values()) else "callback_failure")
                break
            if values["alive"] is not True:
                trigger = "process_exited" if values["alive"] is False else "process_state_invalid"
                break
            health = evaluate(values["read"], policies, now=time.time(), max_age=max_age)
            if not health.ok:
                trigger = "health_failure"
                break
            cancelled.wait(min(interval, max(0.0, deadline - time.monotonic())))
    except BaseException as exc:
        errors["supervisor"] = exc
        trigger = "supervisor_failure"
    finally:
        _, failed = _bounded({"stop:" + k: v for k, v in stop.items()}, cleanup_timeout / 2)
        errors.update(failed)
        verified, failed = _bounded({"verify:" + k: v for k, v in verify_stopped.items()}, cleanup_timeout / 2)
        errors.update(failed)
    cleanup_failed = any(k.startswith(("stop:", "verify:")) for k in errors)
    stopped = not cleanup_failed and all(verified.get("verify:" + k) is True for k in required)
    return Outcome(trigger, health, stopped, errors)
