"""Shared physical admission for service paths outside EngineManager.load."""
from __future__ import annotations

from contextlib import contextmanager
import fcntl
import os
from pathlib import Path


@contextmanager
def resource_lock():
    directory = Path(os.environ.get("XDG_RUNTIME_DIR", f"/run/user/{os.getuid()}")) / "dulche-engine-admission"
    directory.mkdir(mode=0o700, exist_ok=True)
    fd = os.open(directory / "resource.lock", os.O_CREAT | os.O_NOFOLLOW | os.O_RDWR | os.O_CLOEXEC, 0o600)
    with os.fdopen(fd, "a+") as handle:
        fcntl.flock(handle, fcntl.LOCK_EX)
        yield


@contextmanager
def training_claim(device):
    """The training supervisor retains a physical lease until its worker exits.

    This host only exposes the RTX to local trainers. Unknown GPU identities
    are rejected until an explicit physical mapping is registered; CPU
    workflows retain their existing device isolation and need no GPU lease.
    """
    if device == "cpu":
        yield
        return
    from pooled_runtime import RTX_UUID, assert_devices_available, claim_devices
    if device != RTX_UUID:
        raise RuntimeError("The training GPU has no registered physical admission mapping")
    roles = ["rtx5080"]
    claim = claim_devices(roles)
    with resource_lock():
        assert_devices_available(roles)
        claim.__enter__()
    try:
        yield
    finally:
        claim.__exit__(None, None, None)
