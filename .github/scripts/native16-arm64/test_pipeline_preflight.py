from pathlib import Path
import importlib.util
import json
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("pipeline_preflight", Path(__file__).with_name("pipeline-preflight.py"))
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


class RealEnvironmentPreflightTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        identity = self.root / ".github/scripts/native16-arm64/native16-source-identity.json"
        identity.parent.mkdir(parents=True)
        identity.write_text(json.dumps({"changed_files_sha256": {}}))
        self.head = "1" * 40

    def execute(self, arguments):
        if arguments[:2] == ["dpkg", "--print-architecture"]:
            return "arm64"
        if arguments[:2] == ["dotnet", "--version"]:
            return "10.0.301"
        if arguments[0] == "git":
            return self.head
        return "pyatspi importable"

    def run_preflight(self, **overrides):
        arguments = dict(which=lambda name: "/usr/bin/" + name,
                         execute=self.execute, machine=lambda: "aarch64")
        arguments.update(overrides)
        with mock.patch.object(preflight.shutil, "disk_usage", return_value=type("Usage", (), {"free": 8 * 1024**3})()):
            return preflight.preflight(self.root, self.root, self.head, "10.0.301", **arguments)

    def test_environment_checkpoint_never_claims_native_or_release_success(self):
        receipt = self.run_preflight()
        self.assertEqual(receipt["status"], "CHECKPOINT")
        self.assertEqual(receipt["native_application_acceptance"], "NOT RUN")
        self.assertFalse(receipt["release_ready"])

    def test_wrong_machine_and_missing_tool_are_infrastructure_failures(self):
        receipt = self.run_preflight(machine=lambda: "x86_64", which=lambda name: None if name == "xdotool" else name)
        self.assertEqual(receipt["status"], "BLOCKED")
        self.assertEqual({row["check"] for row in receipt["failures"]}, {"native architecture", "native tools"})
        self.assertTrue(all(row["kind"] == "INFRASTRUCTURE" for row in receipt["failures"]))

    def test_wrong_sdk_or_incomplete_accessibility_dependency_fails_before_build(self):
        def execute(arguments):
            if arguments[0] == "dotnet":
                return "different SDK"
            if "/usr/bin/python3" in arguments:
                raise RuntimeError("pyatspi unavailable")
            return self.execute(arguments)
        receipt = self.run_preflight(execute=execute)
        self.assertEqual(receipt["status"], "BLOCKED")
        self.assertEqual(len(receipt["failures"]), 2)


if __name__ == "__main__":
    unittest.main()
