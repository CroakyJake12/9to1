# Theming and Personalisation

How 9to1's five themes, four appearances, accent override, global font and
profile pictures work — and how apps consume them. Implementation lives in
`src/Haven.Desktop/HavenUI/Tokens/`, `Controls/SurfacePaletteCatalog.cs`,
`HavenUI/Tokens` applier, and `Services/UserPreferencesService.cs`.

## Current shared CUI account themes

The current user-directed theme creator uses `framework/CUI/Themes/CuiCustomTheme.cs`.
`CuiCustomThemeContract` validates bounded declarative values and produces canonical
JSON for the existing Home account preference owner. It contains no executable
expressions, downloaded assets, renderer objects, credentials or document formatting.
A library contains at most 32 themes (1 MiB), with stable UUIDs and unique names.
Home/account transport owns durable compare-and-swap, authorisation and cache scope;
the CUI catalog is only a presentation view of that observation.

The built-in choices now include Professional, Glow, Playful, Frosted Glass,
Bubble, Metallic, Retro and Western. Existing Cinematic numeric ID 4 remains
readable; new IDs are appended after Professional 5. Each custom theme has three
independent solid, linear/radial/conic gradient or stripes/checkerboard/dots pattern
fills; each colour retains transparency and Gaussian backdrop radius. Solid and
multi-colour backdrop execution uses the shared material renderer, separately from
Professional's Mica treatment; a foreground blur does not satisfy that contract.

Seven explicit typography roles are Main, PrimaryButton, SecondaryButton,
TertiaryButton, Heading, Body and Code. The creator offers only packaged families
and available weights, so a missing OS font cannot silently replace the selection.
Custom selections are an explicit user override of the historical Montserrat-only
chrome rule; the built-in interface still uses bundled Montserrat. Theme typography
does not modify authored document text. `FontRole` selects a role on ordinary CUI
text, input or button controls. `AccentRole` on a button selects Primary, Secondary
or Tertiary fill and, unless explicitly overridden, its corresponding button font.
Authored Definition/Instance/Region values retain normal precedence over Theme.
Button roundness is a percentage of half the actual shortest control dimension:
0 is square and 100 is fully rounded, recalculated when the control is resized.

`CuiThemeDefinitionCatalog.UpsertCustomTheme` and `RemoveCustomTheme` publish a
local catalog change without writing Home or inventing a Home revision. The theme
creator can use a fixed named scope and private draft ID for a real live preview.
`ReplaceAccountCustomThemes` atomically replaces only account-owned IDs and retains
that draft. `CuiThemePreferenceContext` reconciles the signed-in account observation
and refreshes named scopes when the catalog changes. Resource snapshots include
the canonical custom fingerprint; same-name edits therefore update the existing
controls. Mutable pattern drawing resources are copied per renderer owner.

The sections below describe the historical Desktop personalisation pipeline where
it has not yet been migrated to the shared account contract; they are not evidence
that the browser implementation has passed native device or whole-app acceptance.

## The model

```text
Theme (Glow | Bubble | Retro | Playful | Cinematic)
   ↓  expression: radius/motion/shadow scales + interaction treatment
Appearance (SuperBright | Bright | Dark | SuperDark)   ← the shared light/dark control
   ↓  colour branch
Accent source: override ON → selected palette (13 semantic families)
               override OFF → per-surface hue anchors
   ↓
SurfacePaletteCatalog.For(surface, appearance) → Palette (semantic colours)
   ↓ HavenUiResourceApplier.Apply()
application resources (brushes, radii, motion scale) → Avalonia styles + HUI scenes
```

- **Themes decide how things look and react; never where they go.** Layout,
  navigation, spacing rhythm and information architecture are theme-invariant.
- **Appearances are the Light/Dark dimension** 9to1 already had: Bright and
  SuperBright are the light expressions, Dark and SuperDark the dark ones.
  Every theme ships all four.
- **Glow is the baseline**: with default personalisation the pipeline is an
  identity transform, byte-identical to the pre-theme appearance (guarded by
  `PersonalisationTests.Glow_palette_matches_the_pre_theme_baseline`).

## Theme identities

| Theme | Personality | Signature |
|---|---|---|
| Glow (default/fallback) | tidal gradients, soft glow | unchanged baseline |
| Bubble | soft, glassy, atmospheric | translucent panels, bloom hover, larger radii |
| Retro | engineered, technical | hairline illuminated borders, veil hover, sharp corners, fast motion |
| Playful | tactile, tonal, friendly | opaque tonal fills, bold hover fills, pill radii |
| Cinematic | immersive, depth-driven | tinted translucent layers, heavier shadows, slow fades |

Each non-Glow theme changes hover/press/focus derivation, border treatment,
panel translucency and geometry/motion scales (`HavenThemeCatalog.All`) — not
just hues.

## Accent precedence

Override **off**: surface accent → theme interpretation → semantic brushes.
Override **on**: selected palette anchors → same interpretation. Apps only
ever consume semantic resources (`HavenAccentPrimaryBrush`, …). Never read the
override flag in app code. GenUI `AccentKey` scoping continues to win inside
its `HavenAccentScope`.

All 13 palettes (Red…Monotone) resolve per appearance family; Yellow/Lime use
deepened strong anchors for contrast; Monotone is true grayscale with strong
contrast while status colours keep their meaning.

## Fonts

`HavenUiFont` resolves every "Montserrat" request through the user's selection
with the bundled Montserrat face as guaranteed fallback
(`"User Font, bundled#Montserrat"`). AXAML consumes `HavenFontFamily` as a
dynamic resource; HUI text goes through `HavenSceneControl`'s resolver.
Selection uses OS-installed families via `FontManager.SystemFonts`; missing
fonts degrade down the fallback chain.

## Avatars

Independent opt-ins (`UserAvatarEnabled`, `HavenAvatarEnabled`) rendered in
chat via the optional `Avatar` element in `ChatUserMessage.hui` /
`ChatAssistantMessage.hui`, fed `avatar://user` / `avatar://haven` sources by
the scene. `HavenDesktopImageResolver` resolves that scheme from
`AvatarStore`'s processed local files. Disabled by default; enabling requires
a stored asset; removing the asset disables rendering. Images never leave the
device.

## Live updates

Changing theme/appearance/accent/font/avatar applies immediately:
preferences publish into shared state (`HavenPersonalisation`) and mutate
existing resources in place — no shell rebuild, no restart, active tabs/text/
tasks survive. `AppearanceChanged` fires after any personalisation change;
background consumers re-render from it.

## What app developers must NOT do

- Do not branch on `if Theme == Bubble` in product code; express differences
  through tokens or extend the catalog/applier.
- Do not hard-code hex colours in UI (exception: canonical palette definitions,
  e.g. the settings swatches).
- Do not create app-local theme/accent/font/avatar stores — consume the
  shared one.
- Do not change layout to express a theme.
