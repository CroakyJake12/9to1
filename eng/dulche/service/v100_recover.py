#!/usr/bin/python3
"""Recover the previously deployed Astra V100 guest while preserving its thermal policy."""

import argparse
import contextlib
import fcntl
import hashlib
import json
import os
import socket
import subprocess
import sys
import threading
import time
from pathlib import Path

from engine_profiles import atomic_json
from thermal_policy import active_cooling_rule
from v100_authority import identity
from v100_authority import request as authority

PROJECT = Path.home() / "Documents/Default Project"
STRATA = PROJECT / "strata"
VM = PROJECT / "vm"
GUEST_UNIT = "astra-v100-vm.service"
ENGINE_UNIT = "astra-v100-inference.service"
GUARD_UNIT = "dulche-v100-thermal.service"
GUARD = Path.home() / "strata-run/thermal_guard_status.json"
STANDBY = GUARD.parent / "v100-standby"
SCRIPT = Path(__file__).resolve()
RTX_UUID = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"
V100_UUID = "GPU-b15521a1-14ac-96c1-7166-44abeb5db769"


def need(value, message):
    if not value:
        raise RuntimeError(message)


def run(args, timeout=20, check=True, input=None):
    p = subprocess.run(
        args,
        text=True,
        input=input,
        capture_output=True,
        check=False,
        timeout=timeout,
    )
    if check and p.returncode:
        raise RuntimeError((p.stderr or p.stdout or "Command failed").strip()[:500])
    return p


def active(unit):
    return (
        run(
            ["systemctl", "--user", "is-active", "--quiet", unit], check=False
        ).returncode
        == 0
    )


def owned_vm():
    if not active(GUEST_UNIT):
        return False
    pid = int(
        run(
            ["systemctl", "--user", "show", GUEST_UNIT, "-p", "MainPID", "--value"]
        ).stdout
    )
    proc = Path("/proc") / str(pid)
    args = (proc / "cmdline").read_bytes().split(b"\0")
    need(
        proc.stat().st_uid == os.getuid()
        and (proc / "exe").resolve() == Path("/usr/bin/qemu-system-x86_64").resolve()
        and b"-name" in args
        and args[args.index(b"-name") + 1] == b"strata-v100-vm"
        and int((VM / "qemu.pid").read_text()) == pid,
        "Guest unit identity is uncertain",
    )
    return True


def assets():
    fragment = run(
        ["systemctl", "--user", "show", ENGINE_UNIT, "-p", "FragmentPath", "--value"]
    ).stdout.strip()
    p = Path(fragment).resolve()
    need(
        p.name == ENGINE_UNIT
        and (p.parent / "v100-guest.known_hosts").is_file()
        and (p.parent / "stage-v100-sensor.py").is_file(),
        "Verified V100 lane installation is missing",
    )
    return p.parent


def ssh(command, input=None, check=True, timeout=12):
    return run(
        [
            "ssh",
            "-i",
            str(VM / "id_ed25519"),
            "-p",
            "2222",
            "-o",
            "BatchMode=yes",
            "-o",
            "ConnectTimeout=3",
            "-o",
            "StrictHostKeyChecking=yes",
            "-o",
            "LogLevel=ERROR",
            "-o",
            "HostKeyAlias=astra-v100-guest",
            "-o",
            "GlobalKnownHostsFile=/dev/null",
            "-o",
            "UserKnownHostsFile=" + str(assets() / "v100-guest.known_hosts"),
            "v100@127.0.0.1",
            command,
        ],
        input=input,
        check=check,
        timeout=timeout,
    )


def wait_for(fn, seconds, what):
    deadline = time.monotonic() + seconds
    error = ""
    while time.monotonic() < deadline:
        try:
            if fn():
                return
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as exc:
            error = str(exc)
        time.sleep(0.4)
    raise RuntimeError("Timed out waiting for " + what + ". " + error)


def sample():
    with socket.create_connection(("127.0.0.1", 19109), timeout=3) as s:
        s.sendall(b"telemetry\n")
        r = json.loads(s.makefile("rb").readline(65536))
    need(
        r.get("driver_ok") is True
        and r.get("simulated") is False
        and r.get("name") == "Tesla V100-PCIE-16GB"
        and abs(time.time() - r["ts"]) < 5
        and all(type(r.get(k)) in (float, int) for k in ("temp", "temp_mem", "util")),
        "Missing, stale or unverified V100 thermal reading",
    )
    return r


def safe_sensor(idle=False):
    r = sample()
    need(
        r.get("power_limit_active") == 165 and r["temp"] < 68 and r["temp_mem"] < 67,
        "V100 cap or temperatures unavailable/unsafe",
    )
    if idle:
        need(
            r["util"] == 0 and max(r["temp"], r["temp_mem"]) < 60,
            "Boot requires cool idle sensor readings",
        )
    return r


def guarded():
    s = json.loads(GUARD.read_text())
    v = s["devices"]["v100"]
    r = v.get("reading_full") or {}
    need(
        abs(time.time() - s["ts"]) < 5
        and s["halted"] is False
        and v["stopped"] is False
        and v["telemetry_ok"] is True
        and r.get("power_limit_active") == 165
        and isinstance(r.get("temp_mem"), (float, int))
        and r["temp_mem"] < 67,
        "Guard has not independently verified the V100 cap and thermal sensors",
    )
    return True


def stop_worker():
    if active(ENGINE_UNIT):
        run(["systemctl", "--user", "stop", ENGINE_UNIT], timeout=12)
    if owned_vm():
        ssh("bash ~/strata/v100_worker_ctl.sh stop worker", timeout=12)


def verify_stopped():
    if not owned_vm():
        return
    deadline = time.monotonic() + 7
    last_ts = None
    last_error = "V100 idle state was not proven"
    while time.monotonic() < deadline:
        try:
            p = ssh("bash ~/strata/v100_worker_ctl.sh status worker", check=False, timeout=2)
            need(p.returncode == 10 and not ssh("ss -Hlt '( sport = :5555 )'", timeout=2).stdout.strip(),
                 "V100 RPC process is still active")
            reading = sample()
            need(reading["util"] == 0, "V100 is still computing")
            if last_ts is not None and reading["ts"] > last_ts:
                return
            last_ts = reading["ts"]
        except (OSError, RuntimeError, ValueError, subprocess.SubprocessError) as exc:
            last_ts = None
            last_error = str(exc)
        time.sleep(0.25)
    raise RuntimeError(last_error)


def emergency_detach():
    if active(ENGINE_UNIT):
        run(["systemctl", "--user", "stop", ENGINE_UNIT], check=False, timeout=10)
    if owned_vm():
        # The per-boot root authority independently validates any surviving VFIO holder and has its own bounded stop.
        run(["systemctl", "--user", "--no-block", "stop", GUEST_UNIT], check=False)
    return authority("detach")


def other_guard():
    for proc in Path("/proc").glob("[0-9]*"):
        try:
            if proc.stat().st_uid != os.getuid() or (
                proc / "comm"
            ).read_text().strip() not in ("python", "python3", "python3.12"):
                continue
            if b"serve.thermal_guard" in (proc / "cmdline").read_bytes().split(b"\0"):
                return True
        except (OSError, ValueError):
            pass
    return False


def guard_runtime(directory):
    """Resolve the reviewed persistent sampler without depending on a transient unit."""
    from pooled_runtime import read_private_json

    source = read_private_json(directory / "thermal-guard-source.json")
    need(source.get("schema") == "dulche-thermal-runtime/v1", "Unknown thermal runtime registration")
    root = Path(source["directory"]).resolve(strict=True)
    allowed = Path.home() / ".local/share/9to1/dulche-bridge"
    need(root.is_relative_to(allowed.resolve(strict=True)), "Thermal runtime is outside its owned installation")
    hashes = source.get("files", {})
    need(set(hashes) == {"serve/__init__.py", "serve/telemetry.py", "serve/thermal_guard.py"},
         "Thermal runtime manifest is incomplete")
    for name, expected in hashes.items():
        file = root / name
        need(not file.is_symlink() and file.stat().st_uid == os.getuid()
             and not file.stat().st_mode & 0o022
             and hashlib.sha256(file.read_bytes()).hexdigest() == expected,
             "Thermal runtime differs from its reviewed installation")
    return root


def start_guard(directory):
    if active(GUARD_UNIT):
        return
    need(
        not other_guard(),
        "Another thermal guard is already running, refusing concurrent status writers",
    )
    guard_cfg = directory / "v100-guard.json"
    config = json.loads((guard_cfg if guard_cfg.exists() else VM / "guard_run.json").read_text())
    need({row["id"] for row in config["devices"]} == {"rtx5080", "v100"}
         and len(config["devices"]) == 2, "Thermal policy must identify both physical GPUs")
    rule = next(row for row in config["devices"] if row["id"] == "v100")
    need(
        ((rule["stop_c"] <= 68 and rule["warn_c"] <= 60) or active_cooling_rule(rule, "v100"))
        and rule["power_limit_w"] == 165
        and rule.get("allow_simulated") is False,
        "The prior V100 guard settings need inspection",
    )
    rtx = next(row for row in config["devices"] if row["id"] == "rtx5080")
    need(0 < rtx["warn_c"] < rtx["stop_c"] <= 80
         and rtx.get("allow_simulated") is False and not rtx.get("standby_file"),
         "The prior RTX thermal policy needs inspection")
    runtime = guard_runtime(directory)
    for row in config["devices"]:
        helper = SCRIPT.with_name("native_guard.py" if row["id"] == "rtx5080" else "pooled_guard.py")
        need(helper.is_file(), "The per-device thermal stop helper is unavailable")
        row.update(
            stop_url=None,
            stop_cmd=["/usr/bin/python3", str(helper), "stop"],
            verify_cmd=["/usr/bin/python3", str(helper), "verify"],
            kill_cmd=["/usr/bin/python3", str(helper), "kill"],
        )
    config["status_file"] = str(GUARD)
    config["log"] = str(GUARD.parent / "thermal_events.jsonl")
    atomic_json(guard_cfg, config)
    run(
        [
            "systemd-run",
            "--user",
            "--collect",
            "--unit=" + GUARD_UNIT,
            "-p",
            "Type=exec",
            "-p",
            "Restart=no",
            "-p",
            "WorkingDirectory=" + str(runtime),
            "-p",
            "WatchdogSec=20s",
            "-p",
            "NotifyAccess=main",
            "/usr/bin/python3",
            "-B",
            "-m",
            "serve.thermal_guard",
            "--config",
            str(guard_cfg),
        ]
    )


def boot_guest(progress):
    need(
        run(
            [
                "ss",
                "-Hln",
                "-t",
                "( sport = :2222 or sport = :19109 or sport = :19110 )",
            ]
        ).stdout.strip()
        == "",
        "Existing owner on a guest forwarding port",
    )
    mem = int(
        next(
            l.split()[1]
            for l in Path("/proc/meminfo").read_text().splitlines()
            if l.startswith("MemAvailable:")
        )
    )
    need(mem >= 6 * 1024**2, "Cold 2.5 GiB guest needs 6 GiB of available host RAM")
    for path in (
        VM / "v100-vm.qcow2",
        VM / "vm_vars.fd",
        VM / "seed.img",
        VM / "id_ed25519",
    ):
        need(path.is_file(), "Missing prior guest artifact: " + path.name)
    need(
        identity().name == "18", "V100 IOMMU layout differs from the validated machine"
    )
    need(
        RTX_UUID
        in run(["nvidia-smi", "--query-gpu=uuid", "--format=csv,noheader"]).stdout,
        "Unexpected primary GPU",
    )
    args = [
        "/usr/bin/qemu-system-x86_64",
        "-name",
        "strata-v100-vm",
        "-machine",
        "q35",
        "-enable-kvm",
        "-m",
        "2560M",
        "-smp",
        "4",
        "-cpu",
        "host,host-phys-bits=off,phys-bits=39",
        "-global",
        "q35-pcihost.pci-hole64-size=128G",
        "-fw_cfg",
        "name=opt/ovmf/X-PciMmio64Mb,string=131072",
        "-device",
        "pcie-root-port,id=gpuroot,bus=pcie.0,addr=0x2,chassis=1,slot=1,pref64-reserve=64G,mem-reserve=64M",
        "-drive",
        "if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd",
        "-drive",
        f"if=pflash,format=raw,file={VM}/vm_vars.fd",
        "-drive",
        f"file={VM}/v100-vm.qcow2,if=virtio,format=qcow2",
        "-drive",
        f"file={VM}/seed.img,if=virtio,format=raw",
        "-netdev",
        "user,id=n0,hostfwd=tcp:127.0.0.1:2222-:22,hostfwd=tcp:127.0.0.1:19109-:9109,hostfwd=tcp:127.0.0.1:19110-:5555",
        "-device",
        "virtio-net-pci,netdev=n0",
        "-display",
        "none",
        "-serial",
        f"file:{VM}/serial-dulche-{int(time.time())}.log",
        "-qmp",
        f"unix:{VM}/qmp.sock,server=on,wait=off",
        "-pidfile",
        str(VM / "qemu.pid"),
        "-no-reboot",
    ]
    run(
        [
            "systemd-run",
            "--user",
            "--collect",
            "--unit=" + GUEST_UNIT,
            "-p",
            "Type=exec",
            "-p",
            "Restart=no",
            "-p",
            "MemoryMax=3800M",
            "-p",
            "MemorySwapMax=0",
            "-p",
            "Nice=5",
            *args,
        ]
    )
    progress["created_vm"] = True
    wait_for(owned_vm, 12, "owned guest process")
    wait_for(
        lambda: ssh("true", check=False, timeout=4).returncode == 0,
        90,
        "pinned SSH host identity",
    )
    ssh(
        "if pgrep -x ggml-rpc-server >/dev/null || pgrep -x llama-server >/dev/null; then exit 1; fi; rm -f -- ~/strata/worker.pid"
    )
    progress["attaching"] = True
    authority("attach")
    run(
        [
            sys.executable,
            str(STRATA / "tools/dual_gpu/v100_qmp_add.py"),
            str(VM / "qmp.sock"),
        ],
        timeout=25,
    )
    wait_for(
        lambda: (
            ssh(
                "nvidia-smi --query-gpu=uuid --format=csv,noheader", check=False
            ).stdout.strip()
            == V100_UUID
        ),
        40,
        "expected guest V100 CUDA UUID",
    )
    ssh("sudo -n nvidia-smi -i " + V100_UUID + " -pl 165")
    ssh("python3 -", input=(assets() / "stage-v100-sensor.py").read_text())
    ssh(
        "sudo -n systemd-run --quiet --unit=astra-v100-sensor-guarded --uid=v100 -p Restart=on-failure "
        "-p RestartSec=1 /usr/bin/python3 /home/v100/strata/astra_v100_sensor.py --serve 0.0.0.0:9109"
    )
    wait_for(
        lambda: safe_sensor(idle=True), 20, "verified cool V100 core and HBM telemetry"
    )


def guard_command_matches(argv, config_path):
    """Recognize only the maintained module command and its no-bytecode variant."""
    tail = list(argv[1:])
    if tail[:1] == [b"-B"]:
        tail = tail[1:]
    return tail == [
        b"-m", b"serve.thermal_guard", b"--config", os.fsencode(config_path)
    ]


def detached_guard_ready(directory):
    """Only the identified live guard may stand by for its detached V100, with RTX supervision intact."""
    config_path = (directory / "v100-guard.json").resolve(strict=True)
    pid = int(
        run(
            ["systemctl", "--user", "show", GUARD_UNIT, "-p", "MainPID", "--value"]
        ).stdout
    )
    proc = Path("/proc") / str(pid)
    need(
        pid > 1
        and proc.stat().st_uid == os.getuid()
        and (proc / "exe").resolve() == Path("/usr/bin/python3").resolve()
        and guard_command_matches(
            (proc / "cmdline").read_bytes().rstrip(b"\0").split(b"\0"),
            config_path,
        ),
        "Unverified live watchdog identity",
    )
    cfg = json.loads(config_path.read_text())
    rules = {r["id"]: r for r in cfg["devices"]}
    rtx, v100 = rules["rtx5080"], rules["v100"]
    # Reuse an active guard only within the operator's persisted RTX-specific limit.
    # The independently enforced V100 limits remain fixed below.
    saved_rules = json.loads((VM / "guard_run.json").read_text())["devices"]
    saved_rtx = next(row for row in saved_rules if row["id"] == "rtx5080")
    need(
        not rtx.get("standby_file")
        and 0 < rtx["stop_c"] <= min(80, saved_rtx["stop_c"])
        and (rtx["warn_c"] <= 60 or active_cooling_rule(rtx, "rtx5080"))
        and rtx.get("allow_simulated") is False
        and v100.get("allow_simulated") is False
        and (v100["stop_c"] <= 68 or active_cooling_rule(v100, "v100"))
        and v100["power_limit_w"] == 165
        and Path(v100["standby_file"]) == STANDBY
        and Path(cfg["status_file"]) == GUARD,
        "Live guard does not provide the verified per-device standby and thermal policy",
    )
    status = json.loads(GUARD.read_text())
    live = status["devices"]["rtx5080"]
    detached = status["devices"]["v100"]
    need(
        abs(time.time() - status["ts"]) < 5
        and live["telemetry_ok"]
        and live["stopped"] is False
        and (not live["warned"] or live["reading_full"]["temp"] < rtx["warn_c"] - rtx["clear_c"])
        and live["reading_full"]["driver_ok"] is True
        and live["reading_full"]["temp"] < rtx["warn_c"]
        and detached["stopped"] is True
        and detached["telemetry_ok"] is False
        and detached["reading_full"] is None
        and status["halted"] is True
        and authority("status").get("driver") is None,
        "Handoff requires an idle, cool RTX and a halted, physically detached V100 with no telemetry",
    )
    return pid, min(60, rtx["warn_c"] - (rtx["clear_c"] if live["warned"] else 0))


def rtx_temperature(limit):
    values = (
        run(
            [
                "nvidia-smi",
                "--query-gpu=uuid,temperature.gpu",
                "--format=csv,noheader,nounits",
            ],
            timeout=3,
        )
        .stdout.strip()
        .splitlines()
    )
    actual = [
        int(row.split(",")[1].strip())
        for row in values
        if row.split(",")[0].strip() == RTX_UUID
    ]
    need(
        len(actual) == 1 and 0 < actual[0] < limit,
        "RTX temperature is absent or outside the cool recovery window",
    )
    return actual[0]


@contextlib.contextmanager
def detached_boot_standby(directory, progress):
    if not active(GUARD_UNIT):
        need(
            not STANDBY.exists(),
            "Existing operator standby must be resolved before starting a guest",
        )
        need(
            not other_guard(),
            "An external watchdog is running; refusing competing status writers",
        )
        yield
        return
    guard_pid, warn = detached_guard_ready(directory)
    need(
        not STANDBY.exists(),
        "Existing operator standby must be resolved before cold recovery",
    )
    temperatures = []
    for i in range(4):
        if i:
            time.sleep(1)
        temperatures.append(rtx_temperature(warn))
    need(
        max(temperatures) - min(temperatures) <= 3, "RTX temperature has not stabilized"
    )
    done = threading.Event()
    failure = []

    def monitor():
        while not done.wait(1):
            try:
                need(
                    (Path("/proc") / str(guard_pid) / "cmdline").is_file(),
                    "Original thermal guard exited",
                )
                rtx_temperature(warn)
            except (
                OSError,
                ValueError,
                RuntimeError,
                subprocess.SubprocessError,
            ) as err:
                failure.append(str(err))
                if progress.get("created_vm"):
                    with contextlib.suppress(
                        OSError, RuntimeError, subprocess.SubprocessError
                    ):
                        if owned_vm():
                            run(
                                [
                                    "systemctl",
                                    "--user",
                                    "--no-block",
                                    "stop",
                                    GUEST_UNIT,
                                ]
                            )
                return

    monitor_thread = threading.Thread(target=monitor, daemon=True)
    STANDBY.touch(exist_ok=False)
    monitor_thread.start()
    try:

        def observed():
            current = json.loads(GUARD.read_text())
            return (
                abs(time.time() - current["ts"]) < 5
                and current["devices"]["rtx5080"]["stopped"] is False
                and current["devices"]["v100"]["last_telemetry_ts"] is None
            )

        wait_for(
            observed,
            12,
            "detached V100 standby acknowledgement from the existing guard",
        )
        need(not failure, "RTX became unsafe before guest boot")
        yield
        need(not failure, "RTX recovery monitor refused rearm: " + str(failure))
        rtx_temperature(warn)
        safe_sensor(idle=True)
        need(
            active(GUARD_UNIT)
            and int(
                run(
                    [
                        "systemctl",
                        "--user",
                        "show",
                        GUARD_UNIT,
                        "-p",
                        "MainPID",
                        "--value",
                    ]
                ).stdout
            )
            == guard_pid,
            "Original thermal guard did not remain running",
        )
    finally:
        STANDBY.unlink(missing_ok=True)
        done.set()
        monitor_thread.join(timeout=4)


def up(directory, *, start_worker=True):
    # Obtaining status is read-only. No guest, binding or service changes occur without the approved broker.
    authority("status")
    assets()
    if not owned_vm():
        need(
            not active(ENGINE_UNIT), "A stale gateway is running before the V100 guest"
        )
        progress = {}
        try:
            with detached_boot_standby(directory, progress):
                boot_guest(progress)
            need(not STANDBY.exists(), "An operator standby is still active")
            start_guard(directory)
            wait_for(guarded, 20, "live watchdog")
        except BaseException:
            if progress.get("attaching"):
                with contextlib.suppress(Exception):
                    emergency_detach()
            elif progress.get("created_vm") and owned_vm():
                run(["systemctl", "--user", "stop", GUEST_UNIT], check=False)
            raise
    safe_sensor()
    guarded()
    need(not STANDBY.exists(), "V100 is in operator standby")
    if not start_worker:
        return {
            "ready": True,
            "scope": "V100 guest transport; RPC remains under the caller's lifecycle control",
            "pci": "0000:05:00.0",
            "rpc": "127.0.0.1:19110",
            "reading": sample(),
        }
    rc = ssh("bash ~/strata/v100_worker_ctl.sh status worker", check=False).returncode
    need(rc in (0, 10), "Guest worker ownership is uncertain")
    if rc == 10:
        ssh("bash ~/strata/v100_worker_ctl.sh start worker")
    wait_for(
        lambda: bool(ssh("ss -Hlt '( sport = :5555 )'").stdout.strip()),
        15,
        "guest RPC listener",
    )
    safe_sensor()
    guarded()
    return {
        "ready": True,
        "scope": "V100 guest RPC; model remains under Dulche lifecycle control",
        "pci": "0000:05:00.0",
        "rpc": "127.0.0.1:19110",
        "reading": sample(),
    }


@contextlib.contextmanager
def cli_physical_admission():
    """Serialize standalone recovery with Dulche admission; fault stops stay available.

    EngineManager calls up() while holding this lock itself. Only the standalone
    CLI takes it here, so an imported recovery cannot deadlock its caller.
    """
    from pooled_runtime import assert_devices_available

    runtime = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}"))
    directory = runtime / "dulche-engine-admission"
    directory.mkdir(mode=0o700, exist_ok=True)
    need(not directory.is_symlink() and directory.stat().st_uid == os.getuid(),
         "Physical admission directory is not owned")
    fd = os.open(directory / "resource.lock", os.O_CREAT | os.O_NOFOLLOW | os.O_RDWR, 0o600)
    with os.fdopen(fd, "a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        assert_devices_available(("v100",), runtime_dir=runtime)
        yield


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "action",
        choices=("up", "status", "stop-worker", "verify-stopped", "emergency-detach"),
    )
    args = parser.parse_args()
    if args.action == "up":
        directory = (
            Path(
                os.environ.get("DULCHE_STATE_DIR")
                or Path(os.environ.get("XDG_STATE_HOME", Path.home() / ".local/state"))
                / "9to1/dulche"
            )
            / "engines"
        )
        directory.mkdir(mode=0o700, parents=True, exist_ok=True)
        with cli_physical_admission(), (directory / "v100-recovery.lock").open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            print(json.dumps(up(directory)))
    elif args.action == "stop-worker":
        stop_worker()
    elif args.action == "verify-stopped":
        verify_stopped()
    elif args.action == "emergency-detach":
        print(json.dumps(emergency_detach()))
    else:
        print(
            json.dumps(
                {
                    "authority": authority("status"),
                    "guest": owned_vm(),
                    "watchdog": active(GUARD_UNIT),
                }
            )
        )


if __name__ == "__main__":
    try:
        main()
    except (
        OSError,
        RuntimeError,
        KeyError,
        ValueError,
        subprocess.SubprocessError,
    ) as e:
        sys.exit("V100 recovery: " + str(e))
