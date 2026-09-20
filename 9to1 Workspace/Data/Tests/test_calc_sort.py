#!/usr/bin/env python3
"""Live single-key sort regression for Haven Data Calc."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-sort-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved = directory / "sorted.ods"

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheet = worker.call("listSheets", {"workbookId": workbook_id})[0]["name"]

            cells = [
                (0, 0, "Name"), (0, 1, "Score"),
                (1, 0, "Ada"), (1, 1, "2"),
                (2, 0, "Bob"), (2, 1, "10"),
                (3, 0, "Cara"), (3, 1, "5"),
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

            request = {
                "sheet": sheet,
                "startRow": 0,
                "startColumn": 0,
                "rowCount": 4,
                "columnCount": 2,
            }

            ascending = worker.call(
                "sortRange",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "ascending": True,
                    "containsHeader": True,
                },
            )
            require(ascending["values"][0] == ["Name", "Score"], f"Header moved during ascending sort: {ascending}")
            require(
                [row[0] for row in ascending["values"][1:]] == ["Ada", "Cara", "Bob"],
                f"Ascending numeric sort was wrong: {ascending}",
            )
            require(
                [row[1] for row in ascending["values"][1:]] == ["2", "5", "10"],
                f"Ascending key values were not ordered numerically: {ascending}",
            )

            descending = worker.call(
                "sortRange",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "ascending": False,
                    "containsHeader": True,
                },
            )
            require(
                [row[0] for row in descending["values"][1:]] == ["Bob", "Cara", "Ada"],
                f"Descending numeric sort was wrong: {descending}",
            )
            require(
                [row[1] for row in descending["values"][1:]] == ["10", "5", "2"],
                f"Descending key values were not ordered numerically: {descending}",
            )

            invalid_key = worker.expect_error(
                "sortRange",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 2,
                    "ascending": True,
                    "containsHeader": True,
                },
            )
            require("sort key" in invalid_key.lower(), "Worker accepted a sort key outside the requested range.")

            header_only = worker.expect_error(
                "sortRange",
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
                    "ascending": True,
                    "containsHeader": True,
                },
            )
            require("data row" in header_only.lower(), "Worker accepted a header-only sort range.")

            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": True})
            workbook_id = reopened["id"]
            persisted = worker.call("readRange", {"workbookId": workbook_id, "range": request})
            require(
                [row[0] for row in persisted["values"][1:]] == ["Bob", "Cara", "Ada"],
                f"ODS reopen changed sorted row order: {persisted}",
            )
            require(
                [row[1] for row in persisted["values"][1:]] == ["10", "5", "2"],
                f"ODS reopen changed sorted key order: {persisted}",
            )
            read_only = worker.expect_error(
                "sortRange",
                {
                    "workbookId": workbook_id,
                    "range": request,
                    "keyColumnOffset": 1,
                    "ascending": True,
                    "containsHeader": True,
                },
            )
            require("read-only" in read_only.lower(), "Read-only sort guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

        require(saved.exists() and saved.stat().st_size > 0, "Sorted ODS fixture was not saved.")

    print("Calc single-key sort regression passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
