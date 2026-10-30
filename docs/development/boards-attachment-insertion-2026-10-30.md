# BF-37 Attachment destination — cycle 4 required flow

Pinned before source edits, 30 October 2026. Base main 56d97238ae7d205c809023d370d97bb3c94d2587.
Cycle 4 owns Boards attachment insertion, its existing local storage and host picker seams, and focused
tests. The current Goal and owner authority supersede historical branch/approval defaults.

Authority: canonical v2 Boards block insertion, stable identity, shared implementation, Edit/View,
persistence/history and explicit capability contracts; UX27 local Boards without required Files setup;
29 October inline destination request; checkpoint 6's remaining BF-37 capability queue. No new Files
identity, external-reference ownership or storage policy is invented from a selected pathname.

## User flow and invariants

1. In an editable local Board, open the actual inline plus. Choose Above, Below or Replace; its existing
   preview identifies the exact target and remains through the type stage. Choose Attachment once.
2. Open the host's existing single-file OS picker. Standalone and embedded local Boards use the same
   picker adapter. No SDK/native desktop launch is implied by a headless provider check. Externally saved
   sources retain their existing refusal of local attachment import, with an explicit capability reason.
3. Cancellation returns without a placeholder, document/revision/history change or source-file mutation.
   Missing/unreadable/oversized files report failure while retaining the original block. Capture page,
   target content and lifetime before the picker; leaving/returning, View mode, disposal or changed target
   cannot redirect the operation after the await. No document lock is held while the user chooses.
4. After selection, acquire the existing adapter merge gate; validate current target/capability, join
   pending edits, then prepare the real attachment under that gate. Existing storage copies files up to
   24 MiB into the board when the document read budget permits, and files up to 512 MiB into its
   content-addressed sibling sidecar directory. Repeated medium attachments must remain reopenable:
   choose a sidecar when embedding would exceed the codec budget, and check the exact candidate size
   inside the rollback-protected edit before recording history.
   Local drafts use their recovery location. Do not require connecting Files or manufacture a FileID.
5. Keep the merge gate through preparation and the single canonical mutation, serialising Save As with
   sidecar placement. Recheck target, document revision and lifetime after import and under the canonical
   gate. Insert the prepared attachment-bearing paragraph exactly at the chosen boundary, or replace
   only the intended block/list scope. Preserve positioned replacement geometry and unrelated content.
6. Close transient menu/preview state, select the resulting block, and render its actual attachment name
   and size. Save/reopen must resolve the selected bytes. One Undo restores the exact original content;
   Redo restores the same inserted IDs/payload. Do not delete attachment blobs needed by history or another
   reference. Late refusal may retain a completed immutable blob; only import-owned temporary files may
   be removed. This is distinct from claiming a cancelled picker changed canonical content.
7. Save As carries existing sidecars with the board. Check destination identity under the store's gate
   before copying payloads: refusing another board's path must leave its file and sidecars unchanged.

## Source and validation plan

Reuse BoardsViewModel.Insertion.cs and ContractSessionAdapter.Insertion.cs's typed target and atomic
splice. Pass a prepared canonical attachment into that edit; do not insert a placeholder and then call
AttachFileToBlockAsync (two edits). RichBoardBlock carries display metadata only. Inspect and repair
copy/hash consistency in RichBoardSession.ImportAttachmentAsync before relying on large-file import;
hash the bytes actually copied, preserve bounded size and owned-temp cleanup. Empty files are valid.

Program currently wires PickFileAsync; BoardsWritablePresentation.OpenLocalDocumentAsync does not.
Extract the existing OS adapter and wire the local embedded route with owning-surface/lifetime checks.
Do not grant import to the external-save presentation. Existing CUI destination/preview/type menu and
shared renderer stay the actual presentation path; no alternative demonstration UI.

Reproduce current relative Attachment refusal, then check real persisted Above/Below/Replace and
single undo/redo, empty/small/sidecar payloads, file/import failures, stale page/target/lifetime, list
boundaries and external-source refusal. Exercise mounted menu input and embedded local picker wiring;
capture changed runtime presentation during required checks. Compile each edit unit, run bounded serial
SDK checks with nonzero counts, retain every failure, review diff, commit/push/read back main.

Full independent acceptance remains PAUSED. Desktop is offline; native PC and ARM are NOT RUN, package
held and ARM PC-owned. ROOT-LIVE recovery is BLOCKED by the absent private projection. Ordinary Layer1
is separately permitted, PARTIAL and NOT REUSABLE. Usage remains UNKNOWN; observed ≤5% and the end of
31 October UTC remain the stop boundaries. Other BF-37, Design and Boards/Files gaps remain queued.

## Cycle 4 result and evidence

Relative Attachment insertion is now checked for local Boards. The existing inline plus/preview/type
menu reaches the shared picker; the real embedded-local presentation now supplies it too. External-save
sources retain their explicit local-import refusal. Prepare/import happens before one canonical edit,
with origin/content/lifetime revalidation, exact single undo/redo and retained immutable blobs on late
refusal. Whole-list and interior flow-list destinations preserve retained identity/order/formatting;
positioned whole-block replacement keeps geometry. Unsupported spatial edges remain refused.

Storage fixes found by these checks: empty embedded files resolve; sidecars follow ordinary Save As;
another board's identity is checked under the store gate before publishing sidecars; hashes describe
the bytes actually copied from one source stream. Embedded admission measures cumulative document and
actual JSON-escaped payload size, falling back to sidecars from the same prepared bytes. Final attachment
edits check the actual indented persisted envelope within the rollback boundary before recording history.
Corrupt existing blobs are preserved and refused; only the import's owned temporary file is removed.

Fresh final Release checks: **337/337 full Boards app tests**, **113/113 full Boards contract tests**,
zero failed/skipped. Standalone Boards build passed, with the existing WebRtcVadSharp AnyCPU→x64 native
DLL warning. All 11 changed source/test hashes match the final runs. The shared framework was not edited;
earlier framework suite evidence is historical, not a newly claimed run. All 21 attempts remain in
`boards-attachment-insertion-cycle4-2026-10-30.evidence.json`, including the actual RED cases and fixture,
compile and missing-assets failures. No terminated/invalid suite was counted as passing in this cycle.

Two final 1180×760 headless captures under `evidence/boards-attachment-insertion-2026-10-30/` show the actual
Replace preview/type selection and committed attachment name/size. The mounted test uses raw pointer
and keyboard input, the production local picker wiring and real save/reopen. Only the OS picker result
is substituted; the native OS dialog/application remains NOT RUN. The first diagnostic captures had an
unsynchronized fixture theme; final captures explicitly set the host's Glow/dark appearance and restore
it afterwards. Cancellation during the copy helper and real filesystem temp cleanup are separate checks,
not a claim of a native dialog cancellation test.

Remaining: relative database/Tabber/freeform, positioned Above/Below and interior positioned-list
replacement, lossless Design mappings, legacy toolbar conversion and native/ARM verification. The legacy
toolbar attachment route still uses its prior two-step create/attach edit; this milestone repairs the
relative destination route, and does not claim that legacy route is atomic. Next inspect and pin Tabber's
actual parent/pane ownership flow before enabling relative destinations; keep other dependent rows disabled.
BF-37 is CHECKED PARTIAL, full acceptance PAUSED, package HELD, usage UNKNOWN.
