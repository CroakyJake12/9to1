# Boards donor parity manifest

Status: **inventory in progress; release acceptance is not satisfied**.

Primary donor: AppFlowy, selected revision `804d7898ac0becabf73e45527baf5d5c573cd6bb`, recorded in [DONOR-PROVENANCE.md](Source/DONOR-PROVENANCE.md). The donor must be evaluated at this revision. This manifest separates source presence from integration and runtime proof. No donor source was changed.

The donor-parity contract requires every applicable user-visible feature and non-proprietary block to be classified as Preserve, Replace with a 9to1-native equivalent, or Not applicable. The table below is the current known-feature inventory; it is deliberately marked incomplete until the pinned donor's complete public UI and block catalogue have been audited.

| Donor feature surface | Classification | Current evidence | Remaining acceptance |
| --- | --- | --- | --- |
| Board groups and cards: create, rename, remove, reorder, move cards within/between groups | Preserve | AppFlowy proof harness and neutral command/HUI contracts are documented in [README.md](README.md) and [EVIDENCE.md](EVIDENCE.md). Existing Boards contract tests cover related stable-ID operations. | Re-run tests in the current checkout; prove the production Boards surface uses the same persisted domain operations. |
| Board/card hierarchy and attachments | Replace with 9to1-native equivalent | Neutral Board contract records hierarchy and attachment metadata; content-addressed attachment support is described in the prior evidence ledger. | Prove parity for all donor interactions and attachment lifecycle on the shipping Boards surface. |
| Freeform card layout | Replace with 9to1-native equivalent | Boards freeform contract and shared Boards service support spatial objects with a separate Locked/Unlocked page layout mode. | Prove donor-flow compatibility, arbitrary absolute placement, document-space coordinates and persistence across transforms/restart. |
| Ink/drawing | Replace with 9to1-native equivalent | A pinned Rnote source tree exists. Existing Boards code has native canvas objects and ink-shaped contract coverage; the Rnote integration is explicitly unproven. | Integrate the shared Rnote-derived engine; prove pen/eraser/selection/history, both layout modes, and save/reopen. |
| AppFlowy proprietary AI, account, billing and paid-service integrations | Not applicable | Excluded by the authoritative Boards contract. Dulche/Home own the applicable 9to1 AI and permission behaviour. | Verify no proprietary donor service is invoked and that the 9to1 replacement/API boundary is complete. |
| Remaining AppFlowy workspace, page, database and block behaviours | Unclassified | Complete feature-by-feature audit of the pinned donor is pending. | Inventory every public user-facing feature and block at the selected revision, classify each item, then add deterministic parity tests or a scoped exception. |

## Known release gaps

- The current shared service stores Boards as a `NotesDocument` with `ProductKey=boards`; it does not yet establish the canonical Board/Page schema required by the specification (including `PageType`, nested `ParentPageID`, page-level permissions/revisions, and explicit shared artifact references).
- The current desktop Boards surface has a board picker, recoverable board/page trash, and persisted Edit/View plus Locked/Unlocked controls. Its hierarchy presentation is section/page grouping, not nested parent/child pages; its library is not the required recent-board visual card/grid landing with a Recent pages/components view.
- The generic Home Shared Productivity Engine, versioned cross-app object bundle, Data-owned artifacts, Write/Canvas canonical page references, Chat/ScopedChat and source ingestion, persistent AI bar, and Home permission broker are dependencies owned outside Boards and are not proven by this lane's code.
- Full action-catalogue/API parity, authorization broker enforcement, structured errors, revision conflict handling, collaboration merge semantics, platform capability negotiation, web/9to1-OS acceptance, virtualization/performance, accessibility and Home-absent bootstrap remain unverified or incomplete.
- Current focused shared Boards test execution is blocked before Boards compilation by nullable-reference build errors in `Haven.Application/Canvas/CanvasArtifactCodec.cs`; that file is outside this lane's ownership.

## Completion rule

Do not mark donor parity complete until the remaining AppFlowy inventory is exhaustive at the pinned revision, every row has a classification and acceptance evidence, and applicable donor behaviours are exercised through the shipping Boards UI and equivalent typed API actions. Updating the donor pin requires a fresh inventory and review; an unclassified feature is an open gap, not an implicit exclusion.
