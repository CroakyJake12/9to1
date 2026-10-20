# extra09 integration patch (pass 2) — exact host/binding/scene wiring

Lane: Mercury extra09 (permissions). Scope: reusable typed CUI AI-access/permission selector.

This document is the **exact, apply-able integration patch** against the identified real source
signatures. The shared originals below are read-only and were NOT edited by this lane; every
change ships as a patch here for root to apply. All execution is NOT RUN until root allocation.

New reusable component (owned by this lane, present in the tree):

| File | Role |
| ---- | ---- |
| `framework/CUI/AI/AppAiAccessSelector.cs` | Typed adapter over real `FloatingAiBarState` + typed observation mapping (DEC-001-safe wording). |
| `framework/CUI/AI/UI/AiAccessSelector.cui` | Authored canonical CUI selector. |
| `framework/CUI/AI.Tests/AppAiAccessSelectorTests.cs` | Causal tests (compiles; root runs). |
| `work/extra09-handoff/AppAiAccessSelectorBindings.cs` | PROPOSED host bridge (INotifyPropertyChanged invalidation, subscribe-once, fenced dispose). |
| `work/extra09-handoff/AiAccessSelectorScene.cs` | PROPOSED host loader of the packaged selector resource. |
| `work/extra09-handoff/AppAiAccessSelectorBindingsTests.cs` | PROPOSED host bridge test (marked inclusion). |

---

## 0. Real signatures this patch is anchored to (read, not changed)

- `HomeAppAiServices.Create(IAppAiContext, IAppAiActions, IAppAiDatabaseMutationGuard? = null)` returns
  the single owner `FloatingAiBarState` (HomeAppAiServices.cs:52).
- `FloatingAiBarState.SetAccessMode(AppAiAccessMode)` / `SetReadOnly()` / `SetWriteMode()` /
  `AccessMode` / `Changed` (framework/CUI/AI/FloatingAiBarState.cs).
- `FloatingAiBarScene.ReadSource()` reads the packaged bar resource; the selector resource
  `NineToOne.Cui.AI.UI.AiAccessSelector.cui` is packaged by the existing
  `<EmbeddedResource Include="UI\**\*.cui" />` wildcard.
- `CuiNativeScene(string AppId, string Title, string Surface, CuiDocument Document, ...)` and
  `CuiDocumentSource(string markup, string source)` (Runtime).
- `ChatComposerCuiView` public ctor `(IChatComposerSource source, string? owningAppId = null,
  CuiThemePreferenceContext? themePreference = null)` and internal ctor
  `(source, ICuiSceneReadiness readiness, string? owningAppId = null, ... themePreference = null)`.
- `ChatComposerBindings` implements `ICuiWritableBindingContext, ICuiActionDispatcher,
  ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged`.

---

## 1. Owner state call-site wiring (exact)

The single production owner state is the `FloatingAiBarState` returned by `HomeAppAiServices.Create`
(or `AppAiCoordinatorFactory.Create`). At the call site, build the selector, bridge and scene from
that **same** state (never a second bar/state):

```csharp
using NineToOne.Cui.AI;
using Haven.Desktop.Views.Pages.Chat; // proposed host files

FloatingAiBarState ownerState = homeAppAiServices.Create(context, actions); // existing owner state
var accessSelector = new AppAiAccessSelector(ownerState, statusSource: null); // statusSource optional
var accessBindings = new AppAiAccessSelectorBindings(accessSelector);
string selectorMarkup = AiAccessSelectorScene.ReadSource(); // packaged resource identity
```

If no `IAppAiAccessStatusSource` exists for a host, pass `null` — the selector shows no status and
never invents one. This call-site is the ONLY place a `FloatingAiBarState` is consumed for the
selector; there is no unconnected second bar.

---

## 2. Floating-bar host mount (compilable typed mount adapter)

Truthful dependency: **this worktree contains no rendering host that currently mounts the
`FloatingAiBar` scene** (no CUI scene/host consumes `FloatingAiBarScene.ReadSource()`).
Therefore this lane cannot claim host acceptance for the bar. It supplies the compilable typed
mount adapter for a genuinely supplied owner state:

```csharp
var document = new CuiDocumentSource(selectorMarkup, "AiAccessSelector.cui").Document;
var scene = new CuiNativeScene("spaces.chat.ai-access", "AI access", "Chat",
    document, accessBindings, accessBindings, readiness)
{
    ThemePreference = themePreference,
};
var host = new CuiSceneHost(registry);
await host.ShowAsync(scene, cancellationToken);
```

When the real bar render host lands, it mounts the selector the same way (replacing the legacy
unselected `SetReadOnly`/`SetWriteMode` buttons). No acceptance is claimed for the bar in this
lane.

---

## 3. Shared composer integration — exact diffs (apply-able, not applied)

### 3a. `ChatComposerCuiView.cs`

Keep the existing ctor overloads intact; add a new ctor that consumes the shared selector, and
extend the internal ctor with a **trailing** optional parameter (positional callers unchanged).

```diff
     public ChatComposerCuiView(IChatComposerSource source, string? owningAppId = null,
         CuiThemePreferenceContext? themePreference = null)
         : this(source, ChatComposerReadiness.Instance, owningAppId, themePreference)
     {
     }

+    /// <summary>Integration overload: threads the shared AI access selector into the composer.</summary>
+    public ChatComposerCuiView(IChatComposerSource source, AppAiAccessSelector aiAccessSelector,
+        string? owningAppId = null, CuiThemePreferenceContext? themePreference = null)
+        : this(source, ChatComposerReadiness.Instance, owningAppId, themePreference, aiAccessSelector)
+    {
+    }

-    internal ChatComposerCuiView(IChatComposerSource source, ICuiSceneReadiness readiness,
-        string? owningAppId = null, CuiThemePreferenceContext? themePreference = null)
+    internal ChatComposerCuiView(IChatComposerSource source, ICuiSceneReadiness readiness,
+        string? owningAppId = null, CuiThemePreferenceContext? themePreference = null,
+        AppAiAccessSelector? aiAccessSelector = null)
     {
-        _bindings = new ChatComposerBindings(source ?? throw new ArgumentNullException(nameof(source)));
+        _bindings = new ChatComposerBindings(source ?? throw new ArgumentNullException(nameof(source)),
+            aiAccessSelector is null ? null : new AppAiAccessSelectorBindings(aiAccessSelector));
         _readiness = readiness ?? ChatComposerReadiness.Instance;
         ...
     }
```

In `Dispose()`/`DisposeAsync()` dispose the bindings (which dispose the bridge; the shared
selector and bar state are owned elsewhere and must NOT be disposed here).

### 3b. `ChatComposerBindings.cs`

Route the selector's `Access.*` paths and `access.*` actions through the existing binding/action
chain and forward notifications over the existing `INotifyPropertyChanged`:

```diff
     public sealed class ChatComposerBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
         ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
     {
         private readonly IChatComposerSource _source;
+        private readonly AppAiAccessSelectorBindings? _aiAccess;
         ...

-        public ChatComposerBindings(IChatComposerSource source)
+        public ChatComposerBindings(IChatComposerSource source,
+            AppAiAccessSelectorBindings? aiAccess = null)
         {
             _source = source ?? throw new ArgumentNullException(nameof(source));
+            _aiAccess = aiAccess;
+            if (_aiAccess is not null) _aiAccess.PropertyChanged += OnAiAccessPropertyChanged;
         }
```

In `TryGetValue`, before the trailing `default: return false;`:

```diff
             case "Composer.EmojiIconCandidates":
                 value = ComposerEmojiIconPool.Candidates;
                 return true;
+            case "Composer.HasAiAccessSelector":
+                value = _aiAccess is not null;
+                return true;
             default:
+                if (_aiAccess is not null && _aiAccess.TryGetValue(path, out value)) return true;
                 return false;
         }
     }
```

In `DispatchAsync`, before `default: return ValueTask.FromException(...)`:

```diff
             case "composer.cancel-edit":
                 return _source.CancelEdit();
+            case "access.select-readonly" or "access.select-write" when _aiAccess is not null:
+                return _aiAccess.DispatchAsync(command, parameter, cancellationToken);
             default:
                 return ValueTask.FromException(...);
```

In `IsActionAvailable`:

```diff
             "composer.settings" or "composer.set-action-mode" or "composer.set-response-mode" => true,
+            "access.select-readonly" or "access.select-write" => _aiAccess?.IsActionAvailable(command),
             _ => true
```

Forwarding callback (add once, unsubscribed on dispose):

```csharp
    private void OnAiAccessPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
```

Add `public void Dispose()` (implement `IDisposable`) that unsubscribes `OnAiAccessPropertyChanged`.
`ChatComposerCuiView` calls it on close; it never disposes the shared selector/state.

---

## 4. Bridge test inclusion (explicit)

`work/extra09-handoff/AppAiAccessSelectorBindingsTests.cs` is a PROPOSED source patch for a host
test project that references `CakeOS.Cui.Runtime`, `CakeOS.Cui`, `NineToOne.Cui.AI` and xunit
(e.g. `Haven.Desktop.Tests` or `Browse/Host.Tests`). It is marked as proposed inclusion, not
present executable coverage. Root may run it under a frozen-binary gate while Runtime source moves.

---

## 5. Behavioural invariants (preserved)

- Mode clicks delegate to `FloatingAiBarState.SetAccessMode`; never manufacture a grant, clear a
  real pending approval, or re-execute an action.
- Permission pending/denied/unavailable presentation is backed only by supplied typed observations;
  with no source, no status is shown; wording never implies the mode switch awaits a grant.
- Read-only/Write stay distinct from host Edit/Suggest/View, legacy `PermissionMode`, and
  per-action approval.
- The bridge keeps no duplicate mode cache; it forwards live owner updates once via
  `INotifyPropertyChanged`, fences late notifications after dispose, and never disposes a shared
  selector/state owned by another component.
- Existing composer commands/input/close lifetime are preserved; no second bar/state is created.

---

## 6. NOT RUN / honest remaining host dependencies

- [ ] No .NET/MSBuild/restore/test/app/display executed (all NOT RUN until root allocation).
- [ ] No commit/push; no provider/cap/global/config changes; no extra models/subagents; no images.
- [ ] Root applies patches (Home call-site, ChatComposerCuiView, ChatComposerBindings, host mount)
      then runs: (1) `AI.Tests` compile/test gate, (2) selector render/action with the real bridge.
- [ ] Shared floating bar render host is not present in this worktree — bar acceptance is NOT
      claimed; the typed mount adapter is supplied for the genuine owner state.
