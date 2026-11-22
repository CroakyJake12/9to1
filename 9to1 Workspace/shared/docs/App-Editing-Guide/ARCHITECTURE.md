# Native application boundaries

Haven.Desktop composes the native window and its services. Haven.Core holds shared domain types. Haven.Application defines use cases and service contracts, and Haven.Infrastructure supplies storage, filesystem and operating-system adapters. Haven.Browser owns browser sessions and data. Changes to a native view can cross these boundaries without moving state ownership into the view.

For an appearance change, trace the CUI action to the current Home preference owner, through the revision-checked write, then back to each host’s resolved theme. For Chat, trace the CUI binding to the Spaces controller and the selected conversation. An embedded and a standalone entry should preserve the same object identity and permission result.

Keep source-authored layout and semantic presentation in CUI. A host adapter can supply a window, profile, dispatcher or existing native engine. Preserve its release, cancellation and pending-save behaviour when a route closes.

[Haven composition](../../src/Haven.Desktop/App.UnifiedWorkspaceHost.cs) · [Native preference source](../../src/Haven.Desktop/Services/NativeHomeThemePreferences.cs) · [Chat controller](../../../Spaces/Source/Chat/ChatSpaceController.cs) · [Persistence](../architecture/state-and-persistence.md)
