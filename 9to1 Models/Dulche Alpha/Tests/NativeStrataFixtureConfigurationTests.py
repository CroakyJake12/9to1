"""Subprocess admission regressions; only an explicitly pinned real relay supplies native success.

The sentinel executable is used ONLY in refused-input controls. Its marker must never be written.
Donor checks remain source/contract evidence; unavailable donor or native allocations are NOT RUN.
"""
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

TESTS = pathlib.Path(__file__).resolve().parent
NATIVE_WRAPPER = TESTS / "NativeStrataNativeFixtureTests.py"
CHILD_WRAPPER = TESTS / "NativeStrataChildOwnershipFixtureTests.py"
TRANSPORT_WRAPPER = TESTS / "NativeStrataCandidateTransportTests.py"


class FixtureConfigurationTests(unittest.TestCase):
    def _run(self, wrapper, case, overrides=None, timeout=60):
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(("DULCHE_NATIVE_", "DULCHE_CHILD_", "STRATA_"))}
        environment["PYTHONDONTWRITEBYTECODE"] = "1"
        environment.update(overrides or {})
        return subprocess.run([sys.executable, "-B", str(wrapper), case, "-v"],
                              capture_output=True, text=True, env=environment, timeout=timeout)

    def _native(self, overrides):
        return self._run(NATIVE_WRAPPER, "NativeFixtureTests", overrides)

    def _refused(self, result, reason):
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("BLOCKED:", result.stderr)
        self.assertIn(reason, result.stderr)
        self.assertNotIn("skipped=", result.stderr)

    def _sentinel(self, directory):
        marker = directory / "must-not-execute"
        binary = directory / "refused-input-only"
        binary.write_text("#!/usr/bin/env python3\nfrom pathlib import Path\n"
                          + "Path(" + repr(str(marker)) + ").write_text('executed')\n", encoding="utf-8")
        binary.chmod(0o700)
        return binary, marker

    def _real_candidate(self):
        binary = os.environ.get("DULCHE_NATIVE_CANDIDATE_BINARY")
        expected = os.environ.get("DULCHE_NATIVE_CANDIDATE_SHA256")
        if not binary or not expected:
            if os.environ.get("DULCHE_NATIVE_FIXTURE_REQUIRED") == "1":
                self.fail("Required configuration regression needs the actual measured CPU relay and SHA")
            self.skipTest("NOT RUN: actual pinned CPU relay not configured")
        binary = pathlib.Path(binary)
        self.assertEqual(hashlib.sha256(binary.read_bytes()).hexdigest(), expected.lower())
        return binary, expected

    def test_required_absent_binary_configuration_is_nonzero(self):
        self._refused(self._native({"DULCHE_NATIVE_FIXTURE_REQUIRED": "1"}),
                      "DULCHE_NATIVE_CANDIDATE_BINARY is required")

    def test_required_missing_binary_file_is_nonzero(self):
        with tempfile.TemporaryDirectory() as tmp:
            self._refused(self._native({"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                                       "DULCHE_NATIVE_CANDIDATE_BINARY": str(pathlib.Path(tmp) / "missing"),
                                       "DULCHE_NATIVE_CANDIDATE_SHA256": "0" * 64}), "binary is unavailable")

    def test_required_nonregular_binary_is_nonzero(self):
        with tempfile.TemporaryDirectory() as tmp:
            self._refused(self._native({"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                                       "DULCHE_NATIVE_CANDIDATE_BINARY": tmp,
                                       "DULCHE_NATIVE_CANDIDATE_SHA256": "0" * 64}), "regular executable file")

    def test_required_nonexecutable_binary_is_not_executed(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary, marker = self._sentinel(pathlib.Path(tmp))
            binary.chmod(0o600)
            self._refused(self._native({"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                                       "DULCHE_NATIVE_CANDIDATE_BINARY": str(binary),
                                       "DULCHE_NATIVE_CANDIDATE_SHA256": hashlib.sha256(binary.read_bytes()).hexdigest()}),
                          "regular executable file")
            self.assertFalse(marker.exists())

    def test_required_missing_malformed_and_mismatched_SHA_never_execute(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary, marker = self._sentinel(pathlib.Path(tmp))
            for supplied, reason in [(None, "exactly 64 hexadecimal digits"),
                                     ("not-a-sha", "exactly 64 hexadecimal digits"),
                                     ("g" * 64, "exactly 64 hexadecimal digits"),
                                     ("0" * 64, "SHA256 mismatch")]:
                with self.subTest(SHA=supplied):
                    values = {"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                              "DULCHE_NATIVE_CANDIDATE_BINARY": str(binary)}
                    if supplied is not None:
                        values["DULCHE_NATIVE_CANDIDATE_SHA256"] = supplied
                    self._refused(self._native(values), reason)
                    self.assertFalse(marker.exists())

    def test_tampered_actual_relay_is_refused(self):
        binary, expected = self._real_candidate()
        with tempfile.TemporaryDirectory() as tmp:
            tampered = pathlib.Path(tmp) / "tampered-relay"
            tampered.write_bytes(binary.read_bytes() + b"\nfixture tamper\n")
            tampered.chmod(0o700)
            self._refused(self._native({"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                                       "DULCHE_NATIVE_CANDIDATE_BINARY": str(tampered),
                                       "DULCHE_NATIVE_CANDIDATE_SHA256": expected}), "SHA256 mismatch")

    def test_optional_absence_counts_five_NOT_RUN_cases(self):
        result = self._native({})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("NOT RUN:", result.stderr)
        self.assertIn("skipped=5", result.stderr)

    def test_actual_pinned_CPU_relay_runs_all_five_wire_cases_without_skips(self):
        binary, expected = self._real_candidate()
        with tempfile.TemporaryDirectory() as tmp:
            sentinel, marker = self._sentinel(pathlib.Path(tmp))
            # Ambient overrides must not escape the private no-engine fixture layout.
            values = {"DULCHE_NATIVE_FIXTURE_REQUIRED": "1",
                      "DULCHE_NATIVE_CANDIDATE_BINARY": str(binary),
                      "DULCHE_NATIVE_CANDIDATE_SHA256": expected,
                      "STRATA_ENGINE_PATH": str(sentinel), "STRATA_HELPER_PATH": str(sentinel),
                      "STRATA_HELPER_DIR": tmp, "STRATA_MODEL_PATH": str(sentinel),
                      "STRATA_PYTHON": str(sentinel)}
            result = self._native(values)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("Ran 5 tests", result.stderr)
            self.assertNotIn("skipped", result.stderr)
            self.assertFalse(marker.exists())

    def test_denied_child_allocation_is_explicit_NOT_RUN(self):
        result = self._run(CHILD_WRAPPER, "ChildOwnershipFixtureRunTests")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("NOT RUN", result.stderr)
        self.assertIn("skipped=2", result.stderr)
        self.assertNotIn("fixture output directory:", result.stdout)

    def test_missing_compiler_is_explicit_BLOCKED_NOT_RUN(self):
        result = self._run(CHILD_WRAPPER, "ChildOwnershipFixtureRunTests",
                           {"DULCHE_CHILD_FIXTURE_ALLOWED": "1", "PATH": ""})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("BLOCKED:", result.stderr)
        self.assertIn("NOT RUN", result.stderr)
        self.assertIn("skipped=2", result.stderr)
        self.assertNotIn("fixture output directory:", result.stdout)

    def test_enabled_real_child_wrappers_honor_private_TMPDIR_and_explicit_root(self):
        if os.environ.get("DULCHE_CHILD_FIXTURE_ALLOWED") != "1" or shutil.which("g++") is None:
            self.skipTest("NOT RUN: explicit child fixture allocation and existing g++ are required")
        for explicit in (False, True):
            with self.subTest(explicit_root=explicit), tempfile.TemporaryDirectory() as tmp:
                root = pathlib.Path(tmp)
                values = {"DULCHE_CHILD_FIXTURE_ALLOWED": "1", "TMPDIR": tmp}
                if explicit:
                    values["DULCHE_CHILD_FIXTURE_BUILD_ROOT"] = tmp
                result = self._run(CHILD_WRAPPER, "ChildOwnershipFixtureRunTests", values, timeout=120)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("Ran 2 tests", result.stderr)
                self.assertNotIn("skipped", result.stderr)
                directories = [pathlib.Path(line.split(": ", 1)[1]) for line in result.stdout.splitlines()
                               if line.startswith("fixture output directory: ")]
                self.assertEqual(len(directories), 2, result.stdout)
                for directory in directories:
                    self.assertEqual(directory.parent, root)
                    self.assertFalse(directory.exists(), "owned temporary output must be cleaned")
                counts = re.findall(r"checks=(\d+) failures=(\d+)", result.stdout)
                self.assertEqual(len(counts), 2, result.stdout)
                self.assertTrue(all(int(count) > 0 and int(failures) == 0 for count, failures in counts))

    def test_required_unconfigured_donor_is_nonzero(self):
        result = self._run(TRANSPORT_WRAPPER, "WireContractSourceTests.test_donor_ready_shape",
                           {"DULCHE_NATIVE_DONOR_REQUIRED": "1"})
        self._refused(result, "required donor source is not configured")

    def test_explicit_missing_donor_is_nonzero(self):
        with tempfile.TemporaryDirectory() as tmp:
            result = self._run(TRANSPORT_WRAPPER, "WireContractSourceTests.test_donor_ready_shape",
                               {"DULCHE_NATIVE_DONOR_SOURCE": str(pathlib.Path(tmp) / "missing")})
            self._refused(result, "explicitly configured donor source is missing")

    def test_explicit_wrong_donor_root_and_file_are_nonzero(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            donor = root / "src" / "program" / "generate.cpp"
            donor.parent.mkdir(parents=True)
            donor.write_text("wrong donor revision\n", encoding="utf-8")
            for supplied in (root, donor):
                with self.subTest(source=str(supplied)):
                    result = self._run(TRANSPORT_WRAPPER, "WireContractSourceTests.test_donor_ready_shape",
                                       {"DULCHE_NATIVE_DONOR_SOURCE": str(supplied)})
                    self._refused(result, "donor source bytes do not match the current candidate lock")

    def test_same_size_wrong_donor_bytes_fail_the_real_lock_hash(self):
        lock_path = TESTS.parent / "InferenceEngines/NativeStrata/prep/strata-v0.2.2-lock.candidate.json"
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        pin = next(row for row in lock["rows"] if row["path"] == "src/program/generate.cpp")
        with tempfile.TemporaryDirectory() as tmp:
            donor = pathlib.Path(tmp) / "generate.cpp"
            donor.write_bytes(b"X" * pin["bytes"])
            result = self._run(TRANSPORT_WRAPPER, "WireContractSourceTests.test_donor_ready_shape",
                               {"DULCHE_NATIVE_DONOR_SOURCE": str(donor)})
            self._refused(result, "donor source bytes do not match the current candidate lock")


if __name__ == "__main__":
    unittest.main()
