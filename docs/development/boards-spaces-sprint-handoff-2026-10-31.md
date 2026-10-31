# Boards and Spaces continuation — 31 October 2026

Status: verified implementation committed and pushed to `origin/main` after the owner reported 13% usage and explicitly requested an immediate wrap/commit/push. This supersedes continuing until midnight. No live usage meter was available; percentages were owner reports, never inferred from context. The complete Goal remains unfinished.

## Delivered in this sprint

- Canonical whole-row list font family, size, highlight and line spacing, including all three list kinds, physical save/reopen, history, imports and split preservation. Stable split item IDs now survive saves and continued typing. Explicit list marks/baseline remain explicit; new optional typography fields inherit. Schema-5 additions load old files; old writers discard unknown fields on resave. Substring formatting within list rows is still pending.
- Paragraph before/after spacing, consistent declarative toolbar capabilities and selected values. The toolbar now survives ordinary same-page block renders, preserving open menus and drafts; real selection changes invalidate stale drafts. Selected paragraph ranges survive redraw while the toolbar holds focus, without stealing newer focus or widening a stale range into whole-block formatting.
- Lossless supported block conversions preserve canonical geometry, paragraph settings and dormant payloads. Mixed/unrepresentable conversions are refused. Blank table/divider conversions and new projected-row focus work through the actual mounted menu.
- Nested editor caret/selection continuation survives parent remounts; newer focus, page/tab/section changes, detachment and disposal retire stale restoration.
- Initial graph/table control events no longer falsely dirty or select content. Graph numeric display preserves precision; edits commit before queued rendering, open graph settings survive it, and editor focus is restored after disclosure content is realized.
- CUI application/dispatcher theme ownership, weak global subscription lifetime, retryable theme publication, final foreground aliases, keyboard entry into embedded menu fields, and presenter-owned IME underlines. No-application loaders do not subscribe to a nonexistent UI lifetime.
- Spaces manager archive recovery and revision checks; visible local/typed sources and exact reference removal without deleting originals; platform-correct file-path identity; canonical management-icon alias. Existing stale test fixtures were reconciled to current explicit requirements, with stronger history/layout/shelf checks and explicit noninheritance coverage. See `spaces/test-corrections-2026-10-31.md`.

## Verification

Serial .NET10.0.401 through `eng/development/guarded_lowmem_build.py`, 2GiB admission floor, `/workspace/scratch/9to1-sdk.lock`. Raw logs/TRX, including failed intermediate runs, are retained under `/workspace/scratch/boards-sprint-20261031`.

- Boards contract: **124/124 passed**, `boards-contract-full`.
- Boards integration: **565/565 passed** on final source, `boards-final-full`. The focused graph fast-typing/continued-input/precision regression also passed, `boards-graph-layout`.
- Spaces: **306/306 passed**, `spaces-corrected-full`, including all recovery/source tests and actual CUI rendering. Captures under `spaces-captures-verified`.
- CUI: **774 passed / 5 known failures / 779 total**, `cui-bounded-full`. All13 new cases and the original IME failure passed. Remaining failures: four older Glow opaque-fill expectations and the Home description lookup. Baseline file was not weakened. An earlier full run stalled; its log is retained; the corrected no-application lifetime regression and bounded full rerun completed normally.
- Production builds and current native observation: finalization results below. Earlier committed baseline native evidence remains at `evidence/boards-usability-2026-10-31`; it must not be represented as native verification of every new sprint change.

## Remaining work and exact next task

First perform the current production native retest on the actual standalone Boards and Desktop `9to1://spaces` route; use `spaces/native-route-2026-10-31.md`. Preserve explicit private profiles and normal save/close. Then continue Spaces archive-selection persistence, built-in source management, sibling reordering and responsive pinned rail/dropdown navigation. The current 390px layout leaves too little content width, and bare Spaces entry leaves Chat at its summary until selected. Spaces is **not fully implemented**; its full41-ID working ledger remains `spaces/requirements-2026-10-31.md`.

Boards: add nested toolbar-held range continuation, fix grapheme boundary selection, then implement canonical list-item runs through projection, mutation, split, selection and renderer before enabling list substring formatting. Also pending: font-size choices, universal contextual overflow Properties, heading fallback readout, truthful drawing-history labels, Escape-cancel tab names and stale AddTabber/Enter guards. Remaining canonical/UX27 requirements, real AI/collaborator host work and AppFlowy Rust integration remain open.

Preserve the 9to1 CUI frontend, ghost caret and contextual container outlines. No AppFlowy UI or donor source was imported. Rust integration still needs the documented compatibility/licensing/schema/package boundary; do not substitute the comparison interface for production Boards.

Prepared but **unapplied/unverified** work remains outside Git source: `/workspace/scratch/astra-list-runs-a`, `astra-graphemes`, `astra-nested-next`, `astra-nested-toolbar-range`, `boards-lossless-next/README.md`, `astra-toolbar-size`, and `spaces-20261031-prepared/APPLY.md` plus its SHA256 manifest. Some full-file drafts predate later fixes: rebase, never copy blindly. These local drafts are not remotely delivered implementation. All workers are stopped; no competing writers or SDK processes may be started without rechecking.

## Finalization results

- Implementation commit: `221510f1a817366f813cbbfe557bf7f6f5b7b8a1` on `main`, pushed to `origin/main`; remote `refs/heads/main` readback matched. This handoff is committed in the following documentation-only commit.
- Full Desktop Release build passed: `desktop-final.log`, 0 errors, 1 existing WebRtcVadSharp AnyCPU-to-x64 warning. Actual artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Release/net10.0/Haven.dll`.
- Standalone Boards Release build passed: `standalone-final.log`, 0 errors, the same existing warning. Actual artifact: `9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll`.
- Final verification receipt with test counts, failed test names and log hashes: `evidence/boards-spaces-sprint-2026-10-31/verification.json`.
- Fresh native interaction on the final sprint build remains pending because the owner requested immediate wrap and push. Headless production-control tests passed as recorded above; neither those tests nor compilation establish complete native acceptance. No installed owner testing copy was updated.
- All workers stopped; no further source edits planned. Preserve local scratch proposals separately from the verified implementation. Overall Boards/Spaces completion remains open.
