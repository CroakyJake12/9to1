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

## Verification required before promotion

- Build the repaired vendor, CUI Runtime, Themes, and each native host.
- Render the authored CUI surface on Linux and Windows and exercise input,
  accessibility, layout/DPI, bindings, actions, resources, and theme changes.
- Demonstrate one persisted global theme setting across Home and every migrated
  surface.
- Connect DevTools to a live CUI runtime, including tree, style, binding,
  action, accessibility, and rendering diagnostics.
