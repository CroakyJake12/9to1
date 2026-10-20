# extra09 handoff report — real typed CUI AI-access and permission selector

Lane: **Mercury extra09 (lane 9)** — `/workspace/9to1-mercury-extra09-permissions`
Isolated base: `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`.

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
- Actual source starting points reused (read-only, unchanged):
  - `framework/CUI/AI/AppAiContracts.cs` — `AppAiAccessMode`, `AppAiActionRequest.AccessMode`,
    `AppAiApprovalOutcome`, `AppAiActionResult`.
  - `framework/CUI/AI/FloatingAiBarState.cs` — owns actual AccessMode (default ReadOnly),
    `SetAccessMode`/`SetReadOnly`/`SetWriteMode`/`Changed`.
  - `framework/CUI/AI/AppAiCoordinator.cs` — modes `ExecuteAsync`/`StreamAsync` honour AccessMode.
  - `framework/CUI/AI/UI/FloatingAiBar.cui`, `ChatComposerCuiView.cs`, `ChatComposer.cui` —
    shared ownership, not edited; patches supplied in `integration-patch.md`.

## 2. Added files (exclusive direct-edit scope)

| File | SHA-256 |
| ---- | ------- |
| `framework/CUI/AI/AppAiAccessSelector.cs` | `4a052c99bfb51f817805a22e4fb7ef03020479eddc14f9e955f667dce52a3da7` |
| `framework/CUI/AI/UI/AiAccessSelector.cui` | `2d4dbdbd03e0aaf930b44de85718ee9f17f4a82cd9606eee024aa205e3573578` |
| `framework/CUI/AI.Tests/AppAiAccessSelectorTests.cs` | `b943e24351f89e845869944d8353ef501e6e131f036fc722449c6a70268172e9` |

Handoff artifacts (this directory):

| File | Role |
| ---- | ---- |
| `AppAiAccessSelectorBindings.cs` | PROPOSED host-layer CUI bridge (lives in host project). |
| `integration-patch.md` | Exact minimal patches for shared floating bar host + shared composer. |
| `handoff-report.md` | This report. |

`framework/CUI/AI/NineToOne.Cui.AI.csproj` was **not** modified: the existing
`<Cui Include="UI\**\*.cui" />` + `<EmbeddedResource Include="UI\**\*.cui" />` wildcard already
packages `AiAccessSelector.cui`. `AppAiAccessSelector.cs` intentionally has **no** dependency on
CakeOS.Cui.Runtime/Core, so the AI project needs no new reference; the CUI bridge lives in the host.

## 3. API contract (new)

- `enum AppAiAccessStatusKind { None, Pending, Denied, Unavailable }`
- `record AppAiAccessStatus(AppAiAccessStatusKind Kind, string Message)`
- `interface IAppAiAccessStatusSource { AppAiAccessStatus? Current { get; } event EventHandler? Changed; }`
- `static class AppAiAccessStatusMapper` — `FromOutcome(AppAiApprovalOutcome, string?)`,
  `FromActionResult(AppAiActionResult)` (presentational mapping only).
- `sealed class AppAiAccessSelector : IDisposable`
  - ctor `(FloatingAiBarState state, IAppAiAccessStatusSource? statusSource = null)`
  - `event EventHandler? Changed`; `State`, `AccessMode`, `IsReadOnly`, `IsWriteMode`,
    `CurrentModeLabel`, `CanChangeMode`, `Status`, `HasStatus`
  - `SelectReadOnly()`, `SelectWrite()`, `SetMode(AppAiAccessMode)` (delegates; rejects invalid enum)
  - `Dispose()` — unsubscribes `Changed` from state + status source; does not dispose the state.

## 4. Causal tests authored (`AppAiAccessSelectorTests.cs`)

1. Default ReadOnly reflected by selector.
2. Write→ReadOnly roundtrip affects the **actual request AccessMode** (`dulche.LastPrompt.AccessMode`).
3. Denied observation presented while actual owner mode/state preserved.
4. Invalid enum selection rejected (ArgumentOutOfRangeException), owner state untouched.
5. Pending/Unavailable status exposed from typed observations (`AppAiAccessStatusMapper`).
6. Dispose unsubscribes; late state changes not published; post-dispose calls throw.
7. Selector mode stays distinct from host Edit/Suggest/View state.
8. Authored `AiAccessSelector.cui` parses as canonical CUI with selected/focus/disabled bindings,
   current-mode label, status line, and `access.select-readonly`/`access.select-write` actions.

Tests use the real `FloatingAiBarState`/`AppAiCoordinator`/`AppAiContracts` and real parser; no
standalone snapshots mirroring the implementation.

## 5. Dependencies

- Compile: `framework/CUI/AI` project unchanged. Tests reference existing `AI` and `Core`
  projects (existing AI.Tests inclusion). `using CakeOS.Cui` + `CakeOS.Cui.Language` already used
  by `AppAiCoordinatorTests`.
- Runtime renders require host wiring from `integration-patch.md` (root-applied).

## 6. NOT RUN / pending root allocation

- [ ] No .NET/MSBuild/restore/test/app/display executed (all NOT RUN until root allocation).
- [ ] No commit/push; no provider/cap/global config changes; no extra models/subagents; no images.
- [ ] Root must apply `integration-patch.md` host wiring, then execute real render/action workflows.
- [ ] Do not claim real app acceptance until root applies wiring and executes acceptance.

## 7. Key invariants preserved

- Mode changes always delegate to `FloatingAiBarState.SetAccessMode`; no divergent access-mode cache,
  no invented grants, no clearing of real pending approvals, no action re-execution.
- Read-only/Write stay distinct from host Edit/Suggest/View and from per-action approval.
- Pending/denied/unavailable display backed only by real typed observations; absent source ⇒ no status.
- Dispose/unsubscribe prevents stale completion overriding a newer owner; close/rebind lifetime retained.
