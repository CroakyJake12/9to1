# Mesh sync MVP — pinned flow, measured state and edit-unit plan (8 November 2026)

Source identity at inspection: `main` @ `dc456398c551ac09e4eb96012730864755b92c9c` ("The Trash flow becomes reachable…", 5 Nov 2026).
Pre-existing uncommitted work preserved untouched: `eng/spec-coverage/coverage-summary.json` (modified), `.opencode/opencode.json` (modified), untracked `.opencode/skills/`, `.opencode/study/`, `study/`.
Machine: this PC (Linux x86-64, 32 cores, 62 GiB total). The resident Strata host running this conversation is PID 47059 at ~47 GiB RSS and was not stopped, restarted or reconfigured at any point.

## 1. Required MVP user flow, pinned before any code (owner-approved payload: the Boards document)

> One private LAN, two devices **A** and **B**, both running the same 9to1 desktop build from this checkout. A pairs with B by one-time fingerprint-bound pairing. The user explicitly enables a `mesh.sync` grant for the peer (default OFF). The user edits one **Boards** document on A and saves through the normal save/close lifecycle. Within one round trip B shows the converged board with the revision number, origin device and timestamp recorded in the Mesh view. B restarts the app and the content is still there. A concurrent edit on both ends produces a **visible conflict record with both divergent revisions preserved** — never a silent overwrite, never a wall-clock winner. On A, **Revoke** stops any further mutation reaching B.

Out of scope for this MVP, deliberately: Spaces `Local / Mesh / Cloud` conversation placement, Automations device-level action catalogue, Admin cross-account Business Mesh, shared compute pools, cloud relay, and any non-LAN transport. Those remain mandatory specification work, unchanged by this document.

## 2. Measured current state (labels are binding: source-present / unit-tested / NOT RUN / not observed)

| Capability | Evidence | Level |
|---|---|---|
| Mesh is composed into the desktop app | `shared/src/Haven.Desktop/App.axaml.cs:56 AddHavenMesh()`; `Interface/Shell/MainView.Mesh.cs:23` → `MeshPage(MeshPageViewModel(GetRequiredService<MeshCoordinator>()))` | source-present; **native rendering not observed in this session** |
| Pairing, identity, revocation | `Haven.Application/Mesh/MeshCoordinator.cs:100` `CreatePairingOfferAsync`, `:113` `PairAsync` (refuses silent identity replacement: "identity fingerprint changed… refused the pairing"), `:153` `RevokeAsync` (sets `Revoked` **and** `_transport.DisconnectAsync`) | source-present; unit-tested; **never observed between two devices** |
| TLS transport, pinned fingerprint, heartbeat | `Haven.Infrastructure/Mesh/SecureLanMeshTransport.cs` (TCP, per-session TLS, cert bound to device key, `PeerHello`/capability exchange, 15 s heartbeat, 1 MiB frame cap) | source-present; **NOT RUN natively** |
| LAN discovery | `LanMeshDiscoveryService.cs` — multicast `239.255.72.86:45886`, 5 s announcements, discovery data explicitly untrusted, pairing is the only trust path | source-present; **NOT RUN** |
| Push transfers | `MeshCoordinator.Transfers.cs` — `transfer.clipboard`, chunked `transfer.file.*`, receipts; `MeshFileTransferStore` | source-present; unit-tested; no two-device pass |
| Typed remote capability calls, both-ends gating | `MeshCoordinator.DeviceActions.cs`, `.RemoteRuntime.cs`, `.Tasks.cs`, `MeshDeviceActionProvider.cs` (unadvertised action → `Unsupported`) | source-present; unit-tested |
| Sync decision rules | `Haven.Core/Mesh/MeshSyncEngine.cs` — duplicate-safe on `OperationId`, `BaseRevision` successor → Apply, absent base → Conflict, `Revision <= current` → `IgnoreStale`, divergence → Conflict | **unit-tested only** |
| Sync application + journal | `MeshCoordinator.cs:232 ApplySyncAsync` → merge, 4,096-operation dedupe window, 256 persisted conflicts, saved through `MeshStateStore` | unit-tested; **zero production call sites** |
| **The gap** | No `sync.*` wire message exists anywhere (handled types are only `transfer.*`, `task.*`, `handoff.request`, `device.*`, `runtime.*`); `MeshSyncMutation` is **constructed nowhere** in the tree; `MeshResourceStore.TryApplyAsync` replaces whole payloads and its own docstring states "Feature stores require explicit adapters before Mesh may write them" | measured absence, this checkout |
| Namespace scoping | `MeshSyncMutation` fields = `OperationId, ResourceId, ResourceType, Kind, BaseRevision, Revision, ContentHash, OriginDeviceId, CreatedAt, Payload` — **no namespace/entitlement field** | canonical v2 MUST "Mesh data sharing/sync MUST be namespace/object scoped" is **unmet**; queued as `OPEN-2026-11-08-MESH-SYNC-WIRE-CONTRACT` in `docs/specification/open-decisions.json` |

Specification denominator, stated precisely: 25 requirement statements mention Mesh, 22 at MUST level (Haven 9, Accounts 5, Admin 4, Spaces 2, Automations 2, cross-cutting 1, packaging 1). Register totals for the pinned spec: 5,125 statements (3,982 MUST). None of that is claimed delivered here.

## 3. Environment gates cleared today (receipts)

1. **Toolchain absence, measured not assumed:** `global.json` pins `10.0.301` (`rollForward: latestFeature`, no prerelease) and the code is `net10.0` / `LangVersion 14`, but only `8.0.133` was installed (`/usr/lib/dotnet/sdk`) and `pwsh` was absent, so every registered owning check (`eng/9to1.ps1`) was unrunnable. `eng/development/checks.json` registers 5 checks (workflow, installer-delivery, cui, home, spaces) and **none covers `shared/src/**/Mesh` or `Haven.Infrastructure.Tests` / `Haven.Desktop.Tests`** — Mesh's 41 `[Fact]`/`[Theory]` methods across 1,577 test lines were outside the mapped suite.
2. **Owner decision on 8 Nov 2026 in this conversation:** install the pinned SDK and PowerShell locally, build narrow, and take the Boards document as the first sync payload.
3. **Installed user-scope only** (no sudo, nothing under `/usr`): `~/.dotnet` SDK `10.0.301` (exact pinned band, 235,086,718-byte archive) and `~/.local/bin/pwsh` → PowerShell `7.5.3`. Log: `/tmp/opencode/toolchain-install.log`. MemAvailable before and after both 3.0 GiB — the install did not disturb the resident host.
4. **Guard-clean compile proof**, `Haven.Core`: `result OK`, `exit_code 0`, 4.6 s, peak process-tree RSS **97 MiB**, minimum MemAvailable **2,347 MiB** against floor 1,100 MiB. Log `/tmp/opencode/build-core.log`.
5. **Narrow full-graph build measured:** `Haven.Core`, `NineToOne.CUI` framework projects, `Haven.Application`, `CakeOS.Apps.Boards.Contract`, `Haven.Infrastructure`, `Dulche.Runtime`/`Den`, `HavenOS.Home`, `Haven.Browser`, `Haven.PluginFixture`, `Haven.Infrastructure.Tests` all built in **71 s**, peak tree RSS **218 MiB**, min MemAvailable **1,519 MiB**. The memory fear was overstated by warm `obj/`; the guard holds for this graph.
6. Every build in this session ran through the committed `eng/development/guarded_lowmem_build.py` with `--floor-mib 1100`, `-m:1`, `MSBUILDDISABLENODEREUSE=1` and `-p:UseSharedCompilation=false`, one at a time (serial SDK). No second model, worker, coordinator or process supervisor was created.
7. **Boards app graph measured too:** `CakeOS.Apps.Boards.App.Tests` built and ran in **121.9 s**, peak process-tree RSS **326 MiB**, min MemAvailable **1,581 MiB**. So both the Mesh graph and the Boards graph are buildable on this PC under the guard with the resident host running. The memory gate is real but survivable; it is no longer the binding blocker.

## 4. Defect repaired in this session (the first Mesh unit)

`Haven.Infrastructure.Tests.MeshRegistrationTests.MeshServicesResolveWithoutCircularDependencyAndJoinProviderCollections` **failed on unmodified main**:

```
Failed! - Failed: 1, Passed: 49, Skipped: 0, Total: 50   (log /tmp/opencode/mesh-test-baseline.log)
System.AggregateException : Some services are not able to be constructed
  (Error while validating 'ServiceType: Haven.Infrastructure.DataLocalRecordSessionFactory …':
   Unable to resolve service for type 'Haven.Application.IAuthenticatedResourceActorSource' …)
```

Provenance: the test is from `54ad6deb` (20 Sep 2026); `DataLocalRecordSessionFactory` entered `AddHavenInfrastructure()` in `2fba86a6` (28 Oct 2026, "Repair local Boards drafts, shared record views, native routes and browser interaction") at `shared/src/Haven.Infrastructure/DI/ServiceCollectionExtensions.cs:50`. The test was protecting exactly this composition contract, and the 28 October change gave it an unsatisfied prerequisite.

Repair, in the first edit unit compiled after the change: the fixture now supplies the genuine Home owner pair that production already maps onto that interface (`HomeLocalDomainComposition.cs:122` maps `Profiles` → `IAuthenticatedResourceActorSource`): `FileHomeCoreStateStore` + `HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource())`, mirroring the established pattern in this same assembly and in ten-plus desktop tests. **No default actor source was added to production DI** — that would fail resource authorization open, which the specification forbids.

Result: `Passed! - Failed: 0, Passed: 50, Skipped: 0, Total: 50, Duration: 1 s`, guard-clean (`result OK`, 15.2 s incremental, peak tree RSS 162 MiB, min MemAvailable 2,104 MiB). Log `/tmp/opencode/mesh-test-after-di-fix.log`.

Scope of the claim: this is a **test-level** Mesh composition repair on `main`. It does **not** explain bug row A03 ("Present, Write and Mesh … fail to launch"), which remains unconfirmed against current main and unobserved natively in this session.

**Retained concern, deliberately not buried.** Two things the reviewer should decide rather than take from me:
1. The repair puts the missing dependency in the fixture. The alternative is a production-side move: relocate `DataLocalRecordSessionFactory` out of `AddHavenInfrastructure()` into a Home-scoped extension so `AddHavenInfrastructure` is self-contained again. I chose the fixture because it matches `HomeLocalDomainComposition.cs:122`, touches no authorization boundary, and keeps the 28 October Boards/records wiring in place; a reviewer may prefer the other.
2. Neither the Mesh suite nor the Boards `app.tests` suite is wired into `eng/development/checks.json` (5 registered checks: workflow, installer-delivery, cui, home, spaces). I ran them by hand. Until an owning check maps them, "50/50" and "20/20" are session receipts, not a gate that will catch the next regression of this exact kind.

## 5. Read-only Boards readiness audit (the pending item from `study/migration/first-session-task.md`)

1. **Current commit / identity:** `main` @ `dc456398…`; only read operations against Boards source; no compile of Boards app before this point, and the one Boards app build started in this session is recorded below.
2. **Inspected:** `Boards/contract/` (`HavenBoardDocumentCodec.cs` schema-version handling with `CurrentSchemaVersion` and migration; `JsonFileHavenBoardStore.cs` `GetDocumentPath`, `LoadAsync`, `LoadDocumentAsync`, `LoadDocumentAtPathAsync`, `SaveAsync`, `SaveDocumentAsync`, `SaveDocumentAtPathAsync`; `RichBoardSession.cs` with `RichNotes.Version` monotonic per mutation, `_lastSavedRichVersion` dirty tracking, 4096-style autosave tick, undo history); `Boards/app.tests/BoardsLocalDraftTests.cs` (8 named behaviours, including `New_draft_is_durable_without_a_destination_and_reopens_in_the_same_profile`, `A_blocked_workspace_folder_still_keeps_the_board_as_a_recoverable_draft_and_says_why`, `Optional_save_as_outside_the_private_folder_retains_identity_and_recovery`, `A_board_title_cannot_write_outside_the_workspace_folder`); `BoardsSaveDurabilityRegression.cs`; `Boards/EVIDENCE.md`; the `UX27-BOARD-001…011` correction queue.
3. **Proven at source + named-test level (20/20 passing on today's candidate, run 8 Nov):** (a) account-free local draft durability and reopen **have** dedicated named tests (`New_draft_is_durable_without_a_destination_and_reopens_in_the_same_profile`, `A_blocked_workspace_folder_still_keeps_the_board_as_a_recoverable_draft_and_says_why`); (b) destination/Save-As has real API surface (`SaveDocumentAtPathAsync` / `LoadDocumentAtPathAsync`) plus a named Save-As test (`Optional_save_as_outside_the_private_folder_retains_identity_and_recovery`) and a containment test (`A_board_title_cannot_write_outside_the_workspace_folder`); (c) rich-content save/reopen has `RichNotes.Version` + autosave + `BoardsSaveDurabilityRegression`.
4. **Unproven / blocked:** (d) **equations are not a first-party feature today** — the only `equation|mathml|katex|latex` matches in Boards are four mirrored donor files under `CUI/DonorMirror/appflowy/plugins/document/presentation/editor_plugins/inline_math_equation/` and `…/math_equation/`; no equation block exists in `app/`, `contract/` or `Source/`. (e) **no supported typed tool integration exposes Boards to the active model** — nothing in `shared/src` binds Boards to a tool/plugin contract; `eng/development/boards_export_html.py` is export-only. Per `study/migration/9to1-boards.md` I must not hand-write `.9to1board` JSON, so today the model cannot legitimately read or write a board at all. Historic `EVIDENCE.md` reports "Boards app suite 40/40" and "Native interactive validation remains UNVERIFIED … no available native apps" at an older revision; it also states native was never confirmed.
5. **Smallest blocking defect for "one student writes and reopens a Maths question":** not storage — it is **(e) the missing supported typed Board tool contract**, followed by **the absent first-party equation block (d)** for editable notation. Test-level draft durability already exists; what does not exist is any admitted path for the model to write a board and reopen it, plus on-screen native confirmation of the draft→close→reopen journey.

Boards app test run **completed in this session** (`CakeOS.Apps.Boards.App.Tests`, filter `BoardsLocalDraft|SaveDurability`): `result OK`, exit 0, 121.9 s, peak process-tree RSS **326 MiB**, min MemAvailable **1,581 MiB** at floor 1,100 MiB, and
`Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20, Duration: 767 ms`.
Log `/tmp/opencode/boards-draft-baseline.log`. This is the first recorded run of these named behaviours on this PC, and it is in-process against a real local file store — **not** an on-screen native draft→close→reopen journey, which remains unobserved.

## 6. Edit-unit plan for sync MVP (each unit compiled before the next; no batched patches)

| Unit | Change | Check after it |
|---|---|---|
| U0 | done — Mesh DI composition repair (§4) | `Haven.Infrastructure.Tests --filter ~Mesh` → 50/50 PASS |
| U1 | **DONE 8 Nov** — `sync.mutation` + `sync.ack` on the existing session, routed in `OnMessageReceived` into the shared apply path (`ApplyEvaluatedAsync`) | run `~Mesh`: **59/59 PASS** (was 50), two of them over two real `SecureLanMeshTransport` instances on loopback TLS |
| U2 | **DONE 8 Nov** — two explicit gates, `mesh-resource-sync-in` (receiver) and `mesh-resource-share-out` (sender), both OFF by default; the receiver re-checks that the mutation's origin matches the session that delivered it; grant buttons added to the Mesh device card | covered by the deny, spoofed-origin and revoke checks below; **the two `Haven.Desktop` button lines are not yet compiled** (see §9.3) |
| U3 | Namespace/entitlement field on `MeshSyncMutation` + enforcement in `MeshSyncEngine.Evaluate` (held pending the owner answer on `OPEN-2026-11-08-MESH-SYNC-WIRE-CONTRACT`; may land as a separate additive record to avoid pre-empting the public-shape decision) | new negative tests: personal namespace cannot reach team namespace |
| U4 | Boards owner adapter: one file-backed `.9to1board` document → `MeshSyncMutation` (identity from `BoardID`, revision from the document/`RichNotes.Version`, `ContentHash` from the serialized bytes), with Boards deciding merge/conflict — Mesh only proposes | `BoardsSaveDurabilityRegression` + new Mesh↔Boards round-trip test in `app.tests` |
| U5 | Per-destination queue/ack/retry states + offline rule, and restart replay from `MeshStateStore` | `MeshReliabilityTests` additions; kill-and-rejoin case |
| U6 | Mesh view: show sync state, revision, origin and the **conflict record with both revisions**; keep the existing grant UI honest | `Haven.Desktop.Tests/MeshHavenSceneTests` + on-screen capture |

Then, separately and not before: the native two-device pass (this PC + one more Linux device on the same LAN, checking multicast reachability and AP client isolation first), the A03 launch retest, and the ARM package path. None of those is scheduled as part of writing this code, and none of them has an evidence line yet.

## 7. Honest gate list — nothing here is accepted

- Implementation of U1–U6: **NOT RUN** (only U0 exists).
- Mesh on two devices, on screen: **not observed**, ever, in any retained evidence I could find. `docs/development/unified-host-tasks.md` is explicit for the closest prior Mesh observation: "Mesh identity adapter real encrypted write/read/reopen/delete and original descriptor/path refusal verified, **no network/fullMesh/nativeclose acceptance**."
- Namespace scoping MUST, offline queue MUST, revocation-during-sync MUST: **NOT RUN / unmet in contract**.
- Native desktop launch of the Mesh page, ARM packaging, install, and the seven quality criteria: **NOT RUN / PAUSED for full acceptance** per the standing owner instruction of 24 October 2026.
- A green unit test is not an on-screen experience. Everything above that distinction is stated in its own column, and no percentage of Mesh completeness is being claimed here.

## 9. U1 + U2 landed later the same day (8 Nov 2026), and the measured device-test blocker

### 9.1 What the code now does
- `Haven.Core/Mesh/MeshModels.cs`: `MeshSyncDecisionKind.Rejected` added (policy refusal kept separate from merge outcomes), plus new wire types `MeshSyncAcknowledgement`, `MeshSyncDelivery`, `MeshSyncDeliveryState` and `MeshSyncPublishResult`.
- New `Haven.Application/Mesh/MeshCoordinator.Sync.cs`: `PublishResourceChangeAsync` applies a locally authored change through the shared merge/journal path, then offers it **only** to peers this installation was told to share with (`mesh-resource-share-out`), one destination row per peer. Offline peer → `QueuedOffline` instead of silence; no answer inside 15 s → `Unconfirmed`; payload over 768 KiB refused locally with a reason pointing at the Mesh file-transfer path. Inbound `sync.mutation` requires a trusted session, that the mutation's `OriginDeviceId` equals the peer on that session, and the local `mesh-resource-sync-in` grant — then the existing `MeshSyncEngine` rules run and the receiver answers with its decision **and its own local revision**.
- `MeshCoordinator.ApplySyncAsync` was split so the trust gate stays with the caller that knows the source, and `ApplyEvaluatedAsync` is shared by the inbound peer route and the local originator. The public peer-facing gate is unchanged.
- No change to the public `MeshSyncMutation` shape, so the held namespace/entitlement decision stays open; the sharing boundary today is the two explicit grants.

### 9.2 Evidence (guard receipts, `-m:1`, no node reuse, floor 1,100 MiB)
| Run | Result |
|---|---|
| `~Mesh` before any change | 49/50 — one real composition failure (§4) |
| after the composition fix | 50/50 |
| U1 first compile | compiled clean; 7 of the 9 new checks failed because **my fixture** supplied an identity whose private secret was absent, and `InitialiseAsync` correctly refused to start ("identity metadata exists, but its private identity secret is missing") |
| U1 final `~Mesh` | **Passed: 59, Failed: 0, Skipped: 0**, 15.4 s incremental, peak process-tree RSS 146 MiB, min MemAvailable 1,301 MiB. Logs `/tmp/opencode/mesh-sync-unit1.log`, `-unit1b.log`, `-unit1c.log` |

The nine new checks: converge one board revision over **two real `SecureLanMeshTransport` instances on loopback TLS** with distinct device identities and pinned fingerprints; sender-only opt-in leaves the receiver applying nothing and answering `Rejected`; a mutation claiming a foreign origin is refused on the delivering session; an authorised mutation applies once and a replay answers `Duplicate`; an older revision answers `IgnoreStale` with the newer local content intact; divergent edits produce a conflict row holding both revisions and both content hashes while applying nothing; an offline peer stays `QueuedOffline` with nothing sent; an oversized payload is refused before any send; a revoked device receives nothing and its stale share grant is ignored.

Claim boundary: all of it is in-process. Real TLS over 127.0.0.1 is far stronger than a mock transport, but it is **not** two physical devices, not the app UI, and not a persisted restart replay.

### 9.3 The measured blocker for the on-screen test
The owning command for a runnable copy — guarded `Haven.Desktop.csproj` build into a new runtime directory, per `docs/development/live-preview.md` — was attempted at source `b5f9968d`: **MEM GUARD stop at 221 s when MemAvailable reached 967 MiB against the 1,100 MiB floor.** Peak build-tree RSS was only **388 MiB**, so the compiler is not the hog: MemAvailable sits near 2.5 GiB with the resident inference host at ~47 GiB, swap essentially exhausted and roughly 4.5 GiB held by desktop applications. Stated plainly:
- `Haven.Desktop` has never compiled with the sync changes, so the two new `GrantButton` lines are **unverified source**. I am not claiming they are broken either.
- No approved runtime directory exists, so there is no on-screen evidence and nothing to copy to a second device yet.
Ranked options: (a) close roughly 2 GiB of desktop applications and retry the build; (b) build `Haven.Desktop` on the device that will be the second peer (needs SDK `10.0.3xx` there); (c) deliberately lower the guard floor and record a relaxed-memory admission — that risks the OOM killer reaching this session's own inference host, so I will not do it without an explicit owner instruction.
Useful detail for the pair test: pairing consumes a **pasted offer containing an explicit endpoint**, so it does not depend on multicast discovery. A reachable TCP port is enough, which removes AP client-isolation as a hard blocker.

### 9.4 Deferred, with reasons — not silently skipped
- **Device classification.** `MeshCoordinator.cs:73-74`, `MeshCapabilitySource.cs:10` and `MeshInboundDeviceActionExecutor.cs:43` disagree with each other: two-way branches make a Linux installation present itself as Android for identity and capability advertisement, and as **Windows** for inbound device actions. This is the same item the unified host record leaves open ("Existing non-Windows Mesh Android/Phone classification remains open"). The intended repair is one shared resolver (`MeshLocalDevice.Platform` / `.DeviceClass`) in `Haven.Core`, used by all three. Deferred out of U1 because the inbound device-action path selects a native provider by platform, so changing it moves behaviour on a surface I cannot exercise in this session.
- **Durable queue and restart replay (U5).** `QueuedOffline` and `Unconfirmed` are in-memory only; a restart loses them. Needs a `MeshPersistentState` version bump plus migration.

### 9.5 What `U4` actually needs from Boards (measured, not assumed)
`JsonFileHavenBoardStore.cs:11` gives a clean canonical mapping: `HavenBoardDocument(Format "9to1.board", SchemaVersion 5, **DocumentId**, CreatedUtc, ModifiedUtc, Snapshot, RichNotes)` and `RichNotes.Version` already advances per mutation inside `RichBoardSession`. So the adapter is: `ResourceId = DocumentId`, `ResourceType = "9to1.board"`, `Revision = RichNotes.Version` (with an adapter-maintained counter when a board has no rich notes), `ContentHash = SHA-256` of the serialized document, and the apply side writes through the Boards store's own save path after the Boards owner accepts — Mesh never writes a board file behind the store's back, and the receiving side's conflict row keeps both revisions, matching `UX27-BOARD-008`'s "preserve both divergent revisions … never choose by wall-clock timestamp alone". One design decision remains: boards above the 768 KiB sync frame travel as a Mesh file transfer with only the reference synchronized.

## 10. Next executable actions (handoff state, 8 Nov 2026, after U1/U2)

1. **U4** Boards adapter exactly as mapped in §9.5, compiled with the narrow `Haven.Infrastructure.Tests` graph (71 s cold, 15 s incremental), plus a Boards save/reopen round-trip check in `CakeOS.Apps.Boards.App.Tests`.
2. **U5** durable queue and per-destination acknowledgement across an app restart (`MeshPersistentState` version bump plus migration), and the deferred `MeshLocalDevice` classification resolver from §9.4.
3. **U6** Mesh view work: the share/accept grant buttons now exist, but the delivery rows and conflict rows are still absent. Then compile `Haven.Desktop` — currently memory-blocked (§9.3) — so the two new UI lines stop being unverified source.
4. **Owner action** when reachable: free roughly 2 GiB, or name a second machine that can build, so an approved immutable runtime exists at all.
5. The earliest genuine on-screen pair test is **one PC with two development profiles** (two Mesh identities, pairing by pasted offer, sync visible on both). Then repeat the same journey on the supplied second device. **The extra device is only needed at that last step.**
6. Standing non-claims for everything in this document: no Mesh native acceptance, no two-physical-device evidence, no ARM packaging, no independent acceptance, and Mesh data sharing/sync namespace scoping is still unmet and open.
