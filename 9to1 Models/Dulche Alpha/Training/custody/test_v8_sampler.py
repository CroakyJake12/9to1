import ast
import hashlib
import json
import os
from pathlib import Path
import random
import sys
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from custody.artifacts import CustodyError
from custody.v8_sampler import SamplerLimits, V8HostSampler, V8_SOURCE_SHA256


class TensorFixture:
    def __init__(self, values):
        self.values = list(values)

    def clone(self):
        return TensorFixture(self.values)

    def __len__(self):
        return len(self.values)

    def __setitem__(self, slot, value):
        for i in range(*slot.indices(len(self.values))):
            self.values[i] = value

    def unsqueeze(self, axis):
        assert axis == 0
        return [self.values]


_TORCH = SimpleNamespace(long="long", tensor=lambda ids, **_: TensorFixture(ids))


class TokenizerFixture:
    def __init__(self):
        self.calls = []

    def apply_chat_template(self, messages, *, tokenize, add_generation_prompt=False):
        self.calls.append(("template", messages, tokenize, add_generation_prompt))
        words = [token for message in messages for token in
                 ((100 if message["role"] == "assistant" else 200), *map(ord, message["content"]))]
        if add_generation_prompt:
            words.append(100)
        return tuple(words)

    def __call__(self, text, *, add_special_tokens, truncation=False, max_length=None):
        self.calls.append(("tokens", text, add_special_tokens, truncation, max_length))
        return {"input_ids": list(text[:max_length] if truncation else text)}


class V8SamplerTests(unittest.TestCase):
    def setUp(self):
        temp = TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        source = {
            "train": [[("user", "x"), ("assistant", "123456"), ("assistant", "zz")],
                      [("user", "yy"), ("assistant", "B")],
                      [("assistant", "C"), ("user", "ignored-for-prefix")],
                      [("user", "q")], [("user", "水"), ("assistant", "E")]],
            "validation": [[("user", "V"), ("assistant", "not a training row")]],
            "final_held_out": [[("user", "H"), ("assistant", "not a training row")]],
        }
        files = []
        for split, values in source.items():
            name = split + ".jsonl"
            rows = [json.dumps({"messages": [{"role": r, "content": c} for r, c in row]},
                               ensure_ascii=False) for row in values]
            data = ("\r\n\r\n".join(rows) + "\r\n").encode()
            (self.root / name).write_bytes(data)
            files.append({"split": split, "path": name, "sha256": hashlib.sha256(data).hexdigest()})
        data = json.dumps({"files": files}).encode()
        (self.root / "dataset_manifest.json").write_bytes(data)
        self.manifest = hashlib.sha256(data).hexdigest()

    def make(self, tokenizer=None, **options):
        kwargs = {"manifest_sha256": self.manifest, "seed": 23, "seq_len": 7, "grad_accum": 3, "start_step": 2}
        return V8HostSampler(self.root, tokenizer or TokenizerFixture(), **(kwargs | options))

    def test_shuffled_wrap_resume_label_and_train_only(self):
        tokenizer = TokenizerFixture()
        with patch.dict(sys.modules, {"torch": _TORCH}):
            sampler = self.make(tokenizer)
            fresh = self.make(start_step=0)
            masked = self.make(seed=0)
        self.assertEqual(sampler.microbatch(3, 0), fresh.microbatch(3, 0))
        self.assertEqual(sampler.microbatch(3, 0), sampler.microbatch(8, 0))
        self.assertNotEqual(sampler.python_rng, masked.python_rng)
        expected = [([200, 120, 100, 49, 50, 51, 52], [-100, -100, -100, 49, 50, 51, 52]),
                    ([200, 121, 121, 100, 66], [-100, -100, -100, -100, 66]),
                    ([100, 67, 200, 105, 103, 110, 111], [-100, 67, 200, 105, 103, 110, 111]),
                    ([200, 113], [-100, -100]), ([200, ord("水"), 100, 69], [-100, -100, -100, 69])]
        actual = [(ids[0], labels[0]) for ids, labels in
                  [sampler.microbatch(3 + i // 3, i % 3) for i in range(5)]]
        self.assertCountEqual(actual, expected)
        self.assertEqual(len(tokenizer.calls), 4 * len(expected))
        for coord in ((2, 0), (3, 3), (True, 0)):
            with self.assertRaises(CustodyError):
                sampler.microbatch(*coord)

    def test_integrity_and_budget_refuse_before_encoding(self):
        tokenizer = TokenizerFixture()
        with self.assertRaisesRegex(CustodyError, "pinned bytes"):
            self.make(tokenizer, manifest_sha256="0" * 64)
        with self.assertRaisesRegex(CustodyError, "row budget"):
            self.make(tokenizer, limits=SamplerLimits(rows=1))
        with self.assertRaisesRegex(CustodyError, "token-slot budget"):
            self.make(tokenizer, limits=SamplerLimits(token_slots=7))
        (self.root / "final_held_out.jsonl").write_bytes(b"tampered")
        with self.assertRaisesRegex(CustodyError, "pinned bytes"):
            self.make(tokenizer)
        self.assertFalse(tokenizer.calls)

    @unittest.skipUnless(os.getenv("DULCHE_V8_SOURCE_FOR_PARITY"), "requires exact audited private v8 source")
    def test_exact_source_ast_synthetic_sampler_parity(self):
        path = Path(os.environ["DULCHE_V8_SOURCE_FOR_PARITY"])
        raw = path.read_bytes()
        self.assertEqual(hashlib.sha256(raw).hexdigest(), V8_SOURCE_SHA256)
        parsed = ast.parse(raw)
        main = next(node for node in parsed.body if isinstance(node, ast.FunctionDef) and node.name == "main")
        loader = next(node for node in parsed.body if isinstance(node, ast.FunctionDef) and node.name == "load_rows")
        encoders = [node for node in main.body if isinstance(node, ast.FunctionDef)
                    and node.name in ("assistant_prefix_ids", "encode")]
        tokenizer = TokenizerFixture()
        env = {"Path": Path, "json": json, "hashlib": hashlib, "torch": _TORCH, "tokenizer": tokenizer,
               "args": SimpleNamespace(seq_len=7, seed=23, grad_accum=3)}
        # Execute only three admitted function definitions, not deployed main/imports.
        exec(compile(ast.Module(body=[loader, *encoders], type_ignores=[]), "<pinned-v8-sampler>", "exec"), env)
        rows = [r for r in env["load_rows"](self.root) if r["_split"] == "train"]
        rng = random.Random(env["args"].seed)
        order = list(range(len(rows)))
        rng.shuffle(order)
        expected = [env["encode"](rows[i]) for i in order]
        for resumed, seed in ((None, 23), (random.Random(31).getstate(), 23)):
            other = TokenizerFixture()
            with patch.dict(sys.modules, {"torch": _TORCH}):
                sampler = self.make(other, resumed_python_rng=resumed, seed=seed)
            self.assertEqual(sampler.python_rng, rng.getstate() if resumed is None else resumed)
            self.assertEqual(tokenizer.calls, other.calls)
            bi = 2 * env["args"].grad_accum % len(expected)
            for i in range(18):
                ids, labels = expected[bi % len(expected)]
                self.assertEqual(sampler.microbatch(3 + i // 3, i % 3), (ids.unsqueeze(0), labels.unsqueeze(0)))
                bi += 1
