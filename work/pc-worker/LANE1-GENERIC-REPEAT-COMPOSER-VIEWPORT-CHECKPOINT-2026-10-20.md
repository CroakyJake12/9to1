# Lane-1 Checkpoint — Generic Repeat composition repair + composer emoji viewport

**Date:** 2026-10-20
**Worker lane:** 1 of 6 (chat/CUI lane)
**Worktree:** `/workspace/9to1-chat-cui`
**Branch:** `codex/cloud-native-chat-cui-20261020` (HEAD `ddb2a8b8`)
**Model:** Mercury 3 (mercury/mercury-3)

## Scope (assigned and delivered)
1. Repair the generic `Repeat` layout-composition defect: a `Repeat` directly under a
   `CuiContainerControl` must emit its generated item roots **as individual cells into the owning
   Container's own layout** (Wrap/Horizontal Stack/Vertical Stack/Grid), not into a hidden vertical
   sub-panel.
2. Make the emoji picker a complete, usable, **bounded viewport** with real full-catalogue paging,
   preserving the existing emoji trigger/allowlist work.

## Authority
- Canonical spec `docs/specification/9to1-development-specification-v2.md`
  (SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`):
  - Nonvisual constructs / core elements: lines 1840–1858.
  - Repeated UI: lines 2383–2395 ("Repeat creates one generated UI instance per source item and
    MUST preserve item identity across live collection additions/removals/reordering"). The spec does
    **not** define a hidden panel; the repair makes generated instances participate in the owning
    Container's declared layout while preserving keyed identity.
- Owner corrections read: `2026-10-20-composer-icons/owner-correction.json`,
  `2026-10-20-floating-format-bar/owner-correction.json`,
  `2026-10-20-chat-composer-dulche/owner-correction.json`,
  `2026-10-20-single-app-implementation/owner-correction.json`.

## Change summary

### Framework — generic container-mode Repeat (`framework/CUI/Runtime`)
- `CuiControlLoader.cs`
  - `LoadComponent` threads `parent` into child loading; `LoadRepeat(component, parent)` branches.
  - Canonical container mode: when `!explicitPanel` and `parent is CuiContainerControl`, a
    `ContainerCellRepeatHost` is created and each generated item root is keyed-added into the owning
    Container's layout. No hidden panel, no `ApplyProperties`/theme on a phantom host. Returns a
    `CuiRepeatFragment` sentinel that `AddChild` skips (bookkeeping only).
  - Legacy `Panel=` (and non-container parents) unchanged on the historical nested-panel path for
    compatibility (`LegacyPanelRepeatHost`).
  - New `ICuiRepeatHost` abstraction (`Count/IndexOf/Insert/Remove/Move/IsGrid`) so
    `ReplaceRepeatItems` works for both paths with stable keyed identity (no clear/re-add).
  - `CuiRepeatState` now holds `ICuiRepeatHost` + `HostControl`; `DetachControlTree` uses
    `HostControl` for teardown membership.
- `CuiContainerControl.cs`
  - `InsertAuthoredChild(index, child)`, `MoveAuthoredChild(from,to)`, `AuthoredChildCount`;
    `AddChild` delegates to `InsertAuthoredChild`. All preserve stable cells, subscriptions, and
    physical attachment order.

### Composer emoji picker (bounded viewport + full-catalogue paging)
- `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui`
  - `emoji-grid-viewport`: `Wrap` with `VerticalScrolling="True"`, `MaxWidth="320"`,
    `MaxHeight="150"` so the grid genuinely wraps into multiple columns/rows **and** overflows a
    bounded vertical viewport (real scroll). `HorizontalScrolling="False"`.
  - New `emoji-pager` (`Prev`/label/`Next`) + `composer.emoji-prev`/`composer.emoji-next` actions.
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs`
  - Paging/query state: `EmojiPageSize = 48`, `EmojiPageEntries`, total/start/end metadata,
    `HasPrevious/HasNext`, page label; query change resets+clamps page.
  - Picker search spans **names, groups, and exact glyphs** (matches the canonical controller's
    search semantics) so flags and trigger-excluded sequences stay reachable in the picker while the
    random-trigger icon pool still applies the allowlist/flag/suggestive filters.

### Tests (authored; NOT RUN)
- `framework/CUI/Runtime.Tests/CuiRepeatContainerLayoutTests.cs` — 4 tests:
  1. wrap: item roots flow into owning Wrap as individual cells with static siblings, multi-row wrap,
     distinct X on a row, real hit targets;
  2. keyed add/remove/reorder preserves instance identity and positions;
  3. two repeat ranges + static siblings emit into the owning container in one authored order;
  4. legacy `Panel=` keeps the nested host-panel compatibility escape.
- `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` — 3 tests:
  1. picker opens a real multi-column Wrap grid inside a bounded scroll viewport (overflow + offset
     change);
  2. realized count stays bounded (<= 48) while full-catalogue paging reaches first/last/flags and
     search keeps trigger-excluded sequences;
  3. pointer click on a noninitial, emoji inserts exact Unicode once, preserving draft/caret/input
     identity/focus.

## File hashes (SHA-256)
| File | Hash |
| --- | --- |
| `framework/CUI/Runtime/CuiControlLoader.cs` | `706b8bc09cc6565f82bf5cf26c618de0b398d6cb79f9709ccd43082259da92f5` |
| `framework/CUI/Runtime/CuiContainerControl.cs` | `d487844ef36967aef6aeffb62d8126afbb6fb2c695e9ae42ce1e7e9e869fb290` |
| `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` | `bd858a236bfa98f50b87fb2a62526ed5814124ed8f67b7fbef3e3cf678682687` |
| `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Chat/ChatComposerCuiView.cs` | `00e0bdef38a3aa44dd1d4fca1a652d3a89129071bf995f3588cd6da8315f9a4e` |
| `framework/CUI/Runtime.Tests/CuiRepeatContainerLayoutTests.cs` | `70eb82667e3b23e6d566ea370f1b4330c5f79deb58313e2b7b5a5827c1f1af35` |
| `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` | `05010ce8ee78159db8182eb46bd20604805512a476e79af64e5871e12f25b3fa` |

All changes are uncommitted in the worktree. Prior state was clean at HEAD `ddb2a8b8`.

## Behavior-change scope (documentation)
The container-mode path now activates for any `Repeat` that is a direct child of a `Container` without
a `Panel=` attribute. Existing `.cui` surfaces that match this pattern and now emit into the owning
Container's layout (behavior change from hidden vertical panel → real container layout) include, e.g.:
- `Assistants/NativeUI/UI/AssistantConfiguration.cui`, `AssistantConversation.cui`
- `Stacks/NativeUI/UI/StacksWorkspace.cui`
- `Mail/UI/MailWorkspace.cui` (has `Type`/no `Panel` under a container)
- `Spaces/UI/Chat/ChatSpace.cui` (transcript), `ChatComposer.cui` (chips, emoji grid)
- `Spaces/UI/Home/SpacesHome.cui` (custom spaces, switcher, recents)
- `Browse/NativeUI/UI/BrowseWorkspace.cui` (bookmarks/history; tabs use explicit `Panel=` → legacy)
- `shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomeApps.cui` (uses explicit `Panel=` → legacy)

For vertical-stack containers this is visually equivalent (cells already ordered identically); the
repair is most visible where the owning container is a `Wrap`/`Grid` (items now genuinely participate
in that layout). These other surfaces are the responsibility of their owning lanes; lane-1 only
delivered the framework primitive + the composer picker.

## Test status
- `CuiRepeatContainerLayoutTests` (framework layer): **authored, NOT RUN** — requires a root
  build/restore/test slot (no .NET allowed to this worker).
- `ChatComposerEmojiPickerViewportTests`: **authored, NOT RUN** — additionally depends on root/lane-2
  rebuilding the compiled artifact `CUI.Compilation.v1` from the updated `ChatComposer.cui`; cannot
  pass until that artifact reflects the new viewport/pager markup.

## Remaining gates / open items
- **Native FAIL is NOT fixed by source alone.** The observed clipping (single vertical emoji column from
  the legacy hidden panel) is addressed structurally; acceptance requires root's independent native
  validation of the rebuilt artifact.
- Compiled-artifact rebuild must precede the three composer picker tests.
- Existing strict regressions must stay green: `CuiTranscriptRepeatCapabilityTests.cs`,
  `CuiRepeatedInputFocusTests.cs`, `CuiRepeatedActionScopeTests.cs`, `CuiPerItemTemplateTests.cs`,
  `HostComposerLifetimeTests.cs` (the `Panel=`/non-container paths were left untouched for this).
- No commit/push performed (per worker constraints).

## Next move for coordinator
1. Grant lane-1 a .NET source-test slot to run `CuiRepeatContainerLayoutTests` (no artifact dependency).
2. Rebuild `CUI.Compilation.v1` from the updated `ChatComposer.cui`, then run
   `ChatComposerEmojiPickerViewportTests`.

---

## Resume Pass 3 — coherent typed query integration (same session)

**Date:** 2026-10-20 (later turn, exact checkpoint as537f2cec285/… / freeze e2cf5571933c)
**Scope completed this pass (source/tests only; NOT RUN by worker):**

1. **Runtime/state/query contract (generic, CUI-authored).** New `framework/CUI/Runtime/CuiQueryViewport.cs`
   adds the canonical bounded-paged-query runtime scope:
   - `CuiPageCursor` (opaque token), `CuiPagedResult`, `ICuiPagedSource` (typed data-only port).
   - `CuiQueryViewport : CuiContainerControl` owns the **query string + cursor** (runtime state) and
     exposes authored bindings `Items`, `TotalCount`, `HasPrevious`, `HasNext`, `PageStart`,
     `PageEnd`, `PageLabel`, `Query`. It implements `ICuiWritableBindingContext` (search input
     write-back), `ICuiRepeatItemBindingContext` (delegates item-field resolution to the app host)
     and `ICuiActionDispatcher` (handles `composer.emoji-prev/next`; forwards everything else to the
     app host). It holds NO per-view page cache beyond the single current window and owns no control
     trees — children are authored CUI.
   - Registered as `RegisterControlType("QueryViewport", …)` in `CuiControlRegistry` (compiler and
     runtime share this registry, so **no compiler file change is required**; the compiled artifact
     must simply be rebuilt from the updated `ChatComposer.cui`).
   - `CuiContainerControl` unsealed so the viewport can subclass it.

2. **Loader scope wiring (Runtime only).** `CuiControlLoader`:
   - When loading a `CuiQueryViewport` subtree, captures the parent context/dispatcher, resolves its
     typed `ICuiPagedSource` from `Composer.EmojiCatalogueSource`, sets `InnerContext/InnerDispatcher/
     RefreshHost`, and pushes the viewport as the active binding context while loading children.
   - `ObserveActionDispatch` resolves the nearest `CuiQueryViewport` ancestor at click time and routes
     those controls' actions through the viewport (which forwards non-pager commands to the app host).

3. **App C# page/query/cursor state REMOVED.** `ChatComposerCuiView.cs`:
   - Deleted `EmojiPageSize`, `_emojiPage`, `_emojiQuery`, `_emojiCursor`, `ClampEmojiPage`,
     `AllFilteredEmoji`, `EmojiPageEntries`, `EmojiPageLabel`, and the page/query/label binding
     paths and dispatch cases.
   - Added `Composer.EmojiCatalogueSource` → a typed `ICuiPagedSource` adapter over the pure
     `ComposerEmojiCatalogueQuery` engine (data-only full-catalogue filtering incl. flags/suggestives;
     trigger allowlist still applies only to the random-trigger icon pool).
   - `Composer.EmojiIconCandidates` (random icon pool) unchanged.

4. **CUI authoring.** `ChatComposer.cui` emoji panel now uses `<Container Type="QueryViewport">` with
   the search input two-way to `Query`, the keyed grid Repeat bound to `Items`, and Prev/Next controls
   bound to `HasPrevious/HasNext/PageLabel`. Full 3,944-entry reach, flags/suggestive inclusion,
   bounded realization (48/page), scroll viewport, and editor insertion/focus/undo/lifetime unchanged.

5. **Tests.**
   - `CuiRepeatContainerLayoutTests`: uses `CuiBundledFontTestApplication` (Skia+Headless, font
     initialization consistent with canonical fixtures, fixes the InvalidCastException geometry
     failures). Wrap fixture width corrected 200→260 so the static before-button + first three repeat
     cells share row 0 by the authored layout baseline, while genuine multi-row/multi-column wrap,
     keyed identity, hit targets and strict assertions are retained.
   - `ChatComposerEmojiPickerViewportTests`: rewritten to drive the REAL query viewport through UI
     actions — Next-button paging to the final page (truthful total via page label), real search
     input typing for flags/suggestives, bounded realized cells, and real caret-aware insertion.

**Ownership:** only Repeat/query runtime, composer emoji viewport hunks, and their tests changed.
Toolbar/plus/icons, Compiler/Core/Task/RuntimeCompatibility, transport, and root QA fixtures untouched.

**Next executable gates (root):**
1. Run Runtime group: 45 previous passes + corrected `CuiRepeatContainerLayoutTests` (font app +
   wrap baseline) — expect full PASS.
2. Rebuild the compiled ChatComposer artifact from the updated `.cui` (no compiler source change
   needed), then run `ChatComposerEmojiPickerViewportTests` in Host.Tests (CompileInclude already
   present in `HavenOS.Browse.Host.Tests.csproj`).
3. Confirm no regression in `CuiTranscriptRepeatCapabilityTests`, `CuiRepeatedInputFocusTests`,
   `HostComposerLifetimeTests`, and the 30-test compiler/codec suite.

---

## Resume Pass 4 — generic contract repair + capability admission + retirement (final source receipt)

**Date:** 2026-10-20 (this session, later turn)
**Baseline commit for delta:** `b2410afdd80e` (lane-1 freeze). Working tree HEAD `ddb2a8b8`.
**Scope:** source/tests/static checks only. NO .NET/build/app/native/display/model/subagent/provider
changes; no commit/push. Root owns SDK and rebuilt-artifact execution.

### A. Four compile repairs (warnings-as-errors preserved)
- **CS0108** (`CuiQueryViewport.cs`): replaced the public `event PropertyChangedEventHandler? PropertyChanged`
  (which hid the base member) with an explicit `INotifyPropertyChanged.PropertyChanged` interface
  implementation backed by a private `_propertyChanged` field.
- **CS0165** (`CuiControlLoader.cs`): replaced the outer `CuiQueryViewport? viewport = null;` +
  `if (viewport ...)` pattern with a directly scoped `if (control is CuiQueryViewport viewport)` and
  an `activeViewport` local so no unassigned-local path remains.
- **CS0103** (`CuiQueryViewport.cs`): binding keys are now published string constants
  (`QueryKey/ItemsKey/TotalCountKey/HasPreviousKey/HasNextKey/PageStartKey/PageEndKey/IsEmptyKey`)
  instead of `nameof` member references on the explicit interface.
- **CS0266** (`CuiControlLoader.cs` `ScopedDispatcherFor`): the ancestor walk now starts from `Visual? current = control;`
  and narrows each step with `current is CuiQueryViewport viewport`, eliminating the implicit
  `Visual`→`Control` conversion by walking the raw Visual chain.

### B. Generic runtime contract (app authoring removed from framework)
- `CuiQueryViewport.cs` rewrite: no hardcoded composer path, no `composer.emoji-prev/next`, no
  "No emoji found"/"Chat composer" wording. Uses generic commands `query.previous`/`query.next`
  (`PreviousCommand`/`NextCommand` consts); unrelated commands forward to the inner host dispatcher.
  `Source` is a plain CLR `ICuiPagedSource?` typed port (one-way; invalid type rejected). Adds
  `IsEmpty` binding. `Withdraw()` is internal and clears source/context/dispatcher/refresh callback
  without raising notifications, evaluating, or dispatching.
- `ChatComposer.cui` authored mapping: `composer.emoji-prev` → `command="query.previous"`,
  `composer.emoji-next` → `command="query.next"`; QueryViewport container gets
  `Source="{Binding Composer.EmojiCatalogueSource}"`; the pager label row is now numeric
  (`emoji-page-start` `emoji-page-end` `emoji-total` + authored separators), binding `PageStart`/
  `PageEnd`/`TotalCount`. No `PageLabel` remains.

### C. Capability derivation/admission (compiler → artifact/codec → whole-bundle host admission)
- `CuiCompiledArtifact.cs`: new `QueryViewportCapability = "query-viewport"`; `DeriveRequiredCapabilities`
  now walks component/template/closure trees (`ContainsQueryViewport`/`ComponentUsesQueryViewport`,
  case-insensitive `Type` lookup on Container `Type="QueryViewport"` literal) and runs for EVERY
  document regardless of statefulness (CUIR076 on stripped capability before any effect). ABI1/ABI2
  and V1/V2 transport contracts kept.
- `CuiCompiler.cs`: `DocumentUsesQueryViewport`/`ComponentUsesQueryViewport` detect the scope; the
  compiler stamps `query-viewport` for stateless query docs while keeping ABI1, and stamps both
  capabilities + ABI2 for stateful docs.
- `CuiRuntimeCompatibility.cs`: `QueryViewportCapability` const + included in `Capabilities`; the
  optional `capabilities` param on `ValidateArtifactHostSupport` models an older host profile at the
  real admission boundary (`RequireCapabilities` consults the supplied supported set; CUIR067).

### D. Retirement/withdraw semantics
- `CuiControlLoader.cs`: `DetachPresentation` (canonical teardown path covering reload/close/repeat/
  conditional) now calls `retiredViewport.Withdraw()`; the duplicate `Withdraw()` in
  `DetachControlTree` was removed. `RefreshHost` is generation-fenced via `IsLoadAttemptCurrent(mountGeneration)`.

### E. Tests (new/updated; all NOT RUN — require root SDK/artifact rebuild)
- `framework/CUI/Compiler.Tests/CuiQueryViewportCapabilityTests.cs` (new): query-only doc emits
  `query-viewport` + stays ABI1; stateful+query emits both caps + ABI2; template/definition/closure
  route derives the cap; stripped cap rejected at artifact admission (CUIR076); older host profile
  rejected at `ValidateArtifactHostSupport` (CUIR067) while current host admits identical artifact.
- `framework/CUI/Runtime.Tests/CuiQueryViewportRuntimeTests.cs` (new): generic viewport publishes a
  bounded window, numeric labels, empty state, query reset; paging routes generic commands through
  the owning viewport and stops at boundaries without host fallthrough; two independent viewports
  isolate sources/cursors; live source replacement refreshes the window; unrelated non-pager actions
  forward to host with original fault propagation.
- `framework/CUI/Runtime.Tests/CuiQueryViewportRetirementTests.cs` (new): reload and loader-close
  retire the old viewport so a retained old reference cannot evaluate an old/new source, refresh the
  newer frame, or mutate new Items; the current frame stays functional.
- `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` (updated): final-page
  label assertion now targets the numeric `emoji-total` (`EndsWith` of `TotalCount` string) instead
  of the removed `emoji-page-label`.

### F. File hashes (SHA-256, current working tree)
| File | Hash |
| --- | --- |
| `framework/CUI/Runtime/CuiQueryViewport.cs` | `fec4ff22f5282a4e88db70cad812df1bef41aef8ce9f1468a01c9f4c322c9326` |
| `framework/CUI/Runtime/CuiControlLoader.cs` | `8055014ad1dce18c56014b8d6e137e2fe884af07248a90ffe98e57ea3620e797` |
| `framework/CUI/Core/CuiCompiledArtifact.cs` | `8161613b73b4b4e7dea875a423016f74293a89f46aace3ecd687d064f0e8497a` |
| `framework/CUI/Compiler/CuiCompiler.cs` | `9e636f50b5d600fdf1da0c3823dea35c9900be6eda016fdc67ddcf180f8bb84d` |
| `framework/CUI/Runtime/CuiRuntimeCompatibility.cs` | `dd0a64fe31e0a5134720d8881f4d4f3b039bc7a9dcc96ab57e473616f805ab56` |
| `framework/CUI/Compiler.Tests/CuiQueryViewportCapabilityTests.cs` | `d42be6094e882deea1adbd02159eca927805cfc2c5c23683f837089086109217` |
| `framework/CUI/Runtime.Tests/CuiQueryViewportRuntimeTests.cs` | `42fcfa632d7be6dbd0e8c2de0cf5c6d4ec39437dfc26820c85bb33c32538d109` |
| `framework/CUI/Runtime.Tests/CuiQueryViewportRetirementTests.cs` | `8563e54c8577d910c401b8525f9e94a89bfddedeb4ff012157f2ad497896280e` |
| `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` | `8592f68b3d9846eb522788ae3148fce3524f58c25ca2ef0748a46f0a6ecfaf52` |
| `9to1 Workspace/Browse/Host.Tests/ChatComposerEmojiPickerViewportTests.cs` | `0ee8fffb3ddf5e35ecdfb2f65663deb98b7488a61a3e0d2fc7531ac8e9c1321b` |

### G. Unresolved dependencies (root-owned)
- **All execution gates NOT RUN** until root rebuilds the compiled ChatComposer artifact from the
  updated `ChatComposer.cui` (it must now declare `query-viewport`), and grants a .NET test slot.
  `CuiCompiledArtifact.Validate` derived-capability checks run for all documents (CUIR076), so a
  committed pre-rebuild composer artifact lacking `query-viewport` will be rejected until rebuild.
- New SDK test files are auto-included by the SDK projects (no csproj edit needed).
