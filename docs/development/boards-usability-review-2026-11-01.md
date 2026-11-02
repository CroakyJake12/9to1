# Boards exploratory usability review — 1 November 2026

## Scope and evidence

This is a **limited exploratory review, not independent acceptance**. At review time the
checkout was `main` at `a2f820bf2924fdda1d4f7c77666f6d47e5acd6f6`, with an unrelated
modified `eng/spec-coverage/coverage-summary.json` and an untracked
`9to1 Workspace/Boards/app.tests/BoardsBlockClickFocusTests.cs`. A separate Boards
test session was active, so this reviewer did not send input to its window, start
a competing test instance, run the SDK, or modify its fixtures. The startup recovery
projection was not configured in this environment and the recovery command returned
BLOCKED; no coordinator state was replaced.

New work for this report: visual inspection of six retained screenshots from the
1 November standalone native exploratory run, plus comparison with the Boards
requirements. This is **screenshot review**, not a fresh interactive test. Behaviour
results below are explicitly attributed to the earlier tester's native actions and
file inspections, documented in
[`qwen-boards-theme-continuation-2026-11-01.md`](qwen-boards-theme-continuation-2026-11-01.md).
They are not proof that every later change, the installed build, or the embedded
Haven route behaves identically. That log identifies the builds per milestone; native
testing spans multiple revisions, principally `d7bf967b` through `bac7398d`, not
one fully verified current build. The retained captures are on this PC under
`/tmp/opencode/qwen-native-20261101/` and may be temporary; the log gives capture
names, steps and file-level observations. Screen: Linux x64, X11, standalone Boards,
light theme, 2850×1781 device pixels, approximately 1440×900 logical at 1.979 scale.
Private test profiles and fixture boards were used. No real participant study was run.

## What the earlier native exploration demonstrated

| Journey | Result and evidence level | Boundaries |
|---|---|---|
| Create and resume a local board | Earlier tester created a local board without an account/Files connection, entered text, saved, closed and reopened in a fresh process, checking text and block IDs in the file (log rows 2, 6–7). | Destination-selection variants and all recovery paths were not covered. |
| Add content versus add pages | Earlier tester opened the toolbar Add menu, dismissed it without changing the file, and used the separate sidebar Add menu to create a page (rows 22–24). The selected insert row is visible in `80-add-selected-crop.png`, inspected for this report. | Deeper keyboard navigation and one later search-input attempt were inconclusive; see row 48. |
| Edit, draw and resume | Earlier tester drew black and orange strokes, checked them in the physical file, closed and reopened, and saw both strokes and text (rows 26–27). This reviewer inspected `102-topology-editor2.png`. | Mouse/synthetic pointer only, not a stylus or proof of Rnote integration. |
| Move and undo | Earlier tester moved a page using the context menu, then undid/redid it, and separately grip-dragged a text block and undid/redid that move, comparing saved state (rows 29, 43–44). | Tree drag-to-nest did not work in that attempt; complex drop targets remain untested. |
| Data block | Earlier tester inserted a database block with a real Data reference and observed its controls (row 45); this reviewer inspected `116-add-table-selected.png`. | This is not a completed editable-table, data-entry, or cross-app round-trip test. |

## Prioritised findings

### 1. Recent edits can be lost on abrupt termination — high impact

**Earlier native observation, not freshly reproduced here.** The prior tester forced
termination 150 ms after entering `WXYZ`: the tail was absent after reopening and no
recovery offer appeared. The same input followed by a normal close was retained
(log row 28). This establishes a tested unsaved-edit loss window, not corruption
of the whole board, and does not establish the full recovery contract or exact
maximum loss window. It matters because users may assume a locally autosaved note is
recoverable after a crash. Investigate a durable edit journal and truthful save state;
retest forced termination at multiple points and compare the offered recovery with
the last confirmed durable revision. Relevant: UX27-BOARD-004/008, persistence and
recovery in the canonical Boards specification.

### 2. Tree reorganisation is hard to discover and drag nesting did not work — medium

**Earlier native observation.** Dragging a page row to another page did not change
the tree in the tested attempt, while the right-click `Nest under previous page`
action worked and was undoable (row 29). Users expecting direct manipulation may
not discover the alternative. Provide visible, accessible move targets and feedback;
retest mouse drag, invalid/cancelled drops, keyboard alternatives and undo.
Relevant: UX27-BOARD-015. Do not infer that all tree drag operations fail from one
attempt.

### 3. Ink colour is not apparent at the point of selection — medium

**New screenshot review plus earlier native observation.** In
`84-ink-colour-menu-popup-3e0001b.png`, the palette lists colour names without
visible swatches. The earlier tester reports that the trigger still reads `Ink colour`
after selection, although orange ink was actually stored (row 26). Add a visible
current-colour indicator and labelled swatches while preserving text labels and
accessible contrast. Recheck after changing colours and reopening. Relevant:
UX27-BOARD-035.

### 4. Important page-type actions are disabled without an explanation — medium

**New screenshot review.** `67-footer-add-menu-popup-3e0001f.png` shows Write and
Canvas create/attach choices greyed out, with no explanation visible in that frame.
The prior tester reports these actions disabled (row 24). A screenshot alone cannot
rule out a tooltip or another explanation on focus. Users cannot tell whether a
feature is unavailable, needs setup, or is restricted. Provide a specific accessible
reason and next action, or implement the real supported flow; do not present an
inert action as working. Chat and Scoped Chat page types also remain open in the
continuation log. Relevant: UX27-BOARD-014/042/043.

### 5. Two different “+ Add” controls require users to infer their scope — low/medium

**New screenshot-based heuristic concern, not a demonstrated user failure.**
`102-topology-editor2.png` shows a toolbar `+ Add` and a sidebar-footer `+ Add`,
while the sidebar also offers `+ New page`. The earlier native test established
that toolbar Add inserts page content and footer Add opens page/section actions.
The identical short labels do not communicate that distinction by themselves.
Consider contextual accessible names, hover help or clearer concise labels without
removing either required control. Validate with first-time users and keyboard/screen
reader navigation. Relevant: UX27-BOARD-001/014.

### 6. Drawing mode changes the meaning of a grip gesture — investigate

**Earlier native observation, not yet classified as a defect.** While Drawing was
active, an attempted grip drag added ink instead of reordering; in text mode the
same class of grip reorder was observed to work (rows 41, 43). The pen-mode input
contract intentionally makes pointer input draw, so the appropriate behaviour at
the grip needs a targeted specification/interaction check. Make the active tool and
transition back to selection clear; test the grip boundary rather than assuming
either interpretation. Relevant: UX27-BOARD-023/036.

## Follow-up test plan

When the active test session has finished and source/build/profile ownership has
been reconciled, use a separate private fixture through the approved native lifecycle:

1. Create a board, choose an available destination according to the latest owner
   flow, type/format, save, normal-close and reopen in a fresh process; check file
   contents and state labels.
2. Exercise Add menus by mouse and keyboard, search, Escape/outside dismiss, focus
   restoration and long-menu navigation; repeat the previously inconclusive search.
3. Move pages and blocks by drag and keyboard, check targets, cancel and undo/redo.
4. Draw in locked and unlocked layouts, change colour, undo and reopen; use a real
   stylus when available and check ink/text tool boundaries.
5. Test table editing, board switching, narrow window overflow, screen-reader labels,
   high-contrast and light/dark/Glow/Professional themes. Test the actual Haven
   embedded route as well as standalone and identify each built artifact.
6. On disposable data only, test save failures and crash recovery, including explicit
   recording of what was durably acknowledged before interruption.

No new interactive test, SDK run, performance measurement, accessibility audit,
ARM/laptop test or formal acceptance was performed for this report. Earlier
automated test counts and observations are dated in the cited continuation log and
must not be treated as a pass for the current checkout or all platforms.

## Follow-up request for a fresh interactive test

After this report the owner requested that the test actually be run. A fresh
interactive test remains **BLOCKED**, not passed: the separate standalone Boards
process was still present (PID 3958725, X11 window `0x03e0000f`), using the previous
tester’s profile. It was not clicked, closed or signalled. The local workflow
projection was subsequently located at `work/local-team-runtime/development-workflow-state.json`
under the existing Codex work directory; the real recovery command returned exit 2,
including missing declared source exports and changed guarded-runner identities.
Its pinned ROOT-LIVE candidate is from 28 October, not the current `a2f820bf` checkout.
No matching current candidate, manifest and admitted native runner were established.
The standalone DLL on disk hashed to
`2068bd0f4785706b56ef98515a60d91854e58a333288485bd225c1b99df30c05`, but that
alone does not establish the complete build/source identity or authorise a launch.
No guard was bypassed, profile copied or live instance disturbed.

Next: reconcile the existing running test's ownership and normal close, then use the
existing coordinator to restore/review the relevant source custody and record a
current candidate and runner admission. Only then launch a private fixture and
execute the follow-up plan above. The initial statement that the projection was
unconfigured refers to the environment variable; the projection file was found
and recovery attempted during this follow-up.
---

## Addendum — 2 November 2026, interactive pass by the live development session

This section is written by the session that has been running the native Boards instances
(`qwen-boards-theme-continuation-2026-11-01.md`), not by the screenshot reviewer above. Everything here is
**interactive** evidence: a real standalone instance on this PC, driven by real pointer and keyboard input,
with the physical `.9to1board` file read back after each step. Nothing here is independent acceptance.

Checkout at addendum time: `main` at `e700bbaa`/`290a9dd0`, standalone `CakeOS.Apps.Boards.App.dll`, light
theme, X11 `:1`, client 2850x1781 device px, `RenderScaling` 1.9791666 (reported by the application itself).
Sandboxes `home31`-`home42` under `/tmp/opencode/qwen-native-20261101/`, instances `run55`-`run68`, all closed
through the normal lifecycle with `exit=0`.

### Changed on purpose, then verified on screen

1. **The insert menu now tells you which row the keyboard is on, in both stages.** Before, arrowing through
   the destination stage changed the page-side insertion preview while every row stayed blank; a hovered type
   row had a soft wash but a keyboard-selected type row had nothing. Now focus or hover in either stage draws
   a 1.5-unit accent outline, and it was checked by pressing Down once, twice, three times and capturing each
   frame (run62 frames 320-323: the outline moves to "Add Block Above", then "Add Block Below", then
   "Replace Current Block").
2. **Two defects found while making that change, both fixed before shipping:** adding a border inside the
   authored 34-unit row height deleted the labels of every row at or below the highlight (run61 frames
   316-319 — green headless, broken on screen), and the first attempt at sharing the cue between the two
   stages attached nothing because `GetVisualDescendants()` excludes the element it is called on.
3. **Residual, deliberately not hidden:** a focused row grows by 3 units, so the rows below it shift slightly
   while arrowing. It reads as a small jitter. Left as a design-pass item rather than patched with a private
   layout hack.

### Confirmed working with the mouse

- Creating a board from the landing screen, opening the insert menu from the gutter plus, choosing a
  destination row by click ("BELOW THIS BLOCK" header plus a "Change" affordance appears, run66 frame 339-340),
  and the block-actions menu from the six-dot grip (run56 frame 296: Delete / Duplicate / Move Block /
  Block Type).
- A queued item claiming a "grip hit-target mismatch" was measured and **retracted**: the grip's drawn six-dot
  glyph is inside its own 32-unit target, centred, with no dead zone between the grip and the plus. The
  earlier claim came from probe sequences that started from an unknown menu state.

### Open after this pass, with recipes

| # | What a person experiences | Evidence | Where to look |
|---|---|---|---|
| U1 | The row the menu highlights is not necessarily the row Enter applies, and the same two keys behaved differently in two runs | run68 frames 346-348 (highlight "Add Block Above" -> file unchanged, version 1); run67 frame 344 (same keys -> a Tabber replacing the paragraph, version 1 -> 3); headless reproduces the no-action half (`/tmp/opencode/qwen-1102-hlcase1.log`) | `ToolbarBuilder.cs` 123 (navigation list), 128 (Enter falls back to `choices[0]`), 148-153 (`Fill` clears and re-adds the destination stage) — ledger row 114 |
| U2 | One click on a filtered type row (e.g. "Table") leaves the menu open with **no items at all** and inserts nothing | run63 frame 337, run66 frame 342, both with the file unchanged at version 1 | same `Fill` guard at `ToolbarBuilder.cs` 150: it returns after `list.Children.Clear()` when the transient insertion-preview owner has been revoked — ledger row 113 |
| U3 | Arrowing into a menu row takes the row's own action: typing after Escape from the menu puts the caret nowhere useful, and Escape returns focus to an invisible trigger | run54 frames 288-289 (typing "X" after Escape produced no character on screen and no file change) | pinned in the opposite direction by `BoardsInsertionPreviewTests.cs:84` (`Assert.True(f.Insert.IsFocused)`) — owner question `OPEN-2026-11-02-BOARDS-INSERT-ESCAPE-FOCUS` in `docs/specification/open-decisions.json`, ledger row 102 |
| U4 | Popup geometry reported by the application and the drawn pixels disagree vertically, so screenshot-aimed clicks land on the wrong element | run65 receipt BL02: trigger `32.34x32.34 @ (436.55,180.38)`, panel `326.4x410.27 @ (289.52,212.72)` relative to `TopLevelHost`; the panel's x matches the drawn rectangle (594 device px = 300 units) while its y does not match the drawn menu body | `BlockRenderer.EmitGeometryDiagnostics` and the `BOARDS_DIAG=1` popup report in `ToolbarBuilder` — ledger rows 104 and 112 |
| U5 | The report's own item 2 above still stands: tree drag-to-nest does not exist, moves live in the row context menu, and grip **drag** reordering is still unproven natively (`BF-14` half-open: the menu is proven, the block identity applied to it is not) | run55/run56 gutter measurement; drag-to-move **NOT RUN** natively on the cue build | `BoardsBlockActionsTests` covers the drag threshold headlessly only — ledger row 105 |

The four specification-side notes from the reviewer above (ink colour, unexplained disabled page-type actions,
duplicate "+ Add" labels, drawing-mode grip semantics) were **not** touched by this pass and stay open.
