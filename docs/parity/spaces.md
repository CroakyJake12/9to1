# Spaces Parity Record

## Platform-Neutral Spaces Model

- STATUS: PARTIAL
- SCOPE: `9to1 Workspace/Spaces/`
- IMPLEMENTED: `SpacesModel` defines separate stable scopes for Chat, Study, Tasks, and each custom Space. It creates and enumerates custom Spaces through the existing `Haven.Application.SpaceRegistry`; it does not create a second store or duplicate existing product surfaces.
- SIDEBAR: typed `SpacesSidebarDestination` entries expose the three built-in descriptors followed by active custom Spaces.
- INTEGRATION: `SpacesContext`, `SpacesAction`, and `ISpacesActionHost` are platform-neutral contracts. `SpacesNavigationActionHost` adapts an action to the existing navigation host while preserving the registered Space record and current routing owner.

## CUI Dependency

- STATUS: BLOCKED
- No `.cui` source is authored for Spaces. `framework/CUI/` currently provides a parser and explicit-path loader only; `HavenOS.Spaces` has no CUI project reference, resource discovery, renderer, or host invocation.
- DEPENDENCY: CUI must supply a Spaces-facing resource/load contract and a platform-neutral renderer/host integration point before a Spaces `.cui` surface can be verified.

## Verification

- `HavenOS.Spaces.Tests`: 11 model tests passed in Debug and Release during this consolidation pass.
- No CUI load, renderer, platform launch, package, Dulche action, node editor, or donor-surface parity claim is made.
