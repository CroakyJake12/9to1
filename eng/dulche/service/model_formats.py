"""Read model containers as data. Never import code from a model directory."""

import json
import math
import re
import stat
import struct
import sys
from pathlib import Path, PurePosixPath

# Immutable service releases bundle the validators beside this module. Source checkouts
# reuse the broker's original package without relying on the caller's working directory.
HERE = Path(__file__).resolve().parent
if (HERE / "bundle/formats.py").is_file() and (HERE / "llamacpp/gguf.py").is_file():
    validator_root = HERE
elif HERE.parts[-3:] == ("eng", "dulche", "service"):
    validator_root = HERE.parents[2] / "9to1 Workspace/Home/Source/Dulche"
    if not all(
        (validator_root / rel).is_file()
        for rel in ("bundle/formats.py", "llamacpp/gguf.py")
    ):
        raise ImportError("The 9to1 checkout lacks the Dulche model validators")
else:
    raise ImportError("The frozen Dulche release lacks its bundled model validators")
if not sys.path or sys.path[0] != str(validator_root):
    sys.path.insert(0, str(validator_root))
from bundle.formats import GGUF_PLAIN_TYPE_BYTES, GGUF_SCALAR_SIZE, sniff_safetensors
from llamacpp.gguf import read_gguf_version

MAX_PARAMETERS = 500_000_000_000
MAX_HEADER = 128 * 1024**2
SHARD = re.compile(r"^(.*)-(\d{5})-of-(\d{5})\.gguf$", re.IGNORECASE)
MODEL_ARCHES = frozenset(
    [
        "llama",
        "qwen2",
        "qwen2moe",
        "qwen3",
        "qwen3moe",
        "qwen3next",
        "qwen35",
        "qwen35moe",
        "qwen4exp",
        "gemma",
        "gemma2",
        "gemma3",
        "gemma4",
        "mistral3",
        "ministral3",
        "deepseek2",
        "glm4",
        "glm4moe",
        "glm4moelite",
        "gpt-oss",
        "phi3",
        "granite",
        "nemotron",
        "nemotron_h",
    ]
)
# ggml on-disk block element counts and storage widths. Unsupported tensor types fail closed.
BLOCKS = {
    **{k: (1, v) for k, v in GGUF_PLAIN_TYPE_BYTES.items()},
    2: (32, 18),
    3: (32, 20),
    6: (32, 22),
    7: (32, 24),
    8: (32, 34),
    9: (32, 36),
    10: (256, 84),
    11: (256, 110),
    12: (256, 144),
    13: (256, 176),
    14: (256, 210),
    15: (256, 292),
    16: (256, 66),
    17: (256, 74),
    18: (256, 98),
    19: (256, 50),
    20: (32, 18),
    21: (256, 110),
    22: (256, 82),
    23: (256, 136),
    29: (256, 56),
    34: (256, 54),
    35: (256, 66),
    39: (32, 17),
    40: (64, 36),
    41: (128, 18),
    42: (64, 18),
}
QUANTS = re.compile(
    r"(?:UD[-_])?(?:I?Q[1-8](?:[_-](?:K|[0-9]|XS|XXS|NL|M|S|L|XL))*|BF16|F16|F32|MXFP4|NVFP4)",
    re.IGNORECASE,
)
AUX_FILES = {
    "config.json",
    "generation_config.json",
    "tokenizer.json",
    "tokenizer_config.json",
    "tokenizer.model",
    "vocab.json",
    "vocab.txt",
    "merges.txt",
    "special_tokens_map.json",
    "added_tokens.json",
    "chat_template.jinja",
    "model.safetensors.index.json",
    "adapter_config.json",
    "preprocessor_config.json",
    "processor_config.json",
    "sentencepiece.bpe.model",
    "spiece.model",
    "license",
    "license.md",
    "license.txt",
    "readme.md",
}


def quant(name):
    matches = list(QUANTS.finditer(name))
    return matches[-1].group().upper() if matches else None


def safe_relative(name):
    path = PurePosixPath(name)
    if (
        not name
        or any(ord(c) < 32 for c in name)
        or "\\" in name
        or ":" in name
        or path.is_absolute()
        or ".." in path.parts
        or not path.parts
        or str(path) != name
    ):
        raise ValueError("unsafe relative artifact path")
    return path


def artifact_file(name):
    p = safe_relative(name)
    return p.suffix.lower() in (".gguf", ".safetensors") or p.name.lower() in AUX_FILES


def json_file(path, limit=8 * 1024**2):
    if not path.is_file() or path.stat().st_size > limit:
        raise ValueError(f"missing or oversized {path.name}")
    obj = json.loads(path.read_bytes())
    if not isinstance(obj, dict):
        raise TypeError(f"{path.name} must be a JSON object")
    return obj


def gguf_shard(path):
    read_gguf_version(path)
    size = path.stat().st_size
    with path.open("rb") as f:
        return gguf_header(f, size)


def gguf_header(f, size, *, tensor_bytes=None):
    """Compute logical parameter count and structural bounds from the weight-free prefix."""
    end = min(size, MAX_HEADER)

    def take(n):
        if n < 0 or f.tell() + n > end:
            raise ValueError("GGUF header exceeds file or validation limit")
        data = f.read(n)
        if len(data) != n:
            raise ValueError("truncated GGUF header")
        return data

    def u32():
        return struct.unpack("<I", take(4))[0]

    def u64():
        return struct.unpack("<Q", take(8))[0]

    def string(decode):
        n = u64()
        if n > 16 * 1024**2 or f.tell() + n > end:
            raise ValueError("invalid GGUF string")
        if decode:
            return take(n).decode("utf-8")
        f.seek(n, 1)

    if take(4) != b"GGUF" or u32() not in (2, 3):
        raise ValueError("unsupported GGUF magic or version")
    n_tensors, n_meta = u64(), u64()
    if not 0 <= n_tensors <= 200000 or n_meta > 100000:
        raise ValueError("invalid GGUF table sizes")
    meta = {}
    for _ in range(n_meta):
        key = string(True)
        if key in meta:
            raise ValueError("duplicate GGUF metadata")
        typ = u32()
        if typ == 8:
            value = string(key.startswith(("general.", "adapter.")))
        elif typ == 9:
            elem, n = u32(), u64()
            if n > 5_000_000:
                raise ValueError("oversized GGUF metadata array")
            if elem == 8:
                for _ in range(n):
                    string(False)
            elif elem in GGUF_SCALAR_SIZE:
                length = n * GGUF_SCALAR_SIZE[elem]
                if f.tell() + length > end:
                    raise ValueError("truncated GGUF metadata array")
                f.seek(length, 1)
            else:
                raise ValueError("unsupported GGUF metadata array type")
            value = None
        elif typ in GGUF_SCALAR_SIZE:
            value = int.from_bytes(take(GGUF_SCALAR_SIZE[typ]), "little")
        else:
            raise ValueError("unsupported GGUF metadata type")
        meta[key] = value
    # llama.cpp permits a metadata-only first shard. Validate the sequence and
    # declared nonempty tensor set; standalone and later empty shards remain invalid.
    if n_tensors == 0 and not (
        meta.get("split.no") == 0
        and type(meta.get("split.count")) is int
        and 1 < meta["split.count"] <= 1024
        and type(meta.get("split.tensors.count")) is int
        and meta["split.tensors.count"] > 0
        and isinstance(meta.get("general.architecture"), str)
        and meta["general.architecture"]
    ):
        raise ValueError(
            "empty GGUF is not the metadata member of a nonempty shard group"
        )
    alignment = meta.get("general.alignment", 32)
    if (
        not isinstance(alignment, int)
        or alignment <= 0
        or alignment > 65536
        or alignment & (alignment - 1)
    ):
        raise ValueError("invalid GGUF alignment")
    ranges = []
    names = set()
    total = 0
    for _ in range(n_tensors):
        name = string(True)
        rank = u32()
        if name in names or not 1 <= rank <= 4:
            raise ValueError("invalid GGUF tensor table")
        names.add(name)
        shape = [u64() for _ in range(rank)]
        typ = u32()
        start = u64()
        if min(shape) == 0 or typ not in BLOCKS:
            raise ValueError("unsupported GGUF tensor dimensions or type")
        block, width = BLOCKS[typ]
        elements = math.prod(shape)
        if shape[0] % block or start % alignment:
            raise ValueError("misaligned GGUF tensor")
        byte_count = elements // block * width
        ranges.append((start, start + byte_count))
        if tensor_bytes is not None:
            tensor_bytes[name] = byte_count
        total += elements
    data_start = (f.tell() + alignment - 1) // alignment * alignment
    previous = 0
    for start, stop in sorted(ranges):
        if start < previous or data_start + stop > size:
            raise ValueError("overlapping or truncated GGUF tensor")
        previous = stop
    return meta, names, total


def shard_paths(path):
    match = SHARD.fullmatch(path.name)
    if not match:
        return [path]
    prefix, _, count = match.groups()
    count = int(count)
    if not 1 <= count <= 1024:
        raise ValueError("invalid GGUF shard count")
    paths = [
        path.with_name(f"{prefix}-{i:05}-of-{count:05}.gguf")
        for i in range(1, count + 1)
    ]
    if not all(p.is_file() for p in paths):
        raise ValueError("multipart GGUF has missing shards")
    return paths


def check_tokenizer(path):
    if (path / "tokenizer_config.json").exists() and json_file(
        path / "tokenizer_config.json"
    ).get("auto_map"):
        raise ValueError("custom tokenizer code is not enabled")
    if (path / "tokenizer.json").exists():
        tokenizer = json_file(path / "tokenizer.json", MAX_HEADER)
        if not isinstance(tokenizer.get("model"), dict) or not tokenizer["model"].get(
            "type"
        ):
            raise ValueError("tokenizer.json is missing a tokenizer model")
        return
    for name in (
        "tokenizer.model",
        "spiece.model",
        "sentencepiece.bpe.model",
        "vocab.txt",
    ):
        candidate = path / name
        if candidate.is_file() and candidate.stat().st_size:
            return
    if (
        (path / "vocab.json").is_file()
        and (path / "merges.txt").is_file()
        and (
            json_file(path / "vocab.json", MAX_HEADER)
            and (path / "merges.txt").stat().st_size
        )
    ):
        return
    raise ValueError("missing or empty tokenizer assets")


def check_lora(config, tensors):
    pairs = {}
    for key, tensor in tensors.items():
        match = re.fullmatch(r"(.+)\.lora_([AB])(?:\.default)?\.weight", key)
        if match:
            pairs.setdefault(match[1], {})[match[2]] = tensor["shape"]
    if not pairs or not config.get("target_modules"):
        raise ValueError("adapter requires target modules and LoRA weight pairs")
    for name, pair in pairs.items():
        a, b = pair.get("A", []), pair.get("B", [])
        rank = (config.get("rank_pattern") or {}).get(name, config.get("r"))
        if (
            len(a) != 2
            or len(b) != 2
            or type(rank) is not int
            or a[0] != rank
            or b[1] != rank
            or rank <= 0
        ):
            raise ValueError("invalid LoRA rank or incomplete tensor pair")


def inspect_model(path, base=None, logical_parameters=None):
    path = Path(path).resolve(strict=True)
    if (
        path.is_dir()
        and not (path / "config.json").is_file()
        and not (path / "adapter_config.json").is_file()
    ):
        files = sorted(path.glob("*.gguf"))
        if not files:
            raise ValueError("directory contains no supported model")
        if set(shard_paths(files[0])) != set(files):
            raise ValueError("choose one GGUF checkpoint or its first shard")
        path = files[0]
    if path.is_file() and path.suffix.lower() == ".gguf":
        parts = shard_paths(path)
        meta = {}
        total = 0
        all_names = set()
        for i, part in enumerate(parts):
            m, names, count = gguf_shard(part)
            if i == 0:
                meta = m
            if len(parts) > 1 and (
                m.get("split.no") != i or m.get("split.count") != len(parts)
            ):
                raise ValueError(
                    "multipart GGUF metadata disagrees with shard sequence"
                )
            if len(parts) == 1 and m.get("split.count", 1) > 1:
                raise ValueError("missing multipart GGUF files")
            if all_names & names or m.get(
                "general.architecture", meta.get("general.architecture")
            ) != meta.get("general.architecture"):
                raise ValueError("inconsistent or duplicate GGUF shard content")
            total += count
            all_names.update(names)
        if "split.tensors.count" in meta and meta["split.tensors.count"] != len(
            all_names
        ):
            raise ValueError("incomplete tensor set")
        arch = meta.get("general.architecture")
        if not isinstance(arch, str) or not arch:
            raise ValueError("missing GGUF architecture")
        fmt = "gguf"
        name = meta.get("general.name") or parts[0].stem
        model_path = parts[0]
        if meta.get("general.type") == "adapter":
            raise ValueError("use a PEFT adapter directory with a declared base model")
        backend = "llama.cpp" if arch in MODEL_ARCHES else None
        quantization = quant(path.name) or str(meta.get("general.file_type", "unknown"))
        context = meta.get(arch + ".context_length")
        size = sum(p.stat().st_size for p in parts)
    elif path.is_dir():
        adapter = (path / "adapter_config.json").exists()
        config = json_file(path / ("adapter_config.json" if adapter else "config.json"))
        if config.get("auto_map"):
            raise ValueError("custom repository code is not enabled")
        if adapter:
            if config.get("peft_type") != "LORA" or not base:
                raise ValueError(
                    "select the registered base model for a LoRA/QLoRA adapter"
                )
            expected = config.get("base_model_name_or_path")
            identifiers = {
                base.get("repo"),
                base.get("name"),
                base.get("path"),
                base.get("id"),
            }
            if not expected or expected not in identifiers:
                raise ValueError("adapter base does not match the selected model")
            arch = base["architecture"]
            parts = [path / "adapter_model.safetensors"]
            fmt = "adapter"
        else:
            arch = config.get("model_type")
            if not isinstance(arch, str) or not arch:
                raise ValueError("missing model_type")
            check_tokenizer(path)
            index = path / "model.safetensors.index.json"
            if index.exists():
                weights = json_file(index).get("weight_map")
                if not isinstance(weights, dict) or not weights:
                    raise ValueError("invalid SafeTensors index")
                parts = [
                    path / str(safe_relative(f)) for f in sorted(set(weights.values()))
                ]
            else:
                parts = [path / "model.safetensors"]
            fmt = "safetensors"
        total = 0
        for part in parts:
            if part.suffix != ".safetensors" or not part.is_file():
                raise ValueError("missing SafeTensors shard")
            sniff_safetensors(part)
            with part.open("rb") as f:
                header_len = int.from_bytes(f.read(8), "little")
                if not 0 < header_len <= MAX_HEADER:
                    raise ValueError("invalid SafeTensors header length")
                header = json.loads(f.read(header_len))
            tensors = {k: v for k, v in header.items() if k != "__metadata__"}
            if not tensors:
                raise ValueError("empty SafeTensors checkpoint")
            if adapter:
                check_lora(config, tensors)
            if (
                not adapter
                and index.exists()
                and set(tensors)
                != {
                    k
                    for k, v in weights.items()
                    if v == part.relative_to(path).as_posix()
                }
            ):
                raise ValueError("tensor index disagrees with shard contents")
            total += sum(math.prod(v["shape"]) for v in tensors.values())
        packed = config.get("quantization_config") or (
            config.get("text_config") or {}
        ).get("quantization_config")
        if packed:
            if type(logical_parameters) is not int or logical_parameters <= 0:
                raise ValueError(
                    "packed local SafeTensors require a verified logical parameter count"
                )
            total = max(total, logical_parameters)
        backend = None
        quantization = (packed or {}).get("quant_method")
        size = sum(p.stat().st_size for p in parts)
        name = path.name
        model_path = path
        context = (config.get("text_config") or config).get("max_position_embeddings")
    else:
        raise ValueError(
            "select a GGUF, model directory, PEFT adapter directory, or compatible ZIP"
        )
    if total > MAX_PARAMETERS:
        raise ValueError("checkpoint exceeds 500B total-parameter ceiling")
    return {
        "name": name,
        "path": str(model_path),
        "format": fmt,
        "architecture": arch,
        "backend": backend,
        "compatibility": "known architecture; runtime probe required"
        if backend
        else "requires a compatible runtime adapter",
        "parameters": total,
        "quantization": quantization,
        "bytes": size,
        "context_length": context,
        "shards": len(parts),
        "base_model_id": base["id"] if base and fmt == "adapter" else None,
    }


def archive_members(archive):
    seen = set()
    admitted = []
    total = 0
    if len(archive.infolist()) > 4096:
        raise ValueError("archive contains too many entries")
    for item in archive.infolist():
        name = item.filename.rstrip("/") if item.is_dir() else item.filename
        safe_relative(name)
        normalized = name.casefold()
        mode = stat.S_IFMT(item.external_attr >> 16)
        if (
            normalized in seen
            or mode not in (0, stat.S_IFREG, stat.S_IFDIR)
            or item.flag_bits & 1
        ):
            raise ValueError("duplicate, encrypted or special archive member")
        seen.add(normalized)
        if item.is_dir():
            continue
        if not artifact_file(name):
            raise ValueError("archive contains unsupported or executable content")
        if item.file_size > 200 * 1024**3 or item.file_size > max(
            2 * 1024**2, 200 * item.compress_size
        ):
            raise ValueError("archive exceeds expansion limits")
        total += item.file_size
        if total > 400 * 1024**3:
            raise ValueError("archive uncompressed size exceeds limit")
        admitted.append(item)
    return admitted, total
