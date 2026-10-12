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

Professional is the default/fallback. The required built-in designs are Professional, Glow, Playful, Bubble and Cinematic. The existing Retro enum/API is retained for compatibility with existing saved settings. Built-ins share a controlled component vocabulary while supplying their own palette, geometry, spacing, typography/elevation scales and motion defaults. Custom `.cui-theme` definitions inherit a built-in or another registered custom theme and supply validated element/Type property deltas.

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

## Home preference ownership

`Home/Source/Home/Core/HomeThemePreferenceService.cs` owns typed get/set/invalidation semantics without a renderer dependency. Its provider interfaces adapt existing preference owners; they do not create a second store. Records have a schema, exact device/account scope, revision, theme name and accessibility settings. The actual backend must enforce current authority and atomic compare/exchange. Read permission denial never falls back to a cached account. Missing/incompatible custom themes produce an explicit Professional fallback without discarding the original record.

`CuiThemePreferenceContext` borrows a validated `ICuiThemePreferenceSource`; it contains no independent persistence. Loader Theme scopes inherit its observation, including explicit nested overrides and their return to parent inheritance. Running controls retain identity, focus, selection and canonical state during resource updates.

The browser adapter shares the existing `9to1.home.local-appearance.v1` envelope with dashboard preferences. Both dashboard writes and theme compare/exchange acquire the same Web Locks gate; stale dashboard snapshots preserve the freshest theme and unknown envelope fields. Storage events invalidate other tabs. A missing lock/storage capability gives a nonpersistent Professional observation and disabled saving, not a fictional durable owner. The browser currently supports device-local scope only; a genuine signed-in settings sync provider is still required.

That same revisioned preference can optionally carry `appearance` (`SuperBright`, `Bright`, `Dark`, or `SuperDark`). An absent value keeps the host's existing default and does not migrate or replace old data. Accounts changes brightness through the Home owner, retaining the theme and accessibility settings. CUI borrows the typed choice for every scoped palette; an explicit host appearance still takes precedence. The browser's existing startup palette cache is derived from those canonical application resources, so a saved bright choice also colours the next startup and resource-failure surface. It is a presentation cache, not another preference owner.

Pending operations are retained through shutdown. Post-commit cache or observer errors cannot replace an acknowledged save receipt with a false write failure.

## Validation boundary

Focused tests cover the typed cascade, source ordering, palette aliases, accessibility metrics, cached/custom fallback, scope isolation, input focus/selection through theme changes and absence of installed Fluent/Simple in the tested host. Core tests cover revision conflicts, permission denial, cache fallback, observer/cache failures and shutdown waiting for a held commit. JavaScript owner tests use a Web Locks contract harness and are not actual multi-tab browser proof.

Full conformance remains subject to current packaged native/web workflows, every required control/state and viewport, real Home preference providers, independent app propagation/restart, actual multi-tab storage, specialized content preservation, accessibility/performance and independent acceptance. A successful focused test or static template inventory is not that release verdict.
