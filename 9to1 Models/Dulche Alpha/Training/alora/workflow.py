"""Prepare pinned CPU ALORA workflows for the existing independent supervisor.

Preparation creates an exclusive, inspectable deployment; it never registers or
launches a service. A retained real-corpus activation profile is mandatory.
Large GGUF digests are external attestations, not fresh full-file verification.
"""
from __future__ import annotations

from collections import Counter
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import re

from custody.artifacts import _fsync_directory, _open_regular, checked_path, create_directory

REQUEST_SCHEMA = "alora-training-request-v1"
DEPLOYMENT_SCHEMA = "alora-training-deployment-v1"
WORKFLOW_SCHEMA = "dulche-independent-workflow-v1"
_SHA = re.compile(r"[a-f0-9]{64}\Z")
_ID = re.compile(r"[a-z0-9][a-z0-9-]{0,62}\Z")
_TOP = {"schema", "purpose", "id", "model", "python", "control_client", "dataset",
        "config", "profile", "profile_record_sha256", "shards", "settings", "limits"}
_SETTINGS = {
    "target_updates": (1, 1_000_000), "experts_per_layer": (1, 65536),
    "rank": (1, 1024), "seed": (0, (1 << 63) - 1), "threads": (1, 256),
    "chunk_rows": (1, 65536), "max_decoded_bytes": (1, 1 << 30),
    "max_read_bytes": (1, 1 << 30), "max_fingerprints": (1, 1_048_576),
    "loss_token_rows": (1, 4096),
}
_FLOATS = {"alpha", "learning_rate", "max_grad_norm"}
_LIMITS = {"ram_mib", "min_free_ram_mib", "checkpoint_reserve_bytes",
           "max_checkpoint_bytes", "wall_seconds", "progress_timeout_seconds",
           "checkpoint_grace_seconds", "startup_timeout_seconds", "vram_mib"}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()


def canonical_sha256(value):
    return hashlib.sha256(canonical(value)).hexdigest()


def _object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON keys are refused")
        result[key] = value
    return result


def read_json(path, *, sha256=None, limit=64 * 1024 * 1024):
    path = Path(path)
    with _open_regular(path) as source:
        raw = source.read(limit + 1)
    if len(raw) > limit:
        raise ValueError("JSON input exceeds its explicit byte limit")
    if sha256 is not None and (type(sha256) is not str or not _SHA.fullmatch(sha256)
                               or hashlib.sha256(raw).hexdigest() != sha256):
        raise ValueError("JSON input differs from its pinned raw-file SHA-256")
    value = json.loads(raw, object_pairs_hook=_object,
                       parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Nonfinite JSON")))
    if type(value) is not dict:
        raise ValueError("Expected a JSON object")
    return value


def _absolute(value, *, directory=False):
    if (type(value) is not str or "\0" in value or "\n" in value
            or not Path(value).is_absolute() or ".." in Path(value).parts):
        raise ValueError("Expected an absolute non-traversing path")
    path = Path(value)
    checked_path(path, directory=directory)
    return path


def artifact(path, *, limit=64 * 1024 * 1024):
    path = _absolute(str(path))
    if path.stat().st_size > limit:
        raise ValueError("Pinned small input exceeds its byte allowance")
    digest, count = hashlib.sha256(), 0
    with _open_regular(path) as source:
        while chunk := source.read(1024 * 1024):
            count += len(chunk)
            if count > limit:
                raise ValueError("Pinned input grew beyond its byte allowance")
            digest.update(chunk)
    return {"path": str(path), "sha256": digest.hexdigest()}


def verify_artifact(pin, *, limit=64 * 1024 * 1024):
    if (type(pin) is not dict or set(pin) != {"path", "sha256"}
            or type(pin["sha256"]) is not str or not _SHA.fullmatch(pin["sha256"])):
        raise ValueError("An input needs an exact path and lowercase SHA-256")
    if artifact(pin["path"], limit=limit) != pin:
        raise ValueError("Pinned input bytes changed")
    return Path(pin["path"])


def _integer(value, minimum, maximum, label):
    if type(value) is not int or not minimum <= value <= maximum:
        raise ValueError("Invalid bounded integer setting: " + label)
    return value


def _python_pins(pin):
    # Preserve the venv spelling for the shebang. The service resolves its
    # executable, so registering the venv's python symlink directly loses it.
    if type(pin) is not dict or set(pin) != {"path", "sha256"}:
        raise ValueError("A pinned virtual-environment Python executable is required")
    original = Path(pin["path"]) if type(pin.get("path")) is str else Path()
    if (not original.is_absolute() or ".." in original.parts
            or any(char.isspace() for char in str(original)) or len(str(original)) > 200):
        raise ValueError("The venv interpreter needs a bounded absolute shebang path without whitespace")
    resolved = original.resolve(strict=True)
    resolved_pin = {"path": str(resolved), "sha256": pin["sha256"]}
    verify_artifact(resolved_pin)
    if not os.access(resolved, os.X_OK):
        raise ValueError("Pinned Python is not executable")
    venv = original.parent.parent.resolve(strict=True)
    checked_path(venv / "bin", directory=True)
    if original.parent.name != "bin" or (venv / "bin" / original.name).resolve(strict=True) != resolved:
        raise ValueError("The interpreter is not inside its pinned virtual environment")
    configuration = artifact(venv / "pyvenv.cfg", limit=65536)
    return resolved_pin, configuration, str(venv)


def validate_request(request):
    """Validate actual corpus/profile inputs without loading a model or Torch."""
    if type(request) is not dict or set(request) != _TOP:
        raise ValueError("The ALORA request fields do not match the supported schema")
    if request["schema"] != REQUEST_SCHEMA or request["purpose"] != "real_training":
        raise ValueError("Independent preparation requires an explicit real_training request")
    if type(request["id"]) is not str or not _ID.fullmatch(request["id"]):
        raise ValueError("Use a bounded lowercase workflow identifier")
    if type(request["model"]) is not str or not request["model"].strip() or len(request["model"]) > 256:
        raise ValueError("A bounded explicit model name is required")
    result = copy.deepcopy(request)
    settings = result["settings"]
    if type(settings) is not dict:
        raise ValueError("Explicit training settings are required")
    settings.setdefault("loss_token_rows", 128)
    if set(settings) != set(_SETTINGS) | _FLOATS:
        raise ValueError("Training settings differ from the supported CPU recipe")
    for name, bounds in _SETTINGS.items():
        _integer(settings[name], *bounds, name)
    for name in _FLOATS:
        value = settings[name]
        if type(value) not in (float, int) or not math.isfinite(value) or value <= 0:
            raise ValueError("Optimizer and adapter settings must be finite and positive")
    if settings["loss_token_rows"] * settings["chunk_rows"] * 4 > 16 * 1024 * 1024:
        raise ValueError("The bounded loss score tile would exceed 16 MiB")
    limits = result["limits"]
    if type(limits) is not dict or set(limits) != _LIMITS:
        raise ValueError("All explicit CPU supervisor limits are required")
    for name in _LIMITS - {"vram_mib"}:
        _integer(limits[name], 1, (1 << 63) - 1, name)
    if type(limits["vram_mib"]) is not int or limits["vram_mib"] != 0:
        raise ValueError("This production entrypoint admits CPU only")
    if (limits["checkpoint_grace_seconds"] > limits["wall_seconds"]
            or limits["startup_timeout_seconds"] > limits["wall_seconds"]):
        raise ValueError("Startup/checkpoint allowances cannot exceed the job wall limit")
    if max(settings["max_decoded_bytes"], settings["max_read_bytes"]) > limits["ram_mib"] * 1024**2:
        raise ValueError("A GGUF slice budget cannot exceed the admitted process memory")
    _, _, venv = _python_pins(result["python"])
    result["python"]["path"] = str(Path(venv) / "bin" / Path(result["python"]["path"]).name)
    verify_artifact(result["control_client"], limit=1024 * 1024)
    for name in ("config", "profile"):
        verify_artifact(result[name])
    dataset_spec = result["dataset"]
    if (type(dataset_spec) is not dict or set(dataset_spec) !=
            {"manifest", "root", "vocab_size", "max_tokens"}):
        raise ValueError("Dataset inputs must bind the manifest, root, vocabulary and token bound")
    verify_artifact(dataset_spec["manifest"])
    _absolute(dataset_spec["root"], directory=True)
    _integer(dataset_spec["vocab_size"], 2, (1 << 31) - 1, "vocab_size")
    _integer(dataset_spec["max_tokens"], 2, 32768, "max_tokens")
    config = read_json(result["config"]["path"], sha256=result["config"]["sha256"])
    text = config.get("text_config", config)
    if (type(text) is not dict or text.get("vocab_size") != dataset_spec["vocab_size"]
            or type(text.get("max_position_embeddings")) is not int
            or dataset_spec["max_tokens"] > text["max_position_embeddings"]):
        raise ValueError("Dataset vocabulary/context bounds differ from the pinned model config")
    shards = result["shards"]
    if type(shards) is not list or not 1 <= len(shards) <= 64:
        raise ValueError("The exact external GGUF shard attestations are required")
    names = set()
    for pin in shards:
        if (type(pin) is not dict or set(pin) != {"path", "sha256", "size_bytes"}
                or type(pin["sha256"]) is not str or not _SHA.fullmatch(pin["sha256"])):
            raise ValueError("A GGUF shard needs its external SHA-256 and exact size")
        path = _absolute(pin["path"])
        _integer(pin["size_bytes"], 1, (1 << 63) - 1, "shard size")
        if path.name in names or path.stat().st_size != pin["size_bytes"]:
            raise ValueError("GGUF shard names/sizes differ from the pinned complete set")
        names.add(path.name)
    from .data import TrainingDataset
    from .curriculum import CATEGORY_PERCENTAGES, validate_counts
    from .profile import ActivationProfiler
    dataset = TrainingDataset(Path(dataset_spec["manifest"]["path"]), Path(dataset_spec["root"]),
                              expected_sha256=dataset_spec["manifest"]["sha256"],
                              purpose="real_training", vocab_size=dataset_spec["vocab_size"],
                              max_tokens=dataset_spec["max_tokens"])
    counts = dict.fromkeys(CATEGORY_PERCENTAGES, 0)
    counts.update(Counter(row["category"] for row in dataset.rows()))
    validate_counts(counts)
    profile = ActivationProfiler.from_record(
        read_json(result["profile"]["path"], sha256=result["profile"]["sha256"]),
        expected_sha256=result["profile_record_sha256"])
    if profile.purpose != "real_training" or profile.source_dataset != dataset.identity:
        raise ValueError("The retained profile is not bound to this exact real dataset")
    expected = {f"model.layers.{i}.mlp.experts": text.get("num_experts")
                for i in range(_integer(text.get("num_hidden_layers"), 1, 1024, "num_hidden_layers"))}
    if dict(profile.topology) != expected:
        raise ValueError("The retained activation profile differs from the full native expert topology")
    profile.select(settings["experts_per_layer"], require_all_categories=True)
    return result, dataset, profile, config, counts


def _write(path, payload, *, mode=0o444):
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(path, flags, mode), "wb") as stream:
        if stream.write(payload) != len(payload):
            raise OSError("Incomplete deployment input write")
        stream.flush()
        os.fsync(stream.fileno())
    _fsync_directory(path.parent)
    return artifact(path)


def _copy(pin, destination):
    source = verify_artifact(pin)
    with _open_regular(source) as stream:
        payload = stream.read(64 * 1024 * 1024 + 1)
    if len(payload) > 64 * 1024 * 1024 or hashlib.sha256(payload).hexdigest() != pin["sha256"]:
        raise ValueError("Deployment input changed while copying")
    copied = _write(destination, payload)
    if copied["sha256"] != pin["sha256"]:
        raise ValueError("Deployment copy differs from the admitted input")
    return copied


def _source_files(source_root):
    result = []
    for package in ("alora", "custody"):
        directory = source_root / package
        checked_path(directory, directory=True)
        for path in sorted(directory.glob("*.py")):
            if not path.name.startswith(("test_", "integration_")):
                result.append(artifact(path, limit=2 * 1024 * 1024))
    required = {source_root / p for p in
                ("alora/runner.py", "alora/workflow.py", "alora/__init__.py", "custody/__init__.py")}
    if not required <= {Path(pin["path"]) for pin in result}:
        raise ValueError("The complete production ALORA/custody sources are absent")
    return result


def _launcher_source(python, *, entrypoint="main"):
    if entrypoint not in {"main", "calibration_child_main"}:
        raise ValueError("Unknown pinned executable entrypoint")
    # The bootstrap imports only stdlib until it checks the deployment and every
    # copied execution source. -I removes user-site and environment import paths.
    source = f"#!{python} -I\n" + r'''"""Pinned independent ALORA CPU worker; no inline recipe arguments."""
import hashlib
import json
import os
from pathlib import Path
import sys

def boot():
    if len(sys.argv) != 5 or sys.argv[1] != "--spec" or sys.argv[3] != "--spec-sha256":
        raise ValueError("Use the exact registered deployment arguments")
    path = Path(sys.argv[2])
    with path.open("rb") as stream:
        raw = stream.read(4 * 1024 * 1024 + 1)
    if len(raw) > 4 * 1024 * 1024 or hashlib.sha256(raw).hexdigest() != sys.argv[4]:
        raise ValueError("Deployment bytes differ from their registered digest")
    spec = json.loads(raw)
    deployment = spec["deployment"]
    root = Path(deployment["root"])
    if Path(__file__).resolve().parent != root or path != root / "deployment.json":
        raise ValueError("The launcher and deployment directory differ")
    if Path(sys.prefix).resolve() != Path(deployment["venv_root"]):
        raise ValueError("The independently pinned virtual environment is not active")
    pins = deployment["sources"] + [deployment["python_resolved"], deployment["python_venv_config"]]
    for pin in pins:
        item = Path(pin["path"])
        if not item.is_file() or item.stat().st_size > 64 * 1024 * 1024:
            raise ValueError("Missing or oversized pinned execution input")
        with item.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != pin["sha256"]:
                raise ValueError("Pinned execution input changed before import")
    sys.dont_write_bytecode = True
    sys.path.insert(0, deployment["source_root"])
    from alora.runner import main
    return main(sys.argv[1:])

if __name__ == "__main__":
    raise SystemExit(boot())
'''
    target = ("from alora.runner import main" if entrypoint == "main"
              else "from alora.calibration_cli import child_main as main")
    return source.replace("from alora.runner import main", target)


def deployment_inputs(spec, spec_pin):
    pins = [spec_pin, spec["config"], spec["profile"], spec["dataset"]["manifest"],
            spec["control_client"], spec["deployment"]["python_resolved"],
            spec["deployment"]["python_venv_config"], *spec["deployment"]["sources"]]
    by_path = {}
    for pin in pins:
        if pin["path"] in by_path and by_path[pin["path"]] != pin:
            raise ValueError("Conflicting pins for one execution input")
        by_path[pin["path"]] = pin
    return [by_path[path] for path in sorted(by_path)]


def read_roots(spec):
    return sorted({spec["deployment"]["root"], spec["deployment"]["venv_root"],
                   spec["dataset"]["root"], *(str(Path(pin["path"]).parent) for pin in spec["shards"])})


def _emit_bundle(request, output, *, request_sha256, source_root):
    """Write already validated inputs; intentionally never call a supervisor."""
    output = Path(output)
    create_directory(output, exclusive=True)
    for relative in ("source", "source/alora", "source/custody", "inputs"):
        create_directory(output / relative, exclusive=True)
    spec = copy.deepcopy(request)
    spec["schema"] = DEPLOYMENT_SCHEMA
    resolved, venv_config, venv_root = _python_pins(spec["python"])
    sources = []
    for pin in _source_files(source_root):
        relative = Path(pin["path"]).relative_to(source_root)
        sources.append(_copy(pin, output / "source" / relative))
    spec["control_client"] = _copy(spec["control_client"], output / "source/training_control.py")
    for name in ("config", "profile"):
        spec[name] = _copy(spec[name], output / "inputs" / (name + ".json"))
    spec["dataset"]["manifest"] = _copy(spec["dataset"]["manifest"], output / "inputs/dataset.json")
    spec["request_sha256"] = request_sha256
    spec["deployment"] = {
        "root": str(output), "source_root": str(output / "source"),
        "workflow_path": str(output / "workflow.json"), "sources": sources,
        "python_resolved": resolved, "python_venv_config": venv_config, "venv_root": venv_root,
        "gguf_hash_verification": "external_attestation_plus_actual_slice_verification",
        "fresh_full_file_hash_verified": False,
    }
    spec_pin = _write(output / "deployment.json", canonical(spec) + b"\n")
    launcher = _write(output / "worker", _launcher_source(spec["python"]["path"]).encode(), mode=0o555)
    workflow = {
        "schema": WORKFLOW_SCHEMA, "purpose": request["purpose"], "id": spec["id"], "model": spec["model"],
        "worker_protocol": "checkpoint-yield-v1", "executable": launcher,
        "arguments": ["--spec", spec_pin["path"], "--spec-sha256", spec_pin["sha256"]],
        "cwd": str(output), "inputs": deployment_inputs(spec, spec_pin),
        "read_only_roots": read_roots(spec), "dataset_manifest": spec["dataset"]["manifest"],
        "dataset_root": spec["dataset"]["root"], "limits": spec["limits"], "device": "cpu",
    }
    workflow_pin = _write(output / "workflow.json", canonical(workflow) + b"\n")
    return {"workflow": workflow_pin, "deployment": spec_pin, "executable": launcher,
            "purpose": request["purpose"], "registered": False, "launched": False,
            "fresh_full_file_hash_verified": False}


def prepare_workflow(request_path, *, request_sha256, output):
    request = read_json(Path(request_path), sha256=request_sha256, limit=4 * 1024 * 1024)
    request, _, _, _, counts = validate_request(request)
    source_root = Path(__file__).resolve().parent.parent
    result = _emit_bundle(request, Path(output), request_sha256=request_sha256, source_root=source_root)
    result["category_counts"] = counts
    result["training_samples"] = sum(counts.values())
    return result


def verify_deployment(path, *, sha256):
    """Reverify copied sources and real inputs before model/runtime import."""
    path = Path(path)
    spec = read_json(path, sha256=sha256, limit=4 * 1024 * 1024)
    if set(spec) != _TOP | {"request_sha256", "deployment"} or spec.get("schema") != DEPLOYMENT_SCHEMA:
        raise ValueError("Unsupported prepared deployment")
    if type(spec["request_sha256"]) is not str or not _SHA.fullmatch(spec["request_sha256"]):
        raise ValueError("The original request identity is absent")
    deployment = spec["deployment"]
    fields = {"root", "source_root", "workflow_path", "sources", "python_resolved",
              "python_venv_config", "venv_root", "gguf_hash_verification", "fresh_full_file_hash_verified"}
    if type(deployment) is not dict or set(deployment) != fields:
        raise ValueError("Deployment execution-source metadata is incomplete")
    root = _absolute(deployment["root"], directory=True)
    if (path != root / "deployment.json" or deployment["source_root"] != str(root / "source")
            or deployment["workflow_path"] != str(root / "workflow.json")
            or Path(__file__).resolve().parent.parent != root / "source"
            or deployment["fresh_full_file_hash_verified"] is not False
            or deployment["gguf_hash_verification"] != "external_attestation_plus_actual_slice_verification"):
        raise ValueError("Deployment directory or GGUF verification claim differs")
    actual = _source_files(root / "source")
    if actual != deployment["sources"]:
        raise ValueError("The copied execution source inventory or bytes changed")
    for pin in (deployment["python_resolved"], deployment["python_venv_config"]):
        verify_artifact(pin)
    resolved, configuration, venv = _python_pins(spec["python"])
    if (resolved != deployment["python_resolved"] or configuration != deployment["python_venv_config"]
            or venv != deployment["venv_root"]):
        raise ValueError("The pinned Python environment changed")
    request = {key: copy.deepcopy(spec[key]) for key in _TOP}
    request["schema"] = REQUEST_SCHEMA
    _, dataset, profile, config, counts = validate_request(request)
    return spec, dataset, profile, config, counts


# Reviewed existing main/worktree guard implementation; an unrelated same-name
# script with a caller-supplied digest is not the approved development procedure.
APPROVED_MEMORY_GUARD_SHA256 = "2c6998d4beea8b77218556c88bcddc78b1cd71a4058d44f7bc279e4bdfe6dd64"
CALIBRATION_SCHEMA = "alora-calibration-request-v1"
CALIBRATION_DEPLOYMENT_SCHEMA = "alora-calibration-deployment-v1"
_CALIBRATION_TOP = {"schema", "purpose", "id", "model", "python", "dataset", "config",
                    "shards", "settings", "guard", "resume"}
_CALIBRATION_SETTINGS = {"seed", "threads", "chunk_rows", "max_decoded_bytes",
                         "max_read_bytes", "max_fingerprints", "samples_per_category"}


def sdk_lock_path():
    """The existing shared SDK exclusion, not a newly invented training lock."""
    return Path.home() / ".local/state/9to1-sdk.lock"


def validate_calibration_guard(guard):
    if type(guard) is not dict or set(guard) != {"script", "sdk_lock", "floor_mib", "timeout_seconds"}:
        raise ValueError("Calibration requires the pinned existing memory guard and shared SDK lock")
    script = verify_artifact(guard["script"], limit=1024 * 1024)
    if script.name != "guarded_lowmem_build.py" or guard["script"]["sha256"] != APPROVED_MEMORY_GUARD_SHA256:
        raise ValueError("Use the existing guarded_lowmem_build.py process supervisor")
    lock = _absolute(guard["sdk_lock"])
    if lock != sdk_lock_path():
        raise ValueError("Calibration must use the existing shared 9to1 SDK lock")
    _integer(guard["floor_mib"], 8192, 1_048_576, "calibration memory floor")
    _integer(guard["timeout_seconds"], 60, 86400, "calibration timeout")
    return lock


def validate_calibration_request(request, *, verify_corpus=True):
    """Check pins/settings; full corpus verification belongs inside the guard.

    The parent uses verify_corpus=False only to prepare immutable inputs. The
    child always performs the actual corpus admission before model allocation.
    """
    if type(verify_corpus) is not bool:
        raise ValueError("Corpus verification mode must be explicit")
    from .data import TrainingDataset
    from .curriculum import CATEGORY_PERCENTAGES, validate_counts
    if type(request) is not dict:
        raise ValueError("A pinned calibration request is required")
    result = copy.deepcopy(request)
    result.setdefault("resume", None)
    if (set(result) != _CALIBRATION_TOP or result["schema"] != CALIBRATION_SCHEMA
            or result["purpose"] != "real_training"):
        raise ValueError("Calibration preparation requires an explicit real_training request")
    if type(result["id"]) is not str or not _ID.fullmatch(result["id"]):
        raise ValueError("Use a bounded lowercase calibration identifier")
    if type(result["model"]) is not str or not result["model"].strip() or len(result["model"]) > 256:
        raise ValueError("A bounded explicit model name is required")
    validate_calibration_guard(result["guard"])
    _, _, venv = _python_pins(result["python"])
    result["python"]["path"] = str(Path(venv) / "bin" / Path(result["python"]["path"]).name)
    settings = result["settings"]
    if type(settings) is not dict:
        raise ValueError("Calibration needs explicit bounded CPU settings")
    settings.setdefault("samples_per_category", 4)
    if set(settings) != _CALIBRATION_SETTINGS:
        raise ValueError("Unsupported calibration settings")
    for name in _CALIBRATION_SETTINGS - {"samples_per_category"}:
        _integer(settings[name], *_SETTINGS[name], name)
    _integer(settings["samples_per_category"], 1, 64, "samples_per_category")
    verify_artifact(result["config"], limit=1024 * 1024)
    config = read_json(result["config"]["path"], sha256=result["config"]["sha256"], limit=1024 * 1024)
    text = config.get("text_config", config)
    data = result["dataset"]
    if (type(data) is not dict or set(data) != {"manifest", "root", "vocab_size", "max_tokens"}
            or type(text) is not dict):
        raise ValueError("Calibration needs the exact dataset and model context bounds")
    verify_artifact(data["manifest"])
    _absolute(data["root"], directory=True)
    _integer(data["vocab_size"], 2, (1 << 31) - 1, "vocab_size")
    _integer(data["max_tokens"], 2, 32768, "max_tokens")
    if (text.get("vocab_size") != data["vocab_size"]
            or type(text.get("max_position_embeddings")) is not int
            or text["max_position_embeddings"] < data["max_tokens"]):
        raise ValueError("Calibration tokens differ from the pinned vocabulary/context")
    shards = result["shards"]
    if type(shards) is not list or not 1 <= len(shards) <= 64:
        raise ValueError("The exact external GGUF shard attestations are required")
    names = set()
    for pin in shards:
        if (type(pin) is not dict or set(pin) != {"path", "sha256", "size_bytes"}
                or type(pin["sha256"]) is not str or not _SHA.fullmatch(pin["sha256"])):
            raise ValueError("A GGUF shard needs an external SHA-256 and exact size")
        path = _absolute(pin["path"])
        _integer(pin["size_bytes"], 1, (1 << 63) - 1, "shard size")
        if path.name in names or path.stat().st_size != pin["size_bytes"]:
            raise ValueError("GGUF shard names/sizes differ from the pinned complete set")
        names.add(path.name)
    resume = result["resume"]
    if resume is not None:
        if type(resume) is not dict or set(resume) != {"profile", "receipt"}:
            raise ValueError("Calibration continuation requires both pinned profile and receipt")
        for name in ("profile", "receipt"):
            verify_artifact(resume[name])
    if not verify_corpus:
        return result, None, config, None
    dataset = TrainingDataset(Path(data["manifest"]["path"]), Path(data["root"]),
                              expected_sha256=data["manifest"]["sha256"], purpose="real_training",
                              vocab_size=data["vocab_size"], max_tokens=data["max_tokens"])
    counts = dict.fromkeys(CATEGORY_PERCENTAGES, 0)
    counts.update(Counter(row["category"] for row in dataset.rows()))
    validate_counts(counts)
    if any(count < settings["samples_per_category"] for count in counts.values()):
        raise ValueError("The real corpus cannot supply the requested calibration category sample")
    return result, dataset, config, counts


def _emit_calibration_bundle(request, output, *, request_sha256, source_root):
    """Prepare an exclusive CPU calibration child; do not start it."""
    output = Path(output)
    create_directory(output, exclusive=True)
    for relative in ("source", "source/alora", "source/custody", "inputs"):
        create_directory(output / relative, exclusive=True)
    spec = copy.deepcopy(request)
    spec["schema"] = CALIBRATION_DEPLOYMENT_SCHEMA
    spec.setdefault("resume", None)
    resolved, venv_config, venv = _python_pins(spec["python"])
    sources = []
    for pin in _source_files(source_root):
        sources.append(_copy(pin, output / "source" / Path(pin["path"]).relative_to(source_root)))
    spec["config"] = _copy(spec["config"], output / "inputs/config.json")
    spec["dataset"]["manifest"] = _copy(spec["dataset"]["manifest"], output / "inputs/dataset.json")
    # The immutable receipt binds its original profile/receipt locators.
    # Recheck those pins without relocating or rewriting retained evidence.
    if spec["resume"] is not None:
        for name in ("profile", "receipt"):
            verify_artifact(spec["resume"][name])
    spec["request_sha256"] = request_sha256
    spec["deployment"] = {
        "root": str(output), "source_root": str(output / "source"), "sources": sources,
        "python_resolved": resolved, "python_venv_config": venv_config, "venv_root": venv,
        "guard_log_path": str(output / "guard.log"), "profile_path": str(output / "profile.json"),
        "gguf_hash_verification": "external_attestation_plus_actual_slice_verification",
        "fresh_full_file_hash_verified": False,
    }
    spec_pin = _write(output / "deployment.json", canonical(spec) + b"\n")
    launcher = _write(output / "worker",
                      _launcher_source(spec["python"]["path"], entrypoint="calibration_child_main").encode(),
                      mode=0o555)
    return spec, spec_pin, launcher


def verify_calibration_deployment(path, *, sha256):
    path = Path(path)
    spec = read_json(path, sha256=sha256, limit=4 * 1024 * 1024)
    if (set(spec) != _CALIBRATION_TOP | {"request_sha256", "deployment"}
            or spec.get("schema") != CALIBRATION_DEPLOYMENT_SCHEMA
            or type(spec["request_sha256"]) is not str or not _SHA.fullmatch(spec["request_sha256"])):
        raise ValueError("Unsupported prepared calibration deployment")
    deployment = spec["deployment"]
    fields = {"root", "source_root", "sources", "python_resolved", "python_venv_config", "venv_root",
              "guard_log_path", "profile_path", "gguf_hash_verification", "fresh_full_file_hash_verified"}
    if type(deployment) is not dict or set(deployment) != fields:
        raise ValueError("Calibration execution-source metadata is incomplete")
    root = _absolute(deployment["root"], directory=True)
    if (path != root / "deployment.json" or deployment["source_root"] != str(root / "source")
            or Path(__file__).resolve().parent.parent != root / "source"
            or deployment["guard_log_path"] != str(root / "guard.log")
            or deployment["profile_path"] != str(root / "profile.json")
            or deployment["fresh_full_file_hash_verified"] is not False
            or deployment["gguf_hash_verification"] != "external_attestation_plus_actual_slice_verification"):
        raise ValueError("Calibration directory or GGUF verification claim differs")
    if _source_files(root / "source") != deployment["sources"]:
        raise ValueError("The copied calibration execution code changed")
    resolved, configuration, venv = _python_pins(spec["python"])
    if (resolved != deployment["python_resolved"] or configuration != deployment["python_venv_config"]
            or venv != deployment["venv_root"]):
        raise ValueError("The pinned calibration Python environment changed")
    request = {key: copy.deepcopy(spec[key]) for key in _CALIBRATION_TOP}
    request["schema"] = CALIBRATION_SCHEMA
    _, dataset, config, counts = validate_calibration_request(request)
    return spec, dataset, config, counts


def calibration_guard_command(spec, spec_pin):
    guard = spec["guard"]
    return [
        spec["python"]["path"], "-I", "-B", guard["script"]["path"],
        "--floor-mib", str(guard["floor_mib"]), "--timeout", str(guard["timeout_seconds"]),
        "--log", spec["deployment"]["guard_log_path"], "--",
        str(Path(spec["deployment"]["root"]) / "worker"),
        "--spec", spec_pin["path"], "--spec-sha256", spec_pin["sha256"],
    ]
