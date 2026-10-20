# Handoff — Lane 5 (Pass 4) — Dulche runtime SOURCE/TEST-only corrections

- **Date**: 2026-10-20
- **Session model**: Mercury 3 (`mercury/mercury-3`) via OpenCode harness
- **Worktree**: `/workspace/9to1-mercury-dulche`
- **Scope**: SOURCE + TEST only. **NOT RUN / NOT COMPILED** — no .NET/build/restore/test/app slot was granted for this pass. All changes are uncompiled edits; no runtime evidence is claimed.
- **Owner authority read**: `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json` (re-read this handoff). Canonical v2 spec remains sole product authority. This pass is the lane-5 (pass 4) correction set for the Dulche pass-3 readonly review (7 definite groups).

---

## Verdict / classification

All changes are **SOURCE/TEST-only corrections** for the 7 definite groups from the prior readonly review. No behaviour is fabricated; where real production evidence is absent (e.g. a real `llama-server` SHA-256 pin / model manifest), the axes stay honestly **BLOCKED / Degraded**.

**Explicit**: ` Core is in the frozen diff; we do NOT repeat any prior "Core unchanged" claim. `HomeDulcheRuntimeService.cs` and `HomeDulcheRuntimeContracts.cs` were changed in this lane and are part of the pass-4 set.

---

## Changed source hashes (git-status verified)

| File | SHA-256 |
| --- | --- |
| `Home/Source/Home/Core/HomeDulcheRuntimeObservationSource.cs` | `a39d05b33fe8b912886a4f38a6ae3c3dcb32d847ac8d688cd37a39401c1b9384` |
| `Home/Source/Home/Core/HomeDulcheBrokerAdapters.cs` | `cb57dbb68471fa327f9789660d4f1d5a0bafd724b73e32592f05cec829503b5c` |
| `Home/Source/Home/Core/HomeNativeWindowsComposition.cs` | `203d3e12ce99c6e3d331822b17a96d3de7f548866bdddfb87b8a969172e717f6` |
| `Home/Source/Home/Core/HomeDulcheRuntimeService.cs` | `bbdd05372dcd1f921cb88a491d84e255eb680a121a4534c806a9b5db7fc7c4b4` |
| `Home/Source/Home/Core/HomeDulcheRuntimeContracts.cs` | `cc2aa9cbaa9c45a57d6f5ee106c20a01f9c2d146f96ff10adced519cdea07178` |
| `shared/src/Haven.Desktop/Views/Pages/Chat/ChatRecoveryCuiView.cs` | `d8d803d45fefd896fb3d4013273a75875efe9c5f9a8682c2e5e9d78b959a70d7` |
| `shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs` | `670a302ad2c8a218fdbc32d831b1c772421dc25031c651fd1952bb02d17faa1a` |
| `shared/src/Haven.Desktop/Views/Pages/Chat/NewChatPage.OriginalLifetime.cs` | `093f424e18012c2dd9fd2dbb656ee35ebea32e75fc8d0ea8016b4bc4172fd344` |
| `shared/src/Haven.Desktop/Views/Pages/Chat/NewChatPage.cs` | `a752966522bd94b75966b5629b8213d7270c94c83b40c4a68836450e67f9b420` |
| `shared/src/Haven.Desktop/Services/SpacesBrowseAppContentSession.cs` | `b3fa31f12c14f1ec1a58fa191f639faf0eba43787a9441f65ccc2c8c564c2464` |
| `shared/src/Haven.Desktop/Services/UnifiedHomeContentFactory.cs` | `9a94deec7b830fad467ca15e16ed9330ad6c7bec8e66a79f2c293e34bcc25efe` |
| `Home/Tests/HomeDulcheRuntimeServiceTests.cs` | `499816b1b546b9f7b3a0c0cbf30fc1b8ed6cc0bbd9d6536139c6c76e1c61b5ea` |
| `Browse/Host.Tests/OverlayChatInitializationTests.cs` | `9e393ce837ddd86736e382d6da35b81b92df613d802b0016bf513f751ca85713` |
| `shared/tests/Haven.Desktop.Tests/UnifiedHomeContentTests.cs` | `62600d51526e3099225803e1fccce5ee5325ebd4c3310dcab5b99a462f04d71b` |

`git status` (worktree, uncommitted) confirms the above plus the pre-existing lane-owned/root-owned modified files (`BrowseChrome.AppContent.cs` is NOT edited here — host-side, owned by lane-4/root).

---

## Causal corrections vs the 7 definite groups

### Item 1 — `ChatRecoveryCuiView` ctor + `WaitForExitAsync`
- Ctor changed `public` → `internal`; `ChatRecoveryBindings` stays internal. All construction is within `Haven.Desktop` (scene), so no external callers were harmed. **NOT RUN**.
- `WaitForExitAsync` bool assignment fixed in the broker adapter: `exited = await RunToExitAsync(...)` (was inside a `try` with a confusing return); the bounded-timeout path now kills/reaps and returns `false` explicitly.

### Item 2 — recovery route `browse://home/models`
- `SpacesBrowseAppContentSession.WireChatRecovery` now parses `browse://home/models` (was `9to1://home/models`) and uses `page.RecoveryActionSettlement` (the recovery surface's actual loader action settlement) instead of `page.JoinAdmittedOriginalWorkAsync()`. Queue refusal is now surfaced via trace (`admitted == false`), and capture failures flow as faulted settlement tasks.
- `UnifiedHomeContentFactory` tightened: the `models` segment is admissible only under authority `home` (not `newtab`).
- `NewChatPage` exposes `RecoveryActionSettlement` forwarding to `ChatHavenScene.RecoveryActionSettlement` (UI-thread marshaled capture).
- **Integration patch required (host-side, lane-4/root owns `BrowseChrome.AppContent.cs`)**: widen `IsOwnedCanvas` (line 26-27) — it currently requires `ResourceSegments.Count == 0`, so `browse://home/models` is not routed to the internal Home factory. Requested widening:
  ```csharp
  private static bool IsOwnedCanvas(AppContentAddress address) => address.Namespace == AppContentNamespace.Browse
      && ((address.Authority is "home" or "newtab" && address.ResourceSegments.Count == 0)
          || (address.Authority == "home" && address.ResourceSegments.Count == 1
              && address.ResourceSegments[0].Equals("models", StringComparison.Ordinal)));
  ```
  This is lane-4/root-owned; I did NOT edit it.

### Items 5/6 — `HomeDulcheRuntimeService` lifecycle
- `_startGate` removed; `StartAsync` publishes ONE shared `_startTask` via a begin-gate **before** any source callback, coalesces repeated starts even after completion (reuse regardless of state), refuses after `_admissionClosed`, and awaits with `WaitAsync(callerToken)`.
- `CloseCoreAsync` classifies `HomeDulcheRuntimeUnavailable:` start faults as expected-readiness refusals (normal close succeeds) while genuine faults fail close by identity.
- Tests added: repeated-start-after-degraded coalescing, stop-succeeds-after-expected-refusal, genuine-start-fault-fails-close, stale-completion discard (deadlock-restructured: gate released only after Stop began), and peer-PID forwarding to the health probe.

### Item 7 — capability observation (no startup probe)
- `HomeDulcheCapabilityProbe` delegate removed; `HomeDulcheCapabilityObservation` record added (same-generation match keyed by provider + model id).
- `HomeDulcheRuntimeObservationSource` no longer initiates a probe during startup — it records capability only from an already-admitted, same-generation observation; absent one, the axis stays blocked (deficiency added).
- `HomeDulcheLinuxBrokerAdapter`: extracted `RunToExitAsync` + `TryKillAndReap` custody; `_healthProbe` now takes `(socketPath, expectedPid, ct)` and verifies the AF_UNIX peer PID via `HomeNativePeerObservation.FromAcceptedUnixSocket` against the systemd MainPID.
- `HomeNativeWindowsComposition` ctor seam changed `dulcheCapabilityProbe` → `dulcheCapabilityObservation`; no caller passed the old delegate (verified by grep).

### Items 3/4 — recovery surface `CuiSceneHost` refactor + settlement/cleanup
- `ChatRecoveryCuiView` refactored to mirror `ChatComposerCuiView`: `CuiSceneHost`, `ChatRecoveryReadiness`, `InitializeAsync`, `CaptureOriginalActionSettlement()`, `CloseAsync()`/`CloseOriginalCoreAsync`, `Dispose`/`DisposeAsync`.
- `ChatHavenScene` adds `RecoveryActionSettlement`, `StartRecoveryClose`, `JoinRecoveryCloseAndReleaseAsync`, `ReleaseRecoveryUi`; `Dispose()` now retires bindings + starts the child close but does NOT unregister/dispose the recovery view (release is deferred to the page's joined ACK path, mirroring the composer).
- `NewChatPage.OriginalLifetime.cs`: `StopOriginalPageAsync` starts recovery close; `CleanupOriginalPageAsync` joins `JoinRecoveryCloseAndReleaseAsync` (before `_scene.Dispose()`).
- Test updated: recovery button test uses `CaptureOriginalActionSettlement()`; added a recovery-release cleanup test and the Home-only models-route factory test.

---

## Integration patch request (host-side — lane-4/root owned)

1. `BrowseChrome.AppContent.cs` — widen `IsOwnedCanvas` as above so `browse://home/models` routes to the internal Home factory. **NOT edited in this lane.**
2. `App.UnifiedWorkspaceHost.cs` (already lanes a `UnifiedHomeContentFactory`) — no change needed beyond the owned-canvas widening; the Home factory now admits `home/models`.

---

## NOT RUN / BLOCKED

- **No .NET build / restore / test / app execution was performed** in this lane (no slot granted). All changed files are uncompiled edits.
- **`RuntimeVerified` / `ModelVerified` / `Ready` stay honestly BLOCKED** in production: no real production `llama-server` SHA-256 pin / model manifest is staged, so a genuine package/model verification cannot be claimed. No fabrication.
- End-to-end item-2 route/activation and item-3/4 cleanup tests dependent on the host-side `IsOwnedCanvas` widening are **NOT RUN** until the lane-4/root patch is integrated.

---

## Next root-owned check

After the lane-4/root `BrowseChrome.IsOwnedCanvas` widening is integrated, a **root-owned** check should:
1. Run the .NET build for `Home`, `Haven.Desktop` (incl. `Browse/Host.Tests`, `Haven.Desktop.Tests`).
2. Run `HomeDulcheRuntimeServiceTests`, `OverlayChatInitializationTests` (recovery route + cleanup), and `UnifiedHomeContentTests` (home-only models admission).
3. Confirm the recovery route `browse://home/models` activates an owned Home Models canvas end-to-end, and that the chat draft/refusal survive.

---

## Evidence / hygiene
- All changes are uncommitted worktree edits; `git status` shows the exact modified/untracked set above.
- No lane-4/root-owned files (`BrowseChrome.AppContent.cs`, `App.UnifiedWorkspaceHost.cs`, shared host/model/project seams) were overwritten.
- No commit/push/deploy; no model subagents; text-only edits; no image reads.
