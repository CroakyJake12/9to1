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
* CUI runtime suite and Boards app tests: **not re-run after the chrome unit** — run them next
  (`eng/development/cui_test_ratchet check`, Boards app tests were 436/436 before this unit).
* Independent acceptance: paused per owner instruction. All 102 `UX27` scenarios: NOT RUN.

## Commits in this unit (all pushed to `main`)

* `0c804890` chrome transcription + header fix + the two corrections above;
* `3f045894` `drive_window.py --window-id/--list`, xlib name fix, and the honest input A/B record.
