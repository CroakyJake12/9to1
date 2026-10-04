# CakeOS Canvas document envelope proof

This slice proves a **Cake-owned**, multi-board persistence boundary around the Rnote engine integration. It does not replace Cake's semantic Canvas model with `.rnote`.

## Canonical ownership

The document envelope owns:

- document id, title, timestamps and active board;
- stable board ids and board titles;
- HUI viewport state;
- canonical ink metadata and samples, including pressure, tilt and timestamps;
- structured Canvas objects and ghost/study layers;
- generative UI and other Cake extension state.

Each board may also contain an opaque Rnote `engine.rnote` snapshot. That snapshot is an engine payload for exact current engine state and interchange; it is **not** the document identity, board model, accessibility model, structured-object model, or sole source of Cake ink metadata.

This matches the migration requirement that information Rnote 0.14.2 does not understand—especially tilt, structured objects/connectors, study layers, generative state and board identity—must remain under Cake ownership.

## v1 package layout

The proof uses a ZIP container with fixed, non-extracted entry paths:

```text
manifest.json
boards/<board-id>/board.json
boards/<board-id>/engine.rnote       # optional opaque snapshot
```

`manifest.json` records schema/version, document identity, active board, canonical board paths and optional Rnote engine descriptors. Engine descriptors include the upstream release, media type and SHA-256 digest.

`board.json` contains the canonical Cake board state. Structured objects and ghost layers are intentionally represented as lossless JSON during this staged migration so the existing Notes Canvas data can be retained before every object type has a new typed CakeOS representation.

## Safety properties in the proof

The codec:

- rejects unsupported schema versions;
- requires one to 128 uniquely identified boards;
- requires the active board to exist;
- validates finite viewport and ink values;
- preserves pressure, X/Y tilt and timestamps;
- rejects duplicate or unsafe archive entry paths;
- uses fixed paths derived from board GUIDs instead of trusting arbitrary engine paths;
- limits manifest, board JSON, individual engine payload and total bytes read;
- verifies each engine snapshot with SHA-256 before returning it to the caller;
- never extracts archive entries to the filesystem.

Atomic filesystem replacement, recovery journals and OS package/MIME registration are deliberately later gates. This codec is an in-memory/package-format proof, not a claim that the production persistence service is finished.

## Relationship to the current CakeAI/Haven donor

The donor Canvas stores boards as Notes pages. Each board carries viewport state, structured objects, ink strokes and ghost layers; ink points include pressure, tilt and timestamps. The v1 envelope is shaped to retain those semantics during migration instead of flattening a document into Rnote-only bytes.

## Proof runner

`../document-envelope-proof` builds against this library and checks:

1. two boards survive a write/read round trip with stable ids, titles and active-board identity;
2. pressure, tilt and timestamps survive unchanged;
3. structured objects, ghost layers and generative extension JSON survive unchanged;
4. distinct per-board engine snapshots survive unchanged;
5. the ZIP contains only the expected fixed v1 paths;
6. tampering with an engine snapshot is rejected by digest validation;
7. duplicate board ids are rejected.

The fixture engine bytes are deliberately opaque test payloads; validity as `.rnote` is already proven separately by the native/managed Rnote round-trip CI. The envelope codec does not parse or reinterpret Rnote internals.
