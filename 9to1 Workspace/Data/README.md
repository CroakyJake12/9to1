# Data workbooks, database queries and engine workers

Data belongs to Develop. Preserve workbook, sheet, cell, table and query identities through edits and exchanges between the spreadsheet and database surfaces. The app couples its CUI controller to the owning services and separates Calc/UNO operations from DuckDB worker requests.

For a workbook change, inspect the original revisions and check edit, calculation, save and reopen. For a cross-engine change, test the actual transfer with explicit type and null handling. An incompatible sort, filter or list validation should preserve the workbook and return a meaningful diagnostic.

The compatibility rules below describe the engine boundaries and supported data shapes. Where a cited result is from an earlier platform or disposable test, record a new observation before asserting current support.

## Sort compatibility and safety boundary

- 9to1 exposes only one relative key column, ascending/descending order and a header flag; callers cannot supply arbitrary UNO sort descriptors;
- the target must fit wholly inside the first-slice 10 × 8 viewport when invoked through `DataGridSession`;
- sorting is a mutation and is refused for read-only workbooks;
- the Calc worker supports both LibreOffice sort descriptor forms observed in the supported distro lanes: legacy `SortColumns`/`util.SortField` and newer `IsSortColumns`/`table.TableSortField`;
- PyUNO `SortFields` is passed as an explicitly typed `uno.Any` sequence; a plain Python tuple was accepted by LibreOffice but produced a silent no-op during development;
- live tests prove ascending/descending numeric key order, header preservation, invalid key/header-only rejection and ODS save/reopen persistence on LibreOffice 24.2.7 and 26.2.5.2.

Multi-key sorting and custom collators/user lists remain separate deferred capabilities.

## Filter compatibility and safety boundary

- 9to1 exposes one relative key column, one literal string and a header flag; callers cannot provide arbitrary UNO filter descriptors, formulas, regex patterns or compound predicates;
- first-slice matching is case-insensitive literal text equality only, with regular-expression handling explicitly disabled;
- the target must fit wholly inside the 10 × 8 `DataGridSession` viewport and filter values are limited to 1–256 non-control characters;
- filtering and clearing are mutations and are refused for read-only workbooks;
- `DataRangeSnapshot.RowVisibility` reports Calc's row visibility without compacting or rewriting the underlying cell values, so HUI can render hidden rows deliberately;
- worker tests prove filtering does not mutate cell contents, filters survive ODS save/reopen, and an empty descriptor restores row visibility;
- the full live C# path proves `DataGridSession → CalcSpreadsheetEngine → JSON worker → UNO` filter and clear operations on LibreOffice 24.2.7 and 26.2.5.2.

Numeric comparison operators, NOT-EQUAL/range conditions, multiple criteria, regex filtering and persisted HUI filter-state metadata remain deferred.

## Literal-list validation safety boundary

- only literal list items are created by 9to1 Data in this slice;
- no validation formula, named range, external link or arbitrary UNO validation expression is accepted from HUI/GenUI;
- values are bounded to 1–50 unique strings of at most 64 characters;
- formula-sensitive/control characters used by the literal-list encoding are rejected;
- Calc's stored validation is re-read after apply/clear and must match the requested 9to1 rule;
- imported validation that is not representable as this literal subset is reported as not being a 9to1 literal-list rule rather than being rewritten or deleted;
- live tests prove structural row/column shifts, ODS save/reopen persistence, read-only guards and clearing.

Numeric/date/text-length/custom validation remain separate deferred capabilities.

## Calc → DuckDB bridge

- explicit displayed-value range snapshots rather than shared workbook authority;
- generated A/B/C… columns or sanitised first-row headers;
- source range validation;
- privileged structured `ReplaceTableAsync` rather than exposing DDL strings;
- worker-side table/shape validation;
- quoted SQL identifiers, parameterised values and transactional replacement;
- raw user/model SQL remains read-only.

The worker's `runtimeInfo` request reports the exact accepted DuckDB runtime
version. It fails before serving requests when the installed binding is not
DuckDB 1.5.5, rather than claiming compatibility with an unproven version.

Current publication is intentionally display-string based. Typed date/error/value preservation is a later compatibility milestone.

## DuckDB → Calc bridge

- only a successful query from the currently open query session can be materialised;
- truncated query previews are refused rather than silently producing incomplete sheets;
- first slice supports at most 999 data rows plus one header and 256 columns;
- result data always creates a **new** sheet; existing sheets are not overwritten;
- query output is written through Calc `setString`, so formula-looking values remain literal text;
- portable sheet-name validation and case-insensitive duplicate detection are enforced;
- ODS/XLSX save/reopen tests prove literal result values stay literal.

## Worker protocol safety

`JsonLineWorkerClient` serialises calls per worker. If cancellation occurs after a request may have been written, or stdout contains malformed JSON, EOF, a missing result or a mismatched response ID, the client faults and terminates the worker. It does not continue on a potentially shifted request/response stream.

[CUI integration](Cui) · [Workers](workers) · [Owning tests](Tests) · [Standalone entry](Native/README.md) · [Data requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
