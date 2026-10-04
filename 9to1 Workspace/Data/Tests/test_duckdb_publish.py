#!/usr/bin/env python3
"""Runtime checks for the privileged structured DuckDB publication path."""

from __future__ import annotations

import tempfile
from pathlib import Path

from test_workers import DUCKDB_WORKER, Worker, require


def main() -> int:
    with tempfile.TemporaryDirectory(prefix="haven-data-publish-test-") as directory:
        database = str(Path(directory) / "published.duckdb")
        with Worker(DUCKDB_WORKER) as worker:
            worker.call("open", {"databasePath": database})
            worker.call(
                "replaceTable",
                {
                    "table": {
                        "name": "WorkbookValues",
                        "columns": ["label", "value"],
                        "rows": [["A", "2"], ["B", "3"]],
                    }
                },
            )
            result = worker.call(
                "query",
                {"sql": 'SELECT SUM(CAST("value" AS INTEGER)) AS total FROM "WorkbookValues"', "maxRows": 20},
            )
            require(result["rows"] == [["5"]], "Published workbook values were not queryable through read-only SQL.")
            cte = worker.call("query", {"sql": 'WITH totals AS (SELECT COUNT(*) AS n FROM "WorkbookValues") SELECT n FROM totals', "maxRows": 20})
            require(cte["rows"] == [["2"]], "A parsed read-only CTE query was rejected.")
            keyword_literal = worker.call("query", {"sql": "SELECT 'UPDATE is text, not SQL' AS note", "maxRows": 20})
            require(keyword_literal["rows"] == [["UPDATE is text, not SQL"]], "SQL words inside string literals were misclassified.")

            # Replacement is an internal typed operation, not exposed as raw DDL.
            worker.call(
                "replaceTable",
                {
                    "table": {
                        "name": "WorkbookValues",
                        "columns": ["label", "value"],
                        "rows": [["C", "7"]],
                    }
                },
            )
            replaced = worker.call("query", {"sql": 'SELECT "label", "value" FROM "WorkbookValues"', "maxRows": 20})
            require(replaced["rows"] == [["C", "7"]], "Structured publication did not atomically replace the snapshot table.")

            # Quoted identifiers prove table/column names are not concatenated as executable SQL.
            worker.call(
                "replaceTable",
                {
                    "table": {
                        "name": 'quoted"table',
                        "columns": ['quoted"column'],
                        "rows": [["safe"]],
                    }
                },
            )
            quoted = worker.call("query", {"sql": 'SELECT "quoted""column" FROM "quoted""table"', "maxRows": 20})
            require(quoted["rows"] == [["safe"]], "Quoted structured identifiers were not preserved safely.")

            duplicate_error = worker.expect_error(
                "replaceTable",
                {"table": {"name": "Bad", "columns": ["Value", "value"], "rows": [["1", "2"]]}},
            )
            require("duplicated" in duplicate_error.lower(), "Duplicate publication columns were not rejected.")

            # Rejected SQL must not change the last valid database state.
            insert_error = worker.expect_error(
                "query", {"sql": 'INSERT INTO "WorkbookValues" VALUES (\'injected\', \'99\')', "maxRows": 20}
            )
            require("Only parsed" in insert_error, "Parsed DML was not rejected by the read-only query path.")
            unchanged = worker.call("query", {"sql": 'SELECT "label", "value" FROM "WorkbookValues"', "maxRows": 20})
            require(unchanged["rows"] == [["C", "7"]], "Rejected SQL changed the previously committed table state.")

            # Raw DDL remains unavailable despite the structured publication capability.
            ddl_error = worker.expect_error("query", {"sql": "DROP TABLE WorkbookValues", "maxRows": 20})
            require("Only parsed" in ddl_error, "Raw DDL became reachable after adding publication.")
            multi_error = worker.expect_error(
                "query", {"sql": 'SELECT 1; -- second statement follows\nDELETE FROM "WorkbookValues"', "maxRows": 20}
            )
            require("Multiple SQL" in multi_error, "A multi-statement plan was not rejected before execution.")
            worker.call("close")

        # Persistence gate: a fresh worker/process must be able to reopen the same
        # local database and observe the last committed structured snapshots.
        with Worker(DUCKDB_WORKER) as reopened_worker:
            reopened_worker.call("open", {"databasePath": database})
            persisted = reopened_worker.call(
                "query",
                {"sql": 'SELECT "label", "value" FROM "WorkbookValues"', "maxRows": 20},
            )
            require(persisted["rows"] == [["C", "7"]], "Structured publication did not survive database close/reopen.")
            quoted_persisted = reopened_worker.call(
                "query",
                {"sql": 'SELECT "quoted""column" FROM "quoted""table"', "maxRows": 20},
            )
            require(quoted_persisted["rows"] == [["safe"]], "Quoted structured publication did not survive close/reopen.")
            reopened_worker.call("close")

    print("DuckDB structured publication and close/reopen persistence checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
