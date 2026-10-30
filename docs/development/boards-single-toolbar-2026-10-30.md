# One Boards paragraph toolbar · 30 October 2026

Owner: “No need to show a floating toolbar while editing a paragraph as there's
already a toolbar at the top”, followed by “do the toolbar change and then push
to remote” and “as in commit and push everything”. Cycle 7 starts on main
30957bf24b2d324e1769932cca0f6cf7ae08fdfb; only this foreground owns source.

## Required flow pinned before source edits

1. Open the existing shared standalone/embedded Boards editor. The authored
   top FormattingToolbar remains the one paragraph formatting surface.
2. Focus/select text in a main-page paragraph or a visible owned Tabber pane.
   No additional paragraph/pane toolbar appears inside the document.
3. The existing top toolbar targets that editor. Its CUI bold/italic/underline/
   strikethrough state follows the actual selected range, preserving anchor/caret;
   font/style/insert controls target the same pane through its existing model.
4. Clicking toolbar commands does not switch the destination to the parent page.
   Formatting remains an ordinary canonical transaction with exact undo/redo and
   save/reopen. Return to a main-page paragraph to retarget the top toolbar.
5. Switching pane/page, viewing mode, removing an owner or closing the surface
   must prevent the previous target from receiving toolbar mutations.
6. Compile each edit unit and run authentic headless shared-host regression tests,
   retain earlier failures and actual component captures, then review/commit/push
   all task changes to main and verify remote SHA. No native/ARM acceptance claim.

Source inspection: main paragraphs already have no extra format popup. The
duplicate is PaneEditor._toolbar in BoardsTabberBlocks.cs. Removing it requires
top-toolbar routing; deleting controls alone would lose pane formatting access.
Legacy toolbar authoring/CUI migration outside this targeted repair stays open.
Cycle 6 child-toolbar gesture expectations must now exercise the shared top
toolbar, preserving the rename/first-click and destination assertions.
Two older retirement fixtures now start from the selected pane rather than an
inactive pane. Their pending-picker, nested-Tabber refusal, late-command refusal,
exact undo and fresh-editor positive assertions are unchanged; the new hidden-pane
guard must not make those tests pass without exercising the intended operation.
The Data-port component fixture now supplies one host toolbar around the real pane
instead of searching for the removed pane toolbar. Real insertion-menu, pending
creation and unlinked-artifact replacement-refusal assertions remain unchanged.

Environment refreshed: managed cloud connected, ROOT-LIVE recovery blocked by
absent private projection. Desktop bridge online at 09:32 UTC but native queue
not ready (native_contract_changed); no native launch admitted. SDK work remains
serial bounded ordinary Layer1. Latest usage approximately 8%, owner-reported
09:10 UTC; no later reading inferred. Full acceptance paused, package held.

## Checked result

The pane-local toolbar is removed. The existing top toolbar follows the focused
visible pane, keeps parent-page selection unchanged, and returns to main-page
formatting when its paragraph is focused. CUI range state/actions and the legacy
font/style/insert builder use the same current target. Toolbar-only refreshes do
not rebuild the document. Hidden, removed and inactive-parent pane edits are fenced.

Real pointer checks exposed the input's default selection clearing on focus loss:
clicking Bold collapsed the range and disabled the button before its action. The
existing paragraph selection binding now preserves the real range. Focus returns
before the busy binding disables the clicked command, avoiding a second focus-loss
race. Original canonical mutation, range identity, undo and persistence remain.

Fresh final Release: **387/387 full Boards app tests**, **113/113 contracts**, zero
failures/skips; standalone build passed with the existing WebRtcVadSharp
AnyCPU-to-x64 native-DLL warning. Five added cases cover forward/backward raw toolbar
formatting, font targeting, exact undo/redo, save/reopen and invalidated destinations.
The earlier focused repair passed 44/44; the adapted Data fixture passed 7/7.

All **15 attempts** are retained in
boards-single-toolbar-cycle7-2026-10-30.evidence.json, including two test compilation
failures, selection-collapse and focus-return failures, and the fixture that still
looked for the removed toolbar. No failed/zero-case run was counted as a pass.
All 12 final source/test hashes are in that evidence. Two final actual embedded-host
captures and two earlier failure captures are under evidence/boards-single-toolbar-2026-10-30/.
These are **HEADLESS DEVELOPMENT / ORDINARY_LAYER1_PARTIAL_NOT_REUSABLE**;
the captures retain the host's original dark/purple, tooltip and contrast state,
not a theme-matrix or native acceptance result. Data-port substitutions are labelled.

During validation remote main advanced to 1ffe3a8b46c8ecb76202b80deff148c7c5c37606,
adding only the owner's instruction not to message the parallel worker. It was
fast-forwarded without changing any tested source/test byte; no worker was messaged.
Desktop status at 10:03 UTC is online but native_queue_not_ready with
native_contract_changed. Native PC/ARM remain NOT RUN. The scheduled continuation
recorded at 10:05:18 overlapped this manual milestone and was coalesced while ACTIVE.
