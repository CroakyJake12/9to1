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

## Correction and measured fork surface, 30 October 2026

The text above describes the original staged slice ("the complete `src/Avalonia.Base/Metadata`
directory", "actual, unmodified Avalonia source"). That is no longer an accurate description of what is
on disk. Measured by git blob identity against the declared upstream base tag `12.0.1`
(`fe2741d62a46`):

- **5,274** files in this directory whose path contains no `external` component. Of those, **2,245**
  are build outputs (`bin/`, `obj/`, `.dll`, `.pdb`, `.cache`, generated `.json`/`.txt`), and `git
  ls-files` tracks **none** of them — clutter, not provenance.
- Comparing the remaining source against upstream 12.0.1 by git blob identity: **2,872 files are
  byte-identical to upstream**, **20 modified C#/build-source files** — plus `NuGet.Config`, which
  together make the **21** files on the fork branch — and **21 files exist only in our tree** once build
  outputs are excluded, for example `CakeOS.Cui.AvaloniaFork.csproj`,
  `build/upstream-resource-targets/*.props|targets`, generated interop
  (`src/Avalonia.Native/Interop.Generated.cs`, the four Win32 `*.Generated.cs`) and the browser
  `staticwebassets` JS.
- An earlier draft of this bullet reported `5801 / 2948 / 21 / 19`, measured with a looser filter that
  let build outputs into the comparison. The figures above use the single filter stated here, and
  `FORK-MANIFEST.txt` records both blob identities for every file on the fork branch.
- The 21 modifications are substantive framework changes, including
  `src/Avalonia.Controls/Primitives/Popup.cs` (adds `BeforeShow`, raising the real `PopupRoot`
  destination so CUI can request per-pixel alpha before first show, re-fired per open),
  `ToolTip.cs`/`ContextMenu.cs` (a `ConfigurePopup` seam so both route through that same per-open
  seam rather than a process-wide default), `Presenters/TextPresenter.cs`
  (`CreateTextLayoutInternal` made `protected virtual`, plus `RefreshCaretBounds()`),
  `src/Avalonia.Base/Utilities/Ref.cs` (`IRef<T>.CloneAs<TResult>` removed),
  `src/Avalonia.Base/Avalonia.Base.csproj` (the `Avalonia.Build.Tasks` project reference commented out
  in favour of the CUI compiler), the platform `.csproj` files (Skia, Win32, Win32.Automation, X11,
  HarfBuzz, MicroCom, Headless) which reference source projects directly instead of the meta-package,
  `build/BuildTargets.targets` (pins the tool output location against per-RID properties),
  `NuGet.Config` and `HeadlessUnitTestSession.cs` (a session-start race).
- `SOURCE-MANIFEST.txt` was checked and is correct for what it covers: **20/20** of its blobs equal the
  upstream 12.0.1 blobs (for example `AmbientAttribute.cs` = `1c85a67641fb…` in the manifest, upstream
  and on disk). The gap is coverage, not accuracy: it covers only the 20-file Metadata slice, not the
  whole vendored tree.
- 2,245 build outputs sit inside this directory on disk; `git ls-files` shows **none** of them tracked,
  so they are clutter rather than provenance.

This directory is therefore a **fork of Avalonia**, not unmodified vendored source, and it now has a
controlled fork: `https://github.com/CroakyJake12/Avalonia`, branch `9to1/cui-framework-20261030`, tip `b5bdd8cb9ada162d0282ecb55d4f63409532eadd`, with the
modification split into three reviewable commits on the 12.0.1 base. Every file on that branch is
byte-identical to the corresponding file in this directory (21/21 verified), and
`FORK-MANIFEST.txt` here records both blob identities per file.

The fork branch carries the 21 modifications only (verified: 21 `M` entries, no additions). The 21
non-upstream files of ours (`CakeOS.Cui.AvaloniaFork.csproj`,
`build/upstream-resource-targets/*.props|targets`, generated interop such as
`src/Avalonia.Native/Interop.Generated.cs` and the four Win32 `*.Generated.cs`, and the browser
`staticwebassets`) are **not** on it yet.

Not yet done, deliberately: this directory is still a loose tree rather than a checkout of the fork,
because switching it to a pinned submodule changes how the framework builds and needs a build
verification on this PC plus a decision on where each of our own files belongs - the authored ones
(`CakeOS.Cui.AvaloniaFork.csproj`, the `upstream-resource-targets` props/targets) arguably on the fork,
the generated interop and browser assets arguably build output rather than donor source history. Both
are item 4 of `docs/development/donor-integration-plans-2026-10-30.md`, awaiting owner approval.
