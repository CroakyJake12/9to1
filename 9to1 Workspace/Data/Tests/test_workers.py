#!/usr/bin/env python3
"""Linux integration smoke tests for the Haven Data workers.

The tests use only disposable files. They prove worker protocol/runtime behavior on the
host that executes them; they do not claim CakeOS VM acceptance.
"""

from __future__ import annotations

import json
import os
import select
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
CALC_WORKER = ROOT / "apps" / "Data" / "workers" / "calc_worker.py"
DUCKDB_WORKER = ROOT / "apps" / "Data" / "workers" / "duckdb_worker.py"


class Worker:
    def __init__(self, script: Path, environment: dict[str, str] | None = None) -> None:
        env = os.environ.copy()
        if environment:
            env.update(environment)
        self.process = subprocess.Popen(
            [sys.executable, str(script)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
            env=env,
        )
        self.next_id = 0

    def call(self, method: str, params: dict | None = None, timeout: float = 30.0):
        if self.process.poll() is not None:
            raise AssertionError(f"Worker exited early: {self.process.stderr.read() if self.process.stderr else ''}")
        assert self.process.stdin is not None
        assert self.process.stdout is not None
        self.next_id += 1
        self.process.stdin.write(json.dumps({"id": self.next_id, "method": method, "params": params}) + "\n")
        self.process.stdin.flush()
        readable, _, _ = select.select([self.process.stdout], [], [], timeout)
        if not readable:
            self.process.kill()
            raise TimeoutError(f"Worker timed out handling {method}.")
        line = self.process.stdout.readline()
        if not line:
            stderr = self.process.stderr.read() if self.process.stderr else ""
            raise AssertionError(f"Worker closed stdout handling {method}: {stderr}")
        response = json.loads(line)
        if response.get("id") != self.next_id:
            raise AssertionError(f"Response id mismatch: {response}")
        if response.get("error") is not None:
            raise RuntimeError(response["error"])
        return response.get("result")

    def expect_error(self, method: str, params: dict | None = None) -> str:
        try:
            self.call(method, params)
        except RuntimeError as exc:
            return str(exc)
        raise AssertionError(f"Expected {method} to fail.")

    def close(self) -> None:
        if self.process.poll() is None:
            try:
                self.call("shutdown", timeout=10.0)
            except Exception:
                self.process.kill()
        try:
            self.process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=10)

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        self.close()
        return False


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def test_duckdb() -> None:
    with tempfile.TemporaryDirectory(prefix="haven-data-duckdb-test-") as directory:
        database = str(Path(directory) / "fixture.duckdb")
        with Worker(DUCKDB_WORKER) as worker:
            runtime = worker.call("runtimeInfo")
            require(runtime == {"engine": "duckdb", "version": "1.5.5"}, "DuckDB runtime version is not the proven dependency.")
            worker.call("open", {"databasePath": database})
            result = worker.call("query", {"sql": "SELECT 42 AS answer", "maxRows": 20})
            require(result["columns"] == ["answer"], "DuckDB column metadata mismatch.")
            require(result["rows"] == [["42"]], "DuckDB SELECT result mismatch.")

            limited = worker.call("query", {"sql": "SELECT * FROM range(5)", "maxRows": 2})
            require(len(limited["rows"]) == 2 and limited["truncated"], "DuckDB result cap was not enforced.")

            require("Only SELECT" in worker.expect_error("query", {"sql": "CREATE TABLE unsafe(i INTEGER)", "maxRows": 20}), "DDL was not rejected.")
            require("Multiple SQL statements" in worker.expect_error("query", {"sql": "SELECT 1; SELECT 2", "maxRows": 20}), "Multiple statements were not rejected.")
            external_error = worker.expect_error("query", {"sql": "SELECT * FROM read_csv('/etc/passwd')", "maxRows": 20})
            require("external" in external_error.lower() or "permission" in external_error.lower(), "External filesystem access was not rejected.")
            worker.call("close")

        require(Path(database).exists(), "DuckDB database file was not created.")


def make_fixture_ods(directory: Path) -> Path:
    soffice = shutil.which("soffice")
    if soffice is None:
        raise RuntimeError("soffice is unavailable.")
    csv_path = directory / "fixture.csv"
    csv_path.write_text("1,2\n3,4\n", encoding="utf-8")
    subprocess.run(
        [soffice, "--headless", "--convert-to", "ods", "--outdir", str(directory), str(csv_path)],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        timeout=60,
    )
    ods_path = directory / "fixture.ods"
    require(ods_path.exists() and ods_path.stat().st_size > 0, "LibreOffice did not create the fixture ODS.")
    return ods_path


def test_calc() -> None:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-test-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved_ods = directory / "saved.ods"
        saved_xlsx = directory / "saved.xlsx"

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheets = worker.call("listSheets", {"workbookId": workbook_id})
            require(len(sheets) >= 1, "Calc workbook exposed no sheets.")
            sheet = sheets[0]["name"]

            initial = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": sheet, "startRow": 0, "startColumn": 0, "rowCount": 2, "columnCount": 2}},
            )
            require(initial["values"][0] == ["1", "2"], "Calc failed to read the source grid.")

            worker.call("setCell", {"workbookId": workbook_id, "address": {"sheet": sheet, "row": 2, "column": 0}, "value": "5", "formula": ""})
            worker.call("setCell", {"workbookId": workbook_id, "address": {"sheet": sheet, "row": 2, "column": 1}, "value": "", "formula": "=A3*2"})
            worker.call("recalculate", {"workbookId": workbook_id})
            calculated = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": sheet, "startRow": 2, "startColumn": 0, "rowCount": 1, "columnCount": 2}},
            )
            require(calculated["values"][0] == ["5", "10"], f"Calc formula result mismatch: {calculated['values']}")

            materialized = worker.call(
                "createSheetWithValues",
                {
                    "workbookId": workbook_id,
                    "sheetName": "Query Result",
                    "values": [["value", "note"], ["=1+1", "literal"], ["42", "number-looking text"]],
                },
            )
            require(materialized["sheet"] == "Query Result", "Calc materialisation changed the sheet name.")
            require(materialized["values"][1][0] == "=1+1", "Formula-looking query output was evaluated instead of written literally.")
            duplicate_error = worker.expect_error(
                "createSheetWithValues",
                {"workbookId": workbook_id, "sheetName": "query result", "values": [["x"]]},
            )
            require("already contains" in duplicate_error.lower(), "Calc allowed a case-insensitive duplicate materialized sheet name.")

            in_place_error = worker.expect_error("save", {"workbookId": workbook_id, "destinationPath": str(source)})
            require("in-place overwrite is disabled" in in_place_error, "Calc in-place overwrite guard was not enforced.")
            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved_ods)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved_ods), "readOnly": False})
            workbook_id = reopened["id"]
            reopened_sheets = worker.call("listSheets", {"workbookId": workbook_id})
            sheet = reopened_sheets[0]["name"]
            reopened_values = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": sheet, "startRow": 2, "startColumn": 0, "rowCount": 1, "columnCount": 2}},
            )
            require(reopened_values["values"][0] == ["5", "10"], "ODS save/reopen did not preserve the edited formula.")
            require(any(item["name"] == "Query Result" for item in reopened_sheets), "ODS save/reopen lost the materialized query sheet.")
            reopened_materialized = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": "Query Result", "startRow": 0, "startColumn": 0, "rowCount": 3, "columnCount": 2}},
            )
            require(reopened_materialized["values"][1][0] == "=1+1", "ODS reopen converted literal query output into a formula.")
            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved_xlsx)})
            worker.call("close", {"workbookId": workbook_id})

            xlsx = worker.call("open", {"path": str(saved_xlsx), "readOnly": True})
            workbook_id = xlsx["id"]
            xlsx_sheets = worker.call("listSheets", {"workbookId": workbook_id})
            sheet = xlsx_sheets[0]["name"]
            xlsx_values = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": sheet, "startRow": 2, "startColumn": 0, "rowCount": 1, "columnCount": 2}},
            )
            require(xlsx_values["values"][0] == ["5", "10"], "XLSX save/reopen did not preserve the edited formula result.")
            require(any(item["name"] == "Query Result" for item in xlsx_sheets), "XLSX save/reopen lost the materialized query sheet.")
            xlsx_materialized = worker.call(
                "readRange",
                {"workbookId": workbook_id, "range": {"sheet": "Query Result", "startRow": 0, "startColumn": 0, "rowCount": 3, "columnCount": 2}},
            )
            require(xlsx_materialized["values"][1][0] == "=1+1", "XLSX reopen converted literal query output into a formula.")
            read_only_error = worker.expect_error(
                "setCell",
                {"workbookId": workbook_id, "address": {"sheet": sheet, "row": 0, "column": 0}, "value": "9", "formula": ""},
            )
            require("read-only" in read_only_error.lower(), "Calc read-only mutation guard was not enforced.")
            read_only_materialize = worker.expect_error(
                "createSheetWithValues",
                {"workbookId": workbook_id, "sheetName": "Blocked", "values": [["x"]]},
            )
            require("read-only" in read_only_materialize.lower(), "Calc read-only materialisation guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

        require(saved_ods.exists() and saved_ods.stat().st_size > 0, "Saved ODS is missing or empty.")
        require(saved_xlsx.exists() and saved_xlsx.stat().st_size > 0, "Saved XLSX is missing or empty.")


def main() -> int:
    test_duckdb()
    print("DuckDB worker integration checks passed.")
    test_calc()
    print("Calc worker integration checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
