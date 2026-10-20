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
