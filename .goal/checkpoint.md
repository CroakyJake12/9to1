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
