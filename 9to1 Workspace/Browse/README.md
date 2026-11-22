# Browse sessions and Haven workspace tabs

Browse owns browser sessions, engine choices, navigation records and native workspace chrome. The shared application tabs host both internal destinations and web pages. Keep stable tab identity, active selection and close authority when reordering or changing presentation.

Only the top application strip aligns to the left edge. At narrower widths preserve overflow, keyboard selection, an accessible close target and a recoverable active tab. Dashboard and New Tab remain separate. A change to the native tab strip should reach the actual Haven composition without moving browser data into a UI object.

Use the source and host-specific tests for the engine and its lifecycle. Preserve private navigation boundaries and keep a platform-specific runtime result attached to its tested version.

## Browser engine and private-tab rules

Explicit tab engine choice precedes a site rule, which precedes the default. Changing a site rule must preserve an explicit per-tab choice. If a renderer fails to start, keep the requested destination and surface recovery. A navigation request alone is insufficient to reset document-dependent state.

Ctrl+Shift+T reopens a standard tab and Ctrl+Shift+P creates a private one. The in-memory recovery list holds up to 25 standard tabs with their original placement and engine choice. Private tabs stay outside that list and their permission grants remain tied to origin, permission kind and the private tab. The allowlisted `browse://` pages resolve natively; an app route should retain its typed address and actor-bound owner.

[Workspace CUI](NativeUI/UI/BrowseWorkspace.cui) · [Engine policy](BrowseEngineSelectionPolicy.cs) · [Native workspace tests](NativeUI/Tests) · [Host tests](Host.Tests) · [Browse and shell requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
