# Data external donors

Selected from live default branches 2026-09-24 UTC. Both sources are unmodified; no patches. Full Calc runtime still requires local soffice/Python UNO. Existing DuckDB worker expects runtime 1.5.5; importing latest default-branch source does not silently upgrade it.

LibreOffice's canonical repository is https://git.libreoffice.org/core ; the listed GitHub mirror matched it at the selected SHA.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence files retained |
| --- | --- | --- | --- | --- |
| LibreOffice Calc/UNO | https://github.com/LibreOffice/core @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | https://github.com/CroakyJake12/libreoffice @ `d3fca73f81dfb19a72a0da41598b7390eaf5845b` | `Source/LibreOffice` | `COPYING`, `COPYING.MPL`, `COPYING.LGPL` (MPL-2.0 / LGPL-3.0-or-later) |
| DuckDB | https://github.com/duckdb/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` (`v2.0-cyanoptera`) | https://github.com/CroakyJake12/duckdb @ `12633c226f827d4328a83e820d3d6a3f16195207` | `Source/DuckDB` | `LICENSE` (MIT) |

Compatibility with the newest sources is **not proven**. See `eng/donor-sources.json` for separate source, runtime and integration records.
