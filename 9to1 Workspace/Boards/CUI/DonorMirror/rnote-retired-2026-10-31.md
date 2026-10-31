# Boards / rnote UI mirror retired — 31 October 2026

**What was here:** a Stage A mirror of the rnote *application* UI (`crates/rnote-ui/data/ui/**`, 33 mirrored
`.cui` documents plus gap and inventory records), generated from Boards' own rnote checkout.

**Why it is retired** — the owner's question on 31 October 2026 was correct:

1. **Boards' UI donor is AppFlowy, not rnote.** That is the owner's decision of 31 October 2026 (fork the
   whole current AppFlowy application). rnote enters Boards only as the shared **drawing/ink engine**, which
   the canonical specification states at lines 1400, 9120, 9459 and 10107: the shared engine MUST expose the
   Rnote-derived drawing/ink engine as a reusable shared object/layer. An engine contribution is not a
   licence to carry rnote's app window, app menu and sidebar as Boards' UI.
2. **It was a duplicate of the Canvas mirror at a different revision.** Measured before removal: of 33
   mirrored documents, **31 were byte-identical** to `9to1 Workspace/Canvas/CUI/DonorMirror/rnote`, and 2
   differed. The two donor checkouts are pinned apart — Boards at `1a728d6a85db` (the v0.15 line) and Canvas
   at `8cac558fa73e` (v0.14.2, the revision the Canvas bridge actually compiles) — and their UI directories
   differ in 8 files: `canvasmenu.ui`, `dialogs/dialogs.ui`, `dialogs/export.ui`, `dialogs/import.ui`,
   `mainheader.ui`, `overlays.ui`, `shortcuts.ui`, `sidebar.ui`.
3. **Carrying two revisions of one shared surface is the exact failure mode the pinned single-implementation
   requirement forbids** ("never maintain separate older Haven and newer standalone versions of an app").
   Keeping this corpus would have made the split look intentional.

**What replaces it:** Boards' real UI mirror is now `9to1 Workspace/Boards/CUI/DonorMirror/appflowy`, built
by the new Dart dialect (`eng/cui-mirror/dialect_dart.py`) from AppFlowy's own widget code. The shared ink
surface stays pinned once, in Canvas, at the revision actually consumed; moving it onto the fork branch
`9to1/canvas-engine-20261030` (`44ff1089`) and deciding the v0.14.2 → v0.15 question remain open work items,
recorded in `Canvas/Source/DONOR-PROVENANCE.md`.

**Recovery:** the retired corpus is preserved unchanged in Git at commit `04a19137`. Read it back with
`git show 04a19137:"9to1 Workspace/Boards/CUI/DonorMirror/rnote"` or restore a single file with
`git checkout 04a19137 -- "9to1 Workspace/Boards/CUI/DonorMirror/rnote"`. Recorded content digest of the
retired corpus (sha256 over every relative path plus that file's digest):
`c2892b9c4b401d5a…` — full digest recomputable from those blobs; 36 files, 33 of them `.cui`.

Nothing was deleted from history, and no donor source was touched: `Boards/Source/Rnote` remains as the
engine binding, pinned and provenance-recorded.
