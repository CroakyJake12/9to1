"""Single-architecture HF integration. Runtime/job authority stays with custody."""
from __future__ import annotations

from dataclasses import asdict, dataclass
import hashlib
import json
from pathlib import Path
from typing import Mapping
import copy
import threading

import torch
from peft import LoraConfig, TaskType, get_peft_model, prepare_model_for_kbit_training
from transformers import PreTrainedTokenizerFast, BitsAndBytesConfig, LlamaConfig, LlamaForCausalLM
from tokenizers import Tokenizer

from custody.artifacts import ArtifactRef, ArtifactStore, CustodyError, _open_regular, checked_path
from custody.provenance import DatasetManifest, validate_dataset_manifest, verify_dataset_files


_FILES = {"config.json", "model.safetensors", "tokenizer.json", "tokenizer_config.json",
          "special_tokens_map.json", "generation_config.json"}
_REQUIRED = {"config.json", "model.safetensors", "tokenizer.json", "tokenizer_config.json"}
_BATCH_BYTES = 1024 * 1024
_SPECIAL_TOKENS = {"bos_token", "eos_token", "unk_token", "sep_token", "pad_token", "cls_token", "mask_token", "additional_special_tokens"}
_TOKENIZER_FIELDS = _SPECIAL_TOKENS | {"backend", "model_max_length", "tokenizer_class", "clean_up_tokenization_spaces", "padding_side", "truncation_side"}


def _tokenizer_options(config, special, context_limit):
    """No config-supplied filename or tokenizer implementation reaches HF."""
    if type(config) is not dict or not set(config) <= _TOKENIZER_FIELDS:
        raise CustodyError("Tokenizer config contains an unsupported field or path redirect")
    if config.get("tokenizer_class") not in {"PreTrainedTokenizerFast", "TokenizersBackend"}:
        raise CustodyError("Only a reviewed generic local fast tokenizer is admitted")
    if config.get("backend", "tokenizers") != "tokenizers":
        raise CustodyError("Unsupported tokenizer backend")
    if type(special) is not dict or not set(special) <= _SPECIAL_TOKENS:
        raise CustodyError("Unsupported special-token fields")
    options = {}
    for source in (config, special):
        for key in _SPECIAL_TOKENS & source.keys():
            value = source[key]
            values = value if key == "additional_special_tokens" else [value]
            if type(values) is not list or len(values) > 256 or any(type(token) is not str or not 0 < len(token) <= 1024 for token in values):
                raise CustodyError("Special tokens require bounded literal strings")
            if key in options and options[key] != value:
                raise CustodyError("Conflicting special-token declarations")
            options[key] = copy.deepcopy(value)
    for key in ("padding_side", "truncation_side"):
        if key in config:
            if config[key] not in {"left", "right"}:
                raise CustodyError("Invalid tokenizer direction")
            options[key] = config[key]
    if "clean_up_tokenization_spaces" in config:
        if type(config["clean_up_tokenization_spaces"]) is not bool:
            raise CustodyError("Invalid tokenizer cleanup setting")
        options["clean_up_tokenization_spaces"] = config["clean_up_tokenization_spaces"]
    maximum = config.get("model_max_length", context_limit)
    if type(maximum) is not int or not 0 < maximum <= 10**36 or type(context_limit) is not int or context_limit <= 0:
        raise CustodyError("Invalid tokenizer context bound")
    options["model_max_length"] = min(maximum, context_limit)
    return options


def make_software_model() -> LlamaForCausalLM:
    """Random tiny architecture for software correctness ONLY, never a base model."""
    config = LlamaConfig(vocab_size=32, hidden_size=16, intermediate_size=24,
                         num_hidden_layers=2, num_attention_heads=2, num_key_value_heads=2,
                         max_position_embeddings=32, attention_dropout=0.0,
                         tie_word_embeddings=False, bos_token_id=1, eos_token_id=2,
                         pad_token_id=0, use_cache=False)
    config._attn_implementation = "eager"
    return LlamaForCausalLM(config).to(device="cpu", dtype=torch.float64)


def attach_lora(model, *, rank: int = 2, alpha: int = 4):
    if type(rank) is not int or not 0 < rank <= 64 or type(alpha) is not int or not 0 < alpha <= 256:
        raise ValueError("LoRA rank/alpha must be bounded positive integers")
    if getattr(model.config, "model_type", None) != "llama":
        raise ValueError("Only Llama adapter targets are reviewed")
    return get_peft_model(model, LoraConfig(task_type=TaskType.CAUSAL_LM, r=rank,
                                          lora_alpha=alpha, lora_dropout=0.0,
                                          target_modules=["q_proj", "v_proj"], bias="none"))


def _verified_bytes(path: Path, expected_sha: str, expected_size: int, maximum: int) -> bytes:
    if type(expected_size) is not int or not 0 <= expected_size <= maximum:
        raise CustodyError("Artifact exceeds this adapter's byte limit")
    with _open_regular(path) as stream:
        data = stream.read(maximum + 1)
    if len(data) != expected_size or hashlib.sha256(data).hexdigest() != expected_sha:
        raise CustodyError("Local file differs from admitted artifact")
    return data


@dataclass(frozen=True)
class LocalBundle:
    directory: Path
    artifacts: Mapping[str, ArtifactRef]
    maximum_weight_bytes: int

    def verify(self, store: ArtifactStore) -> dict:
        checked_path(self.directory, directory=True)
        names = set(self.artifacts)
        if not _REQUIRED <= names <= _FILES or {p.name for p in self.directory.iterdir()} != names:
            raise CustodyError("Local bundle has missing/unreviewed files")
        if type(self.maximum_weight_bytes) is not int or self.maximum_weight_bytes <= 0:
            raise CustodyError("Explicit weight storage bound required")
        for name, reference in self.artifacts.items():
            store.verify(reference)
            target = self.directory / name
            # Stream model hashes: do not allocate a second model-sized buffer.
            limit = self.maximum_weight_bytes if name == "model.safetensors" else 16 * 1024 * 1024
            if reference.size_bytes > limit:
                raise CustodyError("Model bundle artifact exceeds admitted bound")
            digest, count = hashlib.sha256(), 0
            with _open_regular(target) as stream:
                while chunk := stream.read(1024 * 1024):
                    count += len(chunk)
                    if count > limit:
                        raise CustodyError("Model bundle byte limit exceeded")
                    digest.update(chunk)
            if count != reference.size_bytes or digest.hexdigest() != reference.sha256:
                raise CustodyError("Model bundle does not match immutable custody reference")
        reference = self.artifacts["config.json"]
        config = json.loads(_verified_bytes(self.directory / "config.json", reference.sha256, reference.size_bytes, 1024 * 1024))
        if not isinstance(config, dict) or config.get("model_type") != "llama" or config.get("architectures") != ["LlamaForCausalLM"]:
            raise CustodyError("Only explicitly declared LlamaForCausalLM is supported")
        if "auto_map" in config or "quantization_config" in config:
            raise CustodyError("Remote architecture/previously quantized bundles are unsupported")
        tokenizer_ref = self.artifacts["tokenizer_config.json"]
        tokenizer_config = json.loads(_verified_bytes(self.directory / "tokenizer_config.json",
                                      tokenizer_ref.sha256, tokenizer_ref.size_bytes, 1024 * 1024))
        special = {}
        if "special_tokens_map.json" in self.artifacts:
            special_ref = self.artifacts["special_tokens_map.json"]
            special = json.loads(_verified_bytes(self.directory / "special_tokens_map.json", special_ref.sha256,
                                                 special_ref.size_bytes, 1024 * 1024))
        _tokenizer_options(tokenizer_config, special, config.get("max_position_embeddings"))
        return config

    def load_tokenizer(self, context_limit):
        # Re-read and reverify only fixed sealed filenames. Never call
        # AutoTokenizer/from_pretrained or pass a config dictionary as kwargs.
        config_ref, json_ref = self.artifacts["tokenizer_config.json"], self.artifacts["tokenizer.json"]
        config = json.loads(_verified_bytes(self.directory / "tokenizer_config.json", config_ref.sha256, config_ref.size_bytes, 1024 * 1024))
        special = {}
        if "special_tokens_map.json" in self.artifacts:
            ref = self.artifacts["special_tokens_map.json"]
            special = json.loads(_verified_bytes(self.directory / "special_tokens_map.json", ref.sha256, ref.size_bytes, 1024 * 1024))
        options = _tokenizer_options(config, special, context_limit)
        serialized = _verified_bytes(self.directory / "tokenizer.json", json_ref.sha256, json_ref.size_bytes, 16 * 1024 * 1024)
        tokenizer = Tokenizer.from_str(serialized.decode("utf-8"))
        return PreTrainedTokenizerFast(tokenizer_object=tokenizer, **options)


def _batch_values(data: bytes, vocab_size: int, max_context: int):
    value = json.loads(data)
    if type(value) is not dict or set(value) != {"input_ids", "attention_mask", "labels"}:
        raise CustodyError("Pretokenized batch fields differ from contract")
    rows = value["input_ids"]
    if type(rows) is not list or not 1 <= len(rows) <= 8 or type(rows[0]) is not list:
        raise CustodyError("Batch row bound exceeded")
    length = len(rows[0])
    if not 2 <= length <= min(max_context, 4096):
        raise CustodyError("Batch sequence bound exceeded")
    for key, matrix in value.items():
        if type(matrix) is not list or len(matrix) != len(rows):
            raise CustodyError("Batch shapes differ")
        for row in matrix:
            if type(row) is not list or len(row) != length or any(type(token) is not int for token in row):
                raise CustodyError("Batch must be a rectangular integer array")
            if key == "attention_mask":
                valid = all(token in (0, 1) for token in row)
            else:
                valid = all(0 <= token < vocab_size or (key == "labels" and token == -100) for token in row)
            if not valid:
                raise CustodyError("Batch token/mask values are invalid")
    if not any(token != -100 for row in value["labels"] for token in row[1:]):
        raise CustodyError("No supervised next-token targets")
    if any(mask == 0 and label != -100 for masks, labels in zip(value["attention_mask"], value["labels"])
           for mask, label in zip(masks, labels)):
        raise CustodyError("Padding tokens must be excluded from loss")
    return value


def _parse_batch(data: bytes, vocab_size: int, max_context: int):
    return {key: torch.tensor(matrix, dtype=torch.long, device="cpu")
            for key, matrix in _batch_values(data, vocab_size, max_context).items()}


def _verify_samples(entry, values):
    rows = len(values["input_ids"])
    if len(entry.samples) != rows:
        raise CustodyError("Declared samples do not match the supervised batch rows")
    for index, sample in enumerate(entry.samples):
        canonical = json.dumps({key: matrix[index] for key, matrix in values.items()},
                               sort_keys=True, separators=(",", ":")).encode("ascii")
        if hashlib.sha256(canonical).hexdigest() != sample.sha256:
            raise CustodyError("Declared sample digest differs from its actual token row")


class LocalSession:
    """Admitted local HF execution. No arbitrary caller tensors enter step_file."""

    def __init__(self, store: ArtifactStore, bundle: LocalBundle, dataset: DatasetManifest,
                 dataset_root: Path, *, purpose: str, method: str = "full", rank: int = 2, alpha: int = 4,
                 gpu_admission=None, on_update=None, on_failed_update=None, checkpoint_publisher=None):
        if method not in {"full", "lora", "nf4_lora"}:
            raise CustodyError("Unsupported HF training method")
        if purpose not in {"software_test_only", "real_training"}:
            raise CustodyError("Explicit supervised purpose required")
        if dataset is not None and dataset.purpose != purpose:
            raise CustodyError("Dataset purpose differs from this execution boundary")
        from .checkpoints import VerifiedCheckpointPublisher
        if checkpoint_publisher is not None and (type(checkpoint_publisher) is not VerifiedCheckpointPublisher
                or checkpoint_publisher.purpose != purpose or checkpoint_publisher.store.root != store.root):
            raise CustodyError("The checkpoint publisher must belong to this store and purpose")
        if purpose == "real_training" and checkpoint_publisher is None:
            raise CustodyError("Real training requires a verified immutable checkpoint publisher")
        if purpose == "software_test_only" and (method == "nf4_lora" or gpu_admission is not None):
            raise CustodyError("Software fixtures may only execute on CPU without NF4")
        self.dataset = validate_dataset_manifest(dataset.as_record(), purpose=purpose)
        if any(not any(trace.role == "labels" for trace in entry.lineage) for entry in self.dataset.files):
            raise CustodyError("Supervised batches require explicit label provenance")
        self.dataset_root = Path(dataset_root)
        verify_dataset_files(self.dataset, self.dataset_root)
        model_config = bundle.verify(store)
        for entry in self.dataset.files:
            data = _verified_bytes(self.dataset_root / entry.path, entry.sha256, entry.size_bytes, _BATCH_BYTES)
            _verify_samples(entry, _batch_values(data, model_config["vocab_size"], model_config["max_position_embeddings"]))
        kwargs = {"local_files_only": True, "trust_remote_code": False, "use_safetensors": True,
                  "attn_implementation": "eager"}
        if method == "nf4_lora":
            # Admission is an operator-supplied custody object, never a request's
            # bool/UUID. It must expose the genuine approved device policy.
            from custody.resources import Admission
            if (type(gpu_admission) is not Admission or gpu_admission.device_uuid is None
                    or gpu_admission.device_uuid not in gpu_admission.policy.allowed_gpu_uuids):
                raise CustodyError("NF4 requires an actual supervised GPU admission")
            import os
            if os.environ.get("CUDA_VISIBLE_DEVICES") != gpu_admission.device_uuid:
                raise CustodyError("Worker GPU visibility differs from admission")
            kwargs.update(quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4",
                          bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=torch.float32), device_map={"": 0})
        else:
            from .cpu_guard import assert_cpu
            # Empty visibility is insufficient on the observed Windows runtime.
            import os
            if os.environ.get("CUDA_VISIBLE_DEVICES") != "-1" or torch.cuda.is_initialized():
                raise CustodyError("CPU session requires an isolated -1 worker before loading")
            kwargs.update(device_map={"": "cpu"}, dtype=torch.float32)
        self.model = LlamaForCausalLM.from_pretrained(str(bundle.directory), **kwargs)
        self.model.config.use_cache = False
        self.tokenizer = bundle.load_tokenizer(self.model.config.max_position_embeddings)
        if method == "nf4_lora":
            self.model = prepare_model_for_kbit_training(self.model, use_gradient_checkpointing=False)
        if method != "full":
            self.model = attach_lora(self.model, rank=rank, alpha=alpha)
        if method != "nf4_lora":
            assert_cpu(self.model, {})
        self.method = method
        self.adapter_config = None if method == "full" else {"rank": rank, "alpha": alpha,
                              "targets": ["q_proj", "v_proj"], "dropout": 0.0, "bias": "none"}
        self.purpose, self.on_update, self.failed = purpose, on_update, False
        self.on_failed_update = on_failed_update
        self._checkpoint_publisher = checkpoint_publisher
        self.last_publication = None
        self._step_lock = threading.RLock()
        self._updating = False
        self.model_artifacts = {name: ref.sha256 for name, ref in bundle.artifacts.items()}
        self.optimizer = torch.optim.Adam([p for p in self.model.parameters() if p.requires_grad], lr=1e-4, foreach=False)
        self.updates = 0

    def snapshot(self) -> dict:
        """Detached owned CPU copies for no-clobber custody publication, no pickle."""
        if not self._step_lock.acquire(blocking=False):
            raise RuntimeError("Snapshot reads require a settled update or its publishing callback")
        try:
            return self._snapshot_owned()
        finally:
            self._step_lock.release()

    def _snapshot_owned(self):
        def clone(value):
            if isinstance(value, torch.Tensor):
                return value.detach().cpu().clone()
            if isinstance(value, dict):
                return {key: clone(item) for key, item in value.items()}
            if isinstance(value, (tuple, list)):
                return type(value)(clone(item) for item in value)
            return copy.deepcopy(value)
        return {"schema": 1, "kind": "hf-local-session", "method": self.method,
                "purpose": self.purpose, "updates": self.updates, "failed": self.failed,
                "model": clone(self.model.state_dict()), "optimizer": clone(self.optimizer.state_dict()),
                "model_config": self.model.config.to_dict(), "rng": torch.get_rng_state().clone(),
                "adapter_config": copy.deepcopy(self.adapter_config),
                "model_artifacts": dict(self.model_artifacts), "dataset_manifest_sha256": self.dataset.sha256,
                "torch_version": str(torch.__version__)}

    def step_file(self, file_id: str, *, cancelled=lambda: False) -> dict:
        if not self._step_lock.acquire(blocking=False):
            raise RuntimeError("An update is already in progress")
        try:
            if self._updating:
                raise RuntimeError("An update is already in progress")
            self._updating = True
            try:
                return self._step_file(file_id, cancelled=cancelled)
            finally:
                self._updating = False
        finally:
            self._step_lock.release()

    def _step_file(self, file_id, *, cancelled):
        if self.failed:
            raise RuntimeError("Session failed; preserve its state, do not repeat the update")
        if cancelled():
            raise InterruptedError("Cancelled before batch read")
        entry = next((item for item in self.dataset.files if item.id == file_id), None)
        if entry is None or entry.split != "train":
            raise CustodyError("Only admitted train-split files may update a model")
        data = _verified_bytes(self.dataset_root / entry.path, entry.sha256, entry.size_bytes, _BATCH_BYTES)
        _verify_samples(entry, _batch_values(data, self.model.config.vocab_size, self.model.config.max_position_embeddings))
        batch = _parse_batch(data, self.model.config.vocab_size, self.model.config.max_position_embeddings)
        device = next(self.model.parameters()).device
        batch = {key: tensor.to(device) for key, tensor in batch.items()}
        if self.method != "nf4_lora":
            from .cpu_guard import assert_cpu
            assert_cpu(self.model, batch)
        self.model.train()
        start_rng = torch.get_rng_state().clone()
        update_started = False
        attempted_update = self.updates + 1
        failure = None
        failure_phase = "optimizer_update"
        try:
            self.optimizer.zero_grad(set_to_none=True)
            if cancelled():
                raise InterruptedError("Cancelled before forward")
            loss = self.model(**batch).loss
            if not bool(torch.isfinite(loss)):
                raise FloatingPointError("Nonfinite causal loss; no update")
            if cancelled():
                raise InterruptedError("Cancelled before backward")
            loss.backward()
            selected = {name: p for name, p in self.model.named_parameters() if p.requires_grad}
            if any(p.grad is not None and not bool(torch.isfinite(p.grad).all()) for p in selected.values()):
                raise FloatingPointError("Nonfinite causal gradient; no update")
            with_gradient = tuple(name for name, p in selected.items() if p.grad is not None)
            nonzero = tuple(name for name, p in selected.items() if p.grad is not None and bool(p.grad.count_nonzero()))
            if cancelled():
                raise InterruptedError("Cancelled before update")
            update_started = True
            self.optimizer.step()
            if any(not bool(torch.isfinite(p).all()) for p in self.model.parameters()):
                raise FloatingPointError("Nonfinite model update; preserve failed candidate")
            if any(isinstance(value, torch.Tensor) and not bool(torch.isfinite(value).all())
                   for slot in self.optimizer.state.values() for value in slot.values()):
                raise FloatingPointError("Nonfinite optimizer update; preserve failed candidate")
            self.updates += 1
            record = {"schema": 1, "status": "completed", "method": self.method, "purpose": self.purpose, "update": self.updates,
                "loss": float(loss.detach()), "loss_target_tokens": int((batch["labels"][:, 1:] != -100).sum()),
                "selected": tuple(selected), "with_gradient": with_gradient, "nonzero_gradient": nonzero,
                "trainable_parameters": sum(p.numel() for p in selected.values()),
                "dataset_manifest_sha256": self.dataset.sha256, "dataset_file_sha256": entry.sha256,
                "hf_recompute_equivalence": "not_validated", "nf4_execution": self.method == "nf4_lora"}
        except BaseException as error:
            failure = error
        try:
            self.optimizer.zero_grad(set_to_none=True)
        except BaseException as cleanup_error:
            failure_phase = "gradient_cleanup"
            failure = cleanup_error if failure is None else BaseExceptionGroup(
                "Update and gradient cleanup failed", [failure, cleanup_error])
        if failure is None:
            try:
                failure_phase = "checkpoint_publication"
                if self._checkpoint_publisher is not None:
                    receipt = self._checkpoint_publisher.publish(self.snapshot(), copy.deepcopy(record))
                    self.last_publication = receipt
                    record["publication"] = {"checkpoint": asdict(receipt.checkpoint), "evidence": asdict(receipt.evidence)}
                if self.on_update is not None:
                    failure_phase = "update_observer"
                    self.on_update(self.snapshot(), copy.deepcopy(record))
            except BaseException as error:
                failure = error
        if failure is not None:
            if isinstance(failure, InterruptedError) and not update_started and self.method != "nf4_lora":
                torch.set_rng_state(start_rng)
            else:
                self.failed = True
            if update_started:
                failure_record = {"schema": 1, "purpose": self.purpose, "status": "failed",
                           "attempted_update": attempted_update, "completed_updates": self.updates,
                           "phase": failure_phase,
                           "dataset_manifest_sha256": self.dataset.sha256, "dataset_file_sha256": entry.sha256}
                try:
                    # A separate immutable publication keeps the exact failed state,
                    # including partial/nonfinite updates. Never retry the optimizer.
                    if self._checkpoint_publisher is not None:
                        receipt = self._checkpoint_publisher.publish(self.snapshot(), copy.deepcopy(failure_record))
                        self.last_publication = receipt
                        failure_record["publication"] = {"checkpoint": asdict(receipt.checkpoint), "evidence": asdict(receipt.evidence)}
                except BaseException as preservation_error:
                    raise BaseExceptionGroup("The update failed and its candidate could not be preserved",
                                             [failure, preservation_error]) from None
                if self.on_failed_update is not None:
                    try:
                        self.on_failed_update(self.snapshot(), copy.deepcopy(failure_record))
                    except BaseException as observer_error:
                        raise BaseExceptionGroup("The update and its failure observer failed; retained publication receipts remain available",
                                                 [failure, observer_error]) from None
            raise failure
        return record
