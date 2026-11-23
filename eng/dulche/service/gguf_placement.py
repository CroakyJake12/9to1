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
    return {
        "layers": layers,
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
