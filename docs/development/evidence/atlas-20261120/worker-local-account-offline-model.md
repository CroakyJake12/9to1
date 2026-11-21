# G04-T02 — Local account (no sign-in) & true offline mode — verification

Repo: /home/jacob/Documents/GitHub/9to1, branch `master` @ ac1f355fe, working tree clean except two
untracked dev scripts under `cloud/files-drive/dev/`. Read-only investigation; no builds or test runs
(`dotnet test` writes obj/bin **into the repo**, which the session boundary forbids — commands to verify
manually are in §5). Paths relative to `9to1 Workspace/` unless noted.

## 1. What first run actually does when you skip sign-in

**Gate.** Production startup runs the first-run gate *before publishing Home*:
`shared/src/Haven.Desktop/App.UnifiedWorkspaceHost.cs:357` (observe local actor) → `:380`
`await RunFirstRunOnboardingGateAsync(_actor.ProfileId, …)` → `:507-560`. If the profile’s durable
completion record is healthy the gate bypasses; otherwise it mounts the authored flow and Home is published
only after a **genuine durable completion commit** (“No Home is published for a damaged or failed state”,
`:500-505`; `RunSessionToPublicationAsync` awaits `CompletionTask`).
Mount: `shared/src/Haven.Desktop/FirstRunOnboardingFlow.cs:69-125` parses `UI/Onboarding.cui`.

**Flow and options** (`Home/UI/Onboarding.cui`):
- Welcome: “A short, skippable first-run before normal Home use. **No account or download is required.**” (`:103`).
- Step “Sign In?” (`:109-118`): two buttons —
  - **Sign In** (`onboarding.signin`) — “Signing in provides access to cloud and connection features. Your
    local tools keep working either way.” (`:112`);
  - **Not Now** (`onboarding.notnow`, accessible name **“Continue in local-only mode”**, `:115`).
- Step “Download Models?” (`:121-128`): **Continue without downloading** (`onboarding.complete`). Model
  download itself is deliberately blocked in this build (“Model downloads are unavailable right now…”,
  `Home/Source/Home/Onboarding/HomeOnboardingCuiScene.cs:233-238`); the recommendation manifest source is
  unconfigured in DI (`App.Onboarding.cs:25` `recommendationSource: null`) and honestly reports unavailable
  (`Home/Source/Home/Core/HomeOnboardingService.cs:147-153`).
- If sign-in is impossible (no cloudflared helper / no network): `ProceedAsync` returns
  `SignInUnavailable` — “Sign-in is unavailable right now. Choose Not Now to use local tools.”
  (`HomeOnboardingCuiScene.cs:155-163`). It never mints auth locally.
- If an eligible **local** model is installed, onboarding auto-completes straight into Home
  (`Onboarding.cui:38-43, 67-71`; `HomeOnboardingService.HasEligibleLocalModelAsync:100-144` verifies real
  installed artifacts via re-hashing, not a hard-coded claim).

**Skip state storage:** `onboarding.notnow` → completion persisted with `signedIn: false` as a
**DeviceLocal** record `home.onboarding.{profileId}` in the Home state file
(`HomeOnboardingService.PersistCompletionAsync:166-203`; record scope/authority `DeviceLocal`/`LocalCanonical`
`:194-196`; file `{DataDirectory}/Home/state.json`, §3). Completion is idempotent and a corrupt record is
preserved for recovery, never silently overwritten (`ReadCompletionAsync:55-90`, recovery screen
`Onboarding.cui:80-92`).

**Result:** after skipping, the user gets a fully functional Home. `HomeOnboardingService` doc `:25-30` and
the controller (`DispatchWithOutcomeAsync:135-151`) confirm only real domain ops; nothing account-bound is
required for Home publication.

## 2. Local profile vs cloud CAKE account

**Local profile (default, no account).**
`Home/Source/Home/Core/HomeLocalProfileIdentity.cs`:
- Trusted principal = the OS process authority: Windows SID (`:23-24`) or `unix-euid:<n>` via `geteuid()`
  (`:26-27`); “environment usernames and request claims are never identity evidence” (`:15`).
- First call derives SHA-256(principal), creates `HomeLocalProfile(Guid.NewGuid(), digest, now)` and writes
  it CAS-guarded into `home.json`/`state.json` as record `home.local-profile`
  (scope `DeviceLocal`, authority `LocalCanonical`, `:81-119`); actor id is `local-profile:{GUID}`
  (`:121-122`). Concurrent first starts converge via store CAS (`:89-106`).
- Explicit contract: “**Local OS profile identity is independent of CAKE AccountID and OrganisationID.
  This creates no artifact ownership grants.**” (`:37`).
- Different OS user ⇒ digest mismatch ⇒ `UnauthorizedAccessException` “local profile belongs to a different
  operating-system principal” (`:118-119`). Multiple desktop users on one machine therefore get **separate
  profiles** (separate `local-profile` records per principal? — actually one record per device store;
  a second OS user is *refused* until “explicit ownership recovery”, not silently shared. Cross-user data
  sharing: **not found — unknown by design**).
- Consumed everywhere as the actor: Boards create admission (`App.UnifiedWorkspaceHost.cs:730-732`),
  Files directory binding (“A verified account **or local profile identity** is required”,
  `Files/CUI/Services/FilesWorkspaceDirectoryResolver.cs:43,95`), Boards Notes route (requires
  `AccountId is null`, `Services/NativeBoardsDesktopRoute.cs:156-158`), standalone Boards
  (`Boards/app/Program.cs:133-136`), Linux Home root-service commands
  (`Services/LinuxHomeRootServiceCommand.cs:37` etc.).

**Cloud account = “CAKE ID” (optional, addable later).**
- UI entry: Settings → button **“CAKE ID account”** (“Open CAKE ID account sign-in, profile and sessions”),
  `Views/Pages/Settings/SettingsHavenPage.NativeCakeAccount.cs:10-24` → `NativeCakeAccountUiOwner.OpenAsync`
  → `Accounts/NativeCakeAccountWindow.cs`.
- Flow: OAuth2/OIDC **public client with PKCE + loopback callback**
  (`Accounts/Remote/NativeCakeBrowserFlow.cs` — `NativeCakeLoopback:102`, system browser via
  `SystemNativeCakeBrowserRequestSource:12`), issuer `https://cake-id-release-validation.jcbailey008.workers.dev/api/auth`
  (Cloudflare Worker = `cloud/cake-id-auth`, `wrangler.jsonc`) behind a **Cloudflare Access tunnel helper**
  (`cloudflared` binary pinned under `AppContext.BaseDirectory/auth`; “Missing helper leaves sign-in
  explicitly unavailable”, `App.NativeCakeAccount.cs:27-45`).
- The signed-in token lives **in memory only** (`NativeCakeAccountSession.cs` `private NativeCakeCredential?
  credential` — no persistence into the secret store found); comment: “Optional account client, never a local
  credential/code issuer or Home principal source.”
- What sign-in gives in this build: a **read-only** profile/sessions view (“Sign in to manage your profile
  and sessions”, session revoke actions; profile edits refused — `Accounts/NativeAccountBindingsAdapter.cs:35-71`).
  When sign-in is unavailable it states: “You can keep using local apps and retry later.” (`:44,54`).
- What sign-in explicitly does **not** change: local files/Boards — “Boards use the original Home-owned
  Notes store on this device. **Account sign-in does not sync this library.**”
  (`Services/NativeBoardsDesktopRoute.cs:128`). Cloud sync of Home state is largely unimplemented: across
  `Home/Source` + `shared/src`, record writes are 61× `HomeDataScope.DeviceLocal`, 1× an
  `AccountSynchronized` allowance (theme preference scope check,
  `Home/Source/Home/Core/HomeThemePreferenceService.cs:7`); `WorkspaceSynchronized`/
  `OrganizationSynchronized` exist only in the enum (`HomeCoreContracts.cs:120-126`).

**Folders the cloud services live in:** `/home/jacob/Documents/GitHub/9to1/cloud/` —
`cake-id-auth` (OIDC issuer worker), `files-drive` (Files drive worker + dev scripts), `public-sites`,
`billing`, `src`, `tests`. The desktop product code references the CAKE issuer only from
`App.NativeCakeAccount.cs` (grep `workers.dev` in `shared/src`/`Files` matches nothing else). The `Connect/`
folder is the MCP connector (`Haven.Connector`), not an account system.

**Cloud data sources in play:** cloud model providers (`ResilientProviderRoutingModelClient`), CAKE account
API, Hugging Face model downloads (`Haven.Application/HuggingFaceModels.cs` `IHuggingFaceModelService`),
app updates (`Haven.Infrastructure/Updates/StoreUpdateProvider.cs`), mail/calendar connectors
(`Haven.Infrastructure/Mail`, `Calendar`). Device sync/Mesh is **not cloud**: it uses a LAN transport
(`Haven.Infrastructure/Mesh/SecureLanMeshTransport.cs:20-55` TCP listener + `LanMeshDiscoveryService.cs:18-33`
UDP discovery) with pairing challenges/offers (`Haven.Application/Mesh/MeshCoordinator.cs:49+`); a paired
peer that is unreachable is queued, never silently executed elsewhere (`:154-155,197`).

## 3. Where 9to1 stores data when you never sign in

All under `IAppPaths` (`Haven.Infrastructure/Lifecycle/AppPaths.cs:19-34`): `DataDirectory =
$XDG_CONFIG_HOME|~/.config /Haven` (override `HAVEN_DATA_DIR`), created private (0700 on Linux, `:36-44`):
- `~/.config/Haven/Home/state.json` — Home core state: local profile record, onboarding completion,
  permissions/trust decisions + audit, theme, etc. (`App.OriginalWindowsHome.cs:51`,
  `Services/OriginalLocalTaskConsole.cs:185`; store is “Crash-safe, revision-checked… corrupt or newer file
  is reported without replacing it”, `Home/Source/Home/Core/FileHomeCoreStateStore.cs:9-13`).
- `~/.config/Haven/haven.db` — SQLite (conversations, tasks, dashboard, pins…; no boards table, §G03).
- `~/.config/Haven/Notes/Documents/…` — Notes/Boards-notebook documents (G03 §2).
- `~/.config/Haven/Boards/profiles/<sha256(profile)>/drafts/…` — Boards drafts + recents + destinations
  (G03 §2); plus `~/.config/Haven/Boards/workspace-resolution.json` diagnostic (`BoardsWorkspaceFolders.cs:91-95`).
- `~/.config/Haven/Credentials/` — provider secrets on Linux: AES-GCM files with a private per-store key,
  descriptor-relative no-follow IO, refuses directories not owned by the euid; “This grants no
  OAuth/account/Home permission” (`Persistence/Secure/LinuxProviderSecretStore.cs:11-38`). Windows:
  Credential Manager (`Persistence/Secure/WindowsProviderSecretStore.cs`). Mail tokens: `CalendarTokenStore.cs`,
  `ConnectedAccountAccessTokenProvider.cs`.
- `~/.config/Haven/BrowserProfile`, `Attachments`, `Logs` (`AppPaths.cs:24-27`).
- User-visible workspace: `~/Documents/9to1/` (+ app folders `Write/Present/Canvas/…`, hidden metadata
  `.9to1-files/drive.json`), xdg-aware, app-data fallback (`Haven.Application/LocalWorkspaceFolders.cs:36-55,120-143`).

**Separation:** the Home state store is per-OS-user (0700 dir + euid-owned checks + principal digest inside
`state.json`), so two desktop users do not share data; but it is one **device** record store, not a
multi-account store: cloud sync of Home state has no producers (§2), and account sign-in is documented as
not syncing the Boards library. Cross-device sync exists only via Mesh pairing (LAN) for explicitly
adapted resources (e.g. `Haven.Infrastructure/Mesh/BoardsMeshResourceOwner.cs` — opt-in adapter, whole
`.9to1board` envelope, hash-checked).

## 4. “Offline mode”

- **No `IsOffline` exists** anywhere in product code: `grep --include=*.cs "IsOffline"` over the repo
  returns **zero** matches outside vendored donor trees (`Browse/Source/FirefoxGecko`,
  `Dev/Source/CodeOSS` — not our code). So “what consumes `IsOffline`” = nothing; the symbol is not in the
  product.
- **There is no global online/offline switch.** The design is per-feature graceful degradation:
  - Model calls: cloud providers are *policy*-gated by `IPrivacyPreferenceStore.LocalOnlyMode`
    (`Haven.Application/Privacy/Preferences.cs`-family: `PrivacyPreferences.LocalOnlyMode` —
    enforced in `Haven.Infrastructure/Providers/ResilientProviderRoutingModelClient.cs:415,800`
    (“Cloud providers are disabled by the current local-only or safety policy”), task-run admission
    (`Haven.Application/Execution/TaskRunConfiguredCloudAdmissionSource.cs:281-312`,
    `TaskRunPermissionAuthority*.cs`), plus `RuntimeSafetyState.IsSafeMode`).
  - **Connectivity is detected by attempting the call.** Availability classification treats only “a real
    transport-level connectivity failure” as degraded
    (`Haven.Application/Abstractions.cs:246-254`), and Chat surfaces classify discovery failures honestly:
    `Views/Pages/Chat/ChatModelDiscoveryPolicy.cs:21-39` → “The configured model provider is unavailable.”
    (non-blocking). Sign-in without network → `SignInUnavailable` (§1). No network pre-probe gates startup;
    the only connectivity check found is a diagnostics *warning*
    (`Haven.Application/Lifecycle/DiagnosticsService.cs:105` — “Could not verify network connectivity.”).
  - Mesh “offline” states refer to **peer** presence, not device network mode
    (`MeshCoordinator.cs:12,154,197`).
- **Local models / local execution paths:** `Haven.Infrastructure/Providers/LlamaCppRuntime.cs:12-25`
  builds and launches a local llama.cpp server (loopback/Unix socket), registered via
  `AddHavenPreviewLocalLlamaCpp` (`DI/ServiceCollectionExtensions.cs:370`); `OllamaModelProvider`
  (`:359`, `OllamaClient` `:192-201`) talks to a local Ollama. Onboarding eligibility reads installed local
  models (`HomeOnboardingService:100-144`), and Dashboard offers device-model recommendations
  (`App.UnifiedWorkspaceHost.cs:722-724`).
- **Does the product work with no internet?** For local authoring/Files/Boards/local models: yes by
  construction — none of those paths perform network I/O (G03 report; local profile, local Files store,
  local approval windows). What degrades or fails without internet: CAKE sign-in, cloud model providers,
  model recommendation manifest (unconfigured anyway), Hugging Face model downloads, mail/calendar/external
  connections, app updates, cloud Files drive worker. I did not run the app, so this is source-level
  confirmation, not runtime confirmation.

## 5. How to verify (commands; I was blocked from running them by the read-only boundary)

Targeted filters (from repo root, adjust quoting for your shell):
```bash
# Desktop: Home/Files board flows + accounts fence + mesh scene + shutdown-onboarding
dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests" \
  --filter "FullyQualifiedName~NativeHomeFiles|FullyQualifiedName~FilesNativeBoard|FullyQualifiedName~NativeAccountObservationFence|FullyQualifiedName~MeshHavenScene|FullyQualifiedName~DesktopOriginalShutdownSequence|FullyQualifiedName~LinuxWorkspaceBoundaryAbi"
# Infrastructure: secret store, home root service, mesh sync/reliability
dotnet test "9to1 Workspace/shared/tests/Haven.Infrastructure.Tests" \
  --filter "FullyQualifiedName~LinuxProviderSecretStore|FullyQualifiedName~LinuxDesktopSecretComposition|FullyQualifiedName~MeshSync|FullyQualifiedName~MeshReliability|FullyQualifiedName~MeshRegistration|FullyQualifiedName~LinuxHomeRootServiceRuntime|FullyQualifiedName~LinuxHomeBootstrapActivation"
# Core: workspace Linux capability + task-run privacy gates
dotnet test "9to1 Workspace/shared/tests/Haven.Core.Tests" \
  --filter "FullyQualifiedName~WorkspaceLinuxCapability|FullyQualifiedName~TaskRunPermissionAuthority|FullyQualifiedName~TaskRunCloudPermissionRemediation"
# Home app unit tests (includes onboarding/session composition)
dotnet test "9to1 Workspace/Home/Tests"   # project with HomeCoreStateStoreTests, HomeNativeLinuxProcessBootstrapTests, etc.
```
Relevant named tests/classes actually found:
- `shared/tests/Haven.Desktop.Tests/NativeAccountObservationFenceTests.cs` —
  `Onboarding_awaits_the_shared_session_and_records_only_its_current_authenticated_result` (sign-in adapter;
  header: “Presentation counterexamples only. Scripted wire records do not prove live CAKE auth.”).
- `shared/tests/Haven.Desktop.Tests/DesktopOriginalShutdownSequenceTests.cs:283-288` — first-run reads
  `HomeOnboardingService` state (`NotCompleted` → `Welcome` step).
- `shared/tests/Haven.Desktop.Tests/MeshHavenSceneTests.cs`; `Haven.Infrastructure.Tests/MeshSyncTests.cs`,
  `MeshReliabilityTests.cs`, `MeshRegistrationTests.cs`, `MeshAutomaticStartupTests.cs`.
- `Haven.Infrastructure.Tests/LinuxProviderSecretStoreTests.cs`, `LinuxProviderSecretStoreCancellationTests.cs`,
  `LinuxDesktopSecretCompositionTests.cs`, `LinuxHomeRootServiceRuntimeTests.cs`,
  `LinuxHomeBootstrapActivationOwnerTests.cs`.
- `Haven.Core.Tests/WorkspaceLinuxCapabilityTests.cs`, `TaskRunPermissionAuthorityTests.cs`.
- `Home/Tests/HomeCoreStateStoreTests.cs` (DeviceLocal record store).

**Missing named tests:** the task listed `shared/tests/Haven.Desktop.Tests/linux/LinuxHomeOwnershipTests.cs`
and `linux/FilesNativeBrowserServiceLinuxHomeTests.cs` — **neither exists** in this tree.
`shared/tests/linux/` contains only `compose-gnome-platform-smoke.sh`. Closest current coverage: the
`FilesNative*Tests.cs` set in `Haven.Desktop.Tests` and `Home/Tests/HomeOwnershipOriginalSourceCallbacksTests.cs`
/ `HomeNativeLinuxProcessBootstrapTests.cs`. Someone should confirm whether the two named files were never
created or were renamed/deleted (git history was out of scope).

## 6. Verdict

**CONFIRMED — you can use 9to1 productively with only a local profile (no cloud account):**
First run explicitly offers “Not Now / Continue in local-only mode” and persists a DeviceLocal completion;
Home publication requires only the OS-derived local profile actor
(`local-profile:{GUID}`); Boards/Files/Notes/SQLite authoring all run under that actor, and account sign-in
is documented (and code-enforced) as not required for — nor a sync trigger of — local libraries.

**CONFIRMED with nuance — there is no single “offline mode”, by design:**
No `IsOffline`/global switch exists; offline behaviour is per-feature honest degradation
(`ChatModelDiscoveryPolicy`, `SignInUnavailable`, Mesh peer-queued tasks) plus policy gates
(`LocalOnlyMode`, safe mode) for cloud models, with genuine local execution via llama.cpp/Ollama. Nothing in
the local authoring path requires network. The caveat: this is **source-verified, not runtime-verified** (no
app run, no test executed under this boundary), and the two `linux/*` test files named in the task are
absent, so Linux ownership/browser test coverage claims must rest on the existing files listed in §5.
