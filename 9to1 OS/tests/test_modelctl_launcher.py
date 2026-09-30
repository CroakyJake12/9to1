"""Run the actual owning Python CLI through the source and installed launchers."""
from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest

OS_ROOT = Path(__file__).resolve().parents[1]
LAUNCHER = OS_ROOT / "packaging/bin/haven-modelctl"
RUNTIME = OS_ROOT.parent / "9to1 Workspace/Home/Source/Dulche/llamacpp"


class ModelctlLauncherTests(unittest.TestCase):
    def run_cli(self, launcher: Path, root: Path, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [str(launcher), "--model-root", str(root / "model store"),
             "--runtime-dir", str(root / "runtime dir"), *args],
            cwd=root, capture_output=True, text=True, timeout=30,
            env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"}, check=False)

    def test_source_launcher_preserves_literal_arguments_and_uses_real_store(self) -> None:
        with tempfile.TemporaryDirectory(prefix="model launcher ") as tmp:
            root = Path(tmp)
            source = root / "a model ' $(not-a-command).gguf"
            source.write_bytes(b"GGUF" + struct.pack("<I", 3) + b"launcher fixture")
            label = "A model ' with $literal and spaces"
            result = self.run_cli(LAUNCHER, root, "import", str(source), "--id", "wrapper-test",
                                  "--name", label, "--license", "MIT", "--origin", "local fixture")
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual("imported", json.loads(result.stdout)["status"])
            listed = self.run_cli(LAUNCHER, root, "list", "--verify")
            self.assertEqual(0, listed.returncode, listed.stderr)
            model = json.loads(listed.stdout)["models"][0]
            self.assertTrue(model["verified"])
            manifest = json.loads((root / "model store/manifests/wrapper-test.json").read_text())
            self.assertEqual(label, manifest["displayName"])
            self.assertTrue(source.exists())

    def test_installed_prefix_invokes_packaged_module_without_repository(self) -> None:
        with tempfile.TemporaryDirectory(prefix="installed model launcher ") as tmp:
            root = Path(tmp)
            launcher = root / "usr/bin/haven-modelctl"
            runtime = root / "usr/lib/haven/inference"
            launcher.parent.mkdir(parents=True)
            runtime.mkdir(parents=True)
            shutil.copy2(LAUNCHER, launcher)
            for name in ("modelctl.py", "gguf.py", "model_lease.py"):
                shutil.copy2(RUNTIME / name, runtime / name)
            result = self.run_cli(launcher, root, "list")
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual({"models": []}, json.loads(result.stdout))

    def test_model_manager_failure_exit_status_is_preserved(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            result = self.run_cli(LAUNCHER, Path(tmp), "import", "missing.gguf", "--id", "missing",
                                  "--name", "Missing", "--license", "MIT", "--origin", "local")
            self.assertEqual(2, result.returncode)
            self.assertEqual("model_manager_error", json.loads(result.stderr)["error"])

    def test_missing_runtime_fails_without_success_output(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            launcher = root / "usr/bin/haven-modelctl"
            launcher.parent.mkdir(parents=True)
            shutil.copy2(LAUNCHER, launcher)
            result = self.run_cli(launcher, root, "list")
            self.assertEqual(69, result.returncode)
            self.assertEqual("", result.stdout)
            self.assertIn("Repair", result.stderr)


if __name__ == "__main__":
    unittest.main()
