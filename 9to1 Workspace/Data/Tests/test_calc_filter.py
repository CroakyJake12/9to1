#!/usr/bin/env python3
"""Live literal text-equality filter regression for Haven Data Calc."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-filter-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved = directory / "filtered.ods"

        request = {
            "sheet": "Sheet1",
            "startRow": 0,
            "startColumn": 0,
            "rowCount": 5,
            "columnCount": 2,
        }

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheet = worker.call("listSheets", {"workbookId": workbook_id})[0]["name"]
            request["sheet"] = sheet

            cells = [
                (0, 0, "Name"), (0, 1, "Category"),
                (1, 0, "Ada"), (1, 1, "Red"),
                (2, 0, "Bob"), (2, 1, "Blue"),
                (3, 0, "Cara"), (3, 1, "Red"),
                (4, 0, "Drew"), (4, 1, "Green"),
            ]
            for row, column, value in cells:
                worker.call(
                    "setCell",
                    {
                        "workbookId": workbook_id,
                        "address": {"sheet": sheet, "row": row, "column": column},
                        "value": value,
                        "formula": "",
                    },
                )

            baseline = worker.call("readRange", {"workbookId": workbook_id, "range": request})
            require(baseline["rowVisibility"] == [True, True, True, True, True], f"Unexpected baseline visibility: {baseline}")

            filtered = worker.call(
                "filterEquals",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "value": "red",
                    "containsHeader": True,
                },
            )
            require(filtered["values"] == baseline["values"], "Filtering changed cell values instead of row visibility.")
            require(
                filtered["rowVisibility"] == [True, True, False, True, False],
                f"Case-insensitive literal equality filter produced wrong row visibility: {filtered}",
            )

            invalid_key = worker.expect_error(
                "filterEquals",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 2,
                    "value": "Red",
                    "containsHeader": True,
                },
            )
            require("filter key" in invalid_key.lower(), "Worker accepted a filter key outside the requested range.")

            empty_value = worker.expect_error(
                "filterEquals",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "value": "",
                    "containsHeader": True,
                },
            )
            require("filter values" in empty_value.lower(), "Worker accepted an empty first-slice filter value.")

            header_only = worker.expect_error(
                "filterEquals",
                {
                    "workbookId": workbook_id,
                    "range": {
                        "sheet": sheet,
                        "startRow": 0,
                        "startColumn": 0,
                        "rowCount": 1,
                        "columnCount": 2,
                    },
                    "keyColumnOffset": 1,
                    "value": "Red",
                    "containsHeader": True,
                },
            )
            require("data row" in header_only.lower(), "Worker accepted a header-only filter range.")

            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": True})
            workbook_id = reopened["id"]
            persisted = worker.call("readRange", {"workbookId": workbook_id, "range": request})
            require(
                persisted["rowVisibility"] == [True, True, False, True, False],
                f"ODS reopen did not preserve filtered row visibility: {persisted}",
            )
            read_only = worker.expect_error(
                "filterEquals",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "value": "Blue",
                    "containsHeader": True,
                },
            )
            require("read-only" in read_only.lower(), "Read-only filter guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": False})
            workbook_id = reopened["id"]
            cleared = worker.call(
                "clearFilter",
                {"workbookId": workbook_id, "range": request, "containsHeader": True},
            )
            require(
                cleared["rowVisibility"] == [True, True, True, True, True],
                f"Clearing the first-slice filter did not restore row visibility: {cleared}",
            )
            worker.call("close", {"workbookId": workbook_id})

        require(saved.exists() and saved.stat().st_size > 0, "Filtered ODS fixture was not saved.")

    print("Calc literal text-equality filter regression passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
