# Full-app shutdown diagnosis — platform secret-store composition + borrower participation

Author: Chat/Spaces lane (OpenCode worker `ses_eb3f94f83ffeOPzvn1NFUoC9Ch`, branch
`opencode/m3-shell-wiring-20261016`). Written 2026-10-18.
Status: **diagnosis only.** No file owned by another worker was edited. No native UI action was
performed (root owns native validation). Read-only source/test inspection plus the existing
independent native diagnostic evidence.

## Evidence used

- Independent diagnostic: `work/cycle-20261017T234532Z/native-diagnostic-current-finding.json`
  (source `9e44ba3335824d3ad0aa09d2c8a517a6dc1d6a60`, `Haven.dll`
  `be995173723cca79dc607b9f5b34dbe364dbb5162a3ff0ff62f827a9b5b1292c`,
  actual Linux ARM64 Debug opt-in diagnostic, PID 614418 / window `0x180001b`, fresh Home-only
  state, normal verified `WM_DELETE_WINDOW` at 00:05:43.440Z, no Files/Chat/tab mutation).
  `status = FAILED; actual shutdown delivery faults retained, process remains present`.
- `unique_causes` chain (exact frames, reproduced verbatim):
  - `[2] System.PlatformNotSupportedException` (HResult `-2146233031`):
    `Haven.Infrastructure.WindowsProviderSecretStore.EnsureWindows`
    → `WindowsProviderSecretStore.SetAsync`
    → `Haven.Infrastructure.MeshIdentitySecretStore.SetPrivateKeyAsync`
    → `Haven.Application.MeshCoordinator.<InitialiseAsync>d__18.MoveNext`
    → `System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw`
    → `System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess`
    → `Haven.Desktop.Services.DesktopOriginalWorkLifetime.Original.<AwaitAsync>d__49.MoveNext`
  - `[4] System.AggregateException`: `DesktopOriginalShutdownSequence.ThrowOriginalFailures`
    ← `DesktopOriginalShutdownSequence.<RunOriginalShutdownAsync>d__28.MoveNext`
    ← `Haven.Desktop.App.<JoinOriginalAppTaskAsync>d__298.MoveNext`
  - `[5] System.AggregateException`: `Haven.Desktop.App.ThrowAppCauses`
    ← `App.<JoinOriginalDesktopBorrowersAsync>d__296.MoveNext`
    ← `DesktopOriginalShutdownSequence.<JoinActualAsync>d__31.MoveNext`
  - `[6] [7] [8]`: three `Haven.Desktop.Services.DesktopOriginalRetirementUnavailableException`
    children of `[5]` (the three retained borrower/owner contracts).

## Proven root cause (source-grounded)

The mesh identity write is dispatched to the **unconditional Windows credential store** on a
non-Windows OS, and that store refuses by design:

1. `9to1 Workspace/shared/src/Haven.Infrastructure/DI/ServiceCollectionExtensions.cs:206`
   ```csharp
   services.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
   ```
   inside `AddHavenInfrastructure` — registered for **every** operating system, with no platform
   guard. This is the only default `IProviderSecretStore` descriptor.
2. `9to1 Workspace/shared/src/Haven.Infrastructure/Persistence/Secure/WindowsProviderSecretStore.cs`
   ```csharp
   public Task SetAsync(...) { EnsureWindows(); ... }        // line 45-48
   private static void EnsureWindows()                        // line 204
   { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(
         "Provider credential storage requires Windows Credential Manager."); }
   ```
   → exactly the observed `PlatformNotSupportedException` from `SetAsync`.
3. `9to1 Workspace/shared/src/Haven.Infrastructure/Mesh/MeshServiceCollectionExtensions.cs:13`
   `services.AddSingleton<IMeshIdentitySecretStore, MeshIdentitySecretStore>()`, and
   `MeshIdentitySecretStore` (`…/Mesh/MeshIdentitySecretStore.cs:6`) takes the ambient
   `IProviderSecretStore`. `MeshCoordinator.InitialiseAsync`
   (`…/Haven.Application/Mesh/MeshCoordinator.cs:46`) writes its identity key through it.
4. The Linux replacement exists, but only as an **optional downstream substitution**, and both
   substitution sites are strict precondition checks that require the *maintained Windows*
   descriptor to still be present and no `LinuxProviderSecretStore` to exist yet:
   - `…/DI/WindowsHomeDomainServiceCollectionExtensions.cs:44-68` (`AddHavenOwnedWindowsHomeDomain`,
     `if (OperatingSystem.IsLinux())` branch: refuses unless
     `secrets[0].ImplementationType == typeof(WindowsProviderSecretStore)` and no
     `LinuxProviderSecretStore` descriptor exists; then removes it and adds
     `LinuxProviderSecretStore` + `IProviderSecretStore → LinuxProviderSecretStore`).
   - `…/ExternalConnections/CloudflareLocalDomainRegistration.cs:84-100`
     (`ConfigureOriginalServices`, same precondition set, same replacement, plus a
     `UnauthorizedAccessException` unless the resolved `IAppPaths` is the SAME supplied instance).

Consequences that make this a real composition defect rather than a one-off:

- Correctness of a platform-wide capability (`IProviderSecretStore`) depends on a *later,
  optional* registration running inside the graph that resolves the borrower. Any provider built
  from `AddHavenInfrastructure` without one of those two substitutions leaves
  `WindowsProviderSecretStore` live on Linux, and the failure surfaces later, inside unrelated
  work (here: mesh identity initialisation awaited inside admitted original work).
- The two substitution sites are **mutually exclusive within one collection** (each refuses if
  `LinuxProviderSecretStore` is already registered), so there is no single canonical platform
  ownership point.
- The substitution is also coupled to `IAppPaths` identity custody (the Linux store factory
  refuses a different paths instance), so platform ownership of the store and of paths must move
  together.

## Proven propagation (why the window retained and `cleanShutdown=false`)

The `PlatformNotSupportedException` is awaited **inside admitted original work**
(`DesktopOriginalWorkLifetime.Original.AwaitAsync` in the chain), so it is retained as an owned
terminal cause of that original rather than lost, by the existing strict lifetime rules. At normal
`WM_DELETE_WINDOW` the whole-app shutdown then joins and reports it:
`App.JoinOriginalAppTaskAsync` → `DesktopOriginalShutdownSequence.RunOriginalShutdownAsync` →
`ThrowOriginalFailures`, and `App.JoinOriginalDesktopBorrowersAsync` → `ThrowAppCauses` with three
`DesktopOriginalRetirementUnavailableException` children. The retained window with no accessible
controls is a *consequence* of the shutdown delivery itself failing, not of a silent crash.

Note on the mesh timing: the trace shows the mesh identity write being awaited **under admitted
original work**, i.e. `MeshCoordinator.InitialiseAsync` is reached from within the App/page
original cohort (not from a bare startup task), which is why the fault is attributed to that
cohort at close, and why it must keep being attributed rather than suppressed.

## Explicitly NOT proven (do not claim)

- Which live container/resolution path supplied the Windows store in that specific process is
  **not** proven from source: the main `Haven.Desktop` App composition *does* call
  `AddHavenOwnedWindowsHomeDomain` (`App.axaml.cs:90` → `App.OriginalWindowsHome.cs:79`, with no
  conditional skip on that path), and `OriginalLocalTaskConsole` applies
  `CloudflareLocalDomainRegistration.ConfigureOriginalServices` (`…/Services/OriginalLocalTaskConsole.cs:213`).
  Source inspection alone cannot decide which of the live graphs (App provider, cold-task console
  provider, or a host built from `AddHavenInfrastructure` such as `Write/WriteAppHost.cs:34` or
  `Present/PresentAppHost.cs:39`) resolved the Windows store here.
- The exact three borrower/owner contracts behind `[6] [7] [8]` are not named in the JSON frames.
  Per the coordination record they are split as WorkspaceWindowService + OverlayWorkspaceController
  (continuation_audit) and NotificationService (queue_review); that mapping is from the
  coordination record, not from this frame set.
- No causal equivalence with the older `192b` fixture is claimed.

## Recommended repair shape (owners: queue_review for platform/borrower registration, root for App shutdown/ledger)

1. Give `IProviderSecretStore` a **single canonical platform ownership point** so no graph can
   resolve the Windows store on a non-Windows OS (either a platform-guarded default in
   `AddHavenInfrastructure`, or one explicit platform-composition step that every host must apply),
   and keep the strict "SAME `IAppPaths` instance" custody check when the Linux store is created.
2. Update the two existing precondition checks so they continue to refuse substitutions and
   parallel owners, without requiring the base descriptor to be specifically the Windows type.
3. Do **not** repair this by catching/waiving the provider failure or by dropping the retained
   cause: the retained `PlatformNotSupportedException` is the honest evidence that identity
   material was never written. Preserve it.
4. Remove the ambiguity before re-running native: record, per live container, the resolved
   implementation type of `IProviderSecretStore` and the identity of `IAppPaths`, and add an
   App-composition regression check mirroring
   `shared/tests/Haven.Infrastructure.Tests/LinuxDesktopSecretCompositionTests.cs` (which covers the
   isolated composition, not the actual App graph).
5. Then repeat the native close/reopen (root) and verify the borrower contracts are terminal.

## Unrelated evidence preserved by the same worker (for context)

- Chat/Spaces model-availability milestone committed as `9fc57054` on
  `opencode/m3-shell-wiring-20261016` (honest persistent unavailable state, admitted-catalogue
  gating, complete terminal-payload classification, canonical CUI Spaces failed-retirement).
- Evidence: `9to1 Workspace/shared/artifacts/chat-unavailable/` — Desktop build exit 0, focused
  `Haven.Desktop.Tests` net10.0 39/39, full `HavenOS.Browse.Host.Tests` 154/154, negative control
  failing both new rig cases, and the 20/20 baseline reproduction of the pre-existing queued-scroll
  versus conversation-transition boundary (which remains a separate unaccepted gate).
