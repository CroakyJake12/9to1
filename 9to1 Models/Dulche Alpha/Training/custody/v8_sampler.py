"""Host-only, pinned v8 SFT sampler. Never loads a tokenizer or exports samples."""

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import random
import re

from .artifacts import CustodyError, _open_regular, relative_parts

V8_SOURCE_SHA256 = "97711a96a0c12751d5c4c3f3ab86dd395924c03ac9367619b35d0f452e7749a8"
_SPLITS = ("train", "validation", "final_held_out")
_DIGEST = re.compile(r"[0-9a-f]{64}")


@dataclass(frozen=True, slots=True)
class SamplerLimits:
    manifest_bytes: int = 1024 * 1024
    split_bytes: int = 128 * 1024 * 1024
    rows: int = 16_384
    token_slots: int = 16 * 1024 * 1024

    def __post_init__(self):
        if any(type(value) is not int or not 0 < value <= cap
               for value, cap in ((self.manifest_bytes, 4 * 1024 * 1024),
                                  (self.split_bytes, 256 * 1024 * 1024),
                                  (self.rows, 65_536), (self.token_slots, 64 * 1024 * 1024))):
            raise CustodyError("Invalid v8 sampler byte or row limit.")


def _pinned_text(path, expected_sha256, max_bytes):
    if type(expected_sha256) is not str or not _DIGEST.fullmatch(expected_sha256):
        raise CustodyError("An approved SHA256 is required for v8 sampler input.")
    with _open_regular(path) as source:
        data = source.read(max_bytes + 1)
    if len(data) > max_bytes or hashlib.sha256(data).hexdigest() != expected_sha256:
        raise CustodyError("V8 sampler file differs from its pinned bytes or approved limit.")
    # v8 Linux read_text uses UTF-8 and splitlines. JSONL round trips the
    # decoded messages unchanged; no data cleaning or filtering is performed.
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError as error:
        raise CustodyError("The pinned v8 sampler input is not UTF-8.") from error


def _train_rows(dataset_root, manifest_sha256, limits):
    manifest = json.loads(_pinned_text(dataset_root / "dataset_manifest.json", manifest_sha256, limits.manifest_bytes))
    if type(manifest) is not dict or type(manifest.get("files")) is not list:
        raise CustodyError("Invalid v8 sampler manifest.")
    files = {}
    for entry in manifest["files"]:
        if type(entry) is not dict or type(entry.get("split")) is not str or entry["split"] in files:
            raise CustodyError("Duplicate or invalid v8 sampler split.")
        files[entry["split"]] = entry
    train, count = [], 0
    for split in _SPLITS:
        entry = files.get(split)
        if (entry is None or type(entry.get("path")) is not str
                or type(entry.get("sha256")) is not str):
            raise CustodyError("The v8 sampler manifest does not pin every required split.")
        path = dataset_root.joinpath(*relative_parts(entry["path"]))
        for line in _pinned_text(path, entry["sha256"], limits.split_bytes).splitlines():
            if line.strip():
                row = json.loads(line)
                if type(row) is not dict:
                    raise CustodyError("A pinned v8 JSONL row is not an object.")
                count += 1
                if count > limits.rows:
                    raise CustodyError("The pinned v8 dataset exceeds its row budget.")
                if split == "train":
                    train.append(row)
    if not train:
        raise CustodyError("The v8 sampler requires at least one training row.")
    return train


def _encode(torch, tokenizer, row, seq_len):
    # v8 train.py 374–400, same tokenizer calls and assistant-header masking.
    messages = row["messages"]
    full_text = tokenizer.apply_chat_template(messages, tokenize=False)
    ids = tokenizer(full_text, truncation=True, max_length=seq_len, add_special_tokens=False)["input_ids"]
    if type(ids) is not list or len(ids) > seq_len:
        raise CustodyError("The admitted tokenizer did not honor v8 sequence truncation.")
    ids = torch.tensor(ids, dtype=torch.long, device="cpu")
    labels = ids.clone()
    assistant_at = next((i for i, m in enumerate(messages) if m["role"] == "assistant"), len(messages))
    prefix = len(tokenizer(tokenizer.apply_chat_template(messages[:assistant_at], add_generation_prompt=True,
                                                         tokenize=False), add_special_tokens=False)["input_ids"])
    labels[: min(prefix, len(labels))] = -100
    return ids, labels


class V8HostSampler:
    """Build only on the host using the launcher-verified existing tokenizer.

    The launcher verifies the tokenizer/runtime/model pins before supplying the
    tokenizer object. This module verifies the dataset manifest plus split
    bytes, but cannot infer which artifact instantiated a Python object.
    """

    def __init__(self, dataset_root, tokenizer, *, manifest_sha256, seed, seq_len, grad_accum, start_step,
                 limits=SamplerLimits(), resumed_python_rng=None):
        if (type(seed) is not int or type(seq_len) is not int or not 0 < seq_len <= 32768
                or type(grad_accum) is not int or not 0 < grad_accum <= 4096
                or type(start_step) is not int or start_step < 0 or type(limits) is not SamplerLimits
                or not callable(tokenizer) or not callable(getattr(tokenizer, "apply_chat_template", None))):
            raise CustodyError("Invalid admitted v8 sampler settings.")
        train = _train_rows(Path(dataset_root), manifest_sha256, limits)
        if len(train) * seq_len > limits.token_slots:
            raise CustodyError("The v8 cached training batches exceed the token-slot budget.")
        rng = random.Random(seed)
        order = list(range(len(train)))
        rng.shuffle(order)
        # v8 restores RNG after shuffling. It does not reshuffle on resume.
        if resumed_python_rng is not None:
            rng.setstate(resumed_python_rng)
        self.python_rng = rng.getstate()
        self.grad_accum, self.start_step = grad_accum, start_step
        import torch
        self._batches = tuple(_encode(torch, tokenizer, train[i], seq_len) for i in order)

    def microbatch(self, step, micro):
        """Return original CPU [1, seq] input IDs and labels for a v8 coordinate."""
        if (type(step) is not int or step <= self.start_step or type(micro) is not int
                or not 0 <= micro < self.grad_accum):
            raise CustodyError("V8 sampler coordinates precede resume or exceed accumulation.")
        # v8 initializes bi = (start_step * grad_accum) % len(batches)
        # then selects and increments it, without reshuffling at wraparound.
        ids, labels = self._batches[((step - 1) * self.grad_accum + micro) % len(self._batches)]
        return ids.unsqueeze(0), labels.unsqueeze(0)
