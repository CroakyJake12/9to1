"""serve/thermal_guard.py - the thermal watchdog for the dual-GPU fork (RTX 5080 host + Tesla V100 remote).

WHY A SEPARATE PROCESS: the mission's safety rule is that closing, crashing or freezing the desktop UI must not
disable protection.  This daemon is therefore standalone: stdlib only, no Qt, no web app, no dependency on
serve/server.py being alive.  Start it beside the server (`python3 -m serve.thermal_guard --config guard.json`),
under systemd/supervisor, and it keeps watching even if the UI is gone.

WHAT IT DOES
  * reads each device every `interval_s` through a DeviceSource (local NVML, or the TCP JSON telemetry of
    tools/v100_agent.py for the V100 running under ITS OWN driver stack inside the VM);
  * WARN at `warn_c`, STOP inference at `stop_c` (hysteresis `clear_c` below the stop point before re-arming);
  * STOP on a dangerous RISE (`rise_c_per_min` sustained over `rise_window_s`) and on a reading that turns
    unreadable (`unreadable_timeout_s`) - a dead sensor is not a cold card;
  * STOP a REMOTE device when its telemetry stream times out (`telemetry_timeout_s`) - the V100 has no operator
    inside the VM, so a lost link means "assume the worst and stop";
  * stopping means stopping INFERENCE: the device's `stop_cmd` (argv list, optional `{index}` placeholder) and/or
    `stop_url` (HTTP POST, e.g. Strata's /api/thermal_stop).  It never claims to power the GPU off;
  * applies and verifies power limits ONLY inside the range the driver itself advertises
    (nvmlDeviceGetPowerManagementLimitConstraints); refuses, with a log line, anything outside it;
  * logs every event as one JSON object per line (`--log`), and publishes an atomic status JSON (`--status-file`)
    that the web app's /gpus endpoint forwards - UI is a window, never the guard.

WHAT IT NEVER DOES: substitute GPU temperature for a memory/HBM reading the card cannot give (the V100 reports
temp_mem = null and the daemon never invents one); run hardware loads; pretend a stop is a power-off.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import socket
import subprocess
import sys
import time
import urllib.request

# ---------------------------------------------------------------- sources

class NvmlSource:
    """A card in THIS driver environment (host: RTX 5080).  `device` is injectable for tests."""

    kind = "nvml"

    def __init__(self, index=0, device=None):
        self.index = index
        if device is None:
            from serve.telemetry import NvmlDevice
            device = NvmlDevice(index)
        self.device = device

    def read(self):
        return self.device.read()

    def set_power_limit(self, watts):
        return self.device.set_power_limit(watts)


class RemoteSource:
    """One line of JSON per connect, produced by tools/v100_agent.py (or --simulate for safety tests).
    Connection failures read as `None` - the timeout rule decides what that means."""

    kind = "remote"

    def __init__(self, host, port, timeout=2.0):
        self.host, self.port, self.timeout = host, port, timeout

    def read(self):
        import socket
        try:
            with socket.create_connection((self.host, self.port), timeout=self.timeout) as s:
                s.sendall(b"telemetry\n")
                buf = b""
                while b"\n" not in buf:
                    chunk = s.recv(65536)
                    if not chunk:
                        return None
                    buf += chunk
                return json.loads(buf.split(b"\n", 1)[0].decode())
        except (OSError, ValueError):
            return None

    def set_power_limit(self, watts):
        """Same (applied, error) tuple contract as NvmlSource - the guard unpacks it."""
        import socket
        try:
            with socket.create_connection((self.host, self.port), timeout=self.timeout) as s:
                s.sendall(("power-limit %g\n" % watts).encode())
                reply = s.recv(4096)
                r = json.loads(reply.decode().split("\n", 1)[0])
                return r.get("applied"), r.get("error")
        except (OSError, ValueError):
            return None, "agent unreachable"


# ---------------------------------------------------------------- guard

def _finite_number(value):
    return type(value) in (int, float) and math.isfinite(value)


def _power_readback_error(rule, reading, now):
    """Validate a completed hardware sample, never a setter acknowledgement or poll timestamp.

    The recovery path independently pins the guest GPU UUID. Its current NVML agent does not
    publish that UUID, so this check uses the same private endpoint and validates its real V100
    identity and cap. No claimed UUID or substitute HBM temperature is manufactured here.
    """
    if reading is None:
        return "power-limit telemetry unavailable"
    if not isinstance(reading, dict):
        return "malformed power-limit telemetry"
    if reading.get("simulated") is True:
        return "simulated power-limit telemetry"
    ts = reading.get("ts")
    if not _finite_number(ts) or not _finite_number(now):
        return "power-limit hardware sample has no finite timestamp"
    if ts > now:
        return "power-limit hardware sample is future-dated"
    if now - ts >= 5.0:
        return "power-limit hardware sample is stale"
    if reading.get("simulated") is not False:
        return "power-limit hardware provenance is missing or malformed"
    if reading.get("driver_ok") is not True:
        return "power-limit hardware driver is unavailable"
    if not _finite_number(reading.get("temp")):
        return "power-limit sample has no finite core temperature"
    memory = reading.get("temp_mem")
    if memory is not None and not _finite_number(memory):
        return "power-limit sample has malformed memory temperature"
    if rule.id == "v100" and rule.kind == "remote":
        if reading.get("name") != "Tesla V100-PCIE-16GB" or not _finite_number(memory):
            return "power-limit sample lacks the expected V100 and real HBM reading"
    requested = rule.power_limit_w
    if not _finite_number(requested) or requested <= 0:
        return "configured power limit is invalid"
    active = reading.get("power_limit_active")
    if not _finite_number(active) or active != requested:
        return "active power limit does not match the configured cap"
    limits = reading.get("power_limit_range")
    if (not isinstance(limits, (list, tuple)) or len(limits) != 2
            or not all(_finite_number(x) for x in limits)
            or not 0 < limits[0] <= requested <= limits[1]):
        return "power limit is outside a verified driver range"
    return None


class Rule:
    def __init__(self, cfg):
        self.id = cfg["id"]
        self.kind = cfg.get("kind", "nvml")
        self.warn_c = cfg.get("warn_c")
        self.stop_c = cfg.get("stop_c")
        self.core_grace_s = cfg.get("core_grace_s", 0.0)
        self.core_emergency_c = cfg.get("core_emergency_c", self.stop_c)
        self.memory_stop_c = cfg.get("memory_stop_c", self.stop_c)
        self.jump_min_c = cfg.get("jump_min_c", 50.0)
        self.policy_version = cfg.get("thermal_policy_version")
        if self.policy_version is not None:
            if (self.policy_version != "active-cooling-v1"
                    or self.id not in ("v100", "rtx5080")
                    or self.warn_c != 75 or self.stop_c != 80 or self.core_grace_s != 10
                    or self.core_emergency_c != (82 if self.id == "v100" else 85)
                    or self.memory_stop_c != (68 if self.id == "v100" else 80)
                    or cfg.get("allow_simulated") is not False
                    or (self.id == "v100" and cfg.get("power_limit_w") != 165)):
                raise ValueError("Unqualified active-cooling policy")
        elif self.core_grace_s != 0 or self.core_emergency_c != self.stop_c or self.memory_stop_c != self.stop_c:
            raise ValueError("Thermal delay or changed emergency limit requires the qualified policy")
        self.clear_c = cfg.get("clear_c", 5.0)            # re-arm this far below stop_c
        self.rise_c_per_min = cfg.get("rise_c_per_min")   # sustained climb that stops work above the floor
        self.rise_window_s = cfg.get("rise_window_s", 30.0)
        # Rise detection is for RUNAWAY heat, not for legitimate load ramps. Measured 2026-11-20: loading a
        # 14B model (8.4 GiB over PCIe x4, ~190 W) climbs this V100 31->45 C in a minute - 27 C/min at an
        # entirely safe 45 C - and a hard-coded 45 C floor made the guard kill three healthy VMs mid-bench.
        # Only climbs AT/ABOVE this absolute floor stop work; the stop_c ceiling still fires regardless.
        self.rise_min_c = float(cfg.get("rise_min_c", 45.0))
        self.telemetry_timeout_s = cfg.get("telemetry_timeout_s", 10.0)
        # FAIL-CLOSED rule: a device that has NEVER produced one valid reading is not "fine", it is unproven.
        # Stop inference when the first reading has not arrived this long after the guard started.
        self.startup_timeout_s = cfg.get("startup_timeout_s", self.telemetry_timeout_s)
        self.unreadable_timeout_s = cfg.get("unreadable_timeout_s", 5.0)
        # Production firewall: a reading carrying {"simulated": true} is TEST DATA. Unless this rule opts in
        # explicitly (test configs only), it is dropped as invalid - and a missing reading is a stop condition,
        # so a replayed simulation can never drive a real card.
        self.allow_simulated = bool(cfg.get("allow_simulated", False))
        # Operator-standby marker: while this file EXISTS, the stack is DELIBERATELY down (stop_v100.sh
        # creates it; the bring-up scripts remove it). Missing telemetry then is an intention, not a
        # fault: no stops, no detach storms. Measured 2026-11-20: a latched stop-episode re-fired every
        # second and SIGTERM-killed two freshly booting VMs that had done nothing wrong.
        self.standby_file = cfg.get("standby_file")
        self.power_limit_w = cfg.get("power_limit_w")
        self.stop_cmd = cfg.get("stop_cmd")               # argv list; "{index}" substituted; []/None: log only
        self.kill_cmd = cfg.get("kill_cmd")               # escalates with SIGKILL when stop_cmd/stop_url fails
        self.stop_url = cfg.get("stop_url")               # HTTP POST target (Strata /api/thermal_stop)
        # Post-stop verification (audit): "my stop command returned 0" is not "the inference processes are
        # gone". verify_cmd (argv, exit 0 == none of the stack's processes exist, e.g. stop_stack.sh --verify)
        # checks reality; failure escalates to kill_cmd, and a failing verify after that is a `crit` event.
        self.verify_cmd = cfg.get("verify_cmd")
        self.index = cfg.get("index", 0)
        self.host = cfg.get("host")
        self.port = cfg.get("port")
        # The mission's "dangerous jump": a reading that jumps this much between consecutive polls stops work
        # even when both individual values look legal (sensor glitch, airflow death, ...).
        self.jump_c = cfg.get("jump_c", 15.0)
        self.min_host_available_mib = cfg.get("min_host_available_mib")


class DeviceGuard:
    """One device's state machine.  `evaluate()` is pure of side effects except returning events and firing the
    injected actions, which makes every rule unit-testable with a fake clock and fake readings."""

    def __init__(self, rule: Rule, source, actions, log):
        self.rule, self.source, self.actions = rule, source, actions
        self._log = log
        self._fired_now = False
        self.state = {"temp_last": None, "temp_mem_last": None, "ok_last_ts": None, "last_temp_ts": None,
                      "stopped": False, "warned": False, "unreadable_since": None, "history": [],
                      "events": 0, "first_seen": None, "sim_rejected": 0,
                      "power_limit_verified": rule.power_limit_w is None, "core_hot_since": None}

    def _fire(self, now, reason, reading):
        r = self.rule
        first = not self.state["stopped"]
        self.state["stopped"] = True
        self.state["stop_reason"] = reason
        self._fired_now = True
        self.actions("stop", self.rule.id, reason, reading, first)
        return {"t": now, "device": r.id, "level": "stop", "reason": reason,
                "reading": _brief(reading), "action": "stop_inference" if first else "stop_repeat"}

    def evaluate(self, reading, now, host_available_mib=None):
        """-> list of event dicts.  `reading` None: source unreachable; missing keys: unsupported sensors.

        FAIL-CLOSED invariants (safety audit):
          * an unreachable source stops within `telemetry_timeout_s` (existing) OR, if it has NEVER answered
            since the guard started, within `startup_timeout_s` ("no_telemetry_since_start") - a V100 whose
            agent never came up must not look safer than one whose agent died;
          * a reading marked `simulated` is dropped (read as no reading) unless this rule explicitly opts in -
            test data can never keep a real device's halt from firing;
          * `temp_mem` is never substituted with `temp` (the V100's real null stays null)."""
        r = self.rule
        ev = []
        st = self.state
        self._fired_now = False
        if r.standby_file and os.path.exists(r.standby_file):
            if not st.get("standby_logged"):
                st["standby_logged"] = True
                self._log({"level": "info", "device": r.id, "reason": "standby",
                           "note": "marker present: telemetry absence is intentional, no stops"})
            st["ok_last_ts"] = None
            st["first_seen"] = now
            return ev
        st["standby_logged"] = False
        if r.min_host_available_mib is not None:
            if (type(host_available_mib) not in (int, float)
                    or not math.isfinite(host_available_mib)):
                return [self._fire(now, "host_memory_unreadable", reading)]
            if host_available_mib < r.min_host_available_mib:
                return [self._fire(now, "host_memory_below_floor", reading)]
        if st["first_seen"] is None:
            st["first_seen"] = now
        if reading is not None and reading.get("simulated") and not r.allow_simulated:
            st["sim_rejected"] += 1
            self._log({"level": "error", "device": r.id, "reason": "simulated_telemetry_rejected",
                       "note": "a rule without allow_simulated never consumes simulated readings"})
            reading = None
        if reading is not None and isinstance(reading.get("ts"), (int, float)) \
                and now - reading["ts"] > max(15.0, 2 * r.telemetry_timeout_s):
            # A SERVING agent whose sampler is wedged must not masquerade as fresh: freshness is judged
            # from the sample's own completion time, not from the socket answering.
            st["stale_rejected"] = st.get("stale_rejected", 0) + 1
            if st["stale_rejected"] == 1 or st["stale_rejected"] % 10 == 0:
                self._log({"level": "error", "device": r.id, "reason": "stale_sample_rejected",
                           "age_s": round(now - reading["ts"], 1)})
            reading = None
        if reading is None:
            if st["ok_last_ts"] is not None:
                if now - st["ok_last_ts"] >= r.telemetry_timeout_s:
                    ev.append(self._fire(now, "telemetry_lost", None))
            elif now - st["first_seen"] >= r.startup_timeout_s:
                ev.append(self._fire(now, "no_telemetry_since_start", None))
            return ev
        st["ok_last_ts"] = now
        temp = reading.get("temp")
        temp_mem = reading.get("temp_mem")          # never substituted with `temp` - a real null on the V100
        st["temp_mem_last"] = temp_mem
        if temp is not None and not _finite_number(temp):
            return [self._fire(now, "temperature_unreadable", reading)]
        if temp_mem is not None and not _finite_number(temp_mem):
            return [self._fire(now, "memory_temperature_unreadable", reading)]
        if temp is None:
            if st["unreadable_since"] is None:
                st["unreadable_since"] = now
            elif now - st["unreadable_since"] >= r.unreadable_timeout_s:
                ev.append(self._fire(now, "temperature_unreadable", reading))
            return ev
        st["unreadable_since"] = None

        # rise / jump detection on the wall-clock history of (ts, temp)
        hist = st["history"]
        prev_temp, prev_ts = st["temp_last"], st["last_temp_ts"]
        st["temp_last"], st["last_temp_ts"] = temp, now
        hist.append((now, temp))
        while hist and hist[0][0] < now - max(120.0, r.rise_window_s * 4):
            hist.pop(0)
        if r.jump_c is not None and prev_temp is not None and (now - prev_ts) > 0 and (temp - prev_temp) >= r.jump_c and temp >= r.jump_min_c:
            ev.append(self._fire(now, f"temperature_jump_{r.jump_c:g}C", reading))
        window = [(t, v) for (t, v) in hist if t >= now - r.rise_window_s]
        if r.rise_c_per_min and len(window) >= 2:
            span = window[-1][0] - window[0][0]
            if span > 0:
                rate = (window[-1][1] - window[0][1]) * 60.0 / span
                if rate >= r.rise_c_per_min and window[-1][1] >= r.rise_min_c:
                    ev.append(self._fire(now, f"temperature_rise_{rate:.0f}C_per_min", reading))

        # HBM/memory temperature has its own thresholds when present (add the rule keys when a card reports it)
        if temp_mem is not None and r.memory_stop_c is not None and temp_mem >= r.memory_stop_c:
            ev.append(self._fire(now, "memory_over_temperature", reading))

        if r.stop_c is not None and temp >= r.stop_c:
            since = st.get("core_hot_since")
            if since is None:
                since = st["core_hot_since"] = now
            if r.core_grace_s and temp >= r.core_emergency_c:
                ev.append(self._fire(now, "core_emergency_temperature", reading))
            elif not r.core_grace_s or now < since or now - since >= r.core_grace_s or st["stopped"]:
                ev.append(self._fire(now, "over_temperature", reading))
            elif now == since:
                ev.append({"t":now,"device":r.id,"level":"info","reason":"core_temperature_grace",
                           "reading":_brief(reading),"action":"monitor"})
        else:
            st["core_hot_since"] = None
        if (self.state["stopped"] and r.stop_c is not None and not self._fired_now
                and self.state["power_limit_verified"]
                and (temp_mem is None or temp_mem <= r.memory_stop_c - r.clear_c)
                and temp <= r.stop_c - r.clear_c):
            self.state["stopped"] = False
            self.state["warned"] = False
            ev.append({"t": now, "device": r.id, "level": "info", "reason": "cleared",
                       "reading": _brief(reading), "action": "re-armed"})
        if (r.policy_version and st["warned"] and not st["stopped"]
                and temp <= r.warn_c - r.clear_c):
            st["warned"] = False
            ev.append({"t":now,"device":r.id,"level":"info","reason":"warning_cleared",
                       "reading":_brief(reading),"action":"monitor"})
        if (r.warn_c is not None and temp >= r.warn_c and not self.state["warned"]
                and not self.state["stopped"]):
            self.state["warned"] = True
            self.actions("warn", r.id, "warn_temperature", reading, True)
            ev.append({"t": now, "device": r.id, "level": "warn", "reason": "warn_temperature",
                       "reading": _brief(reading), "action": "notify"})
        return ev


def _brief(reading):
    if not reading:
        return None
    return {k: reading.get(k) for k in ("temp", "temp_mem", "power", "util", "throttled", "driver_ok")
            if k in reading}


class ThermalGuard:
    def __init__(self, config, log_path=None, status_path=None):
        self.cfg = config
        self.interval_s = config.get("interval_s", 2.0)
        # `~` in JSON configs was passed to open() verbatim -> silent write failures behind a try/except.
        # Expand now, create the directories, and PROVE both files are writable at construction: a watchdog
        # that cannot write its log or status file is not a watchdog (fail fast, audit item 5).
        self.log_path = os.path.expanduser(log_path or config.get("log") or "") or None
        self.status_path = os.path.expanduser(status_path or config.get("status_file") or "") or None
        for p in (self.log_path, self.status_path):
            if p:
                try:
                    d = os.path.dirname(p)
                    if d:
                        os.makedirs(d, exist_ok=True)
                    with open(p, "a", encoding="utf-8"):
                        pass
                except OSError as e:
                    raise RuntimeError(f"thermal_guard cannot write {p!r}: {e}") from e
        self.devices = []
        for entry in config["devices"]:
            rule = Rule(entry)
            self.devices.append(DeviceGuard(rule, self._source(rule), self._act(rule), self.log))
        self._apply_results = {}

    @staticmethod
    def _source(rule: Rule):
        if rule.kind == "nvml":
            return NvmlSource(rule.index)
        # READ tolerance and LOST detection are separate concerns (measured 2026-11-20: one NVML read that
        # stalls ~5 s under driver activity tripped telemetry_lost and the guard's own escalation then
        # detached the healthy VM). A read may take up to max(10s, telemetry_timeout_s); only consecutive
        # FULL failures spanning telemetry_timeout_s count as lost.
        return RemoteSource(rule.host, rule.port, timeout=max(10.0, rule.telemetry_timeout_s))

    def _act(self, rule: Rule):
        def run(cmd):
            cmd = [str(a).replace("{index}", str(rule.index)) for a in cmd]
            self.heartbeat()
            try:
                p = subprocess.run(cmd, check=False, timeout=15,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
                self.heartbeat()
                return p.returncode == 0, p.returncode, (p.stderr or b"").decode()[:200] or None
            except (OSError, subprocess.SubprocessError) as e:
                return False, None, f"{type(e).__name__}: {e}"

        def verify(r):
            """exit 0 of verify_cmd means the stack's processes are GONE - checked, not assumed."""
            if not r.verify_cmd:
                return True, None            # no verifier: trust stop_cmd's self-verification contract
            ok, rc, err = run(r.verify_cmd)
            self.log({"level": "action" if ok else "error", "device": r.id, "what": "verify_stopped",
                      "ok": ok, "returncode": rc, "detail": err})
            return ok, err

        def act(level, device_id, reason, reading, first):
            # Audit rule 3 kept as: while stopped, ANY resumed inference gets hit again - but via VERIFY,
            # not via re-running the kill chain every second. Measured 2026-11-20: the repeat-everything
            # form re-touched the VFIO detach marker each tick, and the hotplug daemon then SIGTERM-killed
            # two freshly booting VMs that were coming up to serve again. A verified-GONE stack is quiet;
            # a verify FAILURE (processes are back) runs the full chain, hot card or not.
            if level != "stop":
                return
            r = rule
            if not first:
                still_gone, detail = verify(r)
                if still_gone:
                    return                       # stopped AND proven: nothing left to kill, stay quiet
                self.log({"level": "action", "device": r.id, "reason": "stopped_but_resumed",
                          "of": reason, "detail": detail,
                          "note": "verify failed while stopped - running full stop chain again"})
            configured, all_ok = 0, True
            if r.stop_cmd:
                configured += 1
                ok, rc, err = run(r.stop_cmd)
                self.log({"level": "action" if ok else "error", "device": r.id, "reason": reason,
                          "stop_cmd": r.stop_cmd, "returncode": rc, "detail": err})
                all_ok &= ok
            if r.stop_url:
                configured += 1
                try:
                    req = urllib.request.Request(r.stop_url, data=json.dumps(
                        {"reason": reason, "device": r.id}).encode(), method="POST",
                        headers={"Content-Type": "application/json"})
                    with urllib.request.urlopen(req, timeout=5) as resp:
                        ok = 200 <= resp.status < 300
                        resp.read(4096)
                    self.log({"level": "action" if ok else "error", "device": r.id, "reason": reason,
                              "stop_url": r.stop_url, "http_ok": ok})
                except OSError as e:
                    ok = False
                    self.log({"level": "error", "device": r.id, "reason": f"stop_url failed: {e}"})
                all_ok &= ok
            if not configured:
                return
            # "commands returned 0" is not "inference stopped": verify against the process table.
            stopped_ok = all_ok
            if stopped_ok:
                stopped_ok, _ = verify(r)
            if not stopped_ok and r.kill_cmd:
                # A stop that failed is not a stop: escalate to the hard kill, loudly, then verify AGAIN.
                ok, rc, err = run(r.kill_cmd)
                self.log({"level": "action", "device": r.id, "reason": "escalated_sigkill",
                          "of": reason, "kill_cmd": r.kill_cmd, "returncode": rc})
                stopped_ok, _ = verify(r)
            if not stopped_ok:
                self.log({"level": "crit", "device": r.id, "reason": "stop_not_verified", "of": reason,
                          "note": "INFERENCE MAY STILL BE RUNNING - intervene manually"})
        return act

    def apply_power_limits(self):
        """Startup pass: apply each configured power_limit_w ONLY where the driver VERIFIES the range.

        Audit rule 6: a device whose power limit could NOT be applied is NOT safe to run - this method stops
        inference for it immediately (same action pipeline as an over-temperature stop) instead of merely
        logging. The result stays in `_apply_results` (and the status file) so starters can check too."""
        for d in self.devices:
            w = d.rule.power_limit_w
            if w is None:
                continue
            self.heartbeat()
            applied, err = d.source.set_power_limit(w)
            self.heartbeat()
            if err is None and (not _finite_number(applied) or applied != w):
                err = "power-limit acknowledgement does not match the configured cap"
            d.state["power_limit_verified"] = False  # a fresh hardware sample must confirm it
            self._apply_results[d.rule.id] = {"requested_w": w, "applied_w": applied, "error": err}
            self.log({"level": "action", "device": d.rule.id, "what": "power_limit", "requested_w": w,
                      "applied_w": applied, "error": err})
            if err is not None or applied is None:
                self.log(d._fire(time.time(), "power_limit_not_applied", None))

    def revalidate_power_limit(self, d, reading, now):
        """Revalidate the cap without restarting the guard or changing any thermal stop state."""
        w = d.rule.power_limit_w
        if w is None:
            return []
        if d.rule.standby_file and os.path.exists(d.rule.standby_file):
            d.state["power_limit_verified"] = False
            return []
        was_verified = d.state["power_limit_verified"]
        error = _power_readback_error(d.rule, reading, now)
        d.state["power_limit_verified"] = error is None
        self._apply_results[d.rule.id] = {
            "requested_w": w,
            "applied_w": reading["power_limit_active"] if error is None else None,
            "error": error,
            "verified_sample_ts": reading["ts"] if error is None else None,
            "verification": "hardware_telemetry" if error is None else None,
        }
        if error is None and not was_verified:
            self.log({"level": "action", "device": d.rule.id, "what": "power_limit_revalidated",
                      **self._apply_results[d.rule.id]})
        # A fresh payload reporting a wrong cap, driver failure or invalid required sensor
        # is an immediate fault. Invalid JSON shapes/timestamps are also fail-closed.
        # Missing/stale/simulated samples retain the existing telemetry timeout policy; none
        # can re-arm a stopped device because the independent power latch remains false.
        deferred = ("power-limit telemetry unavailable", "power-limit hardware sample is stale",
                    "simulated power-limit telemetry")
        if error is not None and error not in deferred:
            return [d._fire(now, "power_limit_not_applied", reading if isinstance(reading, dict) else None)]
        return []

    def tick(self, now=None):
        fixed_clock = now is not None
        now = time.time() if now is None else now
        events, snapshot = [], {}
        for d in self.devices:
            self.heartbeat()
            reading = d.source.read()
            self.heartbeat()
            power_reading = reading
            if not isinstance(reading, dict):
                reading = None
            observed_now = now if fixed_clock else time.time()
            host = None
            if d.rule.min_host_available_mib is not None:
                try:
                    with open("/proc/meminfo", encoding="ascii") as stream:
                        host = next(int(line.split()[1]) / 1024 for line in stream
                                    if line.startswith("MemAvailable:"))
                except (OSError, ValueError, StopIteration):
                    pass
            events += self.revalidate_power_limit(d, power_reading, observed_now)
            events += d.evaluate(reading, observed_now, host_available_mib=host)
            snapshot[d.rule.id] = {"reading": _brief(reading) if reading else None,
                                   "reading_full": None if reading is None else {
                                       k: reading.get(k) for k in
                                       ("temp", "temp_mem", "power", "power_limit", "power_limit_active",
                                        "power_limit_range", "util", "mem_used", "mem_total", "throttled",
                                        "throttle_reasons", "driver_ok", "name", "pci", "pstate")
                                       } | {k: reading[k] for k in ("ts", "simulated") if k in reading},
                                   "stopped": d.state["stopped"], "warned": d.state["warned"],
                                   "standby": bool(d.rule.standby_file and os.path.exists(d.rule.standby_file)),
                                   "power_limit_verified": d.state["power_limit_verified"],
                                   "thermal_policy": {"version":d.rule.policy_version,"warn_c":d.rule.warn_c,
                                                      "stop_c":d.rule.stop_c,"core_grace_s":d.rule.core_grace_s,
                                                      "core_emergency_c":d.rule.core_emergency_c,
                                                      "memory_stop_c":d.rule.memory_stop_c},
                                   "telemetry_ok": reading is not None,
                                   "last_telemetry_ts": d.state["ok_last_ts"]}
        for e in events:
            self.log(e)
        if self.status_path:
            status = {"ts": now if fixed_clock else time.time(), "devices": snapshot,
                      "power_limits": self._apply_results,
                      "halted": any(d.state["stopped"] for d in self.devices)}
            tmp = self.status_path + ".tmp"
            try:
                with open(tmp, "w", encoding="utf-8") as f:
                    json.dump(status, f, default=str)
                os.replace(tmp, self.status_path)
            except OSError:
                pass
        return events

    @staticmethod
    def heartbeat():
        """Let systemd stop bound native engines if this independent sampler freezes."""
        address = os.environ.get("NOTIFY_SOCKET")
        if not address:
            return
        if address.startswith("@"):
            address = "\0" + address[1:]
        with socket.socket(socket.AF_UNIX, socket.SOCK_DGRAM) as notify:
            notify.settimeout(1)
            notify.sendto(b"WATCHDOG=1", address)

    def log(self, obj):
        line = json.dumps(obj, default=str)
        ts = time.strftime("%Y-%m-%dT%H:%M:%S")
        sys.stderr.write(f"[thermal_guard {ts}] {line}\n")
        sys.stderr.flush()
        if self.log_path:
            try:
                with open(self.log_path, "a", encoding="utf-8") as f:
                    f.write(line + "\n")
            except OSError:
                pass

    def run(self, iterations=None):
        self.apply_power_limits()
        n = 0
        while iterations is None or n < iterations:
            self.tick()
            self.heartbeat()
            n += 1
            if iterations is not None and n >= iterations:
                break
            time.sleep(self.interval_s)


def main(argv=None):
    ap = argparse.ArgumentParser(prog="strata-thermal-guard", description=__doc__.splitlines()[0])
    ap.add_argument("--config", required=True, help="JSON config: {devices: [...], interval_s, log, status_file}")
    ap.add_argument("--once", action="store_true", help="one poll then exit (integration tests)")
    ap.add_argument("--check", action="store_true", help="validate config, prove log/status writability, poll "
                                                        "each device once and print the verdict; exit 0 only "
                                                        "if every device answers with a real (non-simulated "
                                                        "unless allowed) reading and no rule is mis-ordered")
    ap.add_argument("--iterations", type=int, default=None, help="poll N times then exit (tests)")
    args = ap.parse_args(argv)
    with open(args.config, encoding="utf-8") as f:
        cfg = json.load(f)
    guard = ThermalGuard(cfg)
    if args.check:
        bad = []
        for d in guard.devices:
            r = d.rule
            if r.stop_c is not None and r.warn_c is not None and r.warn_c >= r.stop_c:
                bad.append(f"{r.id}: warn_c >= stop_c")
            reading = d.source.read()
            sim = bool(reading and reading.get("simulated"))
            print(f"{r.id}: read={'ok' if reading else 'UNREACHABLE'} simulated={sim} "
                  f"temp={reading.get('temp') if reading else None} stop_c={r.stop_c}")
            if reading is None or (sim and not r.allow_simulated):
                bad.append(f"{r.id}: telemetry not proven real")
        print("CHECK_OK" if not bad else "CHECK_FAILED: " + "; ".join(bad))
        return 0 if not bad else 1
    guard.run(iterations=1 if args.once else args.iterations)
    return 0


if __name__ == "__main__":
    sys.exit(main())
