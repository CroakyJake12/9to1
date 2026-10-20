# extra09 integration patch (pass 3) — exact unified patches, honest BLOCKED dependency

Lane: Mercury extra09 (permissions). Shared originals are READ-ONLY; every change ships as a
verified unified `.patch` here for root to apply. All SDK execution is NOT RUN until root allocation.

## Shared preimage hashes (recorded before patching)

| File | SHA-256 |
| ---- | ------- |
| `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs` | `ac26394fe5a7702147214c9115ffddad68c1096082227deb62b466a48968eb60` |
| `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` | `6345c1458f593ef44fb5b82649c8532142b1c7e770b54a37eca2e0054b55cd06` |
| `9to1 Workspace/Home/Source/Home/Core/HomeAppAiServices.cs` | `590504a311b109c0d41d6451da54edd4bbe74c42aa264ed844ef141fe47a9bb6` |

Every patch below is verified with `git apply --check -p1` against these exact preimages.

## Generated unified patches (apply with `git apply -p1`)

| Patch | Applies to | SHA-256 |
| ----- | ---------- | ------- |
| `chat-composer-cuiview.patch` | `ChatComposerCuiView.cs` | `ac9c6f2adbaf7bf839b12bce8473e72465b1e80ba483138965547a825a9b5a52` |
| `chat-composer-cui.patch` | `ChatComposer.cui` | `de23caf0fbdec86377a16d8c642d52c165ca91a7a55bb668f5f7326eb4e99e9f` |
| `add-AppAiAccessSelectorBindings.cs.patch` | NEW `.../Views/Pages/Chat/AppAiAccessSelectorBindings.cs` | `213fae546a5d4c56f06f7929c6839e8daba079861f61c7dcb92167272caa08f7` |
| `add-AiAccessSelectorScene.cs.patch` | NEW `.../Views/Pages/Chat/AiAccessSelectorScene.cs` | `b99b5457e82e073a08925fd1df225937fb999c253ee8e920ce5cf12f27aa3199` |
| `add-AppAiAccessSelectorHostMount.cs.patch` | NEW `.../Views/Pages/Chat/AppAiAccessSelectorHostMount.cs` | `e4a086d7417746d4bec203f027b4189880dd8d29ab42d4a9b4c3f1552cb6f0a4` |
| `add-AppAiAccessSelectorBindingsTests.cs.patch` | NEW `shared/tests/Haven.Desktop.Tests/AppAiAccessSelectorBindingsTests.cs` | `fdf676fe77bc816af2539c6fc4dd2c5813a7acd8190caf2cb6c11ab6f3127205` |

## What the composer patch does

- `ChatComposerCuiView.cs`: adds a public ctor overload `(IChatComposerSource, AppAiAccessSelector,
  string? owningAppId = null, CuiThemePreferenceContext? themePreference = null)` and extends the
  internal ctor with a trailing optional `AppAiAccessSelector? aiAccessSelector = null` (existing
  positional internal callers unchanged). The internal ctor creates the bridge
  `new AppAiAccessSelectorBindings(aiAccessSelector)` and hands it to `ChatComposerBindings`.
  `Dispose`/`DisposeAsync` now dispose the bindings (which dispose the bridge the view created);
  the shared selector and `FloatingAiBarState` stay owned by Home.
- `ChatComposerBindings`: implements `IDisposable`; stores the optional bridge; routes `Access.*`
  paths through `TryGetValue`, adds `Composer.HasAiAccessSelector`, routes the `access.select-*`
  actions through `DispatchAsync`/`IsActionAvailable`, and forwards bridge notifications over the
  existing `INotifyPropertyChanged`. `Dispose` unsubscribes its forwarding callback and disposes
  the bridge (never the shared selector/state).
- `ChatComposer.cui`: mounts an authored `composer-ai-access` row (current-mode label + Read-only/
  Write buttons with `Selected`/`Interactive` + status line), visible only when
  `Composer.HasAiAccessSelector`, preserving all existing composer commands/input/close behavior.

## BLOCKED production dependency (honest)

Search of this worktree finds `HomeAppAiServices.Create` (returns the single owner
`FloatingAiBarState`) but **no production floating-bar render host and no call site that supplies
an owner state to the shared composer**. Therefore:

- This lane does NOT claim production integration and does NOT wire a placeholder
  `homeAppAiServices.Create(context, actions)` call as a real host.
- `AppAiAccessSelectorHostMount.cs` is a **compilable typed mount/scene adapter** that a genuine
  supplied owner can consume (it builds a `CuiNativeScene` over `AiAccessSelectorScene.Load()` and
  `AppAiAccessSelectorBindings`, owns only its bridge, and leaves the shared selector/state to Home).
- The author `AppAiAccessSelectorBindingsTests.cs` is included as an explicit new-file patch to
  `Haven.Desktop.Tests` (which resolves `CakeOS.Cui.Runtime` directly and `NineToOne.Cui.AI`
  transitively via `Haven.Application`). Root must run it after applying the inclusion and the
  bridge/selector patches.

## Verification on the shared source
`git apply --check -p1` reports each patch applies cleanly to its recorded preimage. No shared
file is modified by this lane; only the patch files exist in `work/extra09-handoff/`.
