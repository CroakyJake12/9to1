"""Exact Ubuntu/V100 process command construction from an admitted joint registration.

No process spawning or fallback on import. The dispatch broker must validate
its own signed registration and Flash+hardware admissions before passing the
pins here. The SSH credentials are from the original owner-managed QEMU unit.
"""
from dataclasses import dataclass
import re
import shlex

from .artifacts import CustodyError, canonical_id

_SHA = re.compile(r"[0-9a-f]{64}")
_UUID = re.compile(r"GPU-[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
WORKER_ID = "qwen35-27b-v8-upper-joint-two-rank-v1"
GUEST_KEY_REL = "Documents/Default Project/vm/id_ed25519"
_ENDPOINT = "v100@127.0.0.1"
_EDGE = "astra-v100-guest"
_RTX = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"
_V100 = "GPU-b15521a1-14ac-96c1-7166-44abeb5db769"
_GUEST_SITE = "/home/v100/.dulche-volta-runtime-2.13.0-57toaxdq/site"
_NATIVE_RUNTIME_REL = "/.local/share/unsloth-custom-20261014/native-venv/bin/python"

# Same-descriptor bundle check before imports; no startup/user-site packages.
# Python/runtime dependency pins used by the deployed service are separately
# attested by the broker. Only the pinned module inside the archive is invoked.
_BOOTSTRAP = """
import fcntl, hashlib, os, runpy, stat, sys
path, digest, site, role, attempt, gpu = sys.argv[1:]
fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
with os.fdopen(fd, 'rb') as f:
    info = os.fstat(f.fileno())
    if not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not 0 < info.st_size <= 32 * 1024**2:
        raise RuntimeError('Unadmitted registered joint source archive')
    exact = f.read(32 * 1024**2 + 1)
    if len(exact) != info.st_size or hashlib.sha256(exact).hexdigest() != digest:
        raise RuntimeError('Registered joint source archive differs')
immutable = os.memfd_create('dulche-joint-code', os.MFD_CLOEXEC | os.MFD_ALLOW_SEALING)
with os.fdopen(immutable, 'wb') as code:
    code.write(exact)
    code.flush()
    del exact
    # Stable Linux uapi values; some Python builds omit fcntl seal names.
    fcntl.fcntl(immutable, getattr(fcntl, 'F_ADD_SEALS', 1033), 0x0f)
    if fcntl.fcntl(immutable, getattr(fcntl, 'F_GET_SEALS', 1034)) & 0x0f != 0x0f:
        raise RuntimeError('Could not seal the admitted code archive')
    sys.path[:] = [f'/proc/self/fd/{immutable}', *sys.path, site]
    os.environ.update(CUDA_VISIBLE_DEVICES=gpu, HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1')
    sys.argv = ['custody.v8_registered_rank', role, '--attempt', attempt, '--gpu-uuid', gpu]
    runpy.run_module('custody.v8_registered_rank', run_name='__main__')
"""


@dataclass(frozen=True, slots=True)
class RegisteredEndpoint:
    runtime: str
    archive: str
    archive_sha256: str
    dependency_root: str
    gpu_uuid: str


def _path(path):
    if (type(path) is not str or not path.startswith("/") or len(path) > 4096
            or any(ord(c) < 32 for c in path) or ".." in path.split("/")):
        raise CustodyError("A strict registered absolute endpoint path is required.")
    return path


def _process(endpoint, role, attempt):
    if (type(endpoint) is not RegisteredEndpoint or not isinstance(endpoint.archive_sha256, str)
            or not _SHA.fullmatch(endpoint.archive_sha256) or type(endpoint.gpu_uuid) is not str
            or not _UUID.fullmatch(endpoint.gpu_uuid)):
        raise CustodyError("The registered v8 endpoint has no exact runtime, source and GPU identity.")
    return ("/usr/bin/env", "-u", "LD_PRELOAD", "-u", "LD_LIBRARY_PATH",
            _path(endpoint.runtime), "-I", "-S", "-B", "-c", _BOOTSTRAP,
            _path(endpoint.archive), endpoint.archive_sha256, _path(endpoint.dependency_root),
            role, attempt, endpoint.gpu_uuid)


def owned_endpoint_argv(*, registration_id, attempt_id, native, guest, approved_home,
                        known_hosts, verified_joint_launch):
    """Return native tail, guest stage and independent guest weight argv.

    Caller can use Popen(shell=False) with each tuple. Do not make these
    endpoints available through the old scalar worker or use stdout/stderr
    as proof of custody. Registration and admissions come from the trusted
    daemon, not an input file or CLI flag alone.
    """
    if (registration_id != WORKER_ID or not callable(verified_joint_launch)
            or canonical_id(attempt_id) == "00000000-0000-0000-0000-000000000000"):
        raise CustodyError("The native-only worker cannot authorise a joint v8 run.")
    tail = _process(native, "tail", attempt_id)
    early = _process(guest, "early", attempt_id)
    weights = _process(guest, "weights", attempt_id)
    home = _path(approved_home).rstrip("/")
    if (native.gpu_uuid != _RTX or guest.gpu_uuid != _V100
            or native.runtime != home + _NATIVE_RUNTIME_REL or guest.runtime != "/usr/bin/python3"
            or guest.dependency_root != _GUEST_SITE
            or verified_joint_launch(attempt_id, native, guest) is not True):
        raise CustodyError("Two independently admitted physical GPU owners are required.")
    key = home + "/" + GUEST_KEY_REL
    ssh = ("/usr/bin/ssh", "-F", "/dev/null", "-T", "-i", key, "-p", "2222",
           "-o", "BatchMode=yes", "-o", "ConnectTimeout=3", "-o", "StrictHostKeyChecking=yes",
           "-o", "IdentitiesOnly=yes", "-o", "LogLevel=ERROR", "-o", "Compression=no",
           "-o", "ClearAllForwardings=yes", "-o", f"HostKeyAlias={_EDGE}",
           "-o", "GlobalKnownHostsFile=/dev/null", "-o", "UserKnownHostsFile=" + _path(known_hosts),
           _ENDPOINT)
    # SSH does invoke the remote user's command shell; quote every argv token,
    # including all source and filesystem paths. Never concatenate user args.
    return tail, ssh + (shlex.join(early),), ssh + (shlex.join(weights),)
