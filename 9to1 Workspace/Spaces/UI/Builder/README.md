# Space configuration editor

`SpaceBuilder.cui` is a canonical CUI projection of the existing `SpaceRegistry`
record. The browser route `app.spaces`, entity type `Space`, canonical SpaceID,
action `edit` uses `BrowserSpaceBuilderSession` over the same signed-profile
storage lifetime as the Spaces manager. Navigation caches the editing session;
account revocation clears private fields immediately and drains admitted work.

Basic and Advanced use `SpaceRegistry.ApplyBuilderBasicAsync` and
`ApplyBuilderAdvancedAsync`. Both require the revision originally read by the
editor and patch the current owner record. Basic updates only name, description,
icon and instructions. Advanced additionally updates the existing declared
model, context, memory, capability-binding and inheritance policy properties.
Neither copies a new Space, replaces the complete record, renumbers identities,
changes its parent, removes resources, or discards its retained layout. These APIs
add no persisted schema or second configuration store. Policy declaration does
not grant access or attest effective execution support.

A stale save retains the local draft. Refresh reads the saved revision without
rebasing or deleting unsaved changes. Discard unsaved changes explicitly adopts
the current saved record. An uncertain save prevents replay; the original error
is retained by the session lifetime. Refresh and inspection are available before
the user chooses the saved version. Privacy revocation cannot reopen the old
session or publish its delayed results.

This is an incomplete implementation of the revised Spaces Builder. The Expert
navigation exposes the five specified page names, but graph authoring,
publication, activation and execution are not composed here. Existing legacy
layout nodes are preserved and explicitly identified as non-executable. Resource
references can be inspected; a managed folder workspace and package import are
not implemented. No Preview/Activate action is offered without its real owner.
Category gates, mandatory-skill validation, model capability inspection,
Automation bindings, specialist selection, full CUI customisation and the shared
node editor remain required work. The UI does not claim those settings run in
this browser.

The owning architecture references are the shared
`docs/architecture/system-map.md`, `docs/architecture/state-and-persistence.md`
and `docs/ui/building-ui.md`. Independent full Spaces acceptance, native-device
operation and authenticated browser execution remain separate gates.
