# Haven Boards evidence ledger

## RC2 (revision editor: schema v3, styles, graphs, tables, undo)

- `.9to1board` schema v3 (backwards-compatible additive facet: run baseline/font/colors, paragraph props, styles catalog, table/cell styling + structure, image/divider/graph blocks, ink tools/pressure/selection/view, attachment size/sidecar): **IMPLEMENTED, BUILT, TESTED**.
- v0/v1/v2 → v3 in-memory upgrade (style seeding incl. `heading-1` compat, unknown-style repair to Paragraph, RC1 ink/table/checklist/canvas/attachment preservation; original bytes untouched until save): **IMPLEMENTED, TESTED**.
- Custom + built-in style system (8 built-ins; create/edit/duplicate/delete with fallback; resolver precedence direct-over-style; document-local persistence): **IMPLEMENTED, BUILT, TESTED**.
- Graph blocks (deterministic local evaluator ported from in-repo GenUI plot parser + implicit multiplication + `y=` handling + domain restrictions; expressions/viewport/grid/points persist as data): **IMPLEMENTED, BUILT, TESTED**.
- Tables (insert/delete rows/columns, column widths, per-cell alignment/background/foreground/formatting, table borders/corners/alternating style, formatted cell runs): **IMPLEMENTED, BUILT, TESTED**. Merge/split cells: **DEFERRED** (rectangularity invariant risk).
- Dynamic checklist editing (arbitrary item counts, add/remove/nest/format, Enter/Backspace behaviors): **IMPLEMENTED, TESTED**.
- Images (insert/render/resize/align/alt/replace/remove) and dividers (thickness/style/color): **IMPLEMENTED, TESTED**.
- Attachments: embed ≤24 MB, portable sibling `.files/` sidecar beyond that (relative resolution, honest Missing), 512 MB cap: **IMPLEMENTED, TESTED** (6 MB embed + 26 MB sidecar/move/missing).
- Session undo/redo (bounded history, autosave persists undone state): **IMPLEMENTED, TESTED**.
- Rnote engine linkage: **NOT LINKED** (Rust PoC has no .NET surface/binary); ink model is Rnote-shaped (pen/highlighter/eraser/selector tools, per-point pressure incl. pointer capture, selection, eraser hit-test, pan/zoom view, history undo) with RC1 stroke migration: **IMPLEMENTED, TESTED**.
- CUI editor exposes all of the above via dynamic block rendering, Add menu with styles, style editor, table/graph panels, keyboard shortcuts: **IMPLEMENTED, BUILT, TESTED, LAUNCHED**.
- Subject acceptance (Maths/Law/CS save→reopen), update-safety (v2→v3), perf sanity (8 sections/50 pages): **TESTED**.
- Release build + Release launch: **(final numbers below)**.
- Automated totals: contract **86/86** (Debug) + **85/85** (Release), HUI **34/34**, app **22/22** (Debug) + **22/22** (Release).
- Release build: **succeeded, 0 errors** (7 pre-existing vendored-Avalonia warnings; 0 from Boards/CUI code; `/p:TreatWarningsAsErrors=false` invocation-scoped, no repo change).
- Release launch ×3 (create, spaced-path create, existing-board open): **diagnostics 0, window rendered, alive, valid schema-v3 files written**.

## RC1 (9-1 Boards, .9to1board schema v2 + CUI app)

- `.9to1board` schema v2 envelope (`format 9to1.board`, stable `documentId`, `createdUtc`/`modifiedUtc`, task-snapshot facet + rich-notes facet): **IMPLEMENTED, BUILT, TESTED**.
- Atomic same-directory temp write + byte re-validation + `File.Replace` promotion + `.bak` preservation + corrupt-primary quarantine: **IMPLEMENTED, TESTED**.
- Schema v0/v1 in-memory migration (original bytes untouched until save), legacy raw-`.json` import (source preserved), future-version loud rejection: **IMPLEMENTED, TESTED**.
- Rich-notes payload (sections/pages/paragraphs/headings/runs/bold/italic/underline/strikethrough/lists/checklists/tables/ink-strokes/canvas-objects/embedded attachments): **IMPLEMENTED, BUILT, TESTED**.
- `RichBoardSession` (validated mutations, debounced autosave, monotonic revision, save-gated "Saved" status, flush on dispose, Save As preserving identity): **IMPLEMENTED, TESTED**.
- CUI runtime extensions (action→Tag wiring, TextBox font/accepts-return/wrap, Canvas attached geometry, decorations): **IMPLEMENTED, TESTED**.
- Executable CUI Boards app (`9to1 Workspace/Boards/app`, `dotnet build`, `Boards.exe [board-path]`, Ctrl+S, save-on-close): **IMPLEMENTED, BUILT, LAUNCHED** — window rendered, `Boards.cui` 0 diagnostics, created valid schema-v2 file on first run.
- Lossless editor merge (`ContractSessionAdapter`: delta-only write-back, multi-run/table/canvas/attachment preservation): **IMPLEMENTED, TESTED**.
- Contract suite: **63/63 passed**. HUI suite: **34/34 passed**. App suite: **9/9 passed**. Foundation static checks: **passed**.
- Realistic A-Level Maths board (3 sections, 4 pages, headings, multiline formatted text, checklist, table, ink, canvas note) through save → dispose → reopen → edit → autosave → reopen → copy: **TESTED**.
- Pointer freeform-card drag in HUI scenes: **NOT IMPLEMENTED / NOT CLAIMED** (keyboard nudge covered). Rnote engine reuse: **NOT WIRED** (ink is native stroke points). Attachments >5MB embed: **REJECTED WITH MESSAGE** (sidecar path deferred).

## Prior ledger (boards-appflowy-foundation migration)

This file records what has actually been executed for the `boards-appflowy-foundation` migration. It intentionally distinguishes source inspection, implementation, build/test evidence, and runtime proof.

## Upstream and donor

- AppFlowy Board repository/source/API: **INSPECTED**.
- AppFlowy Board licence boundary: **INSPECTED**; this slice selects the upstream MPL-2.0 option.
- AppFlowy Board revision: **PINNED** to `804d7898ac0becabf73e45527baf5d5c573cd6bb`.
- The Flutter proof lockfile resolves `appflowy_board` to that same exact revision: **PINNED / RESOLVED**.
- CakeAI Boards donor implementation: **INSPECTED**.
- CakeAI HUI architecture/API used by the Boards projections: **INSPECTED**.
- Real HUI donor compatibility revision: **PINNED** to CakeAI commit `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb` in a separate sparse, detached checkout used only for compatibility testing.
- AppFlowy source copied into CakeOS-owned source files: **NO**.
- External AppFlowy fork created: **NO**.

## CakeOS implementation

- Neutral board snapshot/command/reducer contract: **IMPLEMENTED, BUILT, TESTED**.
- AppFlowy callback-to-neutral-command adapter: **IMPLEMENTED, TESTED**.
- JSON local-first store with durable temp write and backup recovery: **IMPLEMENTED, BUILT, TESTED**.
- HUI structured-board projection: **IMPLEMENTED, BUILT, TESTED AGAINST REAL PINNED HUI DONOR API**.
- HUI disabled controls synchronize visual property, accessibility state, and `HavenElementState.Disabled`: **IMPLEMENTED, TESTED**.
- Composed HUI application session (`HavenBoardsHuiSession`) binding scene commands to durable store writes: **IMPLEMENTED, BUILT, TESTED**.
- Flutter/AppFlowy bounded proof harness: **IMPLEMENTED, ANALYZED, TESTED**.
- Explicit non-drag group/card movement controls: **IMPLEMENTED, TESTED IN FLUTTER; HUI KEYBOARD COMMAND PATH TESTED**.
- Typed hierarchy parent/unparent command plus global snapshot validation: **IMPLEMENTED, BUILT, TESTED**.
- Hierarchy validation rejects duplicate card IDs, missing parents, self-parenting, existing cycles, and proposed cycles before publication/rendering: **IMPLEMENTED, TESTED**.
- Cross-lane hierarchy and nested-card HUI presentation survive card moves and durable reopen: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Content-addressed local attachment blob store plus typed attachment metadata commands: **IMPLEMENTED, BUILT, TESTED**.
- Attachment deduplication, bounded imports, display-name path isolation, malformed-reference rejection, reparse/link rejection, and existing-blob digest verification: **IMPLEMENTED, TESTED**.
- Attachment blob + board metadata + HUI attachment-count + reopen/readback composition: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Content-blob deletion/reference counting: **DEFERRED** until backup-aware ownership/reference tracking can make deletion safe.
- Renderer-independent freeform layout with bounded finite geometry and typed set/remove-frame commands: **IMPLEMENTED, BUILT, TESTED**.
- HUI-native freeform projection using the real `Haven.UI.Components.Canvas` primitive and `Left`/`Top` geometry: **IMPLEMENTED, BUILT, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Freeform cards retain the same card identities, hierarchy, attachment metadata, and durable snapshot as the structured projection: **IMPLEMENTED, TESTED**.
- Keyboard-accessible freeform nudge controls emit typed `SetFreeformCardFrameCommand` mutations and disable safely at coordinate bounds: **IMPLEMENTED, TESTED**.
- Freeform HUI keyboard nudge -> shared session queue -> durable save -> dispose -> reopen -> exact frame restoration: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Pointer drag for freeform cards: **NOT IMPLEMENTED / NOT CLAIMED** in this slice.
- Renderer/provider-independent generative plan allowlist with bounded batches/fields and frozen reviewed commands: **IMPLEMENTED, BUILT, TESTED**.
- Generative coordinator retains detached prepared/applied state by opaque plan ID, revalidates before apply, rejects stale plans, applies a valid multi-command plan in one durable save, and supports version-checked one-shot undo: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Explicit HUI generated-change review scene and review flow with keyboard-accessible Apply/Cancel, no store/provider capability in the scene, stale-preview fail-closed behavior, and undo through the coordinator-owned checkpoint: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Model/provider invocation or prompt-to-command generation: **NOT IMPLEMENTED**; the tested boundary starts from already-typed proposed commands.
- Neutral structural collaboration batch boundary with bounded/frozen commands, safe IDs, exact base versions, and attachment-command exclusion: **IMPLEMENTED, BUILT, TESTED**.
- Collaboration permissions default to deny; owner-only first policy is implemented as an explicit provider seam: **IMPLEMENTED, TESTED**.
- Default sync adapter is explicitly disabled, returns no inbound data, and never claims remote delivery: **IMPLEMENTED, TESTED**.
- Inbound collaboration coordinator revalidates public DTOs, enforces board/version identity, authorises each command, applies through the same atomic durable session path, and retains bounded mutation-ID replay protection: **IMPLEMENTED, TESTED AGAINST REAL PINNED HUI DONOR API**.
- Realtime/network collaboration transport, presence, remote attachment transfer, and CRDT merge: **DEFERRED / NOT IMPLEMENTED**.

## Executed on approved desktop

Approved host: `DESKTOP-7CHJ9S6`.

The original isolated CakeOS checkout is at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeOS`

The approved/current branch is `boards-appflowy-foundation`.

During the freeform validation pass this checkout was found to contain **44 local changes with staged deletions**, including most of `apps/Boards`, plus a staged reversal of the `.dart_tool` ignore. Those changes were not reset, stashed, committed, or otherwise altered by this worker.

To avoid clobbering that state, a second clean validation-only checkout was created at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeOS-Boards-Proof`

It tracks only `boards-appflowy-foundation` and is used for current remote-branch build/test evidence.

An isolated sparse CakeAI donor checkout used only for real-HUI compatibility testing exists at:

`C:\Users\Jacob\OneDrive\Personal Files\Development\CakeAI-HUI-Proof`

It is detached at commit `7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb` and sparse-limited to `src/Haven.UI`. The dirty legacy Haven AI checkout was not used or modified.

Executed positive evidence:

1. Strengthened `apps/Boards/tests/verify-boards-foundation.ps1` through Windows PowerShell on the hierarchy-hardened head: **PASSED, exit 0**.
2. `dotnet build apps/Boards/contract/CakeOS.Apps.Boards.Contract.csproj --configuration Debug`: **PASSED, exit 0**.
3. Neutral reducer/store regression `dotnet test apps/Boards/tests/CakeOS.Apps.Boards.Tests.csproj --configuration Debug`: **PASSED, exit 0** after attachment and hierarchy invariant tests were added.
4. A portable Flutter SDK was cloned under the Development root; no system-wide Flutter installation was made.
5. `flutter pub get` in `apps/Boards/appflowy_poc`: **PASSED, exit 0**. The exact pinned AppFlowy Board dependency resolved successfully.
6. Latest `flutter analyze` after the deterministic persistence/test refactor: **PASSED, exit 0, no diagnostics**.
7. Current-head isolated AppFlowy controller reorder test: **PASSED, exit 0**.
8. Deterministic AppFlowy widget/accessibility test with filesystem persistence explicitly disabled: **PASSED, exit 0**.
9. Complete corrected `flutter test` suite: **PASSED, exit 0**.
10. `apps/Boards/tests/verify-hui-compatibility.ps1` against the real pinned donor `src/Haven.UI/Haven.UI.csproj`: **PASSED, exit 0**. This compiled the CakeOS Boards HUI project against the actual donor HUI project and ran the HUI scene tests.
11. Composed local-first HUI lifecycle tests: **PASSED, exit 0**. They cover direct command/save/dispose/reopen and a keyboard-originated HUI move command followed by queue flush, disposal, fresh store/session reopen, and verification of the moved card state.
12. Content-addressed attachment contract/store tests: **PASSED, exit 0**. They cover deduplication, display-name path escape attempts, oversize cleanup, unsafe IDs/malformed references, byte readback, and tampered existing-blob rejection.
13. Real-HUI compatibility after attachment integration: **PASSED, exit 0**. The composed test imports real bytes, attaches returned metadata to a card, observes `1 attachment` in HUI, disposes/reopens the board, observes the same HUI metadata again, and reopens identical bytes from the blob store.
14. Hierarchy neutral tests: **PASSED, exit 0**. They cover set/clear parent, cross-lane parents, missing/self-parent rejection, multi-card cycle rejection, parent preservation across lane moves, and malformed snapshot rejection.
15. Real-HUI compatibility after hierarchy integration: **PASSED, exit 0**. The composed test parents `card-3` to `card-1`, moves the nested card across lanes, verifies the `Nested card` HUI marker, disposes/reopens, and verifies the parent/link/marker again. A separate test persists a missing-parent graph and proves session open rejects it before render.
16. Freeform neutral tests on the clean `CakeOS-Boards-Proof` checkout: after correcting a test-only collection equality assertion, `dotnet test apps/Boards/tests/CakeOS.Apps.Boards.Tests.csproj --configuration Debug`: **PASSED, exit 0**. Coverage includes frame add/replace/remove, unsafe geometry rejection, missing/duplicate frame validation, structured move/hierarchy preservation, and JSON round-trip.
17. Real-HUI compatibility on the clean proof checkout after adding `HavenBoardsFreeformHuiScene`: **PASSED, exit 0**. Coverage includes exact HUI `Canvas` geometry, keyboard nudge command emission, deterministic fallback-to-persisted frame conversion, coordinate-bound disablement, shared-session save, disposal, fresh reopen, and exact frame restoration.
18. The freeform-hardened static foundation gate on the clean proof checkout: **PASSED, exit 0**.
19. Generative planner regression suite on the clean proof checkout: **PASSED, exit 0**. It covers allowed structural preview creation, attachment-command rejection, generated ID/title bounds, empty/oversized batch rejection, and post-review command collection immutability.
20. Generative coordinator real-HUI suite on the clean proof checkout: **PASSED, exit 0**. It covers detached preview tampering, stale plan rejection with zero generated persistence, invalid batch rollback with zero saves, valid multi-command apply with one save, one-shot private-checkpoint undo, stale undo rejection, and reopen after undo.
21. Explicit generative review HUI suite against the pinned real HUI donor: **PASSED, exit 0**. It covers disabled review controls without a plan, keyboard Apply emitting only a plan ID, explicit Apply then undo, explicit Cancel with no mutation, and stale-review fail-closed behavior preserving newer user edits.
22. `verify-generative-board.ps1` is chained into `verify-hui-compatibility.ps1`; the chained gate plus full HUI suite: **PASSED, exit 0**. The gate requires bounded/frozen typed commands, private plan/checkpoint registries, opaque-ID apply/undo, persist-before-publish batches, HUI-only review actions, and matching adversarial tests.
23. Neutral collaboration contract suite on the clean proof checkout: **PASSED, exit 0**. It covers immutable structural batches, attachment-smuggling rejection, unsafe ID/oversized batch rejection, disabled adapter behavior, and exact owner-only permission behavior.
24. Inbound collaboration real-HUI suite: **PASSED, exit 0**. It covers default-deny with zero saves, authorised two-command structural batch with exactly one save and durable reopen, stale-base conflict with zero saves, caller-constructed attachment DTO revalidation/rejection, and mutation-ID replay rejection.
25. `verify-collaboration-boundary.ps1` is chained into `verify-hui-compatibility.ps1`; the combined generative + collaboration safety gates plus the full real-HUI Boards test project: **PASSED, exit 0**.
26. Repository hygiene inspection after Flutter testing identified only generated Flutter state. `.dart_tool` is explicitly ignored and `pubspec.lock` is committed for proof-harness reproducibility on the remote migration branch.

## Negative evidence retained

- The first Flutter widget-test revision used asynchronous filesystem restore/write from `initState` and did not terminate normally.
- Replacing `pumpAndSettle()` with bounded pumps alone did not fix that revision.
- The isolated bounded widget test job `c9bf4272-dcf7-4a70-a1ff-f33f00667e94` was terminated by the coordinator after the approved 300-second limit with `COMMANDTIMEOUT` and exit `-1`.
- The harness was then separated into normal persistence-on execution and deterministic persistence-off UI testing. The corrected deterministic widget test and complete corrected Flutter suite subsequently passed.
- Therefore the timeout is preserved as a harness-lifecycle failure that was fixed; it is not described as upstream AppFlowy runtime failure.
- The first neutral freeform test run on `CakeOS-Boards-Proof` failed only `Freeform_layout_round_trips_through_local_store` because the test compared a record containing an `IReadOnlyList` by reference after deserialization. The assertion was corrected to compare the frame sequence structurally, and the same suite then passed. This is retained as a test-defect/fix, not a persistence failure.
- Static review of the first generative implementation found two security-boundary defects before runtime promotion: reviewed commands were exposed through a castable raw array, and undo accepted a caller-supplied checkpoint record. Commands are now frozen; prepared/applied state is retained as detached coordinator-owned copies; apply/undo accept opaque IDs. The corrected boundary and adversarial tests subsequently passed.
- The original `CakeOS` validation checkout currently has 44 local changes with staged deletions. A pull was correctly refused rather than overwriting them. Current freeform/generative/collaboration evidence therefore comes from the separate clean `CakeOS-Boards-Proof` checkout.

## Not yet proven

The following must not be described as proven yet:

- HUI scene/session compilation against a CakeOS-owned shared HUI runtime after that runtime is permanently landed in CakeOS; current proof uses the exact real CakeAI donor HUI project through a fail-closed compatibility reference;
- approved Ubuntu VM execution;
- Linux package installation/runtime;
- packaged-process offline terminate/reopen on CakeOS/Ubuntu. The composed desktop component lifecycle is tested and contains no network dependency, but that is not the same as final packaged offline runtime proof;
- pointer drag interoperability for structured or freeform cards in final HUI rendering;
- HUI pan/zoom behavior for freeform boards in the final input host;
- assistive-technology runtime accessibility with a screen reader or other AT;
- backup-aware content-blob deletion/reference counting and GC policy;
- model/provider invocation that turns natural-language output into the already-tested typed generative proposal boundary;
- actual realtime collaboration transport, presence, remote attachment/blob transfer, reconnection, or CRDT/merge behavior. The current sync adapter is intentionally disabled;
- multi-user permission administration/UI beyond the tested fail-closed provider seam and owner-only first policy.

## Acceptance rule

Only promote evidence states after directly running the matching stage. Source presence is not build evidence; build success is not runtime proof; desktop component proof is not packaged-process or approved-VM/Linux proof; a tested sync boundary is not a tested network transport.

## 2026-09-23 — Seamless editor surface and shared Glow palette

- User clarified the page treatment: the editor should feel edge-to-edge and seamless, with fixed content margins rather than a fixed visible paper rectangle.
- `app/Boards.cui` now gives the editor column the remaining window space, removes the centered 800 px card/border/radius treatment, and preserves its former inner padding as a 40 px horizontal / 32 px vertical content inset. Page-level ink remains on the full document surface.
- Root cause of the narrow editor during the first layout pass was shared CUI Grid placement: `grid-column` / `grid-row` on a `Grid` control were interpreted as generated definitions instead of attached coordinates. The loader now handles standard attached coordinates consistently; the legacy `column` / `row` convenience attributes retain their definition-count behavior. A runtime regression test covers nested grids.
- Boards’ app-local slate Light/Dark palette bypassed the CUI surface palette and overwrote its resources. `BoardsTheme` now resolves the canonical Boards surface in CUI Glow for Bright/Dark appearance, and `ThemeApplier` applies the shared semantic and gradient resources. Source history identifies commit `89077af` (the Boards visual pass) as the change that introduced the local palette and stopped consuming shared surface tokens.
- Existing local boards now display `Loaded locally` instead of the misleading `Unsaved changes` state.
- Release Boards app suite: **40/40 passed**. CUI Runtime Release suite: **52/52 passed**. Boards foundation static gate: **passed**. Contract Release **86/86**, HUI Release **34/34**, and CUI markup **4/4** passed earlier in this run, before the final UI-only changes.
- Headless screenshots inspected for Maths Light/Dark, Law, and Computer Science; Maths also rendered at 1280×720, 1440×900, and 1920×1080. Automated assertions verify full editor viewport width, fixed content insets, no card treatment, and the Glow gradient resource.
- Native interactive validation remains **UNVERIFIED**: the Computer app inventory had no available native apps. Pointer/trail behavior and live Draw interaction have not been claimed as proven. No commit or merge was made.
