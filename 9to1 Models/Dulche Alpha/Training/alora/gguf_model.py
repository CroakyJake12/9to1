"""Explicit, CPU-first Qwen4Exp text graph backed by immutable GGUF slices.

This loader constructs the installed native CausalLM graph under meta, replaces
large frozen weights with bounded paging, and loads only small frozen state.
It grants no training permit and does not load vision, MTP, or inference services.
A returned model is an implementation candidate; construction is not evidence of
a successful native forward, training run, or equivalence to a serving backend.
"""
from __future__ import annotations

import copy
import hashlib
import json
import math
from pathlib import Path
import inspect

import numpy as np
import torch
from torch import nn
from torch.nn import functional as F
from transformers import __version__ as _TRANSFORMERS_VERSION
from transformers.models.qwen4_exp.configuration_qwen4_exp import Qwen4ExpTextConfig
from transformers.models.qwen4_exp.modeling_qwen4_exp import (
    Qwen4ExpForCausalLM, Qwen4ExpTextExperts, Qwen4ExpTextNGramEmbedding,
    Qwen4ExpTextRotaryEmbedding,
)

from .paging import PagedEmbedding, PagedLinear
from .paged_experts import PagedExperts

_MAX_RESIDENT_BYTES = 512 * 1024 * 1024
_QTYPES = {"F32", "F16", "BF16", "IQ3_S", "IQ4_XS", "IQ4_NL", "Q8_0", "Q6_K"}
_TEXT_KEYS = frozenset("""
attention_bias attention_dropout bos_token_id dtype eos_token_id
full_attention_interval hc_count hc_lowrank head_dim heads_per_ngram hidden_act
hidden_size indexer_budget indexer_compress_ratio indexer_head_dim indexer_kv_heads
indexer_n_heads initializer_range layer_types linear_conv_kernel_dim
linear_key_head_dim linear_num_key_heads linear_num_value_heads linear_value_head_dim
make_ngram_vocab_size_divisible_by mamba_ssm_dtype max_position_embeddings model_type
moe_intermediate_size mtp mtp_num_hidden_layers mtp_use_dedicated_embeddings
ngram_size ngram_vocab_size_base num_attention_heads num_experts num_experts_per_tok
num_hidden_layers num_key_value_heads output_gate_type output_router_logits
pad_token_id partial_rotary_factor ple_conv_kernel_size ple_embed_dim ple_layer_ids
rms_norm_eps rope_parameters router_aux_loss_coef seed shared_expert_intermediate_size
split_ngram_parts tie_word_embeddings use_cache vocab_size norm_topk_prob
""".split())
_TOP_KEYS = frozenset("""
architectures image_token_id language_model_only model_type text_config
tie_word_embeddings transformers_version video_token_id vision_config
vision_end_token_id vision_start_token_id
""".split())


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False).encode()


def _backend_source_manifest():
    """Pin code that reconstructs weights and executes this training backend.

    The full manifest belongs to the loader report; its canonical hash belongs
    to checkpoint identity. Test fixtures are deliberately not execution code.
    """
    from importlib.metadata import version
    from gguf import constants, gguf_reader, quants
    package = Path(__file__).resolve().parent
    alora_sources = {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(package.glob("*.py")) if not path.name.startswith("test_")
    }
    gguf_sources = {
        module.__name__.rsplit(".", 1)[-1] + ".py":
            hashlib.sha256(Path(inspect.getfile(module)).read_bytes()).hexdigest()
        for module in (constants, gguf_reader, quants)
    }
    return {"schema": 1, "alora_sources": alora_sources,
            "gguf_version": version("gguf"), "gguf_sources": gguf_sources,
            "numpy_version": str(np.__version__)}


def _configuration(config_dict):
    if type(config_dict) is not dict:
        raise ValueError("Supply the pinned JSON configuration as a dictionary")
    supplied = copy.deepcopy(config_dict)
    _canonical(supplied)
    if "text_config" in supplied:
        if set(supplied) - _TOP_KEYS or supplied.get("model_type") != "qwen4_exp":
            raise ValueError("Unknown or unsupported outer Qwen4Exp configuration")
        if supplied.get("tie_word_embeddings", False) is not False:
            raise ValueError("Tied output embeddings are outside this loader")
        raw = supplied["text_config"]
    else:
        raw = supplied
    if type(raw) is not dict or set(raw) - _TEXT_KEYS:
        raise ValueError("Unknown text configuration fields")
    if raw.get("model_type", "qwen4_exp_text") != "qwen4_exp_text":
        raise ValueError("Expected the exact Qwen4Exp text architecture")
    for key in ("num_hidden_layers", "hidden_size", "num_experts", "vocab_size"):
        if type(raw.get(key)) is not int or raw[key] <= 0:
            raise ValueError("The source must explicitly specify positive model dimensions")
    if raw["num_hidden_layers"] > 128 or raw["hidden_size"] > 16384 or raw["num_experts"] > 2048:
        raise ValueError("Model dimensions exceed this loader's bounded architecture scope")
    if (raw.get("attention_bias", False) is not False
            or raw.get("tie_word_embeddings", False) is not False
            or raw.get("hidden_act") != "silu"
            or raw.get("output_gate_type") != "sigmoid"):
        raise ValueError("This mapping requires bias-free, untied SiLU experts and sigmoid DeltaNet gates")
    if raw.get("attention_dropout", 0.0) != 0:
        raise ValueError("Only the source's deterministic zero-dropout graph is admitted")
    rope = raw.get("rope_parameters")
    if (type(rope) is not dict or rope.get("rope_type") != "default"
            or set(rope) - {"rope_type", "rope_theta", "partial_rotary_factor",
                           "mrope_section", "mrope_interleaved"}
            or rope.get("mrope_interleaved", True) is not True):
        raise ValueError("Unsupported rotary configuration")
    raw = copy.deepcopy(raw)
    config = Qwen4ExpTextConfig(**raw)
    config._attn_implementation = "eager"
    config._experts_implementation = "eager"
    config.use_cache = False
    if config.linear_key_head_dim != config.linear_value_head_dim:
        raise ValueError("The GGUF SSM metadata describes equal key/value head dimensions")
    if (config.linear_num_value_heads % config.linear_num_key_heads
            or config.indexer_kv_heads != 1 or len(config.ple_layer_ids) > 1):
        raise ValueError("Unsupported head grouping, indexer, or multiple PLE tables")
    if len(config.layer_types) != config.num_hidden_layers:
        raise ValueError("Layer-type register has the wrong length")
    return config, supplied


def _equal_metadata(metadata, key, expected):
    if key not in metadata:
        raise ValueError("Missing GGUF structural metadata: " + key)
    actual = metadata[key]
    if isinstance(expected, float):
        ok = (type(actual) in (int, float) and math.isfinite(actual)
              and math.isclose(actual, expected, rel_tol=1e-6, abs_tol=1e-12))
    else:
        ok = type(actual) is type(expected) and actual == expected
    if not ok:
        raise ValueError("GGUF configuration mismatch: " + key)


def _check_metadata(store, config):
    identity = store.identity
    metadata = identity.get("metadata") if type(identity) is dict else None
    if type(metadata) is not dict or metadata.get("general.architecture") != "qwen4exp":
        raise ValueError("A qwen4exp GGUF store identity is required")
    values = {
        "block_count": config.num_hidden_layers,
        "embedding_length": config.hidden_size,
        "context_length": config.max_position_embeddings,
        "attention.head_count": config.num_attention_heads,
        "attention.head_count_kv": config.num_key_value_heads,
        "attention.key_length": config.head_dim,
        "attention.value_length": config.head_dim,
        "attention.layer_norm_rms_epsilon": float(config.rms_norm_eps),
        "expert_count": config.num_experts,
        "expert_used_count": config.num_experts_per_tok,
        "expert_feed_forward_length": config.moe_intermediate_size,
        "expert_shared_feed_forward_length": config.shared_expert_intermediate_size,
        "hyper_connection.count": config.hc_count,
        "hyper_connection.low_rank": config.hc_lowrank,
        "ssm.conv_kernel": config.linear_conv_kernel_dim,
        "ssm.group_count": config.linear_num_key_heads,
        "ssm.state_size": config.linear_key_head_dim,
        "ssm.inner_size": config.linear_num_value_heads * config.linear_value_head_dim,
        "ssm.time_step_rank": config.linear_num_value_heads,
        "attention.indexer.head_count": config.indexer_n_heads,
        "attention.indexer.key_length": config.indexer_head_dim,
        "attention.indexer.top_k": config.indexer_budget,
        "attention.compress_ratios": [
            0 if kind == "linear_attention" else config.indexer_compress_ratio
            for kind in config.layer_types
        ],
        "rope.dimension_count": int(config.head_dim * config.rope_parameters["partial_rotary_factor"]),
        "rope.dimension_sections": list(config.rope_parameters["mrope_section"]) + [0],
        "rope.freq_base": float(config.rope_parameters["rope_theta"]),
    }
    if len(config.rope_parameters["mrope_section"]) != 3:
        raise ValueError("Exactly three native MRoPE sections are required")
    for suffix, expected in values.items():
        _equal_metadata(metadata, "qwen4exp." + suffix, expected)
    interval = metadata.get("qwen4exp.full_attention_interval")
    if (type(interval) is not int or interval <= 0
            or any((kind == "indexed_attention") != ((index + 1) % interval == 0)
                   for index, kind in enumerate(config.layer_types))):
        raise ValueError("GGUF full-attention interval differs from the native layer register")
    if "qwen4exp.attention.recurrent_layers" in metadata:
        _equal_metadata(metadata, "qwen4exp.attention.recurrent_layers",
                        [kind == "linear_attention" for kind in config.layer_types])
    if config.ple_layer_ids:
        eos = config.eos_token_id
        if type(eos) is not int:
            raise ValueError("List-valued PLE EOS has incompatible converter/native semantics")
        for suffix, expected in {
            "ple.layers": [index - 1 for index in config.ple_layer_ids],
            "ple.ngram_size": config.ngram_size,
            "ple.heads_per_ngram": config.heads_per_ngram,
            "ple.conv_kernel": config.ple_conv_kernel_size,
            "ple.eos_token_id": eos,
            "embedding_length_per_layer_input":
                config.ple_embed_dim // ((config.ngram_size - 1) * config.heads_per_ngram),
        }.items():
            _equal_metadata(metadata, "qwen4exp." + suffix, expected)
    elif any(key.startswith("qwen4exp.ple.") for key in metadata):
        raise ValueError("GGUF PLE metadata is present but the supplied text graph has no PLE")
    return identity, metadata


def tiled_head_order(key_heads, value_heads, head_dim):
    """Indices converting native grouped value heads into GGUF tiled order."""
    if (any(type(v) is not int or v <= 0 for v in (key_heads, value_heads, head_dim))
            or value_heads % key_heads):
        raise ValueError("Invalid DeltaNet value-head grouping")
    return (torch.arange(value_heads * head_dim, dtype=torch.long)
            .reshape(key_heads, value_heads // key_heads, head_dim)
            .transpose(0, 1).reshape(-1))


class _ReorderedLinear(nn.Module):
    def __init__(self, projection, *, input_order=None, output_order=None):
        super().__init__()
        self.projection = projection
        self.in_features, self.out_features = projection.in_features, projection.out_features
        self.register_buffer("input_order", input_order, persistent=False)
        self.register_buffer("output_order", output_order, persistent=False)

    def forward(self, hidden):
        if self.input_order is not None:
            hidden = hidden.index_select(-1, self.input_order)
        result = self.projection(hidden)
        return result if self.output_order is None else result.index_select(-1, self.output_order)


class _ConcatenatedLinear(nn.Module):
    def __init__(self, query, key):
        super().__init__()
        self.query, self.key = query, key
        self.in_features = query.in_features
        self.out_features = query.out_features + key.out_features

    def forward(self, hidden):
        return torch.cat((self.query(hidden), self.key(hidden)), dim=-1)


class _ResidentGate(nn.Module):
    """The GGUF shared-expert gate is a small flat vector, not a matrix ref."""
    def __init__(self, weight):
        super().__init__()
        self.weight = nn.Parameter(weight.reshape(1, -1), requires_grad=False)
        self.in_features, self.out_features = weight.numel(), 1

    def forward(self, hidden):
        return F.linear(hidden, self.weight)


class _PagedNGramEmbedding(Qwen4ExpTextNGramEmbedding):
    """Native PLE hash/cache math with the paged embedding's explicit device.

    The forward below follows installed Transformers 5.20.1 native PLE. Only the
    final embedding-device lookup differs; there is no pretend dense weight.
    """
    @classmethod
    def from_native(cls, native, embedding, metadata):
        result = cls.__new__(cls)
        nn.Module.__init__(result)
        for name in (
            "layer_idx", "ngram_size", "context_len", "heads_per_ngram", "ngram_heads",
            "ple_layer_index", "unigram_vocab_size", "ngram_vocab_size_base", "seed",
            "eos_token_id", "head_vocab_sizes", "head_offsets", "total_vocab_size",
        ):
            setattr(result, name, copy.deepcopy(getattr(native, name)))
        specs = {
            "layer_multipliers": ("ple.layer_multipliers", native.ngram_size),
            "ngram_heads_vocab_sizes": ("ple.head_vocab_sizes", native.ngram_heads),
            "ngram_heads_offsets": ("ple.head_offsets", native.ngram_heads),
        }
        for name, (suffix, length) in specs.items():
            values = metadata.get("qwen4exp." + suffix)
            if (type(values) is not list or len(values) != length
                    or any(type(v) is not int or not 0 <= v < 2**63 for v in values)):
                raise ValueError("Invalid exact PLE integer metadata: " + suffix)
            if name != "ngram_heads_offsets" and any(v == 0 for v in values):
                raise ValueError("PLE sizes and multipliers must be positive")
            result.register_buffer(name, torch.tensor(values, dtype=torch.long, device=embedding.device))
        if (metadata["qwen4exp.ple.head_vocab_sizes"] != native.head_vocab_sizes
                or metadata["qwen4exp.ple.head_offsets"] != native.head_offsets):
            raise ValueError("PLE head ranges differ from the source configuration")
        limit = (2**63 - 1) // max(native.unigram_vocab_size - 1, native.eos_token_id, 1)
        if any(v > limit for v in metadata["qwen4exp.ple.layer_multipliers"]):
            raise ValueError("PLE multiplication exceeds native signed int64 range")
        if native.total_vocab_size > embedding.num_embeddings:
            raise ValueError("PLE head ranges exceed the mapped table")
        result.ngram_embedding = embedding
        return result

    def forward(self, input_ids, past_key_values):
        input_ids = input_ids.long()
        if past_key_values is not None and past_key_values.has_previous_state(self.layer_idx, state_idx=2):
            previous_context = past_key_values.layers[self.layer_idx].conv_states[2].clone()
        else:
            previous_context = input_ids.new_full((input_ids.shape[0], self.context_len), self.eos_token_id)
        if past_key_values is not None:
            input_ids_to_cache = input_ids
            if (not past_key_values.has_previous_state(self.layer_idx, state_idx=2)
                    and input_ids.shape[1] < self.context_len):
                input_ids_to_cache = F.pad(input_ids_to_cache, (self.context_len - input_ids.shape[1], 0),
                                          value=self.eos_token_id)
            past_key_values.update_conv_state(input_ids_to_cache, self.layer_idx,
                                              state_idx=2, conv_kernel_size=self.context_len)
        token_history = torch.cat([previous_context, input_ids], dim=-1)
        shifted_tokens = [self._shift_right_ignore_eos(token_history, shift) for shift in range(self.ngram_size)]
        blocks = []
        for ngram in range(2, self.ngram_size + 1):
            start_idx = (ngram - 2) * self.heads_per_ngram
            end_idx = start_idx + self.heads_per_ngram
            mixed_ids = shifted_tokens[0] * self.layer_multipliers[0]
            for position in range(1, ngram):
                mixed_ids = torch.bitwise_xor(mixed_ids, shifted_tokens[position] * self.layer_multipliers[position])
            sizes = self.ngram_heads_vocab_sizes[start_idx:end_idx]
            offsets = self.ngram_heads_offsets[start_idx:end_idx]
            ngram_ids = torch.remainder(mixed_ids.unsqueeze(-1), sizes.view(1, 1, -1))
            blocks.append(ngram_ids + offsets.view(1, 1, -1))
        ngram_ids = torch.cat(blocks, dim=-1)[:, -input_ids.shape[1]:]
        return self.ngram_embedding(ngram_ids.to(self.ngram_embedding.device)).to(ngram_ids.device).flatten(-2)


def _replace(model, path, value):
    parent_name, _, name = path.rpartition(".")
    parent = model.get_submodule(parent_name) if parent_name else model
    setattr(parent, name, value)


def _guard_text_inputs(module, args, kwargs):
    def argument(name, position):
        return kwargs.get(name, args[position] if len(args) > position else None)
    requested_cache = argument("use_cache", 5)
    if argument("past_key_values", 3) is not None or (module.config.use_cache if requested_cache is None else requested_cache):
        raise ValueError("This CPU training candidate requires use_cache=False and no mutable cache")
    if (module.config.ple_layer_ids and argument("input_ids", 0) is None
            and argument("inputs_embeds", 4) is not None and argument("ple_input_ids", 6) is None):
        raise ValueError("Paged PLE requires explicit input_ids or ple_input_ids; reverse embedding is unsupported")


def _refuse_base_export(module, prefix, keep_vars):
    raise ValueError("Paged GGUF is not a materialized base state_dict; publish adapters through ALORA custody")


def load_text_model(store, config_dict, *, selections=None, rank=8, alpha=16,
                    device="cpu", dtype=torch.float32, chunk_rows=256):
    """Build a mapped native text graph; caller retains store lifetime/permits.

    Only CPU float32/float64 are admitted by this first loader. Configuration,
    every GGUF tensor, every native parameter and all buffers are checked. No
    pretrained/download/inference-offload path is invoked.
    """
    device = torch.device(device)
    if device.type != "cpu" or device.index is not None:
        raise ValueError("This loader is currently qualified only as a CPU candidate")
    if dtype not in (torch.float32, torch.float64):
        raise ValueError("This CPU loader requires float32 or float64 compute")
    if type(chunk_rows) is not int or chunk_rows <= 0:
        raise ValueError("chunk_rows must be a positive integer")
    if type(rank) is not int or not 1 <= rank <= 4096:
        raise ValueError("Adapter rank must be bounded and positive")
    if type(alpha) not in (int, float) or not math.isfinite(alpha) or alpha <= 0:
        raise ValueError("Adapter alpha must be finite and positive")
    config, supplied_config = _configuration(config_dict)
    source_identity, metadata = _check_metadata(store, config)
    selections = {} if selections is None else copy.deepcopy(selections)
    if type(selections) is not dict:
        raise ValueError("Selections must map native expert module paths to expert IDs")
    valid_expert_paths = {f"model.layers.{i}.mlp.experts" for i in range(config.num_hidden_layers)}
    if set(selections) - valid_expert_paths:
        raise ValueError("Selection references a noncanonical expert path")
    for ids in selections.values():
        if (type(ids) not in (list, tuple)
                or any(type(i) is not int or not 0 <= i < config.num_experts for i in ids)
                or len(set(ids)) != len(ids)):
            raise ValueError("Expert IDs must be distinct source expert indices")
    if config.ple_layer_ids and ((config.ngram_size - 1) * config.heads_per_ngram > 64
                                 or config.ngram_vocab_size_base > 20_000_000):
        raise ValueError("PLE constructor constants exceed the admitted bound")

    # Construct only metadata: no complete expert, embedding, or output matrix.
    with torch.device("meta"):
        model = Qwen4ExpForCausalLM(config)
    native_parameters = dict(model.named_parameters(remove_duplicate=False))
    native_buffers = dict(model.named_buffers(remove_duplicate=False))
    native_shapes = {name: tuple(value.shape) for name, value in native_parameters.items()}
    expected_native, expected_sources = set(), {}
    linear_specs, resident_specs, expert_specs, qsa_specs, gate_specs = [], [], [], [], []

    def source(name, shape):
        shape = tuple(shape)
        if name in expected_sources:
            raise ValueError("A GGUF tensor was mapped twice: " + name)
        info = store.tensor_info(name)
        if type(info) is not dict or tuple(info.get("shape", ())) != shape or info.get("qtype") not in _QTYPES:
            raise ValueError("GGUF tensor shape or dtype differs from its native mapping: " + name)
        expected_sources[name] = shape

    def native(name):
        if name not in native_shapes or name in expected_native:
            raise ValueError("Unknown or duplicate native parameter mapping: " + name)
        expected_native.add(name)
        return native_shapes[name]

    def linear(target, name, *, input_order=None, output_order=None):
        source(name, native(target + ".weight"))
        linear_specs.append((target, name, input_order, output_order))

    def resident(target, name, transform="direct", source_shape=None):
        shape = native(target)
        source(name, shape if source_shape is None else source_shape)
        resident_specs.append((target, name, transform, shape))

    source("token_embd.weight", native("model.embed_tokens.weight"))
    source("output.weight", native("lm_head.weight"))
    linear_specs.append(("lm_head", "output.weight", None, None))
    for part, attr in (("norm", "hc_norm"), ("down", "input_mix_weight_down"), ("up", "input_mix_weight_up")):
        target = "model.hyper_connection_mixer." + attr
        name = "output_hc_" + part + ".weight"
        if part == "norm":
            resident(target + ".weight", name, "minus_one")
        else:
            linear(target, name)
    k = config.linear_num_key_heads
    v = config.linear_num_value_heads
    d = config.linear_value_head_dim
    order = tiled_head_order(k, v, d)
    inverse_order = torch.argsort(order)
    head_inverse = torch.argsort(tiled_head_order(k, v, 1))
    qk = 2 * config.linear_key_head_dim * k
    qkv_inverse = torch.cat((torch.arange(qk), inverse_order + qk))
    ple_spec = None
    expected_buffer_names = {"model.rotary_emb.inv_freq", "model.rotary_emb.original_inv_freq"}

    for index, kind in enumerate(config.layer_types):
        base, gg = f"model.layers.{index}", f"blk.{index}."
        for family, attr in (("attn", "attn_hyper_connection"), ("ffn", "mlp_hyper_connection")):
            for part, member in (("norm", "hc_norm"), ("down", "input_mix_weight_down"),
                                 ("up", "input_mix_weight_up"), ("inject", "block_inject_weight")):
                target = base + "." + attr + "." + member
                name = gg + "hc_" + family + "_" + part + ".weight"
                if part == "norm":
                    resident(target + ".weight", name, "minus_one")
                else:
                    linear(target, name)
        resident(base + ".mlp.gate.weight", gg + "ffn_gate_inp.weight")
        expert = base + ".mlp.experts"
        gu = native(expert + ".gate_up_proj")
        down = native(expert + ".down_proj")
        expected_gu = (config.num_experts, 2 * config.moe_intermediate_size, config.hidden_size)
        if gu != expected_gu or down != (config.num_experts, config.hidden_size, config.moe_intermediate_size):
            raise ValueError("Installed native packed expert axes changed")
        for part in ("gate", "up"):
            source(gg + "ffn_" + part + "_exps.weight",
                   (config.num_experts, config.moe_intermediate_size, config.hidden_size))
        source(gg + "ffn_down_exps.weight", down)
        expert_specs.append((expert, index))
        for part in ("gate", "up", "down"):
            linear(base + ".mlp.shared_expert." + part + "_proj", gg + "ffn_" + part + "_shexp.weight")
        gate_target = base + ".mlp.shared_expert_gate"
        if native(gate_target + ".weight") != (1, config.hidden_size):
            raise ValueError("Installed native shared-expert gate shape changed")
        source(gg + "ffn_gate_inp_shexp.weight", (config.hidden_size,))
        gate_specs.append((gate_target, gg + "ffn_gate_inp_shexp.weight"))

        if kind == "linear_attention":
            prefix = base + ".linear_attn."
            linear(prefix + "in_proj_qkv", gg + "attn_qkv.weight", output_order=qkv_inverse)
            linear(prefix + "in_proj_z", gg + "attn_gate.weight", output_order=inverse_order)
            linear(prefix + "in_proj_a", gg + "ssm_alpha.weight", output_order=head_inverse)
            linear(prefix + "in_proj_b", gg + "ssm_beta.weight", output_order=head_inverse)
            linear(prefix + "out_proj", gg + "ssm_out.weight", input_order=order)
            resident(prefix + "norm.weight", gg + "ssm_norm.weight")
            resident(prefix + "A_log", gg + "ssm_a", "a_log")
            resident(prefix + "dt_bias", gg + "ssm_dt.bias", "head_vector")
            resident(prefix + "conv1d.weight", gg + "ssm_conv1d.weight", "delta_conv",
                     (qk + v * d, config.linear_conv_kernel_dim))
        elif kind == "indexed_attention":
            prefix = base + ".self_attn."
            for part, ggpart in (("q_proj", "attn_q"), ("k_proj", "attn_k"),
                                 ("v_proj", "attn_v"), ("o_proj", "attn_output")):
                linear(prefix + part, gg + ggpart + ".weight")
            for part in ("q", "k"):
                resident(prefix + part + "_norm.weight", gg + "attn_" + part + "_norm.weight", "minus_one")
                resident(prefix + "indexer." + part + "_layernorm.weight",
                         gg + "indexer." + part + "_norm.weight", "minus_one")
            fused_target = prefix + "indexer.index_qk_proj"
            fused_shape = native(fused_target + ".weight")
            query_rows = config.indexer_n_heads * config.indexer_head_dim
            key_rows = config.indexer_kv_heads * config.indexer_head_dim
            if fused_shape != (query_rows + key_rows, config.hidden_size):
                raise ValueError("Installed native indexer projection changed")
            source(gg + "indexer.q_proj.weight", (query_rows, config.hidden_size))
            source(gg + "indexer.k_proj.weight", (key_rows, config.hidden_size))
            qsa_specs.append((fused_target, gg + "indexer.q_proj.weight", gg + "indexer.k_proj.weight"))
        else:
            raise ValueError("Unknown native layer type")

        if index + 1 in config.ple_layer_ids:
            prefix = base + ".ple."
            embedding_path = prefix + "ple_embedding.ngram_embedding"
            source("per_layer_token_embd.weight", native(embedding_path + ".weight"))
            ple_spec = (prefix + "ple_embedding", "per_layer_token_embd.weight")
            for part in ("key", "value"):
                linear(prefix + part + "_proj", gg + "ple_" + part + ".weight")
            for part in ("key", "query", "conv"):
                resident(prefix + "norm_" + part + ".weight", gg + "ple_norm_" + part + ".weight", "minus_one")
            resident(prefix + "conv1d.weight", gg + "ple_conv1d.weight", "ple_conv",
                     (config.hc_count * config.hidden_size, config.ple_conv_kernel_size))
            for name in ("layer_multipliers", "ngram_heads_vocab_sizes", "ngram_heads_offsets"):
                expected_buffer_names.add(prefix + "ple_embedding." + name)

    if set(native_shapes) != expected_native:
        raise ValueError("Unmapped installed native parameters: " + repr(sorted(set(native_shapes) ^ expected_native)))
    if set(native_buffers) != expected_buffer_names:
        raise ValueError("Unmapped installed native buffers: " + repr(sorted(set(native_buffers) ^ expected_buffer_names)))
    names = store.tensor_names
    if not isinstance(names, tuple) or len(names) != len(set(names)) or set(names) != set(expected_sources):
        raise ValueError("GGUF tensor inventory contains missing or unmapped tensors")
    resident_bytes = sum(math.prod(shape) * torch.empty((), dtype=dtype).element_size()
                         for _, _, _, shape in resident_specs)
    resident_bytes += len(gate_specs) * config.hidden_size * torch.empty((), dtype=dtype).element_size()
    if resident_bytes > _MAX_RESIDENT_BYTES:
        raise ValueError("Small frozen resident parameters exceed the total resident budget")

    def decoded(name):
        info = store.tensor_info(name)
        array = store.read_tensor(name)
        if (type(array) is not np.ndarray or array.dtype != np.float32
                or array.shape != tuple(info["shape"]) or not array.flags.c_contiguous
                or not array.flags.owndata or not np.isfinite(array).all()):
            raise ValueError("Store returned invalid owned small-tensor bytes")
        return torch.from_numpy(array).to(device=device, dtype=dtype)

    def paged(name):
        return PagedLinear(store, name, device=device, dtype=dtype, chunk_rows=chunk_rows)

    # All mutations below belong to this private, newly constructed candidate.
    for target, name, input_order, output_order in linear_specs:
        projection = paged(name)
        if input_order is not None or output_order is not None:
            projection = _ReorderedLinear(projection, input_order=input_order, output_order=output_order)
        _replace(model, target, projection)
    for target, query_name, key_name in qsa_specs:
        _replace(model, target, _ConcatenatedLinear(paged(query_name), paged(key_name)))
    for target, name in gate_specs:
        _replace(model, target, _ResidentGate(decoded(name)))
    for target, name, transform, shape in resident_specs:
        value = decoded(name)
        if transform == "minus_one":
            value = value - 1
        elif transform in ("a_log", "head_vector"):
            value = value.index_select(0, head_inverse)
            if transform == "a_log":
                if not bool((value < 0).all()):
                    raise ValueError("GGUF SSM A must contain finite negative exponentials")
                value = (-value).log()
        elif transform == "delta_conv":
            value = value.index_select(0, qkv_inverse).unsqueeze(1)
        elif transform == "ple_conv":
            value = value.unsqueeze(1)
        if tuple(value.shape) != shape or not bool(torch.isfinite(value).all()):
            raise ValueError("Small parameter inverse transform failed: " + target)
        _replace(model, target, nn.Parameter(value.contiguous(), requires_grad=False))
    _replace(model, "model.embed_tokens", PagedEmbedding(store, "token_embd.weight", device=device, dtype=dtype))
    if ple_spec is not None:
        target, name = ple_spec
        native_ngram = model.get_submodule(target)
        replacement = _PagedNGramEmbedding.from_native(
            native_ngram, PagedEmbedding(store, name, device=device, dtype=dtype), metadata)
        _replace(model, target, replacement)
    model.model.rotary_emb = Qwen4ExpTextRotaryEmbedding(config)
    for target, index in expert_specs:
        _replace(model, target, PagedExperts(store, index, expert_ids=selections.get(target, ()),
                                             rank=rank, alpha=alpha, device=device, dtype=dtype))
    model.model.register_forward_pre_hook(_guard_text_inputs, with_kwargs=True)
    model.register_state_dict_pre_hook(_refuse_base_export)
    for name, value in list(model.named_parameters()) + list(model.named_buffers()):
        if value.device.type == "meta" or value.device != device:
            raise ValueError("Unmaterialized or misplaced candidate state: " + name)
    if any(isinstance(module, (nn.Linear, nn.Embedding, Qwen4ExpTextExperts))
           for module in model.modules()):
        raise ValueError("An unsupported dense native weight module remains")
    allowed_trainable = {id(parameter)
                         for module in model.modules() if isinstance(module, PagedExperts)
                         for parameter in module.parameters() if parameter.requires_grad}
    if any(p.requires_grad and id(p) not in allowed_trainable for p in model.parameters()):
        raise ValueError("A frozen base parameter was left trainable")
    backend_manifest = _backend_source_manifest()
    model.alora_loader_report = {
        "schema": 1, "kind": "qwen4exp-gguf-native-text-candidate",
        "source_identity": store.source_identity,
        "source_attestation": copy.deepcopy(source_identity),
        "config_sha256": hashlib.sha256(_canonical(supplied_config)).hexdigest(),
        "execution_config_sha256": hashlib.sha256(_canonical(model.config.to_dict())).hexdigest(),
        "backend_source_manifest": backend_manifest,
        "backend_source_sha256": hashlib.sha256(_canonical(backend_manifest)).hexdigest(),
        "transformers_version": _TRANSFORMERS_VERSION,
        "native_class": type(model).__name__,
        "native_modeling_sha256": hashlib.sha256(Path(inspect.getfile(Qwen4ExpForCausalLM)).read_bytes()).hexdigest(),
        "native_configuration_sha256": hashlib.sha256(Path(inspect.getfile(Qwen4ExpTextConfig)).read_bytes()).hexdigest(),
        "native_parameter_count": len(expected_native),
        "mapped_tensor_count": len(expected_sources),
        "mapped_tensor_names": sorted(expected_sources),
        "resident_parameter_bytes": resident_bytes,
        "device": str(device), "dtype": str(dtype), "chunk_rows": chunk_rows,
        "excluded_components": ["vision", "mtp"],
        "forward_verified": False, "training_verified": False,
        "base_export_supported": False,
    }
    return model
