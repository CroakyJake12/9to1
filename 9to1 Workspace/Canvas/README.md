# Canvas

The authoritative product contract is the Canvas section of the [9to1 Development Specification](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit). Canvas owns freeform drawing artifacts; Boards owns mixed-content workspaces.

`CanvasRnoteDocument` embeds the controlled Rnote 0.15 structured engine snapshot in the schema-versioned `.9to1c` artifact. New strokes retain stable Canvas IDs and original samples, pressure and tilt. It uses shared `CanvasArtifactSession` revision checks, operation replay and coherent stroke/engine-state undo. Genuine Rnote/Xournal++ import reports donor precision/page/identity limitations explicitly. SVG preview frames carry original document-space bounds and use a revision cache.

`CanvasFilesArtifactBridge` requires a trusted Home actor, current Files authority and an explicitly registered canonical folder. Immutable candidates publish through Files revision CAS. Reopen validates hash/size and owning identity; stale/failed saves preserve the prior revision. It has no private storage fallback. Public host actions additionally require Home's action broker.

`UI/CanvasWorkspace.cui` and `CanvasCuiWorkspace` provide native-host markup and semantic bindings. The host must register a real `CanvasSpatialSurface`, perform Home/backend readiness checks and route typed commands through Home. Source/parser availability does not prove mounted UI behavior.

The earlier Notes-backed `CanvasAppSurface` remains useful implementation; it does not define the current native format or replace `.9to1c`.

Run `cargo test --locked` and `cargo build --locked` in `rnote-poc`, then Canvas .NET tests with the built library available. See [rnote-poc/README.md](rnote-poc/README.md) for native requirements. Checks cover real donor drawing/history/rendering, Xopp import, stable native round trips, canonical history and durable Files storage. Full donor parity, complete tool shell, shared objects, imported per-entity IDs, collaboration, spatial culling and cross-platform packages remain release gates.
