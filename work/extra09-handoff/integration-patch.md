# extra09 integration patch — consuming the canonical AI access selector

Lane: Mercury extra09 (permissions). Scope: reusable typed CUI AI-access/permission selector.

This document is the **exact minimal integration patch** for the existing shared floating bar
binding/host and the shared composer. The target files below are **read-only shared ownership**
and were NOT edited by this lane. Root applies these patches and then executes the real
render/action workflows (this lane is NOT RUN until root allocation).

The new reusable component (owned by this lane) is:

| File | Role |
| ---- | ---- |
| `framework/CUI/AI/AppAiAccessSelector.cs` | Typed adapter over the real `FloatingAiBarState` + typed observation mapping. |
| `framework/CUI/AI/UI/AiAccessSelector.cui` | Authored canonical CUI selector (current-mode label, selected/focus/disabled states, status presentation). |
| `framework/CUI/AI.Tests/AppAiAccessSelectorTests.cs` | Causal tests over real sources/adapters. |
| `work/extra09-handoff/AppAiAccessSelectorBindings.cs` | PROPOSED host-layer CUI bridge (lives in the host project, not in `NineToOne.Cui.AI`). |

---

## 1. Shared floating bar host

The shared bar (`framework/CUI/AI/UI/FloatingAiBar.cui`; state `FloatingAiBarState`) owns the
persistent AI Read-only/Write mode. The current bar uses two unselected buttons
(`SetReadOnly`/`SetWriteMode`). Replace them with the canonical selector so the selected/focus/
disabled states and status presentation are authored CUI.

### 1a. Host composition (production wiring)

In the host that already creates the `FloatingAiBarState` (e.g. `HomeAppAiServices.Create` /
`AppAiCoordinatorFactory.Create`), build the selector and its CUI bindings:

```csharp
using NineToOne.Cui.AI;
using Haven.Desktop.Views.Pages.Chat; // proposed host bridge

FloatingAiBarState barState = ...; // the existing live state
var accessSelector = new AppAiAccessSelector(barState, statusSource: /* optional IAppAiAccessStatusSource */);
var selectorBindings = new AppAiAccessSelectorBindings(accessSelector);
```

`statusSource` is optional and must be an actual `IAppAiAccessStatusSource` backed by real broker
observations (mapped with `AppAiAccessStatusMapper`). If none is supplied, the selector shows no
status — it never invents one.

### 1b. FloatingAiBar.cui minimal patch

Within the existing `shared-floating-ai-bar` page, replace the access-mode container:

The authored selector surface (`AiAccessSelector.cui`) is a standalone `<Cui>` document that the
host loads with `AppAiAccessSelectorBindings` as its binding/action context. Compose it into the
floating bar host shell (e.g. as a sub-scene or an authored mount slot) so `Access.*` paths and
`access.select-*` actions resolve to the **same** `FloatingAiBarState`.

Equivalent authored markup (same bindings/actions) for direct inclusion in the bar's page:

```xml
<!-- Mount the shared selector (same live state, no second bar) -->
<Container id="ai-access-selector-surface" accessible-name="AI access mode" Type="Horizontal Stack" CellGap="8">
  <Text id="ai-access-current-mode" text="{Binding Access.CurrentModeLabel}"
        accessible-name="Current AI access mode" font-weight="SemiBold" />
  <Button id="ai-access-readonly" content="Read-only" action="access.select-readonly"
          accessible-name="Set AI access to read-only" Selected="{Binding Access.IsReadOnly}"
          Interactive="{Binding Access.CanChangeMode}" />
  <Button id="ai-access-write" content="Write" action="access.select-write"
          accessible-name="Set AI access to write mode" Selected="{Binding Access.IsWriteMode}"
          Interactive="{Binding Access.CanChangeMode}" />
  <Text id="ai-access-status" text="{Binding Access.StatusMessage}" accessible-name="AI access status"
        IsVisible="{Binding Access.HasStatus}" font-size="12" />
</Container>
```

This replaces the existing `<Text text="{Binding AccessModeLabel}" />` plus the two unselected
`SetReadOnly`/`SetWriteMode` buttons. The bar's host must register `AppAiAccessSelectorBindings`
as the source for the `Access.*` paths and the `access.select-*` actions (shared with the rest of
the bar's commands).

The host must dispose `AppAiAccessSelectorBindings` (and the `AppAiAccessSelector`) when the bar
closes, so late state/status changes never publish into a closed view.

---

## 2. Shared composer integration

The shared composer (`ChatComposerCuiView` + `ChatComposer.cui`) must consume the **same** new
selector and **same** existing state — not a second bar and not an app-specific permission UI.

### 2a. Thread the shared AI access state into the composer

`ChatComposerCuiView` currently receives an `IChatComposerSource`. Add an optional constructor
parameter that accepts the shared `AppAiAccessSelector` (wraps the existing `FloatingAiBarState`):

```csharp
public ChatComposerCuiView(IChatComposerSource source,
                           AppAiAccessSelector? aiAccessSelector = null,
                           string? owningAppId = null,
                           CuiThemePreferenceContext? themePreference = null)
    : this(source, aiAccessSelector, ChatComposerReadiness.Instance, owningAppId, themePreference)
{
}

// Internal ctor builds _aiAccessBindings = new AppAiAccessSelectorBindings(aiAccessSelector)
// when aiAccessSelector != null, and registers it into the scene binding/action chain.
```

### 2b. ChatComposer.cui minimal patch

Add the selector (compact, same live state) into the composer controls row:

```xml
<Container id="composer-access-selector" Type="Horizontal Stack" CellGap="6"
          IsVisible="{Binding Composer.HasAiAccessSelector}">
  <Text text="{Binding Access.CurrentModeLabel}" font-weight="SemiBold" font-size="12" />
  <Button action="access.select-readonly" content="Read-only" font-size="12"
          Selected="{Binding Access.IsReadOnly}" Interactive="{Binding Access.CanChangeMode}" />
  <Button action="access.select-write" content="Write" font-size="12"
          Selected="{Binding Access.IsWriteMode}" Interactive="{Binding Access.CanChangeMode}" />
  <Text text="{Binding Access.StatusMessage}" IsVisible="{Binding Access.HasStatus}" font-size="12" />
</Container>
```

Add `"Access.CurrentModeLabel"`, `"Access.IsReadOnly"`, `"Access.IsWriteMode"`,
`"Access.CanChangeMode"`, `"Access.HasStatus"`, `"Access.StatusMessage"` to
`ChatComposerBindings.TryGetValue`, `"access.select-readonly"`/`"access.select-write"` to
`DispatchAsync`, and `"Composer.HasAiAccessSelector"` to `TryGetValue`/`IsActionAvailable` so the
selector row is only shown when a real AI access state is attached. Close/dispose the bindings
with the composer lifetime.

> Important: do NOT conflate this with `composer.set-action-mode` (`All/Basic/JustChat` model
> capability modes) or the legacy `PermissionMode.Ask/AutoSafe/FullAccess`. The AI access mode is
> the persistent Read-only/Write contract. Approval/permission status is rendered only from real
> `IAppAiAccessStatusSource` observations.

---

## 3. Behavioural invariants (preserved)

- Mode clicks delegate to `FloatingAiBarState.SetAccessMode` — they never manufacture a grant,
  clear a real pending approval, or re-execute an action.
- Permission pending/denied/unavailable presentation is backed only by supplied typed observations;
  with no source, no status is shown.
- Read-only/Write stay distinct from host Edit/Suggest/View state and from per-action approval.
- The selector never caches a divergent access mode; it always reflects live state.
- Dispose/unsubscribe unregisters `Changed` handlers so late state/status cannot publish into a
  closed selector, and stale completions never overwrite the current owner.
