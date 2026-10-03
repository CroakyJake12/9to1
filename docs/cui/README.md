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

The repository includes a native runtime and vendored framework source. Their current build, render, interaction and platform acceptance must be observed in this environment; historical build results do not establish present readiness. See [the Astra working ledger](../ASTRA-WORKING-LEDGER.md) for current evidence and [the CUI architecture reference](../architecture/cui.md) for technical context. The current Google Drive specification remains the product authority.
