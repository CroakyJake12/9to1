import hashlib
import io
import json
import shutil
import stat
import struct
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from model_catalog import GiB, ModelCatalog
from model_formats import MAX_PARAMETERS, inspect_model
from model_hub import Hub


def gguf(
    name="demo", values=8, shard=None, tensor="weight", payload=True, declared_tensors=2
):
    def s(value):
        value = value.encode()
        return struct.pack("<Q", len(value)) + value

    meta = {"general.architecture": "llama", "general.name": name}
    if shard is not None:
        meta.update(
            {
                "split.no": shard,
                "split.count": 2,
                "split.tensors.count": declared_tensors,
            }
        )
    fields = b"".join(
        s(k)
        + (
            struct.pack("<I", 8) + s(v)
            if isinstance(v, str)
            else struct.pack("<II", 4, v)
        )
        for k, v in meta.items()
    )
    header = (
        b"GGUF"
        + struct.pack("<IQQ", 3, 1, len(meta))
        + fields
        + s(tensor)
        + struct.pack("<IQIQ", 1, values, 0, 0)
    )
    return (
        header + b"\0" * (-len(header) % 32) + (b"x" * (4 * values) if payload else b"")
    )


def safetensors(path, shapes):
    offset = 0
    obj = {}
    for name, shape in shapes.items():
        size = 2
        for n in shape:
            size *= n
        obj[name] = {
            "dtype": "F16",
            "shape": shape,
            "data_offsets": [offset, offset + size],
        }
        offset += size
    header = json.dumps(obj).encode()
    path.write_bytes(struct.pack("<Q", len(header)) + header + b"x" * offset)


class Response(io.BytesIO):
    def __init__(self, data, offset=0, block=None, total=None):
        super().__init__(data[offset:])
        self.status = 206 if offset else 200
        self.headers = {"Content-Range": f"bytes {offset}-{len(data) - 1}/{len(data)}"}
        self.block = block
        self.reads = 0

    def read(self, n=-1):
        self.reads += 1
        if self.block and self.reads == 2:
            self.block[0].set()
            self.block[1].wait(3)
        return super().read(n)


class Source:
    def __init__(self, data, block=None):
        self.data = data
        self.block = block
        self.offsets = []

    def plan(self, repo, filename, revision):
        item = {
            "path": filename,
            "bytes": len(self.data),
            "git_blob": None,
            "sha256": hashlib.sha256(self.data).hexdigest(),
        }
        return {
            "repo": repo,
            "revision": "a" * 40,
            "license": "apache-2.0",
            "parameters": 8,
            "files": [item],
            "target": filename,
            "total_bytes": len(self.data),
        }

    def open(self, url, headers):
        offset = int(headers["Range"][6:-1]) if headers else 0
        self.offsets.append(offset)
        return Response(
            self.data, offset, self.block if len(self.offsets) == 1 else None
        )


class CatalogueTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.cat = ModelCatalog(self.root / "state")

    def tearDown(self):
        self.cat.close()
        self.temp.cleanup()

    def wait(self, job, states=("completed", "failed", "paused_storage")):
        deadline = time.monotonic() + 7
        while time.monotonic() < deadline:
            row = self.cat.jobs(job["id"])
            if row["state"] in states:
                return row
            time.sleep(0.01)
        self.fail("job timeout")

    def import_path(self, path, **fields):
        status, job = self.cat.handle(
            "POST", "/v1/catalog/import", {}, dict(path=str(path), **fields)
        )
        self.assertEqual(202, status)
        return self.wait(job)

    def test_external_result_is_persistent_and_uninstall_keeps_original(self):
        model = self.root / "example-Q4_K_M.gguf"
        model.write_bytes(gguf())
        job = self.import_path(model)
        self.assertEqual("completed", job["state"])
        self.cat.close()
        self.cat = ModelCatalog(self.root / "state")
        record = self.cat.get(job["installed_id"])
        self.assertEqual(
            ("llama", "external", 8, "Q4_K_M"),
            tuple(
                record[k]
                for k in ("architecture", "ownership", "parameters", "quantization")
            ),
        )
        self.cat.uninstall(record["id"])
        self.assertTrue(model.is_file())

    def test_frozen_release_prefers_neighbor_validators_and_rejects_incomplete_bundle(
        self,
    ):
        service = Path(__file__).resolve().parent
        frozen = self.root / "release"
        frozen.mkdir()
        shutil.copyfile(service / "model_formats.py", frozen / "model_formats.py")
        code = "import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);import model_formats;assert model_formats.validator_root==Path(sys.argv[1]);assert all(Path(p).exists() for p in sys.path if 'Home/Source/Dulche' in p)"
        args = [sys.executable, "-I", "-c", code, str(frozen)]
        fail = subprocess.run(args, capture_output=True, text=True, check=False)
        self.assertNotEqual(fail.returncode, 0)
        self.assertIn("frozen Dulche release lacks", fail.stderr)
        source = service.parents[2] / "9to1 Workspace/Home/Source/Dulche"
        for package, files in [
            ("bundle", ["__init__.py", "formats.py", "identity.py"]),
            ("llamacpp", ["gguf.py"]),
        ]:
            (frozen / package).mkdir()
            for name in files:
                shutil.copyfile(source / package / name, frozen / package / name)
        subprocess.run(args, check=True, capture_output=True)

    def test_single_catalogue_owner(self):
        with self.assertRaisesRegex(RuntimeError, "another service"):
            ModelCatalog(self.root / "state")

    def test_archive_import_and_managed_uninstall(self):
        p = self.root / "safe.zip"
        with zipfile.ZipFile(p, "w") as z:
            z.writestr("nested/model.gguf", gguf())
        j = self.import_path(p)
        self.assertEqual("completed", j["state"])
        rec = self.cat.get(j["installed_id"])
        self.assertEqual("managed", rec["ownership"])
        self.cat.in_use = lambda _: True
        with self.assertRaisesRegex(ValueError, "unload"):
            self.cat.uninstall(rec["id"])
        self.cat.in_use = lambda _: False
        self.cat.uninstall(rec["id"])
        self.assertFalse(Path(rec["path"]).exists())
        self.assertTrue(p.exists())

    def test_unsafe_archive_entries_are_refused(self):
        for filename in (
            "../model.gguf",
            "/outside.gguf",
            "folder\\outside.gguf",
            "repo/auto.py",
        ):
            with self.subTest(filename=filename):
                p = self.root / "unsafe.zip"
                with zipfile.ZipFile(p, "w") as z:
                    z.writestr(filename, b"x")
                self.assertEqual("failed", self.import_path(p)["state"])
        p = self.root / "link.zip"
        with zipfile.ZipFile(p, "w") as z:
            link = zipfile.ZipInfo("model.gguf")
            link.external_attr = (stat.S_IFLNK | 0o777) << 16
            z.writestr(link, "/outside")
        self.assertEqual("failed", self.import_path(p)["state"])
        p = self.root / "ratio.zip"
        with zipfile.ZipFile(p, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr("model.gguf", b"a" * 3_000_000)
        self.assertEqual("failed", self.import_path(p)["state"])

    def test_shards_must_match_metadata(self):
        a = self.root / "d-00001-of-00002.gguf"
        b = self.root / "d-00002-of-00002.gguf"
        a.write_bytes(gguf(shard=0, tensor="a"))
        with self.assertRaisesRegex(ValueError, "missing"):
            inspect_model(a)
        b.write_bytes(gguf(shard=1, tensor="b"))
        rec = inspect_model(b)
        self.assertEqual(16, rec["parameters"])
        self.assertEqual(str(a), rec["path"])
        b.write_bytes(gguf(shard=1, tensor="a"))
        with self.assertRaisesRegex(ValueError, "duplicate"):
            inspect_model(a)

    def test_metadata_only_first_gguf_shard_and_empty_standalone_rejection(self):
        first = self.root / "dense-00001-of-00002.gguf"
        last = self.root / "dense-00002-of-00002.gguf"
        full = gguf(shard=0, tensor="placeholder", declared_tensors=1)
        pos = full.index(struct.pack("<Q", 11) + b"placeholder")
        first.write_bytes(full[:8] + struct.pack("<Q", 0) + full[16:pos])
        last.write_bytes(gguf(shard=1, declared_tensors=1))
        self.assertEqual(8, inspect_model(first)["parameters"])
        solo = self.root / "no-weights.gguf"
        raw = gguf(tensor="placeholder")
        cut = raw.index(struct.pack("<Q", 11) + b"placeholder")
        solo.write_bytes(raw[:8] + struct.pack("<Q", 0) + raw[16:cut])
        with self.assertRaisesRegex(ValueError, "empty GGUF"):
            inspect_model(solo)

    def test_hf_directory_and_adapter_association(self):
        hf = self.root / "hf"
        hf.mkdir()
        (hf / "config.json").write_text('{"model_type":"llama"}')
        (hf / "tokenizer.json").write_text('{"model":{"type":"BPE"}}')
        safetensors(hf / "model.safetensors", {"weight": [2, 2]})
        job = self.import_path(hf)
        self.assertEqual("completed", job["state"])
        model = self.cat.get(job["installed_id"])
        adapter = self.root / "adapter"
        adapter.mkdir()
        cfg = {
            "peft_type": "LORA",
            "base_model_name_or_path": str(hf),
            "target_modules": ["q_proj"],
            "r": 2,
        }
        (adapter / "adapter_config.json").write_text(json.dumps(cfg))
        safetensors(
            adapter / "adapter_model.safetensors",
            {
                "model.q_proj.lora_A.weight": [2, 4],
                "model.q_proj.lora_B.weight": [4, 2],
            },
        )
        self.assertEqual("failed", self.import_path(adapter)["state"])
        valid = self.import_path(adapter, base_model=model["id"])
        self.assertEqual("completed", valid["state"])
        self.assertEqual(
            model["id"], self.cat.get(valid["installed_id"])["base_model_id"]
        )
        with self.assertRaisesRegex(ValueError, "dependent"):
            self.cat.uninstall(model["id"])
        (hf / "tokenizer.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "tokenizer model"):
            inspect_model(hf)

    def test_storage_pause_avoids_writing_weights(self):
        hub = Source(gguf())
        self.cat.hub = hub
        self.cat.disk_usage = lambda p: SimpleNamespace(free=60 * GiB)
        job = self.cat.submit("download", {"repo": "org/demo", "filename": "demo.gguf"})
        self.assertEqual("paused_storage", self.wait(job)["state"])
        self.assertFalse(hub.offsets)
        self.cat.disk_usage = lambda p: SimpleNamespace(free=80 * GiB)
        self.cat.action(job["id"], "resume")
        self.assertEqual("completed", self.wait(job)["state"])

    def test_pause_resume_continues_pinned_range_and_persists_across_restart(self):
        block = (threading.Event(), threading.Event())
        hub = Source(gguf(values=800_000), block)
        self.cat.hub = hub
        job = self.cat.submit("download", {"repo": "org/demo", "filename": "demo.gguf"})
        self.assertTrue(block[0].wait(5))
        self.cat.action(job["id"], "pause")
        block[1].set()
        self.cat.close()
        self.cat = ModelCatalog(self.root / "state", hub=hub)
        self.assertEqual("paused", self.cat.jobs(job["id"])["state"])
        self.cat.action(job["id"], "resume")
        done = self.wait(job)
        self.assertEqual("completed", done["state"])
        self.assertGreater(hub.offsets[-1], 0)
        self.assertEqual(
            hub.data, Path(self.cat.get(done["installed_id"])["path"]).read_bytes()
        )

    def test_parameter_ceiling_precedes_weight_transfer(self):
        h = Hub()

        def info(repo, revision):
            return {"repo": repo, "parameters": MAX_PARAMETERS + 1, "files": []}

        h.detail = info
        with patch.object(h, "open", side_effect=AssertionError("weights requested")):
            with self.assertRaisesRegex(ValueError, "500B"):
                h.plan("org/demo", "x.gguf", None)

    def test_gguf_prefix_count_rejects_mislabeled_checkpoint(self):
        h = Hub()
        count = MAX_PARAMETERS + 1
        header = gguf(values=count, payload=False)
        item = {
            "path": "demo.gguf",
            "bytes": len(header) + count * 4,
            "sha256": "a" * 64,
            "git_blob": None,
        }
        h.detail = lambda *_: {
            "repo": "org/demo",
            "parameters": 1,
            "files": [item],
            "revision": "a" * 40,
            "license": "test",
        }
        with patch.object(h, "open", return_value=Response(header)) as request:
            with self.assertRaisesRegex(ValueError, "500B"):
                h.plan("org/demo", "demo.gguf", None)
            self.assertIn("Range", request.call_args.args[1])

    def test_external_model_is_never_loaded_by_import(self):
        p = self.root / "bad.gguf"
        p.write_bytes(gguf()[:-8])
        j = self.import_path(p)
        self.assertEqual("failed", j["state"])
        self.assertEqual([], self.cat.installed())
