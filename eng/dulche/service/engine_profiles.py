"""Discover installed engine artifacts. Only saved Strata profiles may describe executables."""

from __future__ import annotations

import json
import os
from pathlib import Path
import re
import tempfile

PRIMARY = "qwen3.8-flash-next-unsloth-ud-iq4_xs"
V100 = "qwen3.8-27b-v100"
CPU = "qwen3.8-27b-cpu"
GPU_RTX = "RTX 5080"
GPU_V100 = "Tesla V100"
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
    profile = state_dir / "strata-owned.json"
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
