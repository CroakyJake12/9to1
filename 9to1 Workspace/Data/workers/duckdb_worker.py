#!/usr/bin/env python3
"""Haven Data DuckDB worker.

First-slice worker: one local database, structured table publication, read-only
user SQL execution, and no external access. Requires the pinned DuckDB Python
package at runtime.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

REQUIRED_DUCKDB_VERSION = "1.5.5"


def _validate_duckdb_version(version: object) -> str:
    if not isinstance(version, str) or version != REQUIRED_DUCKDB_VERSION:
        raise RuntimeError(
            f"DuckDB {REQUIRED_DUCKDB_VERSION} is required; found {version!r}."
        )
    return version


try:
    import duckdb
    DUCKDB_RUNTIME_VERSION = _validate_duckdb_version(getattr(duckdb, "__version__", None))
except Exception as exc:  # pragma: no cover - runtime dependency gate
    print(json.dumps({"id": 0, "error": f"duckdb is unavailable: {exc}", "result": None}), flush=True)
    raise SystemExit(78)


# Parsed statement types, rather than leading-keyword heuristics, are the
# authority for whether a query is eligible for this read-only execution path.
# DuckDB SELECT expressions can still mutate sequence state or invoke SQL
# dynamically, so those capabilities are denied explicitly as well.
READ_ONLY_STATEMENT_TYPES = {"SELECT", "EXPLAIN"}
MUTATING_OR_DYNAMIC_FUNCTIONS = re.compile(
    r"\b(NEXTVAL|CURRVAL|SETVAL|QUERY|QUERY\_TABLE)\s*\(", re.IGNORECASE
)
MEMORY_LIMIT = re.compile(r"^[1-9][0-9]*(?:\.[0-9]+)?\s*(?:KB|MB|GB)$", re.IGNORECASE)
MAX_PUBLISHED_ROWS = 10_000
MAX_PUBLISHED_COLUMNS = 256
MAX_PUBLISHED_CELLS = 500_000
MAX_IDENTIFIER_LENGTH = 128


def _printable_identifier(value: object, field: str) -> str:
    text = str(value or "").strip()
    if not text or len(text) > MAX_IDENTIFIER_LENGTH or any(ord(character) < 32 or ord(character) == 127 for character in text):
        raise ValueError(f"{field} must contain 1-{MAX_IDENTIFIER_LENGTH} printable characters.")
    return text


def _quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


class DuckDbRuntime:
    def __init__(self) -> None:
        self.connection = None
        self.path = None

    def open(self, database_path: str) -> dict:
        if self.connection is not None:
            self.close()
        if not database_path or not database_path.strip():
            raise ValueError("databasePath is required.")

        full = str(Path(database_path).expanduser().resolve())
        Path(full).parent.mkdir(parents=True, exist_ok=True)
        connection = duckdb.connect(full)
        try:
            memory_limit = os.environ.get("HAVEN_DATA_DUCKDB_MEMORY_LIMIT", "512MB").strip()
            if not MEMORY_LIMIT.fullmatch(memory_limit):
                raise ValueError("HAVEN_DATA_DUCKDB_MEMORY_LIMIT must be a positive KB/MB/GB value.")
            threads = int(os.environ.get("HAVEN_DATA_DUCKDB_THREADS", "2"))
            if threads < 1 or threads > 8:
                raise ValueError("HAVEN_DATA_DUCKDB_THREADS must be between 1 and 8.")

            # Configure every capability before locking configuration. These settings are
            # defense in depth; the worker must still run inside an OS sandbox before ship.
            connection.execute("SET enable_external_access = false")
            connection.execute("SET autoinstall_known_extensions = false")
            connection.execute("SET autoload_known_extensions = false")
            connection.execute("SET allow_community_extensions = false")
            connection.execute("SET allow_persistent_secrets = false")
            connection.execute(f"SET memory_limit = '{memory_limit}'")
            connection.execute(f"SET threads = {threads}")
            connection.execute("SET lock_configuration = true")
        except Exception:
            connection.close()
            raise

        self.connection = connection
        self.path = full
        return {"ok": True}

    def _conn(self):
        if self.connection is None:
            raise RuntimeError("No DuckDB database is open.")
        return self.connection

    def replace_table(self, table: dict) -> dict:
        if not isinstance(table, dict):
            raise ValueError("table must be an object.")
        name = _printable_identifier(table.get("name"), "Table name")
        supplied_columns = table.get("columns")
        supplied_rows = table.get("rows")
        if not isinstance(supplied_columns, list) or not 1 <= len(supplied_columns) <= MAX_PUBLISHED_COLUMNS:
            raise ValueError(f"Published tables must contain 1-{MAX_PUBLISHED_COLUMNS} columns.")
        if not isinstance(supplied_rows, list) or len(supplied_rows) > MAX_PUBLISHED_ROWS:
            raise ValueError(f"Published tables may contain at most {MAX_PUBLISHED_ROWS} rows.")
        if len(supplied_columns) * len(supplied_rows) > MAX_PUBLISHED_CELLS:
            raise ValueError(f"Published tables may contain at most {MAX_PUBLISHED_CELLS} cells.")

        columns: list[str] = []
        unique: set[str] = set()
        for supplied in supplied_columns:
            column = _printable_identifier(supplied, "Column name")
            key = column.casefold()
            if key in unique:
                raise ValueError(f"Published column name '{column}' is duplicated.")
            unique.add(key)
            columns.append(column)

        rows: list[list[str]] = []
        for supplied in supplied_rows:
            if not isinstance(supplied, list) or len(supplied) != len(columns):
                raise ValueError("Every published row must contain exactly one value per column.")
            rows.append(["" if value is None else str(value) for value in supplied])

        connection = self._conn()
        quoted_name = _quote_identifier(name)
        definitions = ", ".join(f"{_quote_identifier(column)} VARCHAR" for column in columns)
        placeholders = ", ".join("?" for _ in columns)
        connection.execute("BEGIN TRANSACTION")
        try:
            connection.execute(f"CREATE OR REPLACE TABLE {quoted_name} ({definitions})")
            if rows:
                connection.executemany(f"INSERT INTO {quoted_name} VALUES ({placeholders})", rows)
            connection.execute("COMMIT")
        except Exception:
            try:
                connection.execute("ROLLBACK")
            except Exception:
                pass
            raise
        return {"ok": True}

    def _validate_read_only(self, sql: str) -> None:
        text = sql.strip()
        if not text:
            raise ValueError("SQL is empty.")
        try:
            statements = self._conn().extract_statements(text)
        except Exception as exc:
            raise ValueError(f"SQL could not be parsed: {exc}") from exc
        if len(statements) != 1:
            raise PermissionError("Multiple SQL statements are not allowed in the first slice.")
        statement_type = str(statements[0].type).rsplit(".", 1)[-1].upper()
        if statement_type not in READ_ONLY_STATEMENT_TYPES:
            raise PermissionError("Only parsed SELECT and EXPLAIN statements are allowed in the first slice.")
        if MUTATING_OR_DYNAMIC_FUNCTIONS.search(text):
            raise PermissionError("SQL uses a mutating or dynamically executing function disabled by Haven Data.")

    def query(self, sql: str, max_rows: int) -> dict:
        self._validate_read_only(sql)
        if max_rows < 1 or max_rows > 1000:
            raise ValueError("maxRows must be between 1 and 1000.")
        cursor = self._conn().execute(sql)
        columns = [item[0] for item in cursor.description or []]
        fetched = cursor.fetchmany(max_rows + 1)
        truncated = len(fetched) > max_rows
        rows = fetched[:max_rows]
        return {
            "columns": columns,
            "rows": [["" if value is None else str(value) for value in row] for row in rows],
            "truncated": truncated,
        }

    def close(self) -> dict:
        if self.connection is not None:
            self.connection.close()
        self.connection = None
        self.path = None
        return {"ok": True}


def serve() -> int:
    runtime = DuckDbRuntime()
    try:
        for line in sys.stdin:
            if not line.strip():
                continue
            request_id = 0
            try:
                request = json.loads(line)
                request_id = int(request.get("id", 0))
                method = request.get("method")
                params = request.get("params") or {}
                if method == "shutdown":
                    print(json.dumps({"id": request_id, "error": None, "result": {"ok": True}}), flush=True)
                    return 0
                if method == "runtimeInfo":
                    result = {"engine": "duckdb", "version": DUCKDB_RUNTIME_VERSION}
                elif method == "open":
                    result = runtime.open(params["databasePath"])
                elif method == "replaceTable":
                    result = runtime.replace_table(params["table"])
                elif method == "query":
                    result = runtime.query(params["sql"], int(params.get("maxRows", 200)))
                elif method == "close":
                    result = runtime.close()
                else:
                    raise ValueError(f"Unsupported method '{method}'.")
                print(json.dumps({"id": request_id, "error": None, "result": result}), flush=True)
            except Exception as exc:
                print(json.dumps({"id": request_id, "error": f"{type(exc).__name__}: {exc}", "result": None}), flush=True)
    finally:
        runtime.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(serve())
