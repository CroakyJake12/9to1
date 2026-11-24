import ast
from dataclasses import replace
import hashlib
from importlib.util import find_spec
import os
from pathlib import Path
import unittest

from custody.artifacts import CustodyError
from custody.v8_lora_init import (
    V8LoraProof, V8LoraTarget, _TARGETS, _raw, _receipt, _sha, _validate, initialize_attested_v8_lora,
)
from custody.v8_sampler import V8_SOURCE_SHA256

_ROOT = "model.language_model.layers."


class V8LoraProofTests(unittest.TestCase):
    def test_rejects_unproved_wrong_order_or_foreign_target_before_importing_torch(self):
        a = V8LoraTarget(_ROOT + "0.self_attn.q_proj", 7, 9)
        b = V8LoraTarget(_ROOT + "32.mlp.up_proj", 9, 11)
        proof = V8LoraProof(V8_SOURCE_SHA256, "native-version", 41, (a, b), "0" * 64, "1" * 64, "2" * 64)
        _validate(proof)
        for invalid in (replace(proof, targets=(b, a)), replace(proof, targets=(a, a)),
                        replace(proof, targets=(replace(a, path="model.visual.layers.0.q_proj"), b)),
                        replace(proof, targets=(replace(a, path=_ROOT + "0.self_attn.not_original"), b)),
                        replace(proof, cpu_after_sha256="")):
            with self.assertRaises(CustodyError):
                _validate(invalid)
        with self.assertRaisesRegex(CustodyError, "RNG prefix"):
            initialize_attested_v8_lora(proof, b"wrong")

    @unittest.skipUnless(os.getenv("DULCHE_V8_SOURCE_FOR_PARITY"), "requires exact audited private v8 source")
    def test_peft_constants_are_source_verified(self):
        raw = Path(os.environ["DULCHE_V8_SOURCE_FOR_PARITY"]).read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), V8_SOURCE_SHA256)
        tree = ast.parse(raw)
        native = next(node for node in ast.walk(tree) if isinstance(node, ast.Call)
                      and isinstance(node.func, ast.Name) and node.func.id == "LoraConfig")
        settings = {keyword.arg: keyword.value for keyword in native.keywords}
        self.assertEqual(ast.literal_eval(settings["r"]), 16)
        self.assertEqual(ast.literal_eval(settings["lora_alpha"]), 32)
        self.assertEqual(ast.literal_eval(settings["lora_dropout"]), .05)
        self.assertEqual(tuple(ast.literal_eval(settings["target_modules"])), _TARGETS)


@unittest.skipUnless(find_spec("torch") and find_spec("peft"), "requires installed native PyTorch/PEFT")
class NativeV8LoraReplayTests(unittest.TestCase):
    def test_exact_cpu_replay_matches_global_peft_injection_and_rng(self):
        import torch
        from torch import nn
        import peft
        from peft.tuners.lora.layer import Linear

        if peft.__version__ != "0.18.1":
            self.skipTest("requires pinned PEFT 0.18.1")
        from transformers import PretrainedConfig

        class PagedLinear(nn.Linear):
            def __init__(self, in_features, out_features):
                nn.Module.__init__(self)
                self.in_features, self.out_features = in_features, out_features
                self.weight = nn.Parameter(torch.empty(0, dtype=torch.bfloat16), requires_grad=False)
                self.bias = None

        model = nn.Module()
        model.config = PretrainedConfig(tie_word_embeddings=False)
        model.model = nn.Module()
        model.model.language_model = nn.Module()
        model.model.language_model.layers = nn.ModuleList([nn.Module() for _ in range(3)])
        model.model.language_model.layers[0].up_proj = PagedLinear(7, 9)
        model.model.language_model.layers[0].q_proj = PagedLinear(7, 5)
        model.model.language_model.layers[2].k_proj = PagedLinear(5, 3)
        targets = tuple(V8LoraTarget(name, module.in_features, module.out_features)
                        for name, module in model.named_modules() if type(module) is PagedLinear)
        model = peft.prepare_model_for_kbit_training(model, use_gradient_checkpointing=False)
        self.assertTrue(all(module.weight.dtype == torch.float32 for module in model.modules()
                            if type(module) is PagedLinear))
        config = peft.LoraConfig(r=16, lora_alpha=32, lora_dropout=.05, target_modules=list(_TARGETS))
        config._register_custom_module({PagedLinear: Linear})
        ambient = _raw(torch, torch.get_rng_state())
        with torch.random.fork_rng(devices=[]):
            torch.random.default_generator.manual_seed(41)
            before = _raw(torch, torch.get_rng_state())
            native = peft.get_peft_model(model, config)
            after = _raw(torch, torch.get_rng_state())
            expected = {name.removeprefix("base_model.model."): value.detach().float()
                        for name, value in native.named_parameters() if ".lora_" in name}
        proof = V8LoraProof(V8_SOURCE_SHA256, str(torch.__version__), 41, targets,
                            _sha(before), _sha(after), _receipt(torch, expected))
        actual, replay_after = initialize_attested_v8_lora(proof, before)
        self.assertEqual(tuple(actual), tuple(expected))
        self.assertEqual(replay_after, after)
        self.assertEqual(_raw(torch, torch.get_rng_state()), ambient)
        for key, value in expected.items():
            torch.testing.assert_close(actual[key], value, rtol=0, atol=0)
        with self.assertRaisesRegex(CustodyError, "differ"):
            initialize_attested_v8_lora(replace(proof, adapter_sha256="0" * 64), before)
