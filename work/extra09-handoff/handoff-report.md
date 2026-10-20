# extra09 handoff report (pass 2) — real typed CUI AI-access and permission selector

Lane: **Mercury extra09 (lane 9)** — `/workspace/9to1-mercury-extra09-permissions`
Isolated base: `1c080a7bbd1c6b40b09a7eb73f440de9cea7b81d` (parent `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`).

Owner authority: **10 additional Mercury workers authorised (target 16 total including Thunder),
superseding the older six-worker cap; $5 spending cap unchanged.** Single-worker concurrency prose
is superseded for concurrency count only. This lane implemented source/tests/static checks only;
**all execution is NOT RUN until root allocation** (no .NET/MSBuild/restore/test/app/display).

## 1. Source identity & authority read

- Canonical spec `docs/specification/9to1-development-specification-v2.md`, SHA-256
  `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96` (provenance.json).
- Read applicable sections: shared persistent AI bar (2636–2690), Home permission/trust broker
  (1296 onward), CUI declarative authoring boundaries, shared Chat controls & DEC-001 (16913).
- Read owner references: `2026-10-20-continuous-composer`, `2026-10-20-single-app-implementation`,
  `2026-10-20-chat-composer-dulche`, `2026-10-20-composer-icons`.
- DEC-001 is **open**: no invented Ask-for-Approval / trust / grant-lifetime / automatic-approval
  semantics. The selector presents only real typed observations and never manufactures a grant.
- Real source anchors reused (read-only, unchanged): `AppAiContracts.cs`, `FloatingAiBarState.cs`,
  `AppAiCoordinator.cs`, `FloatingAiBar.cui`, `ChatComposerCuiView.cs`, `ChatComposer.cui`,
  `HomeAppAiServices.cs`, `CuiNativeScene`/`CuiDocumentSource` (Runtime).

## 2. Added files (exclusive direct-edit scope)

| File | SHA-256 |
| ---- | ------- |
| `framework/CUI/AI/AppAiAccessSelector.cs` | `915faf92fb04ef12166e44a59e8d7ce9f9e3f4bbcffa6cf2a4285048cc252eb9` |
| `framework/CUI/AI/UI/AiAccessSelector.cui` | `2d4dbdbd03e0aaf930b44de85718ee9f17f4a82cd9606eee024aa205e3573578` |
| `framework/CUI/AI.Tests/AppAiAccessSelectorTests.cs` | `acae8fc2bc40a0d7c50fe6bb1529436b1ea4e347ea1b8ffd69dda10aca591b86` |

Handoff artifacts (`work/extra09-handoff/`):

| File | SHA-256 | Role |
| ---- | ------- | ---- |
| `AppAiAccessSelectorBindings.cs` | `aaa56cee1d7dffb95c07a6a20087e6be57aafb1bbfaa611db653513f2234f319` | PROPOSED host bridge (INotifyPropertyChanged invalidation, subscribe-once, fenced dispose). |
| `AiAccessSelectorScene.cs` | `6c0a11d365027999f7cde765aa6ac90a52c8fc165746e4894f002f3e41e1c6e5` | PROPOSED host loader (packaged resource identity). |
| `AppAiAccessSelectorBindingsTests.cs` | `5495feb0fce9458bd7eaae108372e0698e2ad72171d4513674d0142dfce4a8b5` | PROPOSED host bridge test (marked inclusion). |
| `integration-patch.md` | `927ceda831bd4a982f0f2515040d5b2f23d76323bd6554d6a79278f7aa9535dd` | Exact apply-able host/binding/scene patches. |
| `handoff-report.md` | `737c9b743780c00ac4cfa41a90c2b76631c139c345d8b9860c51c5b7ddc8573a` | This report. |

`framework/CUI/AI/NineToOne.Cui.AI.csproj` was **not** modified: the existing
`<Cui Include="UI\**\*.cui" />` + `<EmbeddedResource Include="UI\**\*.cui" />` wildcard already
packages `AiAccessSelector.cui` as `NineToOne.Cui.AI.UI.AiAccessSelector.cui`. The AI project has
no new dependency; the CUI bridge lives in the host (which already references the Runtime).

## 3. Pass-2 corrections

1. **Test compile typo fixed**: `_.AccessMode` → `state.AccessMode`; real SubmitAsync mode-request
   roundtrip and exact invalid-enum/rejection tests preserved.
2. **Markup test now uses the packaged selector resource** (`typeof(FloatingAiBarScene).Assembly
   .GetManifestResourceStream("NineToOne.Cui.AI.UI.AiAccessSelector.cui")`) instead of an
   unverified deployment path.
3. **Bridge invalidation fixed**: `AppAiAccessSelectorBindings` now implements the runtime's actual
   observable contract (`INotifyPropertyChanged`, as `ChatComposerBindings` uses), subscribes once
   to `AppAiAccessSelector.Changed`, forwards current-owner updates (null property name), keeps no
   duplicate mode cache, fences late notifications after dispose, and unsubscribes without ever
   disposing a shared selector/state owned by another component. Causal bridge test provided
   (`AppAiAccessSelectorBindingsTests.cs`, marked proposed inclusion).
4. **DEC-001 wording fixed**: `AppAiAccessStatusMapper.FromOutcome` defaults now use neutral
   action/permission observation wording; nothing implies switching the persistent mode itself
   acquires/awaits a grant. Write remains distinct from per-action approval, host state, and
   legacy `PermissionMode`.
5. **Exact integration patches** replace placeholder prose: real call-site wiring over the single
   owner `FloatingAiBarState`, `CuiDocumentSource`/`CuiNativeScene` mount snippet, and apply-able
   diffs for `ChatComposerCuiView` (new ctor overload + trailing optional internal param, existing
   positional callers unaffected) and `ChatComposerBindings` (Access.* path/action routing +
   INotifyPropertyChanged forwarding). Shared originals remain read-only.

## 4. API contract (new)

- `enum AppAiAccessStatusKind { None, Pending, Denied, Unavailable }`
- `record AppAiAccessStatus(AppAiAccessStatusKind Kind, string Message)`
- `interface IAppAiAccessStatusSource { AppAiAccessStatus? Current { get; } event EventHandler? Changed; }`
- `static class AppAiAccessStatusMapper` — `FromOutcome(AppAiApprovalOutcome, string?)`,
  `FromActionResult(AppAiActionResult)` (presentational mapping only; DEC-001-safe wording).
- `sealed class AppAiAccessSelector : IDisposable`
  - ctor `(FloatingAiBarState state, IAppAiAccessStatusSource? statusSource = null)`
  - `event EventHandler? Changed`; `State`, `AccessMode`, `IsReadOnly`, `IsWriteMode`,
    `CurrentModeLabel`, `CanChangeMode`, `Status`, `HasStatus`
  - `SelectReadOnly()`, `SelectWrite()`, `SetMode(AppAiAccessMode)` (delegates; rejects invalid enum)
  - `Dispose()` — unsubscribes `Changed` from state + status source; does not dispose the state.

## 5. Causal tests authored

`AppAiAccessSelectorTests.cs` (AI.Tests, compiles; root runs): default ReadOnly; Write→ReadOnly
roundtrip affects actual request `AccessMode`; denied observation presented while actual owner state
preserved; invalid-enum rejection; pending/unavailable status from typed observations; dispose
unsubscribes; selector mode distinct from host editing state; packaged-resource markup parse test.
`AppAiAccessSelectorBindingsTests.cs` (proposed host inclusion): live-mode exposure + notification
forwarding, dispatch delegation to real mode change, unknown-command rejection, read-only paths not
writable, dispose-unsubscribes-without-disposing-shared-selector.

## 6. NOT RUN / pending root allocation

- [ ] No .NET/MSBuild/restore/test/app/display executed (all NOT RUN until root allocation).
- [ ] No commit/push; no provider/cap/global/config changes; no extra models/subagents; no images.
- [ ] Root applies `integration-patch.md` (Home call-site, ChatComposerCuiView, ChatComposerBindings,
      host mount) then runs: (1) actual `AI.Tests` compile/test gate, (2) selector render/action with
      the real bridge.
- [ ] Shared floating bar render host is **not present** in this worktree — bar acceptance is NOT
      claimed; the typed mount adapter is supplied for the genuine owner state.

## 7. Key invariants preserved

- Mode changes delegate to `FloatingAiBarState.SetAccessMode`; no divergent mode cache, no invented
  grants, no clearing of real pending approvals, no action re-execution.
- Read-only/Write stay distinct from host Edit/Suggest/View, legacy `PermissionMode`, and per-action
  approval.
- Pending/denied/unavailable display backed only by real typed observations; absent source ⇒ no status;
  wording never implies the mode switch awaits a grant.
- Bridge forwards live owner updates once via `INotifyPropertyChanged`, fences late notifications, and
  never disposes a shared selector/state; close/rebind lifetime retained.
