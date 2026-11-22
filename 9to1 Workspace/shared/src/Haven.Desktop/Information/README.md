# Desktop composition and event information

Haven.Desktop connects the current profile, application routes, service owners and native window. Start at the unified host composition to find the actual route, then follow the app’s CUI document and bindings. The legacy views and event bus remain part of some supported integrations.

The generated object digest in this directory lists event tokens. Use the producing source to change event definitions. A token describes an available interaction, while the dispatcher or domain owner decides whether the action can run for the current actor.

Shared tabs, settings and Chat have different owners. Keep an event callback or a transient host view from acquiring a second copy of the underlying app state.

[Host composition](../App.UnifiedWorkspaceHost.cs) · [Tabs](../../../../Browse/NativeUI/UI/BrowseWorkspace.cui) · [Settings](../../../../Home/UI/Home.cui) · [Event reference](ObjectDigest.md)
