# Spaces parity record

## Platform-neutral Spaces model

- STATUS: PARTIAL
- SCOPE: `9to1 Workspace/Spaces/`
- IMPLEMENTED: `SpacesModel` defines separate stable scopes for Chat, Study,
  Tasks, and each custom Space. The model creates and enumerates custom Spaces
  through the existing `Haven.Application.SpaceRegistry`; it does not create a
  second store or duplicate the existing Chat, Study, or Tasks surfaces.
- SIDEBAR: typed `SpacesSidebarDestination` entries expose the three built-in
  descriptors followed by active custom Spaces.
- INTEGRATION: `SpacesContext`, `SpacesAction`, and `ISpacesActionHost` are
  platform-neutral contracts. `SpacesNavigationActionHost` adapts an action to
  the existing `ISpacesNavigationHost`, preserving the registered Space record
  and existing shell routing as the source of truth.

## CUI dependency

- STATUS: BLOCKED
- No `.cui` source was added. `CakeOS.Cui.Markup` currently provides an
  uncommitted, standalone `CuiMarkupParser` and explicit-path
  `CuiMarkupLoader` under `9to1 OS/HUI/Cui/`, but `HavenOS.Spaces` has no
  project reference to it, no `.cui` resource discovery, and no renderer or
  host invocation. Its loader therefore is not an active source-load contract
  for the Spaces app.
- DEPENDENCY: the CUI owner must publish the parser/loader and provide the
  Spaces-facing project reference, source discovery/loading registration,
  validation boundary, and a platform-neutral renderer/host integration point
  before a Spaces `.cui` source can be authored safely.
