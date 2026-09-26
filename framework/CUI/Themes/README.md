# CUI Theming

How CUI's six themes, four appearances, accent override, and DefaultTheme scoping
work — and how apps consume them.

## Architecture

```text
Theme (Glow | Bubble | Retro | Playful | Cinematic)
   ↓  expression: radius/motion/shadow scales + interaction treatment
Appearance (SuperBright | Bright | Dark | SuperDark)   ← the shared light/dark control
   ↓  colour branch
Accent source: override ON → selected palette (13 semantic families)
               override OFF → per-surface hue anchors
   ↓
CuiSurfacePaletteCatalog.For(surface, appearance) → CuiPalette (semantic colours)
   ↓ CuiThemeResourceApplier.Apply()
application resources (brushes, radii, motion scale) → Avalonia controls
```

## Files

| File | Purpose | Original Source |
|---|---|---|
| `CuiThemeEnums.cs` | CuiTheme, CuiAppearance, CuiAccentColour enums | `HavenPersonalisationModels.cs`, `GenerativeUiModels.cs` |
| `CuiThemeExpression.cs` | Theme personality record (radius/motion/shadow scales) | `HavenThemeCatalog.cs` |
| `CuiThemeCatalog.cs` | Catalogue of five canonical themes with fallback logic | `HavenThemeCatalog.cs` |
| `CuiAccentPalette.cs` | Three-tier accent gradient system | `HavenAccentPalette.cs` |
| `CuiAccentCatalog.cs` | 13 semantic accent palettes (light/dark) | `AccentColourCatalog.cs` |
| `CuiMotion.cs` | Canonical motion durations | `HavenUiMotion.cs` |
| `CuiSurfacePaletteCatalog.cs` | Surface × appearance × theme → semantic palette | `SurfacePaletteCatalog.cs` |
| `CuiThemeResourceApplier.cs` | Applies palettes to Avalonia resources | `HavenUiResourceApplier.cs` |
| `CuiThemeScope.cs` | DefaultTheme scoping, stack, and control application | New CUI-native construct |

## The Model

### Themes

Six canonical themes. **Glow is the baseline identity transform** — with default
personalisation, every value is byte-identical to the pre-theme appearance.

| Theme | Personality | Signature |
|---|---|---|
| Glow (default/fallback) | tidal gradients, soft glow | unchanged baseline |
| Bubble | soft, glassy, atmospheric | translucent panels, bloom hover, larger radii |
| Retro | engineered, technical | hairline illuminated borders, veil hover, sharp corners, fast motion |
| Playful | tactile, tonal, friendly | opaque tonal fills, bold hover fills, pill radii |
| Cinematic | immersive, depth-driven | tinted translucent layers, heavier shadows, slow fades |
| Professional | restrained, balanced | familiar controls, clear hierarchy, neutral geometry |

### Appearances

Four brightness variants, separate from themes:
- **SuperBright** — maximum lightness
- **Bright** — standard light mode
- **Dark** — standard dark mode
- **SuperDark** — maximum darkness (default)

### Theme Expressions

Each theme has six scale factors:

| Factor | Glow | Bubble | Retro | Playful | Cinematic | Professional |
|---|---|---|---|---|---|---|
| ControlRadiusScale | 1.0 | 1.35 | 0.45 | 1.5 | 1.0 | 1.0 |
| CardRadiusScale | 1.0 | 1.3 | 0.55 | 1.35 | 1.05 | 1.0 |
| PopupRadiusScale | 1.0 | 1.25 | 0.6 | 1.3 | 1.1 | 1.0 |
| MotionDurationScale | 1.0 | 1.15 | 0.7 | 0.9 | 1.25 | 1.0 |
| ShadowOpacityScale | 1.0 | 1.35 | 0.75 | 0.9 | 1.7 | 1.0 |
| BorderIntensity | 1.0 | 0.8 | 1.25 | 1.1 | 0.95 | 1.0 |

Base radii: Control=10, Card=16, Popup=20.

Themes also provide spacing, typography, control-height and elevation scales.
The shared Montserrat-first interface font and code font stacks are exposed as
`CuiFontFamilyInterface` and `CuiFontFamilyCode`; user display scaling is applied
to typography, spacing and control sizing without changing application state.
Reduced-motion and high-contrast preferences are framework resources and can be
passed to the theme applier as `CuiAccessibilitySettings`.

## DefaultTheme Scoping

`DefaultTheme` is a first-class CUI construct that scopes theme defaults for a
subtree. It does NOT create a visual control.

### Syntax

```xml
<DefaultTheme value="Bubble">
  <Page>
    <TextBlock text="Bubble themed" />
  </Page>
</DefaultTheme>
```

### Resolution

| Value | Resolves to |
|---|---|
| `Default` | Currently configured global theme (from personalisation) |
| `Glow` | Explicitly pins to Glow |
| `Bubble` | Explicitly pins to Bubble |
| `Retro` | Explicitly pins to Retro |
| `Playful` | Explicitly pins to Playful |
| `Cinematic` | Explicitly pins to Cinematic |
| (empty/null) | Global default |
| (unknown) | Global default (safe fallback) |

### Inheritance

DefaultTheme is inheritable. Nested scopes override, then restore the parent:

```xml
<DefaultTheme value="Glow">
  <Page>
    <TextBlock text="Glow" />
    <DefaultTheme value="Bubble">
      <Card text="Bubble" />
    </DefaultTheme>
    <TextBlock text="Glow again" />
  </Page>
</DefaultTheme>
```

### Precedence

Theme is a **default**, not an override of explicit styling:

```
canonical CUI defaults
    ↓
DefaultTheme
    ↓
appearance / accent / semantic theme resources
    ↓
component defaults
    ↓
classes / styles
    ↓
states
    ↓
explicit local element styling
```

Explicit styles, classes, and element properties always win over theme defaults.

## Accent Precedence

Override **off**: surface accent → theme interpretation → semantic brushes.
Override **on**: selected palette anchors → same interpretation.

13 palettes (Red…Monotone) × 4 appearances. Yellow/Lime use deepened strong
anchors for contrast. Monotone is true grayscale with strong contrast.

## Special Bindings

CUI supports theme-aware bindings:

- `{Binding Theme}` → current theme name (e.g. "Glow")
- `{Binding Appearance}` → current appearance (e.g. "Dark")

## Resource Names

The applier sets both CUI-prefixed and Haven legacy resource names:

| CUI Resource | Haven Legacy | Type |
|---|---|---|
| `CuiBackgroundBrush` | `HavenBackgroundBrush` | SolidColorBrush |
| `CuiTextBrush` | `HavenTextBrush` | SolidColorBrush |
| `CuiPanelBrush` | `HavenPanelBrush` | SolidColorBrush |
| `CuiButtonBrush` | `HavenButtonBrush` | SolidColorBrush |
| `CuiButtonHoverBrush` | `HavenButtonHoverBrush` | SolidColorBrush |
| `CuiFocusBrush` | `HavenFocusBrush` | SolidColorBrush |
| `CuiShadowBrush` | `HavenShadowBrush` | SolidColorBrush |
| `CuiControlRadius` | `HavenControlRadius` | CornerRadius |
| `CuiCardRadius` | `HavenCardRadius` | CornerRadius |
| `CuiPopupRadius` | `HavenPopupRadius` | CornerRadius |
| `CuiMotionDurationScale` | `HavenMotionDurationScale` | double |
| `CuiAccentBrush` | `HavenAccentBrush` | LinearGradientBrush |
| `CuiAccentPrimaryBrush` | `HavenAccentPrimaryBrush` | LinearGradientBrush |
| `CuiAccentInkBrush` | `HavenAccentInkBrush` | SolidColorBrush |
| `CuiAccentSoftBrush` | `HavenAccentSoftBrush` | SolidColorBrush |
| `CuiTheme` | — | CuiTheme enum |
| `CuiAppearance` | — | string |

## Provenance

All theme values, expressions, palette assembly logic, accent derivation, and
interaction treatment were migrated from the original Haven source:

- `9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs`
- `9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenUiResourceApplier.cs`
- `9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenAccentPalette.cs`
- `9to1 Workspace/shared/src/Haven.Desktop/HavenUI/Tokens/HavenUiMotion.cs`
- `9to1 Workspace/shared/src/Haven.Desktop/Controls/SurfacePaletteCatalog.cs`
- `9to1 Workspace/shared/src/Haven.Core/Models/HavenPersonalisationModels.cs`
- `9to1 Workspace/shared/src/Haven.Core/Models/GenerativeUiModels.cs`
- `9to1 Workspace/shared/tests/Haven.Desktop.Tests/PersonalisationTests.cs`

No values were guessed or approximated. Glow is verified byte-identical to the
pre-theme baseline via `CuiThemeTests.Glow_palette_matches_the_pre_theme_baseline`.
