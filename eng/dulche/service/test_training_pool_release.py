"""The separately frozen trainer must carry its physical-claim dependencies."""
import importlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

import training_release


class FrozenClaimTests(unittest.TestCase):
    def test_frozen_supervisor_claim_works_without_service_source_imports(self):
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            source = root / "source"
            source.mkdir()
            service = Path(training_release.__file__).parent
            for name in ("training_jobs", "training_cycle", "training_budget", "training_worker_exec",
                         "training_worker_client", "dulchectl", "engine_profiles", "physical_admission"):
                shutil.copyfile(service / (name + ".py"), source / (name + ".py"))
            for name in ("pooled_runtime", "pooled_backend", "pooled_budget", "pooled_health"):
                shutil.copyfile(importlib.import_module(name).__file__, source / (name + ".py"))
            shutil.copytree(service / "custody", source / "custody", ignore=shutil.ignore_patterns("__pycache__"))
            release = Path(training_release.prepare(source, root / "releases")["path"])
            runtime = root / "run"
            runtime.mkdir(mode=0o700)
            script = """import os, sys
from pathlib import Path
sys.path.insert(0, str(Path.cwd()))
import physical_admission, pooled_runtime, pooled_backend, pooled_budget, pooled_health
for module in (physical_admission, pooled_runtime, pooled_backend, pooled_budget, pooled_health):
    assert Path(module.__file__).parent == Path.cwd()
with physical_admission.training_claim(pooled_runtime.RTX_UUID):
    try:
        pooled_runtime.assert_devices_available(['rtx5080'])
    except RuntimeError:
        pass
    else:
        raise AssertionError('Frozen trainer did not retain its GPU claim')
pooled_runtime.assert_devices_available(['rtx5080'])
"""
            result = subprocess.run([sys.executable, "-I", "-B", "-c", script], cwd=release,
                                    env={**os.environ, "PYTHONPATH": "/not-a-service-source",
                                         "XDG_RUNTIME_DIR": str(runtime)},
                                    capture_output=True, text=True, timeout=10)
            self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
