# CUI framework donor provenance - Avalonia

Canonical upstream: https://github.com/AvaloniaUI/Avalonia
Controlled fork:    https://github.com/CroakyJake12/Avalonia
Upstream base tag:  12.0.1 = fe2741d62a467c3efa4f0c5f68f7861bc338c99f
9to1 fork branch:   9to1/cui-framework-20261030
Fork tip pinned:    1c465a47b097e849250f3928a8ea8c475a02ec32

Vendored as a loose tree at `framework/CUI/vendor/Avalonia` (not yet a submodule pin - the switch also
has to move the two nested gitlinks `external/Avalonia.DBus` and `external/XamlX` into the fork's own
`.gitmodules`, and update the ARM donor-compiler stage, the source-identity manifest and the
fork-extra-source manifest in the same verified unit).

Licence: MIT, upstream notice files retained in the vendored tree at `NOTICE.md` and `licence.md`.

Measured fork surface (30 October 2026, by git blob identity against the base tag):
- 21 modified source/build files, 43 real content differences and 0 line-ending-only differences;
- 21 files added by 9to1 (authored build glue + generated interop + browser assets), of which the
  generated ones are upstream-gitignored build inputs;
- 2,948 files byte-identical to upstream under the compared filter.

Measured fork-vs-tree equivalence (30 October 2026, comparing `HEAD:framework/CUI/vendor/Avalonia`
against the fork branch tip): 3,027 shared paths with 3,027 identical blobs and 0 differing, 0 paths
unique to our tree after the provenance files were relocated out of it, and the remaining fork-side
paths are upstream files this repository never vendored. Full per-file identities are in
`framework/CUI/Source/AVALONIA-FORK-MANIFEST.txt`.

CUI boundary (unchanged by pinning): the Avalonia FluentTheme engine stays forbidden throughout CUI;
CUI owns presentation, and Avalonia supplies low-level rendering, input, layout and platform
capabilities behind the CUI boundary. See `AVALONIA-SOURCE.md` in this directory for the original
staged-slice record, which predates the measured full-tree state above and is kept as history.
