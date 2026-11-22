# Build the pinned Avalonia platform source

Use Avalonia 12.0.1 at `fe2741d62a467c3efa4f0c5f68f7861bc338c99f` for this source build. Preserve restored platform metadata, 9to1 source and package-target patches, and the accompanying upstream notices. The resource targets compile donor primitives and dialogs. CUI owns first-party application presentation.

The build references the original resource compiler and pinned XamlX and Avalonia.DBus submodules. Verify the actual gitlinks before compiling or changing target imports. Preserve the existing narrow API-diff source mapping and NuGet security audit configuration.

## Pinned submodules

The recorded XamlX revision is `009d4815470cf4bf71d1adbb633a5d81dcb2bb52`. The Avalonia.DBus gitlink is `864a05282841bf04006890f04d11d60d1a046aa9`. Verify both against the containing tree when changing or reproducing a build. Preserve both when reproducing this platform build and record a deliberate pin change in the source provenance.

[Source record](../SOURCE.md) · [Notices](../NOTICE.md) · [Licence](../licence.md)
