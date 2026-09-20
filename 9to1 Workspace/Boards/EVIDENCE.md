# Haven Boards evidence ledger

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
