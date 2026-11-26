"""Read-only llama.cpp capability probes and a qualified CUDA/RPC command plan.

This module never starts inference. Dulche must acquire both physical devices,
check fresh thermal/memory proofs, and install the pooled supervisor before
executing a plan. A command plan is not evidence of model compatibility or of
a hard CUDA memory reservation.
"""
from __future__ import annotations

from dataclasses import dataclass, replace
import hashlib
import os
from pathlib import Path
import re
import stat
import subprocess
import struct
import time
from typing import Callable, Mapping

RTX_UUID = "GPU-6cc11d2c-f0b1-4c95-d8f6-bfc3832655c2"
RPC_ENDPOINT = "127.0.0.1:19110"
MIB = 1 << 20
REQUIRED_FLAGS = frozenset({
    "--rpc", "--list-devices", "--device", "--split-mode", "--fit",
    "--fit-target", "--model", "--ctx-size", "--parallel", "--host",
    "--port", "--alias", "--api-key-file", "--threads",
    "--jinja", "--no-webui", "--ubatch-size", "--log-verbosity",
})
DEVICE_ROW = re.compile(
    r"^\s*(CUDA\d+|RPC\d+):\s+(.+?)\s+"
    r"\((\d+) MiB,\s*(\d+) MiB free\)\s*$"
)
DEVICE_START = re.compile(r"^\s*(?:CUDA|RPC)(?:\s*\d|[^\s:]*\s*:|\[|$)")
FIT_LIST_SIGNATURE = re.compile(
    r"(?m)^\s*(?:-[\w-]+,\s*)*--fit-target[ \t]+MiB0[ \t]*,[ \t]*MiB1(?=,|\s|$)"
)
GPU_UUID = re.compile(r"GPU-[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")
# Remove inherited CLI settings and CUDA allocation/kernel/debug overrides.
# GGML_CUDA_P2P is enabled by presence; setting it to "0" does not disable it.
ENV_UNSET_PREFIXES = ("LLAMA_ARG_", "GGML_CUDA_")
ENV_UNSET_NAMES = ("GGML_RPC_SERVERS", "GGML_OP_OFFLOAD_MIN_BATCH", "GGML_BACKEND_PATH")
ENV_OVERRIDES = (("CUDA_VISIBLE_DEVICES", RTX_UUID),)


class BackendError(RuntimeError):
    pass


@dataclass(frozen=True)
class BackendDevice:
    name: str
    description: str
    total_bytes: int
    free_bytes: int


@dataclass(frozen=True)
class BackendProof:
    executable: str
    sha256: str
    version: str
    flags: frozenset[str]
    devices: tuple[BackendDevice, BackendDevice]
    observed_at_ns: int
    per_device_fit_targets: bool
    cuda_uuid: str = RTX_UUID
    rpc_endpoint: str = RPC_ENDPOINT
    dspark_speculation: bool = False


@dataclass(frozen=True)
class DraftConfig:
    path: str
    sha256: str
    expected_bytes: int
    n_max: int = 3


@dataclass(frozen=True)
class GuestIdentity:
    """Evidence acquired from a caller-owned, pinned guest/RPC source.

    The timestamp uses the host's acquisition clock, not the guest's uptime.
    This module validates consistency; it cannot authenticate caller evidence.
    """
    endpoint: str
    rpc_device_name: str
    gpu_uuid: str
    gpu_name: str
    observed_at_ns: int
    owned_rpc_server_identity: str


@dataclass(frozen=True)
class CommandPlan:
    argv: tuple[str, ...]
    executable_sha256: str
    device_order: tuple[str, str]
    exact_free_targets_bytes: tuple[int, int]
    fitting_targets_mib: tuple[int, int]
    fitting_rounding_bytes: tuple[int, int]
    guest_identity: GuestIdentity
    observed_at_ns: int
    evaluated_at_ns: int
    environment_unset_names: tuple[str, ...] = ENV_UNSET_NAMES
    environment_unset_prefixes: tuple[str, ...] = ENV_UNSET_PREFIXES
    environment_overrides: tuple[tuple[str, str], ...] = ENV_OVERRIDES
    display_reserve_policy_bytes: int = 1_400_000_000
    hardware_partition: bool = False
    load_validated: bool = False

    def materialize_environment(self, base: Mapping[str, str] | None = None) -> dict[str, str]:
        """Executor must call this; inherited values are never stored in the plan."""
        return _environment(base, self.environment_unset_names,
                            self.environment_unset_prefixes, self.environment_overrides)


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def _environment(base, names, prefixes, overrides) -> dict[str, str]:
    env = dict(os.environ if base is None else base)
    for key in list(env):
        if key.startswith(prefixes) or key in names:
            del env[key]
    env.update(overrides)
    return env


def controlled_environment(base: Mapping[str, str] | None = None) -> dict[str, str]:
    return _environment(base, ENV_UNSET_NAMES, ENV_UNSET_PREFIXES, ENV_OVERRIDES)


def _validate_device(device: BackendDevice):
    if (not isinstance(device, BackendDevice)
            or type(device.total_bytes) is not int or type(device.free_bytes) is not int
            or device.total_bytes <= 0 or not 0 <= device.free_bytes <= device.total_bytes):
        raise BackendError("Invalid backend device memory")
    if not isinstance(device.name, str) or not isinstance(device.description, str):
        raise BackendError("Invalid backend device identity")


def _select_devices(devices) -> tuple[BackendDevice, BackendDevice]:
    for device in devices:
        _validate_device(device)
    rtx = [d for d in devices if d.name == "CUDA0" and "RTX 5080" in d.description]
    rpc = [d for d in devices if re.fullmatch(r"RPC\d+", d.name)
           and d.description == RPC_ENDPOINT]
    if len(rtx) != 1 or len(rpc) != 1:
        raise BackendError("The pinned RTX 5080 and one RPC endpoint must be enumerated")
    # RPC enumeration proves connectivity only, not remote GPU identity or use.
    return rtx[0], rpc[0]


def parse_devices(output: str) -> tuple[BackendDevice, BackendDevice]:
    devices = []
    seen = set()
    listing = False
    for line in output.splitlines():
        if line.strip() == "Available devices:":
            if listing:
                raise BackendError("Duplicate backend device listing")
            listing = True
            continue
        if not listing:
            continue
        match = DEVICE_ROW.fullmatch(line)
        if not match:
            if DEVICE_START.match(line):
                raise BackendError("Malformed backend device listing")
            continue
        name, description, total, free = match.groups()
        if name in seen:
            raise BackendError("Duplicate backend device identity")
        seen.add(name)
        total, free = int(total) * MIB, int(free) * MIB
        devices.append(BackendDevice(name, description, total, free))
    return _select_devices(devices)


def _invoke(argv, env, runner: Callable, *, stdout_only=False):
    result = runner(argv, env=env, stdin=subprocess.DEVNULL,
                    capture_output=True, text=True, timeout=20, check=False)
    if result.returncode:
        # No inherited environment, argv or credential contents are included.
        raise BackendError("Backend capability probe exited " + str(result.returncode))
    output = result.stdout + "\n" + result.stderr
    if len(output) > 512 * 1024:
        raise BackendError("Backend capability output exceeded the diagnostic limit")
    return result.stdout if stdout_only else output


def probe(executable: str, runner=subprocess.run, *, clock_ns=time.monotonic_ns) -> BackendProof:
    """Read capabilities; timestamp the start of enumeration on the host clock."""
    path = Path(executable).resolve(strict=True)
    if not path.is_file() or not os.access(path, os.X_OK):
        raise BackendError("Backend executable is unavailable")
    before = digest(path)
    env = controlled_environment()
    version = _invoke([str(path), "--version"], env, runner).strip()
    help_text = _invoke([str(path), "--help"], env, runner)
    flags = frozenset(re.findall(r"(?<![\w-])--[a-z][a-z0-9-]*", help_text))
    missing = REQUIRED_FLAGS - flags
    if missing:
        raise BackendError("Installed backend lacks required options: " + ", ".join(sorted(missing)))
    per_device_fit_targets = FIT_LIST_SIGNATURE.search(help_text) is not None
    if not per_device_fit_targets:
        raise BackendError("Installed backend lacks per-device --fit-target list support")
    observed_at_ns = clock_ns()
    if type(observed_at_ns) is not int or observed_at_ns < 0:
        raise BackendError("Invalid backend observation timestamp")
    devices = parse_devices(_invoke(
        [str(path), "--rpc", RPC_ENDPOINT, "--list-devices"], env, runner, stdout_only=True))
    if digest(path) != before:
        raise BackendError("Backend changed during capability inspection")
    return BackendProof(str(path), before, version[:4096], flags, devices,
                        observed_at_ns, per_device_fit_targets,
                        dspark_speculation=bool(re.search(r"(?<![\w-])draft-dspark(?![\w-])", help_text)))


def _private_key_file(path: Path):
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid()
            or info.st_mode & 0o077 or not 1 <= info.st_size <= 4096):
        raise BackendError("The API key must be an owned private regular file")


def _fresh(observed_at_ns: int, now_ns: int, max_age_ns: int):
    if (any(type(value) is not int or value < 0
            for value in (observed_at_ns, now_ns, max_age_ns))
            or max_age_ns == 0 or not 0 <= now_ns - observed_at_ns <= max_age_ns):
        raise BackendError("Backend or guest evidence has invalid, future or stale timestamps")


def _validate_guest(guest: GuestIdentity, rpc: BackendDevice, now_ns: int, max_age_ns: int):
    if not isinstance(guest, GuestIdentity):
        raise BackendError("Separate pinned guest identity evidence is required")
    _fresh(guest.observed_at_ns, now_ns, max_age_ns)
    if (guest.endpoint != RPC_ENDPOINT or guest.rpc_device_name != rpc.name
            or not isinstance(guest.gpu_uuid, str) or not GPU_UUID.fullmatch(guest.gpu_uuid)
            or guest.gpu_uuid.lower() == RTX_UUID.lower()
            or not isinstance(guest.gpu_name, str)
            or not re.search(r"\bV100\b", guest.gpu_name, re.IGNORECASE)
            or not re.search(r"\b16\s*GB\b", guest.gpu_name, re.IGNORECASE)
            or not isinstance(guest.owned_rpc_server_identity, str)
            or not guest.owned_rpc_server_identity.strip()):
        raise BackendError("Guest evidence does not identify the owned V100 RPC endpoint")


def _gguf_candidate(source: Path):
    if not source.is_file() or source.suffix.lower() != ".gguf":
        raise BackendError("A regular GGUF candidate is required")
    with source.open("rb") as handle:
        header = handle.read(24)
        file_size = os.fstat(handle.fileno()).st_size
    if len(header) != 24:
        raise BackendError("Truncated GGUF header")
    magic, version, tensors, metadata = struct.unpack("<4sIQQ", header)
    if magic != b"GGUF" or version not in (2, 3):
        raise BackendError("Unsupported GGUF magic, version or byte order")
    # Deployment bounds, not GGUF format maxima. No architecture is parsed.
    if not (1 <= tensors <= 1_000_000 and 1 <= metadata <= 1_000_000):
        raise BackendError("Unreasonable GGUF candidate counts")
    if file_size < 24 + 24 * tensors + 14 * metadata:
        raise BackendError("GGUF candidate is too short for its declared counts")


def parallel_configuration(context, parallel=1, kv_type="f16", cpu_moe_layers=0):
    # Context is per slot; llama.cpp --ctx-size is the sum of all slots.
    if (type(context) is not int or not 512 <= context <= 1048576
            or type(parallel) is not int or not 1 <= parallel <= 64
            or context * parallel > 1048576):
        raise BackendError("Invalid per-slot context, slot count or total context budget")
    if kv_type not in ("f16", "q8_0", "q4_0"):
        raise BackendError("Unsupported KV type")
    if type(cpu_moe_layers) is not int or not 0 <= cpu_moe_layers <= 48:
        raise BackendError("Static CPU expert layer count must be 0–48")
    return context * parallel


def command_plan(proof: BackendProof, *, guest_identity: GuestIdentity,
                 now_ns: int, max_age_ns: int, model: str, alias: str,
                 key_file: str, context: int, port: int,
                 free_targets_bytes: tuple[int, int], threads: int = 4,
                 split_weights: tuple[int, int] | None = None,
                 draft: DraftConfig | None = None, parallel: int = 1,
                 kv_type: str = "f16", cpu_moe_layers: int = 0) -> CommandPlan:
    """Compile a candidate; caller must revalidate admissions before execution.

    Targets mean additional unused free bytes at the cold-load snapshot. The
    memory planner computes them after accounting for existing display usage.
    llama.cpp's whole-MiB fit option is rounded upward, with the difference
    reported. An explicit positive split can constrain layer placement.
    All timestamps must share the host acquisition clock. Revalidate at launch.
    """
    if proof.cuda_uuid != RTX_UUID or proof.rpc_endpoint != RPC_ENDPOINT:
        raise BackendError("Backend proof belongs to different physical endpoints")
    if REQUIRED_FLAGS - proof.flags:
        raise BackendError("Backend proof does not include all required options")
    if proof.per_device_fit_targets is not True:
        raise BackendError("Backend proof lacks per-device fitting support")
    if split_weights is not None:
        if (not isinstance(split_weights, tuple) or len(split_weights) != 2
                or any(type(n) is not int or not 1 <= n <= 10000 for n in split_weights)
                or "--tensor-split" not in proof.flags):
            raise BackendError("Explicit split requires two positive bounded weights and --tensor-split support")
    total_context = parallel_configuration(context, parallel, kv_type, cpu_moe_layers)
    memory_args = ()
    if kv_type != "f16":
        if {"--cache-type-k", "--cache-type-v", "--flash-attn"} - proof.flags:
            raise BackendError("Quantised KV options are not supported by the inspected backend")
        memory_args += ("--cache-type-k", kv_type, "--cache-type-v", kv_type, "--flash-attn", "on")
    if cpu_moe_layers:
        if "--n-cpu-moe" not in proof.flags:
            raise BackendError("Static CPU expert offload is not supported by the inspected backend")
        memory_args += ("--n-cpu-moe", str(cpu_moe_layers))
    draft_args = ()
    if draft is not None:
        required = {"--spec-type", "--model-draft", "--spec-draft-n-max", "--spec-draft-n-ubatch"}
        if not isinstance(draft, DraftConfig) or not proof.dspark_speculation or required - proof.flags:
            raise BackendError("Installed backend does not prove DSpark option support")
        source_draft = Path(draft.path).resolve(strict=True)
        if (type(draft.expected_bytes) is not int or draft.expected_bytes <= 0
                or not re.fullmatch(r"[0-9a-f]{64}", draft.sha256)
                or type(draft.n_max) is not int or not 1 <= draft.n_max <= 8
                or source_draft.stat().st_size != draft.expected_bytes
                or digest(source_draft) != draft.sha256):
            raise BackendError("DSpark sidecar identity or bounded draft length differs")
        _gguf_candidate(source_draft)
        draft_args = ("--spec-type", "draft-dspark", "--model-draft", str(source_draft),
                      "--spec-draft-n-max", str(draft.n_max), "--spec-draft-n-ubatch", "128")
    _fresh(proof.observed_at_ns, now_ns, max_age_ns)
    if not isinstance(proof.devices, tuple) or len(proof.devices) != 2:
        raise BackendError("Exactly two backend devices are required")
    rtx, rpc = _select_devices(proof.devices)
    if proof.devices != (rtx, rpc):
        raise BackendError("Backend devices are not ordered RTX then RPC")
    _validate_guest(guest_identity, rpc, now_ns, max_age_ns)
    if digest(Path(proof.executable)) != proof.sha256:
        raise BackendError("Backend no longer matches the inspected executable")
    source = Path(model).resolve(strict=True)
    _gguf_candidate(source)
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,199}", alias):
        raise BackendError("Invalid model alias")
    if (type(context) is not int or not 512 <= context <= 1_048_576
            or type(port) is not int or not 1024 <= port <= 65535
            or type(threads) is not int or not 1 <= threads <= 16):
        raise BackendError("Invalid bounded server configuration")
    key = Path(key_file).absolute()
    _private_key_file(key)
    if (not isinstance(free_targets_bytes, tuple) or len(free_targets_bytes) != 2
            or any(type(x) is not int or x < 0 for x in free_targets_bytes)):
        raise BackendError("Exactly two byte-denominated fitting targets are required")
    if any(target > device.free_bytes for target, device in zip(free_targets_bytes, proof.devices)):
        raise BackendError("Reserved fitting headroom exceeds captured free memory")
    targets = tuple((n + MIB - 1) // MIB for n in free_targets_bytes)
    if any(target * MIB > device.free_bytes for target, device in zip(targets, proof.devices)):
        raise BackendError("Rounded fitting headroom exceeds captured free memory")
    rounded = tuple(mib * MIB - exact for mib, exact in zip(targets, free_targets_bytes))
    argv = (proof.executable, "--model", str(source), "--alias", alias,
            "--rpc", RPC_ENDPOINT, "--device", rtx.name + "," + rpc.name,
            "--split-mode", "layer", "--fit", "on",
            "--fit-target", ",".join(map(str, targets)),
            "--ctx-size", str(total_context), "--parallel", str(parallel),
            "--ubatch-size", "128", "--log-verbosity", "4",
            "--threads", str(threads), "--host", "127.0.0.1",
            "--port", str(port), "--api-key-file", str(key), "--jinja", "--no-webui")
    if split_weights is not None:
        argv += ("--tensor-split", ",".join(map(str, split_weights)))
    argv += memory_args + draft_args
    return CommandPlan(argv, proof.sha256, (rtx.name, rpc.name), free_targets_bytes,
                       targets, rounded, guest_identity,
                       min(proof.observed_at_ns, guest_identity.observed_at_ns), now_ns)


def refresh_fitting_targets(plan: CommandPlan, *, free_targets_bytes: tuple[int, int],
                            available_free_bytes: tuple[int, int], now_ns: int) -> CommandPlan:
    """Refresh only fitting margins from the final claimed-device snapshot.

    Desktop use may move between capability discovery and physical admission.
    The caller derives these exact targets from budget_pool while holding both
    device claims. No executable, model, device, split, draft, or policy setting
    can change through this narrow operation.
    """
    if (not isinstance(plan, CommandPlan) or plan.display_reserve_policy_bytes != 1_400_000_000
            or type(now_ns) is not int or now_ns < plan.evaluated_at_ns
            or plan.argv.count("--fit-target") != 1):
        raise BackendError("Only an unchanged bounded pool plan can refresh its fitting targets")
    for values in (free_targets_bytes, available_free_bytes):
        if (not isinstance(values, tuple) or len(values) != 2
                or any(type(n) is not int or not 0 <= n < 1 << 63 for n in values)):
            raise BackendError("Fresh fitting targets and available memory require two byte counts")
    old_targets = tuple((n + MIB - 1) // MIB for n in plan.exact_free_targets_bytes)
    old_rounding = tuple(mib * MIB - n for mib, n in zip(old_targets, plan.exact_free_targets_bytes))
    position = plan.argv.index("--fit-target") + 1
    if (position >= len(plan.argv) or plan.fitting_targets_mib != old_targets
            or plan.fitting_rounding_bytes != old_rounding
            or plan.argv[position] != ",".join(map(str, old_targets))):
        raise BackendError("Existing fitting command and exact accounting disagree")
    if free_targets_bytes[0] > 1_400_000_000 or free_targets_bytes[1] != 0:
        raise BackendError("Fresh fitting targets differ from the exact pool reserve policy")
    targets = tuple((n + MIB - 1) // MIB for n in free_targets_bytes)
    if any(mib * MIB > available for mib, available in zip(targets, available_free_bytes)):
        raise BackendError("Fresh rounded fitting headroom exceeds available physical memory")
    argv = plan.argv[:position] + (",".join(map(str, targets)),) + plan.argv[position + 1:]
    return replace(plan, argv=argv, exact_free_targets_bytes=free_targets_bytes,
                   fitting_targets_mib=targets,
                   fitting_rounding_bytes=tuple(mib * MIB - n for mib, n in zip(targets, free_targets_bytes)),
                   evaluated_at_ns=now_ns)
