#!/usr/bin/env python3
"""Dependency-policy tests for the real DuckDB worker entry point."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import types
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch


WORKER = Path(__file__).resolve().parents[1] / "workers" / "duckdb_worker.py"


def _load_worker(version: object):
    module_name = f"haven_data_duckdb_worker_{uuid.uuid4().hex}"
    specification = importlib.util.spec_from_file_location(module_name, WORKER)
    if specification is None or specification.loader is None:
        raise AssertionError("DuckDB worker module could not be loaded.")
    module = importlib.util.module_from_spec(specification)
    dependency = types.SimpleNamespace(__version__=version)
    with patch.dict(sys.modules, {"duckdb": dependency}):
        specification.loader.exec_module(module)
    return module


class DuckDbDependencyTests(unittest.TestCase):
    def test_accepted_version_is_exposed_by_the_worker(self) -> None:
        worker = _load_worker("1.5.5")

        self.assertEqual(worker.DUCKDB_RUNTIME_VERSION, "1.5.5")
        self.assertEqual(worker._validate_duckdb_version("1.5.5"), "1.5.5")

    def test_unproven_version_fails_before_worker_serves_requests(self) -> None:
        output = io.StringIO()
        with contextlib.redirect_stdout(output), patch.dict(
            sys.modules, {"duckdb": types.SimpleNamespace(__version__="1.6.0")}
        ):
            with self.assertRaisesRegex(SystemExit, "78"):
                _load_worker("1.6.0")

        self.assertIn("DuckDB 1.5.5 is required", output.getvalue())


class DuckDbLockAgreementTests(unittest.TestCase):
    """The pinned artifact set must agree with the version the worker actually enforces."""

    WORKERS = WORKER.parent
    LOCKS = sorted((WORKERS / "locks").glob("*.txt")) if (WORKERS / "locks").is_dir() else []

    def test_requirements_declaration_pins_exactly_the_worker_version(self) -> None:
        declaration = self.WORKERS / "requirements.in"
        self.assertTrue(declaration.is_file(), "workers/requirements.in is missing")
        worker = _load_worker("1.5.5")
        pins = [line.strip() for line in declaration.read_text().splitlines()
                if line.strip() and not line.strip().startswith("#")]
        self.assertEqual(pins, [f"duckdb=={worker.REQUIRED_DUCKDB_VERSION}"],
                         "requirements.in must pin exactly the version the worker requires")

    def test_locks_are_hash_locked_and_agree_with_the_declaration(self) -> None:
        self.assertTrue(self.LOCKS, "workers/locks/*.txt resolved artifacts are missing")
        worker = _load_worker("1.5.5")
        for path in self.LOCKS:
            text = path.read_text()
            pinned = [line.strip().rstrip("\\").strip() for line in text.splitlines()
                      if line.strip().startswith("duckdb==")]
            self.assertEqual(pinned, [f"duckdb=={worker.REQUIRED_DUCKDB_VERSION}"],
                             f"{path.name} disagrees with the worker's required version")
            digests = [line.strip()[len("--hash="):] for line in text.splitlines()
                       if line.strip().startswith("--hash=")]
            self.assertTrue(digests, f"{path.name} carries no --hash entry, so it is not hash-locked")
            for digest in digests:
                self.assertRegex(digest, r"^sha256:[0-9a-f]{64}$", f"{path.name}: malformed hash")

    def test_both_target_platforms_are_represented(self) -> None:
        # This development host is x86-64 and the test-package target is ARM64. A lock covering only one
        # of them would silently fall back to whatever the machine already has installed.
        names = [path.name for path in self.LOCKS]
        self.assertTrue(any("aarch64" in name for name in names), f"no aarch64 lock among {names}")
        self.assertTrue(any("x86_64" in name for name in names), f"no x86_64 lock among {names}")

    def test_no_range_pinning_is_accepted(self) -> None:
        # If the worker ever relaxes to a range while the lock stays exact (or the reverse), the
        # agreement test above is what notices. Range operators on a requirement line would defeat it.
        # Comments are excluded because they legitimately mention placeholder paths like <wheelhouse>.
        lines = [line.strip() for line in (self.WORKERS / "requirements.in").read_text().splitlines()
                 if line.strip() and not line.strip().startswith("#")]
        joined = " ".join(lines)
        for operator in (">=", "<=", "~=", "!=", ">", "<"):
            self.assertNotIn(operator, joined, f"requirements.in must not use {operator}")
        self.assertEqual(lines, ["duckdb==1.5.5"])


if __name__ == "__main__":
    unittest.main()
