# CUI authoring and native host integration

Write the structure, styles, state and actions for a first-party surface in its canonical CUI document. The compiler validates the authored tree. The runtime resolves the component, binds its state and routes typed actions to the host. CUI templates and semantic resources give the native controls their presentation.

A legacy Haven.UI scene or AXAML host can be part of a supported integration. Trace the actual producer and consumer before replacing it. Keep donor engines, native accessibility, document formats and service lifetimes behind the established boundaries.

Check both a rendered component and its real input path. A successful parse demonstrates language validity. A pointer or keyboard interaction should also reach the owning service and produce an observable result in the active window.

[Compiler](../../../../framework/CUI/Compiler) · [Runtime](../../../../framework/CUI/Runtime) · [Themes](../../../../framework/CUI/Themes/README.md) · [Home example](../../../Home/UI/Home.cui)
