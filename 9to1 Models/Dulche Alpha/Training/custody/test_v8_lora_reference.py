import hashlib
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from custody.artifacts import CustodyError
from custody.v8_lora_reference import _GIB, _PAGED_SHA, _capture, _headroom, _model_unchanged, _pinned_small, _verify_model
from custody.v8_sampler import V8_SOURCE_SHA256


def digest(data):
    return hashlib.sha256(data).hexdigest()


class NativeReferenceBoundaryTests(unittest.TestCase):
    def test_pinned_flat_metadata_and_synthetic_shard_refuse_changed_or_added_input(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            model = root / "model"
            model.mkdir()
            payloads = {"config.json": b"{}", "tokenizer_config.json": b"{}",
                        "model.safetensors.index.json": b"{}", "model-00001-of-00001.safetensors": b"fixture"}
            for file, data in payloads.items():
                (model / file).write_bytes(data)
            pins = root / "pins.json"
            manifest = json.dumps({"files": {name: digest(data) for name, data in payloads.items()}}).encode()
            pins.write_bytes(manifest)
            before = _verify_model(model, pins, digest(manifest))
            _model_unchanged(model, before)
            (model / "extra.json").write_text("{}")
            with self.assertRaisesRegex(CustodyError, "exact pinned file set"):
                _verify_model(model, pins, digest(manifest))
            (model / "extra.json").unlink()
            (model / "config.json").write_text("[]")
            with self.assertRaisesRegex(CustodyError, "changed"):
                _model_unchanged(model, before)
            with self.assertRaisesRegex(CustodyError, "failed verification"):
                _verify_model(model, pins, digest(manifest))

    def test_system_and_cgroup_headroom_and_no_swap_must_all_hold(self):
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            proc, cg = root / "proc", root / "cgroup"
            (proc / "self").mkdir(parents=True)
            (cg / "owned").mkdir(parents=True)
            (proc / "self/cgroup").write_text("0::/owned\n")
            (proc / "meminfo").write_text(f"MemAvailable: {76 * _GIB // 1024} kB\n")
            for filename, data in {"memory.max": 80 * _GIB, "memory.current": _GIB, "memory.swap.max": 0}.items():
                (cg / "owned" / filename).write_text(str(data))
            self.assertEqual(_headroom(72, proc=proc, cgroup=cg)["memory_max_bytes"], 80 * _GIB)
            with self.assertRaisesRegex(CustodyError, "required free memory"):
                _headroom(77, proc=proc, cgroup=cg)
            (cg / "owned/memory.swap.max").write_text("max")
            with self.assertRaisesRegex(CustodyError, "memory.swap.max=0"):
                _headroom(72, proc=proc, cgroup=cg)

    def test_cpu_capture_refuses_unknown_cuda_visibility_before_importing_runtime(self):
        with patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": "0"}):
            with self.assertRaisesRegex(CustodyError, "CUDA_VISIBLE_DEVICES=-1"):
                _capture(SimpleNamespace(), b"", {})

    @unittest.skipUnless(os.getenv("DULCHE_V8_SOURCE_FOR_PARITY"), "requires exact audited private v8 source")
    def test_both_reviewed_sources_are_hash_qualified(self):
        script = Path(os.environ["DULCHE_V8_SOURCE_FOR_PARITY"])
        self.assertIn(b"torch.manual_seed(args.seed)", _pinned_small(script, V8_SOURCE_SHA256))
        pager = _pinned_small(script.with_name("paged_training.py"), _PAGED_SHA)
        self.assertIn(b"prepare_model_for_kbit_training", pager)
