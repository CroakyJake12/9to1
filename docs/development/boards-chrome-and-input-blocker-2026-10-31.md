# Boards AppFlowy re-UI — chrome transcribed, and the input-delivery blocker

Session state at the end of 31 October 2026 (evening). Companion to
`boards-appflowy-reui-plan-2026-10-31.md`. Everything below is labelled by evidence level.

## What is now on screen (on-screen capture, running dev copy on this PC)

AppFlowy's **window chrome** is transcribed in CUI inside
`9to1 Workspace/Boards/app/AppFlowyBoardStageOne.cui`, with geometry read from the donor, not from eye:

| donor source | measured value | where it is used |
| --- | --- | --- |
| `home_sizes.dart` `minimumSidebarWidth` | 268 | sidebar column width |
| `home_sizes.dart` `workspaceSectionHeight` | 32 | sidebar workspace row |
| `home_sizes.dart` `searchSectionHeight` / `newPageSectionHeight` | 30 / 30 | Search, `+ New page` |
| `home_sizes.dart` `tabBarHeight` / `tabBarWidth` | 40 / 200 | content tab row, each tab |
| `HomeSpaceViewSizes.leftPadding` / `viewHeight` | 16 / 30 | page-tree rows |
| `sidebar.dart:332-420` Column order | top menu, 6, search, 6, new page, 12, divider, tree, divider, footer, 14 | sidebar child order and margins |
| `tab_bar_item.dart` | Row of emoji + title + close | tab item cells |

Frame: `/tmp/opencode/h1.png` `7c8812a5b90b…` (crop `h1-crop.png`) — monogram + board name + `+`,
`Search`, `+ New page`, `⌄ Notes +`, nested page `Start here`, footer `Local board`, board to the right under
the tab row. Document load: **0 diagnostics** (`boards-run-h1.log` lines 3 and 5).

The header defect from `g8-crop.png` (section icon overlapping the title glyph) is **fixed and verified on
screen**: grid placement does not reach the children of a conditional branch, so both title branches fell into
the icon's cell; the branches now sit inside one `Container` carrying `Column="1"`.

## Data honesty in the chrome

* tree = this board's real sections/pages, including real nesting via `RichBoardPage.ParentPageId`;
* workspace "icon" = monogram derived from the real board title (a board has no icon field);
* page rows show **no** icon: 9to1 pages carry no page icon, so the donor's per-page emoji is not faked;
* the donor's workspace-menu and account-cluster controls are **not rendered at all** — this standalone host has
  no space or account session, and a dead control is ruled out by the owner's "route it up so it actually works";
* sidebar `+ New page` creates in the board's first section (9to1 has no section-less page) and the notice says so;
* Search is wired to a real filter over that same tree (`DonorSidebarQuery`, `ICuiWritableBindingContext`).

## Corrections to my own earlier claims

1. **`bb6a6370` said the board commands are "wired". That was compile-level evidence only.** No synthetic input
   has yet been shown to change any surface frame, so create/rename/delete/open remain **UNVERIFIED at runtime**.
2. **My first chrome draft invented CUI vocabulary**: `<Space>`, `<Border>`, `IconContent=`, `Command=`,
   `CommandParameter=`, `JustifyContent=`, `GridColumn/GridRow/GridSpan`, `Variant=`. The only occurrences of
   those in the whole own-`.cui` corpus were mine. Measured vocabulary instead: elements
   `Container/Text/Button/Input/Image/If/Else/Repeat`; placement `Column=`/`Row=`/`ColumnSpan=`; a list item is a
   `Button` carrying `Text` children with `action-argument` (`Mail/UI/MailWorkspace.cui:10-13`); a divider is
   `Container Type="Separator"` (1,639 uses, all inside the mirror corpus — **not yet proven in a live host**, so
   no divider is authored in the chrome).
3. `OPEN-2026-10-31-CUI-HORIZONTAL-REPEAT-LAYOUT` is **closed as not-a-gap** (see open-decisions entry): the
   "lost columns" story was a misread of one-section boards; `g6`/`g7` are byte-identical.

## The blocker: synthetic input does not visibly reach the app (unresolved)

Measured with `eng/development/drive_window.py` (XTEST) and `capture_window.py` (X GetImage):

* click on the stage-one `Leave AppFlowy re-UI` control: window `(628,103)` → receipt `root_xy (628,177)`,
  `window_origin (0,74)`, `window_id 0x480000f`. Frames `p0`, `p1`, `h2`, `h3` are **byte-identical**
  (`278bc6da2fb3…`) after two different clicks.
* `ctrl+o` on the first-party Boards surface: frame identical before/after (`0b4b7c4aeece…`) — **not** a clean
  negative, because that window may not hold focus at send time.
* Session is genuine **X11** (`/usr/lib/xorg/Xorg`, `Type=x11`, no Xwayland), so XTEST should be delivered.
* Direct `XSetInputFocus` on the client and on the frame: the server then reports focus on the frame `0x6001ca`,
  and `ctrl+o` still produced an identical frame. So focus alone is not the explanation either, though a key
  chord is weak evidence because the binding may not exist in this host.
* `--list` measured two viewable matches for the name `Boards`: WM frame `0x006001ca (-28,-24,2906,1913)` and
  client `0x0420000f (28,98,2850,1781)`. Name matching is therefore ambiguous in principle; `drive_window.py`
  now has `--window-id` (address the exact id the capture reported) and `--list`.

### Next executable step, in order

1. `--list` to get the client id, `capture_window.py` to get the same id and a frame;
2. click a **first-party** control whose effect cannot be missed — the `File` menu button, whose position can be
   read from a 1:1 top-left crop — and compare bytes. A menu opening proves the whole click path;
3. if it proves out, repeat on the stage-one surface with `--window-id`; if the stage-one surface alone ignores
   clicks, inspect how `Program.cs` mounts the stage-one document (host layer / hit-test visibility) rather than
   the CUI markup;
4. if even step 2 shows no reaction, the harness is the suspect: add an in-app probe (the CUI action dispatcher
   writing a visible notice when any action fires) so a frame change is the signal, independent of pixel hunting.

Until then: rendering claims are on-screen; behaviour claims are compile/test-level.

## Test state at handoff

* Boards app build: succeeds (`reui-build43.log`).
* Boards app tests after the chrome unit: **Passed! Failed: 0, Passed: 436, Skipped: 0, Total: 436**
  (`/tmp/opencode/boards-app-tests-chrome.log`, project `CakeOS.Apps.Boards/app.tests`). Note that the earlier
  attempt against `app/CakeOS.Apps.Boards.App.csproj` was a no-op (restore only, no tests in an app project) —
  the test project is `app.tests`, which is why the first run printed nothing.
* CUI runtime suite: **not re-run** in this unit (unchanged framework code); run `eng/development/cui_test_ratchet
  check` next session to confirm the 749/755 baseline with the 6 pinned failures.
* Handoff reload: the newest built candidate is running on `DISPLAY=:1` (window `0x420000f`, frame
  `/tmp/opencode/j1.png` `cd82fe05837c…`, 0 load diagnostics).
* Independent acceptance: paused per owner instruction. All 102 `UX27` scenarios: NOT RUN.

## Commits in this unit (all pushed to `main`)

* `0c804890` chrome transcription + header fix + the two corrections above;
* `3f045894` `drive_window.py --window-id/--list`, xlib name fix, and the honest input A/B record.

## Owner direction received after this handoff was written (31 Oct, late evening)

*"…literally just rebuild the interface in cui"*, *"and make it work"*, *"but you can do stuff in the native
rust"*, *"rather than forcing it inside of cui actions"*, *"just wire up the existing code to new ui"*.

Consequence for this file's blocker: **do not spend another unit on synthetic-input archaeology in the parallel
preview host.** The port target is the working `Boards.cui` surface, and the wiring reuses existing code. Full
route and the measured handler/adapter mapping (with file:line citations) are now in
`docs/development/boards-appflowy-reui-plan-2026-10-31.md` under "Owner direction, 31 October 2026 late evening";
the owner decision is recorded as `OPEN-2026-10-31-BOARDS-WIRE-EXISTING-CODE` (commit `e0046fad`).

### Executable recipe for the next unit (no re-reading required)

1. In `9to1 Workspace/Boards/app/Boards.cui`, wrap the current board presentation in
   `<Container Type="Grid" ColumnSizes="268 1fr">` and insert the sidebar block already authored and measured in
   `AppFlowyBoardStageOne.cui` (sidebar width 268; workspace row 32; Search 30; `+ New page` 30; tree rows 30 at
   padding 16; footer 28) re-using **Boards.cui's existing bindings/actions** — do not copy the `Donor*` actions.
2. Add the tab row above the board: height 40, each tab width 200, contents emoji/title/close, and a `+` tab at
   the right end calling the existing `AddPage` case.
3. Keep the section column geometry already measured: column 256, header 50 with `ColumnSizes="Auto 1fr Auto Auto
   Auto"`, card width 226 with `CuiFloatingSurfaceMaterial` + `CuiLineBrush` 1px border, card margin `3 4`, footer
   directly under the cards (grid rows `Auto Auto Auto`, column top-aligned).
4. Grid placement attributes do NOT reach children of `<If>` branches — wrap conditional content in one `Container`
   that carries `Column=`/`Row=` (this fixed the header icon/title overlap; see the header note above).
5. Registered palette names only: `CuiAccentBrush CuiAccentInkBrush CuiFloatingSurfaceMaterial CuiLineBrush
   CuiMutedBrush CuiPanel2Brush CuiPanelBrush CuiSurfaceMaterial CuiTextBrush CuiTextSoftBrush
   CuiWorkspaceMaterial`.
6. Compile after each edit unit with the guarded dotnet entrypoint, then capture on `DISPLAY=:1` and compare bytes
   against the pre-change frame to prove the change rendered.
7. Only after the ported surface renders: add the two missing view-model dispatch cases for `RenameSectionAsync`
   and `DeleteSectionAsync` (host-side, C#), then remove the preview seam and `AppFlowyBoardStageOneSurface.cs`
   duplicates.

## Port executed onto the working surface (commit `0fd48244`), plus a new crash found while verifying it

The donor geometry now lives in `Boards.cui` (the surface the owner actually uses): sidebar 268, inset 8, nav rows
header / search(30) / `+ New page`(30) / tree(1fr) / footer with CellGap 6, and a 40-high content tab strip above
the authoring toolbar (`EditorHost` rows renumbered: FormattingToolbar 1, ContextHost 2, PageCard 3). Wiring reuses
existing code only: `Action="AddPage"` for both new-page controls and `IsEnabled="{Binding CanEdit}"`.

Evidence levels, stated exactly:
* **on-screen capture**: `k1.png` `324fd437aee3…` differs from the pre-port frame `k0.png` `d6b9ede8c001…`, 0
  runtime diagnostics; restored instance `k4.png` `324fd437aee3…`.
* **NOT RUN**: the *editor* view of the port (the tab strip lives in `EditorHost`, which is `Active=` when a board
  is open). Reaching it needs either a click (input delivery still unproven) or opening a board by path, and that
  route crashes (below).

### New defect, with repro and retained logs

Launching the Boards app with a board file as `argv[0]` **aborts the process with an unhandled exception and opens
no window**:

```
Unhandled exception. System.IO.IOException: A local board with this identity already exists. Open that draft to resolve both revisions.
```

Repro that was run: `HAVEN_DATA_DIR=<profile copy> Boards.App.dll <path to a .9to1board>` using (a) the draft file
itself and (b) a copy with a brand-new filename in a different directory — **both** abort identically, because the
collision is on the identity stored *inside* the document, not the filename. Logs kept:
`/tmp/opencode/boards-run-k2.log`, `/tmp/opencode/boards-run-k3.log` (core dumped). Expected behaviour by the
exception's own message: open the draft and present a revision-resolution flow instead of crashing. This belongs in
the bug queue next to the reported-bug list; it is a crash, not a cosmetic gap.

### Corrections to my own claims made earlier in this session (both wrong, both recorded)

1. "the block editor sits inside the 280-wide nav column" — **false**. Parsed ancestry:
   `Page < Container < EditorHost(Row=1 Column=1) < PageCard`. My same-indent close-tag scan mislocated `NavPane`'s
   end at line 108 and I reported from that instead of verifying.
2. "board search is a capability gap" — **false**. `NavSearchBox` exists (`Boards.cui:92`) and is wired in
   `BoardsViewModel.cs:1767`, with edit-mode exemptions at `:1300` and `:1744-1745`.

Remaining true gaps after these corrections: filter, sort, layout-settings model, hidden sections, the multi-page
tab stack, card hover seam, popover 168 x 300 limit, drag reorder, grid/calendar.

## Crash fixed and reproduced against the same command that killed it (commit follows)

Two host-side changes, no CUI Actions involved:
1. `BoardsLocalDocuments.ImportAsync` — when the retained local copy is **byte-identical** to the file being
   opened, the import is a no-op returning the retained identity (plus `Recent.Record`) instead of throwing. A
   genuine divergence between two revisions still reports, because Boards has no revision-resolution surface yet
   (that stays a named gap; the old message even promised a flow that does not exist).
2. `Program.cs` startup — the import/open is wrapped in `try/catch (IOException)`; on divergence the app starts on
   the landing page with the reason on the existing `StatusText` channel (`_openPathFailure` on the nested `Program`
   class, same qualifier style as `Program.OpenPath`) instead of aborting.

Proof against the exact failing command from this session: the same `argv[0]` draft path that produced
`Unhandled exception. System.IO.IOException … / core dumped` (`boards-run-k2.log`, `boards-run-k3.log`) now runs
clean — `boards-run-k5.log` contains **no** unhandled exception, the window maps with title
`Untitled board — Boards`, and the frame is `/tmp/opencode/k5.png` `d17b7e69c9eb…`. Side effect worth noting: this
finally reaches the **editor** view without any synthetic click, so the ported sidebar/tab-strip geometry is now
observable rather than blocked.

Measured observation from that frame (pure-PIL pixel scan, thresholds noisy, so stated as an observation not a
verdict): no material boundary was detected inside the nav column at y=300 — `NavPane` is authored
`Background="Transparent" BorderWidth="0"`, so the 268-wide sidebar has no visual separation from the workspace,
unlike AppFlowy's sidebar surface. Closing that is presentation work on this surface, not a framework gap.
