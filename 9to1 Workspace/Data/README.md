# Haven Data — Calc + DuckDB first slice

Status: **implemented and runtime-proven in disposable Ubuntu CI, including a thin HUI contract integration against the accepted CakeOS HUI reference; not yet graphically hosted inside the accepted Linux host and not yet accepted inside the approved CakeOS VM**.

This directory is the CakeOS-native Haven Data foundation. It does not copy the legacy donor implementation or LibreOffice/DuckDB upstream source. HUI-facing code depends only on Haven-owned interfaces; LibreOffice Calc and DuckDB live behind separate worker processes.

## Boundary

```text
accepted Haven.UI contract
        |
DataHuiScene / DataHuiController
        |
DataGridSession / DataQuerySession / DataAppService
   |                         |
IDataSpreadsheetEngine       IDataDatabaseEngine
   |                         |
CalcSpreadsheetEngine        DuckDbDatabaseEngine
   |                         |
JSON-lines stdio             JSON-lines stdio
   |                         |
calc_worker.py                duckdb_worker.py
   |                         |
UNO local pipe                DuckDB connection
   |                         |
headless LibreOffice Calc     local .duckdb file
          |                   ^
          +-- DataWorkbookDatabaseBridge --+
               typed snapshot publication
```

No UNO type, DuckDB API, Avalonia type, Linux-host renderer primitive or platform backend crosses the Data HUI boundary. Calc remains authoritative for workbook semantics. DuckDB receives explicit typed snapshots. Query results return to Calc only through a typed, new-sheet, literal-value materialisation operation.

## Accepted HUI reference

The Data branch consumes, but does not merge or copy, the accepted CakeOS HUI platform revision:

- CakeOS HUI platform: `2a578502c31973fbefa3b3f82efa22d7ad11db53`
- pinned `Haven.UI` donor: `CroakyJake12/CakeAI@7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb:src/Haven.UI`
- Data pin: `apps/Data/Hui/hui-platform-reference.txt`

CI stages that exact platform commit into a disposable worktree, runs its own donor-staging contract, checks the exact donor revision, runs the accepted HUI preview smoke, then builds and exercises `apps/Data/Hui` against the staged `Haven.UI.csproj`. The Data branch does not wholesale merge the accepted platform branch.

## Evidence ledger

| Claim | Evidence |
| --- | --- |
| Source implemented | Yes — CakeOS branch `data-calc-duckdb-first-slice` |
| .NET 10 build | Yes — Ubuntu 24.04 and Ubuntu 26.04.1 GitHub Actions with warnings treated as errors |
| HUI-facing app smoke | Yes — 10 × 8 grid, edit/recalculate/refresh, sheet selection, row/column edits, range-backed named ranges, literal-list validation, single-key sorting, literal equality filtering, database publication, bounded query history and query-result materialisation |
| Worker Python syntax | Yes — workers and integration/corpus harnesses compile with `py_compile` |
| Calc/UNO runtime | Yes — live headless Calc over a local UNO pipe opens, edits, recalculates, structurally edits, sorts, filters, manages range-backed names and literal-list validation, saves and reopens disposable ODS/XLSX workbooks |
| Formula compatibility baseline | Yes — arithmetic precedence, SUM, absolute references, exponentiation, AVERAGE, COUNT, MIN and MAX |
| Row/column structural edits | Yes — live Calc insert/delete shifts formulas/references correctly, rejects unsafe/read-only edits and survives save/reopen |
| Range-backed named ranges | Yes — create/list/delete, case-insensitive duplicate/delete handling, row/column shift tracking and ODS persistence are live-proven; arbitrary named formulas are not exposed |
| Literal-list data validation | Yes — apply/read/clear, row/column shift tracking, ODS persistence, read-only guards and the full C# `DataGridSession → Calc` path are green on both distro lanes |
| Single-key sorting | Yes — bounded row sorting by one relative key column, ascending/descending numeric order, optional header preservation, invalid-key/header-only/read-only guards, ODS persistence, and the full C# `DataGridSession → Calc` path are green on both distro lanes; source run `34156898422` |
| Literal text-equality filtering | Yes — one bounded case-insensitive text `EQUAL` condition over a relative key column, header preservation, row-visibility reporting, invalid-key/header-only/empty-value/read-only guards, explicit clearing and ODS persistence are live-proven. Worker run `34158504780`; full `DataGridSession → Calc` run `34158775542` is green on both distro lanes |
| DuckDB runtime | Yes — DuckDB 1.5.5 opens disposable databases, executes bounded read-only SQL, rejects DDL/multiple statements/external access, and atomically replaces typed snapshot tables |
| Calc → DuckDB bridge | Yes — displayed-value range publication with generated/sanitised headers and structured table replacement |
| DuckDB → Calc bridge | Yes — non-truncated current-session query results materialise into a new Calc sheet as literal text; formula-looking values such as `=1+1` are not executed |
| Full .NET → workers runtime path | Yes — real C# adapters perform workbook edits, formula recalc, structural edits, named-range, validation, sort and filter operations, Calc→DuckDB publication, SQL aggregation, DuckDB→Calc materialisation, save and reopen |
| Accepted HUI contract provenance | Yes — CI stages exactly CakeOS HUI `2a578502c31973fbefa3b3f82efa22d7ad11db53` and verifies its pinned `Haven.UI` donor `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb` |
| Thin Data HUI adapter | Yes — `apps/Data/Hui` builds against the staged accepted `Haven.UI` with zero warnings/errors and is guarded against Avalonia, UNO and package references |
| HUI layout/render/input contract | Yes — headless contract proof uses accepted `HavenLayoutEngine`, `HavenSceneRenderer` and `HavenInputRouter`; it produces layout/render commands and exercises pointer and keyboard routing |
| HUI → Data → Calc runtime | Yes — on Ubuntu 26.04.1, pointer selection and keyboard action dispatch drive real Calc edit → recalculation → refresh, proven single-key sort, save-as and reopen; run `34159933201` prints `Haven Data HUI contract edit/recalc/sort/save-reopen runtime checks passed.` |
| Ubuntu 26.04.1 distro proof | Yes — GitHub Actions `ubuntu-26.04`, LibreOffice `26.2.5.2-0ubuntu0.26.04.1`, Python 3.14, DuckDB 1.5.5 and .NET 10 |
| Worker protocol cancellation safety | Implemented — an in-flight cancellation or malformed/misaligned response faults and terminates the worker instead of reusing a desynchronised stream |
| Accepted Data graphical Linux-host window | **No** — the accepted Linux host currently constructs its own private preview scene and exposes no external scene/root-provider injection seam for Data; no renderer copy is introduced here |
| Visual screenshot of the Data scene | **No** — Data HUI contract/runtime is proven headlessly, not through the accepted graphical host screenshot gate |
| Approved desktop checkout | Located — Sandbox project `cakeos`; its tracked/untracked Boards work is intentionally untouched by this Data branch |
| Approved CakeOS VM | **No** — the actual CakeOS VM/session has not run this branch |
| CakeOS package/image | **No** — no package list, image, ISO or VM state was changed by this branch |
| Approved-VM visual/accessibility acceptance | **No** — requires the graphical host seam first, then actual CakeOS-session verification |

The HUI contract result is intentionally labelled separately from graphical-host and approved-VM proof. A headless layout/render/input pass does not establish that the Data scene has been drawn in the accepted Linux window or visually inspected in CakeOS.

## Implemented spreadsheet behaviour

- isolated headless `soffice` process and disposable LibreOffice user profile;
- local UNO pipe rather than a TCP listener;
- macro execution and document-link updates disabled at load;
- non-Calc document rejection;
- real worksheet enumeration;
- bounded range reads with per-row visibility metadata and viewport-bounded typed cell edits;
- text, number and formula editing with Calc-owned recalculation;
- row/column insertion and deletion behind typed operations;
- bounded single-key row sorting, ascending or descending, with optional header preservation;
- bounded case-insensitive literal text-equality row filtering with explicit clear;
- global named ranges backed by one concrete cell range only;
- range-backed named ranges track Calc row/column shifts and persist in ODS;
- formula-style/non-range named expressions are not surfaced through the Haven API;
- literal-list data validation behind a typed range operation;
- first-slice list rules contain 1–50 unique literal strings, each 1–64 characters, with control characters, semicolons and double quotes excluded;
- imported formula/range-driven validation is not reinterpreted as a Haven literal-list rule;
- list validation tracks Calc row/column shifts, persists in ODS and obeys read-only mutation guards;
- first-slice save-as to `.ods` or `.xlsx`, with in-place overwrite disabled;
- saved-output existence/size verification;
- worker stderr draining and bounded shutdown/kill fallback.

## HUI-facing spreadsheet session

`DataGridSession` exposes only Haven-owned records and operations:

- fixed 10 × 8 first-slice viewport;
- worksheet discovery and selection;
- edit → recalculate → refresh;
- typed row/column insertion and deletion with refresh;
- bounded single-key sort of a visible range, followed by refresh;
- bounded literal text-equality filter and explicit filter clearing, with row visibility returned in the grid snapshot;
- named-range create/list/delete; creation must fit wholly inside the current viewport in this slice;
- literal-list validation apply/read/clear; the target range must fit wholly inside the viewport;
- save-as and deterministic close/dispose semantics;
- atomic open/close failure handling.

`DataHuiScene` and `DataHuiController` add only a thin HUI presentation/intent layer:

- `Page`, `Container`, `Button` and `Text` from the accepted `Haven.UI` contract;
- one 10 × 8 HUI grid whose cells mirror the typed `DataGridSession` snapshot;
- selected-cell state and accessibility names;
- hidden-row state represented from `DataRangeSnapshot.RowVisibility` without changing workbook values;
- typed actions for edit, ascending sort and save/reopen;
- no Avalonia, UNO, DuckDB or Linux-host backend references.

The accepted platform renderer remains platform-owned. Data does not copy or fork it.

## HUI contract runtime proof

The `hui-contract` CI job on Ubuntu 26.04.1 performs one reproducible integration chain:

1. stage exact accepted HUI platform revision `2a578502c31973fbefa3b3f82efa22d7ad11db53`;
2. stage and verify its exact `Haven.UI` donor `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb`;
3. run the accepted HUI preview smoke;
4. enforce that `apps/Data/Hui` has no Avalonia/UNO/package dependency;
5. build the Data HUI adapter against that exact `Haven.UI` project;
6. create a disposable ODS fixture with LibreOffice;
7. layout/render the Data HUI scene with `HavenLayoutEngine` and `HavenSceneRenderer`;
8. route pointer input through `HavenInputRouter` to select cell B2;
9. route keyboard Enter to a typed edit action, update B2, and verify a dependent Calc formula recalculates;
10. route a typed ascending-sort action and verify workbook row order;
11. route save/reopen and verify the edited and sorted state survives ODS persistence.

Run `34159933201` completed the HUI job successfully and the unchanged Ubuntu 24.04/26.04 Data engine matrix remained green on the same source head.

## Sort compatibility and safety boundary

- Haven exposes only one relative key column, ascending/descending order and a header flag; callers cannot supply arbitrary UNO sort descriptors;
- the target must fit wholly inside the first-slice 10 × 8 viewport when invoked through `DataGridSession`;
- sorting is a mutation and is refused for read-only workbooks;
- the Calc worker supports both LibreOffice sort descriptor forms observed in the supported distro lanes: legacy `SortColumns`/`util.SortField` and newer `IsSortColumns`/`table.TableSortField`;
- PyUNO `SortFields` is passed as an explicitly typed `uno.Any` sequence; a plain Python tuple was accepted by LibreOffice but produced a silent no-op during development;
- live tests prove ascending/descending numeric key order, header preservation, invalid key/header-only rejection and ODS save/reopen persistence on LibreOffice 24.2.7 and 26.2.5.2.

Multi-key sorting and custom collators/user lists remain separate deferred capabilities.

## Filter compatibility and safety boundary

- Haven exposes one relative key column, one literal string and a header flag; callers cannot provide arbitrary UNO filter descriptors, formulas, regex patterns or compound predicates;
- first-slice matching is case-insensitive literal text equality only, with regular-expression handling explicitly disabled;
- the target must fit wholly inside the 10 × 8 `DataGridSession` viewport and filter values are limited to 1–256 non-control characters;
- filtering and clearing are mutations and are refused for read-only workbooks;
- `DataRangeSnapshot.RowVisibility` reports Calc's row visibility without compacting or rewriting the underlying cell values, so HUI can render hidden rows deliberately;
- worker tests prove filtering does not mutate cell contents, filters survive ODS save/reopen, and an empty descriptor restores row visibility;
- the full live C# path proves `DataGridSession → CalcSpreadsheetEngine → JSON worker → UNO` filter and clear operations on LibreOffice 24.2.7 and 26.2.5.2.

Numeric comparison operators, NOT-EQUAL/range conditions, multiple criteria, regex filtering and persisted HUI filter-state metadata remain deferred.

## Literal-list validation safety boundary

- only literal list items are created by Haven Data in this slice;
- no validation formula, named range, external link or arbitrary UNO validation expression is accepted from HUI/GenUI;
- values are bounded to 1–50 unique strings of at most 64 characters;
- formula-sensitive/control characters used by the literal-list encoding are rejected;
- Calc's stored validation is re-read after apply/clear and must match the requested Haven rule;
- imported validation that is not representable as this literal subset is reported as not being a Haven literal-list rule rather than being rewritten or deleted;
- live tests prove structural row/column shifts, ODS save/reopen persistence, read-only guards and clearing.

Numeric/date/text-length/custom validation remain separate deferred capabilities.

## Database/query behaviour

- local DuckDB database;
- DuckDB 1.5.5 pinned in CI/runtime proof and enforced by the worker before it opens a database;
- external access, extension autoload/install, community extensions and persistent secrets disabled;
- bounded memory/thread defaults followed by locked DuckDB configuration;
- raw query text restricted to one `SELECT` or `EXPLAIN` statement;
- worker-side mutation/capability keyword rejection;
- query previews capped at 1–1000 rows;
- `DataQuerySession` tracks published tables and the 20 most recent successful queries;
- query-session close is atomic;
- raw database APIs are not exposed directly to generated UI.

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

## Runtime dependencies — not installed into CakeOS by this branch

The Calc worker expects:

- LibreOffice Calc headless runtime (`soffice`);
- Python with LibreOffice `pyuno`/`uno` bindings;
- Calc filters required for ODS/XLSX.

The DuckDB worker expects Python and the approved/pinned DuckDB runtime. The process boundary means a later move from the Python binding to the DuckDB C API does not change HUI-facing contracts.

The Ubuntu 26.04.1 CI lane proved the narrow distro packages `libreoffice-calc-nogui`, `libreoffice-core-nogui`, `libreoffice-common` and `python3-uno` are sufficient for the current integration suite. This branch does **not** install those packages into the CakeOS image or approved VM.

## Reproducible checks

Base Data checks:

```bash
dotnet build apps/Data/App/HavenOS.Data.App.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release
python3 -m py_compile apps/Data/workers/calc_worker.py
python3 -m py_compile apps/Data/workers/duckdb_worker.py
python3 -m py_compile apps/Data/Tests/test_workers.py
python3 -m py_compile apps/Data/Tests/test_duckdb_publish.py
python3 -m py_compile apps/Data/Tests/test_calc_formula_corpus.py
python3 -m py_compile apps/Data/Tests/test_calc_structure.py
python3 -m py_compile apps/Data/Tests/test_calc_named_ranges.py
python3 -m py_compile apps/Data/Tests/test_calc_validation.py
python3 -m py_compile apps/Data/Tests/test_calc_sort.py
python3 -m py_compile apps/Data/Tests/test_calc_filter.py
```

After disposable LibreOffice/UNO and DuckDB runtimes are available:

```bash
python3 apps/Data/Tests/test_workers.py
python3 apps/Data/Tests/test_duckdb_publish.py
python3 apps/Data/Tests/test_calc_formula_corpus.py
python3 apps/Data/Tests/test_calc_structure.py
python3 apps/Data/Tests/test_calc_named_ranges.py
python3 apps/Data/Tests/test_calc_validation.py
python3 apps/Data/Tests/test_calc_sort.py
python3 apps/Data/Tests/test_calc_filter.py
dotnet run --project apps/Data/Tests/HavenOS.Data.Runtime.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Validation.Runtime.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Sort.Runtime.csproj -c Release
dotnet run --project apps/Data/Tests/HavenOS.Data.Filter.Runtime.csproj -c Release
```

After staging the accepted `Haven.UI` project at an exact path:

```bash
dotnet build apps/Data/Hui/HavenOS.Data.Hui.csproj -c Release \
  -p:HavenUiProject=/absolute/path/to/Haven.UI.csproj

HAVEN_DATA_PYTHON=/usr/bin/python3 \
dotnet run --project apps/Data/Tests/HavenOS.Data.Hui.Runtime.csproj -c Release \
  -p:HavenUiProject=/absolute/path/to/Haven.UI.csproj
```

`.github/workflows/data-first-slice.yml` reproduces both the dual-distro engine matrix and the accepted-HUI contract integration gate.

## Graphical HUI host gate still required

The accepted `HUI/LinuxHost` graphical implementation is platform-owned and already has its own Xvfb/screenshot proof, but its current preview surface constructs a private hard-coded scene. Data cannot inject `DataHuiScene.Root` into that host through a published seam.

The next platform-owned slice should therefore be deliberately small:

1. add a generic scene/root-provider injection seam to the accepted Linux host without making it Data-specific;
2. keep measurement, rendering, pointer/keyboard translation and screenshot capture owned by `HUI/LinuxHost`;
3. host `DataHuiScene.Root` through that seam under the existing Xvfb graphical gate;
4. exercise selection, edit/recalc/refresh, sort and save/reopen in the actual graphical host;
5. retain the screenshot and interaction evidence separately from approved-VM acceptance.

Data should **not** copy the Avalonia host, `HuiPreviewSurface`, renderer bridge or screenshot plumbing just to bypass this seam.

## Approved-VM gate still required

1. Preserve the existing dirty Boards checkout; do not clean, stash, reset or overwrite it for Data testing.
2. Only after the graphical platform seam is accepted should this Data branch be consumed by an approved platform integration path.
3. Confirm the approved VM identity, Ubuntu 26.04.1 environment and current package state before mutation.
4. Build first without package/image mutation.
5. If dependencies are absent, record that before any install and add them only through the approved platform/package path.
6. Run the same disposable Python/.NET/HUI integration gates in the actual CakeOS session.
7. Verify worker cleanup, local-pipe-only UNO communication, filesystem/network confinement, resource limits and failure/restart behaviour.
8. Perform visual and accessibility acceptance in the actual CakeOS session; do not infer it from headless CI.

## Deliberately deferred

- direct linking to LibreOffice `sc/` internals;
- embedding LibreOffice/VCL UI;
- LibreOfficeKit rendering;
- charts and drawings;
- general formatting;
- numeric/date/text-length/custom or formula/range-backed data validation;
- multi-key/custom-collation sorting;
- numeric/comparison, compound, regex and other advanced filter operations;
- arbitrary named formulas/expressions;
- raw SQL mutation/DDL;
- typed preservation beyond displayed strings in Calc↔DuckDB transfer;
- package/image changes;
- unrestricted generated-UI write operations;
- copying or forking the platform graphical renderer into the Data app.

## Safety notes

The worker boundary is defence in depth, not the final OS sandbox. Before shipping, Calc and DuckDB still need target-runtime confinement (AppArmor/systemd/bubblewrap or the platform-selected equivalent), explicit filesystem brokers, resource limits, crash supervision, a larger golden-file/formula corpus and actual visual/accessibility acceptance through the platform HUI host and approved CakeOS runtime.
