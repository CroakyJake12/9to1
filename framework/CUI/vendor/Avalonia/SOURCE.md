# Avalonia source boundary

This directory contains actual, unmodified Avalonia source from
[`AvaloniaUI/Avalonia`](https://github.com/AvaloniaUI/Avalonia), tag `12.0.1`,
commit `fe2741d62a467c3efa4f0c5f68f7861bc338c99f`.

## Staged slice

The initial CUI fork stages the complete `src/Avalonia.Base/Metadata` directory.
It is compiled locally by `CakeOS.Cui.AvaloniaFork.csproj`; CUI does not obtain
these types from an Avalonia binary package. This narrow slice supplies stable
markup/content metadata while the renderer and platform backends remain an
explicit later migration. It must not be described as the complete Avalonia
runtime.

The upstream MIT text is preserved at `../../LICENSES/Avalonia-MIT.md`. Blob
identities for every staged file are recorded in `SOURCE-MANIFEST.txt`.

## Update strategy

1. Select a reviewed upstream release and resolve its tag to a full commit.
2. In a disposable clone, copy the entire metadata directory from that commit.
3. Regenerate `SOURCE-MANIFEST.txt` with `git ls-tree -r <commit> -- src/Avalonia.Base/Metadata`.
4. Update `.revision`, verify the MIT license/copyright, and review the upstream diff.
5. Build all four CUI projects and run `CakeOS.Cui.Markup.Tests` before landing.

Never copy generated `bin`, `obj`, NuGet packages, or upstream build outputs.
