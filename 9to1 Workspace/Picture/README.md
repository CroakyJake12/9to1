# Picture

Picture is the HavenOS image viewer and editing surface under active development. The current local Avalonia implementation opens an image, previews it, supports fit/zoom/pan and adjacent-file navigation, and records a non-destructive crop operation before exporting a PNG copy.

## Current local journey

1. Open a local PNG, JPEG, BMP, GIF, or WebP supported by the platform decoder.
2. View the image with fit, zoom, pointer-centred wheel zoom, and drag pan.
3. Browse neighboring supported files in the same directory.
4. Inspect the file signature, decoded pixel dimensions, file size, and metadata exposed by the installed reader through **Info**.
5. Enter crop bounds in source-image pixels and export a new PNG file.
6. Choose **Preserve supported metadata**, **Remove location (strips all metadata)**, or **Remove all metadata**. The location option currently strips all metadata because this implementation cannot reliably distinguish every location-bearing field across formats.

The source image is protected from replacement. Crop edits remain non-destructive until export. Unsupported/corrupt data and metadata-reader limitations are surfaced as unavailable or failed operations.

## Current limits

This is not yet the complete Picture product contract. Raster brush/eraser, selections and transforms, layers, vector/text tools, history, AI generation/editing, linked Files identity and save workflows, color-profile handling, broad format writing, shared Home/CUI integration, and accessibility/device validation still require implementation or owner integration. Metadata DPI and ICC profiles are not currently reported. Donor trees in `Source/glycin` and `Source/loupe` are provenance inputs only; they are not integrated runtime dependencies.

## Focused validation

```powershell
dotnet build "9to1 Workspace/Picture/HavenOS.Images.csproj" -c Release --no-restore -p:UsedAvaloniaProducts=
dotnet test "9to1 Workspace/Picture/Tests/HavenOS.Images.Tests.csproj" -c Release --no-restore -p:UsedAvaloniaProducts=
```
