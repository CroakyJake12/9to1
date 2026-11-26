"""Read stored weights by layer before admitting a partial V100 residency experiment."""

from pathlib import Path
import re
from model_formats import gguf_header, shard_paths

MIB = 2**20


def layer_budget(path, layers, context, kv_type):
    blocks = {}
    shared = 0
    meta = {}
    for part in shard_paths(Path(path)):
        sizes = {}
        with part.open("rb") as f:
            header, _, _ = gguf_header(f, part.stat().st_size, tensor_bytes=sizes)
        if not meta:
            meta = header
        for name, size in sizes.items():
            group = re.match(r"blk\.(\d+)\.", name)
            if group:
                number = int(group[1])
                blocks[number] = blocks.get(number, 0) + size
            else:
                shared += size
    if not blocks or not 1 <= layers <= len(blocks):
        raise ValueError("GPU layer count exceeds the stored layer table")
    # Includes the heaviest possible layers, including any unused auxiliary blocks.
    gpu_weights = sum(sorted(blocks.values(), reverse=True)[:layers])
    arch = meta.get("general.architecture")
    prefix = str(arch) + "."
    head_count = meta.get(prefix + "attention.head_count")
    heads = meta.get(prefix + "attention.head_count_kv")
    head_dim = meta.get(prefix + "attention.key_length")
    if not head_dim and type(head_count) is int and head_count > 0:
        head_dim = meta.get(prefix + "embedding_length", 0) // head_count
    if type(heads) is not int or type(head_dim) is not int or min(heads, head_dim) <= 0:
        raise ValueError(
            "The GGUF lacks scalar attention metadata for a bounded partial trial"
        )
    byte_factor = {"q4_0": 18 / 32, "q8_0": 34 / 32, "f16": 2}[kv_type]
    kv = int(2 * heads * head_dim * layers * context * byte_factor)
    reserve = 512 * MIB
    # The installed llama.cpp 3cf03257f uses a contiguous suffix of
    # n_layer_all + 1 (including output) for -ngl. Qwen35 recurrent
    # layers do not allocate context-length attention KV. Qualify only
    # this exact 65-block layout; otherwise retain the generic upper bound.
    canonical = Path.home() / "Documents/Default Project/models/Qwen3.8-27B-UD-IQ4_XS.gguf"
    try:
        same = Path(path).samefile(canonical)
    except OSError:
        same = False
    attention_layers = None
    if (same and arch == "qwen35" and meta.get(prefix + "block_count") == 65
            and meta.get(prefix + "nextn_predict_layers") == 1
            and meta.get(prefix + "full_attention_interval") == 4
            and prefix + "attention.recurrent_layers" not in meta
            and set(blocks) == set(range(65)) and heads == 4 and head_dim == 256
            and meta.get(prefix + "attention.value_length") == 256):
        suffix = range(66 - layers, 65)
        attention_layers = sum(i == 64 or (i + 1) % 4 == 0 for i in suffix)
        # Include even the unused MTP block and all shared tensors; the
        # measured runtime guard still enforces the physical ceiling.
        gpu_weights = sum(blocks[i] for i in suffix) + shared
        kv = int(2 * heads * head_dim * attention_layers * context * byte_factor)
        reserve = 1536 * MIB
    return {
        "layers": layers,
        "attention_layers_upper": attention_layers,
        "total_layer_groups": len(blocks),
        "weight_upper_mib": (gpu_weights + MIB - 1) // MIB,
        "attention_kv_upper_mib": (kv + MIB - 1) // MIB,
        "workspace_allowance_mib": reserve // MIB,
        "estimated_vram_mib": (gpu_weights + kv + reserve + MIB - 1) // MIB,
        "estimated_cpu_weight_mib": (
            sum(blocks.values())
            + shared
            - sum(sorted(blocks.values())[:layers])
            + MIB
            - 1
        )
        // MIB,
    }


def long_context_host_ceiling(path, context, kv_type):
    """Conservative host cgroup budget for a Qwen hybrid long-context profile.

    Allow the entire mapped artifact, all global KV, and 4 GiB of workspace.
    Unknown architectures use all blocks as attention blocks.
    """
    import math
    p = Path(path)
    with p.open("rb") as f:
        meta, _, _ = gguf_header(f, p.stat().st_size)
    arch = meta.get("general.architecture")
    groups = meta.get(str(arch) + ".block_count")
    if type(groups) is not int or groups < 1:
        raise ValueError("Missing block count for long-context memory estimate")
    interval = meta.get(str(arch) + ".full_attention_interval")
    attention = math.ceil(groups / interval) if arch == "qwen35" and type(interval) is int and interval > 0 else groups
    prefix = str(arch) + "."
    heads = meta.get(prefix + "attention.head_count_kv")
    dim = meta.get(prefix + "attention.key_length")
    if type(heads) is not int or type(dim) is not int or min(heads, dim) <= 0:
        kv_upper = layer_budget(p, groups, context, kv_type)["attention_kv_upper_mib"]
    else:
        factor = {"q4_0":18/32,"q8_0":34/32,"f16":2}[kv_type]
        kv_upper = math.ceil(2 * heads * dim * attention * context * factor / MIB)
    weight_mib = (sum(x.stat().st_size for x in shard_paths(p)) + MIB - 1) // MIB
    return weight_mib + kv_upper + 4096
