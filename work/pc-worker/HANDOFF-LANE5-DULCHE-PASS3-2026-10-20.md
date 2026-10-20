# Lane 5 — Dulche pass-3 implementation handoff (Mercury)

Date: Tue Oct 20 2026. Lane 5 (no .NET slot). All source/test edits are **uncompiled / NOT RUN**.
No .NET build/restore/app/test ran. No commit/push made. Worktree remains uncommitted and dirty
(branch `codex/mercury-dulche-20261020`).

This handoff supersedes the pass-2 handoff in scope but does **not** replace it: the pass-2
handoff (`HANDOFF-LANE5-DULCHE-PASS2-2026-10-20.md`) is preserved verbatim for history. This is
the **next root-owned check** (the pass-3 handoff the coordinator requested).

## Pinned authority reread (this session)
- `AGENTS.md` (reread): pinned single-app implementation requirement; pass-1 frozen checkpoint at
  `/workspace/shared/9to1-test-evidence/parallel6-dulche-pass1-frozen` (base `fb70840d`,
  checkpoint `dd9b6d1f80d9e4e17ac89b80613a2eb1341d8f79`).
- Canonical spec sections and owner refs governing this surface: Home Core/Dulche/runtime control,
  model resolution, `2026-10-20-chat-composer-dulche` (bundled Dulche default path, no external
  provider setup), `2026-10-20-continuous-composer`, `2026-10-20-floating-format-bar`,
  `2026-10-18-haven-spaces`, `2026-10-20-haven-shared-header`, `2026-10-20-single-app-implementation`.
- No new owner correction supersedes the pass-1/pass-2 scope on the recovery route.

## Scope (finding set) — pass 3
Pass-2 delivered the Home-owned runtime contracts + a *code-level* recovery route
(typed seam + host wiring). Pass-3 replaces the **HUI "Manage local models" button** with a
genuine **CUI recovery action** that is (a) authored CUI, (b) loaded through the shared
`CuiControlLoader`, (c) routed to the real **Home Models** destination (`9to1://home/models`)
with a **real settlement** (the page's genuinely admitted original work), and (d) disposed with a
fail-safe retire/unregister path. This is the finding-set fix for "real CUI recovery action in the
real composition" from pass-1 review.

## Changes made this session (pass 3)
### Authored CUI recovery surface (new)
- `shared/src/Haven.Desktop/Views/Pages/Chat/ChatRecovery.cui` — authored recovery CUI: root
  `<Cui id="chat.recovery" version="1">`, one named `<Action name="chat.recovery.manage"
  command="chat.recovery.manage"/>`, root `<Button id="recovery-manage-button" content="Manage local
  models" IsVisible="{Binding Recovery.Visible}">` containing `<ActionEvent Type="Clicked"
  Action="chat.recovery.manage"/>`. Git hash `4ba94add839c`; SHA-256
  `bd2c38060216b47e52f4b7087fae854160300bda3765a2e1cd866e5c303ce88c`.
- `shared/src/Haven.Desktop/Views/Pages/Chat/ChatRecoveryCuiView.cs` — `ChatRecoveryBindings`
  (internal, implements `ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
  INotifyPropertyChanged`; `Recovery.Visible` binding, `TrySetValue` always false, `Retire()`
  halts `DispatchAsync`, `DispatchAsync("chat.recovery.manage")` raises `ManageLocalModelsRequested`),
  `ChatRecoveryCuiDocument` (static loader for embedded resource
  `Haven.Desktop.Chat.Recovery.cui`), and public `ChatRecoveryCuiView : ContentControl, IDisposable`
  (owns `CuiControlLoader`, sets binding context/action dispatcher, `TryLoad`, `WireBindings`,
  `WhenActionsIdleAsync`, `Refresh`, disposal that retires + disposes loader). Git hash
  `3c9174c283b6`; SHA-256 `df41380f9e59686943cf81948548c2d81e7014d3f5360da6a6c41dd21b1b880b`.
  The view **must remain public** (tests access `Scene(page).RecoveryView` cross-assembly).
- `shared/src/Haven.Desktop/Haven.Desktop.csproj` — added the recovery CUI as an `EmbeddedResource`
  with LogicalName `Haven.Desktop.Chat.Recovery.cui`. Git hash `00003c6e7a4e`; SHA-256
  `8a8eeb6fc6cc9face1027ad0502536c35cb42af49144242f2c0eae989b256e5e`.

### Home runtime wiring (modified)
- `shared/src/Haven.Desktop/Views/Pages/Chat/ChatHavenScene.cs` — **HUI `ManageLocalModels`
  button removed**; scene now owns a `RecoveryHost` `NativeHost` (present in markup), creates
  `ChatRecoveryBindings` + `ChatRecoveryCuiView`, registers the view via `_nativeResolver.Register`,
  exposes public `RecoveryView` property + `ManageLocalModelsRequested` event (public setter kept),
  `SetManageLocalModelsVisible(bool)` drives bindings + `RecoveryView.Refresh()`, and `Dispose()`
  retires bindings, unregisters the native mapping and disposes the view (unregister failures not
  swallowed). Git hash `fc8bc72b5dd6`; SHA-256
  `85cbc414df5289fa2744d39b4bb2e9113b6464ddfe55ec790feddc36475eb9b2`.
- `shared/src/Haven.Desktop/Views/Pages/Chat/NewChatPage.cs` — `PublishOriginalModelGateVisual`
  still drives `SetManageLocalModelsVisible(modelAvailability == Unavailable)` (line 2683);
  `ModelUnavailableStatus` text changed to remove external-provider wording: *"Local models are
  unavailable. Open the model manager to install or repair the safe local artifacts."* Git hash
  `b8a38566cc73`; SHA-256
  `7a42af214e57c92f8d15e8d7c81c4e83f8824466bd2e72c7647d1c5eff3a7d42`.

### Recovery routing to Home Models (modified)
- `shared/src/Haven.Desktop/Services/SpacesBrowseAppContentSession.cs` — `WireChatRecovery`
  (used by both `NewChatSessionFactory()` and `ExecuteChatOnUiAsync`) now routes the request through
  `_hostQueueNavigation` to **`9to1://home/models`** (`AppContentAddress.Parse` → Namespace=NativeApp,
  Authority=home, segments=`["models"]`) in a new tab, guarded by
  `_hostQueueNavigation is null || _closing || !_published`. Settlement = `page.JoinAdmittedOriginalWorkAsync()`
  (**real**, never `Task.CompletedTask`). Git hash `57158095aeac`; SHA-256
  `1a934e4b622b6437670e4eb7d2cb90e2a339b35127d86689cb3bc45e7ccfa80c`.
- `shared/src/Haven.Desktop/Overlay/OverlayChatSessionFactory.cs` — `WireHost(page)` moved **inside**
  the disposal-protected `try` in `CreatePageAsync` so a host wiring fault disposes the page (no
  leaked page); `WireHost` seam still applies to every created page. Git hash `3fa7acfceb1b`;
  SHA-256 `9e4573639b64c8c9b754d45aa35b515a36c75aa73ba678d562b51b89605f4deb`.
- `shared/src/Haven.Desktop/Services/UnifiedHomeContentFactory.cs` — `CreateSession` now admits
  `home` authority with 0 or exactly `["models"]` segments (the destination for the recovery route).
  Git hash `19031f8ecda2`; SHA-256
  `a253e57268c73691e6a37c98f49e8ffd4865547cae7ce14c4b51774d8c295808`.
- `shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomePage.cs` — `_section` selection honors the
  `models` segment (`home.models`/`models` → Models/Discover surface). Git hash `0f17ca00369c`;
  SHA-256 `61a19de8841f5af0b20ad0a7b969a029a243da24925d2754bc7a7143f4abe96e`.

### Focused owning tests (authored; NOT RUN)
- `Browse/Host.Tests/OverlayChatInitializationTests.cs` — added
  `Rendered_recovery_button_routes_to_home_models_while_draft_survives` (drives the **rendered CUI**
  recovery button, asserts Home Models destination via `authority=="home"` + segments `["models"]`,
  draft and truthful send-refusal survive) and `Recovery_button_is_hidden_when_models_are_available`
  (hidden when model Available). Helpers `RecoveryView(page)` → `Scene(page).RecoveryView` and
  `RecoveryButton(page)` → `NativeButton`. Existing recovery test kept. Git hash `7078d984f6ae`;
  SHA-256 `d2f6824bb32d200b53f35c296724eb84eb7b39b23cf134e777c678777c55cc9a`.

## Evidence — source hashes
git hash-object (12 chars) for the pass-3 surface:
- ChatRecovery.cui `4ba94add839c`
- ChatRecoveryCuiView.cs `3c9174c283b6`
- ChatHavenScene.cs `fc8bc72b5dd6`
- NewChatPage.cs `b8a38566cc73`
- SpacesBrowseAppContentSession.cs `57158095aeac`
- UnifiedHomeContentFactory.cs `19031f8ecda2`
- UnifiedHomePage.cs `0f17ca00369c`
- OverlayChatSessionFactory.cs `3fa7acfceb1b`
- Haven.Desktop.csproj `00003c6e7a4e`
- Browse/Host.Tests/OverlayChatInitializationTests.cs `7078d984f6ae`

(Pass-2 Home Core hashes are in the preserved pass-2 handoff and are unchanged this session.)

## Not run (no .NET slot)
- Any `dotnet` build/restore/test for the above. All tests remain **NOT RUN**.
- Exact commands to run when a serialized slot is granted (lane 5 has none):
  - `dotnet build 9to1 Workspace/Home/Source/Home/Home.csproj -m:1`
  - `dotnet test 9to1 Workspace/Home/Tests/Home.Tests.csproj -m:1 --filter HomeDulcheRuntimeServiceTests`
  - `dotnet build 9to1 Workspace/Browse/Host.Tests/Host.Tests.csproj -m:1`
  - `dotnet test 9to1 Workspace/Browse/Host.Tests/Host.Tests.csproj -m:1 --filter OverlayChatInitializationTests`
  - Verify `Haven.Desktop` build embeds `Haven.Desktop.Chat.Recovery.cui` (resource name match).

## Residual blockers / unresolved decisions
- No production `llama-server` SHA-256 pin exists and no real model manifest/blob is supplied, so
  `RuntimeVerified`/`ModelVerified` stay truthfully blocked in production (`Ready` unreachable until
  real artifact metadata is provided). Not fabricated.
- `MainView.OpenHomeDiscoverAsync` (→ HomeDiscoverPage tab) and `OverlayWorkspaceController`
  (honest-toast) recovery paths are **not yet unified** with the genuine `9to1://home/models`
  routing. `MainView.axaml.cs` retains the old recovery path. This is a **root/lane-4 merge** item:
  align those recovery semantics with the genuine Home Models routing now owned by the shared
  recovery CUI + `WireChatRecovery` seam. Left flagged, not silently rewritten by lane 5 (lane 4
  owns shared controls/host/factory seams).
- No .NET build available; all pass-3 state is source-only and **NOT RUN**. Handoff records
  BLOCKED/NOT RUN and the next root-owned check above.

## Next root-owned check
1. Build `Home.csproj` + run `HomeDulcheRuntimeServiceTests` (pass-2 surface preserved).
2. Build `Host.Tests.csproj` + run `OverlayChatInitializationTests` (pass-2 + pass-3 recovery/CUI tests).
3. Verify `Haven.Desktop` build embeds `Haven.Desktop.Chat.Recovery.cui`.
4. Lane-4/root merge: align `MainView.OpenHomeDiscoverAsync` + `OverlayWorkspaceController` recovery
   with genuine `9to1://home/models` routing.
