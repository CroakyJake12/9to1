#!/usr/bin/env python3
"""Own one already-built local Haven process. Never builds, installs or kills it."""
import argparse
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import stat
import struct
import subprocess
import sys
import time
import uuid

from workflow import atomic, encoded, now, process_identity, remaining_group, safe_relative

_guardians = []  # Retain/reap only guardians launched by this helper invocation.

class Refusal(Exception):
    pass


def require(condition, message):
    if not condition:
        raise Refusal(message)


def absolute(value):
    path = Path(value).expanduser()
    require(path.is_absolute(), "An absolute local path is required")
    return path


def regular(path):
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid == os.getuid()
            and not info.st_mode & 0o022, "Expected an owner-controlled regular file: " + str(path))
    return info


def hashed(path):
    regular(path)
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def read_json(path):
    require(regular(path).st_size <= 4 * 1024 * 1024, "Metadata exceeds its size bound")
    return json.loads(path.read_bytes())


def create_json(path, value):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    with path.open("xb") as stream:
        os.chmod(path, 0o600)
        stream.write(encoded(value))
        stream.flush()
        os.fsync(stream.fileno())
    sync_directory(path.parent)


def sync_directory(path):
    fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def private_root(value):
    root = absolute(value)
    info = root.lstat()
    require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.getuid() and not info.st_mode & 0o077,
            "State root must be an existing private owner directory (0700)")
    return root


@contextlib.contextmanager
def operation(root):
    lock_path = root / "operation.lock"
    if lock_path.exists() or lock_path.is_symlink():
        regular(lock_path)
    fd = os.open(lock_path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "a+b") as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise Refusal("Another start/reload owns this development copy") from error
        yield


def init(root_value, data_value, window_title, profile_observation):
    root = absolute(root_value)
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    private_root(root)
    data = absolute(data_value)
    require(data != root and root not in data.parents and data not in root.parents,
            "Application data and launcher state must be separate directories")
    require(data == data.resolve() and data.is_dir() and not data.is_symlink()
            and data.stat().st_uid == os.getuid(), "Choose an existing canonical owner profile path (resolve any symlink parents)")
    require(isinstance(profile_observation, str) and 0 < len(profile_observation.strip()) <= 1000,
            "Record the local worker's one-time profile-path observation")
    require(isinstance(window_title, str) and 0 < len(window_title) <= 200
            and not any(ord(character) < 32 for character in window_title), "Exact main-window title required")
    with operation(root):
        create_json(root / "config.json", {"schema": 1, "data_directory": str(data), "window_title": window_title,
                                            "profile_observation": profile_observation.strip(), "profile_confirmation": "OPERATOR_RECORDED_NOT_APP_CONFIRMED"})
    return {"state_root": str(root), "data_directory": str(data), "application_started": False}


def build_proof(path, expected_hash, revision, runtime):
    require(re.fullmatch(r"[0-9a-f]{64}", expected_hash or ""), "Exact build-receipt SHA-256 required")
    require(hashed(path) == expected_hash, "Build receipt changed")
    receipt = read_json(path)
    command = receipt.get("command", [])
    require(receipt.get("result") == "PASS" and receipt.get("exit_code") == 0
            and receipt.get("admission", {}).get("result") == "PASS"
            and receipt.get("source_revision") == revision, "A matching successful admitted owning build is required")
    require(isinstance(command, list) and all(isinstance(argument, str) for argument in command)
            and len(command) >= 3 and Path(command[0]).is_absolute() and Path(command[0]).name == "dotnet"
            and command[1] in ("build", "publish")
            and any(Path(argument).name == "Haven.Desktop.csproj" for argument in command[2:]), "Receipt must describe the normal Haven.Desktop owning build")
    # Local trust boundary: the existing guard/worker supplies this receipt.
    # These checks pin its contents; they do not authenticate an execution issuer.
    pin = receipt.get("admission", {}).get("source_manifest")
    if pin:
        source_manifest = absolute(pin["path"])
        require(hashed(source_manifest) == pin["sha256"] and regular(source_manifest).st_size == pin["bytes"],
                "Retained owning source-manifest pin changed")
    output = None
    for index, argument in enumerate(command):
        if argument in ("-o", "--output") and index + 1 < len(command):
            output = command[index + 1]
        elif argument.startswith("--output="):
            output = argument.split("=", 1)[1]
    require(output is not None and absolute(output) == runtime,
            "Owning build receipt must name this exact runtime with --output/-o")


def runtime_files(runtime):
    require(runtime == runtime.resolve() and runtime.is_dir() and not runtime.is_symlink(), "Runtime must be a canonical real directory")
    files = {}
    total = 0
    for folder, directories, names in os.walk(runtime, followlinks=False):
        directory = Path(folder)
        info = directory.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.getuid() and not info.st_mode & 0o222,
                "Runtime directories must be immutable (no write bits): " + str(directory))
        for name in directories:
            require(not (directory / name).is_symlink(), "Runtime directory links are refused")
        for name in names:
            path = directory / name
            info = regular(path)
            require(not info.st_mode & 0o222, "Runtime files must be immutable: " + str(path))
            total += info.st_size
            require(len(files) < 10000 and total <= 4 * 1024**3, "Runtime exceeds review bounds")
            files[str(path.relative_to(runtime))] = {"bytes": info.st_size, "sha256": hashed(path), "mode": stat.S_IMODE(info.st_mode)}
    require(all(name in files for name in ("Haven", "Haven.dll", "Haven.deps.json", "Haven.runtimeconfig.json")),
            "The complete normal Haven runtime layout is required")
    with (runtime / "Haven").open("rb") as stream:
        header = stream.read(64)
    require(len(header) == 64 and header[:6] == b"\x7fELF\x02\x01"
            and struct.unpack_from("<H", header, 18)[0] == 62
            and files["Haven"]["mode"] & 0o111, "Haven must be an executable Linux x86-64 ELF apphost")
    return files


def prepare(runtime_value, receipt_value, receipt_hash, revision, output_value):
    runtime, receipt, output = map(absolute, (runtime_value, receipt_value, output_value))
    require(re.fullmatch(r"(?:[0-9a-f]{40}|[0-9a-f]{64})", revision or ""), "Exact source revision required")
    require(runtime not in output.parents and output != runtime, "Manifest belongs outside its immutable runtime")
    build_proof(receipt, receipt_hash, revision, runtime)
    value = {"schema": "9to1.live-preview-runtime/v1", "source_revision": revision, "runtime_directory": str(runtime),
             "build_receipt": {"path": str(receipt), "sha256": receipt_hash}, "files": runtime_files(runtime), "prepared_at": now()}
    create_json(output, value)
    return {"manifest": str(output), "manifest_sha256": hashed(output), "source_revision": revision}


def validate_manifest(value, expected_hash=None):
    path = absolute(value)
    digest = hashed(path)
    require(expected_hash is None or digest == expected_hash, "Approved runtime manifest changed")
    manifest = read_json(path)
    require(manifest.get("schema") == "9to1.live-preview-runtime/v1", "Unknown runtime manifest")
    runtime = absolute(manifest["runtime_directory"])
    for name in manifest["files"]:
        safe_relative(name)
    build_proof(absolute(manifest["build_receipt"]["path"]), manifest["build_receipt"]["sha256"], manifest["source_revision"], runtime)
    require(runtime_files(runtime) == manifest["files"], "Approved runtime bytes, modes or file set changed")
    return manifest, digest


def config(root):
    value = read_json(root / "config.json")
    require(value.get("schema") == 1, "Unknown launcher configuration")
    return value


def test_profile_parent(root, cfg, runtime=None):
    require(cfg.get("test_profile_parent"), "Configure an explicit private test-profile parent before resetting")
    parent = private_root(cfg["test_profile_parent"])
    require(parent == parent.resolve(), "Test-profile parent must be canonical, without symlink parents")
    data = absolute(cfg["data_directory"])
    require(parent != root and root not in parent.parents and parent not in root.parents
            and parent != data and data not in parent.parents,
            "Test-profile parent must be separate from launcher state and not inside an existing profile")
    if runtime:
        require(parent != runtime and parent not in runtime.parents and runtime not in parent.parents,
                "Test-profile parent and immutable runtime must be separate")
    return parent


def configure_test_profiles(root_value, parent_value):
    """Opt in an existing copy to fresh profiles; does not close or start anything."""
    root = private_root(root_value)
    with operation(root):
        before = config(root)
        after = {**before, "test_profile_parent": str(absolute(parent_value))}
        test_profile_parent(root, after)
        receipt = root / "profile-configuration" / (str(uuid.uuid4()) + ".json")
        create_json(receipt, {"schema": "9to1.live-preview-test-profile-configuration/v1", "at": now(),
                              "before": before, "after": after, "application_started": False})
        atomic(root / "config.json", after)
    return {"test_profile_parent": after["test_profile_parent"], "receipt": str(receipt), "application_started": False}


def current(root):
    path = root / "current.json"
    if not path.exists():
        return None
    value = read_json(path)
    require(re.fullmatch(r"[0-9a-f-]{36}", value.get("run_id", "")), "Invalid retained run identity")
    return value


def run_directory(root, run):
    return root / "runs" / run["run_id"]


def owned_process(launch):
    expected = launch["process"]
    actual = process_identity(expected["pid"])
    require(actual and actual["state"] != "Z" and actual["start_ticks"] == expected["start_ticks"]
            and actual["session"] == expected["session"], "Owned application PID/start identity is absent or changed")
    proc = Path("/proc") / str(expected["pid"])
    require(proc.stat().st_uid == os.getuid()
            and str((proc / "exe").readlink()) == launch["executable"], "Owned executable identity changed")
    return actual


def status(root_value):
    root = private_root(root_value)
    value = current(root)
    if not value:
        return {"state": "NOT_STARTED", "data_directory": config(root)["data_directory"]}
    directory = run_directory(root, value)
    if not (directory / "launch.json").exists():
        if (directory / "error.json").exists():
            error = read_json(directory / "error.json")
            exit_record = read_json(directory / "exit.json") if (directory / "exit.json").exists() else None
            return {"state": "LAUNCH_ERROR_REQUIRES_REVIEW", **value, "error": error, "exit": exit_record}
        return {"state": "STARTING_OR_BLOCKED", **value}
    launch = read_json(directory / "launch.json")
    result = {"run_id": value["run_id"], "source_revision": launch["source_revision"], "runtime_directory": launch["runtime_directory"],
              "manifest_sha256": launch["manifest_sha256"], "runtime_hashes": launch["runtime_hashes"],
              "process": launch["process"], "data_directory": launch["data_directory"],
              "profile_observation": launch["profile_observation"], "profile_confirmation": "OPERATOR_RECORDED_NOT_APP_CONFIRMED"}
    if (directory / "exit.json").exists():
        exit_record = read_json(directory / "exit.json")
        require(exit_record["run_id"] == value["run_id"] and exit_record["process"] == launch["process"], "Exit receipt identity mismatch")
        return {"state": "EXITED_CLEANLY" if exit_record["exit_code"] == 0 else "EXIT_FAILED", "exit_code": exit_record["exit_code"], **result}
    def pending_exit():
        if (directory / "exit.json").exists():
            return status(root)
        guardian = process_identity(launch["guardian"]["pid"])
        if (directory / "exit.json").exists():
            return status(root)
        require(guardian and guardian["start_ticks"] == launch["guardian"]["start_ticks"] and guardian["state"] != "Z",
                "Application ended without its original guardian exit receipt; manual review required")
        return {"state": "EXIT_RECEIPT_PENDING", **result}
    actual = process_identity(launch["process"]["pid"])
    if actual is None or actual["state"] == "Z":
        return pending_exit()
    try:
        owned_process(launch)
    except (FileNotFoundError, ProcessLookupError):
        return pending_exit()
    except Refusal:
        actual = process_identity(launch["process"]["pid"])
        if actual is None or actual["state"] == "Z":
            return pending_exit()
        raise
    guardian = process_identity(launch["guardian"]["pid"])
    if (directory / "exit.json").exists():
        return status(root)
    require(guardian and guardian["start_ticks"] == launch["guardian"]["start_ticks"] and guardian["state"] != "Z",
            "Original guardian identity changed; no close/replacement attempted")
    return {"state": "RUNNING", **result}


def refuse_existing_app():
    # Never adopt or close an installed/untracked copy, even on another profile.
    for proc in Path("/proc").iterdir():
        if not proc.name.isdigit():
            continue
        try:
            if proc.stat().st_uid != os.getuid():
                continue
            executable = (proc / "exe").readlink().name
            arguments = (proc / "cmdline").read_bytes().split(b"\0")
            require(executable != "Haven" and not any(Path(os.fsdecode(arg)).name == "Haven.dll" for arg in arguments if arg),
                    "An existing Haven process requires manual close/reconciliation: " + proc.name)
        except (FileNotFoundError, ProcessLookupError, PermissionError):
            continue


def start_locked(root, manifest_path, reviewed_failure=None, profile_reset=None):
    require(sys.platform == "linux" and platform.machine().lower() in ("x86_64", "amd64"), "Linux x86-64 is required")
    previous = current(root)
    if previous:
        state = status(root)
        if state["state"] != "EXITED_CLEANLY":
            require(state["state"] == "EXIT_FAILED" and reviewed_failure
                    and reviewed_failure["failed_run_id"] == previous["run_id"]
                    and hashed(run_directory(root, previous) / "exit.json") == reviewed_failure["failed_exit_sha256"],
                    "Previous run has not exited cleanly; preserved state requires review")
    manifest, digest = validate_manifest(manifest_path)
    if reviewed_failure:
        require(digest == reviewed_failure["candidate_manifest_sha256"], "Reviewed replacement manifest changed")
    if profile_reset:
        require(digest == profile_reset["candidate_manifest_sha256"], "Reset replacement manifest changed")
    cfg = config(root)
    data = absolute(cfg["data_directory"])
    require(data == data.resolve() and data.is_dir() and not data.is_symlink(), "Configured profile path changed")
    runtime = absolute(manifest["runtime_directory"])
    require(runtime != data and runtime not in data.parents and data not in runtime.parents
            and runtime != root and runtime not in root.parents and root not in runtime.parents,
            "Immutable runtime, application data and launcher state must be separate")
    refuse_existing_app()
    run_id = profile_reset["new_run_id"] if profile_reset else str(uuid.uuid4())
    directory = root / "runs" / run_id
    directory.mkdir(mode=0o700, parents=True)
    value = {"run_id": run_id, "manifest": str(absolute(manifest_path)), "manifest_sha256": digest}
    value["profile"] = {key: cfg[key] for key in ("data_directory", "profile_observation", "profile_confirmation")}
    if reviewed_failure:
        value["recovery"] = reviewed_failure["audit_pin"]
    if profile_reset:
        require(profile_reset["new_data_directory"] == str(data), "Planned reset profile changed")
        value["profile_reset"] = profile_reset["request_pin"]
    atomic(root / "current.json", value)
    with (directory / "guardian.log").open("xb") as log:
        _guardians[:] = [guardian for guardian in _guardians if guardian.poll() is None]
        _guardians.append(subprocess.Popen(
            [sys.executable, "-B", str(Path(__file__).resolve()), "_supervise", "--state-root", str(root), "--run-id", run_id],
            stdin=subprocess.DEVNULL, stdout=log, stderr=log, start_new_session=True, close_fds=True))
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if (directory / "launch.json").exists():
            return status(root)
        if (directory / "error.json").exists():
            raise Refusal("Application launch failed; retained details: " + str(directory / "error.json"))
        time.sleep(0.05)
    raise Refusal("Launch handshake timed out; retained ownership must be reconciled, not replaced")


def start(root_value, manifest_path):
    root = private_root(root_value)
    with operation(root):
        return start_locked(root, manifest_path)


def recover_failed(root_value, manifest_path, review_value, review_hash):
    """Explicit reviewed restart after a failed exit; never a normal-close receipt."""
    root, review_path = private_root(root_value), absolute(review_value)
    with operation(root):
        _, candidate_hash = validate_manifest(manifest_path)
        state = status(root)
        require(state["state"] == "EXIT_FAILED", "Recovery requires an already recorded failed exit, never a running or cleanly closed app")
        run = current(root)
        directory = run_directory(root, run)
        launch = read_json(directory / "launch.json")
        exit_path = directory / "exit.json"
        exit_hash = hashed(exit_path)
        original = process_identity(launch["process"]["pid"])
        require(not original or original["state"] == "Z"
                and original["start_ticks"] == launch["process"]["start_ticks"],
                "Failed run's original process is still live or its PID was reused; no restart permitted")
        guardian = process_identity(launch["guardian"]["pid"])
        require(not guardian or guardian["state"] == "Z"
                and guardian["start_ticks"] == launch["guardian"]["start_ticks"],
                "Failed run's guardian is not terminal or its PID was reused")
        require(not remaining_group(launch["process"]) and not remaining_group(launch["guardian"]),
                "Failed run retains process-group members; manual custody review required")
        launch_hash = hashed(directory / "launch.json")
        profile = config(root)["data_directory"]
        require(launch["data_directory"] == profile, "Configured profile changed since the failed launch")
        require(re.fullmatch(r"[0-9a-f]{64}", review_hash or ""), "Exact local review receipt SHA-256 required")
        require(regular(review_path).st_size <= 4 * 1024 * 1024, "Review receipt exceeds its size bound")
        raw = review_path.read_bytes()
        require(hashlib.sha256(raw).hexdigest() == review_hash, "Local failed-run review receipt changed")
        review = json.loads(raw)
        require(isinstance(review, dict)
                and review.get("schema") == "9to1.live-preview-failed-run-review/v1"
                and review.get("result") == "RETRY_SAFE"
                and review.get("failed_run_id") == run["run_id"]
                and review.get("failed_launch") == {"path": str(directory / "launch.json"), "sha256": launch_hash}
                and review.get("failed_exit") == {"path": str(exit_path), "sha256": exit_hash}
                and review.get("failed_process") == launch["process"]
                and review.get("data_directory") == profile
                and review.get("candidate_manifest_sha256") == candidate_hash
                and isinstance(review.get("reviewer"), str) and bool(review["reviewer"].strip())
                and isinstance(review.get("diagnosis"), str) and 0 < len(review["diagnosis"].strip()) <= 2000
                and isinstance(review.get("reason"), str) and 0 < len(review["reason"].strip()) <= 2000,
                "Explicit local review must bind this failed run, actual exit and exact replacement with a reviewer and reason")
        refuse_existing_app()
        audit_path = root / "recoveries" / (run["run_id"] + "-" + review_hash + ".json")
        require(not audit_path.exists(), "This failed-run review was already consumed; a fresh local review is required")
        audit = {"schema": "9to1.live-preview-failed-run-recovery/v1", "at": now(),
                 "operation": "REVIEWED_FAILED_EXIT_RESTART_NOT_NORMAL_RELOAD",
                 "failed_run_id": run["run_id"], "failed_exit_sha256": exit_hash,
                 "failed_launch_sha256": launch_hash, "failed_process": launch["process"], "data_directory": profile,
                 "candidate_manifest_sha256": candidate_hash,
                 "review_receipt": {"path": str(review_path), "sha256": review_hash},
                 "reviewer": review["reviewer"], "reason": review["reason"], "diagnosis": review["diagnosis"],
                 "stage": "REVIEW_RECORDED_BEFORE_ACTIVATION",
                 "normal_close_verified": False}
        create_json(audit_path, audit)  # Persist review before replacing current; old run remains untouched.
        require(hashed(review_path) == review_hash, "Review receipt changed before restart")
        result = start_locked(root, manifest_path, reviewed_failure={**audit,
            "audit_pin": {"path": str(audit_path), "sha256": hashed(audit_path)}})
        return {**result, "activation": audit["operation"], "recovery_receipt": str(audit_path)}


def supervise(root_value, run_id):
    root = private_root(root_value)
    run = current(root)
    require(run and run["run_id"] == run_id, "Superseded launch refused")
    directory = run_directory(root, run)
    # This is only a per-run child-custody lock, separate from the SDK allocation.
    with operation(directory):
        require(not any((directory / name).exists() for name in ("launch.json", "exit.json", "error.json")),
                "This retained run has already been launched; never duplicate its child")
        child = None
        identity = None
        executable = None
        try:
            manifest, digest = validate_manifest(run["manifest"], run["manifest_sha256"])
            cfg = run.get("profile", config(root))  # New runs pin their profile before the guardian is allocated.
            data = absolute(cfg["data_directory"])
            require(data == data.resolve() and data.is_dir() and data.stat().st_uid == os.getuid(), "Pinned run profile changed")
            if run.get("profile_reset"):
                pin = run["profile_reset"]
                require(hashed(absolute(pin["path"])) == pin["sha256"], "Profile-reset request receipt changed")
                request = read_json(absolute(pin["path"]))
                require(request["new_run_id"] == run_id and request["new_data_directory"] == str(data)
                        and request["candidate_manifest_sha256"] == digest, "Guardian reset/profile/runtime identity mismatch")
            executable = str(absolute(manifest["runtime_directory"]) / "Haven")
            environment = dict(os.environ, HAVEN_DATA_DIR=cfg["data_directory"])
            refuse_existing_app()
            with (directory / "application.log").open("xb") as log:
                child = subprocess.Popen([executable], cwd=manifest["runtime_directory"], env=environment, stdin=subprocess.DEVNULL,
                                         stdout=log, stderr=log, start_new_session=True, close_fds=True)
                identity = process_identity(child.pid)
                require(identity is not None, "Could not retain original application process identity")
                launch = {"run_id": run_id, "source_revision": manifest["source_revision"], "runtime_directory": manifest["runtime_directory"],
                          "manifest_sha256": digest, "build_receipt": manifest["build_receipt"], "executable": executable,
                          "runtime_hashes": {name: manifest["files"][name]["sha256"] for name in ("Haven", "Haven.dll")},
                          "process": identity, "guardian": process_identity(os.getpid()), "display": environment.get("DISPLAY"),
                           "data_directory": cfg["data_directory"], "profile_observation": cfg["profile_observation"], "started_at": now()}
                if run.get("recovery"):
                    launch["recovery"] = run["recovery"]
                if run.get("profile_reset"):
                    launch["profile_reset"] = run["profile_reset"]
                create_json(directory / "launch.json", launch)
                code = child.wait()
            create_json(directory / "exit.json", {"run_id": run_id, "process": identity, "exit_code": code, "ended_at": now()})
        except Exception as error:
            try:
                create_json(directory / "error.json", {"run_id": run_id, "error": str(error), "process": identity,
                                                       "executable": executable, "guardian": process_identity(os.getpid()), "at": now()})
            finally:
                # Even a failed launch-receipt write must not silently orphan the
                # already started child. Wait/reap it without sending any signal.
                if child is not None:
                    code = child.wait()
                    if not (directory / "exit.json").exists():
                        create_json(directory / "exit.json", {"run_id": run_id, "process": identity, "exit_code": code, "ended_at": now()})
            raise


def command_output(arguments):
    return subprocess.run(arguments, check=True, capture_output=True, text=True, timeout=5).stdout


def window_matches(text, pid, title):
    process = re.search(r"^_NET_WM_PID(?:\([^)]*\))?\s*=\s*(\d+)\s*$", text, re.M)
    name = re.search(r'^_NET_WM_NAME(?:\([^)]*\))?\s*=\s*(".*")\s*$', text, re.M)
    transient = re.search(r"^WM_TRANSIENT_FOR[^\n]*(0x[0-9a-fA-F]+)", text, re.M)
    return bool(process and int(process[1]) == pid and name and json.loads(name[1]) == title
                and "_NET_WM_WINDOW_TYPE_NORMAL" in text and not (transient and int(transient[1], 16)))


def request_normal_close(launch, title):
    owned_process(launch)
    require(launch.get("display") and os.environ.get("DISPLAY") == launch["display"], "Use the same X11 DISPLAY as the launched app")
    xprop, wmctrl = shutil.which("xprop"), shutil.which("wmctrl")
    require(xprop and wmctrl, "Normal X11 close requires xprop and wmctrl; no signal fallback is provided")
    windows = re.findall(r"0x[0-9a-fA-F]+", command_output([xprop, "-root", "_NET_CLIENT_LIST"]))
    properties = ["_NET_WM_PID", "_NET_WM_NAME", "_NET_WM_WINDOW_TYPE", "WM_TRANSIENT_FOR"]
    matches = [window for window in windows if window_matches(command_output([xprop, "-id", window, *properties]), launch["process"]["pid"], title)]
    require(len(matches) == 1, "Exactly one normal main window with the owned PID and exact title is required")
    owned_process(launch)
    require(window_matches(command_output([xprop, "-id", matches[0], *properties]), launch["process"]["pid"], title), "Window identity changed before close")
    command_output([wmctrl, "-ic", matches[0]])
    return matches[0]


def reload(root_value, manifest_path, timeout=90, close=request_normal_close):
    require(0 < timeout <= 300, "Close timeout must be positive and at most 300 seconds")
    root = private_root(root_value)
    with operation(root):
        _, digest = validate_manifest(manifest_path)  # Refuse a bad candidate before disturbing the live copy.
        state = status(root)
        if state["state"] == "RUNNING":
            run = current(root)
            launch = read_json(run_directory(root, run) / "launch.json")
            close(launch, config(root)["window_title"])
            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline:
                state = status(root)
                if state["state"] not in ("RUNNING", "EXIT_RECEIPT_PENDING"):
                    break
                time.sleep(0.05)
            require(state["state"] == "EXITED_CLEANLY", "Normal close was vetoed, timed out or failed; no force/replacement was attempted")
        else:
            require(state["state"] == "EXITED_CLEANLY", "An owned clean exit is required before reload")
        validate_manifest(manifest_path, digest)
        return start_locked(root, manifest_path)


def reset_profile(root_value, manifest_path, expected_run_id, timeout=90, close=request_normal_close):
    """One normal-close/fresh-profile cycle. Never erases a profile or recovers a failed exit."""
    require(0 < timeout <= 300, "Close timeout must be positive and at most 300 seconds")
    root = private_root(root_value)
    with operation(root):
        manifest, digest = validate_manifest(manifest_path)  # Before any close/profile side effect.
        before = config(root)
        parent = test_profile_parent(root, before, absolute(manifest["runtime_directory"]))
        old_run = current(root)
        require(old_run and old_run["run_id"] == expected_run_id, "Expected current run changed; stale reset refused")
        state = status(root)
        require(state["state"] in ("RUNNING", "EXITED_CLEANLY"), "Fresh-profile reset requires a running or cleanly exited owned run; failed exits require Root review")
        old_directory = run_directory(root, old_run)
        launch_path = old_directory / "launch.json"
        launch = read_json(launch_path)
        require(launch["data_directory"] == before["data_directory"], "Configured profile differs from the owned run")
        reset_id, new_run_id = str(uuid.uuid4()), str(uuid.uuid4())
        new_data = parent / ("profile-" + reset_id)
        require(not new_data.exists() and not new_data.is_symlink(), "Fresh test profile must be create-new")
        after = {**before, "data_directory": str(new_data),
                 "profile_observation": "Fresh isolated onboarding test profile from one-shot reset " + reset_id,
                 "profile_confirmation": "OPERATOR_RECORDED_NOT_APP_CONFIRMED"}
        journal = root / "profile-resets" / reset_id
        request_path = journal / "request.json"
        request = {"schema": "9to1.live-preview-profile-reset/v1", "at": now(), "reset_id": reset_id,
                   "operation": "NORMAL_CLOSE_CREATE_NEW_TEST_PROFILE", "old_run": old_run,
                   "old_launch": {"path": str(launch_path), "sha256": hashed(launch_path)},
                   "old_data_directory": before["data_directory"], "new_data_directory": str(new_data),
                   "new_run_id": new_run_id, "candidate_manifest": str(absolute(manifest_path)),
                   "candidate_manifest_sha256": digest, "config_before": before, "config_after": after}
        create_json(request_path, request)  # Durable plan BEFORE close; never rewrites historical run receipts.
        try:
            if state["state"] == "RUNNING":
                close(launch, before["window_title"])
                deadline = time.monotonic() + timeout
                while time.monotonic() < deadline:
                    state = status(root)
                    if state["state"] not in ("RUNNING", "EXIT_RECEIPT_PENDING"):
                        break
                    time.sleep(.05)
            require(state["state"] == "EXITED_CLEANLY", "Normal close was vetoed, timed out or failed; no profile reset or replacement attempted")
            require_clean_terminal_run(launch, timeout)
            refuse_existing_app()
            require(current(root) == old_run and config(root) == before, "Run/config changed during normal close")
            validate_manifest(manifest_path, digest)
            require(test_profile_parent(root, before, absolute(manifest["runtime_directory"])) == parent, "Test-profile parent changed")
            create_json(journal / "closed.json", {"at": now(), "old_run_id": old_run["run_id"],
                        "old_exit": {"path": str(old_directory / "exit.json"), "sha256": hashed(old_directory / "exit.json")},
                        "normal_close_verified": True})
            new_data.mkdir(mode=0o700)  # Never exist_ok, copy, rename, clear, or delete an existing profile.
            sync_directory(parent)
            require(new_data == new_data.resolve() and stat.S_IMODE(new_data.stat().st_mode) == 0o700, "Fresh profile must be private and canonical")
            atomic(root / "config.json", after)
            result = start_locked(root, manifest_path, profile_reset={"new_run_id": new_run_id,
                "new_data_directory": str(new_data), "candidate_manifest_sha256": digest,
                "request_pin": {"path": str(request_path), "sha256": hashed(request_path)}})
            require(result.get("run_id") == new_run_id and result.get("data_directory") == str(new_data), "Reset successor identity was not observed")
            require(result.get("state") == "RUNNING", "Reset successor did not remain running; retained exit/custody requires review")
            create_json(journal / "result.json", {"at": now(), "result": result, "old_run_id": old_run["run_id"],
                        "new_run_id": new_run_id, "old_data_directory": before["data_directory"], "new_data_directory": str(new_data),
                        "stage": "SUCCESSOR_OBSERVED", "onboarding_acceptance": "NOT_VERIFIED"})
            return {**result, "activation": request["operation"], "reset_receipt": str(request_path),
                    "old_data_directory": before["data_directory"], "old_run_id": old_run["run_id"]}
        except Exception as error:
            # Roll back configuration only when the successor cannot have used it.
            # A live, receipt-failed or still-starting child retains its new profile/custody.
            try:
                rollback = reset_config_after_failure(root, old_run, new_run_id, before, after)
                create_json(journal / "failure.json", {"at": now(), "error": str(error), "config_disposition": rollback,
                            "old_run_id": old_run["run_id"], "new_run_id": new_run_id,
                            "old_data_directory": before["data_directory"], "new_data_directory": str(new_data),
                            "new_profile_preserved": new_data.exists()})
            except Exception as custody_error:
                if hasattr(error, "add_note"):
                    error.add_note("Reset failure reconciliation also failed; retain all custody for review: " + repr(custody_error))
            raise


def require_clean_terminal_run(launch, timeout):
    original = process_identity(launch["process"]["pid"])
    require(not original or original["state"] == "Z" and original["start_ticks"] == launch["process"]["start_ticks"],
            "Clean exit receipt conflicts with a live/reused application PID")
    deadline = time.monotonic() + min(timeout, 3)
    while True:
        guardian = process_identity(launch["guardian"]["pid"])
        if not guardian or guardian["state"] == "Z" and guardian["start_ticks"] == launch["guardian"]["start_ticks"]:
            break
        require(guardian["start_ticks"] == launch["guardian"]["start_ticks"] and time.monotonic() < deadline,
                "Original guardian is not terminal or its PID was reused")
        time.sleep(.02)
    require(not remaining_group(launch["process"]) and not remaining_group(launch["guardian"]),
            "Old run retains process-group members; no fresh profile activation")


def reset_config_after_failure(root, old_run, new_run_id, before, after):
    cfg = config(root)
    if cfg == before:
        return "ORIGINAL_CONFIG_UNCHANGED"
    if cfg != after:
        return "CONFIG_CHANGED_EXTERNALLY_REQUIRES_REVIEW"
    if current(root) == old_run:
        atomic(root / "config.json", before)
        return "ROLLED_BACK_BEFORE_SUCCESSOR_ALLOCATION"
    run = current(root)
    if not run or run["run_id"] != new_run_id:
        return "SUCCESSOR_CUSTODY_CHANGED_REQUIRES_REVIEW"
    directory = run_directory(root, run)
    try:
        with operation(directory):  # An in-flight guardian owns this lock through its child's terminal receipt.
            error = read_json(directory / "error.json") if (directory / "error.json").exists() else {}
            if (directory / "launch.json").exists() or (directory / "exit.json").exists() or error.get("process") is not None:
                return "NEW_PROFILE_USED_RETAINED_FOR_REVIEW"
            if not (directory / "error.json").exists():
                create_json(directory / "error.json", {"run_id": new_run_id, "process": None,
                            "error": "Reset aborted before guardian launch; original config restored", "at": now()})
            # The durable error plus per-run lock fences a delayed guardian before it can spawn.
            atomic(root / "config.json", before)
            return "ROLLED_BACK_NO_CHILD_FAILED_RUN_RETAINED"
    except Refusal:
        return "GUARDIAN_PENDING_NEW_PROFILE_RETAINED_FOR_REVIEW"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    setup = commands.add_parser("init")
    setup.add_argument("--state-root", required=True)
    setup.add_argument("--data-directory", required=True)
    setup.add_argument("--window-title", default="9to1")
    setup.add_argument("--profile-observation", required=True)
    prepare_parser = commands.add_parser("prepare")
    for name in ("runtime", "build-receipt", "receipt-sha256", "source-revision", "output"):
        prepare_parser.add_argument("--" + name, required=True)
    for name in ("start", "status", "reload", "_supervise"):
        command = commands.add_parser(name)
        command.add_argument("--state-root", required=True)
        if name in ("start", "reload"):
            command.add_argument("--manifest", required=True)
        if name == "reload":
            command.add_argument("--timeout", type=float, default=90)
        if name == "_supervise":
            command.add_argument("--run-id", required=True)
    recovery = commands.add_parser("recover-failed")
    for name in ("state-root", "manifest", "review-receipt", "review-sha256"):
        recovery.add_argument("--" + name, required=True)
    profiles = commands.add_parser("configure-test-profiles")
    profiles.add_argument("--state-root", required=True)
    profiles.add_argument("--test-profile-parent", required=True)
    reset = commands.add_parser("reset-profile")
    for name in ("state-root", "manifest", "expected-run-id"):
        reset.add_argument("--" + name, required=True)
    reset.add_argument("--timeout", type=float, default=90)
    args = parser.parse_args()
    os.umask(0o077)
    try:
        if args.command == "init":
            result = init(args.state_root, args.data_directory, args.window_title, args.profile_observation)
        elif args.command == "prepare":
            result = prepare(args.runtime, args.build_receipt, args.receipt_sha256, args.source_revision, args.output)
        elif args.command == "start":
            result = start(args.state_root, args.manifest)
        elif args.command == "status":
            result = status(args.state_root)
        elif args.command == "reload":
            result = reload(args.state_root, args.manifest, args.timeout)
        elif args.command == "recover-failed":
            result = recover_failed(args.state_root, args.manifest, args.review_receipt, args.review_sha256)
        elif args.command == "configure-test-profiles":
            result = configure_test_profiles(args.state_root, args.test_profile_parent)
        elif args.command == "reset-profile":
            result = reset_profile(args.state_root, args.manifest, args.expected_run_id, args.timeout)
        else:
            supervise(args.state_root, args.run_id)
            return
        print(json.dumps(result, indent=2))
    except Exception as error:
        print("Live preview refused: " + str(error), file=sys.stderr)
        raise SystemExit(2) from error


if __name__ == "__main__":
    main()
