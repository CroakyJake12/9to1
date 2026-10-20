# Lane 4 handoff — Pass 6 (bounded compiler-backed correction of four residuals; exact integration patch; source/tests/static only)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`
(base; plus frozen pass3 dirty source checkpoint `8631490897ce8dceac910df170aa28cee27375fb`).
Gate: frozen source_checkpoint `53ae17bcdce306e35c82206c2dbe5b2db18cad35`, parent
`e76f11aef85e2d45ff5e603c0f977b38f9dccf6d` — status `PREPARED_NOT_BUILT_NOT_TESTED`; NO `root-*`
logs present (NOT RUN, not PASS). Pass: `parallel16-lane4-haven-pass6.prompt.md`.

## Owner references re-read this handoff (sole product authority)
- `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json`;
  one owning app surface/engine across standalone and embedded; shared controls owned by Haven when
  embedded; no parallel divergent implementations.
- `docs/specification/owner-references/2026-10-20-haven-shared-header/owner-correction.json`;
  embedded app header exposes only search; model/notifications/profile on the persistent Browse/Haven rail
  and unified across apps; standalone exposes all four; preserve genuine Home identity/state.
- `docs/specification/owner-references/2026-10-20-spaces-svg-icons/owner-correction.json`;
  Assistants button = running assistant, else most-used, else generic; resolve from real authorised state,
  never fabricate running/usage.
- Canonical spec SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96` (verified).

## Identity / constraints honored
- No edits to `App.UnifiedWorkspaceHost.cs` (lane5-owned) or `App.OriginalWindowsHome.cs` (root-owned)
  — both remain clean vs HEAD (verified `git diff --stat` = no output). All lane4 host behavior lives in
  `App.UnifiedWorkspaceHost.SharedControls.cs` partials.
- NO .NET/MSBuild/restore/build/test/app/display/native/model/provider/network/config/credential/cap
  changes; no image/SVG image tooling; no model subagents; no commit/push/deploy/external writes;
  TEXT ONLY; execution NOT RUN without a root grant.
- Preserved `work/` checkpoints (pass3/4/5 + pass5 frozen bytes); no reset/clean/checkout/cherry-pick;
  no shared registry/status update.

## Four bounded residual corrections this pass (source/tests/static only)

### Defect 1 — retiring-root leak on reentry (`App.UnifiedWorkspaceHost.SharedControls.cs`)
`HideSharedPanel` now detaches and removes the exact captured A root through real
`CuiContainerControl.RemoveChild` bookkeeping BEFORE any disposal callback runs. If A.Dispose synchronously
mounts a replacement B, B's own `ShowSharedPanel` takes over `_sharedPanelLoader`/`_sharedPanelRoot` and
adds B's root, so A's exact-root removal never clears B and never bumps B's generation. Only when no
replacement mounts does the code bump the shared-panel generation and clear `HasOpenPanel`. No-loader/root
inconsistencies return with `HasOpenPanel=false` without clearing unrelated children. `ShowSharedPanel`'s
repaired incoming-generation reservation, no-invalid-shell-action-dispatcher, and exact post-Dispose ACK
are preserved.

### Defect 2 — authored search cannot write back (`HavenAppHeaderBindings.cs`)
`HavenAppHeaderBindings` now implements the typed `ICuiWritableBindingContext` (CUIR006 TwoWay contract). A
new `TrySetValue` accepts ONLY the `SearchText` path, rejects non-string/oversized values
(`MaxSearchLength = 512`), stores through the validated `SearchText` setter, and raises `PropertyChanged`;
unknown paths are refused (never silently accepted). No reflection/direct domain setters/new local flag.

### Defect 3 — Assistant read failures conflated with empty evidence (`AssistantRailIconProjection.cs`)
- Added `AssistantRailIconState.Cancelled`; `AssistantRailIconReadResult` now carries `Code`/`Message`
  provenance. Running/usage reads distinguish fault (`Unavailable` with `RunningReadFailed`/`UsageReadFailed`)
  from authorized-none (null → proceed) and cancellation (`Cancelled`) from empty config.
- Every returned running/most-used snapshot is validated against the fresh authorised configured catalogue by
  identity+revision (`TryMatchConfigured`); stale snapshots surface as `StaleRunningSnapshot`/`StaleUsageSnapshot`
  rather than being trusted. Caller cancellation is never downgraded to empty config or cached success.
- `HostAssistantRailIconSource.cs` unchanged (still honestly returns null for live axes — NOT accepted
  running/most-used behavior until real storage owners are composed).

### Defect 4 — notification/current-actor admission and original-task ordering (`App.UnifiedWorkspaceHost.SharedControls.cs`)
- `ReobserveActorAsync` now takes a full `AuthenticatedResourceActor` and returns `ActorAdmissionStatus
  {Current,Changed,Cancelled,Faulted}`; new `ActorAdmissionCode` maps status → structured code.
- `HomeModelPickerDispatcher` stores the full actor, re-checks `IsPanelCurrent` AFTER the admission await, and
  publishes structured refusals (StalePanel / ActorChanged / Cancelled / ActorReadFailed) instead of silent returns.
- `HavenNotificationsCuiBindings` takes the full actor, adds current-actor admission BEFORE reads
  (`LoadCoreCoreAsync`) and mutations, uses `RetainBeforeOwner` (reserves the original-task slot in the host
  ledger before the service callback), validates row membership (`IsMemberRow`), and publishes structured
  refusals. Committed results are preserved if retirement follows commit (no fake rollback).

## Files changed / created (pass 6) — SHA-256 (post-edit)
| path | SHA256 |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `77640d797b522c381a0c39f480adbadca467ab24c706358fe3d07903fb01e0d8` |
| `Haven.Desktop/HavenAppHeaderBindings.cs` | `71872f539ec77921d2d4cdbb3fef6a53bb552b68b2048c50c389d83448fdafac` |
| `Haven.Desktop/AssistantRailIconProjection.cs` | `5a4b25e6e1fe197ee5c3bf67993899143a63d931b3451b88047d093c0a4cb9e1` |
| `Haven.Desktop.Tests/AssistantRailIconProjectionTests.cs` | `6fb791fb4e9e0c30cf1975730eaa6405a230b3f84cc73212d45f1a512c829b81` |
| `Haven.Desktop.Tests/HavenAppHeaderBindingsTests.cs` | `15acad68b545300b9cc7dbe3246e40fe434b4f2a9030aeaf436704fecdb982ab` |
| `work/lane4-pass6-integration.patch` | (new; actual applicable unified diff, base = pass5 frozen source) |

`App.UnifiedWorkspaceHost.cs` and `App.OriginalWindowsHome.cs` remain clean vs HEAD (verified).

## Tests authored (appended to existing files; frozen originals NOT modified) — NOT RUN
- `Haven.Desktop.Tests/AssistantRailIconProjectionTests.cs` (+8 facts): running-read fault is unavailable and
  never downgraded to most-used; usage-read fault unavailable distinct from usage-not-recorded; catalogue
  cancellation is Cancelled; running cancellation is Cancelled and never selects most-used; stale running
  snapshot identity not published; stale usage snapshot revision not published; running snapshot published
  only within current configured catalogue. (Existing running-first/most-used/none/unavailable/denial/
  usage-not-recorded regressions preserved.)
- `Haven.Desktop.Tests/HavenAppHeaderBindingsTests.cs` (+4 facts): TwoWay write-back updates SearchText and
  raises PropertyChanged (asserts `ICuiWritableBindingContext`); non-string + oversized values refused; unknown
  paths refused. (Existing embedded/standalone/dispatch tests preserved.)

## NOT-RUN commands (when root grants the serialized slot)
- Root gate: `python3 run-gate.py {restore,build,test} --dotnet DOTNET` (status PREPARED_NOT_BUILT_NOT_TESTED;
  no `root-*` logs exist).
- `dotnet test ... --filter "FullyQualifiedName~AssistantRailIconProjectionTests|FullyQualifiedName~HavenAppHeaderBindingsTests"` (Haven.Desktop.Tests), plus Native/Home filters as pass4/5.
- Host composition/close settlement/header mount: exercised after PATCH A/E applied (root-owned slot).

## Truthful residuals (NOT closed by this lane)
- **Running/usage sources for the Assistants rail are not composed** → host projection returns typed
  `Unavailable`/`UsageNotRecorded`/`Cancelled`, never an invented most-used; the rail button consumer is not
  yet wired to a real composed source (missing domain read dependency; exact root-only seam in PATCH F).
- **Compiled CUI transport (PATCH B) BLOCKED (CUIR070)** + **CUIB001** (legacy `.hui` migration) → the header
  (`HavenAppHeader.cui`) and panels cannot be loaded/verified yet; TWOWay write-back is implemented at the typed
  contract level (`ICuiWritableBindingContext`), but the authored loader/input writeback regression and the
  submit path depend on the compiled transport landing.
- **Host-level show/hide/header mount tests** and the A.Dispose→B-mount production-owner regression: blocked on
  lane5/root wiring + transport; no builds/tests run per grant.

## For the coordinator
1. Apply `work/lane4-pass6-integration.patch` (actual applicable unified diff across the 5 source/test files;
   base = pass5 frozen source; verified it applies cleanly and reproduces this working tree byte-for-byte) plus
   PATCH A/D/E/F seam documentation carried forward from pass5 (header/assistant seams; lane5 host hooks; route
   owner registration).
2. Compile (guarded dotnet, `-m:1`): Browse NativeUI, Home, Haven.Desktop, Haven.Desktop.Tests; run t filters.
3. Accept: A.Dispose→B mount leaves A absent/B present/current; header TwoWay writes back with validation;
   Assistant read faults/cancellation/stale snapshots surface distinctly; no forbidden read/mutation after actor
   retirement; structured refusals published; committed results preserved after late retirement.
