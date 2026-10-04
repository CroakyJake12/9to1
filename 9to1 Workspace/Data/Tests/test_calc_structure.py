#!/usr/bin/env python3
"""Live Calc row/column structural-edit regression for Haven Data."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def read(worker: Worker, workbook_id: str, sheet: str, row: int, column: int, rows: int, columns: int):
    return worker.call(
        "readRange",
        {
            "workbookId": workbook_id,
            "range": {
                "sheet": sheet,
                "startRow": row,
                "startColumn": column,
                "rowCount": rows,
                "columnCount": columns,
            },
        },
    )["values"]


def set_cell(worker: Worker, workbook_id: str, sheet: str, row: int, column: int, value: str = "", formula: str = "") -> None:
    worker.call(
        "setCell",
        {
            "workbookId": workbook_id,
            "address": {"sheet": sheet, "row": row, "column": column},
            "value": value,
            "formula": formula,
        },
    )


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-structure-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved = directory / "structure-saved.ods"

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            worker.call(
                "createSheetWithValues",
                {
                    "workbookId": workbook_id,
                    "sheetName": "Structure",
                    "values": [["Label", "Value", "Double"], ["Ada", "", ""]],
                },
            )
            set_cell(worker, workbook_id, "Structure", 1, 1, value="5")
            set_cell(worker, workbook_id, "Structure", 1, 2, formula="=B2*2")
            worker.call("recalculate", {"workbookId": workbook_id})
            require(read(worker, workbook_id, "Structure", 1, 1, 1, 2)[0] == ["5", "10"], "Initial structural fixture formula is incorrect.")

            worker.call("insertRows", {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 1})
            set_cell(worker, workbook_id, "Structure", 2, 1, value="6")
            worker.call("recalculate", {"workbookId": workbook_id})
            require(
                read(worker, workbook_id, "Structure", 2, 1, 1, 2)[0] == ["6", "12"],
                "Calc did not preserve/shift the dependent formula when a row was inserted.",
            )

            worker.call("deleteRows", {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 1})
            set_cell(worker, workbook_id, "Structure", 1, 1, value="7")
            worker.call("recalculate", {"workbookId": workbook_id})
            require(
                read(worker, workbook_id, "Structure", 1, 1, 1, 2)[0] == ["7", "14"],
                "Calc did not preserve/shift the dependent formula when the inserted row was deleted.",
            )

            worker.call("insertColumns", {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 1})
            set_cell(worker, workbook_id, "Structure", 1, 2, value="8")
            worker.call("recalculate", {"workbookId": workbook_id})
            require(
                read(worker, workbook_id, "Structure", 1, 2, 1, 2)[0] == ["8", "16"],
                "Calc did not preserve/shift the dependent formula when a column was inserted.",
            )

            worker.call("deleteColumns", {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 1})
            set_cell(worker, workbook_id, "Structure", 1, 1, value="9")
            worker.call("recalculate", {"workbookId": workbook_id})
            require(
                read(worker, workbook_id, "Structure", 1, 1, 1, 2)[0] == ["9", "18"],
                "Calc did not preserve/shift the dependent formula when the inserted column was deleted.",
            )

            excessive = worker.expect_error(
                "insertRows",
                {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 101},
            )
            require("1-100" in excessive, "Calc structural mutation count limit was not enforced.")

            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": True})
            workbook_id = reopened["id"]
            require(
                read(worker, workbook_id, "Structure", 1, 1, 1, 2)[0] == ["9", "18"],
                "ODS save/reopen did not preserve the structurally shifted formula.",
            )
            read_only_error = worker.expect_error(
                "insertColumns",
                {"workbookId": workbook_id, "sheet": "Structure", "index": 1, "count": 1},
            )
            require("read-only" in read_only_error.lower(), "Read-only structural edit guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

        require(saved.exists() and saved.stat().st_size > 0, "Structural-edit ODS fixture was not saved.")

    print("Calc row/column structural edit regression passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
