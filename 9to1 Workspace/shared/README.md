# Haven native host and shared services

This solution contains the desktop application, shared domain and service assemblies, browser state and persistence used by several native entry points. Start in the host for the route you are changing. A standalone product entry can borrow Haven’s existing window composition and services while selecting a different initial destination.

The Browse workspace owns the outer application tabs. Home supplies global settings and the device-local theme. Spaces supplies the canonical chat surface and controller. Keep these owners when an edit affects both a standalone app and the embedded experience.

For a desktop change, use the guarded build and managed preview procedure for the canonical checkout. Verify the genuine route in the candidate and record its revision before comparing it with the Atlas.

[Haven solution](Haven.sln) · [Browse workspace](../Browse/NativeUI/UI/BrowseWorkspace.cui) · [Home](../Home/UI/Home.cui) · [Spaces Chat](../Spaces/UI/Chat/ChatSpace.cui) · [Development preview](../../docs/development/live-preview.md)
