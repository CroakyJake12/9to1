#!/usr/bin/env python3
"""Live range-backed named-range regression for Haven Data Calc."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import CALC_WORKER, Worker, make_fixture_ods, require


def get_named(worker: Worker, workbook_id: str, name: str) -> dict:
    ranges = worker.call("listNamedRanges", {"workbookId": workbook_id})
    for item in ranges:
        if item["name"].casefold() == name.casefold():
            return item
    raise AssertionError(f"Named range {name!r} was not listed: {ranges}")


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-calc-named-ranges-") as directory_text:
        directory = Path(directory_text)
        source = make_fixture_ods(directory)
        saved = directory / "named-ranges.ods"

        with Worker(CALC_WORKER) as worker:
            opened = worker.call("open", {"path": str(source), "readOnly": False})
            workbook_id = opened["id"]
            sheet = worker.call("listSheets", {"workbookId": workbook_id})[0]["name"]

            created = worker.call(
                "createNamedRange",
                {
                    "workbookId": workbook_id,
                    "name": "DataBlock",
                    "range": {
                        "sheet": sheet,
                        "startRow": 1,
                        "startColumn": 0,
                        "rowCount": 1,
                        "columnCount": 2,
                    },
                },
            )
            require(created["name"] == "DataBlock", "Calc changed the requested named-range name.")
            require(
                created["range"] == {
                    "sheet": sheet,
                    "startRow": 1,
                    "startColumn": 0,
                    "rowCount": 1,
                    "columnCount": 2,
                },
                f"Calc created the wrong named range: {created}",
            )

            duplicate = worker.expect_error(
                "createNamedRange",
                {
                    "workbookId": workbook_id,
                    "name": "datablock",
                    "range": {"sheet": sheet, "startRow": 0, "startColumn": 0, "rowCount": 1, "columnCount": 1},
                },
            )
            require("already contains" in duplicate.lower(), "Case-insensitive named-range duplicate guard was not enforced.")

            worker.call("insertRows", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            shifted_row = get_named(worker, workbook_id, "DataBlock")
            require(shifted_row["range"]["startRow"] == 2, f"Named range did not shift with inserted row: {shifted_row}")
            worker.call("deleteRows", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_named(worker, workbook_id, "DataBlock")["range"]["startRow"] == 1, "Named range did not shift back after row deletion.")

            worker.call("insertColumns", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            shifted_column = get_named(worker, workbook_id, "DataBlock")
            require(shifted_column["range"]["startColumn"] == 1, f"Named range did not shift with inserted column: {shifted_column}")
            worker.call("deleteColumns", {"workbookId": workbook_id, "sheet": sheet, "index": 0, "count": 1})
            require(get_named(worker, workbook_id, "DataBlock")["range"]["startColumn"] == 0, "Named range did not shift back after column deletion.")

            worker.call("save", {"workbookId": workbook_id, "destinationPath": str(saved)})
            worker.call("close", {"workbookId": workbook_id})

            reopened = worker.call("open", {"path": str(saved), "readOnly": True})
            workbook_id = reopened["id"]
            persisted = get_named(worker, workbook_id, "DataBlock")
            require(persisted["range"]["startRow"] == 1 and persisted["range"]["startColumn"] == 0, "ODS reopen changed the named range.")
            delete_read_only = worker.expect_error("deleteNamedRange", {"workbookId": workbook_id, "name": "DataBlock"})
            require("read-only" in delete_read_only.lower(), "Read-only named-range delete guard was not enforced.")
            worker.call("close", {"workbookId": workbook_id})

            writable = worker.call("open", {"path": str(saved), "readOnly": False})
            workbook_id = writable["id"]
            worker.call("deleteNamedRange", {"workbookId": workbook_id, "name": "datablock"})
            require(worker.call("listNamedRanges", {"workbookId": workbook_id}) == [], "Named range was not deleted case-insensitively.")
            worker.call("close", {"workbookId": workbook_id})

        require(saved.exists() and saved.stat().st_size > 0, "Named-range ODS fixture was not saved.")

    print("Calc range-backed named range regression passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
