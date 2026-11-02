# Qwen continuation — Boards first, then the shared theme pass (1 November 2026)

Durable checkpoint for the local Qwen run. Re-read this after compaction together with `AGENTS.md`
and `docs/development/model-performance-feedback-2026-10-29.md`.

Work order sources: `9to1_Qwen_Handoff_v3.1` pack (`START_QWEN.md`) plus the v3.1 amendment embedded in
`9to1_Development_Specification_v3.1.docx` (requirement IDs `V31-BOARDS-001–007`, `V31-DELIVERY-001`,
`V31-THEMES-001–003`). The corrected v3.2 atlas is `newAtlas.html`.

## 1. Supplied pack: what is present, what is missing

Pack as received (`/home/jacob/Downloads/9to1.zip`, unpacked at `/home/jacob/Downloads/9to1_pack_v3/9to1spec`),
SHA-256 recorded 1 November:

| File | SHA-256 | Status |
|---|---|---|
| `9to1_Development_Specification_v3.1.docx` | `32468a9822162ff5bd84dd2f4f4ca462ec73fdad79444b600a0e75a5bf16838f` | present |
| `newAtlas.html` (self-describes as *v3.2 corrected* atlas: "corrected Bubble, Glow, Playful and Frosted materials; retained v3.1 Boards controls; retained slides 1–16 authority; eight current themes") | `80a4ecc6957529aa63216c3c3037401f8360e0ace47efafbcabd530f1fc1a131` | present |
| `ThemeRefs.zip` (27 PNGs across exactly the 10 folders the amendment counts) | `e88fb828620217dfa0401703c6ea6643585468cd45bc56ae33a408adcd40d024` | present |
| `START_QWEN.md` | `6e443d7518bb6a5462488b30aeb094741d3de6de0e2297d003bdfbb878e48f76` | present |

Named by `START_QWEN.md` / the amendment but **not supplied** anywhere on this machine (searched
`/` `-xdev` for `*Theme_Correction*`, `*OWNER_DECISIONS*`, `*BOARDS_ACCEPTANCE*`,
`*THEME_REFERENCE_INDEX*`; exact missing names, contents not invented):

- `OWNER_DECISIONS_v3.1.md` — the "full compact amendment". Its clause IDs (`V31-BOARDS-001–007`,
  `V31-DELIVERY-001`, `V31-THEMES-001–003`) *are* present verbatim inside the v3.1 DOCX
  (extracted text retained at `/tmp/opencode/spec-v31.txt`, lines 4173–4227), so the requirement text is
  available; the standalone file is not.
- `BOARDS_ACCEPTANCE.md` — genuinely absent. Nearest retained authority: the v3.0 pack's
  `ACCEPTANCE.md` (`/home/jacob/Downloads/9to1_Boards_First_v3/ACCEPTANCE.md`, gates `BF-01`–`BF-20`),
  which I am using as the acceptance recipe set, plus the v3 DOCX clauses
  `V3-BOARDS-ACC-01/02` and `V3-ACCEPTANCE-*`.
- `THEME_REFERENCE_INDEX.md` — absent. The DOCX `V31-THEMES-001–002` gallery rows (one per folder with
  per-folder image counts) were cross-checked against `ThemeRefs.zip` and agree: Professional 3, Glow 2,
  Playful 4, Frost 2, Bubble 4, Industrial 3, Retro 2, Western 2, Synthwave 4, Snug 1 = 27.
- `MANIFEST.json` — absent, so the package hash record itself is missing; the hashes above are mine.
- `9to1_Interface_Atlas_v3.1.html` — absent under that name. `newAtlas.html` is the v3.2-corrected
  successor and is what the owner's 1 November instruction points me at ("the corrected v3.2 atlas").
- **The whole `9to1_Theme_Correction_v3.2` pack** (the "Markdown instructions and amendment" the owner
  referenced) is **not on this machine**. Consequence recorded honestly: the *theme* guidance I hold is the
  v3.1 DOCX text plus the v3.2-corrected atlas. Any v3.2 markdown-only theme revision I have not seen, so
  theme-pass claims stay provisional until that pack is supplied. Boards scope is unaffected: it is fully
  specified by `START_QWEN.md` §3 + `V31-BOARDS-001–007` + `V3-BOARDS-*` + the v2 canonical spec.
- Referenced technical chapters `technical/boards-01.md` … `technical/boards-07.md` (and
  `modules/boards.complete.md`) are not in the repository and were never supplied. Recorded as source gaps;
  no clauses reconstructed.

**Correction recorded 1 Nov afternoon** (verified by hash, not by memory): the v3.2 *atlas* is on this
machine — `newAtlas.html` and the owner-attached `/tmp/codex-file-preview-2hRdoO/9to1_Interface_Atlas_v3.2.html`
have the identical SHA-256 `80a4ecc6…1a131`, so they are the same file under two names. `ThemeRefs.zip`
is also unpacked at `/home/jacob/Downloads/ThemeRefs/` with exactly 27 PNGs in the 10 folders
(Bubble 4, Playful 4, Synthwave 4, Professional 3, Industrial 3, Frost 2, Glow 2, Retro 2, Western 2,
Snug 1). What remains absent is the **markdown** half of the `9to1_Theme_Correction_v3.2` pack
(`OWNER_DECISIONS_v3.1.md`, `THEME_REFERENCE_INDEX.md`, `MANIFEST.json`, `BOARDS_ACCEPTANCE.md`), so the
theme-pass *instructions* stay provisional while the atlas and reference images themselves are usable.

## 2. Baseline integration (safe pull, done first)

Observed before any edit: branch `main`, upstream `origin/main` = `https://github.com/CroakyJake12/9to1.git`,
local HEAD `bf0375e4`, one dirty file `eng/spec-coverage/coverage-summary.json` (tool-generated summary),
no `.git/index.lock`, no competing build writer, Astra's `.goal/CURRENT` status `PAUSED_OWNER_WRAP`
("all Astra workers stopped"). One stale **test** instance of the standalone Boards app from the previous
Qwen session is still running (PID 3270834, private profile copy under `/tmp/opencode/boards-cui-evidence`);
it was left running, not killed.

Incoming Astra work on `origin/main`, verified from history (not from chat claims), 8 commits
`bf0375e4..f7b86225`, no local divergence (`git log origin/main..HEAD` empty):

| Commit | Subject |
|---|---|
| `6efb2fed` | fix(cui): keep popups anchored and adaptive toolbars stable |
| `94d7c865` | fix(cui): preserve keyboard focus in nested menu inputs |
| `26f5cccb` | feat(boards): redesign document toolbar and preserve editing focus |
| `115834b9` | fix(boards): expose accurate caret values and list capabilities |
| `a0f37f4f` | fix(boards): retain editor focus after list conversion |
| `d748bbf9` | docs(boards): checkpoint verified usability overhaul and native evidence |
| `221510f1` | Improve Boards editing continuity and Spaces recovery |
| `f7b86225` | Record verified Boards and Spaces sprint handoff |

Integration: `git pull --ff-only` → now `f7b86225`; the dirty summary file was not touched by the incoming
diff and is preserved (it re-generates on the next `assess.py`/`write_boards_state.py` run). No stash,
reset, clean, rebase or force operation was used. Astra's own handoffs:
`docs/development/boards-usability-handoff-2026-10-31.md`,
`docs/development/boards-spaces-sprint-handoff-2026-10-31.md`, evidence under
`docs/development/evidence/boards-usability-2026-10-31/` and `.../boards-spaces-sprint-2026-10-31/`.

Astra explicitly labels part of its work **not natively retested** on the final sprint build, and lists
prepared-but-unapplied drafts outside Git (`/workspace/scratch/astra-list-runs-a`, `astra-graphemes`,
`astra-nested-next`, `astra-nested-toolbar-range`, `boards-lossless-next`, `astra-toolbar-size`,
`spaces-20261031-prepared/APPLY.md`). Those paths are on the cloud worker, not on this PC, so they are
**not available to me** — recorded, not assumed integrated.

## 3. Local machine facts (this PC, not the cloud worker)

- Repo/checkout: `/home/jacob/Documents/GitHub/9to1` (main canonical, per 28 October owner direction).
- Only usable SDK: `/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from/work/dotnet-sdk-10.0.301/dotnet`
  (10.0.301). `/usr/bin/dotnet` is 8.0 and cannot build net10.0.
- Guard used: `python3 -B eng/development/guarded_lowmem_build.py --floor-mib 1100 --timeout 1800 --log <log> --`
  serialised with `flock -n /tmp/opencode/9to1-sdk.lock`. Memory is tight because this session *is* the
  resident Strata host (~52 GiB RSS); `free -m` shows ~4 GiB available and swap full, so the guard stays at
  floor 1100 MiB and SDK work stays strictly serial (never two builds/tests at once).
- Native evidence tooling that already works here: `eng/development/capture_window.py` (real X11 pixel
  capture) and `eng/development/drive_window.py` (synthetic X11 input addressed by exact window id).
  `DISPLAY=:1`, X11 session, python-xlib + PIL present. **`wmctrl` is not installed**, so
  `live_preview.py` automatic close/reload cannot be used on this PC; normal in-app Exit is used instead.
- No scheduled tasks, no self-restarting services, no new background services are created by this run.
  Astra's 2%-usage stop rule does not apply locally; no usage meter is invented.

## 4. Requirement ledger being maintained

- Boards MUST rows: `eng/spec-coverage/assessments/Boards.json` (138 rows, generated by
  `eng/spec-coverage/write_boards_state.py` from the pinned register `eng/spec-coverage/register.json`).
  States at takeover: IMPLEMENTED 15, PARTIAL 95, NOT-IMPLEMENTED 22, NOT-ASSESSED 6. These are
  code/test-reading states, **not** acceptance.
- The 22 NOT-IMPLEMENTED rows cluster into: canonical Board/Page entity model (1), Rnote ink engine
  consumption (2), AI typed-action namespace `9to1.Boards.*` (2 + error identities), Chat / Scoped Chat
  page types (10), large-board virtualisation and performance harness (4), collaboration revision
  conflict semantics (1), Home-absent bootstrap (1), Save-status truthfulness (1, NOT-ASSESSED).
- `V31-BOARDS-001–007` (toolbar/identity/Add-destinations/preserve-CUI) map onto Astra's delivered work;
  they are being verified against the running app rather than trusted.

## 5. Plan (in order, no theme work before the Boards gate)

1. Verify the inherited baseline compiles and passes the real Boards suite on this PC. *(done: 565/565
   re-measured here, `/tmp/opencode/qwen-1101-boards-baseline.log`)*
2. Drive the actual mounted route (`Boards/app/Boards.cui`, standalone `Program.cs`) natively and
   re-test each `START_QWEN.md` §3 / `V31-BOARDS-001–007` decision as an observed behaviour, with
   screenshots and input receipts. Fix only defects reproduced on this build. *(done for the identity row,
   adaptive/fixed pills, Style/Font grouping, contextual Properties, Add destinations, and the menu
   defects found this way — five in Milestone 1, the Add-menu selection/alignment pair in Milestone 4)*
3. Continue the remaining Boards specification in dependency order: list-item range formatting,
   font-size choices, universal contextual Properties, heading fallback readout, truthful drawing-history
   labels, Escape-cancel tab names, stale AddTabber/Enter guards, nested toolbar-held range continuation,
   grapheme-boundary selection, then the ledger's NOT-IMPLEMENTED clusters that are local-first
   (save-status truth, canonical error identities, block/ink undo boundaries, mode matrix Edit/View ×
   Locked × Snap). *(mode matrix done in Milestone 3; the rest still open)*
4. Execute the acceptance gates `BF-01`–`BF-20` against this build with disposable fixtures, including
   save → close → reopen in a **fresh process**; record exact commands, build identity, dirty state,
   screenshots, and keep FAIL/BLOCKED/NOT RUN visible. *(exercised with evidence so far: BF-02, BF-03,
   BF-05, BF-06, BF-08, BF-09, BF-11, BF-12, BF-14 (block reorder/undo/redo in text mode — rows 43–44 —
   plus ink transforms), the history half of BF-19, and the canonical Data-embed half of BF-15 (row 45).
   Still NOT RUN or open on this build: BF-01 (the Haven host route is unverifiable here — see the
   `Haven.Desktop.Tests` limitation), BF-04, BF-07 (headless click-to-block and typed-text-into-block
   coverage now exists from row 52; the native re-probe was invalidated by the contaminated fixture),
   BF-10, BF-13 (tables remain NOT RUN natively: the
   database half of structured content is exercised, the table half is not), BF-15's Chat / Scoped Chat
   page types, BF-16, BF-17 (menu navigation and the undo/redo keys are exercised; the deep keyboard walk
   is inconclusive — row 48), BF-18 (only the authored-content half: ink colours and view transforms
   preserved across save/reopen), the search half of BF-19 and BF-20. The gate as a whole has therefore
   **not** passed, and the theme pass must not start until that is closed or the owner narrows the gate.)*
5. Only after that gate: shared CUI theme-design pass using the corrected v3.2 atlas + `ThemeRefs/`,
   honouring `V31-THEMES-001–003`, `V3-CUI-LAY/FEA/ENT/PER/ACC`, and the unresolved mappings
   (Frost≠renamed Frosted Glass, Industrial≠Metallic, Synthwave/Snug not built-ins).
6. Commit coherent verified units and push to `origin/main` at safe milestones (no force-push).

**Position at this checkpoint:** branch `main`, HEAD `ff5eba4a`, `origin/main` = `ff5eba4a` (pushed; history
this run `f7b86225` → `64f0e394` → `50dfb42d` → `d7bf967b` → `7450816e` → `67a0bf55` → `a9b93148`
→ `3a35032e` → `c30ef24a` → `532e25ba` → `bac7398d` → `a2f820bf` → `3dbf512c` → `24f07d80` → `e49c138a`
→ `566145dd` → `222610f6` → `f1fe3588` → `ba6a7bd4` → `ceec047c` → `ec351be1` → `271bc113`
→ `5c0ba8fe` → `59c7fc53`), only inherited
`eng/spec-coverage/coverage-summary.json` dirty and deliberately left unstaged, plus one **other session's**
untracked document (`docs/development/boards-usability-review-2026-11-01.md`, hashed in the Milestone 8
reviewer note) left for its author to commit. Suite counts measured on this PC: contract **140/140**, app
**609/609**. The standalone binary on disk is `09bcf761b4044d66fc1a8345e0f1dc3b01145e20e0d107de3f459bdc5b59e114`
built 19:54 from `ec351be1`, which is the candidate every run26 frame below was taken from.

## 6. Evidence log (append)

### Milestone 1 — native verification of Astra's build, then five repaired defects (1 Nov 2026)

Native method (this PC, x64, X11 `:1`): standalone `CakeOS.Apps.Boards.App.dll` built Release from
`f7b86225` + my edits, launched with an explicit private profile (`HAVEN_DATA_DIR` under
`/tmp/opencode/qwen-native-20261101/profile*`, and a sandbox `HOME` so nothing is written into the
owner's real `Documents/9to1/Boards`). Window placed at the reference viewport 2850×1781 device px =
**1440×900 logical at scale 1.979** (Xft.dpi 190). Input through `eng/development/drive_window.py`
(same-mechanism helper `step.py`, private, not in Git), frames through
`eng/development/capture_window.py`. Every step was checked **against the physical `.9to1board` file**,
not only against a frame.

Baseline first: the inherited build compiled and `565/565` Boards app tests passed **on this PC**
(`/tmp/opencode/qwen-1101-boards-baseline.log`, guard `OK`, 283 s, min available 1957 MiB), which
independently confirms Astra's claimed `565/565`.

| # | Requirement ID(s) | Action | Result / evidence |
|---|---|---|---|
| 1 | — | `git fetch` + inspection, then `git pull --ff-only` | HEAD `bf0375e4` → `f7b86225`; 8 Astra commits integrated; diff 115 files, +7680/−1202; dirty `eng/spec-coverage/coverage-summary.json` preserved |
| 2 | V3-BOARDS-ACC-01, BF-02 | fresh private profile, no account/Files/AI: landing → "Create a board" | one local board created in the profile's draft store; no provider needed; captured `02-sandbox-splash.png`, `11-view.png` |
| 3 | V31-BOARDS-001 | clicked the board name | the file menu opened from the name (Save/Save As…/New Board…/Open Board…/All Boards…/Rename…/Board properties…/Exit), no separate File button; `09-identity-menu-popup-580001f.png`, `30-menu-popup-5800073.png`; keyboard navigation reached Exit (`32-menu-navigated-popup-5800083.png`) |
| 4 | V31-BOARDS-002/003/004, BF-04 | read the toolbar on screen | one adaptive pill (style, font family, −/size/+ , `Aa`, then B I U S + colour/highlight, then alignment/list/spacing/indent, then Properties) plus the separate fixed pill `+ Add` / `Drawing` / `AI` (AI honestly disabled with a reason) — `11-view.png`, `42-crop-topbar.png` |
| 5 | V3-BOARDS-FEA-02, BF-07 | clicked into a block, Shift+Left ×3, `Ctrl+B`, then the toolbar Italic button | document showed the run marked bold (`[B]`) then bold+italic (`[BI]`), i.e. the toolbar's real wiring works; step receipts `M2/M3` |
| 6 | UX27-BOARD typing/Enter | clicked, `End`, typed, `Return`, typed | text continued in the focused block, `Return` split into a sibling that took the new text; document-level (`M5/M6`, and natively `AC-enter-split-and-type` → second block `'SECOND'`) |
| 7 | BF-03, V3-BOARDS-ENT-01 | `Ctrl+S`, then file-menu **Exit** (clean process end), then a **new process** reopened the board from the landing's Recent card | the reopened file kept 3 blocks, the same block IDs, the text and the run boundaries (`'ZedAPP'` unmarked + `'EMORE'` bold+italic) — `33-reopen-landing.png`, `34-reopened.png` + document dump. This is the native round trip Astra listed as pending. |
| 8 | G0:L9437 (MUST), V3-BOARDS-LAY-01, V3-BOARDS-FEA-03, V3-BOARDS-ACC-02 | **fixed**: Edit/View and the layout mode were only inside the "…" overflow, so the required persistent page-mode switch was not visible at all | new persistent chrome: `Edit`/`View` segmented control + `Vertical Stacking` layout-mode button in the top-right, both visible on screen (`41`/`42-crop-topbar.png`); the two overflow rows were removed rather than duplicated; commands `SetEditModeOn`/`SetEditModeOff` refuse a no-op re-selection, View mode refuses mutation (`AddBlock`, `ToggleBold` leave the snapshot unchanged and say why) |
| 9 | BF-07, V3-BOARDS-FEA-02, unified undo | **fixed**: the block text controls kept a private character-level undo stack next to the canonical board history | measured natively: two `Ctrl+Z` after a bold+italic pair removed two **characters** (`'ZedAPPEND'` → `'ZedAPPE'`) while the marks stayed. Now `UndoLimit = 0` on paragraph/heading/code, list-row and table-cell editors; the session history is the only undo owner. Native after-fix sequence: select-all, bold, `Ctrl+Z`, `Ctrl+Y`, `Ctrl+Z` toggled the **formatting** with the text intact (`X6/X7/X8/X9`). Covered by `Formatting_is_undone_as_formatting_and_the_typed_text_survives` and `Document_text_controls_expose_no_private_undo_stack` |
| 10 | boards-insert, BF-05, V31-BOARDS-005 | **fixed**: the empty-page "+ Add your first block" wrote the literal words **"New block"** into the document and bypassed the canonical insertion path | `AddBlock` now goes through `InsertKindAsync("paragraph", initialText: "", focusEditor: true)`; the paragraph/heading insertion default no longer pre-fills placeholder words. Native: a fresh board's first block is empty (`0:''`) and the inserted block is an empty focused paragraph; test `Empty_page_add_block_inserts_one_empty_canonical_paragraph_and_undoes_in_one_step` |
| 11 | BF-02, BF-07 (typing at all) | **fixed, root cause of the "can't start typing" complaint**: the block row's insertion-preview overlay had `Background="Transparent"`, which in Avalonia is hit-testable, and it sits above the block row — so a click in the blank part of a paragraph never reached its editor | before: click + type produced nothing in the saved file (`W1..W5`, `Y1..Y3`, `Z1..Z4` all `''`); after removing the fill from `BlockInsertionPreviewCui.cui`, the same click+type writes (`AB-click-blank-paragraph` → `'TYPEDNATIVE'`), and `Clicking_the_blank_part_of_a_paragraph_reaches_the_editor_and_typing_writes_to_the_document` covers it |
| 12 | V31-BOARDS-006/007, BF-19 | **fixed**: `AppFlowy re-UI (stage one)` was offered in the owner-facing board file menu | removed from `Boards.cui`; the comparison seam remains opt-in per process (`BOARDS_STAGE_ONE_PREVIEW=1`) and no AppFlowy UI or donor source was imported; test asserts the menu item is absent while Rename/Save stay present |
| 13 | BF-20 | full affected suite re-run on this PC after the fixes | `570/570` passed (`/tmp/opencode/qwen-1101-boards-full6.log`: 565 inherited + 5 new), 0 failed; `9to1 Workspace/Boards/app.tests` + `app` + `BlockInsertionPreviewCui.cui` recompiled with 0 errors (1 pre-existing WebRtcVadSharp AnyCPU warning). No framework/CUI source changed, so the CUI runtime suite was not re-run for this milestone |
| 14 | — | **two of my own earlier claims were wrong and are retracted**: (a) "the window-manager close does not close the app"; (b) "typing is broken in the editor" | (a) my `WM_DELETE_WINDOW` was sent with `XSendEvent` and event mask 0 and never reached the client; asking the window manager properly (`_NET_CLOSE_WINDOW` to the root, SubstructureNotify) closed all three test instances cleanly through the app's own save/drain path. (b) the early "no typing" observations were partly mis-aimed clicks; the real, reproducible cause was finding #11. Both are recorded as measured, with the receipts, not quietly dropped |
| 15 | — | input-routing incident (honesty record) | before the harness raised the frame, synthetic clicks aimed at my window were delivered to whichever window was stacked at that root position; that is how a board named "Untitled board (9)" came to exist in `/home/jacob/Documents/9to1/Boards` at 11:22 (it is left in place, untouched, because it may be the owner's) and how a tooltip/hover was triggered in another test instance. The harness now raises the frame and activates the window first, and addresses windows by exact id |

### Milestone 2 — the third independent dimension: Snap (1 Nov 2026, second commit)

The reference identity/mode row carries **three** independent controls (Edit/View, `Locked layout`,
`Snap on`), and `V3-BOARDS-FEA-03`/`V3-BOARDS-ACC-02` require them to stay independent. Boards had no
Snap state at all, so it was implemented rather than faked:

| # | Requirement ID(s) | Action | Result / evidence |
|---|---|---|---|
| 16 | V3-BOARDS-LAY-01, V3-BOARDS-FEA-03, V3-BOARDS-ACC-02 | **added** `HavenRichPage.SnapToGrid` (additive to schema 5, persisted per page) + `HavenRichNotesOps.SetSnapToGrid`, projected both ways through `BoardProjection`, exposed as `ContractSessionAdapter.SetSnapAsync`, and a persistent chrome `SnapButton` beside the layout control | contract suite **127/127** on this PC (`/tmp/opencode/qwen-1101-snap-contract.log`), including three new Snap cases: default off + legacy page without the field loads off, the flag survives a physical reopen, and with snap on the canvas move/resize and `PlaceBlock` commit to the grid while with snap off they keep the free coordinates (and turning it off does not disturb stored geometry) |
| 17 | same | snap applied in the canonical operations, not in the view | `MoveCanvasObject`, `ResizeCanvasObject` and `PlaceBlock` route through `SnapPlacementValue(page, value)` on the page's own `PlacementGridUnits = 8`, the precedent the repository already has in `NotesCanvasOperations.Move/Resize` and in the Boards 8-unit keyboard nudge |
| 18 | V3-BOARDS-FEA-03 independence | `Snap` is gated on the edit permission, not on the layout mode | app suite **571/571** (`/tmp/opencode/qwen-1101-apps-full7.log`): `Snap_is_a_persisted_page_state_that_View_mode_cannot_change` shows View mode cannot flip it, Edit mode can, the layout label and Edit/View state are unchanged by toggling it, and a **reopened session** still reports it on; `SnapButton` is asserted present, effectively visible and measured on the mounted surface |
| 19 | — | **deliberate conservative default, recorded rather than hidden**: Snap ships **off** | the supplied v3.1 text and the corrected v3.2 atlas show a `Snap on` control but never define the snap targets, the step, or whether it starts engaged, and `technical/boards-04.md` (the chapter that would) was not supplied. Turning it on by default silently rewrites committed geometry of existing free-form content — measured: two retained contract tests asserting exact coordinates (`72,128,420,208`) moved when the default was on, and they were **not** edited to accommodate an invented default. The question (default, target set, grid step) is filed as `OPEN-2026-11-01-BOARDS-SNAP-SEMANTICS` in `docs/specification/open-decisions.json`; only that acceptance/semantics decision is held, the state, control and independence matrix are implemented |

### Milestone 3 — the mode matrix, measured against canonical state (1 Nov 2026, third commit `d7bf967b`)

`V3-BOARDS-FEA-03` forbids collapsing Edit/View, Locked and Snap into one read-only toggle and
`V3-BOARDS-ACC-02` requires them verified independently. Until this unit the only mode coverage was two
disconnected checks (a canvas-lock case and a layout-mode case) that never combined the dimensions.

| # | Requirement ID(s) | Action | Result / evidence |
|---|---|---|---|
| 20 | V3-BOARDS-FEA-03, V3-BOARDS-ACC-02, BF-09 | new `BoardsModeMatrixTests` (8 cases) exercising Edit/View × Locked/Unlocked × Snap on/off through the real session and view model, comparing every refused or applied operation with `ContractSessionAdapter.Snapshot` (the canonical document), which is what "verify both agree; never test a disconnected checkbox alone" means | app suite **579/579** (`/tmp/opencode/qwen-1101-appsuite.log`, guard `OK`, 76 s, min available 2 585 MiB). View mode refuses structural, text, spatial, canvas, ink and mode-change requests for **all four** layout × snap pairs and the document is byte-identical afterwards, while navigation, selection and the theme switch still work; Locked layout refuses absolute block placement (the block keeps `Flow`) while page ink is still accepted on that page (boards-ink rule 2); the canvas-object lock is a different dimension (a box moves while the layout is locked, is held once the canvas is locked, and the refusal names the reason); Snap changes only where a move commits (free `137,245` vs a multiple of the 8-unit grid) and never the other two dimensions, and both directions of that independence hold after a save plus a fresh open; an authored orange stroke keeps its colour/width/points across a theme switch, a save and a reopen, and the theme switch writes no document revision (boards-ink rule 5) |
| 21 | — | API guesses corrected against the real signatures rather than by editing the contract | `CommitInkStrokeAsync` requires pressure triples on the 5-argument overload; `PlacementGridUnits` is owned by `HavenRichNotesOps`, not `HavenRichBoardV3`; the view model refuses silently in View mode (`StatusText` names the reason) instead of throwing, so the tests assert canonical state rather than an exception that does not exist. Two compile units (`qwen-1101-mode-matrix-build.log` errors → `build2.log` 0 errors) |

Evidence level for this milestone: unit/integration only. No native render or capture in that commit.

### Milestone 4 — the acceptance pass on the running app, and the Add-menu repair (1 Nov 2026, fourth commit `7450816e`)

Native identity for every row below: standalone `CakeOS.Apps.Boards.App.dll` built Release from
`9to1 Workspace/Boards/app/bin/Release/net10.0/` at HEAD `7450816e`-pre (`d7bf967b` for the first
runs, rebuilt after the menu fix), launcher
`/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from/work/dotnet-sdk-10.0.301/dotnet`,
display X11 `:1`, window 2850×1781 device px = **1440×900 logical at scale 1.979** (Xft.dpi 190),
sandboxed `HOME=/tmp/opencode/qwen-native-20261101/home6` with `XDG_*` inside it and
`HAVEN_DATA_DIR=/tmp/opencode/qwen-native-20261101/profile6`, so nothing was written into the owner's
real `Documents/9to1/Boards`. Dirty tree at the time: only the inherited
`eng/spec-coverage/coverage-summary.json` (left unstaged on purpose). Receipts:
`/tmp/opencode/qwen-native-20261101/step-receipts.jsonl` steps `G01`–`G49`; every behaviour claim was
checked against the **physical `.9to1board` file** with the private reader `board_inspect.py`
(contenthash = volatile fields removed) and, where relevant, against an on-screen frame.

| # | Gate recipe | Observed / action | Evidence |
|---|---|---|---|
| 22 | BF-05 toolbar `Add` = current-page content | clicked the toolbar `+ Add` pill at (2410,178); the popover is an in-surface panel (no separate X window) listing search + DATABASE/CONTAINERS/TEXT/CONTENT groups, i.e. block types only | frames `64-toolbar-add-menu.png`, `74-add-search-filtered.png` (typing `Tab` filters to New database, Tab View, Tabber, Table); the search filter genuinely works |
| 23 | BF-05 non-mutating cancellation, both dismissals | `Escape`, then re-open + **outside click** at (900,1100) | `contenthash` stayed `7c74eee4…` for both; frame `66-after-outside-click.png` shows the popover gone with the focus ring returned to `+ Add` |
| 24 | BF-05 footer `Add` = pages/sections, distinct destination | sidebar footer `+ Add` at (175,1725) opens a **separate override-redirect window** (864×707 at root (0,1038)) whose rows are page/section actions: `New page in this section`, `New section`, plus Write/Canvas attach/create rows and `Link created page to original section…` (the last five disabled) | frame `67-footer-add-menu-popup-3e0001f.png`; Escape cancelled with `contenthash` unchanged; choosing `New page in this section` created `page-151f56eab92a` "Page 2" (a **page**, not page content) and the identity state then read `Saved 13:42:18` |
| 25 | **defect found and fixed** — the Add menu had no visible selection and mis-aligned rows | five `Down` presses moved keyboard focus (the search field lost its outline) and `Return` then really inserted a paragraph into the file, yet **no row ever showed a state**; every row also sat centred under its left-aligned group heading while the app's own destination menu left-aligns. Fixed by driving the toolbar's existing selected treatment + CUI's own selection state from pointer/focus events, clearing it on popup close (Avalonia does not always raise `LostFocus` inside a dismissing popup), pinning the row alignment left through CUI Instance precedence, focusing the row `Return` is about to apply, and leaving Home/End to the search caret | before: `70-crop-insert-popover.png` (nothing highlighted). after: `80-add-selected-crop.png` ("Tab View" visibly chosen, rows left-aligned). New tests `BoardsInsertMenuRowStateTests` (5) fail against the previous source and pass against this one (`qwen-1101-insertmenu-pre.log` 5 failed → `…test4.log` 5 passed); app suite **584/584** (`qwen-1101-appsuite2.log`, guard `OK`, min available 2 650 MiB); native Escape frame `81-add-escaped-crop.png` shows the surface empty |
| 26 | BF-08 ink drawn with a real pointer, then persisted | clicked `Drawing` (2645,178): the adaptive pill swapped to the contextual ink set (`Pen ▾`, `2.5 px ▾`, `Ink colour ▾`, `View ▾`, width dash, ink-settings) while the fixed `+ Add / Drawing / AI` pill stayed put; XTEST button-drag (760,620)→(1500,900) wrote a stroke on the **same native page** as the text (`inkStrokes=1`, 6+ points, width 2.5, `tool=0`); the ink colour palette opened as a separate window (469×1107 at root (770,257)) and a second drag stored `color=#FFE8710A` (orange) beside the first `#FF111111` | frames `82-drawing-toolbar-crop.png`, `84-ink-colour-menu-popup-3e0001b.png`, `94`/`90-landing-grid-annotated.png` (both strokes on screen, black and orange); file `ink` arrays quoted in the receipts; **observed gap**: the palette lists colour *names* with no swatch and the trigger keeps saying `Ink colour ▾` after a choice, so the authored colour is not visible where it is set |
| 27 | BF-08 / BF-11 persistence in a **fresh process** | `_NET_CLOSE_WINDOW` → the app ran its own lifecycle and the launcher recorded `exit=0` (`run7.exit`, `run12.exit`, `run13.exit`); relaunched (run8/run13), the landing listed `Untitled board — 2 pages · 1 sections — 11/1/2026 2:03 PM`, and reopening showed both ink strokes, both page texts (`DIRTYCLOSE7`, `LATEEDIT42`) and the tree | frames `77-fresh-landing-crop.png`, `90-landing-grid-annotated.png`; `contenthash` values `9ebe16eb…` → `6fc9ae7a…` → `25168fce…` recorded before/after each mutation |
| 28 | BF-11 close arms | clean close ×3 (exit 0, `.9to1board` + `.9to1board.bak` rotation, no orphan process); **close immediately after an edit** (`LATEEDIT42` typed, closed with no pause) is durable; autosave is continuous (several revisions per minute: `version=47`, `69`, `77`) so a "dirty" close is rarely a real state | measured limitation, recorded not hidden: `kill -9` 150 ms after typing `WXYZ` lost exactly that tail — the file and the profile draft copy were both still at `version=69`, and after relaunch the Recent tile preview shows `DIRTYCLOSE7CRASHDRAFT7` with **no recovery offer** for the lost tail. The control case (same keys, normal close) persisted `WXYZ` within ~2 s, so the difference is the abrupt stop, not the input. Recovery drafts do exist on disk (`profile6/Boards/profiles/<hash>/drafts/<id>.9to1board` + `.bak` + `.write-lock`) and a `destinations.json`/`recent.json` sit beside them, but they mirror the last autosave, not the live buffer |
| 29 | BF-06 tree moves and undo | drag on a sidebar page row (210,504)→(210,412) changed **nothing** — the tree has no drag-to-nest; the real affordance is the row's right-click `ContextMenu` (`Rename`, `Duplicate`, `Move up`, `Move down`, `Delete`, `Add subpage`, `Nest under previous page`). Using it: `Nest under previous page` set `parentPageId='page-546fcf10b6ba'` and the tree showed the caret + indent + guide; `Ctrl+Z` restored the exact pre-move `contenthash` and `Ctrl+Y` re-applied it | frames `98-page-context-menu-popup-3e00023.png`, `99-after-nest-crop.png`, `100-after-undo-nest-crop.png`; file `contenthash 440445ae…` (flat) ↔ `17ad19bf…` (nested), back to flat as the final state |
| 30 | BF-12 popover/focus lifecycle | insert menu: opened with the caret in search, Home/End now stay there, Down lands on a *visible* row, Return applies that row, Escape closes and returns focus to the opening button (asserted in the new tests and seen natively). CUI menus/popups on this desktop are separate override-redirect windows, and the harness's per-batch focus restore light-dismisses them, so open-and-pick must be one batch — that is why the earlier `Down`-only probes were harness artefacts and are recorded as such rather than as app behaviour | receipts `G12`–`G18`, `G22`–`G23`, `G26`–`G28`, `G45`–`G46` |

### Milestone 5 — reference topology numbers, mode labels and the accent-correct `+` (1 Nov 2026, fifth commit `a9b93148`)

| # | Requirement / carried item | Action | Measured result |
|---|---|---|---|
| 31 | "Sidebar is 256 logical units by default, with 32-unit rows and 16-unit nesting" (v3.1 boards-add rule 2; atlas `.board-sidebar{width:256px}`) | `ColumnSizes="268 1fr"` → `256 1fr`; page rows 34 → 32 and section rows 38 → 32, indent ladder unchanged at 16 | on the rebuilt X11 app the sidebar divider is at device x 505–506 = **255.2 logical** at scale 1.979 (frame `102-topology-editor2.png`), where the inherited build put the same edge at x 529 = 268 logical; headless: `TreeRowsShareOne32UnitPitchAndNestingIndentsByExactly16Units` |
| 32 | "This mock-up defines a 760-unit readable content column" | `MaxWidth="760"` with stretch on `DocumentContentLayer`; the absolute/free-placement layer deliberately stays uncapped | measured at window widths 1200 and 900 logical: the column fills 760 and never exceeds it, and still fits the narrower surface. Centring it (atlas `margin:0 auto`) was tried and **reverted**: measured 147 units shrink-wrapped on an empty page, which would move flow text off the click target. Filed as `OPEN-2026-11-01-BOARDS-READABLE-COLUMN-CENTRING` because Avalonia has no stretch-to-max-then-centre alignment |
| 33 | accent rule vs "green block `+` inside a purple accent" | `plus`/`add`/`insert` resolve through the app accent; `save`/`check` keep the success fill (both in the role switch and in the hard-coded fallback palette) | before/after pixel counts at the same glyph: **104 green-dominant pixels** in the older build (frames `62-landing-again`, `80-add-selected`, `83-ink-drawn`, bbox x 708–733 y 380–405) versus **6 stray anti-aliasing pixels** now. The test states the rule against whatever the app accent currently is, so a green-accent theme is not mis-reported |
| 34 | chrome labels (carried item: "long `Vertical Stacking` label") | `LayoutModeLabel` → `Locked layout` / `Free placement`; accessible names state what the mode constrains so it cannot read as View/Edit (V3-BOARDS-FEA-03) | on screen the mode row reads `Edit | View`, `Locked layout`, `Snap off`, `…` (frame `102-crop-chrome.png`); asserted in `TheLayoutControlNamesItsModeTheWayTheReferenceChromeDoes` |
| 35 | suite state after the shared-surface changes | 5 new `BoardsTopologyTests`; two retained expectations updated to the new specification (34 → 32 pitch; `plus` semantic colour), all previous coverage preserved | Boards app suite **589/589** (`/tmp/opencode/qwen-1101-appsuite7.log`, guard `OK`). Authored ink unaffected: 508 dark px in the black-stroke band and 396 orange-dominant px in the orange band on the rebuilt app, no cross-contamination |

Two items are **held as decisions, not implemented**, with the reason in `docs/specification/open-decisions.json`:
`OPEN-2026-11-01-BOARDS-FOOTER-AVATAR` (the fixed-footer avatar pair — standalone Boards has no profile or
account surface at all: the only identity it resolves is `AuthenticatedResourceActor(ActorId, ProfileId,
AccountId, OrganisationId, AuthenticationRevision)`, two GUIDs and a revision string, and the owner forbids
copying illustrative account state; in embedded Haven the profile control is already Home-owned), and
`OPEN-2026-11-01-BOARDS-READABLE-COLUMN-CENTRING` (above).

Host limitation recorded, not skipped silently: `Haven.Desktop.Tests`, which covers the **embedded Haven**
Boards surface, targets `net10.0-windows10.0.19041.0` and cannot be built or run on this Linux PC
(`error NETSDK1100`; a cross-target build additionally needs a Windows-TFM restore, `NETSDK1005`, which is
not available here — attempted and stopped, `/tmp/opencode/qwen-1101-havendesktop-boards.log`,
`/tmp/opencode/qwen-1101-havendesktop-build.log`). The shared-surface effect on the Haven host therefore
stays **unverified on this machine**; the standalone app is the only Boards host exercised here.

### Milestone 6 — a real crash found by the acceptance pass, then ink history and transforms (1 Nov 2026, commit `c30ef24a`)

| # | Gate recipe / rule | What happened | Evidence |
|---|---|---|---|
| 36 | BF-08 / boards-ink rule 4 | **the running app crashed** on the first pointer press after entering Drawing mode: the launcher recorded `exit=134` (SIGABRT) and `run15.log` carries `System.ArgumentException: NaN is not a valid value for 'MinWidth'` raised from `BoardInkOverlayRenderer.ApplyOverlayExtent` (line 69) through `BoardInkInteractionController.RenderLiveStroke` (line 202) from `OnPointerPressed` (line 140). The code used `double.NaN` as its "no constraint" marker; Avalonia accepts NaN for Height but rejects it for MinWidth | crash preserved in `/tmp/opencode/qwen-native-20261101/run15.log` and `run15.exit` (`exit=134`); fixed to the `0` convention `BlockRenderer` already uses |
| 37 | same | two new `BoardsInkOverlayExtentTests` cases reproduce the identical exception against the previous source and pass against the fix (grow-with-unmeasured-sibling, and shrink-back-below-grown-height, which is the branch the crash hit) | measured control: `…inkfix-pre2.log` 2 failed with `NaN is not a valid value for 'MinWidth'` → `…inkfix-test3.log` 2 passed; whole app suite **591/591** (`qwen-1101-appsuite8.log`) |
| 38 | BF-08 persistence after the fix | at the **same coordinates that crashed**, the rebuilt app survives and the stroke lands in the physical file ("Start here" `inkStrokes` 2 → 3) | receipt `H07`, frame `107-stroke-after-crashfix.png`, process still alive afterwards; instance later closed with `exit=0` (`run16.exit`) |
| 39 | boards-ink rule 4 (one-stroke undo) + BF-19 history | one-stroke undo `3 → 2` with the two surviving strokes byte-identical, redo back to `3`; undo is page-scoped — an accidental stroke on "Page 2" was removed there while the three strokes on "Start here" were untouched | receipts `H08`/`H09`/`H12` with the `ink` arrays printed from the file before and after each key |
| 40 | BF-14 ink transforms; drawing history labels | the drawing menu states its actions honestly (`Zoom in`, `Zoom out`, `Reset view`, `Undo drawing`, `Redo drawing`); the view transform is canonical and persisted per page (`inkView.zoom 1 → 1.25` on the current page, strokes visibly scaled on screen, `Reset view` back to `1` on both pages) | frame `111-ink-view-menu-popup-3e0001f.png`, frame `113-start-here-zoomed.png`, receipts `H14`–`H17` with the `inkView` objects read back from the file |
| 41 | BF-14 block manipulation — **observed, not concluded** | while Drawing is active the ink overlay takes the pointer, so a grip-drag attempt **drew ink instead of reordering** (receipt `H11`: a stroke appeared on "Page 2", block order unchanged; removed again in `H12`). Whether Drawing should keep grip drags available is a specification question, and block-grip reordering in text mode is therefore still **unverified** in this pass | receipts `H10`–`H12`, frame `110-after-block-drag.png` |
| 42 | accent rule (row 33) re-checked visually | on these frames the block-row `+` renders purple in the running app (previously green), and the chrome reads `Locked layout` / `Snap off` with `Edit | View` | frames `109-page2-blocks.png`, `112-after-zoom-in.png`, `102-crop-chrome.png` |



### Milestone 7 — block manipulation, a canonical Data embed, and the identity-shape divergence (1 Nov 2026, commits `bac7398d` and this ledger update)

| # | Gate recipe / rule | Action | Measured result |
|---|---|---|---|
| 43 | BF-14 selection and manipulation | grip-drag on the second block of "Page 2" (device (833,488) → (833,352)) in text mode | the physical file changed order: before `[(block-b366537a9347, order 0, "LATEEDIT42"), (block-5510cc923890, order 1, "")]` → after `[(block-5510cc923890, order 0, ""), (block-b366537a9347, order 1, "LATEEDIT42")]` (receipt `J02`) |
| 44 | BF-19 history for that manipulation | `Ctrl+Z`, then `Ctrl+Y`, then `Ctrl+Z` again | undo returned the exact previous order, redo re-applied it, and a second undo restored the original (receipts `J03`–`J05`, ids and `order` values printed from the file each time). Final fixture state is the original order |
| 45 | BF-15 canonical embed / Data dependency | the Add menu's first row (keyboard-confirmed highlighted in `117`/`119`) created a **database block** carrying a real Data reference `{storeID, workbookID, tableID, viewID}`; it rendered `Table / Board / Timeline` view switching, `Add record`, `Filter`, `Sort +`, field editing, its own `Undo`/`Redo`, and the honest empty state "No records match this view. Filters do not delete records."; the adaptive toolbar and the contextual **Properties** panel switched to `Database ⚙` for the selected block (V31-BOARDS-003/004 behaviour, seen rather than assumed) | frame `116-add-table-selected.png`; the block was removed again by `Ctrl+Z`, leaving `blocks: [(0, block-b366537a…), (0, block-5510cc92…)]` (receipt `J07`) |
| 46 | carried item: block-id shape divergence | **observed on the running app**: the inserted database block was written with id `8086b2b0cc664806bcdbe1c313bdc15b` while its neighbours were `block-b366537a9347` / `block-5510cc923890` — two identity shapes in one document. Root cause: the app-side view entities defaulted to a bare 32-hex Guid while the contract model has always used `sec-`/`page-`/`block-`/`item-`/`cell-`/`canvas-`/`att-` + 12 hex digits, and 23 construction sites rely on that default. Fixed at the source (`RichBoardSection`/`RichBoardPage`/`RichBoardBlock` defaults now match the contract); nothing in Boards parses these ids as GUIDs and retained documents keep their existing strings | commit `bac7398d`; new `BoardsIdentityShapeTests` (2) assert both sides of the boundary and a real fallback-path insertion; whole app suite **593/593** (`/tmp/opencode/qwen-1101-identity-appsuite.log`) |
| 47 | BF-12 / keyboard access, re-checked on the running app | a single `Down` in the Add menu now shows the chosen row | frames `117-search-able-highlight.png` and `119-table-row-highlighted.png`: "New database" carries the selection fill; this is the Milestone 4 fix working on the live build |
| 48 | input observations **not concluded** | (i) in the run18 instance, typing into the insert-menu search field did not register in two attempts (the field kept its placeholder and the list stayed unfiltered, receipts `K02`/`K03`), although the identical interaction was captured working on an earlier instance of the same build family (frame `74-add-search-filtered.png`, receipt `G16`); (ii) a twelve-step `Down` walk ended with the popover closed and nothing applied (`119` → `120`, file unchanged). Neither is recorded as a product defect: this run's synthetic key delivery to that window is the prime suspect and the same paths pass under the headless app suite. BF-13 (tables) and the deep keyboard walk therefore stay **NOT RUN / unverified** natively | receipts `K01`–`K03`, `step-receipts.jsonl` |
| 49 | close behaviour, cause not established | in this run one `_NET_CLOSE_WINDOW` removed a Boards window while the process survived with a *different* window id; a second close request terminated it with `exit=0` (`run18.exit`). Earlier instances (`run7`–`run16`) each exited on the first close request. Recorded as an observation to re-test; not claimed as a defect, and no process or window was left on the owner's desktop | receipts at 15:35:59 and 15:36:40, `run18.exit` |

Gate state after Milestone 7: **BF-14 block manipulation and BF-19 block/page/ink history are now exercised with
file-level evidence**; BF-15 has its canonical Data-embed half exercised (Chat / Scoped Chat page types and the
disabled Write/Canvas rows remain open); BF-13 and BF-17's deeper keyboard behaviour remain NOT RUN; BF-01,
BF-04, BF-07, BF-10, BF-16, BF-18 (chrome half) and BF-20 are unchanged. The gate as a whole still has not
passed, so the theme pass stays gated.

### Milestone 8 — a second undo system, and a retraction of my own click claim (1 Nov 2026, commit `3dbf512c`)

| # | Requirement / rule | Action | Measured result |
|---|---|---|---|
| 50 | "the canonical board history is the only undo owner" (the rule BlockRenderer already implements) | **defect measured on the running app**: with the page-title input focused, `Ctrl+Z` removed typed characters instead of a board revision — the stored title went `Start here BF07OK` → `Start here BF07` over two presses with no canonical revision between them. Headless the cause is one number: `PageTitleBox.UndoLimit == 10` (Avalonia's default), i.e. a private character-level history beside the canonical snapshot stack | receipts `L05`; new test `ThePageTitleInputIsNotASecondUndoSystemBesideTheCanonicalBoardHistory` fails against the previous source (`Expected: 0, Actual: 10`, `/tmp/opencode/qwen-1101-undolimit-pre.log`) and passes after (`…undolimit-test2.log`) |
| 51 | same | fixed at the shared owner `BoardsTextUndoOwner` and applied to every document text surface: the mount walk in `Attach` (which had to switch to a logical-descendant walk — see row 53), the board-name editor when a rename opens, the tree rename box, the tab-pane caption and the two style-editor inputs. The two query fields (sidebar filter, insert-menu search) keep their own undo on purpose, because they hold a query rather than board content | app suite **596/596** (`/tmp/opencode/qwen-1101-appsuite10.log`, guard OK). No contract source changed, so the contract suite is unaffected |
| 52 | my own earlier claim, **retracted** | the run19 probes (receipts `L08`–`L11`) looked like "clicks inside the page do nothing and focus stays in the page title". They are not usable evidence: mounting the real composition headlessly (BlocksHost filled by `BlockRenderer`, ink layer owned by `BoardInkInteractionController`, landing dismissed) shows a click on paragraph text **does** focus that block, and text typed afterwards reaches the block while the title is unchanged. Worse, during that same instance the fixture gained **Page 3 and Page 4 plus an ink stroke** that no step in my receipt sequence accounts for, so nothing measured in run19 can be attributed | new `BoardsBlockClickFocusTests` cases 1–2 pass on the pre-fix source too (`…undolimit-pre.log`), i.e. they are coverage rather than a fix; run19 is recorded as unattributed synthetic input. **BF-07 stays NOT RUN natively** and must be re-probed with a fresh disposable profile and per-step window-id verification |
| 53 | implementation gap found while fixing row 51 | `BoardsViewModel.FindControls<T>` only descends **Panel** children, so any control inside a border/decorator container (the CUI-authored title inputs among them) is invisible to the walker that wires `TextChanged`/`GotFocus` and to any rule applied through it. That is why the undo rule never reached the titles, and why `BoardsTextAndKeyboardOwner` already needed a logical-descendant fallback to find the page title at all | observed in this checkout at `a2f820bf`; worked around for the undo rule by walking `GetLogicalDescendants()` in `Attach`. The wider consequence (other rules silently skipping those surfaces) is **not** fixed here and is carried below |
| 54 | headless fixture honesty | a mounted Boards CUI surface still carries the landing overlay until a board is opened, so a click test that does not dismiss it measures the landing: the topmost visual at the block's own point was `LandingHost`'s `ScrollContentPresenter` and focus was `null` | fixed in the fixture with an explicit `SetLanding(false)` and a comment; the failure message now prints the visual chain under the click point, which is what exposed this |

An **independent reviewer session** added `docs/development/boards-usability-review-2026-11-01.md`
(SHA-256 `be340b32c273a85a2276a948eed80cd7c05632f2f4a4fd26878459517737fd1f`, written 16:56 against
`a2f820bf`). It is a limited exploratory/screenshot review, **not** acceptance, and its six prioritised
findings (unsaved-edit loss on abrupt stop, undiscoverable tree reorganisation, ink colour not visible at
the point of selection, disabled Write/Canvas page-type actions with no explanation, two identically labelled
`+ Add` controls, and the drawing-mode grip ambiguity of row 41) are consistent with this log. It is left
uncommitted here because its author owns it. That report also recorded being **BLOCKED** from a fresh
interactive test by my still-running instance (PID 3958725, window `0x03e0000f`): I closed it through
`_NET_CLOSE_WINDOW` at 17:02:38 and the launcher recorded `exit=0` (`run19.exit`), so that particular blocker
is cleared, while its coordinator/candidate-admission blocker is not mine to resolve.

### Milestone 9 — the canonical structured failure identities, part 1 (1 Nov 2026, commit `e49c138a`)

Authority: the canonical v2 register's Boards **"Failure behaviour"** clause — "Boards MUST return
structured failures for BoardNotFound, PageNotFound, BlockNotFound, UnsupportedPageType,
UnsupportedBlockType, InvalidLayoutTransition, RevisionConflict, PermissionDenied, SourceIngestionFailed,
ScopedChatExternalAccessDenied, DependencyUnavailable and SharedEngineIncompatible, plus the global error
contract" (`docs/specification/9to1-development-specification-v2.md:9679`, repeated verbatim in the owner's
27 October expanded package at line 7219 and in the retained baseline export) — together with the global
actions clause "Errors identify recoverability and the affected operation without leaking forbidden source
details" (line 1007). The historical register row for that clause
(`docs/handoff-20261014/registers/source-clauses.json`, `M-06395-06395`) is still `NOT_RUN`, and it stays
NOT_RUN: this unit produces six of the twelve codes, not the contract as a whole.

| # | Requirement | Action | Measured result |
|---|---|---|---|
| 55 | the twelve canonical codes | vocabulary in one place (`contract/BoardsFailures.cs`: `BoardsFailureCode` + `BoardsFailureException` carrying code, canonical operation name, stable target id, retryability and retained state; derives from `InvalidOperationException` so existing callers keep working). Converted the sites whose meaning is unambiguous: the page/block/section lookups, the parent-page lookups, `MoveBlock`, `MoveSection`, the three Native-page layout/placement guards, the locked-placement refusal (`InvalidLayoutTransition`, retryable, naming free placement as the retry) and the unsupported block kind in validation | codes the contract now actually produces: `PageNotFound`, `BlockNotFound`, `MissingSourceSection` (global equivalent), `UnsupportedPageType`, `UnsupportedBlockType`, `InvalidLayoutTransition`. Before this the same conditions were 16 bare `KeyNotFoundException` throws plus prose `InvalidOperationException` (179 of those in the contract layer) |
| 56 | the app must not drop the identity | the layout, placement and vertical-flow catch sites render through `FailureStatusText` as `activity: <plain reason> (Code, retryable)` — the user reads a sentence first, the code is present and assertable | app case asserts `BlockNotFound` appears in `StatusText` for a missing block and that the canonical document is byte-identical afterwards |
| 57 | regression coverage preserved, not weakened | two retained expectations were **updated to the structured identity** rather than deleted: the locked-placement case (`HavenRichBoardPlacementTests`) now asserts code + operation + target + retryability + the word "Unlocked" + assignability to `InvalidOperationException`; the cross-section parent-page case (`HavenRichBoardV3Tests`) now asserts `PageNotFound` with the parent id as the target. Both are strictly stronger than the bare-type assertion they replaced | contract suite **134/134** (`/tmp/opencode/qwen-1101-failcodes-contract5.log`, was 127/127, seven new cases); app suite **599/599** (`/tmp/opencode/qwen-1101-failcodes-final-app.log`, was 596/596, three new cases). Control: with the contract conversion reverted and the new cases kept, **all seven** identity cases fail (`/tmp/opencode/qwen-1101-failcodes-pre.log`) |
| 58 | **not claimed done** | seven codes still have no producer, each belonging at a layer this unit did not touch: `BoardNotFound` and `RevisionConflict` at the store / write-lease boundary; `PermissionDenied` at the two read-only guards (`RichBoardSession.cs:163/386`, converting them would rewrite three existing exact-type `SaveAsync` expectations, so it is deliberately its own unit); `SourceIngestionFailed` at attachment ingestion; `ScopedChatExternalAccessDenied` at the unbuilt Scoped Chat page type; `DependencyUnavailable` at the Data/Files embed; `SharedEngineIncompatible` at the unbuilt shared-engine handshake. The shared lookup helpers also still name themselves (`Boards.Page.Require`) rather than the public operation that asked — stated in the new test comments instead of glossed over | remaining work listed, gate and register states unchanged |



### Milestone 10 — the keyboard could not undo, and one undo was one character (1 Nov 2026, commits `f1fe3588`, `ba6a7bd4`, `ceec047c`, `ec351be1`)

Native identity for this milestone (standalone `CakeOS.Apps.Boards.App.dll`, launcher SDK 10.0.301, sandbox under
`/tmp/opencode/qwen-native-20261101/`, every instance closed through `_NET_CLOSE_WINDOW`):

| instance | binary SHA-256 (first 12) | built from | sandboxes | frames / receipts |
|---|---|---|---|---|
| run20 | `9037a16ad76b` | `222610f6` | `home7`/`profile7` | 127-140, receipts M01-M19 |
| run21 | `e018c42fe4a2` | `f1fe3588` | `home7`/`profile7` (reopen) | 151-159, receipts N01-N10 |
| run22 | `a7bc409ba650` | `ba6a7bd4` | `home8`/`profile8` | 160-162, receipts P01-P03 |
| run23 | `67eea0ab5d03` | `ceec047c` | `home9`/`profile9` | 163-165, receipts Q01-Q03 |
| run24 | `abb8686b650c` | `ceec047c` + temporary diagnostics | `home9`/`profile9` | 166-167, receipts Q10-Q11, `merges-run24.log` |
| run25 | `abb8686b650c` | as run24 | `home9`/`profile9` | 168, receipt Q21, `merges.log` |
| run26 | `09bcf761b404` | `ec351be1` | `home10`/`profile10` (fresh) | 169-172, receipts R01-R04 |

| # | Requirement / rule | Action | Measured result |
|---|---|---|---|
| 59 | BF-02 create and save without Files, an account or a Boards folder | re-proved on the rebuilt binaries in four **fresh** sandboxes | each produced `Untitled board.9to1board` in the sandbox's own `Documents/9to1/Boards` with canonical ids (`sec-00645271d89a`/`page-b7126a99449b`/`block-47b0537df78b`, `sec-5901e73be7c4`…, `sec-28ee646b5bbd`…, `sec-…` in home10 with `page-959c72c95057`/`block-136b62500d6b`) and `version=1`; frames 132, 160, 163, 169 — the identity fix from `bac7398d` is now verified on screen, not just in tests |
| 60 | BF-11 close and reopen without loss (clean-close half) | run20 closed gracefully (`exit=0`), then run21 was a **different process** on the same sandbox and opened the board from the landing's Recent boards card | the reopened page showed exactly the four blocks and their texts (`AAASOFT1\nSELREP`, `REPLACED\nXX`, `SPLITME`, `BF07SECOND`) and the physical file's `contenthash df50673e542103137b003a27ad829193e51b5954b230209221edec9d3d584781` was identical before and after opening; frame 152. Dirty-close, refused close and save-failure recovery paths are still NOT RUN |
| 61 | my own harness, evidence honesty | `step.py --type` was a **single-value** option, so a step asking for two texts silently sent only the last one | this - not the product - produced the "BF07FIRST vanished" observation recorded earlier: the receipt for M07 lists only `Return` and `BF07SECOND`, so the nine characters were never sent. `--type` is now repeatable and records each text run in order. The M07 result is withdrawn |
| 62 | coordinate discipline (third coordinate error of the day) | measure control geometry from PNG pixels before claiming what a click did | measured glyph boxes in frame 155: Undo x 376-405 y 47-74 (centre 391,59), Redo x 452-481 y 47-74 (centre 465,58). My earlier run20 clicks at (461,68)/(477,72) and run21's first at (466,60) were on **Redo** with an empty redo stack, so the earlier "toolbar Undo did nothing" is withdrawn; at (391,59) the button works (receipts N09, N10). Landing cards measured: title row y 724-750 x 632-892, clicked (762,737) opened the board |
| 63 | "the canonical board history is the only undo owner" - the keyboard half | **defect, native then headless**: Ctrl+Z inside a focused block reached *nothing*. The text control consumes Ctrl+Z/Ctrl+Y for its own stack, which `3dbf512c` had just emptied (`UndoLimit = 0`), and the shared owner only mapped the shortcut on the bubbling route the control had already stopped. Native run20: four Ctrl+Z presses changed neither the page (frames 141, 149) nor the file (still `richNotes.version 130`, same four blocks) while CanUndo was true. Headless control: `CtrlZInsideAFocusedBlockDrivesTheCanonicalHistory` fails on the pre-fix source with `CanUndo=True CanRedo=False; live editor="Q"` (`/tmp/opencode/qwen-1101-undo-keyboard-pre.log`) | fixed in `f1fe3588`: `BoardsTextAndKeyboardOwner` claims Ctrl+Z/Ctrl+Y on the **tunneling** route of the CUI surface and dispatches the canonical Undo/Redo; the bubbling mapping stays for focus outside the surface; the sidebar filter and insert-menu search are excluded on purpose (they hold a query, not content) and pinned by `CtrlZInTheSidebarFilterLeavesTheBoardHistoryAlone`. Native run21 then showed the keys working (receipts N04-N06) |
| 64 | BF-19 history: one undo boundary must be one **edit**, not one character | **defect, measured natively** in three successive runs: after the keyboard fix, Ctrl+Z stepped the file back one character per press - run21 receipts N04/N05, frames 154-155 (`SPLITMEZZTOP` → `SPLITMEZZTO` → `SPLITMEZZT` → `SPLITMEZZ`, `richNotes.version` 140 → 138 → 136 → 134), and the toolbar button at its measured glyph did the same per click (N09/N10). Root cause chain, corrected twice before it was right: (a) `ba6a7bd4` grouped only merges that differed by one character - re-tested natively in run22 (frame 161-162, version 1 → 9 for `GRAN`, one undo → `GRA`); (b) `ceec047c` keyed the group on the changed *surface* instead of the size - re-tested natively in run23, still one letter per press; (c) instrumented runs 24-25 (temporary merge/history dumps, `merges-run24.log`, `merges.log`) showed both rules were already behaving correctly - each merge changed one surface by one character and three consecutive pushes all carried `group=text-edit:block-df471a94dd00` - so the failure was the **clock**: `HistoryCoalescingWindow.Ticks * (Stopwatch.Frequency / 1e9)` mixed TimeSpan ticks (100 ns) with a nanosecond counter, turning 1500 ms into about 15 ms, shorter than the gap between keystrokes | `ec351be1` converts the window to the timestamp's unit (`TotalSeconds * Stopwatch.Frequency`). New case `RealisticKeystrokeSpacingStillGroupsAsOneStep` types 60 ms apart; control: with the previous factor restored and nothing else changed it fails while the other five pass (`/tmp/opencode/qwen-1101-units-pre.log` vs `…-units-post.log`) - the in-process cases could never see this because they are faster than the mis-scaled window. **Native proof, run26** (fresh `home10`/`profile10`, binary `09bcf761…`): typing `GRAN` took the file 1 → 9, **one** Ctrl+Z took it 9 → **1** with the block empty on screen and in the file (receipts R02, R03; frame 171 shows the empty paragraph with its placeholder), and one Ctrl+Y brought the whole run back (R04, version 9) |
| 65 | suites after this milestone | both Boards suites re-measured on this PC with SDK 10.0.301 under the low-memory guard | contract **140/140** (`/tmp/opencode/qwen-1101-units-contract-full.log`, was 127 at the start of the day); app **606/606** (`/tmp/opencode/qwen-1101-appsuite15.log`, was 591). New files this milestone: `app.tests/BoardsBlockClickFocusTests.cs` (extended), `app.tests/BoardsTypingMergeGroupingTests.cs`, `tests/BoardsHistoryGranularityTests.cs` |
| 66 | BF-07 typing and block boundaries - now partly native | driven on the running app with per-step file checks (receipts M11-M30, frames 137-147): clicking paragraph text focuses that block and typed text lands in it; `End` appends; `Return` splits into a new canonical block id (`block-5a48d466df9f` appeared between the two); `Shift+Return` keeps **one** block id and writes a real `\n` inside it (`AAASOFT1\nSOFT2M9TAIL`); a click-selected range replaces exactly (`M9NEXT` → `REPLACED`, and only the second line of a two-line block: `REPLACED\nTWO` → `REPLACED\nXX`); a click in the grip/`+` column or in the row padding selects no block (M22) - that is where several of my earlier "nothing happens" readings came from; a click left of the text places the caret mid-run (`AAAM9TAIL` + typing gave `AAASOFT1M9TAIL`) | still NOT RUN for BF-07: IME composition, applying formatting marks to a selection, Backspace/Delete merge semantics, Unicode input, and undo-after-split preserving ids and selection. BF-07 is therefore **partially** exercised, not closed |
| 67 | gate state | the native gate rows gained evidence, none gained acceptance | BF-02, BF-11 (clean-close half), BF-19 (history granularity and the keyboard route) and BF-07 (typing/boundaries half) are now exercised on the running app; BF-01, BF-04, BF-10, BF-13 (tables), BF-16, BF-17's deep keyboard walk, BF-18's chrome half, BF-19's search row and BF-20 remain NOT RUN. The gate as a whole has still not passed, so the shared theme pass stays gated |

| 68 | BF-05 / BF-07 insert route from the keyboard | the slash affordance was probed natively with per-step file checks | typing "/" in an **empty** block opens the searchable insertion menu and the trigger character never reaches the document (`block-9df9226d267a` stayed empty, receipt S03, frame 175); typing the filter **within the same step** lands in the search field, which shows "table" with the "ADD WHERE" rows beneath it (receipt S05, frame 177, file unchanged). My earlier claim that typed characters escape into the block (receipt S04, frame 176, block "TABLE") is withdrawn: every `step.py` invocation re-activates the window first, which takes focus away from an open popup. That is the fourth harness-caused observation today (rows 52, 61, 62, 68) and the rule now followed is: drive popup interactions inside one invocation, or with `--no-focus` |
| 69 | BF-13 compact tables | **open question, recorded not passed** | the destination step never reached a table natively: clicking the measured "Replace Current Block" row (845,846 in frame 177's popup) closed the menu and inserted nothing (receipt S06, frame 178); `Down` then `Return` left the menu showing the same three "ADD WHERE" rows with no visible selection and no insertion (receipt S07, frame 179, file unchanged at `richNotes.version 23`). A headless attempt to pin this is inconclusive - after filtering to "table", the destination rows are not present in the mounted window tree, so the case cannot drive the step. It is committed **skipped with a written reason** (`ChoosingAnInsertDestinationKeepsTheMenuOpenAndOffersTheFilteredType`, commit `5c0ba8fe`) rather than left red or deleted, and the reason names what to try next (drive the destination surface through its own CUI actions; re-check the native click point against the popup bounds). BF-13 remains **NOT RUN** |
| 70 | coverage for the promised slash route | `TypingSlashInABlockHandsTheKeyboardToTheInsertMenuSearch` (commit `5c0ba8fe`) | passes on the current source: the menu opens from "/", the trigger never becomes content, the search field holds keyboard focus, and subsequent typing filters the menu instead of writing into the board. Coverage, not a fix |
| 71 | suites at this point | re-measured on this PC under the low-memory guard | app **607 passed + 1 skipped of 608** (`/tmp/opencode/qwen-1101-appsuite16.log`); contract **140/140**. run26 closed through `_NET_CLOSE_WINDOW` at 20:18:37 with `exit=0`; no test instance is running on the owner's desktop |

| 72 | BF-13 compact tables | **proved end to end headlessly** (commit `59c7fc53`): "/" in an empty block opens the searchable insertion menu; the trigger character never becomes content; filtering to "table" and choosing the destination row keeps the menu open and offers the filtered type; choosing it creates a real table block and honours the chosen position; then clicking the first cell editor and typing lands in the canonical cells (`TableCells["0,0"] == "CE11"`) with the neighbour left empty. Cases `TheSlashRouteReachesARealTableThroughTheDestinationStep` and `ATypedTableCellReachesTheCanonicalTable`; app suite **609/609** with no skips (`/tmp/opencode/qwen-1101-appsuite17.log`) | the two probe errors that hid this are named in the case comments: typing "/table" in one go never fires the trigger (it needs the block's whole text to be exactly "/"), and the rows are named "Insert Table" by the menu's own convention, not "Table". BF-13 is **not** native-verified and not accepted |
| 73 | BF-05 non-mutating open/cancel, native | run27 (fresh `home11`/`profile11`, binary `09bcf761…`): "/" then "table" typed through the real X11 build | the menu opened and accepted the search text while the board stayed untouched: `richNotes.version` still 1 and the single block still empty (receipts T02, T04; frames 181, 184), and a pixel diff of the menu area between the two frames shows zero change |
| 74 | BF-13 / BF-17 native interaction, **new open investigation** | the same native run could not complete the insertion: a pointer click on the measured destination row centre (811,678 in frame 181, inside the row's own text band 668-688, x 695-927) closed the menu and inserted nothing (receipt T03, frame 182), and `Down` produced no row selection at all - the menu area is pixel-identical to the unpressed state (receipt T04, frame 184) - while the identical sequence works headlessly | Not called a defect yet, and not called a probe error either: the difference is real and reproduced twice natively (also run26 receipts S06/S07, frames 178-179). Leading hypothesis, **unverified**: the destination rows are authored as `<Button Type="Menu Item">` and materialise as `CuiMenuItem`, a `MenuItem` placed inside a plain `StackPanel`/`ScrollViewer` popup rather than inside a `Menu`, so native keyboard navigation and possibly native pointer activation are inert; the headless mount drives them through a different route. Next step: read the menu-item activation contract in the fork, then either author the rows inside a real menu host or drive them through their typed actions, and re-run the same probe with per-keystroke frames. BF-13 stays **NOT RUN natively**, and BF-17's keyboard row also stays NOT RUN |

| 75 | CUI recompile gate, **measured mechanism** | The 1 Nov design edits appeared not to build: `CakeOS.Apps.Boards.App.dll` stayed `09bcf761…` and both build logs looked empty. Building `CakeOS.Apps.Boards.Embedding.csproj` directly re-ran `CompileCuiSources` (the target has no condition: `framework/CUI/Compiler/buildTransitive/CakeOS.Cui.targets:60`), producing `obj/CakeOS.Apps.Boards.Embedding/Release/net10.0/Cui/compilation.v1.cuic.json` at 14:18 and a new `…Embedding.dll`, after which the app build produced `f8ee655b…` | So CUI edits **do** recompile; what the app-only build skipped was the *Embedding* project, and `dotnet build App.csproj` did not notice the changed `.cui` inputs. Recorded because the earlier "CUI edits do not trigger a rebuild" note in the handoff was wrong as stated: the correct rule is **build the Embedding project (or the pair) after editing a `.cui`**. Verified the authored values reached the artifact: the compiled bundle reads `AdaptiveFixedActions … CellGap 2` from `BoardsAdaptiveToolbar.cui` line 390 |
| 76 | Toolbar control scale, **on-screen capture before and after** | Same surface, same window geometry, two builds: frame `201-u02-editor.png` (binary `09bcf761…`, 1 Nov) versus frame `203-u04-editor-after.png` (binary `f8ee655b…`, 2 Nov, run30/`home12`). Connected-component measurement of the exact chip fill (232,233,237), ±5: before, the four formatting controls were 62×66 device px and `+ Add`/`Drawing` were 124×70 and 208×70 device px, with 10 device px between the fixed pills; after, all six measured controls are **62×66** (31×33 logical) and every gap inside the cluster is **6 device px (3 logical)** | `BoardsAdaptiveToolbar.cui` `AdaptiveFixedActions`: `CellGap 4→2`, `Height 36→32`, `CornerRadius 12→8` for `AdaptiveAdd`, `AdaptiveDrawing`, `AdaptiveAI`, matching the `AdaptivePill` controls in the same row. One row, one optical scale, even rhythm. Not accepted: the numbers are from captures of the running app, and the toolbar's *behaviour* was not re-tested natively in this pass |
| 77 | Landing action row, **on-screen capture** | Frames `200-u01-landing.png` (before, run28 binary) and `202-u03-landing-after.png` (after, `f8ee655b…`), fresh `profile12`/`profile13`: the row band (device y 376-424) changed and every band below it shifted down by ~20 device px, i.e. the content re-flowed. A vertical colour scan through the first pill gives y 355-420 (66 device px) before and y 355-424 (69 device px) after, and the crop shows the two pills and the search field on one shared height with text centred | `Boards.cui` `LandingHost`: `LandingCreateBoard`/`LandingOpenBoard` `Height="36" VerticalAlignment="Center"`, `LandingSearchBox` `Height="36" MinHeight="36" VerticalContentAlignment="Center"`. The three controls in one row previously had three different rendered heights. Design judgement only; no requirement ID is closed by it |
| 78 | Block gutter and toolbar hit targets, **measured headlessly, suspicion disproved** | The 1 Nov native measurement made the `+` glyph look tiny: accent bbox x 883-908 y 380-405 device = a 13-logical glyph. A headless size dump of every visible interactive control on the mounted surface gives: `Insert` (block gutter) **32×32**, `Open block actions for paragraph` (grip) **32×32**, `More board actions` 32×34, sidebar `Search this Board` 235×38, `New page` 239×34, `Add page or section` 168×36, `Page title` 760×35, `Paragraph editor` 760×34 | Nothing on those surfaces is undersized: the small number was the *glyph*, drawn inside an adequate 32×32 target. So no hit-target fix was made, and the earlier "tiny target" impression is withdrawn. The dump probe was temporary and is **not in the tree**; the numbers here are the record |
| 79 | Insert menu pointer activation, **measured native defect, mechanism still open** | run33/run34 (diagnostic binaries `cda0ec7b…`/`510c8b3a…`, fresh `home16`/`home17`) with each interaction driven inside one harness invocation. (a) Clicking a *drawn* destination row at (700,674) or (811,674) leaves the menu open and changes nothing: no `ChooseDestination` dispatch in any run (grep count 0 across four sequences), and the on-disk board stays `richNotes.version 1` with one empty block (frames 217, 220, 222; doc reads). (b) Clicking a *type* row (`Header` at (2197,697)) in the toolbar-anchored menu does the same (frame 220 bands identical to 218, document unchanged). (c) A window-level tunnel press probe reports, for the press aimed at the row: `point=353.7, 340.5` hitting `ScrollContentPresenter < Grid < ScrollViewer < CuiContainerControl[EditorScroll] < … < CuiContainerControl[PageCard] < CuiContainerControl[EditorHost]` — the **page behind** the drawn menu, not the menu. (d) Pointer *motion* at that same point does reach the row: its CUI action fires as `command=Preview … input=Pointer placement=Below enter=True`. (e) `Enter` after `/` + `table` does not complete the insertion either (doc unchanged, frame 223) | Stated as measured, not as diagnosed: pointer events for the insert popup are routed to the page beneath it, while hover actions route to the popup, and no keyboard fallback completes the route. On the same surface the headless mount drives the identical sequence to a real table (case `TheSlashRouteReachesARealTableThroughTheDestinationStep`, app suite 609/609 on `f8ee655b…`). So the mouse user cannot finish an insertion at all in the running X11 build, and BF-13/BF-17 stay **NOT RUN natively**. Mechanism is deliberately left open: the decisive next instrument is the same press probe attached to the popup's own visual root (`popup.GetVisualRoot()`), plus the popup's post-arrangement bounds, because the destination root logged `bounds=0,0 isAttached=False` at creation time and the X11 enumeration showed only a tooltip window (`0x1a0001b` "Paragraph — select to format", 560×190 at (2433,409)) while the menu is visible in the client-window grab. Also recorded: the insert menu's *type list* is built in C# (`ToolbarBuilder.cs:61-171`) while the destination rows are CUI (`InsertDestinationCui.cui`), i.e. two authoring paths in one surface — a conformance question to raise separately, and both are inert to native clicks |
| 80 | Diagnostic hygiene | Four temporary diagnostics (a staleness reporter in `BoardsViewModel.InsertionPreview.cs`, dispatch tracing in `InsertMenuContext.cs`, a destination-root press probe, a window-level press probe) plus one temporary test were added, used, and **reverted**: `git status` after the pass shows only the two `.cui` design edits, the inherited `eng/spec-coverage/coverage-summary.json` and the other session's untracked review document. The app assembly returned to the byte-identical clean hash `f8ee655b…` after the revert, which is the check that no diagnostic survived into the committed source. Suites on that source: app **609/609** (`/tmp/opencode/qwen-1102-appsuite18.log`), contract **140/140** (`/tmp/opencode/qwen-1102-contract11.log`) | Scratch run35 ended abnormally with no `.exit` file written while a suite ran alongside it (memory floor observed 1606 MiB during that suite): its board file `Untitled board.9to1board` and its profile draft `513caf28-….9to1board` **survived**, together with a stale `….9to1board.write-lock`. That is one observation of crash-durability, not a test: recovery from a stale write lock is **NOT RUN** |

| 81 | Toolbar typography cluster, **on-screen capture before and after** | The paragraph-style dropdown drew its own selected label cut off: frame `225-z02-typed.png` zoom shows "Paragraph" ending in a partial glyph at the control edge (dark glyph run x 583-716 device inside the 124-logical control). `AdaptiveStyle` `Width 124→150`: frame `231-z08-editor.png` shows the word complete with the drop marker clear of it. The size field read `−  14   px  +` with the number stranded left in a wide field (frames 225 and `225-zoom-size.png`); `AdaptiveSize` `Width 43→34`, `Padding 6 0 → 2 0` moved the number next to the unit - measured number-to-unit gap **38→30 device px (19→15 logical)**. Widening the style control shifted the alignment group right by 33 device px (x 1404→1437) and the right-hand cluster stayed at x 2359-2696, all controls still 66 device tall with 7-device gaps: no overflow at the 1440-logical window width | App suite **609/609** on this source (`/tmp/opencode/qwen-1102-appsuite19.log`), Embedding artifact `34ea130a…`. Design judgement: no requirement ID is closed. A centred-content attempt on `AdaptiveSize` was tried and **rejected by the CUI compiler** - `CUIC013: Property 'HorizontalContentAlignment' is not allowed on 'Input'` (`/tmp/opencode/qwen-1102-emb4.log`) - which is a CUI capability gap recorded here, not something to work around in code-behind |
| 82 | Landing recent-board card, **on-screen capture before and after** | Frame `227-z04-landing.png` (Embedding `f8ee655b` era): the card authored `Background="{Resource CuiPanelBrush}"` had **no visible boundary** because Panel equals the page colour on this surface, so the icon, title, preview and date read as four unrelated elements; the folder icon was cross-aligned to the card centre (icon bbox x 830-865, card centre 851 device) while every text line was left-aligned (x 574). Frame `230-z07-landing-after.png` (Embedding `049d6af7`): the card now has a measured fill region x 581-1097 × y 420-779 device (261×182 logical) using `CuiFloatingSurfaceMaterial`, and the icon leads at x 608-643. The title button also carried its own filled chrome inside the card (a second grey blob, measured pill 161×37 logical at x 603-920 y 706-779); `Background="Transparent" BorderWidth="0" Padding="0"` removes the box while keeping the action, the accessible name "Open recent board" and the 18 SemiBold type | Icon still draws 18×13 logical inside the authored 24×24 box (bbox unchanged apart from position) - that is an icon-geometry question, recorded as **not fixed**, mechanism not investigated. Card surface choice is a design judgement; the shared theme pass may revisit it, and it is exactly the kind of decision that stays out of `CuiPanelBrush` until Boards is through its gate |
| 83 | Landing recent-page rows, **on-screen capture before and after** | The full-width row bar began at x 571 device but its content began at **x 1383** (frames 229 and 230, i.e. an indent of 410 logical of empty grey). Adding `HorizontalAlignment="Left"` to the inner `Container` changed nothing at all (frame 230 measured identically), because a Button places its content by its **own** content alignment; moving it to the button as `HorizontalContentAlignment="Left"` worked: frame `232-z09-landing.png` measures text starting at x 600, an indent of 29 device = **15 logical**, matching the authored 12-logical padding | Two authored facts worth keeping: CUI accepts `HorizontalContentAlignment` on `Button` but refuses it on `Input` (row 81), and alignment on the child of a Button is not the lever. Row height (106 logical) and the heavy row fill were **not** changed: no measurement says they are wrong |
| 84 | Native Enter, **new measured defect, not yet diagnosed** | run36/run37 on `f8ee655b` (fresh `profile19`): clicking the empty page area below the last block appended a block (good), then typing reached it - the physical file went to `richNotes.version 78` with `block-b87e700f34ca` holding `Meeting notesAction: book the roomDone` - but **every `Return` press left that text in one block**: three typed runs concatenated, no split, no newline in `plainText` (unlike the 1 Nov soft-break evidence, which stored `AAASOFT1\nSOFT2M9TAIL`). The harness key is the same one that produced the 1 Nov Enter-split receipts (`Return` appears 24 times in `step-receipts.jsonl`, including `I-enter-split-probe` and `M6-enter-split-then-type`), and headless Enter coverage exists (`BoardsEnterBehaviourTests`, part of the green 609) - so this is either an X11 input-routing difference or a native-only regression, and neither is established | Kept as measured, with the two candidate explanations named and neither chosen. It sits next to row 79 in seriousness: keyboard Enter for block splitting did not work on the running build during this pass, while the same suite passes headlessly. BF-07's native half must be re-run before it is called verified |
| 85 | Probe hygiene, learned the hard way again | Two probe errors in this pass, both mine: (1) the y coordinates measured while the landing overlay is mounted stop being valid the moment a board opens - the page content moves up ~75 device px (frame 201 with the landing still mounted versus frame 224 without it), so a click at the old block row (1000,392) hit empty page and typed into a newly appended block instead; (2) the crop images I read are **downscaled when displayed**, so reading element coordinates off a displayed crop put a probe 165 device px away from its target (the folder icon is at x 830-865 device, while the displayed crop suggested x 125). Both were caught by going back to pixel measurement in the full frame instead of trusting the picture | Recorded because the earlier notes already carried two popup-focus traps and three input traps; the rule that survives is that every coordinate used by a probe must come from a pixel measurement of the same frame the probe writes, from the same run state |

| 86 | Insert-menu popup hosting, **measured with temporary instrumentation (runs 41-46, diagnostic binaries `85a72962…`/`234a5e06…`/`ee865f5d…`)** | At `Open()` on the shipped configuration: `popupBounds=0,0,0,0`, `childBounds=0,0,326.4,410.3`, `usingOverlay=True`, `lightDismiss=True`, `placement=Bottom`, `offsets=0,0`, `target=Button[Insert]` (the block-gutter `+`), `targetBounds=(31.3,0,32.3,32.3)` relative to its parent. After a real layout pass the chain from the search box runs `TextBox < Grid < StackPanel < CuiContainerControl < Border < Panel[13.1,13.1,…] < CuiContainerControl[0,0,326.4,410.3] < Panel < ContentPresenter < CuiMaterialBorder < OverlayPopupHost[289.5,212.7,326.4,410.3] < PopupOverlayLayer[0,0,1440,899.9] < VisualLayerManager`, every element reporting `ht=True vis=True/True`, with `targetAttached=True childAttached=True popupOpen=True` | The popup **is** hosted in the window's `PopupOverlayLayer` through an `OverlayPopupHost` with real bounds. So the earlier zero-bounds reading was a timing artifact of logging before layout (row 79), and the drawn-where-input-is-not description stands on the input evidence rather than on bounds |
| 87 | Insert-menu input routing, **framework-level defect located to the hit-test path** | (a) Hit-testing the Window and the `TopLevelHost` root at a point that lies inside `OverlayPopupHost` returns only the page: `ScrollContentPresenter < CuiContainerControl[EditorScroll] < CuiContainerControl[PageCard] < …` - the popup subtree is never returned. (b) Independently, a real routed press recorded by a window-level tunnel handler in run35 had the same page chain as its source. (c) The fork deliberately makes the `Popup` control hit-test-invisible (`framework/CUI/vendor/Avalonia/src/Avalonia.Controls/Primitives/Popup.cs:162 IsHitTestVisibleProperty.OverrideDefaultValue<Popup>(false)`), and `IsHitTestVisibleProperty` does not inherit (`…/Avalonia.Base/Input/InputElement.cs:64-65`), while the measured `OverlayPopupHost` itself reports `ht=True` - so that default is not the mechanism. (d) Experiment: removing `ShouldUseOverlayLayer = true` makes `Open()` fail **silently** - no menu at all, no host log, no popup window on the display, document unchanged (run45) - and was reverted. (e) Experiment: setting `IsHitTestVisible = true` on the Popup control changed behaviour again, this time suppressing the row hover action that runs 33/34 recorded (`Preview input=Pointer placement=Below`), with no `ChooseDestination` and no document change (run46) - reverted, recorded rather than adopted. | Consequence stated plainly: on the running X11 build the insert menu cannot be completed by mouse **or** by Enter, while the identical route works headlessly, so BF-13 and BF-17 stay **NOT RUN natively** and the usability verdict for the insertion flow is **fail** until this is fixed. This is now placed in the popup host/hit-test path (the fork plus this Avalonia version), not in Boards layout. Named next step, not yet done: fix the keyboard route inside Boards first - `ToolbarBuilder.cs:97-105` builds its `choices` list filtered by `IsEffectivelyEnabled && IsEffectivelyVisible && IsHitTestVisible && Focusable`, and the CUI loader sets `Focusable = interactive` only when `Interactive` is authored (`framework/CUI/Runtime/CuiControlLoader.cs:3554-3562`), so the plausible reason Enter does nothing is that `choices` is empty; prove or disprove that headlessly (assert a non-empty choice list and that Enter commits) before touching shared framework hit-testing |
| 88 | Two more probe errors of mine, and the clean state | run41 `AA01`: a click aimed at the block row hit the page title, so the trigger character went into the page's identity (`home20` page title became `St/art here`) - the same class of error as row 85 and the reason every probe from now on measures the gutter glyph in the frame it is about to act on. run45 `AD02/AD03`: the probe reported nothing at all; the honest reading was that the menu never opened (no host log), which is evidence about the experiment, not a lost keystroke | All diagnostics are reverted (`git checkout` on `InsertMenuContext.cs` and `ToolbarBuilder.cs`); the only tracked modifications in the tree are the two design `.cui` files already committed and the inherited `eng/spec-coverage/coverage-summary.json`. Clean rebuild from the committed source: App `9dc913d9…`, Embedding `9ec9d376…`, app suite **609/609** (`/tmp/opencode/qwen-1102-appsuite20.log`). Scratch sandboxes now also include `home20`-`home24` with `profile20`-`profile24`; `profile20`'s board carries the `St/art here` probe marker, so it is not usable as clean evidence |

| 89 | **App-wide: no CUI overlay-layer popup can be operated in the running X11 build**, and one over-strong claim of mine corrected | The block-grip menu uses the same hosting configuration as the insert menu (`BlockGripCui.cs:86-91`: `new Popup { … ShouldUseOverlayLayer = true, … }`). run47 on the clean binary `9dc913d9…` (fresh `home25`/`profile25`): clicking the grip opened the actions menu (19 940 changed pixels in x 599-1192 y 456-949, frame 246, rows measured at device y centres 475/543/619/720), and a click aimed at the "Duplicate" row at (760,619) left the physical file at `richNotes.version 3` with the same three blocks (`block-d2febaf98d88`, `block-383deb02c12d`, `block-51afd0d6dfca`) - only partial menu redraw followed (6 232 changed pixels, frame 247). A deliberately offset click at (760,692), testing whether the press was being mapped ~37 logical units out (the 74-device client-area offset), changed nothing either (frame 248). Keyboard evidence: with the insert menu open, two `Return` presses produced two **extra empty paragraph blocks** in the file rather than driving the menu, and no `Preview` or `ChooseDestination` dispatch was logged at all (run47 AF02, frames 244-245) - so the popup did not own the keyboard either | Corrects row 79's overreach: a single `Return` was never supposed to finish the insertion (the flow deliberately asks for a destination first), so that part of the earlier statement was wrong and is withdrawn. What replaces it is stronger and simpler: overlay popups receive neither pointer presses nor keyboard focus in the running build, so **every** popup surface in Boards - insert menu, destination step, block actions, board switcher, overflow, font menu - is unusable by mouse or keyboard on this platform, while working in the headless mount. Consequence for the gate: BF-05, BF-12, BF-13 and BF-17 cannot be exercised natively until this is fixed, and the usability verdict for popup interaction is **fail**. This is framework work (`framework/CUI` popup presentation plus the vendored Avalonia: `TopLevel.EnableVisualLayerManagerLayers`/`VisualLayerManager` at `…/Avalonia.Controls/Primitives/VisualLayerManager.cs`, `TopLevel.InputHitTest` and `HitTesterOverride` at `…/Avalonia.Controls/TopLevel.cs:454,831`, and the fork default `IsHitTestVisibleProperty.OverrideDefaultValue<Popup>(false)` at `…/Primitives/Popup.cs:162`), and it must be raised as a shared-framework defect with a headless and native reproducer rather than patched around inside Boards. Named first instrument: a mounted-surface test that hit-tests a point known to be inside an open overlay popup and asserts the popup control is in the returned visuals, then the same assertion natively through the press probe, so the platform difference is pinned before any fork edit |
| 90 | Native Enter re-check, with the earlier row-84 claim narrowed | run47 typed into a block and pressed `Return` twice while the insert menu was open, so that run cannot decide the row-84 question: the two Enters created two extra paragraph blocks, which is what block-editor Enter does. A separate clean measurement is required (no popup open, caret at a known offset, one Enter) before either confirming or denying that native Enter splitting regressed | Row 84 stays open as written with this limitation stated. BF-07's native half stays NOT RUN rather than re-marked verified. Scratch `home25`/`profile25` now holds a three-empty-block board used only for popup probing |

### Evidence level of Milestones 8 and 9, and the on-disk native binary

Both of those units are **test evidence only**: no on-screen capture and no native run was made for the
single-undo-owner fix or for the structured failure identities, and neither was ever rendered. Stating it
plainly because the earlier milestones mix native and headless evidence:

- Milestone 8 (undo ownership): the *defect* was observed on screen and in the physical file (receipt `L05`,
  title going `BF07OK` → `BF07` under Ctrl+Z). The *repair* is proven by the headless suite only.
- Milestone 9 (failure identities): entirely headless. No native path was exercised, and the app-facing half
  is proven only by a `StatusText` assertion. Still true after Milestone 10.
- Milestone 10 (keyboard undo route and undo granularity): **native evidence exists** - the defects were
  measured on the running app (run20/run21/run22/run23) and the final state was proved on the running app in
  run26 (receipts R02-R04, frames 170-172, one Ctrl+Z taking `richNotes.version` 9 → 1 and the block empty on
  screen). The structured failure identities of Milestone 9 and the `FindControls<T>` traversal gap of row 53
  have **no** native evidence.

The standalone binary still on disk
(`9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll`,
SHA-256 `2068bd0f4785706b56ef98515a60d91854e58a333288485bd225c1b99df30c05`, built 15:53 from `a2f820bf`)
**predates both fixes**. Anyone re-running native evidence must rebuild from `e49c138a` or later first; the
independent reviewer recorded the same hash in their report, which is why it is quoted here. Nothing in
Milestones 8 or 9 has been seen running.

### Native run identity for Milestones 4, 5, 6, 7 and 8

Native identity for these frames: standalone `CakeOS.Apps.Boards.App.dll` at
`9to1 Workspace/Boards/app/bin/Release/net10.0/`, launcher`/home/jacob/Documents/Codex/2026-10-24/prepare-the-local-9to1-teams-from/work/dotnet-sdk-10.0.301/dotnet`,
X11 `:1`, client 2850×1781 device px (1440×900 logical at scale 1.979), sandboxed
`HOME=/tmp/opencode/qwen-native-20261101/home6` (+ `XDG_*` inside it) and
`HAVEN_DATA_DIR=/tmp/opencode/qwen-native-20261101/profile6`. Instances `run7`–`run14` and `run16` were
closed through `_NET_CLOSE_WINDOW` and all recorded `exit=0`; `run15` is the crash record and exited `134`
by itself (nothing was killed by the harness); `run17`/`run18` ended in one `exit=0` after two close
requests (row 49); `run19` was closed at 17:02:38 and recorded `exit=0` (`run19.exit`), which also clears the
"instance still running" blocker recorded by the independent reviewer. No test instance is left running on
the owner's desktop, and nothing was written into the owner's real `Documents/9to1/Boards` — every fixture
file lives under the sandbox.

`home7`/`profile7`, `home8`/`profile8`, `home9`/`profile9` and `home10`/`profile10` are this session's
scratch sandboxes: they contain the probe markers (`GRAN`, `ZZTOP`, `SELREP`, `SPLITME`, `BF07SECOND`,
`REPLACED`, `M9NEXT`) named in Milestone 10, and are the fixtures for those receipts only.

**The `profile6` / `home6` fixture is contaminated and must not be used as evidence by the next session.**
Its `Untitled board` now holds four pages (`Start here` renamed by unattributed typing to
`Stqzart here BF07k`, `Page 2`, `Page 3`, `Page 4`), ink strokes of unknown attribution on two of them, and
the marker texts `DIRTYCLOSE7CRASHDRAFT7WXYZ`, `LATEEDIT42`. Rows 50–52 were measured before that
contamination mattered for the undo finding (the title text itself is the witness), but nothing else in that
profile should be treated as a known-good state. Start the next native probe from a **new** sandbox profile
directory with the window id re-read before and after every step.

Also corrected in §1 above: the v3.2 **atlas** is present (`newAtlas.html` matches the owner-attached
`/tmp/codex-file-preview-2hRdoO/9to1_Interface_Atlas_v3.2.html` by SHA-256 `80a4ecc6…1a131`) and
`ThemeRefs/` is unpacked with 27 PNGs in the 10 folders; only the **markdown** half of the v3.2 pack is
missing, so the theme-pass *instructions* remain provisional while the imagery and the DOCX theme rows
(`V31-THEMES-001–003`, which carry the per-folder cues including the Professional/Glow component sheets)
are usable.

**Closed by Milestone 5, with the measurement that closes them:** sidebar width (was 268, now 255.2 logical
measured on screen), the 32-unit tree pitch, the 760-unit readable-column **cap** (measured at two window
widths), the green block `+` (104 green px → 6), and the long "Vertical Stacking" label (now `Locked
layout` / `Free placement`, read off the running app). **Closed by Milestone 7:** the block-id shape
divergence (row 46: the app-side defaults now match the contract prefixes, with the divergence measured on
the running app first).

Still open after this pass, unchanged and explicitly not relabelled complete: the `AI` pill is inert
(greyed) and the AI Read-only/Write dimension does not exist; footer `Add` + **avatar** pair (held as
`OPEN-2026-11-01-BOARDS-FOOTER-AVATAR`); the *centred* half of the 760-column reference (held as
`OPEN-2026-11-01-BOARDS-READABLE-COLUMN-CENTRING`); Chat / Scoped Chat page types (10 MUST rows) and the
Write/Canvas rows in footer `Add` that are all disabled without explanation; the remaining seven canonical
failure codes with no producer yet (row 58) and the untouched `9to1.Boards.*` typed action catalogue, which
is the API-parity half of the same specification section ("Every action must declare its name,
arguments/schema, result schema, required target-app permissions, risk level, whether it is reversible,
whether it can produce external side effects, and the objects/scopes it affects"); shared Productivity Engine
+ Rnote ink engine consumption; large-board
virtualisation and the performance harness; list/table placeholder words (`"New item"`, `"New table"`);
the ink palette with no swatches and
no current-colour readout; tree drag-to-nest absent; keystroke-level draft journaling absent (the sub-1 s
loss measured in row 28); whether Drawing mode should still allow block-grip drags (the ink overlay takes
the pointer there, row 41 — grip reordering itself in text mode is now verified, rows 43–44); the
unresolved input observations in row 48 (insert-menu typing and the deep keyboard walk) and the
close/window-id observation in row 49; BF-07 native typing/block-boundary behaviour, which is still
**NOT RUN natively** (the headless coverage added in row 52 proves click-to-block focus works in the
composed surface, but the native re-probe was invalidated by the contaminated fixture, row 52); the
`FindControls<T>` Panel-only traversal gap of row 53, whose wider consequence (any rule applied through
that walker silently skipping controls inside border/decorator containers) is unfixed; and the
embedded-Haven side of every shared-surface change, unverifiable on this PC (rows above).
`eng/spec-coverage/assessments/Boards.json` is still a code/test reading pass and has **not** been
regenerated for these fixes yet — it stays at takeover states until the whole gate is exercised end to end.
