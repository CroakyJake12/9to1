# 9to1 Cards — canonical domain contribution

**Status: source-only implementation checkpoint; not a finished Cards application.**

This isolated module is the first Cards-owned code, built against the published
`integration/astra-consolidated-staging-20261005` branch. It implements a
revision-aware, non-UI card-set domain so later CUI, Files, Spaces, Revision
Bank, Study and typed-action integrations can share **one set of identities and
mutation rules**, rather than each inventing a private flashcard model.

## Implemented in this checkpoint

- Stable SetID, ArtifactID and CardID, ordered membership and recoverable
  card tombstones.
- Structured front/back document payloads kept intact for the existing Shared
  Productivity Engine; no Markdown flattening, mock graph editor or duplicate
  rendering engine.
- Atomic bulk creation, per-side editing, duplication, ordering, grouping
  assignment, non-destructive deletion and restore.
- Optimistic revision checks (domain level) and View-mode write denial.
- Search against a *derived* text index that does not replace rich content.
- Principal-specific review record **construction**, outside shared deck content.
- JSON schema validation, explicit future-schema rejection and preservation of
  unknown extension fields across a JSON round trip.
- Atomic multi-card front/back edits with per-card and set revision checks.
- Visible-position reordering that keeps recoverable tombstone positions stable.
- Set title editing and revision-bound, filtered page queries (maximum 100
  cards per page), providing a usable contract for a future virtualised UI.
- Principal-scoped review queue projections with red/amber/green difficult-first
  and difficult-only modes; mixed-owner data is ignored by the projection.
- Versioned import validation that explicitly rejects malformed/unversioned JSON.
- Canonical Files asset-reference manifest (stable AssetID, Files reference,
  MIME type, content hash and byte length), with revision-checked atomic
  registration, duplicate-ID rejection and lossless metadata round trips.
  This registers *metadata only*: it does not upload an asset, validate an
  external Files object or grant file permissions.
- Focused xUnit regression cases covering these domain operations.

## Typed mutation gateway (source-only)

The Cards-owned `CardMutationGateway` now routes typed Rename, BulkEdit,
Move, Delete and Restore actions through three **injected canonical-owner
ports**: trusted caller evidence, live Home permission admission, and the
canonical revision-aware commit store. Its response represents an **actual
commit receipt**, not an optimistic UI mutation. The interface requires an
atomic commit-boundary permission recheck and operation-ID replay deduplication;
the gateway itself provides neither a competing file store nor local grants.

An in-memory fake store exists only inside the unit tests. It exercises
revocation between preview and commit, optimistic concurrency, exact
operation-ID/payload reuse, deletion conflict and duplicate delivery. These
tests do not prove a real Home or Files provider has implemented the ports.

## Home shared-engine adapter (dependent verification)

`HomeIntegration` references the actual `HavenOS.Home` assembly and
`IHomeProductivityEngine`. Rich Home object bundles remain structured; an
unsupported object or external format refuses compatibility instead of
silently flattening content.

The published integration branch currently causes a compiler collision in
`Haven.Application.IWorkspaceOriginalTraversalService` during the
Home-dependent build. That is outside this Cards-owned lane; the original
failure evidence is retained in GitHub Actions. It requires receiving-source
reconciliation by Astra's shared Application owner. No stub, copied assembly
or suppressed error will be substituted to get a misleading green build.

## Integration required before any app-readiness claim

These **remain open** and are intentionally not simulated:

1. Home/Files authority, owner and permission gates at every typed action,
   including inherited approval for destructive edits.
2. A durable, atomic, versioned CAS store with operationId replay deduplication,
   interrupted-save recovery and source-preserving import/export.
   Files-owned asset existence/hash/permission checking and attachment mutation
   through the real Shared Productivity Engine are likewise outstanding.
3. Real Shared Productivity Engine formatting, image/graph/ink rendering and
   editing, font/background colour, history and undo/redo.
4. Separate private review storage and authorised, attributable Study evidence
   publication for valid TopicIDs; no fabricated topic links.
5. First-party CUI Cards application (desktop/web/9to1-OS), navigation,
   accessibility, search/sidebar, virtualisation and persisted preferences.
6. Canonical typed automation actions, Files/open-with, installer, contextual
   AI bar and Spaces/Revision Bank interoperation.
7. Genuine Debug/Release compilation, owning tests, installed GUI workflows,
   multi-user permission/conflict tests and full specification acceptance.

The pure operations return successor snapshots; **they do not perform persistence
or permission checks**. The review queries accept a principal ID only from a
trusted caller; they cannot authenticate a user and do not persist ratings.
Review-owner and per-user access checks must be enforced by canonical services.
A filtered page carries its source revision so a UI can reject stale next-page
requests. A 100-entry page cap is an internal provisional bound, not a product
performance acceptance result. Callers must obtain canonical authority *before* calling
them, then atomically commit against the expected revision. Do not treat a
returned snapshot as saved. Review records must not be written into shared
CardSet payloads. Downstream integration must not copy this engine into another
independent application runtime.

## First-party CUI scene — new source slice

`Cards/CUI` now embeds an authored `.cui` scene and a scene-binding
controller using the **existing** `CakeOS.Cui.Runtime` binding and action
interfaces. It includes a subject/topic sidebar, search, focused vertical
card navigation, explicit Flip, View/Edit switching, an injected
Home/shared-productivity rich editor action, and red/amber/green personal
review actions. The controller **does not directly save cards or ratings**.
A Card write invokes `CardMutationGateway` and reloads the canonical
acknowledged revision; a review is shown as saved only after the personal
review owner confirms the write.

The scene's actual rich content renderer is **not yet embedded**; the
current content status explicitly reports that limitation rather than
displaying derived search text as if it were a rich editor. Group choices
currently live in session state: persistent horizontal navigation and
custom-group settings require the owner-backed preferences API. Actual
scroll geometry, shadowed neighbours, responsive/appearance checks,
accessibility and keyboard/touch smoke acceptance remain unverified.

New owning tests cover CUI parser acceptance, basic navigation/flip,
search, grouped sidebar selection, edit-mode protection, actual returned
commit receipts and personal review receipts using **test-only** owner
fakes. A separate CI job exercises the real CUI project and test graph;
it must pass before this CUI slice is labelled build-verified.

## Focused navigation and preference receipts — current contribution

The Cards-owned `CardNavigationSession` now centralises deterministic
selection, front/back flips, keyboard direction, grouped navigation,
query filtering and preservation of CardID when the canonical deck is
refreshed. It publishes a **bounded three-card focused window** (previous,
current, next), not a duplicate card store or rendered UI. Underlying
collections are still memory-backed; true large-deck storage paging and
host-side visual virtualisation are not yet verified.

Vertical navigation is the default; horizontal arrow navigation is
supported when that preference is chosen. A changed orientation preserves
the currently selected group and CardID when still present. Deleted or
filtered-out cards cannot remain selected.

The CUI presenter now delegates focus/group/search/flip/navigation state
to this shared Cards session, avoiding a second implementation of those
rules. The `ICardCuiPreferencesOwner` port requires an actual authorised
settings-owner receipt for durable navigation preference changes. If no
owner is supplied, orientation is explicitly session-only. A denied write
leaves the chosen preference unchanged. A real horizontal *visual* layout,
scroll/touch routing, preferences provider and installed platform rendering
remain outstanding.

The new core navigation tests can run without the blocked CUI runtime.
The full CUI action tests still require the repository's vendored Avalonia
dependency graph, which currently fails independently of Cards source.
Do not claim runtime/UI verification from a passing parser or core test.

## Local focused checks

From `9to1 Workspace` with .NET SDK 10 installed:

```sh
dotnet restore Cards/Tests/HavenOS.Cards.Tests.csproj
dotnet test Cards/Tests/HavenOS.Cards.Tests.csproj -c Debug
dotnet test Cards/Tests/HavenOS.Cards.Tests.csproj -c Release
dotnet test Cards/CUI/Tests/HavenOS.Cards.CUI.Tests.csproj -c Release
```

These commands have **not** been executed in the originating ChatGPT
environment because .NET SDK 10 is unavailable. No suite is claimed to pass.

The authoritative product contract is in the 9to1 Development Specification,
"Cards app" and "LE-10 — Cards canonical product contract". The published
integration branch may trail Astra's unpublished Cloud workspace, so source
merging requires an owner-side conflict/identity review.
