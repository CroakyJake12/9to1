#!/usr/bin/env python3
"""Regressions for two reviewed workflows' job-level runner context failure.

This scans their conventional block indentation, not arbitrary YAML semantics.
GitHub supplies runner context to steps, but rejects it in jobs.<id>.env before
any job starts. Environment paths needed across steps are set with GITHUB_ENV.
"""
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest


WORKFLOWS = {
    "canvas-rnote-poc.yml": {"CARGO_TARGET_DIR": "canvas-rnote-target", "DIAGNOSTICS": "canvas-rnote-evidence"},
    "havenos-linux-package.yml": {"PREVIEW_INPUT_CACHE": "9to1-pinned-inputs"},
}


def job_env_runner_expressions(source):
    """Locate runner references in job env blocks of this reviewed format."""
    lines = source.splitlines()
    in_jobs = False
    errors = []
    for index, line in enumerate(lines):
        if line == "jobs:":
            in_jobs = True
        elif line and not line[0].isspace() and not line.startswith("#"):
            in_jobs = False
        if not in_jobs or not re.match(r"^    env:", line):
            continue
        block = [line]
        for following in lines[index + 1:]:
            if following.strip() and not following.lstrip().startswith("#") and len(following) - len(following.lstrip()) <= 4:
                break
            block.append(following)
        text = "\n".join(block)
        for expression in re.finditer(r"\$\{\{[\s\S]*?\}\}", text):
            if re.search(r"\brunner\s*\.", expression.group()):
                errors.append((index + 1 + text[:expression.start()].count("\n"), expression.group()))
    return errors


class WorkflowRunnerContextPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(__file__).resolve().parents[2] / ".github" / "workflows"
        cls.workflows = {name: (root / name).read_text() for name in WORKFLOWS}

    def test_current_job_environment_has_no_runner_expressions(self):
        for name, source in self.workflows.items():
            with self.subTest(workflow=name):
                self.assertEqual([], job_env_runner_expressions(source),
                                 "job-level runner context must not prevent workflow validation")

    def test_former_block_inline_and_multiline_job_values_are_rejected(self):
        for value in ("    env:\n      PATH: ${{ runner.temp }}/inputs",
                      "    env: {PATH: '${{ runner.temp }}/inputs'}",
                      "    env:\n      PATH: >-\n        ${{\n          runner.temp\n        }}/inputs"):
            with self.subTest(value=value):
                self.assertTrue(job_env_runner_expressions("jobs:\n  product:\n" + value + "\n    steps: []\n"))

    def test_step_environment_inputs_and_legal_job_contexts_remain_allowed(self):
        source = """jobs:
  product:
    env:
      SOURCE: ${{ github.sha }}
      TARGET: ${{ matrix.target }}
      LITERAL: runner.temp
    steps:
      - name: Capture runner input
        env:
          TEMP: ${{ runner.temp }}
          EXISTING: ${{ env.PATH }}
        run: echo "$TEMP"
      - uses: actions/cache/restore@reviewed
        with:
          path: ${{ runner.temp }}/inputs
"""
        self.assertEqual([], job_env_runner_expressions(source))

    def test_actual_initializers_set_current_shell_and_later_step_environment(self):
        for name, variables in WORKFLOWS.items():
            with self.subTest(workflow=name), tempfile.TemporaryDirectory(prefix="runner-context-policy-") as temporary:
                source = self.workflows[name]
                assignments = [line.strip() for line in source.splitlines()
                               if any(line.strip().startswith("export " + key + "=") for key in variables)]
                propagation = [line.strip() for line in source.splitlines()
                               if "printf " in line and '"$GITHUB_ENV"' in line
                               and any(key in line for key in variables)]
                self.assertEqual(len(variables), len(assignments), "one local assignment for each runtime path")
                self.assertEqual(1, len(propagation), "one propagation of initialized paths")
                runner_temp = Path(temporary) / "runner temp with spaces"
                environment_file = Path(temporary) / "GITHUB_ENV"
                runner_temp.mkdir()
                script = "\n".join(assignments + propagation) + "\n"
                script += "printf '%s\\n' " + " ".join('"$' + key + '"' for key in variables) + "\n"
                result = subprocess.run(["bash", "-euo", "pipefail", "-c", script], check=True, text=True,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        env={"PATH": os.defpath, "RUNNER_TEMP": str(runner_temp),
                                             "GITHUB_ENV": str(environment_file)})
                expected = {key: str(runner_temp / leaf) for key, leaf in variables.items()}
                self.assertEqual(list(expected.values()), result.stdout.splitlines())
                self.assertEqual([key + "=" + value for key, value in expected.items()],
                                 environment_file.read_text().splitlines())


if __name__ == "__main__":
    unittest.main()
