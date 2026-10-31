# Owner stop checkpoint — 31 October 2026

At the owner's 13% remaining instruction, all Spaces source work stopped. Root owns commit/push
of the current verified unit. **306/306 Spaces tests pass** in the guarded 22:22 UTC full run.
Actual ordinary Desktop build/run and native route evidence remain pending; do not call this fully
implemented or accepted. Headless capture evidence is retained separately.

Known actual gaps: the 390px headless layout gives the main content only about 40px because both
sidebars stay expanded; bare `9to1://spaces` currently leaves the generic summary for default Chat
until the Chat rail button is selected. Neither is fixed in the current live unit. Built-in source
controls, selected-Space archive fallback persistence, sibling ordering, persisted pin/layout
navigation and effective inherited-policy display also have prepared follow-ups, not live fixes.

Every remaining patch/test is under `/workspace/scratch/spaces-20261031-prepared/`, with ordered
instructions in `APPLY.md` and exact file hashes in `SHA256.json`. Apply each only with a separate
compile/test feedback cycle and ordinary native retest; do not batch them blindly. The live
`apps/Web/BrowserCanonicalIcons.cs` alias is authorized and passed the actual image-resolver test.
All 41 acceptance requirements remain tracked in `requirements-2026-10-31.md`.

---

# Spaces lane checkpoint

Owner: dedicated Spaces worker in the 31 October parallel implementation session. Root owns all SDK
execution, native launch, commits and pushes. Work occurs only on shared main; other workers own Boards/CUI.

## Live source prepared

- Mounted CUI manager archive list and same-ID Restore, with revision checking in existing registry.
- Archived children can retain an archived parent; active children still cannot. Parent recovery is explicit.
- Local file references now appear in Sources. Their labels describe requested access, never claimed ingestion.
- Local and typed context references have exact-row removal controls; originals remain untouched.
- Typed sources show their owning app, recorded revision and automatic/explicit inclusion.
- Archive, AddFile and RemoveFile retain existing APIs and add expected-revision overloads.
- Local reference identity is case-sensitive off Windows; Windows retains case-insensitive matching.

New tests: `SpacesArchiveRecoveryTests`, `SpacesSourceManagementTests`; rendered CUI action tests in
`SpacesHomeCuiRenderingTests`. Focused and full SDK outcomes are owned by Root. The first Spaces command
stopped at missing Dev restore assets. After restore the CUI compiler produced the authored Spaces
artifacts, and managed compilation found one nullable ContextReferences enumeration. The null-handling and
known-populated test annotation fixes are live, and Spaces production now compiles. The test build
then exposed stale test-project restore assets missing existing CUI/Avalonia references; Root owns
the targeted restore and retry. The full run completed: 294 passed, 10 failed, including all 13 new
archive/source/rendered-action cases passing. The ten existing fixture failures were traced to stale
assumptions; approved corrections are live with exact rationale in `test-corrections-2026-10-31.md`.
The headless screenshots also exposed a real 390px fixed-sidebar width defect; this is open and its
flow is pinned. These results are not full native or independent acceptance.

## Prepared next units (not live at this checkpoint)

Apply only after Root releases the frozen SDK snapshot, one coherent unit with tests at a time:

1. `/tmp/spaces-builtin-sources.py`: distinguish source management from protected built-in structure.
2. `/tmp/spaces-ordering.py`, `/tmp/SpacesOrderingTests.cs`, `/tmp/spaces-order-render-test.txt`: actual
   sibling Move up/down using the existing canonical operation; expected revision and reviewed order;
   fix same-parent source compaction that otherwise changes unaffected siblings.
3. `/tmp/spaces-navigation.py`, `/tmp/SpaceRegistry.Navigation.cs`,
   `/tmp/SpacesHomeController.Navigation.cs`, `/tmp/SpacesNavigationTests.cs`: same-profile persisted
   rail/dropdown and pin ordering, discoverable unpinned Spaces, safe archived/missing pin behavior.

Copies of all pending scripts/tests are also retained in `/workspace/scratch/spaces-20261031-prepared/`.
The archive-selection restart correction is prepared in `/tmp/spaces-archive-selection.py`.

Prepared script transformations were exercised on isolated source copies and the resulting CUI XML
parsed successfully. This is only script/XML validation; no pending unit has compiled or run.

The exact flow pins are in `implementation-2026-10-31.md`; all 41 acceptance IDs remain in
`requirements-2026-10-31.md`. The authoritative acceptance ledger is unchanged. No 100% implementation,
native screenshot, running-profile update or independent acceptance claim has been made.

## Current freeze after test correction unit

Reviewed existing-test corrections and explicit model noninheritance counterpart are live. Shared
`apps/Web/BrowserCanonicalIcons.cs` has the parent-authorized `manage-spaces` alias to the existing
canonical settings glyph, with `SpacesManagementIconTests` checking real resolved image geometry
and scoped pen against the unknown placeholder. Parent owns next compile/test/native milestone.
No remaining feature scripts have been applied.

Responsive follow-up is prepared in `/tmp/spaces-responsive.py`,
`/tmp/SpacesHomeCuiBindings.Responsive.cs`, `/tmp/spaces-responsive-render-test.txt`, also copied
under `/workspace/scratch/spaces-20261031-prepared/`. It depends on the navigation script and
keeps temporary narrow-sidebar state out of profile persistence. Pending compile/native evidence.

Parent also allocated the minimal `SpacePolicyResolver.cs` extraction for the later effective-policy
projection unit. Its script/tests are in the prepared directory; see `APPLY.md` there for ordered
application and exact additional test copies. The effective source view uses canonical inheritance
against the exact displayed snapshot, with separate read-only inherited rows and recorded-status
labels. It remains scratch-only, after navigation and compact presentation.

## First full Spaces verification milestone

Parent's guarded `spaces-corrected-full.log` completed at 22:22 UTC: **306 passed, zero failed,
zero skipped**, 13-second test duration. TRX is
`/workspace/scratch/boards-sprint-20261031/spaces-corrected-full-trx/_7da2d9b32d40_2026-10-31_22_22_41_net10.0.trx`.
This covers initial archive/source production, reviewed fixture corrections and the canonical icon
alias. Ten headless captures are under `spaces-captures-verified/`, including actual scrolled panes.
It does not close the known 390px content-width defect or establish the native desktop route.
Source remains frozen for the parent's first actual Desktop build/run milestone. All pending feature
units remain only in the prepared directory.

Pending navigation review additionally prepared physical-store regressions for original admission
revocation at publication (no bytes changed) and defensive pin-array capture before awaited admission.
`SpacesNavigationTests.cs` in scratch includes these; no production authorization was widened.
Pending ordering now also aligns its three equal-position tie-breakers with the existing GetAll/UI
name order, with an archive/reorder/restore regression (Zulu/Middle/Alpha). This remains uncompiled
until its dedicated release.
