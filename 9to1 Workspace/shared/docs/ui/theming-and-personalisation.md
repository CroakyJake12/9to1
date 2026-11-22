# Native themes and personalisation

Home owns the device profile’s theme, accessibility settings, accent preferences and display choices. Mode selects Light, Dark or Follow system. Brightness changes material lightness and Colour contrast changes separation between chrome layers. Their neutral values are 50. High contrast takes presentation priority while retaining the saved levels.

Consume semantic brushes from the native CUI theme context. Preserve the user’s global or per-app accent, authored document colours and site content. Custom theme libraries are bounded declarative records with stable identities; the preference owner performs authorization and revision checks. Theme choices, accessibility, fonts and avatars have distinct owners and lifecycles.

On this checkout, the native Home sliders and displayed percentage use a draft before Apply. Check the rendered material after applying, then reopen the same device profile and compare it to the confirmed preference. Check a second app accent, high contrast and an unrelated legacy preference change. Keep evidence of rendering separate from the save receipt.

## Custom themes and type

The custom library can hold up to 32 bounded records within 1 MiB. Preserve its stable IDs and unique names. Use the interface and code font overrides through the shared font resolver; fallback glyphs must leave authored text intact. The renderer observes Home’s scoped profile through `CuiThemePreferenceContext`. Changing an accent or accessibility setting should not write a new document, duplicate the theme library or change an avatar asset.

[Controls](../../../Home/UI/Home.cui) · [Native preference adapter](../../src/Haven.Desktop/Services/NativeHomeThemePreferences.cs) · [Accessibility and display levels](../../../../framework/CUI/Themes/CuiAccessibilitySettings.cs) · [Resource projection](../../../../framework/CUI/Themes/CuiThemeResourceApplier.cs) · [Appearance decisions](../../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md) · [Theme context](../../../../framework/CUI/Themes/CuiThemePreferenceContext.cs)
