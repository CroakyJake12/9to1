"""Pinned, pretokenized ALORA input; no tokenizer, model import or data generation.

Each UTF-8 JSONL line has exactly id, category, input_ids, attention_mask, labels.
All three splits use this format. IDs match the manifest's sample order exactly.
Sample SHA-256 uses compact sorted ASCII JSON of the three token arrays only,
matching hf_backend's row convention. Metadata is bound by the file SHA-256;
different IDs/categories therefore cannot disguise identical cross-split tokens.
A separate unmasked-input fingerprint also rejects reused input sequences when
labels, loss masks or masked padding differ; it does not alter manifest hashes.

rows() exposes training records only. Call snapshot(row) immediately before an
optimizer update to reverify its manifest and shard and obtain owned input lists.
Neither this parser nor a digest proves that a declared origin/licence is true;
the existing independent custody admission remains responsible for that claim.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re

from custody.artifacts import CustodyError, _open_regular, checked_path
from custody.provenance import validate_dataset_manifest, verify_dataset_files

CATEGORIES = ("autonomous_recovery", "ui", "ux", "proactive_reasoning", "coding")
TOKEN_FIELDS = ("input_ids", "attention_mask", "labels")
ROW_FIELDS = frozenset(("id", "category", *TOKEN_FIELDS))
_SHA = re.compile(r"[0-9a-f]{64}")
_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}")
_MANIFEST_LIMIT = 64 * 1024 * 1024
_SHARD_LIMIT = 64 * 1024 * 1024
_MAX_TOKENS = 131072


def _strict_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise CustodyError("Duplicate JSON object field")
        result[key] = value
    return result


def _reject_constant(_value):
    raise CustodyError("Nonfinite JSON values are not input data")


def _json(data):
    try:
        return json.loads(data.decode("utf-8"), object_pairs_hook=_strict_object,
                          parse_constant=_reject_constant)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise CustodyError("Invalid UTF-8 JSON input") from error


def _verified_bytes(path, digest, limit, expected_size=None):
    with _open_regular(path) as stream:
        data = stream.read(limit + 1)
    if len(data) > limit or (expected_size is not None and len(data) != expected_size):
        raise CustodyError("Input byte count differs from its admitted bound")
    if hashlib.sha256(data).hexdigest() != digest:
        raise CustodyError("Input bytes differ from their pinned SHA-256")
    return data


def token_sha256(row):
    """Canonical token-row digest, excluding file-bound ID/category metadata."""
    payload = {key: row[key] for key in TOKEN_FIELDS}
    return hashlib.sha256(json.dumps(
        payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
        allow_nan=False).encode("ascii")).hexdigest()


def _unmasked_input_sha256(row):
    # Only called after strict shape/type validation. Keep this independent of
    # labels and masked padding, which must not disguise cross-split reuse.
    tokens = [token for token, mask in zip(row["input_ids"], row["attention_mask"])
              if mask == 1]
    return hashlib.sha256(json.dumps(
        tokens, separators=(",", ":"), ensure_ascii=True).encode("ascii")).hexdigest()


class TrainingDataset:
    """Read-only bound dataset; only the train split can reach an update."""

    def __init__(self, manifest_path: Path, dataset_root: Path, *,
                 expected_sha256: str, purpose="real_training",
                 max_tokens=4096, vocab_size=2147483647):
        if type(expected_sha256) is not str or not _SHA.fullmatch(expected_sha256):
            raise CustodyError("Expected manifest SHA-256 must be lowercase hexadecimal")
        if type(max_tokens) is not int or not 2 <= max_tokens <= _MAX_TOKENS:
            raise CustodyError("max_tokens must be an explicit bounded integer")
        if type(vocab_size) is not int or not 2 <= vocab_size <= 2147483647:
            raise CustodyError("vocab_size must be a bounded integer")
        self.manifest_path = Path(manifest_path)
        self.dataset_root = Path(dataset_root)
        checked_path(self.dataset_root, directory=True)
        self.manifest_sha256 = expected_sha256
        self.max_tokens, self.vocab_size = max_tokens, vocab_size
        raw = _verified_bytes(self.manifest_path, expected_sha256, _MANIFEST_LIMIT)
        self.manifest = validate_dataset_manifest(_json(raw), purpose=purpose)
        self.purpose = self.manifest.purpose
        self.dataset_sha256 = self.manifest.sha256
        if any(entry.size_bytes > _SHARD_LIMIT for entry in self.manifest.files):
            raise CustodyError("Pretokenized shards must be at most 64 MiB")
        for entry in self.manifest.files:
            if not {"examples", "labels"} <= {trace.role for trace in entry.lineage}:
                raise CustodyError("Each supervised split needs examples and labels lineage")
        verify_dataset_files(self.manifest, self.dataset_root)
        # Validate hidden rows internally, retaining only hashes/split metadata.
        # No hidden row or its token arrays are published through this interface.
        seen, seen_inputs = {}, {}
        for entry in self.manifest.files:
            for row in self._read_entry(entry):
                digest = token_sha256(row)
                if digest in seen and seen[digest] != entry.split:
                    raise CustodyError("Actual token content overlaps dataset splits")
                seen[digest] = entry.split
                input_digest = _unmasked_input_sha256(row)
                if input_digest in seen_inputs and seen_inputs[input_digest] != entry.split:
                    raise CustodyError("Unmasked input token content overlaps dataset splits")
                seen_inputs[input_digest] = entry.split
        self._train_entries = tuple(e for e in self.manifest.files if e.split == "train")

    @property
    def identity(self):
        return {"manifest_sha256": self.manifest_sha256,
                "dataset_sha256": self.dataset_sha256, "purpose": self.purpose,
                "format": "alora-pretokenized-jsonl-v1"}

    def _verify_manifest(self):
        _verified_bytes(self.manifest_path, self.manifest_sha256, _MANIFEST_LIMIT)

    def _validate_row(self, row):
        if type(row) is not dict or set(row) != ROW_FIELDS:
            raise CustodyError("Pretokenized row fields differ from contract")
        if type(row["id"]) is not str or not _ID.fullmatch(row["id"]):
            raise CustodyError("Invalid pretokenized sample ID")
        if type(row["category"]) is not str or row["category"] not in CATEGORIES:
            raise CustodyError("Unsupported curriculum category")
        ids = row["input_ids"]
        if type(ids) is not list or not 2 <= len(ids) <= self.max_tokens:
            raise CustodyError("Pretokenized token count exceeds the configured bound")
        for key in TOKEN_FIELDS:
            values = row[key]
            if type(values) is not list or len(values) != len(ids):
                raise CustodyError("Pretokenized token arrays have different shapes")
            if any(type(value) is not int for value in values):
                raise CustodyError("Token arrays require integers, never booleans")
            if key == "attention_mask":
                valid = all(value in (0, 1) for value in values)
            else:
                valid = all(0 <= value < self.vocab_size or
                            (key == "labels" and value == -100) for value in values)
            if not valid:
                raise CustodyError("Token, label or mask value is out of bounds")
        masks, labels = row["attention_mask"], row["labels"]
        if any(mask == 0 and label != -100 for mask, label in zip(masks, labels)):
            raise CustodyError("Padding labels must be excluded from loss")
        if any(labels[i] != -100 and masks[i - 1] == 0 for i in range(1, len(ids))):
            raise CustodyError("A supervised target cannot be predicted from padding")
        if not any(label != -100 for label in labels[1:]):
            raise CustodyError("No supervised shifted next-token target")
        return row

    def _read_entry(self, entry):
        data = _verified_bytes(self.dataset_root / entry.path, entry.sha256,
                               _SHARD_LIMIT, entry.size_bytes)
        lines = data.splitlines()
        if len(lines) != len(entry.samples):
            raise CustodyError("Declared sample count differs from actual JSONL rows")
        rows = []
        for sample, line in zip(entry.samples, lines):
            if not line.strip():
                raise CustodyError("Blank JSONL records are not admitted")
            row = self._validate_row(_json(line))
            if row["id"] != sample.id:
                raise CustodyError("Sample ID/order differs from its manifest")
            if token_sha256(row) != sample.sha256:
                raise CustodyError("Sample hash differs from its actual token arrays")
            rows.append(row)
        return rows

    def _bound_row(self, entry, row):
        # New lists and dictionary; caller mutations cannot change admitted state.
        bound = {"id": row["id"], "category": row["category"],
                 **{key: list(row[key]) for key in TOKEN_FIELDS}}
        bound["_source"] = {
            **self.identity, "file_id": entry.id, "file_sha256": entry.sha256,
            "sample_id": row["id"], "sample_sha256": token_sha256(row)}
        return bound

    def rows(self):
        """Yield verified train rows only; snapshot each row before its update."""
        self._verify_manifest()
        for entry in self._train_entries:
            for row in self._read_entry(entry):
                yield self._bound_row(entry, row)

    def snapshot(self, row):
        """Reverify a training row immediately before use, rejecting stale inputs."""
        if type(row) is not dict or set(row) != ROW_FIELDS | {"_source"}:
            raise CustodyError("An update needs a bound training row")
        self._validate_row({key: row[key] for key in ROW_FIELDS})
        source = row["_source"]
        if type(source) is not dict:
            raise CustodyError("Missing training source identity")
        entry = next((e for e in self._train_entries if e.id == source.get("file_id")), None)
        if entry is None:
            raise CustodyError("Only admitted training files may supply updates")
        self._verify_manifest()
        for actual in self._read_entry(entry):
            if actual["id"] == source.get("sample_id"):
                bound = self._bound_row(entry, actual)
                if bound != row:
                    raise CustodyError("Training input differs from its bound source")
                return bound
        raise CustodyError("Unknown sample in the admitted training file")
