# CUI theme ownership

CUI owns the presentation of ordinary first-party controls. The pinned backend supplies layout, input, text shaping, clipboard, focus, accessibility and platform mechanics; it is not a second theme or public markup language. First-party markup uses canonical `Page`, `Container`, `Text`, `Button`, `Input`, `Object` and the other registered CUI primitives and constructs.

## Resolution

The only presentation cascade is, from lowest to highest:

1. Built-in CUI property default.
2. Inherited Theme.
3. Type/Definition default.
4. Explicit instance property.
5. Scoped property region.
6. Active Style/event overlay.

`Runtime/CuiEffectivePropertyStore.cs` retains each declaration's typed value, source, source order and level. Runtime assignments update their stored level; ending an overlay reveals the current underlying value. `Resolve` and `Inspect` return the effective value and its actual source. `CuiPropertyPresentation` projects that winner onto backend properties. Its native priority fence is renderer transport only; no native selector or resource load order decides the CUI winner. Editable input keeps its in-progress/invalid representation under the CUI writeback contract.

`CuiControlTemplates` owns backend templates and required behavior parts for buttons, text/check/number/select/slider inputs, menus, popups, scrollbars, lists/trees/tabs and content hosts. `CuiComponentStyles` registers those templates without installing FluentTheme or SimpleTheme. CUI's state adapter resolves hover, pressed, selected, disabled, validation and focus through the same store. Specialized Object engines retain their own content semantics; their surrounding chrome remains CUI and a theme update must not rewrite document colours or content.

## Themes and accessibility

Professional is the default/fallback. The selectable built-in designs are Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western. Cinematic remains readable for existing saved settings; every persisted enum ID is unchanged. Built-ins share a controlled component vocabulary while supplying their own palette, geometry, spacing, typography/elevation scales and motion defaults. Custom `.cui-theme` definitions inherit a built-in or another registered custom theme and supply validated element/Type property deltas.

```xml
<Theme Name="user_preference">
  <Page>
    <Container Type="Vertical Stack">
      <Text>Shared appearance</Text>
      <Theme Name="Bubble"><Button Text="Scoped preview" /></Theme>
    </Container>
  </Page>
</Theme>
```

Montserrat is the default interface font. The host maps it to the bundled OFL font assets. Validated interface/code font overrides keep CUI's fallback stack. Display scale affects typography, padding and control dimensions; high contrast and reduced motion remain inherited accessibility settings rather than a theme's optional preference. Application chrome and authored document styling are distinct.

`CuiThemeResourceApplier` supplies the same semantic palette for global and nested scopes. Exact legacy brush names required by retained renderers alias these resources; they do not install legacy control templates. The browser retained Write assembly no longer imports the extracted `DefaultTheme.Resources.axaml` dictionary. Its engine source, font bytes and assembly identity are preserved.

Frosted Glass uses cool translucent layers and directional light; Metallic uses neutral steel surfaces and compact reflective gradients; Western uses parchment/leather tones and warm borders. Their four brightness appearances retain app accent identities and the shared explicit accent policy. Decorative brushes are immutable ordinary gradients; high contrast replaces them with opaque canonical surfaces. Frosted Glass does **not** currently perform Gaussian backdrop filtering. Its real blur and actual browser acceptance remain pending; the colour/gradient implementation is not evidence of a backdrop effect. Professional alone has implicit Mica.

Explicit All Apps or Individual Apps accent choices recolour a custom theme's primary fill through `CuiCustomThemeAccentResolver.Resolve`. A solid maps to the chosen colour; the first gradient stop or pattern foreground anchors the colour transformation, and the other colours retain relative hue and tonal order. Stop offsets, geometry, transparency and per-colour blur remain authored; secondary/tertiary fills and fonts are untouched. The result is a rendering copy, never a change to the saved theme library. No explicit override returns the authored fill unchanged.

## Professional material surfaces

Professional alone enables the four typed material resources below. Other built-in themes and high contrast receive their exact existing brush objects, including the distinction between shell, workspace, panel and floating panel. Reduced motion requires no additional switch: these materials are static and have no timer or animation.

| Resource | Professional rendering | Other theme / high-contrast fallback |
| --- | --- | --- |
| `CuiShellMaterial` | Opaque neutral shell with subtle static tone/grain; no blur | `CuiBackgroundBrush` |
| `CuiWorkspaceMaterial` | Opaque inset workspace with subtle static tone/grain; no blur | `CuiWorkspaceBrush` |
| `CuiSurfaceMaterial` | Strongly tinted panel over a radius-8 backdrop filter | `CuiPanelBrush` |
| `CuiFloatingSurfaceMaterial` | Brighter strongly tinted floating panel over a radius-8 backdrop filter | `CuiPanel2Brush` |

The canonical Container renderer and CUI-owned template frames consume immutable `CuiMaterialBrush` descriptors. The descriptor carries a tint, opacity, uniform backdrop radius (0–32), optional static grain and optional tonal base. Its native transparent brush is a transport marker, not a rendered transparent placeholder. Apply it only to supported CUI surface backgrounds; authored document brushes and foreground/icon colours remain independent ordinary brushes.

`CuiMaterialBorder` places one noninteractive decoration behind its existing content, without replacing or reparenting that content. The registered Skia backend clips the live backdrop to the surface and applies a backdrop image filter before drawing the tint and its sharp foreground children. Inherited opacity is applied to the entire material output. It does not snapshot the application, blur the foreground, install Fluent, invoke operating-system Mica, or drive a refresh timer. An unavailable Skia drawing capability raises `CUI_MATERIAL_UNSUPPORTED` rather than silently claiming a material effect. Shell/workspace bases deliberately have no filter, so nested base surfaces do not cause redundant full-stage blur; apply only one base material to each editing stage.

The native pixel regression checks actual background blur, sharp foreground and unaffected underlying content, live repaint, resize, inherited opacity, and switching back to an ordinary opaque brush. Resource tests cover every current built-in, all four appearances, high contrast and reduced motion. This bounded surface implementation is not acceptance of the wider free-form CUI Background Blur/Refract language or a spatially varying custom-fill blur. Browser rendering, document input, every targeted surface and performance still require independent verification on the combined published artifact.

## Home preference ownership

`Home/Source/Home/Core/HomeThemePreferenceService.cs` owns typed get/set/invalidation semantics without a renderer dependency. Its provider interfaces adapt existing preference owners; they do not create a second store. Records have a schema, exact device/account scope, revision, theme name and accessibility settings. The actual backend must enforce current authority and atomic compare/exchange. Read permission denial never falls back to a cached account. Missing/incompatible custom themes produce an explicit Professional fallback without discarding the original record.

`CuiThemePreferenceContext` borrows a validated `ICuiThemePreferenceSource`; it contains no independent persistence. Loader Theme scopes inherit its observation, including explicit nested overrides and their return to parent inheritance. Running controls retain identity, focus, selection and canonical state during resource updates.

The browser adapter shares the existing `9to1.home.local-appearance.v1` envelope with dashboard preferences. Both dashboard writes and theme compare/exchange acquire the same Web Locks gate; stale dashboard snapshots preserve the freshest theme and unknown envelope fields. Storage events invalidate other tabs. A missing lock/storage capability gives a nonpersistent Professional observation and disabled saving, not a fictional durable owner. The browser currently supports device-local scope only; a genuine signed-in settings sync provider is still required.

That same revisioned preference can optionally carry `appearance` (`SuperBright`, `Bright`, `Dark`, or `SuperDark`). An absent value keeps the host's existing default and does not migrate or replace old data. Accounts changes brightness through the Home owner, retaining the theme and accessibility settings. CUI borrows the typed choice for every scoped palette; an explicit host appearance still takes precedence. The browser's existing startup palette cache is derived from those canonical application resources, so a saved bright choice also colours the next startup and resource-failure surface. It is a presentation cache, not another preference owner.

Pending operations are retained through shutdown. Post-commit cache or observer errors cannot replace an acknowledged save receipt with a false write failure.

## Validation boundary

Focused tests cover the typed cascade, source ordering, palette aliases, accessibility metrics, cached/custom fallback, scope isolation, input focus/selection through theme changes and absence of installed Fluent/Simple in the tested host. Core tests cover revision conflicts, permission denial, cache fallback, observer/cache failures and shutdown waiting for a held commit. JavaScript owner tests use a Web Locks contract harness and are not actual multi-tab browser proof.

Full conformance remains subject to current packaged native/web workflows, every required control/state and viewport, real Home preference providers, independent app propagation/restart, actual multi-tab storage, specialized content preservation, accessibility/performance and independent acceptance. A successful focused test or static template inventory is not that release verdict.
