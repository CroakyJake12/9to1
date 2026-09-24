# Third-party software and donor provenance

This index does not replace the licence text distributed with each component.
Preserve notices, source offers and licence files when packaging or moving code.

| Component | Repository location | Licence/provenance status |
| --- | --- | --- |
| Avalonia-derived CUI platform | `framework/CUI/vendor/` | Import must record upstream URL and exact revision and retain the upstream MIT licence. Not present at the baseline audit. |
| llama.cpp-derived Dulche runtime | `9to1 Workspace/Home/Source/Dulche/llamacpp/` (9-1 broker); `9to1 Workspace/Home/Source/Dulche/Source/llama.cpp/` (new upstream source) | Older broker runtime revision is pinned by its `upstream.lock.json`; latest source and MIT notice are recorded separately in `eng/donor-sources.json`. Compatibility is unverified. |
| Files donor | `9to1 Workspace/Files/Source/Files/` | Donor includes `LICENSE-MIT` and `LICENSE-MPL`; both must remain with donor-derived distributions as applicable. |
| Rnote | `9to1 Workspace/Boards/Source/Rnote/` and `9to1 Workspace/Canvas/Source/Rnote/` | Latest upstream `1a728d6a`; GPL-3.0-or-later in each source tree. Historical Canvas Cargo proof still pins v0.14.2 separately. |
| AppFlowy Board | `9to1 Workspace/Boards/Source/AppFlowyBoard/` | Latest upstream `804d7898` (matches existing proof); AGPL-3.0 OR MPL-2.0 in `LICENSE`, MPL-2.0 selected for Boards. |
| Firefox/Gecko | `9to1 Workspace/Browse/Source/FirefoxGecko/` | MPL-2.0; source present does not prove Gecko embedding. |
| LibreOffice / LibreOfficeKit | `9to1 Workspace/{Write,Present,Data}/Source/LibreOffice/` | Latest core `d3fca73f`; retain `COPYING*` (MPL/LGPL); installed runtime and LOK/UNO ABI are separately validated. |
| DuckDB | `9to1 Workspace/Data/Source/DuckDB/` | MIT; latest default branch `12633c22` is distinct from the existing worker's 1.5.5 runtime requirement. |
| EDS / libical | `9to1 Workspace/Planner/Source/{EvolutionDataServer,libical}/` | Preserve LGPL/dual MPL-LGPL and component notices; runtime integration not proven. |
| libvterm | `9to1 Workspace/Terminal/Source/libvterm/` | MIT; platform PTY/ConPTY APIs are not code donors. |
| VSCodium / Code - OSS | `9to1 Workspace/Dev/Source/{VSCodium,CodeOSS}/` | MIT licences retained; services not runtime-integrated. |
| glycin / Loupe | `9to1 Workspace/Picture/Source/{glycin,loupe}/` | glycin MPL/LGPL dual, Loupe GPL-3.0-or-later; loader integration and viewer parity still open. |
| GStreamer / GES | `9to1 Workspace/{Wave,Motion}/Source/{GStreamer,GES}/` | LGPL notices retained; plugin-specific terms and runtime acceptance remain separate. |
| Wine / WinBoat | `9to1 OS/compatibility/{wine,winboat}/Source/` | Wine LGPL-2.1-or-later, WinBoat MIT; host launch and VM/container acceptance not proven. |
| GNOME Shell and Mutter | `9to1 OS/platform/` submodules | Preserve upstream licences and exact submodule revisions in OS source and images. |
| Historical CakeOS/CakeAI material | `reference/` | Reference-only unless deliberately promoted with its original provenance and licence reviewed. |

## Packaging gate

No third-party-backed package is release-ready until the package contains the
required licence/notices, its source revision is immutable, and the parity record
identifies the exact included implementation. The repository's GPL-3.0 root
licence does not silently relicense independently licensed donor code.

## Donor-source audit status — 2026-09-24

`eng/donor-sources.json` records each canonical upstream, exact current default-branch HEAD selected during this pass, controlled `CroakyJake12` fork and matching fork HEAD, app-local path, upstream notice, and the separate integration/runtime requirement. The per-app `Source/DONOR-PROVENANCE.md` records preserve these boundaries. `python eng/check-donor-sources.py --local-only` validates the materialised detached donor trees; the **strict** `python eng/check-donor-sources.py` additionally requires committed app-local gitlinks for a fresh recursive checkout. Gitlink persistence remains **BLOCKED** while staging and committing are explicitly disallowed; a local clone and `.gitmodules` text alone do not pass the strict gate. No packaging or release readiness is inferred.
