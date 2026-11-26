"""Discover installed engine artifacts. Only saved Strata profiles may describe executables."""

from __future__ import annotations

import json
import os
from pathlib import Path
import re
import tempfile
import secrets
import hashlib

PRIMARY = "qwen3.8-flash-next-unsloth-ud-iq4_xs"
INKLING = "inkling-2-mini-strata-sq-10"
INKLING_POOL = "inkling-2-mini-pooled-5080-v100"
V100 = "qwen3.8-27b-v100"
CPU = "qwen3.8-27b-cpu"
GPU_RTX = "RTX 5080"
GPU_V100 = "Tesla V100"
GPU_POOL = "RTX 5080 + Tesla V100"
CONTROL_MOUNT = re.compile(r"^/tmp/\.mount_Strata[^/]+/usr/lib/Strata/control(?=/|$)")


class EngineError(RuntimeError):
    """An actionable runtime refusal, safe to display to a local client."""


class EngineBusy(EngineError):
    """Temporary resource contention. The requester may retry after a cooldown."""


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix="." + path.name + "-")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(value, handle, indent=2)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    finally:
        Path(tmp).unlink(missing_ok=True)


def load_json(path):
    return json.loads(Path(path).read_text())


def native_config_identity(config):
    """Identify the exact source choices, excluding ephemeral log/authentication fields."""
    value = {k: v for k, v in config.items() if k not in ("log", "api_key")}
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                     allow_nan=False).encode()).hexdigest()


def native_tuning_path(state_dir, model_id):
    key = hashlib.sha256(model_id.encode()).hexdigest()[:32]
    return Path(state_dir) / ("native-tuning-" + key + ".json")


def validate_native_tuning(config, settings):
    """Only exact-answer performance choices may overlay a protected source profile.

    Model files, adapter, context, KV precision, reserve floors, device placement,
    drafting and existing environment guards are deliberately outside this contract.
    """
    if not isinstance(settings, dict) or set(settings) - {"args", "env", "per_request"}:
        raise EngineError("Invalid native optimisation settings")
    args, env = settings.get("args", {}), settings.get("env", {})
    if not isinstance(args, dict) or not isinstance(env, dict) or settings.get("per_request"):
        raise EngineError("This serving route cannot apply per-request tuning presets")
    permitted = {"--kv-resident", "--prefill", "--pool-workers", "--host-core",
                 "--trim-stage-weights", "--kv-grow"}
    if set(args) - permitted or set(env) - {"STRATA_PIN_BY_NEED"}:
        raise EngineError("Optimisation attempted to change a protected or unsupported choice")
    original = config.get("args", [])
    if not isinstance(original, list) or not all(isinstance(x, str) for x in original):
        raise EngineError("Native source arguments must be strings")
    try:
        context = int(original[original.index("--max-context") + 1])
    except (ValueError, IndexError) as exc:
        raise EngineError("Native source has no explicit context length") from exc
    for flag, value in args.items():
        if flag in config.get("user_args", []):
            raise EngineError("Optimisation conflicts with an explicit user argument: " + flag)
        if flag in ("--trim-stage-weights", "--kv-grow"):
            valid = value is True
        elif flag == "--kv-resident":
            valid = type(value) is int and 512 <= value <= context
        elif flag == "--pool-workers":
            valid = type(value) is int and 1 <= value <= min(256, os.cpu_count() or 1)
        elif flag == "--host-core":
            valid = value == "last" or type(value) is int and 0 <= value < (os.cpu_count() or 1)
        else:
            valid = (type(value) is int and 1 <= value <= context or
                     isinstance(value, str) and re.fullmatch(r"auto(?::[1-9][0-9]{0,6})?", value)
                     and (":" not in value or int(value.split(":")[1]) <= context))
        if not valid:
            raise EngineError("Invalid optimisation value for " + flag)
    for key, value in env.items():
        if value not in ("0", "1") or key in config.get("env", {}) and config["env"][key] != value:
            raise EngineError("Optimisation cannot overwrite an existing environment guard")
    return {"args": dict(args), "env": dict(env), "per_request": {}}


def apply_native_tuning(state_dir, model_id, runtime, *, source_config=None):
    """Apply a validated Dulche overlay without editing the user's saved source."""
    path = native_tuning_path(state_dir, model_id)
    if not path.exists():
        return runtime
    if path.is_symlink() or path.stat().st_uid != os.getuid() or path.stat().st_mode & 0o077:
        raise EngineError("Native optimisation receipt must be a private owned file")
    receipt = load_json(path)
    if receipt.get("state") != "active":
        return runtime
    source = source_config if source_config is not None else runtime
    if receipt.get("model") != model_id or receipt.get("base_identity") != native_config_identity(source):
        raise EngineError("The saved optimisation belongs to a different source configuration; restore it first")
    settings = validate_native_tuning(source, receipt.get("settings"))
    result = {**runtime, "args": list(runtime["args"]), "env": dict(runtime.get("env", {}))}
    boolean = {"--trim-stage-weights", "--kv-grow"}
    for flag, value in settings["args"].items():
        while flag in result["args"]:
            index = result["args"].index(flag)
            del result["args"][index:index + (1 if flag in boolean else 2)]
        result["args"].append(flag)
        if flag not in boolean:
            result["args"].append(str(value))
    result["env"].update(settings["env"])
    return result


def local_data():
    return Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share"))


def strata_home():
    return local_data() / "strata"


def find_native_profile():
    root = Path.home() / ".strata/data/native-instances"
    for path in sorted(
        root.glob("*.json"), key=lambda p: p.stat().st_mtime_ns, reverse=True
    ):
        if path.is_symlink() or path.stat().st_uid != os.getuid():
            continue
        try:
            cfg = load_json(path)
        except (OSError, ValueError):
            continue
        if (
            cfg.get("model_name") == PRIMARY
            and cfg.get("exe") == "component:strata-engine"
        ):
            return path, cfg
    raise EngineError(
        "The saved primary Strata profile is unavailable; open the installed Strata model configuration."
    )


def prepare_native(state_dir):
    source, cfg = find_native_profile()
    controls = strata_home() / "components/control"
    active = load_json(controls / "active.json")
    control = (controls / active["folder"]).resolve()
    if control.parent != controls.resolve():
        raise EngineError("Invalid installed Strata control directory")
    interpreter = control / "python/bin/python3.12"
    engine = Path(cfg["vision"]["exe"]).resolve().with_name("strata")
    engine_root = (strata_home() / "components/engine").resolve()
    if (
        engine_root not in engine.parents
        or not engine.is_file()
        or not interpreter.is_file()
    ):
        raise EngineError(
            "The saved Strata engine or its installed Python runtime is missing"
        )
    if cfg.get("gpu") != 0 or cfg.get("host") != "127.0.0.1" or not cfg.get("api_key"):
        raise EngineError(
            "The saved preferred profile must identify its loopback host, GPU and authentication"
        )

    # Resolve the paths formerly inside the desktop AppImage to identical installed control assets.
    # All generation, vision, reserve, KV, context, expert, and drafting choices remain in the saved profile.
    def relocate(item):
        if isinstance(item, dict):
            return {k: relocate(v) for k, v in item.items()}
        if isinstance(item, list):
            return [relocate(v) for v in item]
        return CONTROL_MOUNT.sub(str(control), item) if isinstance(item, str) else item

    runtime = relocate(cfg)
    runtime["exe"] = str(engine)
    runtime["lib_dirs"] = list(
        dict.fromkeys([str(engine.parent), *runtime.get("lib_dirs", [])])
    )
    runtime["log"] = str(state_dir / "strata-runtime.log")
    args = runtime["args"]
    for flag in ("--pack", "--native", "--expert-profile", "--mtp"):
        if flag not in args or not Path(args[args.index(flag) + 1]).exists():
            raise EngineError("Missing local asset for the saved Strata option " + flag)
    runtime = apply_native_tuning(state_dir, PRIMARY, runtime, source_config=cfg)
    profile = state_dir / "strata-owned.json"
    atomic_json(profile, runtime)
    return source, profile, control, interpreter, runtime


def verified_sq10(path):
    import stat
    p = Path(path)
    expected = Path.home() / "Models/Inkling-2-Mini-SQ-10/Inkling-2-Mini-SQ-10.strata"
    if p != expected or p.is_symlink() or not p.is_file():
        raise EngineError("The exact SQ-10 artifact is required")
    receipt_path = p.with_suffix(p.suffix + ".verified.json")
    if receipt_path.is_symlink() or receipt_path.stat().st_uid != os.getuid() or receipt_path.stat().st_mode & 0o077:
        raise EngineError("SQ-10 requires a private verification receipt")
    receipt = load_json(receipt_path)
    st = p.lstat()
    fingerprint = dict(device=st.st_dev,inode=st.st_ino,size=st.st_size,mtime_ns=st.st_mtime_ns,ctime_ns=st.st_ctime_ns)
    if (not stat.S_ISREG(st.st_mode) or st.st_uid != os.getuid() or st.st_size != 10471526400
            or receipt.get("sha256") != "ac4d452ad03e4930abd9d46f9c5eec22b2f75a1da45a1e49bd24ccd58b065934"
            or receipt.get("fingerprint") != fingerprint or receipt.get("path") != str(p)
            or receipt.get("model_id") != INKLING):
        raise EngineError("SQ-10 verification or artifact fingerprint changed")
    return p


def find_inkling_profile(state_dir):
    """Read the explicitly installed native Inkling profile; never substitute a model."""
    pointer = Path(state_dir) / "inkling-source.json"
    if pointer.is_symlink() or not pointer.is_file() or pointer.stat().st_uid != os.getuid():
        raise EngineError("The native Inkling profile has not been registered")
    source = Path(load_json(pointer)["profile"])
    if source.is_symlink() or not source.is_file() or source.stat().st_uid != os.getuid():
        raise EngineError("Native Inkling profile must be an owned regular file")
    cfg = load_json(source)
    if cfg.get("arch") != "inkling2_loop" or cfg.get("exe") != "component:strata-inkling":
        raise EngineError("Native Inkling needs its own installed shape engine")
    args = cfg.get("args")
    if not isinstance(args, list) or not all(isinstance(x, str) for x in args):
        raise EngineError("Invalid native Inkling arguments")
    try:
        model = verified_sq10(args[args.index("--native") + 1])
        context = int(args[args.index("--max-context") + 1])
    except (ValueError, IndexError) as exc:
        raise EngineError("Native Inkling profile lacks its model/context") from exc
    if model.name != "Inkling-2-Mini-SQ-10.strata":
        raise EngineError("This temporary native benchmark adapter owns only verified SQ-10")
    parallel = cfg.get("parallel", 1)
    if not 512 <= context <= 1048576 or type(parallel) is not int or not 1 <= parallel <= 64:
        raise EngineError("Native Inkling context or slot count exceeds engine limits")
    if "--parallel" in args:
        try:
            if int(args[args.index("--parallel") + 1]) != parallel:
                raise ValueError("parallel mismatch")
        except (ValueError, IndexError) as exc:
            raise EngineError("Native Inkling profile has conflicting slot counts") from exc
    if cfg.get("gpu") != 0 or cfg.get("backend") not in (None, "cuda") or cfg.get("host", "127.0.0.1") != "127.0.0.1":
        raise EngineError("Native Inkling profile must use the configured local RTX CUDA device")
    if not Path(cfg.get("tokenizer", "")).is_dir():
        raise EngineError("Native Inkling tokenizer assets are missing")
    return source, cfg


def prepare_inkling(state_dir):
    source, cfg = find_inkling_profile(state_dir)
    controls = strata_home() / "components/control"
    control = (controls / load_json(controls / "active.json")["folder"]).resolve()
    interpreter = control / "python/bin/python3.12"
    if control.parent != controls.resolve() or not interpreter.is_file() or not (control / "serve/inkling_kind.py").is_file():
        raise EngineError("The installed Strata Control cannot serve native Inkling")
    # serve.server resolves component:strata-inkling and verifies its signed component hashes.
    # It never downloads on this startup path. Preserve the installer's measured placement.
    runtime = dict(cfg)
    runtime.update(model_name=INKLING, backend="cuda", host="127.0.0.1", port=19042, parallel=cfg.get("parallel", 1),
                   api_key=cfg.get("api_key") or secrets.token_hex(32),
                   cwd=str(control), log=str(Path(state_dir) / "inkling-runtime.log"))
    profile = Path(state_dir) / "inkling-owned.json"
    atomic_json(profile, runtime)
    return source, profile, control, interpreter, runtime


def dense_files():
    root = Path(
        os.environ.get(
            "DULCHE_LLAMA_ROOT",
            Path.home() / "Documents/Default Project/strata/build-rpc-cuda/bin",
        )
    ).resolve()
    exe = root / "llama-server"
    model = Path.home() / "Documents/Default Project/models/Qwen3.8-27B-UD-IQ4_XS.gguf"
    return exe, model


def read_signature(path):
    path = Path(path)
    if not path.is_file() or path.suffix.casefold() != ".gguf":
        raise EngineError("A regular GGUF model file is required")
    with path.open("rb") as handle:
        if handle.read(4) != b"GGUF":
            raise EngineError("The selected file does not contain a GGUF header")
    return path.resolve()
