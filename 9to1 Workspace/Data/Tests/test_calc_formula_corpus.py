#!/usr/bin/env python3
"""First Haven Data Calc formula-compatibility corpus.

This is intentionally small and deterministic. It establishes a regression baseline for
formula semantics that existed in the donor Data experience without making the donor
formula engine authoritative in CakeOS.
"""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-formula-corpus-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheet = worker.call("listSheets", {"workbookId": workbook_id})[0]["name"]

            for row, value in enumerate(("2", "3", "4")):
                worker.call(
                    "setCell",
                    {
                        "workbookId": workbook_id,
                        "address": {"sheet": sheet, "row": row, "column": 0},
                        "value": value,
                        "formula": "",
                    },
                )

            cases = [
                ("=A1+A2*A3", "14"),
                ("=SUM(A1:A3)", "9"),
                ("=$A$1+A2", "5"),
                ("=A1^3", "8"),
                ("=AVERAGE(A1:A3)", "3"),
                ("=COUNT(A1:A3)", "3"),
                ("=MIN(A1:A3)", "2"),
                ("=MAX(A1:A3)", "4"),
            ]

            for row, (formula, _) in enumerate(cases):
                result = worker.call(
                    "setCell",
                    {
                        "workbookId": workbook_id,
                        "address": {"sheet": sheet, "row": row, "column": 1},
                        "value": "",
                        "formula": formula,
                    },
                )
                require(result["formula"].startswith("="), f"Calc did not retain formula syntax for {formula}.")

            worker.call("recalculate", {"workbookId": workbook_id})
            values = worker.call(
                "readRange",
                {
                    "workbookId": workbook_id,
                    "range": {
                        "sheet": sheet,
                        "startRow": 0,
                        "startColumn": 1,
                        "rowCount": len(cases),
                        "columnCount": 1,
                    },
                },
            )["values"]

            for index, (formula, expected) in enumerate(cases):
                actual = values[index][0]
                require(actual == expected, f"Formula {formula} produced {actual!r}; expected {expected!r}.")

            worker.call("close", {"workbookId": workbook_id})

    print(f"Calc formula compatibility corpus passed ({len(cases)} cases).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
