"""Software-only deployment fixtures; no real corpus, model or service is made."""
import copy
import hashlib
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch

from alora import workflow


def fixture_bundle(root):
    """A software_test_only file-protocol bundle; never register or execute it."""
    root = Path(root)
    source = root / "fixture-source"
    (source / "alora").mkdir(parents=True)
    (source / "custody").mkdir()
    for name in ("alora/__init__.py", "alora/runner.py", "alora/workflow.py", "custody/__init__.py"):
        (source / name).write_text('"""software_test_only source pin fixture"""\n')
    (source / "alora/test_excluded.py").write_text("raise AssertionError('not execution code')\n")
    inputs = root / "fixture-inputs"
    inputs.mkdir()
    for name in ("config", "profile", "dataset"):
        (inputs / (name + ".json")).write_text(json.dumps({"purpose": "software_test_only", "fixture": name}))
    (inputs / "control.py").write_text('"""software_test_only control source fixture"""\n')
    shard = inputs / "numeric-fixture.gguf"
    shard.write_bytes(b"software_test_only numeric bytes; not model weights")
    venv = root / "fixture-venv"
    (venv / "bin").mkdir(parents=True)
    (venv / "bin/python").symlink_to(Path(sys.executable).resolve())
    (venv / "pyvenv.cfg").write_text("home = /usr/bin\ninclude-system-site-packages = false\n")
    settings = {
        "target_updates": 2, "experts_per_layer": 1, "rank": 2, "seed": 45, "threads": 1,
        "chunk_rows": 8, "max_decoded_bytes": 1024 * 1024, "max_read_bytes": 1024 * 1024,
        "max_fingerprints": 32, "loss_token_rows": 2, "alpha": 4,
        "learning_rate": 0.01, "max_grad_norm": 1.0,
    }
    limits = {
        "ram_mib": 512, "min_free_ram_mib": 512, "checkpoint_reserve_bytes": 8_000_000,
        "max_checkpoint_bytes": 2_000_000, "wall_seconds": 300,
        "progress_timeout_seconds": 60, "checkpoint_grace_seconds": 20,
        "startup_timeout_seconds": 120, "vram_mib": 0,
    }
    request = {
        "schema": workflow.REQUEST_SCHEMA, "purpose": "software_test_only",
        "id": "software-alora-fixture", "model": "software_test_only numeric fixture",
        "python": {"path": str(venv / "bin/python"),
                   "sha256": workflow.artifact(Path(sys.executable).resolve())["sha256"]},
        "control_client": workflow.artifact(inputs / "control.py"),
        "dataset": {"manifest": workflow.artifact(inputs / "dataset.json"),
                    "root": str(inputs), "vocab_size": 32, "max_tokens": 3},
        "config": workflow.artifact(inputs / "config.json"),
        "profile": workflow.artifact(inputs / "profile.json"),
        "profile_record_sha256": "c" * 64,
        "shards": [{"path": str(shard), "sha256": hashlib.sha256(shard.read_bytes()).hexdigest(),
                    "size_bytes": shard.stat().st_size}],
        "settings": settings, "limits": limits,
    }
    output = root / "deployment"
    result = workflow._emit_bundle(request, output, request_sha256="d" * 64, source_root=source)
    spec = workflow.read_json(result["deployment"]["path"])
    return request, result, spec, source


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="alora-workflow-software-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()

    def test_public_preparation_rejects_software_corpus_without_creating_deployment(self):
        request, _, _, _ = fixture_bundle(self.root)
        path = self.root / "request.json"
        path.write_bytes(workflow.canonical(request))
        destination = self.root / "must-not-exist"
        with patch.object(workflow, "_emit_bundle") as emit, self.assertRaises(ValueError):
            workflow.prepare_workflow(path, request_sha256=workflow.artifact(path)["sha256"],
                                      output=destination)
        emit.assert_not_called()
        self.assertFalse(destination.exists())

    def test_every_execution_input_is_pinned_without_fresh_large_shard_hash_claim(self):
        request, result, spec, _ = fixture_bundle(self.root)
        registered = workflow.read_json(result["workflow"]["path"])
        self.assertEqual(registered["purpose"], "software_test_only")
        self.assertEqual(registered["worker_protocol"], "checkpoint-yield-v1")
        self.assertEqual(registered["device"], "cpu")
        for pin in registered["inputs"]:
            self.assertEqual(workflow.artifact(pin["path"]), pin)
        self.assertIn(spec["config"], registered["inputs"])
        self.assertIn(spec["profile"], registered["inputs"])
        self.assertIn(spec["control_client"], registered["inputs"])
        self.assertNotIn(request["shards"][0]["path"], {pin["path"] for pin in registered["inputs"]})
        self.assertIn(str(Path(request["shards"][0]["path"]).parent), registered["read_only_roots"])
        self.assertFalse(spec["deployment"]["fresh_full_file_hash_verified"])
        self.assertFalse(result["registered"])
        self.assertFalse(result["launched"])

    def test_regular_launcher_preserves_venv_shebang_and_pins_resolved_interpreter(self):
        request, result, spec, _ = fixture_bundle(self.root)
        launcher = Path(result["executable"]["path"])
        self.assertFalse(launcher.is_symlink())
        self.assertTrue(launcher.stat().st_mode & stat.S_IXUSR)
        self.assertEqual(launcher.read_text().splitlines()[0], "#!" + request["python"]["path"] + " -I")
        self.assertEqual(spec["deployment"]["python_resolved"]["path"],
                         str(Path(sys.executable).resolve()))
        self.assertTrue(Path(request["python"]["path"]).is_symlink())
        registered = workflow.read_json(result["workflow"]["path"])
        self.assertEqual(registered["arguments"],
                         ["--spec", result["deployment"]["path"], "--spec-sha256",
                          result["deployment"]["sha256"]])
        self.assertNotIn("-c", registered["arguments"])

    def test_copied_sources_are_stable_and_test_files_are_excluded(self):
        _, _, spec, source = fixture_bundle(self.root)
        pins = spec["deployment"]["sources"]
        self.assertFalse(any(Path(pin["path"]).name.startswith("test_") for pin in pins))
        original = source / "alora/runner.py"
        original.write_text('"""changed original software fixture"""\n')
        for pin in pins:
            self.assertEqual(workflow.artifact(pin["path"]), pin)
        copied = Path(spec["deployment"]["source_root"]) / "alora/runner.py"
        self.assertNotEqual(copied.read_bytes(), original.read_bytes())

    def test_preparation_never_overwrites_an_existing_deployment(self):
        request, result, _, source = fixture_bundle(self.root)
        path = Path(result["workflow"]["path"])
        prior = path.read_bytes()
        with self.assertRaises(FileExistsError):
            workflow._emit_bundle(request, path.parent, request_sha256="d" * 64, source_root=source)
        self.assertEqual(path.read_bytes(), prior)

    def test_input_tampering_and_symbolic_aliases_are_refused(self):
        path = self.root / "numeric.json"
        path.write_text('{"purpose":"software_test_only","value":1}')
        pin = workflow.artifact(path)
        path.write_text('{"purpose":"software_test_only","value":2}')
        with self.assertRaises(ValueError):
            workflow.verify_artifact(pin)
        alias = self.root / "alias.json"
        alias.symlink_to(path)
        with self.assertRaises(ValueError):
            workflow.verify_artifact({"path": str(alias), "sha256": workflow.artifact(path)["sha256"]})

    def test_duplicate_nonfinite_and_wrong_raw_json_pins_are_refused(self):
        path = self.root / "input.json"
        for content in ('{"x":1,"x":2}', '{"x":NaN}', '{"x":Infinity}'):
            path.write_text(content)
            with self.subTest(content=content), self.assertRaises(ValueError):
                workflow.read_json(path)
        path.write_text('{"purpose":"software_test_only"}')
        with self.assertRaises(ValueError):
            workflow.read_json(path, sha256="0" * 64)

    def test_pretending_software_inputs_are_real_does_not_bypass_corpus_admission(self):
        request, _, _, _ = fixture_bundle(self.root)
        request["purpose"] = "real_training"  # Deliberate invalid relabelling attack.
        from alora import test_data
        fixture = test_data.TrainingDatasetTests()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.seal()
        request["dataset"] = {"manifest": workflow.artifact(fixture.manifest_path),
                              "root": str(fixture.root), "vocab_size": 32, "max_tokens": 3}
        config_path = self.root / "software-config.json"
        config_path.write_bytes(workflow.canonical({"vocab_size": 32, "max_position_embeddings": 16,
                                                   "num_hidden_layers": 2, "num_experts": 4}))
        request["config"] = workflow.artifact(config_path)
        with self.assertRaises(ValueError):
            workflow.validate_request(request)

    def test_boolean_limits_and_oversized_loss_tiles_fail_before_model_or_data_loading(self):
        request, _, _, _ = fixture_bundle(self.root)
        request["purpose"] = "real_training"  # Validation-failure fixture, never admitted.
        for key, value in (("threads", True), ("max_fingerprints", 0), ("loss_token_rows", 4097)):
            invalid = copy.deepcopy(request)
            invalid["settings"][key] = value
            with self.subTest(setting=key), self.assertRaises(ValueError):
                workflow.validate_request(invalid)
        invalid = copy.deepcopy(request)
        invalid["settings"].update(loss_token_rows=4096, chunk_rows=4096)
        with self.assertRaises(ValueError):
            workflow.validate_request(invalid)
        invalid = copy.deepcopy(request)
        invalid["limits"]["vram_mib"] = 1
        with self.assertRaises(ValueError):
            workflow.validate_request(invalid)


if __name__ == "__main__":
    unittest.main()
