# Frame image viewing and editing

Frame is the current product name. The Picture directory and compatible route host the image viewer and editor. Preserve the source file and its metadata when applying a nondestructive edit or exporting a separate image.

Open a supported image, exercise fit, zoom and pan, navigate to an adjacent file and return. Check that any crop applies to the intended source coordinates. Export to a separate destination, reopen the result and confirm the original remains usable.

Format support depends on the real decoder and encoder on the target platform. Treat unsupported input, a missing file and an unavailable output destination separately. Keep decoder provenance and source-image ownership with the relevant adapter.

## Focused validation

```powershell
dotnet build "9to1 Workspace/Picture/HavenOS.Images.csproj" -c Release --no-restore -p:UsedAvaloniaProducts=
dotnet test "9to1 Workspace/Picture/Tests/HavenOS.Images.Tests.csproj" -c Release --no-restore -p:UsedAvaloniaProducts=
```

[Image app](HavenOS.Images.csproj) · [Image UI](UI) · [Owning tests](Tests) · [Frame and compatible aliases](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
