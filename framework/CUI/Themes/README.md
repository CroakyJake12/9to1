# Native CUI themes and display resources

Home owns the saved device preference. CUI resolves that observation for the current app and provides semantic colours, text, control sizes, surfaces and focus behavior. Reuse these resources for first-party UI. Preserve authored document and guest content.

Mode selects Light, Dark or Follow system. Brightness and Colour contrast operate independently. The midpoint preserves a preset’s ordinary values; high contrast overrides ordinary visual levels without deleting the saved settings. Built-in and custom themes can also carry a global or per-app accent, with the current app selecting its scoped value.

Professional uses quiet material layers. Glow uses tonal gradients derived from the active accent. UI-002 specifies Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro, Western and Comic. Comic replaces Cinematic in pickers and needs a one-time compatible preference migration. At the reviewed native source revision, the enum and catalog still contain Cinematic. Treat that as an implementation gap; preserve existing saved values until the migration is implemented and verified. For a shared token change, exercise two substantially different accents, inspect selected and disabled states and test actual keyboard focus in the consuming control.

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

[Resource applier](CuiThemeResourceApplier.cs) · [Accessibility and levels](CuiAccessibilitySettings.cs) · [Preference observation](CuiThemePreferenceContext.cs) · [Built-in catalog](CuiThemeCatalog.cs) · [Atlas theme decisions](../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
