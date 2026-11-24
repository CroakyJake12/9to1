"""Offline v8 native CPU LoRA provenance capture; never run a training step."""

import argparse
from dataclasses import asdict
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys
import sysconfig

from .artifacts import CustodyError, _open_regular, checked_path, relative_parts
from .v8_lora_init import _PEFT, _PEFT_LAYER_SHA, _PEFT_MODEL_SHA, _TARGETS, _raw, _receipt, V8LoraTarget
from .v8_sampler import V8_SOURCE_SHA256

_PAGED_SHA = "2c1a0b615f935a8da7af9669cc99d7a7bc1e8833a5613e63e512f1bb870f2d21"
_SHA = re.compile(r"[0-9a-f]{64}")
_GIB = 1 << 30
_CANONICAL_DOCS = {".gitattributes", "LICENSE"}


def _identity(stat):
    return stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns


def _pinned_small(path, sha, limit=1024 * 1024):
    if type(sha) is not str or not _SHA.fullmatch(sha):
        raise CustodyError("A trusted SHA256 pin is required.")
    with _open_regular(path) as source:
        value = source.read(limit + 1)
    if len(value) > limit or hashlib.sha256(value).hexdigest() != sha:
        raise CustodyError("Source or immutable manifest does not match its pin.")
    return value


def _model_files(root):
    checked_path(root, directory=True)
    files = {path.name for path in root.iterdir()}
    if ".cache" in files:
        # Exact installed HF cache entry. Verify the directory itself, never
        # walk or trust its content as model inputs.
        cache = checked_path(root / ".cache", directory=True)
        if cache.st_uid != os.geteuid():
            raise CustodyError("The installed model cache directory must belong to the operator.")
        files.remove(".cache")
    return files


def _verify_model(root, pins_path, pins_sha):
    """Exact flat local file set. No links, remote code, or byte transformations."""
    checked_path(root, directory=True)
    spec = json.loads(_pinned_small(pins_path, pins_sha))
    files = spec.get("files") if type(spec) is dict else None
    if (type(files) is not dict or not 1 <= len(files) <= 128
            or any((name not in _CANONICAL_DOCS and len(relative_parts(name)) != 1)
                   or type(sha) is not str or not _SHA.fullmatch(sha)
                   for name, sha in files.items())):
        raise CustodyError("Invalid trusted local v8 model file manifest.")
    if _model_files(root) != set(files):
        raise CustodyError("The local v8 model directory is not the exact pinned file set.")
    if not {"config.json", "tokenizer_config.json", "model.safetensors.index.json"} <= files.keys():
        raise CustodyError("The model manifest must include config, tokenizer and Safetensors index.")
    observed = {}
    total = 0
    for name, sha in files.items():
        if name not in _CANONICAL_DOCS and not name.endswith(
                (".json", ".safetensors", ".txt", ".model", ".tiktoken", ".jinja", ".md")):
            raise CustodyError("Unexpected executable or unreviewed model file type.")
        with _open_regular(root / name) as source:
            before = _identity(os.fstat(source.fileno()))
            total += before[2]
            if total > 128 * _GIB:
                raise CustodyError("The pinned model exceeds the reference byte budget.")
            digest = hashlib.file_digest(source, "sha256").hexdigest()
            if digest != sha or _identity(os.fstat(source.fileno())) != before:
                raise CustodyError("A pinned model file changed or failed verification.")
            observed[name] = before
    return observed


def _model_unchanged(root, observed):
    if _model_files(root) != observed.keys():
        raise CustodyError("The pinned model directory changed during native loading.")
    for name, expected in observed.items():
        if _identity(checked_path(root / name, directory=False)) != expected:
            raise CustodyError("A pinned model file changed during native loading.")


def _headroom(min_gib, *, proc=Path("/proc"), cgroup=Path("/sys/fs/cgroup")):
    """Admission for a deliberately isolated Linux v2 cgroup, with no swap."""
    if type(min_gib) is not int or not 64 <= min_gib <= 88:
        raise CustodyError("Choose an explicit 64–88 GiB free host memory requirement.")
    rows = (proc / "self/cgroup").read_text().splitlines()
    group = [row[3:] for row in rows if row.startswith("0::/")]
    if len(group) != 1 or ".." in Path(group[0]).parts:
        raise CustodyError("A Linux unified cgroup v2 reference process is required.")
    current = cgroup / group[0].lstrip("/")
    raw_max = (current / "memory.max").read_text().strip()
    swap = (current / "memory.swap.max").read_text().strip()
    if not raw_max.isdecimal() or not 0 < int(raw_max) <= 88 * _GIB or swap != "0":
        raise CustodyError("Use a dedicated finite memory.max (at most 88 GiB) and memory.swap.max=0 cgroup.")
    available = next(line for line in (proc / "meminfo").read_text().splitlines() if line.startswith("MemAvailable:"))
    tokens = available.split()
    required = min_gib * _GIB
    if (len(tokens) != 3 or tokens[2] != "kB" or not tokens[1].isdecimal()
            or int(tokens[1]) * 1024 < required
            or int(raw_max) - int((current / "memory.current").read_text()) < required):
        raise CustodyError("Host or cgroup does not have the required free memory for native reference.")
    return {"min_headroom_bytes": required, "memory_max_bytes": int(raw_max)}


def _pin_installed_peft_source(purelib, filename, sha):
    """Accept aliases only for these two fixed, owner-owned installed sources."""
    if (filename, sha) not in (("layer.py", _PEFT_LAYER_SHA), ("model.py", _PEFT_MODEL_SHA)):
        raise CustodyError("The installed-runtime source is not an allowlisted PEFT module.")
    path = purelib / "peft" / "tuners" / "lora" / filename
    checked_path(path.parent, directory=True)

    def trusted(info):
        return (stat.S_ISREG(info.st_mode) and info.st_uid == os.geteuid()
                and info.st_nlink >= 1 and not info.st_mode & 0o022
                and 0 < info.st_size <= 1024 * 1024)

    inspected = path.lstat()
    if not trusted(inspected):
        raise CustodyError("The pinned installed PEFT source must be owner-owned and regular.")
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
    with os.fdopen(fd, "rb") as source:
        opened = os.fstat(source.fileno())
        if not trusted(opened) or _identity(inspected) != _identity(opened):
            raise CustodyError("The pinned installed PEFT source changed while opening.")
        data = source.read(1024 * 1024 + 1)
        if (len(data) != opened.st_size or hashlib.sha256(data).hexdigest() != sha
                or _identity(os.fstat(source.fileno())) != _identity(opened)
                or _identity(path.lstat()) != _identity(opened)):
            raise CustodyError("The pinned installed PEFT source changed or failed verification.")
    return path


def _admit_runtime(opts):
    if os.environ.get("CUDA_VISIBLE_DEVICES") != "-1":
        raise CustodyError("Reference capture requires CUDA_VISIBLE_DEVICES=-1 before imports.")
    os.environ["HF_HUB_OFFLINE"] = os.environ["TRANSFORMERS_OFFLINE"] = "1"
    # Avoid reading a pre-existing source-side .pyc. No cache is written.
    cache = opts.output.parent / ".reference-empty-pycache"
    if cache.exists() or cache.is_symlink():
        raise CustodyError("The isolated reference bytecode cache path must be unused.")
    sys.pycache_prefix, sys.dont_write_bytecode = str(cache), True
    purelib = Path(sysconfig.get_path("purelib"))
    source_entries = (("layer.py", _PEFT_LAYER_SHA), ("model.py", _PEFT_MODEL_SHA))
    expected = tuple(_pin_installed_peft_source(purelib, name, sha) for name, sha in source_entries)
    peft_spec = importlib.util.find_spec("peft")
    if ("peft" in sys.modules or peft_spec is None
            or peft_spec.origin != str(purelib / "peft" / "__init__.py")):
        raise CustodyError("PEFT does not resolve to the exact installed native environment.")
    import torch

    attempted_cuda = []

    def refuse_cuda():
        attempted_cuda.append(True)
        raise CustodyError("The CPU reference must never initialize CUDA.")

    torch._C._cuda_init = refuse_cuda
    import peft
    import transformers
    from peft.tuners.lora import layer, model

    if (torch.cuda.is_initialized() or attempted_cuda or str(torch.__version__) != opts.torch_version
            or transformers.__version__ != "5.20.1" or peft.__version__ != _PEFT
            or tuple(Path(mod.__file__) for mod in (layer, model)) != expected):
        raise CustodyError("Reference runtime differs from the reviewed CPU-only v8 runtime.")
    for name, sha in source_entries:
        _pin_installed_peft_source(purelib, name, sha)
    return torch, transformers, peft, layer, attempted_cuda


def _capture(opts, paged_source, model_identity, runtime=None):
    torch, transformers, peft, layer, attempted_cuda = runtime or _admit_runtime(opts)
    torch.manual_seed(opts.seed)
    tokenizer = transformers.AutoTokenizer.from_pretrained(opts.model_root, local_files_only=True, trust_remote_code=False)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token
    # Import the exact deployed pager after tokenizer, as original v8 does.
    module = {"__name__": "dulche_pinned_paged_reference"}
    exec(compile(paged_source, "<verified v8 paged_training.py>", "exec"), module)
    torch.set_num_threads(min(3, os.cpu_count() or 1))
    config = transformers.AutoConfig.from_pretrained(opts.model_root, local_files_only=True, trust_remote_code=False)
    if config.architectures != ["Qwen3_5ForConditionalGeneration"]:
        raise CustodyError("The pinned model is not the exact v8 native architecture.")
    native, loading = transformers.Qwen3_5ForConditionalGeneration.from_pretrained(
        opts.model_root, dtype=torch.bfloat16, device_map={"": "cpu"}, local_files_only=True,
        trust_remote_code=False, low_cpu_mem_usage=True, output_loading_info=True)
    if loading.get("missing_keys") or loading.get("mismatched_keys") or loading.get("error_msgs"):
        raise CustodyError("Native reference did not load a complete original checkpoint.")
    _model_unchanged(opts.model_root, model_identity)
    if hasattr(native.model, "visual"):
        native.model.visual = torch.nn.Identity()

    class NoCompute:
        def wait_for_compute(self):
            raise CustodyError("CPU reference cannot acquire a compute lease.")

    native, _, layer_count = module["prepare"](native, NoCompute(), torch.device("cuda:0"), "upper")
    if layer_count != 64:
        raise CustodyError("The reference model does not have the original 64 text layers.")
    native.config.use_cache = False
    if opts.grad_checkpointing:
        native.gradient_checkpointing_enable()
        native.enable_input_require_grads()
    targets = tuple(V8LoraTarget(name, child.in_features, child.out_features)
                    for name, child in native.named_modules()
                    if type(child) is module["PagedLinear"] and name.rsplit(".", 1)[-1] in _TARGETS)
    pre = _raw(torch, torch.get_rng_state())
    lora = peft.LoraConfig(task_type=peft.TaskType.CAUSAL_LM, r=16, lora_alpha=32, lora_dropout=.05,
                           target_modules=list(_TARGETS))
    lora._register_custom_module({module["PagedLinear"]: layer.Linear})
    native = peft.get_peft_model(native, lora)
    post = _raw(torch, torch.get_rng_state())
    adapters = {name.removeprefix("base_model.model."): tensor.detach()
                for name, tensor in native.named_parameters() if ".lora_" in name}
    expected = tuple(f"{row.path}.lora_{component}.default.weight" for row in targets for component in ("A", "B"))
    if (not targets or tuple(adapters) != expected or any(value.dtype != torch.float32 or value.device.type != "cpu"
                                                        for value in adapters.values())
            or attempted_cuda or torch.cuda.is_initialized()):
        raise CustodyError("Native v8 LoRA order, dtype, or CPU-only custody did not verify.")
    return {"schema": 1, "source_sha256": V8_SOURCE_SHA256, "pager_sha256": _PAGED_SHA,
            "model_pin_manifest_sha256": opts.model_pins_sha256, "seed": opts.seed,
            "torch_version": opts.torch_version, "peft_version": _PEFT, "targets": [asdict(row) for row in targets],
            "cpu_before_sha256": hashlib.sha256(pre).hexdigest(), "cpu_before_hex": pre.hex(),
            "cpu_after_sha256": hashlib.sha256(post).hexdigest(), "cpu_after_hex": post.hex(),
            "adapter_sha256": _receipt(torch, adapters), "grad_checkpointing": opts.grad_checkpointing}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source-root", "model-root", "model-pins", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--model-pins-sha256", required=True)
    parser.add_argument("--torch-version", required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--min-headroom-gib", type=int, required=True)
    checkpointing = parser.add_mutually_exclusive_group(required=True)
    checkpointing.add_argument("--grad-checkpointing", dest="grad_checkpointing", action="store_true")
    checkpointing.add_argument("--no-grad-checkpointing", dest="grad_checkpointing", action="store_false")
    opts = parser.parse_args(argv)
    if os.environ.get("CUDA_VISIBLE_DEVICES") != "-1":
        raise CustodyError("Reference capture requires CUDA_VISIBLE_DEVICES=-1.")
    if not -(2**63) <= opts.seed < 2**64:
        raise CustodyError("V8 seed is outside the native torch seed range.")
    checked_path(opts.output.parent, directory=True)
    if opts.output.exists() or opts.output.is_symlink() or opts.output.is_relative_to(opts.model_root):
        raise CustodyError("Reference output must be a new private file outside the model directory.")
    admission = _headroom(opts.min_headroom_gib)
    _pinned_small(opts.source_root / "train.py", V8_SOURCE_SHA256)
    pager = _pinned_small(opts.source_root / "paged_training.py", _PAGED_SHA)
    runtime = _admit_runtime(opts)  # critical prerequisite before any full-shard I/O
    original = _verify_model(opts.model_root, opts.model_pins, opts.model_pins_sha256)
    _headroom(opts.min_headroom_gib)
    receipt = _capture(opts, pager, original, runtime)
    receipt["headroom"] = admission
    fd = os.open(opts.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0), 0o600)
    with os.fdopen(fd, "w", encoding="ascii") as out:
        json.dump(receipt, out, sort_keys=True, allow_nan=False)
        out.write("\n")
        out.flush()
        os.fsync(out.fileno())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
