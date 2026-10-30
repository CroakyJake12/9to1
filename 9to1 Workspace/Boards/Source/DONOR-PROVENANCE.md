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
