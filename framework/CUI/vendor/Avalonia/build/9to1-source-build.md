# Pinned platform source build

Platform source and restored missing build/native metadata use AvaloniaUI/Avalonia tag12.0.1, immutable commit fe2741d62a467c3efa4f0c5f68f7861bc338c99f. Existing 9to1 source and package-target modifications are preserved. Upstream NOTICE.md and licence.md accompany the source.

The internal build imports the original resource targets in upstream-resource-targets for donor platform primitives, font assets and framework dialogs. These targets do not author a 9to1 product shell. First-party scenes remain canonical .cui documents parsed and rendered by CakeOS.Cui.Runtime; the existing CUI package-target overrides remain separate. The donor asset loader consumes its own original resource format; this build does not pretend renamed CUI resources are donor resources.

Source dependencies are recursive gitlinks with immutable upstream pins:

- https://github.com/kekekeks/XamlX at009d4815470cf4bf71d1adbb633a5d81dcb2bb52.
- https://github.com/AvaloniaUI/Avalonia.DBus at864a05282841bf04006890f04d11d60d1a046aa9.

Build dependencies use project references, including the real internal resource compiler. NuGet security auditing uses the public nuget.org audit source while the original narrowly mapped API-diff transport source is retained.
