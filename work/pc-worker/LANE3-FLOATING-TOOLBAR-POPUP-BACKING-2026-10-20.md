# Lane 3 handoff — floating formatting toolbar + CUI popup backing (pass 2) — 2026-10-20

Source identity: `/workspace/9to1-mercury-toolbar` (worktree), git HEAD unchanged (`fb70840d` WIP
checkpoint for six isolated Mercury lanes). No commit/push/deploy/credentials. No .NET build/test/app
launch performed: **execution is NOT RUN** until the coordinator grants a serialized .NET slot (and a
fresh compiled CUI bundle for the composer). Static checks (markup inspection, registry/property
cross-reference, template API usage, headless-click semantics) done by review only.

First-pass frozen source `d0622f24226697d3e41b20121e3f37215571e46d` preserved (untouched); this pass
replaces the first-pass working-tree changes for the toolbar and popup backing.

## Assigned scope (lane boundaries respected)
- Own: `ChatComposer.cui` outer placement/composition + `format-toolbar` segment; generic
  layout/container/placement helper; popup template/backend support in `CuiControlTemplates.cs` +
  `CuiPopupPresentation` + new `CuiTransparencyFallback`.
- Minimal registration patch (documented, integration-pending): added two container layout types
  (`overlay float`, `floating`) in `CuiContainerControl.cs` and `CuiControlRegistry.cs`. No behavior
  changes to existing layout types.
- NOT edited: `CuiPropertyPresentation.cs`, composer plus/emoji/state (lane 1); compiler/parser/
  compiled transport (lane 2); `ChatComposerCuiDocument.cs`.

## Part A — floating `format-toolbar` (genuine out-of-flow live placement)
Changes:
- `ChatComposer.cui`: `composer-surface` is now `Type="Overlay Float"` (was `Overlay`). Normal-flow
  children are grouped unchanged under `composer-body` (vertical stack). The `format-toolbar` moved
  into a new `composer-floating-layer` (`Type="Floating"`), a sibling of the body, still hidden via
  `IsVisible="{Binding FormattingOpen}"` and removed from the visual tree when closed.
- New `framework/CUI/Runtime/CuiOverlayPanel.cs`: the `Overlay Float` layout. Regular cells are
  measured/arranged exactly as a plain Panel (fill). Floating cells (a cell whose direct child is a
  `CuiFloatingLayer`) are **excluded from this panel's desired size** (out-of-flow) and are arranged
  at their content size adjacent to an anchor control (the first editable `TextBox` in the
  normal-flow body = `composer-input`), flipping above/below when room is insufficient and clamping
  to the panel/viewport edges. Only targeted per-anchor/per-bounds observers are used; cleared on
  detach. There is no permanent global LayoutUpdated subscription and no static screenshot offset.
- New `framework/CUI/Runtime/CuiFloatingLayer.cs`: simple content host (measured to its child,
  arranges child to fill). Anchor math lives in the overlay panel.
- Because the floating cell is content-sized and placed beside/above the editable input, it does not
  cover the composer surface; the toolbar is hit-testable only over its own bounds, and the input and
  controls remain reachable. This replaces the first-pass full-surface overlay that painted over the
  body at the same content origin.

Notes / honest caveats:
- Anchor is the first editable input. With a visible chips row (Source.Chips non-empty), the bar is
  placed above the input, which may overlap the chips row; the primary regression tests exercise the
  no-chips path. A configured anchor/margin seam is the natural follow-up if the coordinator wants
  chip-aware placement.
- `Type="Overlay Float"` and `Type="Floating"` are new layout registrations; they are additive and do
  not alter existing `grid/vertical stack/horizontal stack/wrap/absolute/overlay` behavior.

## Part B — CUI popup backing (white rectangle fix, alpha-aware projection)
Changes:
- Distinct guaranteed-opaque semantic fallback brush `CuiPopupOpaqueFallbackBrush` added in all three
  theme appliers (`CuiThemeResourceApplier`, `CuiPresetSurfaceResources`, `CuiCustomThemeResourceApplier`).
  It is always a plain opaque solid (A=255) — never the material surface whose
  `ISolidColorBrush.Color` is Transparent — so Professional non-HC and gradient presets resolve a
  truthful opaque fallback.
- `CuiControlTemplates.TopLevelContent` PopupRoot branch: `TopLevel.TransparencyBackgroundFallback`
  binds to the opaque fallback brush; the `PART_TransparencyFallback` border is now driven by the new
  alpha-aware, lifetime-owned projection instead of an unconditional `GetResourceObservable` LocalValue
  binding (which repainted the whole backing after a theme change even when `ActualTransparencyLevel`
  was Transparent). The visible popup chrome stays a `CuiMaterialBorder` bound to `CuiPopupSurfaceBrush`
  (material kept separate). Normal full-window roots unchanged.
- New `framework/CUI/Runtime/CuiTransparencyFallback.cs`: observes the PopupRoot's
  `ActualTransparencyLevel` and the live opaque resource; paints the fallback border only on a
  positively opaque destination (`WindowTransparencyLevel.None`), keeps it null for Transparent /
  Blur / AcrylicBlur / Mica, and never infers opacity from a default or parent-window alpha. Per-
  owner subscriptions, disposed on detach/close (ConditionalWeakTable keyed by PopupRoot).
- `CuiPopupPresentation.ResolveOpaquePaint(level, brush)` exposes the pure projection for tests. The
  host-matrix (`PreferOverlayHost` / `PreferOverlayHostUnknown`) is implemented and tested but still
  has zero real callers: native hosting remains the shipping default. Overlay host activation is
  BLOCKED on a reviewed pre-show capability/airspace signal (platform transparency negotiation +
  conservative native child-window query); I did not invent one. This is a documented dependency, not
  a claim that the matrix changed the shipping host.

## Tests authored (NOT RUN — require slot grant + recompiled composer bundle)
- `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs` (now explicitly compiled in
  `HavenOS.Browse.Host.Tests.csproj`): real `ChatComposerCuiView`.
  - Compares **translated window-space bounds** (not local box/send Bounds).
  - Asserts the surface is `Overlay Float`, body vertical stack, input in body, toolbar null when
    closed, visible when open, lives in the Floating layer (not the body).
  - Asserts out-of-flow measure: surface desired size unchanged by toggling the toolbar.
  - Asserts no overlap with the editable input and the send control, and toolbar stays within the
    window viewport.
  - Performs **real headless pointer clicks** (`MouseDown`/`MouseUp`) for Aa toggle, B/I dispatch,
    and a click on the first editable line near the left edge verifying caret placement ~0, zero
    selection, input focus, and no toolbar overlap.
  - **Real narrow resize** (420 → 300) plus multiline growth, with in-viewport/no-overlap and
    desired-size assertions.
- `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs`: host-matrix theory, unknown guard, and the
  alpha-aware paint projection across `WindowTransparencyLevel` values, plus a real PopupRoot template
  assertion (transparent root frame; distinct opaque fallback A=255).

## Existing-regression considerations
- `HostComposerLifetimeTests.Shared_composer_aa_button_toggles_floating_toolbar_...` must keep
  passing after recompile: toolbar null when closed (cell removal), IsVisible true when open, B/I at
  indices 0/1, same input identity. The new markup preserves all of these.
- No changes to absolute-layout / grid / stack runtime behavior; `CuiAbsoluteLayoutTests` and other
  Runtime.Tests suites are unaffected by the additive type registrations.

## Dependencies / open notes for coordinator
- Part B overlay-host adoption remains BLOCKED on a reviewed pre-show capability/airspace signal;
  native hosting is the safe default and already fixes the white backing via the opaque fallback +
  alpha-aware projection.
- Chips-row overlap caveat (Part A) is noted above; a configured anchor/margin seam is the suggested
  follow-up.
- Lane-1/lane-2: no new typed property or compiler changes were required; the registration patch is
  limited to the two container layout types. `docs/specification/open-decisions.json` was not
  modified; if overlay activation or chip-aware placement need recording as open decisions, the
  coordinator should add them.
- Files (pass 2):
  - `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` sha256 `547ae697a8b218a2ea489630855c0c03f51e5b2960ec3ac501a42e4b389e3279`
  - `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs` sha256 `c0806aab7452bc1237976d2ad0aa8de5f96e5abc95560b07716a60fb4c87d216`
  - `framework/CUI/Runtime/CuiFloatingLayer.cs` sha256 `b3fe77dc6a55c25d08992bab76d82766b57513d7967d87cbea13ce6b87ca6e96`
  - `framework/CUI/Runtime/CuiOverlayPanel.cs` sha256 `098b86efecfae6e64b61b8760684281977778d7294206c6f529e071df805687f`
  - `framework/CUI/Runtime/CuiTransparencyFallback.cs` sha256 `318d5a1e803f85a5dbdc95a8aae6073524ebace5eaf9e94bc56340ef2bc18d87`
  - `framework/CUI/Runtime/CuiPopupPresentation.cs` sha256 `fdf1a324be923ba69975c7f27fe98e9ddbcace7805c4a9bb6b400364216a9844`
  - `framework/CUI/Runtime/CuiControlTemplates.cs` sha256 `79702227c13e4957af278fb8114a4b50457e4f3485e1470f88898ba0f673e99b`
  - `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs` sha256 `9d7084167e3fac4bc62756313e176fba3daf86ecab3f9bf9ad0c1220b9d61b80`

## Exact unrun commands (require slot grant + recompiled composer bundle)
- `dotnet build framework/CUI/Runtime/CakeOS.Cui.Runtime.csproj -m:1` (guarded dotnet entrypoint)
- `dotnet test framework/CUI/Runtime.Tests/CakeOS.Cui.Runtime.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiPopupBackingTests"`
- `dotnet build "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj" -m:1`
- `dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" -m:1 --filter "FullyQualifiedName~FloatingFormatToolbarTests"`
- Also re-run the full `HavenOS.Browse.Host.Tests` + `CakeOS.Cui.Runtime.Tests` suites for regression.

---

# Pass 3 — repair of the six pass-2 review blockers (source/test authoring only)

Source identity (dirty, uncommitted; working HEAD remains `fb70840d` + carried source). All
execution still **NOT RUN** — no .NET build/test/app until the coordinator grants a serialized slot
plus a recompiled CUI composer bundle. Only source + static checks this pass.

## Repaired findings (L3P2-001..006)

- **001 — TypesFor omitted Overlay Float/Floating.** `CuiControlRegistry.TypesFor("container")` now
  includes `"Overlay Float"` and `"Floating"`; `UnknownContainerType` message updated. The compiler
  (`CuiCompiler.ValidateProperties`) and runtime `TryCreate` both consume TypesFor, so the authored
  composer now passes the canonical compiler→artifact→runtime path instead of CUIC017 rejection.
- **002 — static compilation defects.** `CuiOverlayPanel` uses the public imported method
  `IsAttachedToVisualTree()` (vendor `VisualExtensions.cs:477`), not the internal `Visual` property.
  `CuiPopupBackingTests` uses `WindowTransparencyLevel` (record struct) everywhere; the bogus
  `TransparencyLevel` alias is gone.
- **003 — placement inside protected composer content.** `CuiOverlayPanel` placement rule is now
  authored: resolves the **whole protected `composer-body`** as the anchor (via the authored
  `Target="composer-body"` `Id`), places within the **real owner top-level viewport**
  (`TopLevel.GetTopLevel`), and flips/fits so it never covers chips, the editable input, or the
  controls row. The panel's own bounds are only a degraded fallback, never claimed as the scene.
- **004 — anchor ownership/live tracking unscoped.** Target resolution is scoped to this owner
  subtree (`ValidTarget`: attached + effectively visible + same-owner descendant). Failed resolution
  returns an **empty (invisible) rect — never (0,0)**. Observer tracking is lifetime-owned and
  scoped to the panel, its coordinate ancestors up to the top-level, each target, and the target's
  intermediate coordinate ancestors (so ancestor movement re-translates correctly); disposed on
  detach/close.
- **005 — producer alpha/hosting missing.** `CuiPopupPresentation.RequestTransparency(popup)`
  (open-boundary alpha request) is wired into the Select (ComboBox) and MenuItem popup factories in
  `CuiControlTemplates.cs`. `CuiTransparencyFallback` now models UNKNOWN/unobserved (`null
  _observedLevel`) and paints only on positively observed `None`; `ResolveOpaquePaint` and the host
  matrix keep native hosting for every Unknown/present state. `ApplyHostDecision` /
  `RegisterAirspaceInspector` / `QueryAirspace` provide the typed seam.
- **006 — popup projection lacks causal live-root tests.** `CuiPopupBackingTests` reworked to drive a
  real `PopupRoot` (`ObserveDestinationAlpha` receipts: None paints the latest opaque semantics,
  Transparent/Blur/AcrylicBlur/Mica keep the fallback border null; close/reopen resets to unknown).
  Two new `FloatingFormatToolbarTests` added: **chips no-overlap** and **all 6 commands reachable at
  300px width** (authored Wrap).

## Authored placement contract
- `composer-floating-layer` (`Type="Floating"`, sibling of `composer-body`) carries
  `Target="composer-body" Placement="Above"`. `Target` names the whole protected composer body; the
  side is composed `Placement` (`Above/Under/LeftOf/RightOf`). Never inferred from a first-input
  heuristic.

## Producer callers (real production wiring)
- `CuiControlTemplates.Select` (ComboBox popup) and `CuiControlTemplates.MenuItem` (submenu popup)
  each call `CuiPopupPresentation.RequestTransparency(popup)` after constructing their Popup.

## Integration dependencies (documented, not claims of completion)
- Loader maps authored `Target`/`Placement` onto `CuiContainerControl.AuthoredTargetId/AuthoredPlacement`
  in `CuiControlLoader.ApplyLiteralProperty` (typed, validated, never heuristic).
- `CuiContainerControl` gained the two authored-relationship properties; constructor already mapped
  `overlay float`→`CuiOverlayPanel`, `floating`→`CuiFloatingLayer`.
- Overlay-ne activation stays BLOCKED on a reviewed pre-show capability/airspace signal; native host is
  the safe default and already removes the white backing via the opaque fallback + alpha-aware projection.
- Lane-1 plus/emoji/state and lane-2 compiler/transport ownership untouched.

## Current file identity (SHA-256, pass 3)
- `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` `9679d2123eb5c81a054378d5dff9fece0e54daaab4864907ed12439c41c43f2b`
- `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs` `1e6274585f9680242eb834d46b649918a799a14da213f9a5adb65472e8d8b92c`
- `framework/CUI/Runtime/CuiFloatingLayer.cs` `b3fe77dc6a55c25d08992bab76d82766b57513d7967d87cbea13ce6b87ca6e96`
- `framework/CUI/Runtime/CuiOverlayPanel.cs` `e51440187d21e801aeaa0f4c836e8e01605de1b6d7c73690bfd8b64283c9252c`
- `framework/CUI/Runtime/CuiTransparencyFallback.cs` `847680b1e92494625a55543e5dbeffd44599acf2f2b70d3425fc51e0d04186f2`
- `framework/CUI/Runtime/CuiPopupPresentation.cs` `8cad7e3415d54d4843c68312509807df7aca640220427af8f5cee57d04bf0e13`
- `framework/CUI/Runtime/CuiControlTemplates.cs` `65e8fea9dbb18669106d47164a03544d3219ce3d8df82a4463f7ec75679eaaca`
- `framework/CUI/Runtime/CuiControlRegistry.cs` `d4b67c863512c8c4417bc43349dfa83055adb10ea4e202b63e8e0459cea4efca`
- `framework/CUI/Runtime/CuiContainerControl.cs` `3cd890c1970b329b88cf00e91da46e1fc146aeceb82bd4ccd743d4ddc2770db4`
- `framework/CUI/Runtime/CuiControlLoader.cs` `f505cad6dbb4b532fd5e4e7d64f06984041d0941c211134c5f1d649940aac4aa`
- `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs` `a895af434abdcbfa4451d3fd58e4c4c2628b3150787a3838e9b9d9b229ec7786`

## Exact unrun commands (unchanged; require slot grant + recompiled composer bundle)
- `dotnet build framework/CUI/Runtime/CakeOS.Cui.Runtime.csproj -m:1` (guarded dotnet entrypoint)
- `dotnet test framework/CUI/Runtime.Tests/CakeOS.Cui.Runtime.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiPopupBackingTests"`
- `dotnet build "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj" -m:1`
- `dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" -m:1 --filter "FullyQualifiedName~FloatingFormatToolbarTests"`
- Also re-run the full `HavenOS.Browse.Host.Tests` + `CakeOS.Cui.Runtime.Tests` suites for regression.

---

# Pass 4 — repair pass the three pass-3 reviewed failures (source/test authoring only)

Source identity (dirty, uncommitted; working HEAD remains `fb70840d` + carried source). All execution
still **NOT RUN** — no .NET build/test/app until the coordinator grants a serialized slot plus a
recompiled CUI composer bundle. Only source + static review this pass. No subagents, no commits, no
network, no images, no .NET/app/native actions.

## Repaired findings (L3P4-001..003)

- **001 — genuine per-open alpha request + real destination receipt.** `Popup.BeforeShow` (new
  vendor-fork event on `Popup`, fired inside `Open()` just before the fresh destination's first `Show`)
  is the pre-show seam. `CuiPopupPresentation.RequestTransparency(popup)` subscribes once
  (idempotent via `ConditionalWeakTable`), and the handler applies `TransparencyLevelHint = [Transparent]`
  to each fresh destination per open + reopen. Receipt is real: `CuiTransparencyFallback` subscribes
  to the new `TopLevel.TransparencyLevelReported` hook (raised inside
  `HandleTransparencyLevelChanged` for every processed report, including same-valued None→None and
  OnApplyTemplate re-reports), plus explicit `ObserveDestinationAlpha`. `Popup`'s pre-show hook has
  cleanup-on-throw (host disposed, open state cleared, retryable) so a subscriber exception never
  leaves a half-open popup. Producers wired: Select/ComboBox + MenuItem popups
  (`CuiControlTemplates`), authored context menus (`CuiControlRegistry.CreateContextMenu`), TextInput
  context menu, and ToolTip (`ConfigurePopup` instance + `DefaultConfigurePopup` static, applied
  per-open).
- **002 — collision-safe floating placement + bounded remeasurement.** `CuiOverlayPanel` placement:
  authored `Target` id resolves to the whole protected `composer-body` (Ordinal, descendant-scoped,
  attached+effectively-visible); `Placement` is case-insensitive parsed and fail-closed (unsupported →
  null → Unplaced). Content is remeasured at the clamped usable viewport width BEFORE sizing (wrap
  height reflects actual width). Candidates try authored side → opposite → perpendiculars
  (`OrderedSides`), acceptable only when `Fits` = full viewport containment AND no intersection with
  the protected composer body. No safe side → **degraded to hidden** (empty `Unplaced()` rect), never
  (0,0) and never covering editing/actions. Observers lifetime-scoped (panel + coordinate ancestors to
  top-level + each target + target's intermediate coordinate ancestors), disposed on detach/close.
- **003 — complete small Target/Placement contract.** `CuiContainerControl.AuthoredTargetId` /
  `AuthoredPlacement` are read-only properties; `SetAuthoredTarget` / `SetAuthoredPlacement` are
  validated typed setters raising `RelationshipChanged` on change. Loader routes authored
  Target/Placement through these typed methods (`CuiControlLoader`). New `CuiContainerRelationshipTests`
  covers placement casing normalization, invalid/empty value fail-closed, and target non-empty/white-
  space rejection.

## Remaining pre-show capability/airspace dependency (BLOCKED, not implemented)
- Overlay-host adoption stays BLOCKED on a reviewed pre-show capability/airspace signal: platform
  transparency negotiation + conservative native child-window airspace query. The typed seam exists
  (`PreferOverlayHost` / `PreferOverlayHostUnknown` / `ApplyHostDecision` /
  `RegisterAirspaceInspector` / `QueryAirspace`), and **zero real callers** use it; native hosting is
  the shipping default and already fixes the white backing via the opaque fallback + alpha-aware
  projection. Exact insertion point: before `CreatePopupHost` in `Popup.Open` — not added. Not a
  claim that the host matrix changed the shipping host.

## Producer call map (real production wiring)
- `CuiControlTemplates` static ctor: `ToolTip.DefaultConfigurePopup = CuiPopupPresentation.RequestTransparency`.
- `CuiControlTemplates`: Select popup + MenuItem submenu popup call `RequestTransparency(popup)`.
- `CuiControlTemplates` TextInput context menu: `menu.ConfigurePopup = RequestTransparency`.
- `CuiControlRegistry.CreateContextMenu()`: `menu.ConfigurePopup = RequestTransparency`.
- `ToolTip` per-open apply: `(ConfigurePopup ?? DefaultConfigurePopup)?.Invoke(_popup)`.

## Static review status
- Vendor-fork event signatures match their invocations and subscribers (`EventHandler<PopupBeforeShowEventArgs>`,
  `Action<WindowTransparencyLevel>`, `Action<Popup>`). `ResolveOpaquePaint`, `ObserveDestinationAlpha`,
  `RequestTransparency`, `SetAuthoredTarget/Placement` present with matching call sites. Runtime.Tests
  csproj is SDK-style default-glob (new test files compile automatically). Host.Tests csproj explicitly
  compiles `FloatingFormatToolbarTests.cs`. TreatWarningsAsErrors is on; **no build has run** so
  compiler-level verification remains pending the slot.

## Current file identity (SHA-256, pass 4)
- `9to1 Workspace/Spaces/UI/Chat/ChatComposer.cui` `9679d2123eb5c81a054378d5dff9fece0e54daaab4864907ed12439c41c43f2b` (unchanged from pass 3)
- `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs` `c96be34429bd34e207ddb6d4ecf85f05b4ee5237562248c2973ecddae934afd4`
- `framework/CUI/Runtime/CuiFloatingLayer.cs` `b3fe77dc6a55c25d08992bab76d82766b57513d7967d87cbea13ce6b87ca6e96`
- `framework/CUI/Runtime/CuiOverlayPanel.cs` `938a3a50a034e5546a6abb66f6306ca343b1e141d37c61fd6a710343656779f1`
- `framework/CUI/Runtime/CuiTransparencyFallback.cs` `9b49a3b29f628a08d8c776e8ff20ef2e927c7f40e77ebf583b73a2950847c237`
- `framework/CUI/Runtime/CuiPopupPresentation.cs` `ef89f7bfad18418125f43f20dc6cf4ad8ed8d514e587659ec6148e103b960f52`
- `framework/CUI/Runtime/CuiControlTemplates.cs` `073fdc03b7ee2ef9543c0ee57cbddab6846c75f9ec46d6eb9b8bb371e828adfb`
- `framework/CUI/Runtime/CuiControlRegistry.cs` `92f5cc328f94395686903cd09f56b5abac36d133d9cc5d9b0128ec27706046be`
- `framework/CUI/Runtime/CuiContainerControl.cs` `58906f09af19ffa3edd072660ba5357910fbcd51258fc3e6b47f8a7ed3c2d9e1`
- `framework/CUI/Runtime/CuiControlLoader.cs` `29045b2e2bc28a1339ee1c83fde6ab17539d8406d4ee806bf4fdf5a43524d941`
- `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs` `71ef6cf3f84704b7498c77ae6e084c2d399be365a99e7f4eb9eba21be556a60c`
- `framework/CUI/Runtime.Tests/CuiContainerRelationshipTests.cs` `fe080c81bfcf5ffe64aa33752187a8df99b720c00506531968fccb6780a27a5b` (new)
- Vendor fork (new this pass):
  - `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/Primitives/Popup.cs` `eaa579811e066c3446f48b6f64d58eac81e04d8b12dbb5fca3647e12d14b2ebe`
  - `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/TopLevel.cs` `5045b3ec830365b2adb37efa210a4fdb28cd3aa90f9b51c1e79567e33afb27b5`
  - `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/ContextMenu.cs` `8b9abee31ff9c09fa1c49ca56a8dc08275c8cef69c48250f5aa4eb579ecabef7`
  - `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/ToolTip.cs` `c1d5d7bd14dc09537cfaf62f79b774a05ddadd1d115d6ca43ea87cbb4ede19b6`

---

# Pass 5 — close pass-4 acceptance gaps (source/test authoring only)

Source identity (dirty, uncommitted; working HEAD remains `fb70840d` + carried source). All
execution still **NOT RUN** — no .NET build/test/app until the coordinator grants a serialized slot
plus a recompiled CUI composer bundle. Only source + static review this pass. No subagents, no
commits, no network, no images, no .NET/app/native actions.

Starting identity carried from the frozen pass-4 checkpoint:
`8569ce7efec9f6f10722487af27bbacac85ec5b9` (parent `c25eb54a70b0c8706bb3ae2c94f36e0af251fc1b`),
receipt `/workspace/shared/9to1-test-evidence/parallel6-toolbar-pass4-frozen-20261020T184438Z/identity.json`.
Base commit `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb`. Owner's 16-worker authorization (latest,
overriding six-worker text) is respected; this lane remains ONE worker with the $5 cap unchanged.

## Repaired findings (L3P5-001..005)

- **001 — unplaced is real, not an arrange-only claim.** `CuiOverlayPanel` no longer "hides" a
  floating cell merely by `cell.Arrange(Rect(0,0,0,0))` (which still let MinWidth/MinHeight toolbar
  descendants paint/hit/focus near the origin). New `ApplyFloatingPlacement` drives a genuine
  unplaced state: when no safe rect exists the panel-private cell wrapper is removed from
  render/input/focus/tab path (`IsVisible=false`) while the authored FormattingOpen/IsVisible binding
  is preserved; safe space returning re-arranges and restores placement (automatic recovery). Hidden
  unplaced cells remain in Children so relationship/placement observers keep living.
- **002 — no process-wide ToolTip default registration.** Removed the `CuiControlTemplates` static
  ctor assignment `ToolTip.DefaultConfigurePopup = RequestTransparency` and the vendor static
  `DefaultConfigurePopup`. CUI-authored tooltips now route through the narrow per-control attached
  `ToolTip.ConfigurePopupProperty` path: the loader sets it on authored controls (`ToolTip.SetTip`
  case), and the vendor ToolTip service copies it onto the instance BEFORE the retained popup's first
  Show. Unrelated non-CUI tooltip producers are never configured.
- **003 — guarded `Popup.BeforeShow`.** `Popup.Open` captures the exact `_openState`/destination,
  guards after the synchronous callback (a handler that `Close()`/retires/reopens must never lead to
  `Show()` on the disposed destination), and on subscriber fault retires the captured state exactly
  once through its own cleanup custody — never double-disposing a destination a handler already
  closed — preserving the original error and leaving the popup retryable. Original popup
  dismissal/focus/open semantics are unchanged for the normal (no-handler) path.
- **004 — causal tests, not source claims.** `CuiPopupBackingTests` now drive the real
  `ITopLevelImpl.TransparencyLevelChanged` delegate (not reflection on a private handler), assert the
  LATEST exact opaque color after re-emission (not just A=255), keep first+second open coverage, and
  add real crafted ContextMenu and ToolTip path tests (including "ConfigurationPopup established before
  first show" on the tooltip instance and no-leak on a plain control). New
  `CuiPopupBeforeShowGuardTests` cover deterministic close/reentry/throw/cleanup-fault. Relationship
  tests add a real loader-authored Target/Placement integration + live retarget (still-attached target,
  casing, idempotent re-set).
- **005 — prune retired relationship observers.** `CuiOverlayPanel.EnsureRelationshipObservers` now
  removes handlers/host→cell mappings/stale cached targets for hosts no longer in Children, not only
  on whole-panel detach. Scoped observations preserved.

## Producer call map (real production wiring, pass 5)
- Select (ComboBox) popup: `CuiControlTemplates.Select` → `CuiPopupPresentation.RequestTransparency(popup)`.
- Submenu/MenuItem popup: `CuiControlTemplates.MenuItem` → `RequestTransparency(popup)`.
- Authored ContextMenu: `CuiControlRegistry.CreateContextMenu()` → `menu.ConfigurePopup = RequestTransparency`.
- TextInput context menu: `CuiControlTemplates.TextInput` → `menu.ConfigurePopup = RequestTransparency`.
- CUI tooltip: `CuiControlLoader` "tooltip" case → `ToolTip.SetConfigurePopup(control, RequestTransparency)`;
  vendor `ToolTip.IsOpenChanged` copies it to the instance before the popup's first `Show`; `Open()` invokes
  `ConfigurePopup` before `IsOpen = true`.

## Pre-show capability/airspace dependency (BLOCKED, not implemented)
- Overlay-host adoption stays BLOCKED on a reviewed pre-show capability/airspace signal; native hosting
  remains the shipping default and removes the white backing via the opaque fallback + alpha-aware
  projection. `PreferOverlayHost`/`PreferOverlayHostUnknown`/`ApplyHostDecision`/
  `RegisterAirspaceInspector`/`QueryAirspace` remain the typed seam with zero real callers. Not a claim
  that the host matrix changed the shipping host.

## Current file identity (SHA-256, pass 5)
- `framework/CUI/Runtime/CuiOverlayPanel.cs` `6f3d5e394e2f375fd1ebabc511bcc17bcd82e23152e1e1dcc5bb5272e7c6e4fc`
- `framework/CUI/Runtime/CuiControlTemplates.cs` `c18847ff03d75920db953540b2d62186d381cd0e481d87c40ef5d30cdca31e97`
- `framework/CUI/Runtime/CuiControlLoader.cs` `94947e35554c6e697fb9616432f03ed9212d3452b9a37f71f7fb4ab879b9711f`
- `framework/CUI/Runtime.Tests/CuiPopupBackingTests.cs` `aa40d6cfbc3d55356f1820e99b54fa4056fbe2b009a40cef150cb14ec85b0b28`
- `framework/CUI/Runtime.Tests/CuiContainerRelationshipTests.cs` `166a9264b09ffead8c3e6a373c078a9d1e763fd8a029464f16df74968d298583`
- `framework/CUI/Runtime.Tests/CuiPopupBeforeShowGuardTests.cs` `5d796eed67ce9ea70675f878c589461a407c0b1abdaace99c21c5cc3a545c166` (new)
- `9to1 Workspace/Browse/Host.Tests/FloatingFormatToolbarTests.cs` `949a98276828b01fcb3d6fed8dc4f8c20c66db65a92d722b3ff9a9d9a7a4d241`
- `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/Primitives/Popup.cs` `5f30e8c037f5a236890dde7ecf8cd0c180e30746ce3c824d399d943284182f85`
- `framework/CUI/vendor/Avalonia/src/Avalonia.Controls/ToolTip.cs` `6ee3ec5aa91f9b94a0d09b8e99fc4da6f423088ca4fa388efc65e1da76666860`

## Exact unrun commands (unchanged; require slot grant + recompiled composer bundle)
- `dotnet build framework/CUI/Runtime/CakeOS.Cui.Runtime.csproj -m:1` (guarded dotnet entrypoint)
- `dotnet test framework/CUI/Runtime.Tests/CakeOS.Cui.Runtime.Tests.csproj -m:1 --filter "FullyQualifiedName~CuiPopupBackingTests|FullyQualifiedName~CuiPopupBeforeShowGuardTests|FullyQualifiedName~CuiContainerRelationshipTests"`
- `dotnet build "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj" -m:1`
- `dotnet test "9to1 Workspace/Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" -m:1 --filter "FullyQualifiedName~FloatingFormatToolbarTests"`
- Also re-run the full `HavenOS.Browse.Host.Tests` + `CakeOS.Cui.Runtime.Tests` suites for regression.
