# Donor source provenance

## Thunderbird pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/mozilla-thunderbird/thunderbird and https://github.com/mozilla-thunderbird/thunderbird
- Controlled fork: https://github.com/CroakyJake12/thunderbird-9to1
- Extracted from `refs/heads/main` at commit `86898fed4da61aa8b935a87a849b8052ebb9c98e` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Connect/Source/Thunderbird` - 1774 files, content digest `0c7add8f2ff578aeaeb041deb4ec0bcab7c4b2cab0425321cb506b7ebe370704` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: comm/mail (the email client UI), its themes, and the shared mail chrome
- Licence: MPL-2.0 (upstream notice: toolkit/content/license.html); notices read from the tree: `LICENSE`, `toolkit/content/license.html`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Owner scope decision 2026-10-31: Connect ships the email capabilities only, so this is the Mail donor. The GitHub mirror publishes `main`; the comm-esr release refs are not exposed on it, which is recorded instead of glossed over.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
