# Data Parity Record

Audit date: 2026-09-20

## Sources inspected

| Role | Source |
| --- | --- |
| Superseded donor | `reference/cakeai/data-app-surface/App/DataAppSurface.cs` and `Tests/DataAppSurfaceTests.cs` |
| Current engine | `9to1 Workspace/Data/App/` (`CalcSpreadsheetEngine`, `DuckDbDatabaseEngine`, sessions, contracts, bridge, and worker client) |
| Current HUI adapter | `9to1 Workspace/Data/Hui/DataHuiScene.cs` |
| Current tests/workers | `9to1 Workspace/Data/Tests/` and `9to1 Workspace/Data/workers/` |

## Exact status

| Capability | Superseded donor | Current source | Status | Evidence / blocker |
| --- | --- | --- | --- | --- |
| Data app identity and routes | JSON-workbook `DataAppSurface` with `/data` routes | Calc/DuckDB sessions expose no donor route facade | Intentionally not carried forward | The newer CakeOS engine is active; the donor `DataAppSurface` remains reference-only and was not restored. |
| Spreadsheet editing and formula recalculation | In-process donor formula model | Calc worker behind `IDataSpreadsheetEngine` and `DataGridSession` | Implemented | Contract smoke plus live-runtime programs exist; local engine contract validation is recorded below. |
| Workbook structure, named ranges, validation, sort, and filter | Not provided by the donor surface | Typed Calc operations with bounded first-slice guards | Implemented | Dedicated worker/runtime programs are present. LibreOffice runtime validation is unavailable on this Windows host. |
| Read-only local SQL and snapshot publication | Donor query service over its workbook model | DuckDB worker plus explicit Calc-to-DuckDB snapshot bridge | Implemented | Raw SQL remains read-only; structured replacement is the only publication path. |
| Query result materialisation | Not provided by donor | New Calc sheet with literal values only | Implemented and hardened | Materialisation now requires the exact successful execution instance retained by the open session; a copied equal-value record is rejected by the contract smoke. |
| HUI scene and registry root | No equivalent donor HUI integration | `DataHuiScene` and `DataHuiProduct` | Implemented; registry runtime validated | The HUI project builds against the local vendor `Haven.UI`, and the registry/mountable-root runtime contract passes. The full Calc-backed HUI interaction gate still requires Linux Calc dependencies and the exact staged platform dependency. |
| Graphical host / approved VM | No donor acceptance evidence | Explicitly outside the app boundary | Not established | The Data README records the missing platform host injection seam and VM validation. |
| CUI parity | No `.cui` artifact | No `.cui` artifact | Not assessed | No CUI runtime validation exists; CUI parity is not claimed. |

## Local validation

- The focused smoke test includes the copied-execution rejection contract for query materialisation.
- Python dependency-policy tests pass without Calc. Full Calc-backed HUI interaction suites require Linux LibreOffice/UNO and the exact staged HUI dependency.
