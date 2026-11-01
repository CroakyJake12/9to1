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

1. Verify the inherited baseline compiles and passes the real Boards suite on this PC. *(running: log
   `/tmp/opencode/qwen-1101-boards-baseline.log`)*
2. Drive the actual mounted route (`Boards/app/Boards.cui`, standalone `Program.cs`) natively and
   re-test each `START_QWEN.md` §3 / `V31-BOARDS-001–007` decision as an observed behaviour, with
   screenshots and input receipts. Fix only defects reproduced on this build.
3. Continue the remaining Boards specification in dependency order: list-item range formatting,
   font-size choices, universal contextual Properties, heading fallback readout, truthful drawing-history
   labels, Escape-cancel tab names, stale AddTabber/Enter guards, nested toolbar-held range continuation,
   grapheme-boundary selection, then the ledger's NOT-IMPLEMENTED clusters that are local-first
   (save-status truth, canonical error identities, block/ink undo boundaries, mode matrix Edit/View ×
   Locked × Snap).
4. Execute the acceptance gates `BF-01`–`BF-20` against this build with disposable fixtures, including
   save → close → reopen in a **fresh process**; record exact commands, build identity, dirty state,
   screenshots, and keep FAIL/BLOCKED/NOT RUN visible.
5. Only after that gate: shared CUI theme-design pass using the corrected v3.2 atlas + `ThemeRefs/`,
   honouring `V31-THEMES-001–003`, `V3-CUI-LAY/FEA/ENT/PER/ACC`, and the unresolved mappings
   (Frost≠renamed Frosted Glass, Industrial≠Metallic, Synthwave/Snug not built-ins).
6. Commit coherent verified units and push to `origin/main` at safe milestones (no force-push).

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

Open in-scope items carried forward (not relabelled complete): footer `Add` + **avatar** pair, the 760-unit readable column,
mode (visible `Locked layout` / `Snap on` pair in the reference; no Boards-side snap state exists today —
only the Notes canvas `gridSize` precedent), footer `Add` + **avatar** pair, the 760-unit readable column,
sidebar 256 vs the current 268, block-row `+` coloured green against a purple accent, long
"Vertical Stacking" label, Chat / Scoped Chat page types (10 MUST rows), `9to1.Boards.*` typed actions and
structured failure identities, shared Productivity Engine + Rnote ink engine consumption, large-board
virtualisation and the performance harness, list/table placeholder words (`"New item"`, `"New table"`),
and the block-id shape divergence that remains for conversion-created blocks.
`eng/spec-coverage/assessments/Boards.json` is a code/test reading pass and has **not** been regenerated
for these fixes yet — it stays at takeover states until the Boards gate is exercised end to end.
