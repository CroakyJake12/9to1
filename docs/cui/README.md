# Canonical CUI framework

CUI owns first-party interface structure, authored actions, scoped presentation and semantic control resources. The language model and compiler validate the document; the runtime binds the resulting tree to an actual host and dispatches typed actions. The maintained backend provides native rendering and input.

Start with a real app document. A change to a shared component should preserve its keyboard and accessibility behavior across every caller. Check the scope where a theme or action is resolved before changing its default.

[Language](../../framework/CUI/Language) · [Compiler](../../framework/CUI/Compiler) · [Runtime](../../framework/CUI/Runtime) · [Themes](../../framework/CUI/Themes/README.md) · [Home example](../../9to1%20Workspace/Home/UI/Home.cui)
