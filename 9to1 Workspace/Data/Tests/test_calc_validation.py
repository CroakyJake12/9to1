#!/usr/bin/env python3
"""Live literal-list data-validation regression for Haven Data Calc."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def get_validation(worker: Worker, workbook_id: str, sheet: str, row: int, column: int, rows: int = 2) -> dict:
    return worker.call(
        "getListValidation",
        {
            "workbookId": workbook_id,
            "range": {
                "sheet": sheet,
                "startRow": row,
                "startColumn": column,
                "rowCount": rows,
                "columnCount": 1,
            },
        },
    )


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-validation-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved = directory / "validation.ods"
        allowed = ["Open", "Closed", "Pending"]

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheet = worker.call("listSheets", {"workbookId": workbook_id})[0]["name"]
            request = {
                "sheet": sheet,
                "startRow": 1,
                "startColumn": 2,
                "rowCount": 2,
                "columnCount": 1,
            }

            before = worker.call("getListValidation", {"workbookId": workbook_id, "range": request})
            require(not before["enabled"], f"Fixture unexpectedly began with Haven list validation: {before}")

            applied = worker.call(
                "applyListValidation",
                {"workbookId": workbook_id, "range": request, "values": allowed, "allowBlank": False},
            )
            require(applied["enabled"], "Calc did not enable literal list validation.")
            require(applied["values"] == allowed, f"Calc changed literal validation values: {applied}")
            require(applied["allowBlank"] is False, "Calc changed the allow-blank validation flag.")
            require(get_validation(worker, workbook_id, sheet, 1, 2)["values"] == allowed, "Calc did not read back the applied validation.")

            unsafe_value = worker.expect_error(
                "applyListValidation",
                {"workbookId": workbook_id, "range": request, "values": ["Safe", "Not;Safe"], "allowBlank": True},
            )
            require("semicolons" in unsafe_value.lower(), "Worker accepted a formula-sensitive validation list item.")

            worker.call("insertRows", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_validation(worker, workbook_id, sheet, 2, 2)["values"] == allowed, "Validation did not shift with inserted row.")
            worker.call("deleteRows", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_validation(worker, workbook_id, sheet, 1, 2)["values"] == allowed, "Validation did not shift back after row deletion.")

            worker.call("insertColumns", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_validation(worker, workbook_id, sheet, 1, 3)["values"] == allowed, "Validation did not shift with inserted column.")
            worker.call("deleteColumns", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_validation(worker, workbook_id, sheet, 1, 2)["values"] == allowed, "Validation did not shift back after column deletion.")

            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": True})
            workbook_id = reopened["id"]
            persisted = get_validation(worker, workbook_id, sheet, 1, 2)
            require(persisted["enabled"] and persisted["values"] == allowed and persisted["allowBlank"] is False,
                    f"ODS reopen changed list validation: {persisted}")
            read_only_clear = worker.expect_error("clearValidation", {"workbookId": workbook_id, "range": request})
            require("read-only" in read_only_clear.lower(), "Read-only validation clear guard was not enforced.")
            read_only_apply = worker.expect_error(
                "applyListValidation",
                {"workbookId": workbook_id, "range": request, "values": ["Yes", "No"], "allowBlank": True},
            )
            require("read-only" in read_only_apply.lower(), "Read-only validation apply guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

            writable = worker.call("open", {"path": str(saved), "readOnly": False})
            workbook_id = writable["id"]
            cleared = worker.call("clearValidation", {"workbookId": workbook_id, "range": request})
            require(not cleared["enabled"] and cleared["values"] == [], "Calc did not clear literal list validation.")
            require(not get_validation(worker, workbook_id, sheet, 1, 2)["enabled"], "Cleared validation still reads as enabled.")
            worker.call("close", {"workbookId": workbook_id})

        require(saved.exists() and saved.stat().st_size > 0, "List-validation ODS fixture was not saved.")

    print("Calc literal list validation regression passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
