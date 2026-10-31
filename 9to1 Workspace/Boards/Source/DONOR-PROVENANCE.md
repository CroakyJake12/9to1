# Boards external donor source

Selected 2026-09-24 UTC from live upstream default branches; see `eng/donor-sources.json` for branch, runtime and compatibility boundaries. These source trees are unmodified. Nested Git checkouts are detached at the listed SHAs.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence retained |
| --- | --- | --- | --- | --- |
| AppFlowy Board | https://github.com/AppFlowy-IO/appflowy-board @ `804d7898ac0becabf73e45527baf5d5c573cd6bb` | https://github.com/CroakyJake12/appflowy-board @ `804d7898ac0becabf73e45527baf5d5c573cd6bb` | `Source/AppFlowyBoard` | `LICENSE` (AGPL-3.0 OR MPL-2.0; Boards selects MPL-2.0) |
| Rnote | https://github.com/flxzt/rnote @ `1a728d6a85db3528f9c79dc0990700e91b22696f` | https://github.com/CroakyJake12/rnote @ `1a728d6a85db3528f9c79dc0990700e91b22696f` | `Source/Rnote` | `LICENSE` (GPL-3.0-or-later) |

AppFlowy Board proof still uses its existing pin; Rnote freeform integration at this latest commit is not proven. No donor files were modified or patched. Source presence is separate from integration and physical UI acceptance.

## Pinned as submodules 30 October 2026 - measured state, including what is NOT consumed

- `Source/AppFlowyBoard` gitlink pinned at `804d7898ac0becabf73e45527baf5d5c573cd6bb`
  (`git describe` -> `0.1.0-41-g804d789`, i.e. 41 commits past tag v0.1.0). Previously this donor was
  declared in `.gitmodules` with **no gitlink in HEAD**, so `git submodule update` could never fetch it.
- Discrepancy recorded, not resolved: the donor registry describes the consumer as "Board proof uses
  the older **0.1.2 package** pin", yet no package or project reference to AppFlowy exists in any
  Boards project (verified by search). The only file named after the donor,
  `contract/AppFlowyBoardEventAdapter.cs`, is pure C# mapping from described AppFlowy callbacks onto
  Haven command records and **links no Flutter, Dart or AppFlowy code**. So Boards is currently a
  first-party implementation with a donor-parity checklist, not a build on donor code.
- `Source/Rnote` gitlink pinned at the recorded manifest commit
  `1a728d6a85db3528f9c79dc0990700e91b22696f` (`git describe` -> `v0.14.2-36-g1a728d6a`; its
  `Cargo.toml` declares `version = "0.15.0"`). **No Boards build consumes Rnote today** - the only
  references are parity/evidence documents. This pin therefore records the documented donor revision,
  it is not a build dependency.
- Cross-donor note: Canvas pins the same donor at **v0.14.2** (`8cac558fa73e`), the version its bridge
  actually compiles against, while Boards records the **v0.15.0** line. That divergence is unresolved
  and is now visible in the pins rather than hidden in prose.

## AppFlowy pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/AppFlowy-IO/AppFlowy and https://github.com/AppFlowy-IO/AppFlowy
- Controlled fork: https://github.com/CroakyJake12/appflowy-9to1
- Extracted from `refs/tags/0.14.11` at commit `5cf3a365dec0d59f64bad1ee4bb1050471a39b93` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Boards/Source/AppFlowy` - 4543 files, content digest `9f8d94e88ee5f5d94687d674583e304c0507298853f6900c9cf524b8749bcde4` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: frontend/ (appflowy_flutter UI, including the board widget, + rust-lib engine)
- Licence: AGPL-3.0-only (read from the pinned tree's LICENSE); notices read from the tree: `LICENSE`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Owner decision 2026-10-31: fork the newest AppFlowy rather than the old stand-alone board widget package, because the board view lives inside the application and the shared engine comes from frontend/rust-lib. The previously pinned appflowy-board package stays in history and remains what the app itself depends on (pubspec pins e8317c0d1af8).
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
