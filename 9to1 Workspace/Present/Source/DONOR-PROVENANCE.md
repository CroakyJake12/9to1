# Present LibreOffice source

Canonical repository https://git.libreoffice.org/core and its GitHub mirror https://github.com/LibreOffice/core (`master`) matched at the selected revision; controlled fork https://github.com/CroakyJake12/libreoffice is pinned to `d3fca73f81dfb19a72a0da41598b7390eaf5845b` (selected 2026-09-24 UTC). Actual unmodified source is in `Source/LibreOffice/`; `COPYING`, `COPYING.MPL` and `COPYING.LGPL` retain MPL-2.0 / LGPL-3.0-or-later terms. No patches. The Impress/LibreOfficeKit worker's ABI with this revision is unverified; its current headless tests do not establish donor-runtime compatibility.

## LibreOffice pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/LibreOffice/core and https://github.com/LibreOffice/core
- Controlled fork: https://github.com/CroakyJake12/libreoffice
- Extracted from `refs/heads/master` at commit `7528c0d406012e10c6ce34c3f24bf277302ec695` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Present/Source/LibreOffice` - 486 files, content digest `8a13fd0e343e30d8ed827082911dd8ed862639702516a73291edf3e6fcc86254` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: sd/uiconfig (Impress), svx/uiconfig + chart2/uiconfig (shared UI), icon-themes, licence notices
- Licence: MPL-2.0-or-later / LGPL-3.0-or-later (LibreOffice is triple-licensed; notices read from the pinned tree); notices read from the tree: `COPYING`, `COPYING.LGPL`, `COPYING.MPL`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Stage A mirror of the Impress UI from its own module tree. Owner decision 2026-10-31: Draw and Math are NOT donors, so this tree carries no sd/../draw or math module; Impress, Writer and Calc only. Kept as its own app-local tree so the binding path is real.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
