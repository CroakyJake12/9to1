import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

from alora.__main__ import main
from alora.profile import ActivationProfiler
from alora.curriculum import CATEGORY_WEIGHTS


class CommandTests(unittest.TestCase):
    def test_exact_curriculum_and_existing_plan_preservation(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            self.assertEqual(main(["curriculum", "--examples", "10000"]), 0)
        self.assertEqual(json.loads(output.getvalue())["category_counts"]["ui"], 2500)
        profile = ActivationProfiler({"layer.experts": 4}, base_model_sha256="a" * 64)
        profile.bind_dataset({"manifest_sha256": "b" * 64, "dataset_sha256": "c" * 64,
                              "purpose": "software_test_only", "format": "alora-pretokenized-jsonl-v1"})
        for i, category in enumerate(CATEGORY_WEIGHTS):
            profile.record("layer.experts", category, str(i), [[1, 2]], [[.8, .2]], [1])
        record = profile.to_record()
        with tempfile.TemporaryDirectory(prefix="alora-cli-software-") as folder:
            path = Path(folder)
            source, destination = path / "profile.json", path / "plan.json"
            source.write_text(json.dumps(record))
            command = ["plan", "--profile", str(source), "--profile-sha256", record["sha256"],
                       "--experts-per-layer", "1", "--rank", "2", "--alpha", "4",
                       "--output", str(destination)]
            with contextlib.redirect_stdout(io.StringIO()):
                main(command)
            plan = json.loads(destination.read_text())
            self.assertEqual(plan["selections"], {"layer.experts": [1]})
            self.assertEqual(plan["purpose"], "software_test_only")
            before = destination.read_bytes()
            with self.assertRaises(FileExistsError):
                main(command)
            self.assertEqual(destination.read_bytes(), before)
            source.write_text(json.dumps({**record, "purpose": "real_training"}))
            with self.assertRaises(ValueError):
                main(command)
            self.assertEqual(destination.read_bytes(), before)
