# R01 third poison path — exact real-runtime cause (measured 2026-10-18 22:34–22:37 BST)

Implementation evidence. Acceptance remains NOT_RUN and belongs to independent review.

## Run identity
- Artifact: `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven` built from
  `opencode/v2-pc-sequential-20261018` at HEAD `889fbaba` with
  `/home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1` → `BUILD_EXIT:0`,
  `0 Error(s)` (`r01-shutdown-diag-build-1.log`).
- Disposable profile (worker-owned): `/tmp/opencode/r04-shutdown-diag-20261018T2234Z`
  (`HAVEN_DATA_DIR` + `0700` XDG roots, `umask 077`, `DOTNET_ROOT=/home/jacob/.local/share/dotnet`).
- Process/window: PID `2318427`, window `0x180001b`, frame `9to1`.
- Journey: address entry `9to1://boards` → `Go to address` → truthful refusal
  (`inspect-refusal.json`: "Browse retained the original failed operation: This app does not have a
  registered native surface in the current host.") → one normal `WM_DELETE_WINDOW` close.
- Result: process still alive at 40s and at 201s after a second normal close. Cleaned up with SIGTERM
  as worker-owned cleanup, recorded as cleanup, never as a passing product shutdown.
- Owner preview PID `1885357` / window `0x140001b` / profile `/tmp/opencode/haven-data-4` untouched.

## What the new diagnostic revealed
Before this commit the apphost produced **no shutdown diagnostic at all**. After it, the retained
original is written to the process error stream (`run-1.log`, sha256
`89b966fc2b2b1a4b1936d9950dea9c1d17342df2becacab484f7f1fa113e139e`):

`[9to1 original shutdown] Haven.Desktop.Services.NativeAppContentRegistry+NativeAppContentAdmissionRefusal: This app does not have a registered native surface in the current host.`

Relevant frames, innermost first:

1. `NativeAppContentRegistry.CreateSession` (NativeAppContentRegistry.cs:57)
2. `BrowseChrome.InitializeAppOwnerAsync` (:71) → `AttachAppContentAsync` (:112)
3. `BrowseChrome.AddRuntimeAsync` (BrowseChrome.cs:781) → `OpenContentCoreAsync` (:477)
4. `BrowseChrome.RetainContentTransition` (BrowseChrome.ClosePreparation.cs:81)
5. `BrowseNativeWorkspace.Operation` (:312) → `RunAsync` (:226, :299)
6. `CuiControlLoader.DispatchActionAsync` (:595, :602) + `ThrowRetainedFailures` (:356)
7. **`CuiSceneHost.JoinOriginalTaskAsync` (:751) → `ThrowOriginal` (:775) → `ThrowOriginalClose` (:746)
   → `CloseOriginalCoreAsync` (:301)**
8. `App.JoinOriginalAppTaskAsync` → `UnifiedWorkspaceHost.CloseCoreAsync` (:516, :532)
9. `MainWindow.CleanupOriginalWindowResourcesAsync` / `JoinOriginalWindowRetirementAsync`
10. `DesktopOriginalShutdownSequence.DrainActualRequiredOriginalsAsync` → `RunOriginalShutdownAsync`
11. `App.DeliverOriginalDesktopShutdownAsync` (:486) → failure retained, `desktop.TryShutdown()` never
    reached, window stays mapped, process lives.

## Conclusion: a third path, above the two already repaired
The two paths named in SHUTDOWN-ASSIGNMENT items 4 and 5 (`BrowseChrome` retained sources and
`BrowseNativeWorkspace` command history) are already repaired and their focused regressions pass.
The real product still cannot close because **`CuiSceneHost` re-joins the CUI loader's retained
action originals during its own close** and adds every fault cause to its close errors
(`JoinOriginalTaskAsync` → `AddOriginal` → `ThrowOriginalClose`). The typed admission refusal, which
acquired no session and no effect, therefore becomes a retirement obligation at the framework layer
that neither Browse fix can reach.

`CuiControlLoader.RetainActionFailure` (:615) only records the user-visible `CUIA_FAILED` status; the
faulted action *task* itself stays in the scene host's publication ledger and is re-thrown at close.

## Designed repair (not yet implemented — next executable step)
CUI has no typed way for an action owner to settle a conclusively effect-free failure. Per AGENTS.md
this is a framework conformance gap to implement, not permission for a parallel path:

1. Add a narrow typed CUI contract (for example `ICuiSettledActionFailure`) that an action owner
   implements only with attached proof that the operation acquired no owner, session or unresolved
   effect. `CuiSceneHost.JoinOriginalTaskAsync`/`ThrowOriginalClose` must keep the failure in the
   diagnostics ledger but must not count it as an outstanding retirement duty.
2. Have `NativeAppContentAdmissionRefusal` (already a non-forgeable typed refusal with the exact
   admission + propagation-lineage proof from `e1d02a7d`) satisfy that contract, and prove the
   lineage is re-validated rather than inferred from exception text.
3. Negative coverage stays binding: real acquired-owner initialization failure, failed `DisposeAsync`,
   late acquisition during cancellation, contract mismatch and permission revocation must still block
   a clean receipt. Arbitrary code implementing the marker without the typed proof must not be
   honoured.
4. Required gates afterwards: failing-before/passing-after regression at the scene-host level, then
   the real apphost journey refusal → normal close exits → relaunch same disposable profile restores
   tab IDs/selection.

## Verified resume anchors (re-checked 22:47 BST against current HEAD `7fee4501`)
These exact lines were re-read so the next session does not re-derive them:
- `framework/CUI/Runtime/CuiSceneHost.cs:301` `ThrowOriginalClose(errors, originals);` — the close
  site that surfaces the retained action failure.
- `framework/CUI/Runtime/CuiSceneHost.cs:751` `try { await original.ConfigureAwait(false); }` inside
  `JoinOriginalTaskAsync`, which adds every fault cause of a retained action original to `errors`.
- `framework/CUI/Runtime/CuiSceneHost.cs:746` the sole-source-withdrawal cancellation carve-out and
  `:775` `ThrowOriginal` — the existing narrow precedent for a typed exception to close accounting.
  Any new settled-failure honour must sit beside this, not replace it.
- `framework/CUI/Runtime/CuiControlLoader.cs:595` `await actualDispatcher;`, `:602`
  `ThrowRetainedFailures(failures);`, `:356` the `ExceptionDispatchInfo` rethrow, and `:615`
  `RetainActionFailure` which only records the user-visible `CUIA_FAILED` status — it does **not**
  retire the faulted task, which is why the scene host still replays it.
- `shared/src/Haven.Desktop/Services/NativeAppContentRegistry.cs:49` `CreateSession` — the typed
  refusal site; the registration/host-current/namespace/Home-profile security checks above the throw
  must stay intact.

First executable action after the pause is lifted: add a scene-host level failing-before regression
that publishes a Browse surface, drives one verified effect-free admission refusal, then closes the
`CuiSceneHost` and asserts the close completes while the refusal stays inspectable in the loader
diagnostics. Capture it failing at `7fee4501` before changing any framework source, then implement
the typed settled-failure contract and prove passing-after, then repeat the real apphost
refusal → normal close exits → relaunch same profile restores tab IDs/selection journey.

## Also still open in R01

- User-visible CUI recovery experience for a genuine unresolved close obligation
  (SHUTDOWN-ASSIGNMENT item 7 / spec 1791): still absent; only log diagnosability was added.
- Unsaved-draft close-preparation journey on the real UI route: NOT RUN.
