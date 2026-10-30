# Data external donors

Selected from live default branches 2026-09-24 UTC. Both sources are unmodified; no patches. Full Calc runtime still requires local soffice/Python UNO. Existing DuckDB worker expects runtime 1.5.5; importing latest default-branch source does not silently upgrade it.

LibreOffice's canonical repository is https://git.libreoffice.org/core ; the listed GitHub mirror matched it at the selected SHA.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence files retained |
| --- | --- | --- | --- | --- |
| LibreOffice Calc/UNO | https://github.com/LibreOffice/core @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | https://github.com/CroakyJake12/libreoffice @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | `Source/LibreOffice` | `COPYING`, `COPYING.MPL`, `COPYING.LGPL` (MPL-2.0 / LGPL-3.0-or-later) |
| DuckDB | https://github.com/duckdb/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` (`v2.0-cyanoptera`) | https://github.com/CroakyJake12/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` | `Source/DuckDB` | `LICENSE` (MIT) |

Compatibility with the newest sources is **not proven**. See `eng/donor-sources.json` for separate source, runtime and integration records.

## DuckDB pinned as a submodule 30 October 2026 - re-pinned to the version the runtime demands

- `Source/DuckDB` gitlink pinned at **tag `v1.5.5` = `d8cdaa33fda8`** (`git describe` -> `v1.5.5`).
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
