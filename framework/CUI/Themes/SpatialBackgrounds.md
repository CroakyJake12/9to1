# Render a custom spatial background

The portable fill describes colours, opacity, geometry and backdrop blur. `CuiCustomMaterialBrush` converts a validated fill for the backend. Apply it to a background role; ordinary foreground and icon resources use normal fills.

A gradient blends the per-stop Gaussian results over the original backdrop. Equal radii can share a branch. A zero radius preserves the source. Pattern regions use their own foreground or background colour, transparency and blur in the same geometry as their masks. Preserve the fill’s bounds, rotation and scale under resizing.

Verify the result on an actual native target with that backend’s supported transparency path. Exercise transparent, opaque and mixed-stop fills and retain an opaque fallback when the platform cannot negotiate transparency.

## Gradient and pattern geometry

The filter graph allows two to sixteen validated gradient stops. Interpolate each stop’s independently blurred original backdrop in the same proportions as its colour contribution. Preserve the authored linear, elliptical radial or conic geometry. For stripes, checkerboards and dots, preserve cell size, spacing and rotation around the surface centre. A fully transparent region reveals its original or filtered backdrop; it must not expose the opposite pattern tint.

[Custom material](CuiCustomMaterialBrush.cs) · [Resource mapping](CuiCustomThemeResourceApplier.cs) · [Native material](CuiMaterialBrush.cs)
