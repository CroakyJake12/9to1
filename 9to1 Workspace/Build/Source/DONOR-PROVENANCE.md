# Donor source provenance

## Gitea pinned snapshot (2026-10-31, Stage A UI mirror)

- Upstream: https://github.com/go-gitea/gitea and https://github.com/go-gitea/gitea
- Controlled fork: https://github.com/CroakyJake12/gitea-9to1
- Extracted from `refs/tags/v28.2.0` at commit `829b49c1ae96015e345f9d2a0eb1ea7acd5f31e1` (fetch date 2026-10-31).
- Local tree: `9to1 Workspace/Build/Source/Gitea` - 6366 files, content digest `756481adff65dfc504d8c13175fb802359d6318ec184e00490ce70fb789c5d41` (sha256 over every relative path plus that file's sha256). The digest is what makes a silent in-place edit of donor source detectable.
- Modules kept: templates (Go html/template) and web_src
- Licence: MIT; notices read from the tree: `LICENSE`
- Fork state: the exact pinned object is reachable in the controlled fork (measured with the GitHub API on this date). That is object reachability, **not** a submodule gitlink: the tree is not committed to this repository and recursive checkout cannot materialise it. Converting the snapshot into a fork submodule pin is a separate verified unit.
- Why snapshot rather than submodule: Stage A mirror of the source-collaboration web UI. Latest release, per the owner's always-the-latest rule.
- Evidence level: source materialisation and licence custody only. Nothing here is built, run, rendered, packaged or accepted.
