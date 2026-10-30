# Boards menu repair — 30 October 2026

Continuation of the overnight handoff at `7199daab`. Scope: BF-35; UX27-BOARD-023/024/027,
UX27-SHARED-001/002/009/010/013 and the canonical Boards inline-control/action contracts.

## Required user flow (pinned before editing)

1. In an editable native Board page, hover/focus a block to reveal its existing gutter controls.
2. Press and release the grip without crossing the drag threshold: open the authored floating menu
   anchored to that grip, with Delete, Duplicate, Move Block, a divider, then Design.
3. Move Block reveals Move up/Move down; Design reveals Text, Heading, Checklist, Bulleted list,
   Numbered list, Table, Image and Divider. Ineligible actions stay disabled. Preserve the current
   revision/board/page/block target while the menu is open; reopening captures a fresh target.
4. A supported action uses the existing typed Boards operation, changes only the intended block,
   closes the menu without blocking the UI, and preserves undo/save/reopen behaviour. Conversion
   must preserve content and identity under the owning contract; destructive replacement is not a
   substitute for conversion.
5. Outside click, Escape, owner detachment and target loss dismiss the menu and reset its expansion
   state. The next ordinary grip click can reopen it. Escape in a submenu closes that submenu first.
6. Dragging continues to move/place the block with the existing preview, cancellation and revision
   checks. Drag, cancelled drag and stale drag must not open the menu or become an accidental click.

The 29 October menu order elaborates the existing grip contract; it does not remove drag behaviour,
revision checks or shared implementation. BF-37 hover preview is the next separate unit. BF-38 Copy
link remains awaiting the owner's scheme decision and must not become a nonfunctional menu item.

## Validation and evidence

Cloud baseline at `7199daab`: **223 cases, 215 passed, 8 failed, 0 skipped**, reproduced with fresh
normal compilation. The six failures in `BoardsBlockActionsTests` comprised two incorrect `Button`
lookups for authored `MenuItem` rows, one fixture with unassigned block IDs, one menu-load lookup,
and two immediate drag-transform cleanup assertions. The remaining failures are listed below.

After this repair: **233 cases, 231 passed, 2 failed, 0 skipped**. All **25** block-action cases passed.
The added checks exercise actual window keyboard input, popup light dismissal/reopening, submenu
Escape, stale/closed tickets, duplicate/one-step undo and focus after a queued renderer rebuild for
paragraphs, checklists, bullets and numbered lists. Pointer cases retain the genuine open-popup,
visible-row, canonical-order, stale-refusal and undo assertions. Successful drag cleanup now waits
at most two seconds while pumping the actual dispatcher timers; the test also checks that the first
landing translation matches the release position and that refused moves never start that animation.

Production repairs: one authored toggle owner; overlay popup hosting; no self-wait inside the active
action; dismissal resets expansion; keyboard focus/activation stays in the authored rows; real enabled
states; submenu expansion retains the original revision ticket; duplicated text/list editors regain
focus without render-time no-op text events redirecting selection. Drag landing uses the correct offset,
requires an applied move, completes abandoned settle waiters and restores visuals on the UI thread.

The full suite still fails these pre-existing cases, preserved without weakening their assertions:

- `EmbeddedPresentationLifecycleTests.Two_live_boards_keep_custom_and_high_contrast_resources_isolated_across_repaint_and_await`:
  expected white document foreground, observed black after theme repaint.
- `BoardsEditorTests.Boards_cui_exposes_dynamic_host_and_menus`: declared-surface assertion at line 710.

Evidence: ordinary Layer1 development checks, **PARTIAL / NOT REUSABLE**, SDK 10.0.401, Release,
normal dependency graph, serial guarded execution. Full-suite peak SDK process-group RSS 1,007,091,712
bytes, no guard violation, source remained unchanged during the run. Retained logs, TRX, source-diff
hashes and resource receipts are under `/workspace/scratch/9to1-overnight-20261030/`; final run is
`menu-full-suite`. `block-menu-headless.png` is a Skia headless component capture, not native evidence.
`boards-menu-repair-2026-10-30.evidence.json` records exact result counters and hashes.

Reproduction command (use the serial/resource guards from the handoff):

```sh
dotnet test '9to1 Workspace/Boards/app.tests/CakeOS.Apps.Boards.App.Tests.csproj' \
  -c Release --no-restore --nologo -v minimal --disable-build-servers \
  -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false \
  -p:EnableWindowsTargeting=true --logger trx --results-directory /private/fresh/results
```

**Next, not complete:** Design still uses the prior blank insert-and-remove replacement. Replace it
with revision-fenced, content-preserving conversion before claiming the whole menu fixed. Mapping
incompatible objects or multi-item projected lists must not silently discard content. BF-37 remains
queued and BF-38 retains its owner decision. The two other suite failures remain open.

Original PC screenshots and private runtime evidence are unavailable here. Headless UI tests or
component captures must be labelled as such. ARM installation/execution and owner-instance retest
remain NOT RUN. Package hold stays in force until genuine menu behaviour is observed.
