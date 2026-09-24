# Boards external donor source

Selected 2026-09-24 UTC from live upstream default branches; see `eng/donor-sources.json` for branch, runtime and compatibility boundaries. These source trees are unmodified. Nested Git checkouts are detached at the listed SHAs.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence retained |
| --- | --- | --- | --- | --- |
| AppFlowy Board | https://github.com/AppFlowy-IO/appflowy-board @ `804d7898ac0becabf73e45527baf5d5c573cd6bb` | https://github.com/CroakyJake12/appflowy-board @ `804d7898ac0becabf73e45527baf5d5c573cd6bb` | `Source/AppFlowyBoard` | `LICENSE` (AGPL-3.0 OR MPL-2.0; Boards selects MPL-2.0) |
| Rnote | https://github.com/flxzt/rnote @ `1a728d6a85db3528f9c79dc0990700e91b22696f` | https://github.com/CroakyJake12/rnote @ `1a728d6a85db3528f9c79dc0990700e91b22696f` | `Source/Rnote` | `LICENSE` (GPL-3.0-or-later) |

AppFlowy Board proof still uses its existing pin; Rnote freeform integration at this latest commit is not proven. No donor files were modified or patched. Source presence is separate from integration and physical UI acceptance.
