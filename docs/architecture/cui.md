# CUI Architecture

## Status

CUI is **UNFINISHED**. The active foundation is `framework/CUI/src/NineToOne.Cui.Markup.csproj`. It accepts only `.cui` input, requires a `<Cui>` root, and rejects `.axaml` and `.hui` before parsing. `9to1 Workspace/Home/UI/Home.cui` is an authored consumer.

The current library is a markup loader and document tree only. It is not a CUI compiler, renderer, platform host, editor, language service, resource system, or DevTools implementation. Applications must not present legacy HUI controls or Avalonia XAML as CUI.

## Avalonia Foundation

Existing Linux and Windows legacy hosts use Avalonia packages. This preserves working rendering, input, windowing, text, accessibility, DPI, clipboard, drag/drop, and platform integrations while migration work proceeds. No in-repository Avalonia fork is currently present, so no fork revision, modification set, or rebase strategy can be claimed.

Before CUI is promoted beyond parser-only status, the repository needs a licensed, vendored or maintained Avalonia-derived source tree with upstream repository, immutable revision, MIT notice, modification log, and rebase process. A CUI compiler must lower native CUI constructs to a renderer-neutral scene contract; CUI must not depend on runtime string parsing as its final rendering mechanism.

## Legacy Boundary

HUI, Haven.UI, `.hui`, and `.axaml` still exist in active migration-era code. They are not valid CUI authoring inputs. New CUI work must use `.cui`; conversion from legacy formats is an explicit import task, not silent runtime compatibility.

## Required Next Layers

- Schema and compiler diagnostics for components, bindings, resources, styles, templates, accessibility, and actions.
- Renderer-neutral CUI scene contract backed by the retained Avalonia-derived platform architecture.
- Linux and Windows CUI hosts with native presentation adapters.
- CUI DevTools with tree inspection, layout, style, binding, event, accessibility, and rendering diagnostics.
- A complete HUI and AXAML migration plan that preserves donor material under `reference/` or history documentation.
