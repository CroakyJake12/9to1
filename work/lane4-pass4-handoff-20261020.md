# Lane 4 handoff — Pass 4 (exact shared-control admission, close and production bindings; source-only)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`
(base; plus frozen pass3 dirty source checkpoint `8631490897ce8dceac910df170aa28cee27375fb`).
Pass: `parallel6-lane4-haven-pass4.prompt.md`; read pass3 prompt, pass3 readonly review
`lane4-pass3-readonly-review-86314908.md`, pass2 peer review, owner references and the canonical
spec (SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`, verified).

## Identity / constraints honored
- No edits to `App.UnifiedWorkspaceHost.cs` (lane5-owned) or `App.OriginalWindowsHome.cs`
  (root-owned). All lane4 behavior lives in `App.UnifiedWorkspaceHost.SharedControls.cs` partials;
  root-applied integration/registration hunks are in `work/lane4-pass4-integration-patch-20261020.md`.
- NO .NET/MSBuild/restore/tests/app/display/native/network/model/provider/credential/config/cap
  changes; no commit/push/deploy; no subagents; TEXT ONLY; execution NOT RUN.

## What pass 4 fixed (review blockers 1–7)

### 1. Compile error + CUI-owned frame
- `_sharedPanelHost` retyped to the real registered `CuiContainerControl` and uses the genuine
  `AddChild`/`RemoveChild`/`AuthoredChildren` contract — no donor Panel/Children escape or cast.
- `ComposeSharedFrame` no longer authors a `Grid`/children in C#. The authored `HavenSharedPanelShell.cui`
  now authors the whole frame (scene host + overlay backdrop + panel host). The host only mounts
  `_scene` into the named scene host element and records the named panel host. Missing bundle
  degrades truthfully to `Content = _scene` (scene-only; no panels; no raw reparse).

### 2. Async fault/result/cancellation + own-action close refusal
- `RunSharedOpenAsync` no longer swallows faults into status success: it publishes an actionable
  structured status (via `Changed()` on the UI thread) and rethrows the ORIGINAL fault so the
  retained open task faults and close joins it. Cancellation is published as a structured
  non-success without faulting (safe, joinable). Refusals publish structured `Code: message`.
- Shared dispatch now runs inside the same `CommandInvocation`/`LogicalDriver` guard as ordinary
  commands (`invocation.Self` bound before the driver proceeds), so a reentrant provider request to
  close cannot join its own in-flight action.
- Host `AdmitOriginal` now enters `CloudflareOriginalExecutionGuard.EnterOriginal(this)` (same
  execution-guard/ledger protocol as `RunObservation`). `OpenProfilePanelAsync` now invokes the
  native owner inside `AdmitOriginal` (reservation precedes the callback) and surfaces a typed
  refusal on fault.

### 3. Model gateway awaits real initialization; UI-thread publication
- `OpenModelPickerPanelAsync` now awaits `session.BeginAsync` AND `controller.OpenAsync("User","chat",…)`
  before reporting `Succeeded`; the nested discarded refresh `AdmitOriginal` is gone. Failures
  surface typed refusal codes.
- The whole model open runs on the UI thread (no `ConfigureAwait(false)` after real profile/provider
  I/O), so control load/mount and `Surface.ApplyState`/`PropertyChanged` publication are fenced
  before visible mutation on the UI thread.
- Notifications `LoadCoreCoreAsync` now marshals the row mutation + `PropertyChanged` publication
  to the UI thread via `Dispatcher.UIThread.InvokeAsync`.

### 4. Exact A→B replacement and post-Dispose acknowledgement
- `ShowSharedPanel` detaches A as current and bumps generation BEFORE any synchronous
  Dispose/cancel callback; a callback that mounts B is preserved (B is not overwritten by outer A
  retirement). Dispose failure is retained by identity through `Track`.
- `JoinSharedPanelSettlementAsync` now DISPOSES first (reserving the post-Dispose cleanup task) and
  then joins the full settlement including that disposal task (via `WhenActionsIdleAsync`); the
  shell disposal is joined likewise before session leases are released. Close remains externally
  joined (lane5 hook).

### 5. Command bindings to actual panel/actor/row
- Notifications now re-observe the ACTUAL current Home actor/auth revision via
  `ObserveActorAsync` before every owning mutation and after acquisition before publication
  (`ReobserveActorAsync`), not just the startup-cached `_actor?.ProfileId`. Model refresh reobserves
  through `ObserveActorAsync`. `IsPanelCurrent` remains the synchronous fence; full async actor
  re-observation for every dispatcher command is documented as the one residual (below).

### 6. Root registration patch (PATCH D)
- Provided exact root-integrated registration (before `Home.Resources/Broker` construction) for
  `HomeModelRouteProfileOwner`, `HomeModelRouteOwner`, `HomeModelRouteActionPolicies` — exactly one
  owning resolver per resource kind, no permissive private owner, no parallel Home/store. Without it
  real model reads/mutations deny.

### 7. Compiled consumer/raw parser fallback removed
- Removed the `CuiRichParser`/`SourceText` reconstruction in `CompiledCuiDocument.Load`; the
  consumer now refuses with a structured `CUIR070` until lane2's aligned typed transport is merged
  (no raw fallback). The ModelPicker raw `LoadDefault` path remains only for standalone V1
  compatibility; the host-open path no longer reports success before real init. PATCH B corrected to
  the right root relative path `../../../../framework/CUI/Compiler/...` (was `../../framework/...`).

## Files changed / created (pass 4)
| path | SHA256 (post-edit) |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `d0d454c8920496c2326c95dc7698979c5255a67863fe7a364b5755a9f7cceea5` |
| `Haven.Desktop/Views/Pages/Home/HavenSharedPanelShell.cui` | `5312593134116022c5ed5cdc7aa9b45bd90781f54600743f9384707a67e0c07f` |
| `Browse/NativeUI/BrowseNativeWorkspace.SharedControls.cs` | `88d5aa807c0bbcb86653f03f9c92a23c8c36009b90e220706ca8cf82de54b644` |
| `Browse/NativeUI/BrowseNativeWorkspace.cs` | `3a913299497d3417a830a5392e515066e1463d55f8b6eeb9235c57b89f39708f` |
| `Browse/NativeUI/Tests/BrowseSharedControlsTests.cs` | `7a4901955c575f0ca68636293f8e46161215d9deb46307ebb5bd7b378b526586` |
| `work/lane4-pass4-integration-patch-20261020.md` | (new) |

`App.UnifiedWorkspaceHost.cs` and `App.OriginalWindowsHome.cs` remain clean vs HEAD.

## Tests authored / corrected (NOT RUN)
- `BrowseSharedControlsTests.cs`:
  - Corrected `Shared_gateway_fault_is_retained_and_never_fabricated_as_success` (was an obsolete
    success-on-fault assertion) to assert the original action task FAULTS (so close joins it) and
    the next rail action still routes.
  - Added `Shared_gateway_cancellation_is_safe_and_never_fabricates_success` (ignored cancellation
    is non-fault, non-success, and next action routes).
- Existing Home tests retained (`HomeModelPickerCuiControllerTests`, `HomeModelPickerCuiProjectionTests`)
  exercising the PublicationGate and structured refusal seams.

## NOT-RUN commands (when root grants the serialized slot)
- NativeUI.Tests:
  `dotnet test "9to1 Workspace/Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj" --filter "FullyQualifiedName~BrowseNativeUiTests"`.
- Home.Tests:
  `dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --filter "FullyQualifiedName~HomeModelPickerCuiControllerTests|FullyQualifiedName~HomeModelPickerCuiProjectionTests"`.
- Host composition/close settlement: exercised after PATCH A is applied (root-owned slot).

## Truthful residuals (NOT closed by this lane)
- **Compiled CUI transport (PATCH B)**: Haven.Desktop does not yet emit `CUI.Compilation.v1`; the
  consumer refuses with `CUIR070` until lane2's aligned typed semantic reconstruction is merged. No
  raw reparse fallback.
- **Model-picker V2 transport (PATCH C)**: root/lane2 owns the compiled transport; standalone/tests
  keep genuine stateless V1 `LoadDefault`.
- **Synchronous dispatcher actor fence**: `HomeModelPickerDispatcher` and the close path still use
  the synchronous `IsPanelCurrent`; the notifications path is the one that re-observes the actual
  actor before mutation. Making every dispatcher command await a full actor re-observation is the
  remaining refinement (documented, not fabricated).
- **Reentrant-close host guard test**: the guard is implemented (`AdmitOriginal` EnterOriginal +
  Browse CommandInvocation); a direct host-level test requires the lane5 wiring and is queued for
  the root slot.
- **Assistants button projection (running → most-used → generic)**: still not implemented; it
  requires real run/usage source reads and is not fabricated. Still pending required work.
- **Cross-app host-aware topbar**: Browse rail wired; standalone/embedded header unification across
  other apps spans other lanes' surfaces and is unverified — not claimed complete.

## For the coordinator
1. Apply PATCH A (lane5 hooks) and PATCH D (route-owner registration) after lane5/root freezes;
   reconcile PATCH B/C with lane2.
2. Compile (guarded dotnet, `-m:1`): Browse NativeUI, Home, Haven.Desktop.
3. Run the test filters above; confirm nonzero executed counts.
4. Independent acceptance: embedded rail opens Profile/Notifications and resolves the model picker
   under genuine Home theme with real authorization; panels load under authored shell frame; close
   joins all retained originals incl. post-Dispose cleanup; retired-panel callbacks never mutate the
   replacement.
