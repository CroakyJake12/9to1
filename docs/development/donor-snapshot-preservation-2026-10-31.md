# Donor snapshot preservation on a remote — 31 October 2026

## Why this document exists

The owner asked whether anything from AppFlowy could be lost, and then instructed that the source
pulls be committed to a remote. The answer at the time of asking was **yes, it could be lost**: every
`scope: snapshot` donor existed only as untracked working files. Measured with the repository's own
tooling:

```
$ git ls-files "9to1 Workspace/Boards/Source/AppFlowy" | wc -l
0
```

A registry entry recording an upstream commit and a content digest is not preservation. A clean
checkout, a hard reset, a lost disk or a different worker machine would have removed the AppFlowy UI
reference that the Boards re-UI work is supposed to read.

## What is now on a remote, and how each copy was proved

| App | Donor | Files | Content digest (verified) | Preservation repository | Commit / tag |
| --- | --- | ---: | --- | --- | --- |
| Boards | appflowy | 4,543 | `9f8d94e8…` | `CroakyJake12/donor-appflowy` | `29c9b46d7ef0` / `upstream-0.14.11-5cf3a365dec0` |
| Connect | thunderbird | 1,774 | `0c7add8f…` | `CroakyJake12/donor-connect-thunderbird` | `9c90d8a47033` / `upstream-main-86898fed4da6` |
| Build | gitea | 6,366 | `756481ad…` | `CroakyJake12/donor-build-gitea` | `f3839630fb3a` / `upstream-v28.2.0-829b49c1ae96` |
| Dev | code-oss | 21,264 | `abf5007d…` | `CroakyJake12/donor-dev-code-oss` | `d4959f389314` / `upstream-1.144.0-2c33b50b16c4` |
| Write | libreoffice | 808 | `481e6e47…` | `CroakyJake12/donor-libreoffice-write` | `e170f1365103` / `upstream-master-7528c0d40601` |
| Present | libreoffice | 486 | `8a13fd0e…` | `CroakyJake12/donor-libreoffice-present` | `415d520f3f79` / `upstream-master-7528c0d40601` |
| Data | libreoffice | 488 | `eafb3acb…` | `CroakyJake12/donor-libreoffice-data` | `8dcadbe7475e` / `upstream-master-7528c0d40601` |

Total preserved: 35,275 files, 431 MB.

Every digest in that table is the digest **already recorded** in `eng/donor-sources.json`, re-derived
after the push route, not a new number chosen to make the record pass.

## How faithfulness is proved (`eng/development/preserve_donor_snapshot.py`)

The tool refuses instead of improvising, and it only prints success after three checks:

1. the local tree's `snapshot_digest` (the same function `eng/check-donor-sources.py` uses) equals the
   recorded digest, so an edited tree is never pushed and called faithful;
2. the **prepared commit**, exported with `git archive` into a clean directory, reproduces that digest —
   this is the check that catches silent byte changes introduced by git itself;
3. `git ls-remote` shows both the commit and the upstream tag on the remote.

Two real defects were found by those checks rather than assumed away:

- **git normalisation altered a donor tree.** Gitea's exported digest came out `67f22f23ae92` against
  the recorded `756481adff65`: the donor's own `.gitattributes` let git rewrite line endings on commit.
  The tool now disables text conversion and export filters through `.git/info/attributes` (inside `.git`,
  so it is not part of the preserved tree). Gitea then reproduced its recorded digest exactly.
- **the donor's `.gitignore` drops files the snapshot contains.** AppFlowy's recorded tree includes
  `frontend/appflowy_flutter/assets/translations/mr-IN.json` and `frontend/rust-lib/flowy-sqlite/.env`,
  which a plain `git add` skipped, giving 4,542 files instead of 4,543. The tool uses `git add -A -f` and
  refuses on any file-count disagreement, so the preserved copy is the recorded copy.

A third guard exists because GitHub rejects single files over 100 MB: the tool stops and reports the
oversized paths rather than pruning them, which would have made "byte-faithful" false.

## What is still open, stated plainly

- The loose trees remain the materialisation the donor gate checks. Converting each path into a
  committed **submodule/gitlink** pin inside `9to1` is a separate verified unit and has **not** been done.
  Until then `eng/check-donor-sources.py --local-only` still reports: *SNAPSHOT DONORS WITHOUT A GITLINK (7)*.
- The remote copies are preservation snapshots, not forks carrying upstream history; only the recorded
  upstream revision is referenced, by tag.
- No product code was changed by this work, and nothing here is on-screen or release evidence.
