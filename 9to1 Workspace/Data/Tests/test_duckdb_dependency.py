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
