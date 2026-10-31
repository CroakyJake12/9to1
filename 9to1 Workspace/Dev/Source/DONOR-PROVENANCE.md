# Dev editor-service donor sources

Selected from live upstream default branches 2026-09-24 UTC. No donor source modifications or patches. The existing read-only Dev app does not yet run these editor services.

| Donor | Upstream URL and commit | Controlled fork URL and commit | Source | Licence files retained |
| --- | --- | --- | --- | --- |
| VSCodium | https://github.com/VSCodium/vscodium @ `5a73682ca091082675b10c9dc3f348c1d824d94f` (tag `1.135.06055`) | https://github.com/CroakyJake12/vscodium @ `5a73682ca091082675b10c9dc3f348c1d824d94f` | `Source/VSCodium` | `LICENSE` (MIT) |
| Code - OSS | https://github.com/microsoft/vscode @ `4212437dd11e9c7e728caa26a269540c87343930` | https://github.com/CroakyJake12/vscode @ `4212437dd11e9c7e728caa26a269540c87343930` | `Source/CodeOSS` | `LICENSE.txt` (MIT) |

## Code - OSS pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/microsoft/vscode and https://github.com/microsoft/vscode
- Controlled fork: https://github.com/CroakyJake12/vscode-9to1
- Extracted from `refs/tags/1.144.0` at commit `2c33b50b16c472efe58357d1d2a8a5414bc23fbf` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Dev/Source/CodeOSS` - 21264 files, content digest `abf5007dae54e7c46b69bfc530debdcca3305aeb51358080ce818fc672b35504` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: whole release tag tree (workbench, editor, extensions)
- Licence: MIT; notices read from the tree: `LICENSE.txt`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Stage A reference for Build's code workspace. Its workbench UI is TypeScript-built custom elements, so the mirror pass for it is not written yet; the snapshot makes the pin and the licence real rather than implied.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
