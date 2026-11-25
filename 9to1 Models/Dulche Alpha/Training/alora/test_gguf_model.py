"""CPU-only native graph tests using numeric fixtures, not a training corpus."""
import copy
from dataclasses import dataclass
import hashlib
import json
import unittest

import numpy as np
from hf_backend.cpu_guard import install

_GUARD = install()

import torch
from torch import nn
from transformers.models.qwen4_exp.configuration_qwen4_exp import Qwen4ExpTextConfig
from transformers.models.qwen4_exp.modeling_qwen4_exp import Qwen4ExpForCausalLM

from .gguf_model import load_text_model, tiled_head_order
from .paged_experts import PagedExperts


@dataclass(frozen=True)
class _Ref:
    tensor_name: str
    expert: int | None
    row_start: int
    row_count: int
    shape: tuple
    qtype: str
    source_identity: str


class _MemoryStore:
    """Owned F32 protocol fixture; conversion runs in the opposite direction."""
    def __init__(self, tensors, metadata):
        self.tensors = {name: np.array(value.detach().cpu().float().numpy(), copy=True, order="C")
                        for name, value in tensors.items()}
        self.metadata = copy.deepcopy(metadata)
        digest = hashlib.sha256(json.dumps(metadata, sort_keys=True).encode())
        for name, array in sorted(self.tensors.items()):
            digest.update(name.encode())
            digest.update(array.tobytes())
        self.source_identity = digest.hexdigest()
        self.max_decoded_bytes = 1024 * 1024
        self.max_read_bytes = 1024 * 1024
        self.reads = []

    @property
    def identity(self):
        return {"schema": 1, "format": "software-test-memory-GGUF-protocol",
                "metadata": copy.deepcopy(self.metadata), "source": self.source_identity}

    @property
    def tensor_names(self):
        return tuple(sorted(self.tensors))

    def tensor_info(self, name):
        if name not in self.tensors:
            raise ValueError("Unknown fixture tensor")
        array = self.tensors[name]
        return {"shape": array.shape, "qtype": "F32", "bytes": array.nbytes, "shard": "numeric-fixture"}

    def matrix_ref(self, name, *, expert=None, row_start=0, row_count=None):
        shape = self.tensor_info(name)["shape"]
        if len(shape) == 3:
            if type(expert) is not int or not 0 <= expert < shape[0]:
                raise ValueError("Packed fixture requires an expert")
            shape = shape[1:]
        elif len(shape) != 2 or expert is not None:
            raise ValueError("Expected fixture matrix")
        rows, columns = shape
        row_count = rows - row_start if row_count is None else row_count
        if not 0 <= row_start < rows or not 0 < row_count <= rows - row_start:
            raise ValueError("Invalid fixture row range")
        if row_count * columns * 4 > self.max_decoded_bytes:
            raise ValueError("Fixture matrix byte bound")
        return _Ref(name, expert, row_start, row_count, (row_count, columns), "F32", self.source_identity)

    def read(self, ref):
        if ref != self.matrix_ref(ref.tensor_name, expert=ref.expert,
                                  row_start=ref.row_start, row_count=ref.row_count):
            raise ValueError("Fixture reference mismatch")
        array = self.tensors[ref.tensor_name]
        if ref.expert is not None:
            array = array[ref.expert]
        self.reads.append((ref.tensor_name, ref.expert, ref.row_start, ref.row_count))
        return np.array(array[ref.row_start:ref.row_start + ref.row_count], copy=True, order="C")

    def read_tensor(self, name):
        array = self.tensors[name]
        if array.nbytes > self.max_decoded_bytes:
            raise ValueError("Fixture tensor byte bound")
        self.reads.append((name, "resident"))
        return np.array(array, copy=True, order="C")

    def gather_rows(self, name, indices):
        return np.array(self.tensors[name][indices], copy=True, order="C")


def _tiny_config():
    return {
        "model_type": "qwen4_exp_text", "dtype": "float32",
        "vocab_size": 32, "hidden_size": 8, "num_hidden_layers": 2,
        "num_attention_heads": 2, "num_key_value_heads": 1, "head_dim": 4,
        "num_experts": 3, "num_experts_per_tok": 2,
        "moe_intermediate_size": 8, "shared_expert_intermediate_size": 8,
        "hidden_act": "silu", "output_gate_type": "sigmoid",
        "attention_bias": False, "attention_dropout": 0.0, "tie_word_embeddings": False,
        "linear_key_head_dim": 2, "linear_value_head_dim": 2,
        "linear_num_key_heads": 2, "linear_num_value_heads": 6,
        "linear_conv_kernel_dim": 4,
        "layer_types": ["linear_attention", "full_attention"], "full_attention_interval": 2,
        "hc_count": 2, "hc_lowrank": 3,
        "indexer_n_heads": 1, "indexer_kv_heads": 1, "indexer_head_dim": 4,
        "indexer_budget": 2, "indexer_compress_ratio": 2,
        "ple_layer_ids": [1], "ple_embed_dim": 8, "ple_conv_kernel_size": 4,
        "ngram_size": 3, "heads_per_ngram": 1, "ngram_vocab_size_base": 5,
        "make_ngram_vocab_size_divisible_by": 4, "split_ngram_parts": 1,
        "seed": 1234, "eos_token_id": 2, "bos_token_id": 2, "pad_token_id": None,
        "max_position_embeddings": 64, "rms_norm_eps": 1e-6,
        "rope_parameters": {"rope_type": "default", "rope_theta": 10000.0,
                            "partial_rotary_factor": 1.0, "mrope_interleaved": True,
                            "mrope_section": [1, 1, 0]},
        "use_cache": False, "output_router_logits": False,
    }


def _forward_convert(native, raw_config):
    """Mirror the inspected converter's HF-to-GGUF format, independently of loader maps."""
    c = native.config
    tensors = {}
    k, r = c.linear_num_key_heads, c.linear_num_value_heads // c.linear_num_key_heads
    qk = c.linear_key_head_dim * k * 2

    def tile(value, axis=0, head_dim=None):
        head_dim = c.linear_value_head_dim if head_dim is None else head_dim
        transposed = value.movedim(axis, 0)
        reordered = transposed.reshape(k, r, head_dim, *transposed.shape[1:]).transpose(0, 1)
        return reordered.reshape(transposed.shape).movedim(0, axis).contiguous()

    hc = {"hc_norm": "norm", "input_mix_weight_down": "down",
          "input_mix_weight_up": "up", "block_inject_weight": "inject"}
    for name, value in native.named_parameters():
        value = value.detach()
        if name == "model.embed_tokens.weight":
            tensors["token_embd.weight"] = value
        elif name == "lm_head.weight":
            tensors["output.weight"] = value
        elif name.startswith("model.hyper_connection_mixer."):
            part = hc[name.split(".")[2]]
            tensors["output_hc_" + part + ".weight"] = value + 1 if part == "norm" else value
        else:
            bits = name.split(".")
            assert bits[:2] == ["model", "layers"], name
            index, tail = int(bits[2]), ".".join(bits[3:])
            gg = f"blk.{index}."
            if tail == "mlp.experts.gate_up_proj":
                gate, up = value.chunk(2, dim=1)
                tensors[gg + "ffn_gate_exps.weight"] = gate
                tensors[gg + "ffn_up_exps.weight"] = up
            elif tail == "mlp.experts.down_proj":
                tensors[gg + "ffn_down_exps.weight"] = value
            elif tail == "mlp.gate.weight":
                tensors[gg + "ffn_gate_inp.weight"] = value
            elif tail == "mlp.shared_expert_gate.weight":
                tensors[gg + "ffn_gate_inp_shexp.weight"] = value.squeeze(0)
            elif tail.startswith("mlp.shared_expert."):
                part = tail.split(".")[2].removesuffix("_proj")
                tensors[gg + "ffn_" + part + "_shexp.weight"] = value
            elif tail.startswith(("attn_hyper_connection.", "mlp_hyper_connection.")):
                family = "attn" if tail.startswith("attn_") else "ffn"
                part = hc[tail.split(".")[1]]
                tensors[gg + "hc_" + family + "_" + part + ".weight"] = value + 1 if part == "norm" else value
            elif tail.startswith("linear_attn."):
                part = tail.removeprefix("linear_attn.")
                if part == "in_proj_qkv.weight":
                    tensors[gg + "attn_qkv.weight"] = torch.cat((value[:qk], tile(value[qk:])), dim=0)
                elif part == "in_proj_z.weight":
                    tensors[gg + "attn_gate.weight"] = tile(value)
                elif part in ("in_proj_a.weight", "in_proj_b.weight"):
                    suffix = "ssm_alpha.weight" if part == "in_proj_a.weight" else "ssm_beta.weight"
                    tensors[gg + suffix] = tile(value, head_dim=1)
                elif part == "out_proj.weight":
                    tensors[gg + "ssm_out.weight"] = tile(value, axis=1)
                elif part == "A_log":
                    tensors[gg + "ssm_a"] = -tile(value, head_dim=1).exp()
                elif part == "dt_bias":
                    tensors[gg + "ssm_dt.bias"] = tile(value, head_dim=1)
                elif part == "conv1d.weight":
                    flat = value.squeeze(1)
                    tensors[gg + "ssm_conv1d.weight"] = torch.cat((flat[:qk], tile(flat[qk:])), dim=0)
                elif part == "norm.weight":
                    tensors[gg + "ssm_norm.weight"] = value
                else:
                    raise AssertionError(name)
            elif tail.startswith("self_attn."):
                part = tail.removeprefix("self_attn.")
                direct = {"q_proj.weight": "attn_q.weight", "k_proj.weight": "attn_k.weight",
                          "v_proj.weight": "attn_v.weight", "o_proj.weight": "attn_output.weight"}
                if part in direct:
                    tensors[gg + direct[part]] = value
                elif part in ("q_norm.weight", "k_norm.weight"):
                    tensors[gg + "attn_" + part] = value + 1
                elif part == "indexer.index_qk_proj.weight":
                    split = c.indexer_n_heads * c.indexer_head_dim
                    tensors[gg + "indexer.q_proj.weight"] = value[:split]
                    tensors[gg + "indexer.k_proj.weight"] = value[split:]
                elif part in ("indexer.q_layernorm.weight", "indexer.k_layernorm.weight"):
                    tensors[gg + part.replace("_layernorm", "_norm")] = value + 1
                else:
                    raise AssertionError(name)
            elif tail.startswith("ple."):
                part = tail.removeprefix("ple.")
                if part == "ple_embedding.ngram_embedding.weight":
                    tensors["per_layer_token_embd.weight"] = value
                elif part in ("key_proj.weight", "value_proj.weight"):
                    tensors[gg + "ple_" + part.replace("_proj", "")] = value
                elif part in ("norm_key.weight", "norm_query.weight", "norm_conv.weight"):
                    tensors[gg + "ple_" + part] = value + 1
                elif part == "conv1d.weight":
                    tensors[gg + "ple_conv1d.weight"] = value.squeeze(1)
                else:
                    raise AssertionError(name)
            else:
                raise AssertionError(name)

    facts = {
        "block_count": c.num_hidden_layers, "embedding_length": c.hidden_size,
        "context_length": c.max_position_embeddings, "attention.head_count": c.num_attention_heads,
        "attention.head_count_kv": c.num_key_value_heads, "attention.key_length": c.head_dim,
        "attention.value_length": c.head_dim, "attention.layer_norm_rms_epsilon": c.rms_norm_eps,
        "expert_count": c.num_experts, "expert_used_count": c.num_experts_per_tok,
        "expert_feed_forward_length": c.moe_intermediate_size,
        "expert_shared_feed_forward_length": c.shared_expert_intermediate_size,
        "hyper_connection.count": c.hc_count, "hyper_connection.low_rank": c.hc_lowrank,
        "ssm.conv_kernel": c.linear_conv_kernel_dim, "ssm.group_count": c.linear_num_key_heads,
        "ssm.state_size": c.linear_key_head_dim,
        "ssm.inner_size": c.linear_num_value_heads * c.linear_value_head_dim,
        "ssm.time_step_rank": c.linear_num_value_heads,
        "attention.indexer.head_count": c.indexer_n_heads,
        "attention.indexer.key_length": c.indexer_head_dim, "attention.indexer.top_k": c.indexer_budget,
        "attention.compress_ratios": [0, c.indexer_compress_ratio],
        "full_attention_interval": raw_config["full_attention_interval"],
        "rope.dimension_count": c.head_dim,
        "rope.dimension_sections": list(c.rope_parameters["mrope_section"]) + [0],
        "rope.freq_base": c.rope_parameters["rope_theta"],
        "ple.layers": [0], "ple.ngram_size": c.ngram_size, "ple.heads_per_ngram": c.heads_per_ngram,
        "ple.conv_kernel": c.ple_conv_kernel_size, "ple.eos_token_id": c.eos_token_id,
        "embedding_length_per_layer_input": c.ple_embed_dim // ((c.ngram_size - 1) * c.heads_per_ngram),
    }
    ple = native.model.layers[0].ple.ple_embedding
    facts["ple.layer_multipliers"] = ple.layer_multipliers.tolist()
    facts["ple.head_offsets"] = ple.ngram_heads_offsets.tolist()
    facts["ple.head_vocab_sizes"] = ple.ngram_heads_vocab_sizes.tolist()
    metadata = {"general.architecture": "qwen4exp", **{"qwen4exp." + key: value for key, value in facts.items()}}
    return _MemoryStore(tensors, metadata)


def _fixture():
    raw = _tiny_config()
    config = Qwen4ExpTextConfig(**copy.deepcopy(raw))
    config._attn_implementation = "eager"
    config._experts_implementation = "eager"
    torch.manual_seed(314159)
    native = Qwen4ExpForCausalLM(config)
    with torch.no_grad():
        for parameter in native.parameters():
            parameter.uniform_(-0.08, 0.08)
            parameter.requires_grad_(False)
    return native.eval(), raw, _forward_convert(native, raw)


class GGUFModelTests(unittest.TestCase):
    def test_literal_tiled_head_order_and_inverse(self):
        order = tiled_head_order(2, 6, 2)
        self.assertEqual(order.tolist(), [0, 1, 6, 7, 2, 3, 8, 9, 4, 5, 10, 11])
        native = torch.arange(12)
        self.assertTrue(torch.equal(native[order][torch.argsort(order)], native))
        with self.assertRaises(ValueError):
            tiled_head_order(3, 4, 2)

    def test_native_graph_logits_input_gradients_and_adapter_update(self):
        native, raw, store = _fixture()
        paths = {"model.layers.0.mlp.experts": [0, 1, 2]}
        loaded = load_text_model(store, raw, selections=paths, rank=2, alpha=4, chunk_rows=3).eval()
        tokens = torch.tensor([[3, 5, 2, 7, 9]], dtype=torch.long)
        expected = native(input_ids=tokens, use_cache=False).logits
        actual = loaded(input_ids=tokens, use_cache=False).logits
        torch.testing.assert_close(actual, expected, rtol=2e-4, atol=3e-6)

        hidden = native.model.embed_tokens(tokens).detach().requires_grad_()
        paged_hidden = hidden.detach().clone().requires_grad_()
        native_logits = native(inputs_embeds=hidden, ple_input_ids=tokens, use_cache=False).logits
        paged_logits = loaded(inputs_embeds=paged_hidden, ple_input_ids=tokens, use_cache=False).logits
        native_logits.square().sum().backward()
        paged_logits.square().sum().backward()
        torch.testing.assert_close(paged_hidden.grad, hidden.grad, rtol=5e-4, atol=5e-6)
        adapters = {name: parameter for name, parameter in loaded.named_parameters() if parameter.requires_grad}
        self.assertTrue(adapters)
        self.assertTrue(all(name.startswith("model.layers.0.mlp.experts.") for name in adapters))
        self.assertTrue(any(p.grad is not None and bool((p.grad != 0).any()) for p in adapters.values()))
        before = {name: p.detach().clone() for name, p in adapters.items()}
        optimizer = torch.optim.AdamW(adapters.values(), lr=0.01, weight_decay=0)
        optimizer.step()
        self.assertTrue(any(not torch.equal(before[name], p) for name, p in adapters.items()))
        self.assertTrue(all(p.grad is None for p in loaded.parameters() if not p.requires_grad))
        self.assertTrue(bool(torch.isfinite(loaded(input_ids=tokens, use_cache=False).logits).all()))

    def test_exact_coverage_real_buffers_and_paged_ple_eos(self):
        native, raw, store = _fixture()
        loaded = load_text_model(store, raw, chunk_rows=3)
        report = loaded.alora_loader_report
        self.assertEqual(report["mapped_tensor_names"], list(store.tensor_names))
        self.assertEqual(report["native_parameter_count"], len(list(native.named_parameters())))
        self.assertEqual(report["source_identity"], store.source_identity)
        self.assertFalse(report["forward_verified"])
        self.assertFalse(report["training_verified"])
        self.assertEqual(len(report["native_modeling_sha256"]), 64)
        self.assertEqual(len(report["native_configuration_sha256"]), 64)
        self.assertEqual(len(report["execution_config_sha256"]), 64)
        self.assertTrue(report["transformers_version"])
        self.assertFalse(any(p.requires_grad for p in loaded.parameters()))
        self.assertFalse(any(isinstance(m, (nn.Linear, nn.Embedding)) for m in loaded.modules()))
        self.assertTrue(all(v.device.type == "cpu" for _, v in loaded.named_buffers()))
        self.assertTrue(all(v.device.type == "cpu" for _, v in loaded.named_parameters()))
        before = native.model.layers[0].ple.ple_embedding
        after = loaded.model.layers[0].ple.ple_embedding
        ids = torch.tensor([[3, 2, 5, 7, 2], [2, 3, 3, 2, 4]])
        torch.testing.assert_close(after(ids, None), before(ids, None), rtol=0, atol=0)
        self.assertTrue(torch.equal(after.layer_multipliers, before.layer_multipliers))
        self.assertGreater(int(after.layer_multipliers.max()), 2**53)
        with self.assertRaisesRegex(ValueError, "state_dict"):
            loaded.state_dict()
        with self.assertRaisesRegex(ValueError, "reverse embedding"):
            loaded(inputs_embeds=torch.zeros(1, 2, 8))
        with self.assertRaisesRegex(ValueError, "cache"):
            loaded(input_ids=ids[:1], use_cache=True)
        loaded.config.use_cache = True
        with self.assertRaisesRegex(ValueError, "cache"):
            loaded(input_ids=ids[:1])

    def test_missing_extra_shape_and_metadata_fail_closed(self):
        mutations = (
            lambda s: s.tensors.pop("blk.1.indexer.k_proj.weight"),
            lambda s: s.tensors.__setitem__("unexpected.weight", np.ones((1,), dtype=np.float32)),
            lambda s: s.tensors.__setitem__("blk.0.ssm_dt.bias", np.ones((5,), dtype=np.float32)),
            lambda s: s.metadata.__setitem__("qwen4exp.hyper_connection.count", 3),
            lambda s: s.metadata.__setitem__("qwen4exp.ple.layer_multipliers", [1.0, 2.0, 3.0]),
            lambda s: s.metadata.__setitem__("qwen4exp.ple.head_offsets", [0, 6]),
            lambda s: s.tensors.__setitem__("blk.0.ssm_a", np.ones((6,), dtype=np.float32)),
        )
        for change in mutations:
            with self.subTest(change=change):
                _, raw, store = _fixture()
                change(store)
                with self.assertRaises(ValueError):
                    load_text_model(store, raw, chunk_rows=3)

    def test_unsupported_config_device_and_selection_refused(self):
        _, raw, store = _fixture()
        for key, value in (("unknown_feature", True), ("hidden_act", "relu"),
                           ("tie_word_embeddings", True), ("output_gate_type", "silu")):
            bad = {**raw, key: value}
            with self.subTest(key=key), self.assertRaises(ValueError):
                load_text_model(store, bad)
        with self.assertRaisesRegex(ValueError, "CPU"):
            load_text_model(store, raw, device="cuda")
        for selected in ({"other.experts": [0]}, {"model.layers.0.mlp.experts": [0, 0]},
                         {"model.layers.0.mlp.experts": [True]}):
            with self.subTest(selected=selected), self.assertRaises(ValueError):
                load_text_model(store, raw, selections=selected)

    def test_each_layer_is_native_and_routing_is_preserved(self):
        _, raw, store = _fixture()
        loaded = load_text_model(store, raw, chunk_rows=3)
        self.assertIs(type(loaded), Qwen4ExpForCausalLM)
        self.assertEqual(loaded.model.layers[0].__class__.__name__, "Qwen4ExpTextDecoderLayer")
        self.assertEqual(loaded.model.layers[1].self_attn.__class__.__name__, "Qwen4ExpTextAttention")
        for layer in loaded.model.layers:
            self.assertEqual(layer.mlp.gate.__class__.__name__, "Qwen4ExpTextTopKRouter")
            self.assertIsInstance(layer.mlp.experts, PagedExperts)
            self.assertFalse(layer.mlp.gate.weight.requires_grad)


if __name__ == "__main__":
    unittest.main()
