# Data external donors

Selected from live default branches 2026-09-24 UTC. Both sources are unmodified; no patches. Full Calc runtime still requires local soffice/Python UNO. Existing DuckDB worker expects runtime 1.5.5; importing latest default-branch source does not silently upgrade it.

LibreOffice's canonical repository is https://git.libreoffice.org/core ; the listed GitHub mirror matched it at the selected SHA.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence files retained |
| --- | --- | --- | --- | --- |
| LibreOffice Calc/UNO | https://github.com/LibreOffice/core @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | https://github.com/CroakyJake12/libreoffice @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | `Source/LibreOffice` | `COPYING`, `COPYING.MPL`, `COPYING.LGPL` (MPL-2.0 / LGPL-3.0-or-later) |
| DuckDB | https://github.com/duckdb/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` (`v2.0-cyanoptera`) | https://github.com/CroakyJake12/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` | `Source/DuckDB` | `LICENSE` (MIT) |

Compatibility with the newest sources is **not proven**. See `eng/donor-sources.json` for separate source, runtime and integration records.

## DuckDB pinned as a submodule 30 October 2026 - re-pinned to the version the runtime demands

- `Source/DuckDB` gitlink pinned at **tag `v1.5.5` = `d8cdaa33fda8df955cc76ef58a280f68f4cd43fa`**
  (`git describe` -> `v1.5.5`).
- The donor registry previously recorded `12633c226f827d4328a83e820d3d6a3f16195207`, which measures
  **14,480 commits past the v1.5.5 tag** (mainline, not a release). Re-pinned because
  `Data/workers/duckdb_worker.py:17` hard-requires exactly `REQUIRED_DUCKDB_VERSION = "1.5.5"` and
  refuses any other version at runtime, so the recorded pin could never satisfy the gate.
- Remaining gap, stated plainly: the **vendored C++ source is not what Data actually executes**.
  `DuckDbDatabaseEngine` spawns a Python worker that does `import duckdb`, and that Python dependency
  is pinned nowhere in the repository (no requirements file, no version+digest record). So the runtime
  dependency currently comes from whatever DuckDB the environment provides - the same
  "depends on what the user has" pattern flagged for the browser engine. Closing it needs either a
  versioned, digest-verified Python dependency or a build of the vendored source into a bundled library.

## LibreOffice pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/LibreOffice/core and https://github.com/LibreOffice/core
- Controlled fork: https://github.com/CroakyJake12/libreoffice
- Extracted from `refs/heads/master` at commit `7528c0d406012e10c6ce34c3f24bf277302ec695` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Data/Source/LibreOffice` - 488 files, content digest `eafb3acb76abffac7de4abd56cf501c77fbea51f5bf72aa0afa1bbfa8f9a6929` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: sc/uiconfig (Calc), svx/uiconfig + chart2/uiconfig (shared UI), icon-themes, licence notices
- Licence: MPL-2.0-or-later / LGPL-3.0-or-later (LibreOffice is triple-licensed; notices read from the pinned tree); notices read from the tree: `COPYING`, `COPYING.LGPL`, `COPYING.MPL`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Stage A mirror of the Calc UI. DuckDB stays the analytical engine the specification names; LibreOffice Calc is the spreadsheet-compatibility donor and the UI reference for it.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
