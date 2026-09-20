# CUI Foundation

`9to1 OS/HUI/Cui/CakeOS.Cui.Markup.csproj` is the active CUI foundation. It
parses a small renderer-neutral CUI tree with a required `<Cui>` root and loads
only `.cui` files. Both the parser and file loader explicitly reject `.axaml`
and `.hui` inputs.

The CUI project has no references to `Haven.UI`, `CakeOS.Hui.Renderer`, or an
Avalonia package. All material under `9to1 OS/HUI/vendor/Haven.UI`,
`HuiRenderer`, and the existing HUI hosts remains legacy HUI implementation;
none is an input or runtime dependency for new CUI documents.

The existing `CakeOS.Hui.Renderer` remains the Avalonia-package backend that
translates Haven scene draw commands. No Avalonia framework source or fork is
staged here, and CUI makes no claim to provide one. The unchanged Haven donor
is pinned at `9to1 OS/HUI/vendor/.donor-revision` (`7c021082565b3e0ef9110bc4a1287ca3cc2c1fbb`);
the CUI foundation copies no donor or Avalonia source. A future CUI-to-renderer
adapter requires an approved shared scene/draw contract and applicable source
provenance and licensing before importing or deriving any renderer code.
