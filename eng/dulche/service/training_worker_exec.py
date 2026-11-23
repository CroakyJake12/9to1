#!/usr/bin/python3
"""Linux-only worker entry point. Limit writable paths and visible compute-device nodes before exec."""

import ctypes
import hashlib
import json
import os
import platform
import sys
from pathlib import Path


def checked(value, operation):
    if value < 0:
        err = ctypes.get_errno()
        raise OSError(err, operation + ": " + os.strerror(err))
    return value


def restrict_files(read_roots, output, devices):
    if platform.machine() != "x86_64" or sys.platform != "linux":
        raise RuntimeError("This verified worker isolation targets x86-64 Linux")
    c = ctypes.CDLL(None, use_errno=True)
    c.syscall.restype = ctypes.c_long
    # linux/uapi/linux/landlock.h and the x86_64 syscall table. Device ioctl rights require ABI 5.
    abi = checked(c.syscall(444, 0, 0, 1), "query Landlock")
    if abi < 5:
        raise RuntimeError("Worker startup requires Landlock ABI 5 or later")
    execute, write, read, ls, truncate, ioctl = 1, 2, 4, 8, 1 << 14, 1 << 15
    all_fs = (1 << 16) - 1
    attr = ctypes.c_uint64(all_fs)
    ruleset = checked(
        c.syscall(444, ctypes.byref(attr), ctypes.sizeof(attr), 0),
        "create Landlock ruleset",
    )

    class Beneath(ctypes.Structure):
        _pack_ = 1
        _fields_ = [("allowed_access", ctypes.c_uint64), ("parent_fd", ctypes.c_int32)]

    def grant(path, rights):
        path = Path(path)
        if not path.exists():
            return
        if not path.is_dir():
            rights &= execute | write | read | truncate | ioctl
        fd = os.open(path, os.O_PATH | os.O_CLOEXEC)
        try:
            rule = Beneath(rights, fd)
            checked(
                c.syscall(445, ruleset, 1, ctypes.byref(rule), 0),
                "add Landlock rule for " + str(path),
            )
        finally:
            os.close(fd)

    try:
        for path in read_roots:
            grant(path, execute | read | ls)
        # Allow ordinary output/checkpoint operations; a worker has no reason to create block/char devices.
        grant(output, all_fs & ~((1 << 6) | (1 << 11) | ioctl))
        for path in devices:
            grant(path, read | write | ioctl)
        checked(c.prctl(38, 1, 0, 0, 0), "set no_new_privs")
        checked(c.syscall(446, ruleset, 0), "enforce Landlock")
    finally:
        os.close(ruleset)
    return abi


def main():
    job = Path(sys.argv[1]).resolve(strict=True)
    manifest = json.loads((job / "launch.json").read_text())
    exe = Path(manifest["executable"]["path"]).resolve(strict=True)
    with exe.open("rb") as f:
        if (
            hashlib.file_digest(f, "sha256").hexdigest()
            != manifest["executable"]["sha256"]
        ):
            raise RuntimeError("Worker executable changed since admission")
    out = job / "output"
    roots = [
        Path(p) for p in ("/usr", "/lib", "/lib64", "/bin", "/etc", "/proc", "/sys")
    ]
    roots += [
        job,
        exe,
        Path(manifest["cwd"]),
        *[Path(p) for p in manifest["read_roots"]],
    ]
    devices = [Path("/dev") / p for p in ("null", "zero", "full", "random", "urandom")]
    if manifest["device"] != "cpu":
        minor = manifest.get("gpu_minor")
        if type(minor) is not int or not 0 <= minor < 256:
            raise RuntimeError("Selected GPU minor is unverified")
        devices += [
            Path("/dev") / p
            for p in (
                f"nvidia{minor}",
                "nvidiactl",
                "nvidia-uvm",
                "nvidia-uvm-tools",
                "nvidia-caps",
            )
        ]
    abi = restrict_files(roots, out, devices)
    if manifest["device"] == "cpu" and Path("/dev/nvidiactl").exists():
        try:
            fd = os.open("/dev/nvidiactl", os.O_RDONLY | os.O_CLOEXEC)
        except PermissionError:
            pass
        else:
            os.close(fd)
            raise RuntimeError("CPU isolation failed to deny GPU access")
    try:
        fd = os.open(
            job / "control-isolation-probe", os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600
        )
    except PermissionError:
        pass
    else:
        os.close(fd)
        raise RuntimeError("Worker can mutate supervisor control state")
    (out / "isolation.json").write_text(
        json.dumps(
            {"landlock_abi": abi, "pid": os.getpid(), "device": manifest["device"]}
        )
    )
    tmp = out / "tmp"
    tmp.mkdir(exist_ok=True, mode=0o700)
    env = {
        k: v
        for k, v in os.environ.items()
        if k.startswith("DULCHE_TRAIN_")
        or k
        in (
            "DULCHE_RESUME_CHECKPOINT",
            "CUDA_VISIBLE_DEVICES",
            "PATH",
            "LANG",
            "LC_ALL",
            "HOME",
            "OMP_NUM_THREADS",
        )
    }
    env.update(
        TMPDIR=str(tmp),
        HF_HUB_OFFLINE="1",
        TRANSFORMERS_OFFLINE="1",
        PYTHONDONTWRITEBYTECODE="1",
    )
    os.execve(exe, [str(exe), *manifest["arguments"]], env)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, KeyError) as exc:
        sys.exit("Training worker refused: " + str(exc))
