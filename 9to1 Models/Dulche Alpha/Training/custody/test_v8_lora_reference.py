import hashlib
import json
import os
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from custody.artifacts import CustodyError, _open_regular
from custody.v8_lora_reference import _GIB, _PAGED_SHA, _capture, _headroom, _model_unchanged, _pin_installed_peft_source, _pinned_small, _verify_model, main
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
                        "model.safetensors.index.json": b"{}", "model-00001-of-00001.safetensors": b"fixture",
                        ".gitattributes": b"official metadata", "LICENSE": b"official license"}
            for file, data in payloads.items():
                (model / file).write_bytes(data)
            pins = root / "pins.json"
            manifest = json.dumps({"files": {name: digest(data) for name, data in payloads.items()}}).encode()
            pins.write_bytes(manifest)
            cache = model / ".cache"
            cache.mkdir()
            (cache / "untraversed").symlink_to(model / "unavailable")
            before = _verify_model(model, pins, digest(manifest))
            self.assertEqual(set(before), set(payloads))
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

    def test_only_exact_real_owned_cache_and_two_pinned_doc_names_are_admitted(self):
        from custody.v8_lora_reference import _model_files
        with TemporaryDirectory() as tmp:
            model = Path(tmp)
            (model / ".cache").write_bytes(b"not a directory")
            with self.assertRaisesRegex(CustodyError, "not a directory"):
                _model_files(model)
            (model / ".cache").unlink()
            outside = model / "actual"
            outside.mkdir()
            (model / ".cache").symlink_to(outside)
            with self.assertRaisesRegex(CustodyError, "links"):
                _model_files(model)
            (model / ".cache").unlink()
            (model / ".cache").mkdir()
            with patch("custody.v8_lora_reference.os.geteuid", return_value=os.geteuid() + 1):
                with self.assertRaisesRegex(CustodyError, "operator"):
                    _model_files(model)
            self.assertEqual(_model_files(model), {"actual"})

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

    def test_peft_exact_runtime_hardlink_is_admitted_but_generic_storage_stays_strict(self):
        with TemporaryDirectory() as tmp:
            purelib = Path(tmp)
            package = purelib / "peft/tuners/lora"
            package.mkdir(parents=True)
            original, target = purelib / "cached-source", package / "layer.py"
            content = b"# exact synthetic known PEFT source"
            original.write_bytes(content)
            original.chmod(0o644)
            target.hardlink_to(original)
            with patch("custody.v8_lora_reference._PEFT_LAYER_SHA", digest(content)):
                self.assertEqual(_pin_installed_peft_source(purelib, "layer.py", digest(content)), target)
                with self.assertRaisesRegex(CustodyError, "without aliases"):
                    _open_regular(target)
                with self.assertRaisesRegex(CustodyError, "allowlisted"):
                    _pin_installed_peft_source(purelib, "other.py", digest(content))
                original.write_bytes(b"changed")
                with self.assertRaisesRegex(CustodyError, "failed verification"):
                    _pin_installed_peft_source(purelib, "layer.py", digest(content))
                target.unlink()
                target.symlink_to(original)
                with self.assertRaisesRegex(CustodyError, "owner-owned and regular"):
                    _pin_installed_peft_source(purelib, "layer.py", digest(content))

    def test_native_runtime_failure_occurs_before_full_model_integrity_io(self):
        with TemporaryDirectory() as tmp, patch.dict(os.environ, {"CUDA_VISIBLE_DEVICES": "-1"}):
            path = Path(tmp)
            opts = ["--source-root", tmp, "--model-root", str(path / "model"), "--model-pins", str(path / "pins"),
                    "--model-pins-sha256", "0" * 64, "--torch-version", "test", "--seed", "41",
                    "--min-headroom-gib", "70", "--grad-checkpointing", "--output", str(path / "out")]
            with (patch("custody.v8_lora_reference._headroom"),
                  patch("custody.v8_lora_reference._pinned_small"),
                  patch("custody.v8_lora_reference._admit_runtime", side_effect=CustodyError("runtime refused")),
                  patch("custody.v8_lora_reference._verify_model") as expensive):
                with self.assertRaisesRegex(CustodyError, "runtime refused"):
                    main(opts)
                expensive.assert_not_called()

    @unittest.skipUnless(os.getenv("DULCHE_V8_SOURCE_FOR_PARITY"), "requires exact audited private v8 source")
    def test_both_reviewed_sources_are_hash_qualified(self):
        script = Path(os.environ["DULCHE_V8_SOURCE_FOR_PARITY"])
        self.assertIn(b"torch.manual_seed(args.seed)", _pinned_small(script, V8_SOURCE_SHA256))
        pager = _pinned_small(script.with_name("paged_training.py"), _PAGED_SHA)
        self.assertIn(b"prepare_model_for_kbit_training", pager)
