# Picture

Picture is the creation/editing app defined by the [9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit). The retained local Avalonia editor opens images, supports fit/zoom/pan and adjacent-file navigation, and previews a non-destructive crop before exporting a separate PNG. The editable operation graph also supports quarter-turn rotation, flips and high-quality resize replay from the original source.

## Current local journey

1. Open a local PNG, JPEG, BMP, GIF, or WebP supported by the platform decoder.
2. View the image with fit, zoom, pointer-centred wheel zoom, and drag pan.
3. Browse neighboring supported files in the same directory.
4. Inspect the file signature, decoded pixel dimensions, file size, and metadata exposed by the installed reader through **Info**.
5. Enter crop bounds in source-image pixels and export a new PNG file.
6. Choose **Preserve supported metadata**, **Remove supported location metadata**, or **Remove all metadata**. Location removal clears the EXIF GPS directory and supported XMP location fields while preserving other supported metadata, including title/comment. It does not infer location hidden in arbitrary free text.

The source image is protected from replacement. Crop edits remain non-destructive until export. Unsupported/corrupt data and metadata-reader limitations are surfaced as unavailable or failed operations.

## Canonical Files adapters

`PictureArtifactCodec` validates a portable editable envelope with a stable DocumentID, a separate backing Files identity and a revision-pinned raw source reference (AssetID, FileID, revision, hash and size). It rejects machine paths and mismatched identities. `PictureFilesArtifactBridge` registers this editable artifact in the explicit canonical Files folder and publishes flushed immutable candidates through Files revision CAS; its original source remains separately revision-addressable. A failed or stale save never replaces the prior committed revision.

`PictureFilesSourceRenderer` accepts the shared Files-owned retained source lease, verifies its exact identities and byte proof, and releases it before replaying the existing raster graph from captured memory. Native hosts must inject the authenticated profile/provider, current Files authorization and retained lease resolver. `PictureAppAiContext` supplies permission-filtered semantic context to the shared Dulche coordinator; it provides no private AI bar or mutation permission. CUI Save/Export requires the separate backing artifact identity and cannot target the raw source ID.

## Current limits

`UI/PictureWorkspace.cui` and `PictureCuiWorkspace` provide markup/semantic bindings over the same editable document. A real `PictureRasterSurface`, Home readiness and brokered typed commands must be mounted by the native host; source/parser checks do not prove integration.

This is not the complete Picture contract. Brush/eraser, mature selection, full composition/layer graph, advanced transforms/colour/effects, vector/text, shared history, AI editing/generation, live Files/Home host composition, colour profiles, animation, broad format writing and complete CUI/device validation remain release gates. Controlled `Source/glycin` and `Source/loupe` provenance does not establish runtime parity. Current source lease/raster tests include injected trusted fixtures; they do not prove production OS/profile binding.

## Focused validation

```powershell
dotnet build "9to1 Workspace/Picture/HavenOS.Images.csproj" -c Release
dotnet test "9to1 Workspace/Picture/Tests/HavenOS.Images.Tests.csproj" -c Release
```
