# Boards usability continuation — 31 October 2026

Status: verified implementation committed and normally pushed to `origin/main`. Latest implementation commit **`a0f37f4fa5961d26ec832bf624e549bb1bcd2660`** (`main`); the later handoff commit changes documentation/evidence only. Recovered Qwen base `bf0375e438f9cb5d6487a9bbb1e4c823d753a0e5`. Main overhaul: `26f5cccb`; shared CUI fixes: `6efb2fed` and `94d7c865`; list/caret correction: `115834b9`; conversion focus: `a0f37f4f`. Goal remains open for the work below.

## Completed changes

- Board name opens the existing file operations, with direct Undo/Redo and actual save state. Rename uses the real document title. Recovery operations appear when applicable. Existing New/Open/Save/Save As/board properties/comparison operations retained; no existing export command was removed. Board-level custom icon persistence is not present.
- One authored adaptive CUI toolbar plus a separate fixed Add/Drawing/AI pill. Text style, actual font/size, range formatting, colours and compact paragraph menus; contextual image/table/drawing controls; shared overflow and Properties routing. Existing insertion services retained. AI stays disabled with an explanation until a real host exists.
- Font, size, colour, highlight and baseline edits preserve unaffected text runs and canonical undo/save behavior. Clearing a selected highlight removes inherited fill. Whole-block scaling preserves mixed run sizes.
- Queued block rebuilds preserve the active editor/caret and forward/backward selection without overriding an explicit new focus destination. Paragraph-to-list conversion selects the new projected row before refresh and retains its caret, so typing continues in that row. Qwen's committed ghost caret and container-only outlines retained.
- Shared CUI fixes: popup tracking through ancestor layout/scroll, left-aligned top-level menus, stable overflow remeasurement, text-input line-height forwarding, keyboard focus for inputs inside submenus, and Escape returning to the selector.
- Paragraph line spacing uses persisted block/style values and native text/caret metrics.
- Qwen continuation: Design renamed Block Type with a distinct icon, blank-only entry and stale-target checks. Existing safe paragraph/heading conversion capabilities preserved.

## Architecture and provenance

The actual mounted route remains `Boards/app/Boards.cui`, used by both standalone `Program.cs` and `BoardsWritablePresentation`. No replacement UI or new AppFlowy source was imported. AppFlowy Rust integration remains behind the existing session boundary, subject to AGPL compatibility, schema and packaging decisions. Missing pinned Avalonia XamlX/DBus submodules were initialized; no pins changed. No competing local writer or SDK was found at takeover; remote bridges were offline and no remote process actions were taken.

Latest Qwen `bf0375e4` already contained the BlockRenderer hover/ghost-caret work and recorded a successful build/436 tests. Older staged/uncompiled notes were stale. This session independently built/tested the current source and exercised the real application.

## Verification

Serial SDK10.0.401, Release, checked-in low-memory guard, `/workspace/scratch/9to1-sdk.lock`. Detailed logs/TRX under `/workspace/scratch/boards-overhaul-20261031`. Failed intermediate runs retained.

- `boards-conversion-final`: **469 passed, 0 failed** on final source, including all three list conversion/focus cases, continued typing and a delayed operation that must preserve newer Search focus. Earlier full run464/464 and final adaptive5/5 passed. Covers actual mounted header/toolbar, Enter focus, selected-run formatting, canonical save/reopen, history, line spacing and Block Type.
- `cui-escape-final`: **754 passed, 12 failed, 766 total**. Every failure matches unchanged `eng/development/cui_test_baselines.json`; no new failure. New popup/overflow/menu-input cases pass. The known theme/appearance/layout failures remain unfixed and are not treated as acceptance.
- Production standalone Release builds `native-verified` and final `native-conversion` succeeded, **0 errors**, one existing WebRtcVad AnyCPU/x64 warning. The final native binary was built/tested immediately before committing identical source; artifact hashes and informational-version provenance are in the evidence receipts.
- Native X11/Skia production Program.cs, private profiles: created a board, entered successive paragraphs with Enter, selected/bolded text, saved/closed through Exit/reopened, renamed, changed font size, observed persisted Undo/Redo, drew and reopened real ink. At 900px, fixed actions remain visible while adaptive sections collapse into overflow. A 55-character input was fully saved 1.61s after delivery; this includes autosave and is not a typing-latency benchmark.
- Final native follow-up: Escape then Space reopens the style selector; disabled list typography explains itself on hover; real toolbar paragraph→list conversion keeps focus and accepts/saves appended text without a second click. Earlier paragraphs and ink remain intact; final 900px layout still fits. All private test instances closed normally through Exit.
- Native artifacts: [evidence](evidence/boards-usability-2026-10-31/README.md), with raw runs at `/workspace/scratch/boards-native-tools/runs/{overhaul1,overhaul2,final,verified,focusfix}`. Xvfb :93 with client-mode xcompmgr; a fresh launch resolved the initial opaque shadow-margin artifact. Native checks used the default bright theme. This is not owner-PC/ARM or full theme acceptance. No installed application or owner profile was changed.

## Follow-up detail

Commit `115834b9` guards unsupported list-row typography/alignment/conversion actions with visible explanations, routes list indentation through existing canonical operations, projects inherited alignment/indent, and reads font/colour at a valid collapsed caret. The real mounted list test preserves supported bold/foreground/baseline/case/indent through physical save/reopen and verifies unsupported commands do not change the document. Caret tests cover run boundaries, end-of-text and stale/mismatched inputs. Full 464/464 plus focused 5/5 passed, followed by final 469/469 after the native-discovered conversion focus repair. Full list typography remains the next task rather than being presented as working.

## Exact next implementation task

Implement persistent list-row font family/size/highlight/line spacing end to end. Add optional inheriting fields to `HavenRichListItem` and `SetListItemFormatting`; validate them; project/merge/import them through every list path in `BoardProjection`; preserve them on split/duplicate/conversion; use them in `BlockRenderer.BuildChecklistRow` instead of its hardcoded 14px. Verify underline/strike/baseline rendering as part of this path. Clone/history paths use JSON and still need real tests.

First decide compatibility: additive fields load old schema-5 files, but old writers discard unknown fields on resave. Do not silently promise old-writer preservation. These fields provide whole-row formatting only; range formatting additionally needs list-item runs, composite-ID selection support and run-aware editing. Cover all three list kinds, actual presenter output, physical save/reopen, one undo/redo, split/duplicate/conversion preservation and old files before enabling the currently unsupported controls.

Other remaining specification work: lossless conversions retaining geometry/format with explicit row-versus-whole-list semantics; richer blank-block conversions; async nested-pane focus after parent remount; contextual properties across all types; actual AI/collaboration host integration; AppFlowy Rust integration/licensing/schema boundary; full canonical/UX27 acceptance closure. No claim that the full Boards specification or Goal is complete.

Cloud usage allowance is not exposed by available tools. Percentage UNKNOWN; context capacity was not used as a substitute. No new scheduled worker was created or remote worker dispatched. Current Goal state is preserved under `.goal`, with the previous contract/checkpoint archived and hash-recorded.

## Continuation commands

Use `/workspace/9to1`, read `AGENTS.md` and `.goal/CURRENT`, confirm main/status and no competing writer. Serial SDK example (choose a fresh log path):

```sh
flock -n /workspace/scratch/9to1-sdk.lock python3 -B eng/development/guarded_lowmem_build.py --floor-mib 2048 --timeout 900 --log /workspace/scratch/boards-next.log -- /workspace/.tools/dotnet/dotnet test "9to1 Workspace/Boards/app.tests/CakeOS.Apps.Boards.App.Tests.csproj" -c Release --no-restore --nologo -v minimal --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:BuildInParallel=false -p:EnableWindowsTargeting=true
```

Production launch is `dotnet "9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll" [path.9to1board]` after building its adjacent csproj. Preserve an explicit private `HAVEN_DATA_DIR`/XDG profile. Existing cloud helper is `/workspace/scratch/boards-native-tools/native.py`; Xvfb :93 and client-mode xcompmgr are private test infrastructure. Never substitute the separate AppFlowy comparison surface for this route.
