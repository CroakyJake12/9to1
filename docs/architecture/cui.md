# CUI Architecture

## Current boundary

The active CUI language pipeline is `framework/CUI/Language` plus
`framework/CUI/Core`. `CuiRichParser` accepts `.cui` only, requires a `Cui`
root, and rejects legacy `.axaml` and `.hui` inputs. Its document model keeps
authored root metadata, components, resources, actions, literal attributes,
bindings, and direct element text so that a later compiler/runtime sees the
authored surface rather than a lossy convenience tree.

`framework/CUI/src/NineToOne.Cui.Markup.csproj` is retained only as a retired
compatibility shell; it no longer compiles the competing lightweight parser.
New production code must reference CUI Core and Language directly.

## Runtime status

`framework/CUI/Runtime` lowers the document into native Avalonia controls and
resolves CUI semantic theme resources. The earlier missing-public-key and
`MSB4006` build blockers no longer reproduce in the canonical checkout:
the CUI Runtime Release tests and native Home Release build pass. Native
Windows interaction and platform/package validation remain unverified.
The parser's authored component `id` is retained as the native control's
`Name` and default automation ID; an explicit `automation-id` attribute can
override only the automation ID. This contract is covered by the CUI Runtime
suite and a headless test that builds the actual Home host window from its
canonical `Home.cui`, arranges the route, and checks bound unavailable states.
`CuiThemeScope.ResolveThemeName` uses its supplied global default for `Default`,
empty and unknown names. The loader snapshots the active global theme at load
time, so a loader constructed before a theme change still builds the new
palette; explicit nested scopes override it. `ApplyGlobalTheme(theme)` updates
the active catalog theme and application palette for subsequently loaded
surfaces. This does not itself persist the choice across processes or update
already rendered code-built controls.
Haven's `UserPreferencesService` remains the sole writer and migration owner for
`preferences.json` (`IAppPaths.DataDirectory`, default `%APPDATA%/Haven`, or
`HAVEN_DATA_DIR`). The read-only `CuiThemePreferenceReader` consumes its
`havenUiThemeName` for standalone CUI hosts, falling back to Glow if absent or
invalid. Native Home loads the choice before building its CUI window; Boards
loads it at startup and continues to keep its separate light/dark appearance
setting. A newly built Home window rereads the choice. This is restart/reload
propagation, not live cross-process notification or an additional settings store.

The intended runtime order is:

```text
.cui source -> CUI Language/Core -> CUI Runtime -> native platform host
                                      |-> DevTools inspection
                                      |-> semantic global theme resources
```

## Authoring and legacy boundary

`9to1 Workspace/Home/UI/Home.cui` is the canonical Home document. Applications
must not maintain a second demo `.cui` surface for the same product. New CUI
work uses semantic palette resources and explicit actions/bindings; it does not
use HUI or AXAML as an input format.

Existing HUI and AXAML sources remain legacy donor/host material until their
individual migration has been rendered and validated. Their presence means a
full CUI-only claim is currently false.

Shared CUI Language/Core, Runtime and Themes contracts have one framework
owner during convergence. Product lanes consume the documented parser,
resource and binding interfaces; they do not change shared CUI or vendor
sources while those contracts remain under validation. Theme preference
propagation across the remaining native hosts must continue to use Haven's
preference owner; no product-specific copy of the theme choice is authoritative.

The DevTools project attaches a read-only `CuiLiveTreeInspector` to a native
CUI host's loader and control tree. The loader retains authored component
type/ID and `.cui` source spans, action references, and the last observed
binding resolution; inspection never invokes commands or calls a binding
getter again. It reports binding path/mode, resolved and native values,
configured/used fallback, missing source and observed error. An optional
`ICuiActionAvailability` host signal distinguishes explicitly unavailable
routes from unknown availability; a registered dispatcher handler alone does
not assert that its destination is connected. Home marks its disconnected
routes unavailable. DevTools also observes effective Avalonia property values,
authored attributes, resolved semantic resources, the native automation peer's
role/name/states, layout, visibility, opacity, clipping and render scaling.
The actual Home CUI route is inspected after arrangement and on a second theme
window build. Draw-call counts, frame timings, selector-rule provenance and
physical compositor/input behavior are not inferred from these snapshots.

## Verification required before promotion

- Build the repaired vendor, CUI Runtime, Themes, and each native host.
- Render the authored CUI surface on Linux and Windows and exercise input,
  accessibility, layout/DPI, bindings, actions, resources, and theme changes.
- Demonstrate one persisted global theme setting across Home and every migrated
  surface.
- Connect DevTools to a live CUI runtime, including tree, style, binding,
  action, accessibility, and rendering diagnostics.
