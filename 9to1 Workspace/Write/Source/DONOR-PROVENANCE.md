# Write LibreOffice source

Canonical repository https://git.libreoffice.org/core and its GitHub mirror https://github.com/LibreOffice/core (`master`) matched at the selected revision; controlled fork https://github.com/CroakyJake12/libreoffice is pinned to `d3fca73f81dfb19a72a0da41598b7390eaf5845b` (selected 2026-09-24 UTC). Actual unmodified source is in `Source/LibreOffice/`, with `COPYING`, `COPYING.MPL` and `COPYING.LGPL` retained (MPL-2.0 / LGPL-3.0-or-later source terms). No patches. The existing LibreOfficeKit Writer prototype has not been built against this revision; installed system soffice is not a substitute for source.

## LibreOffice pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/LibreOffice/core and https://github.com/LibreOffice/core
- Controlled fork: https://github.com/CroakyJake12/libreoffice
- Extracted from `refs/heads/master` at commit `7528c0d406012e10c6ce34c3f24bf277302ec695` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Write/Source/LibreOffice` - 808 files, content digest `481e6e474660135179a56a8938f3a70a2ea525bad25209e3b70e94486b964c0a` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: sw/uiconfig (Writer), svx/uiconfig + chart2/uiconfig (shared UI), icon-themes, licence notices
- Licence: MPL-2.0-or-later / LGPL-3.0-or-later (LibreOffice is triple-licensed; notices read from the pinned tree); notices read from the tree: `COPYING`, `COPYING.LGPL`, `COPYING.MPL`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Stage A: Writer's UI is mirrored into CUI from the donor's own GTK Builder sets. Extracted at an exact revision rather than cloned with history because the mirror needs the UI declarations, not decades of commits. Fork/submodule conversion is a separate unit.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
