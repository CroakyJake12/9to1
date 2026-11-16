"""Transactional resume for the supervised HF CPU software fixture."""

from dataclasses import asdict
from importlib.metadata import version

import torch

from custody import ArtifactRef, CustodyError
from custody.artifacts import _encode_record, _read_record
from custody.snapshot import load_snapshot, publish_snapshot
from .checkpoints import CheckpointPublication
from .cpu_guard import assert_cpu

_PURPOSE = "software_test_only"
_EVIDENCE_BYTES = 1024 * 1024
_SNAPSHOT_KEYS = {
    "schema", "kind", "method", "purpose", "updates", "failed", "model", "optimizer",
    "model_config", "rng", "adapter_config", "model_artifacts", "dataset_manifest_sha256",
    "torch_version", "resume",
}


def _same(left, right):
    if type(left) is not type(right):
        return False
    if type(right) is dict:
        return left.keys() == right.keys() and all(_same(left[key], value) for key, value in right.items())
    if type(right) in (list, tuple):
        return len(left) == len(right) and all(_same(a, b) for a, b in zip(left, right))
    return left == right


def _topology(model):
    first = {}
    return tuple((name, first.setdefault(id(parameter), name), tuple(parameter.shape),
                  str(parameter.dtype), parameter.requires_grad)
                 for name, parameter in model.named_parameters(remove_duplicate=False))


def _modules(model):
    return tuple((name, module.training) for name, module in model.named_modules())


def compatibility(session):
    return {
        "schema": 1, "optimizer": "torch.optim.Adam",
        "base_refs": {name: dict(ref) for name, ref in session._model_artifact_refs.items()},
        "versions": {"transformers": version("transformers"), "peft": version("peft")},
        "threads": torch.get_num_threads(), "default_dtype": str(torch.get_default_dtype()),
        "topology": _topology(session.model),
    }


def resume_metadata(session):
    return {"compatibility": compatibility(session), "training_modes": _modules(session.model),
            "parameter_updates": dict(session._parameter_updates)}


def _manifest(store, reference):
    directory = store._artifact_directory(reference.attempt, reference.artifact_id)
    seal = _read_record(directory / "seal.json")["sha256"]
    return _read_record(directory / "manifest.json", seal)


def _read_checkpoint(session, publisher, publication):
    if type(publication) is not CheckpointPublication:
        raise CustodyError("Resume requires a verified checkpoint and its linked evidence.")
    checkpoint, evidence = publication.checkpoint, publication.evidence
    if (type(checkpoint) is not ArtifactRef or type(evidence) is not ArtifactRef
            or checkpoint.attempt != evidence.attempt or checkpoint == evidence
            or checkpoint.run_id != publisher.attempt.run_id or checkpoint.attempt == publisher.attempt
            or type(evidence.size_bytes) is not int or not 0 < evidence.size_bytes <= _EVIDENCE_BYTES):
        raise CustodyError("Resume inputs must identify one separate attempt in the same run.")

    store = publisher.store
    destination = _read_record(store.attempt_path(publisher.attempt) / "attempt.json")
    inputs = destination.get("resume_from")
    if type(inputs) is not list or any(asdict(ref) not in inputs for ref in (checkpoint, evidence)):
        raise CustodyError("The destination attempt must retain both source references.")
    sealed = _read_record(store.verify(evidence), evidence.sha256)
    manifest = _manifest(store, evidence)
    if (manifest.get("kind") != "evidence" or manifest.get("inputs") != [asdict(checkpoint)]
            or sealed.keys() != {"schema", "purpose", "status", "checkpoint", "record"}
            or type(sealed["schema"]) is not int or sealed["schema"] != 1
            or sealed["purpose"] != _PURPOSE or sealed["status"] != "completed"
            or sealed["checkpoint"] != asdict(checkpoint)):
        raise CustodyError("Resume requires the exact sealed evidence for a completed checkpoint.")
    update = sealed["record"]
    if (type(update) is not dict or type(update.get("schema")) is not int or update["schema"] != 1
            or update.get("status") != "completed" or update.get("purpose") != _PURPOSE
            or update.get("method") != session.method or update.get("dataset_manifest_sha256") != session.dataset.sha256
            or not any(item.split == "train" and item.sha256 == update.get("dataset_file_sha256")
                       for item in session.dataset.files)
            or type(update.get("update")) is not int or update["update"] < 1):
        raise CustodyError("The checkpoint update evidence differs from the admitted training fixture.")
    state = load_snapshot(store, checkpoint, max_bytes=publisher.max_bytes)
    if _manifest(store, checkpoint).get("kind") != "segment_checkpoint":
        raise CustodyError("Resume requires a sealed segment checkpoint.")
    return state, update["update"]


def _model_config(config):
    if type(config) is not dict:
        raise CustodyError("The checkpoint model configuration is invalid.")
    return {key: value for key, value in config.items() if key != "_name_or_path"}


def _matches_tensor(value, expected):
    return (type(value) is torch.Tensor and value.device.type == "cpu" and value.layout == torch.strided
            and value.dtype == expected.dtype and value.shape == expected.shape
            and bool(torch.isfinite(value).all()))


def _identical_tensor(left, right):
    return torch.equal(left.detach().contiguous().reshape(-1).view(torch.uint8),
                       right.detach().contiguous().reshape(-1).view(torch.uint8))


def _stage_adam(session, values, counts, updates):
    parameters = tuple((name, param) for name, param in session.model.named_parameters() if param.requires_grad)
    if (type(session.optimizer) is not torch.optim.Adam or type(counts) is not dict
            or counts.keys() != {name for name, _ in parameters}
            or any(type(count) is not int or not 0 <= count <= updates for count in counts.values())):
        raise CustodyError("The checkpoint optimizer update history differs from its trainable parameters.")
    staged = torch.optim.Adam([param for _, param in parameters], lr=1e-4, foreach=False)
    expected = staged.state_dict()
    if (type(values) is not dict or values.keys() != expected.keys()
            or not _same(values["param_groups"], expected["param_groups"])):
        raise CustodyError("The checkpoint Adam settings or parameter order differs.")
    states = values["state"]
    if (type(states) is not dict or any(type(key) is not int for key in states)
            or states.keys() != {index for index, (name, _) in enumerate(parameters) if counts[name] > 0}):
        raise CustodyError("The checkpoint Adam slots differ from its update history.")
    scalar_dtype = torch.float64 if torch.get_default_dtype() == torch.float64 else torch.float32
    for index, slots in states.items():
        name, parameter = parameters[index]
        if (type(slots) is not dict or slots.keys() != {"step", "exp_avg", "exp_avg_sq"}
                or any(not _matches_tensor(slots[key], parameter) for key in ("exp_avg", "exp_avg_sq"))
                or bool((slots["exp_avg_sq"] < 0).any())):
            raise CustodyError("The checkpoint contains invalid Adam moments.")
        step = slots["step"]
        if (type(step) is not torch.Tensor or step.device.type != "cpu" or step.layout != torch.strided
                or step.ndim != 0 or step.dtype != scalar_dtype or not bool(torch.isfinite(step))
                or float(step) != counts[name]):
            raise CustodyError("The checkpoint Adam step differs from its update history.")
    staged.load_state_dict(values)
    return staged


def _prepare(session, state, updates):
    if (state.keys() != _SNAPSHOT_KEYS or state["kind"] != "hf-local-session"
            or state["purpose"] != _PURPOSE or state["method"] != session.method or state["failed"] is not False
            or type(state["updates"]) is not int or state["updates"] != updates
            or state["torch_version"] != str(torch.__version__)
            or state["dataset_manifest_sha256"] != session.dataset.sha256
            or not _same(state["model_artifacts"], session.model_artifacts)
            or not _same(state["adapter_config"], session.adapter_config)
            or not _same(_model_config(state["model_config"]), _model_config(session.model.config.to_dict()))):
        raise CustodyError("The checkpoint model, purpose, dataset, method or runtime differs.")
    meta = state["resume"]
    identity = compatibility(session)
    if (type(meta) is not dict or meta.keys() != {"compatibility", "training_modes", "parameter_updates"}
            or not _same(meta["compatibility"], identity)):
        raise CustodyError("The checkpoint resume topology or dependency identity differs.")
    modes = meta["training_modes"]
    names = tuple(name for name, _ in session.model.named_modules())
    if (type(modes) is not tuple or len(modes) != len(names)
            or any(type(item) is not tuple or len(item) != 2 or item[0] != name or type(item[1]) is not bool
                   for item, name in zip(modes, names))):
        raise CustodyError("The checkpoint module modes differ from the model topology.")
    model, live = state["model"], session.model.state_dict()
    if (type(model) is not dict or model.keys() != live.keys()
            or any(not _matches_tensor(value, live[name]) for name, value in model.items())
            or any(not _identical_tensor(model[name], model[alias])
                   for name, alias, *_ in identity["topology"] if name != alias)):
        raise CustodyError("The checkpoint contains invalid CPU model tensors or inconsistent tied weights.")
    rng = state["rng"]
    if not _matches_tensor(rng, torch.get_rng_state()):
        raise CustodyError("The checkpoint CPU generator state has an invalid shape or dtype.")
    try:
        torch.Generator(device="cpu").set_state(rng)
    except RuntimeError as error:
        raise CustodyError("The checkpoint CPU generator state is invalid.") from error
    return _stage_adam(session, state["optimizer"], meta["parameter_updates"], updates)


def _set_modes(model, modes):
    for (_, module), (_, training) in zip(model.named_modules(), modes, strict=True):
        module.training = training


def _confirm_state(model, expected, modes, rng):
    current = model.state_dict()
    if (current.keys() != expected.keys() or any(not _identical_tensor(value, current[name])
            for name, value in expected.items()) or _modules(model) != modes
            or not torch.equal(torch.get_rng_state(), rng)):
        raise RuntimeError("The applied model, module modes or CPU RNG could not be confirmed.")


def _preserve_uncertain(session, publisher, publication):
    store, attempt = publisher.store, publisher.attempt
    candidate = publish_snapshot(store, attempt, "failed-restore-state", session.snapshot(),
                                 max_bytes=publisher.max_bytes, kind="checkpoint")
    store.verify(candidate)
    payload = _encode_record({
        "schema": 1, "purpose": _PURPOSE, "status": "restore_rollback_unconfirmed",
        "source": asdict(publication), "candidate": asdict(candidate),
    })
    proof = store.publish_bytes(attempt, "failed-restore-evidence.json", "evidence", payload,
                                inputs=(publication.checkpoint, publication.evidence, candidate))
    store.verify(proof)


def restore_checkpoint(session, publication):
    publisher = session._checkpoint_publisher
    if (session.purpose != _PURPOSE or session.method not in ("full", "lora")
            or publisher is None or session.failed or session.updates != 0):
        raise CustodyError("Resume requires a fresh CPU software-test session with a destination publisher.")
    assert_cpu(session.model, {})
    state, updates = _read_checkpoint(session, publisher, publication)
    staged = _prepare(session, state, updates)
    model = session.model
    prior_model = {name: tensor.detach().clone() for name, tensor in model.state_dict().items()}
    prior_modes, prior_rng = _modules(model), torch.get_rng_state().clone()
    try:
        model.load_state_dict(state["model"], strict=True)
        _set_modes(model, state["resume"]["training_modes"])
        torch.set_rng_state(state["rng"])
        _confirm_state(model, state["model"], state["resume"]["training_modes"], state["rng"])
    except BaseException as error:
        rollback = []
        for undo in (lambda: model.load_state_dict(prior_model, strict=True),
                     lambda: _set_modes(model, prior_modes), lambda: torch.set_rng_state(prior_rng)):
            try:
                undo()
            except BaseException as failure:
                rollback.append(failure)
        try:
            _confirm_state(model, prior_model, prior_modes, prior_rng)
        except BaseException as failure:
            rollback.append(failure)
        if rollback:
            session.failed = True
            try:
                _preserve_uncertain(session, publisher, publication)
            except BaseException as failure:
                rollback.append(failure)
            raise BaseExceptionGroup("Checkpoint restore rollback was uncertain; the session is stopped",
                                     [error, *rollback]) from None
        raise
    session.optimizer = staged
    session.updates = updates
    session._parameter_updates = state["resume"]["parameter_updates"]
