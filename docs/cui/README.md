# CUI Foundation

The authoritative CUI source is `framework/CUI`:

- `Language` parses only `.cui` documents and rejects `.axaml` and `.hui`.
- `Core` owns the renderer-neutral document tree.
- `Compiler` and `Runtime` are the intended lowering and native-host layers.
- `Themes`, `AI`, and `DevTools` are framework extensions, not replacements for
  the language/core boundary.

The former lightweight `NineToOne.Cui.Markup` parser is retired from active
compilation. Do not add a second parser or reference historical
`9to1 OS/HUI/Cui` project paths.

The native runtime is not presently build-verified because the checked-in
Avalonia vendor tree imports a missing build file and has restore-time target
cycles. See
`docs/MASTER-MIGRATION-STATUS.md` for the evidence ledger and
`docs/architecture/cui.md` for the required runtime verification chain.
