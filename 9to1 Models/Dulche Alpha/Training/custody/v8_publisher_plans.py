"""Derive actual v8 rank startup plans using pinned headers, not weight payload."""

from dataclasses import asdict
import hashlib
import os
from pathlib import Path

from .artifacts import CustodyError, _encode_record, _open_regular
from .qwen35_shard_plan import SHARD, ShardHeader, TensorHeader, plan_qwen35_text_shards
from .qwen35_shard_reader import (_META_LIMIT, _document, RankShardManifest, RankTensorReader,
                                 ShardPin, TensorPin)


def _read_doc(path, digest):
    with _open_regular(path) as source:
        data = source.read(_META_LIMIT + 1)
    if (not 0 < len(data) <= _META_LIMIT or type(digest) is not str
            or hashlib.sha256(data).hexdigest() != digest):
        raise CustodyError("Rank plan metadata differs from its authenticated source.")
    return _document(data)


def publisher_rank_plans(model_root, rank_manifests, *, config_sha256):
    """Return canonical plan bytes after validating both sealed publisher inputs.

    rank_manifests holds exactly (path, SHA256) in guest, native order. The
    configured launcher must obtain those hashes from authenticated custody.
    Reads index, config and shard headers only; no shard payload is opened for
    a read and no new weight hashes are invented.
    """
    if (type(rank_manifests) is not tuple or len(rank_manifests) != 2
            or any(type(row) is not tuple or len(row) != 2 for row in rank_manifests)):
        raise CustodyError("Both original publisher rank manifests are required.")
    root = Path(model_root)
    config = _read_doc(root / "config.json", config_sha256)
    if (config.get("architectures") != ["Qwen3_5ForConditionalGeneration"]
            or type(config.get("text_config")) is not dict):
        raise CustodyError("Publisher-backed plan requires the original Qwen3.5 native config.")
    tied = config["text_config"].get("tie_word_embeddings", config.get("tie_word_embeddings"))
    manifests, shard_pins = [], {}
    for path, digest in rank_manifests:
        document = _read_doc(Path(path), digest)
        try:
            if set(document) != {"index_sha256", "shards", "tensors"}:
                raise ValueError
            manifest = RankShardManifest(document["index_sha256"], tuple(ShardPin(**s) for s in document["shards"]),
                                         tuple(TensorPin(**t) for t in document["tensors"]))
        except (KeyError, TypeError, ValueError) as error:
            raise CustodyError("Invalid previously sealed publisher rank data.") from error
        for pin in manifest.shards:
            if (type(pin.name) is not str or not SHARD.fullmatch(pin.name)
                    or (pin.name in shard_pins and shard_pins[pin.name] != pin)):
                raise CustodyError("Conflicting or invalid authenticated shard header pins.")
            shard_pins[pin.name] = pin
        manifests.append(manifest)
    if manifests[0].index_sha256 != manifests[1].index_sha256:
        raise CustodyError("The original ranks do not share one model index.")
    index = _read_doc(root / "model.safetensors.index.json", manifests[0].index_sha256)
    headers = {}
    try:
        weights, index_size = index["weight_map"], index["metadata"]["total_size"]
        if type(weights) is not dict or set(weights.values()) != set(shard_pins):
            raise ValueError
        for name, pin in shard_pins.items():
            with _open_regular(root / name) as buffered:
                # Read exact envelope with no read-ahead of even a payload byte.
                source = buffered.raw
                size = os.fstat(source.fileno()).st_size
                prefix = source.read(8)
                length = int.from_bytes(prefix, "little")
                if len(prefix) != 8 or size != pin.file_bytes or not 0 < length <= _META_LIMIT or 8 + length >= size:
                    raise ValueError
                raw = source.read(length)
                if len(raw) != length or hashlib.sha256(prefix + raw).hexdigest() != pin.header_sha256:
                    raise ValueError
            doc = _document(raw)
            doc.pop("__metadata__", None)
            if any(type(row) is not dict or set(row) != {"dtype", "shape", "data_offsets"}
                   or type(row["shape"]) is not list or type(row["data_offsets"]) is not list
                   or len(row["data_offsets"]) != 2 for row in doc.values()):
                raise ValueError
            headers[name] = ShardHeader(size - 8 - length, {
                key: TensorHeader(row["dtype"], tuple(row["shape"]), *row["data_offsets"])
                for key, row in doc.items()})
        plans = plan_qwen35_text_shards(weights, headers, index_payload_bytes=index_size,
                                         tied_embeddings=tied)
    except (KeyError, ValueError, TypeError, OSError) as error:
        raise CustodyError("Original publisher index/shard header proof differs from the registered model.") from error
    # The normal reader may only read its headers inside this context. It
    # additionally checks every rank's per-tensor pins and exact membership.
    for plan, manifest in zip(plans.ranks, manifests, strict=True):
        with RankTensorReader(root, plan, manifest, max_tensor_bytes=max(
                place.header.end - place.header.start for place in plan.tensors)):
            pass
    return tuple(_encode_record(asdict(rank)) for rank in plans.ranks)
