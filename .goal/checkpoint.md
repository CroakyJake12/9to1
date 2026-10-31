# Checkpoint 5 · 31 October 2026

Verified implementation **`a0f37f4fa5961d26ec832bf624e549bb1bcd2660`**, `main`, normally pushed to `origin/main`. Final documentation/evidence commit follows without source changes. Goal remains open; no active source writer, all delegated scopes frozen. No authoritative usage meter; percentage UNKNOWN. No new schedule or remote worker was created.

## Completed and verified

Recovered clean stale work checkout to Qwen main `bf0375e4`. Its BlockRenderer ghost-caret work was already committed with recorded build/436 tests; older uncommitted notes were stale. Mounted route remains production `Boards/app/Boards.cui` through Program.cs and BoardsWritablePresentation. No competing local writer, remote bridges offline. Preserved old Goal files under revisions/2026-10-31-before-usability with a hash manifest.

- `6efb2fed`: shared CUI popup ancestor/scroll tracking, left-aligned menus, stable overflow, text-input line-height binding.
- `94d7c865`: nested menu editor focus and Escape returning to the selector.
- `26f5cccb`: board-name file menu/direct history/save status; declarative adaptive toolbar/fixed actions; selected-run attributes; queued rebuild focus/caret; actual paragraph spacing; safe blank-only Block Type continuation.
- `115834b9`: accurate caret/inherited-style readouts, explicit list capabilities, canonical list indentation and supported formatting persistence.
- `a0f37f4f`: native-discovered paragraph→list focus repair; new projected row retains selection/caret, newer explicit focus wins.

Final **Boards 469/469 passed**, `boards-conversion-final.log`/TRX. Earlier adaptive5/5/full464/464 passed. **CUI 754 passed/12 failed/766** in `cui-escape-final`; every failure matches unchanged tracked baseline, no new failure. Release standalone `native-conversion` build succeeded, 0 errors/1 existing WebRtcVad architecture warning. Serial SDK10.0.401 through checked-in guard and `/workspace/scratch/9to1-sdk.lock`, 2GiB floor. Logs retained under `/workspace/scratch/boards-overhaul-20261031`; pinned missing XamlX/DBus submodules initialized without pin changes.

Actual private X11/Skia native runs created/edited/renamed/saved/closed/reopened boards, exercised Enter/selected bold/font change/Undo/Redo, drew and reopened ink, resized to 900px, verified selector Escape→Space and disabled-list hover explanations. Final conversion was retested through the toolbar: subsequent typing reached and saved in the new list row; earlier paragraphs/ink remained intact. All test apps closed through Exit; private Xvfb :93/compositor remain available. Default bright theme only; owner-PC/ARM/full acceptance not claimed. Evidence/artifact hashes: docs/development/evidence/boards-usability-2026-10-31.

## Exact next task

Implement canonical list-row font/size/highlight/line spacing through record validation, all projection/merge/import/split paths, actual renderer, and physical save/reopen/undo tests. Decide compatibility before changing persisted fields: schema-5 additive fields load old files but older writers discard them. Whole-row fields do not supply selected-substring formatting; that additionally needs list-item runs and composite-ID range support. Current unsupported controls are explicitly unavailable.

Then address lossless conversion geometry/format and row-versus-whole-list semantics, async child-pane focus after parent remount, remaining contextual properties, real AI/collaboration host, AppFlowy Rust licence/schema/packaging boundary and remaining canonical/UX27 acceptance. Preserve CUI frontend/ghost caret/container outlines. No donor source imported; no full-specification completion claim.

Durable detailed handoff and continuation command: docs/development/boards-usability-handoff-2026-10-31.md.

## ACTIVE sprint restart — 31 October 21:56 UTC

Owner corrected remaining allowance to76% (owner report, not a live meter), commands2h continued work until midnight and Astra-only subagents. Six current scopes: astra_lists (contract/list projection/renderer), astra_conversion (adapter/conversion), astra_toolbar (adaptive UI), astra_nested (tabber continuity), astra_cui (CUI lifetime/appearance), astra_spaces (actual Spaces). AppFlowy feasibility lane paused for explicit Spaces allocation; no donor source imported. Root owns main VM/range integration, SDK, native interaction, commits/push. First integrated Boards focused compile/test running under `/workspace/scratch/boards-sprint-20261031/boards-first.log`; no new sprint changes verified yet. Source units currently frozen; mainVM added row-only formatting, list split attribute preservation, nested capture hook and composite conversion-focus handling.

### Sprint verification update — 22:05 UTC

First Boards focused run compiled: 81 passed, 4 failed. New list presenter tests exposed actual CUI font-default precedence (24px became14px); fixed via explicit CUI document typography, including foreground. Source also now contains paragraph margins, safe conversion style mapping, nested pane lifetime fences, list inherited-spacing split preservation and supported adaptive controls. Awaiting second/full Boards run; do not label these verified yet. CUI lifetime/theme/admission focused run passed33/33 (`cui-focused.log`). Spaces full run was blocked before tests by missing Dev NuGet assets (`spaces-first.log`); desktop dependency restore in progress. Spaces implementation adds archived recovery/CAS, visible removable local and typed sources, preserved physical files and platform-correct path identity; tests pending. Root SDK is serial; six Astra workers prepare next patches in scratch while live source is frozen.

### Sprint verification update — 22:13 UTC

Boards full second run compiled:552 passed/4 failed/556. All22 list typography,47 lossless conversion and12 nested continuity tests passed. Two new toolbar tests had erroneous exact-type assertions (CuiMenuItem subclasses MenuItem), now corrected and passing in diagnostic run. Remaining2 VisualCapture style-menu failures exposed real initial graph/table events changing selection and dirtying unchanged content; VM no-op guards and renderer initialization suppression/precise numeric display now applied. Toolbar reuse fix in progress to preserve open menus/drafts across same-page block renders. Diagnostic run9passed/2failed, including3 stable split-row identity tests after fixing composite ID preservation.

Desktop dependency restore succeeded154s. Spaces production now compiles after nullable reference fix; test assets are stale (missing current explicit CUI references), so targeted Spaces test restore is next. No Spaces tests have executed yet in this sprint. All next feature units remain scratch until initial integrated milestone is verified/native-tested and committed. SDK logs in /workspace/scratch/boards-sprint-20261031.

### Active integration state — 22:18 UTC

- Boards contract full124/124 passed (`boards-contract-full.log`).
- Boards focused latest97/98 passed (`boards-lifetime-focused.log`): style menus and direct selected-range formatting across toolbar-focused redraw now pass. One graph instant-typing failure remains; synchronous TextProperty binding plus diagnostic now applied, awaiting retry. Same-target toolbar retained, drafts invalidated for true range/selection changes, stale detached ranges refuse whole-block fallback.
- CUI full767/773 passed (`cui-full.log`); six failures are known baseline: four old Glow fill assertions, IME cross-dispatcher preedit decoration, Home exact-copy assertion. Menu keyboard-entry + real action-shelf availability tests first27/29 (two duplicate-logical-path test helper errors); corrected joined/deduplicated tests now running `cui-menu-joined.log`, exec16464.
- Spaces full294/304 (`spaces-assetsfixed-full.log`), all13 new recovery/source/render cases passed. Reviewed stale-test corrections now applied (no requirements relaxed), with added explicit noninheritance case, exact layout/history/shelf assertions and canonical manager-icon alias test. Need full retry and actual desktop build/launch. Current CUI actual shelf-availability test supplies equivalent runtime dispatch-guard coverage.
- All current code frozen during serial SDK. Next: corrected CUI focused result; Spaces full retry; Boards graph/lifetime focused then full; native standalone + actual Desktop Spaces builds/interaction; commit/push verified milestone.
- Pending scratch only: lists grapheme patch and canonical item.Runs A; conversion third-unit guard/grapheme/AddTabber plus future A mapping guard; toolbar size dropdown/context Properties/heading fallback; nested Escape/AddTabber/Enter and separately peer-reviewed nested toolbar-range extension; CUI IME decoration fix; Spaces archive selection/builtin source/order/persistent navigation/compact390px/effective inherited policy. Agents retain exact scratch manifests. Do not overwrite current source with older full-file scratch copies: rebase them.
- Deadline still00:00 UTC; owner-reported76% at21:56 is not a live meter. Continue useful cycles after first milestone. Stop new features about23:45 and secure verified work before midnight.

## Owner-requested final wrap — 31 October 2026

Owner reported13% and explicitly requested immediate wrap/commit/push, superseding continuation until midnight. No live usage meter was available. All Astra workers stopped; no new features started. Implementation `221510f1a817366f813cbbfe557bf7f6f5b7b8a1` committed on main and pushed to origin/main; remote ref readback matched.

Final source: Boards565/565, contract124/124, Spaces306/306 passed. CUI774/779, with5 recorded existing expectation failures; all13 new cases and original IME case pass. Final full Desktop and standalone Boards Release builds both passed, each0 errors/1 existing WebRtcVadSharp platform warning. `git diff --check` clean. Raw evidence retained in /workspace/scratch/boards-sprint-20261031, portable verification receipt in docs/development/evidence/boards-spaces-sprint-2026-10-31.

Fresh final-build native retest deferred at explicit owner wrap request. Spaces remains incomplete (390px navigation, archive selection, further canonical requirements); list substring formatting and remaining Boards specification also open. Exact next steps, architecture, local-only unapplied drafts and evidence limits: docs/development/boards-spaces-sprint-handoff-2026-10-31.md. This checkpoint and handoff follow in a documentation-only commit.
