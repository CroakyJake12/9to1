# BF-37 · Data creation and relative insertion · cycle 8

Flow pinned before coding, 30 October 2026, starting main `12a4c03527ee5f0c29dd7901b21f48e55e82a9a1`.
Owner authority: existing Goal contract revision 1; canonical Boards block-system,
Data-backed structured items and embedded ownership sections. This is one bounded
relative-database capability; freeform and positioned insertion edges remain queued.
The PC-only `.handoff/boards-first-v3/work/REQUIREMENTS.md` is unavailable here; this
tracked flow extends the existing cloud handoffs, without fabricating that ledger.

## Actual source and ownership

- Standalone `Program.StandaloneDataSource` and embedded
  `NativeCanvasBoardEmbeddedArtifactHost.Database` both call the actual
  `DataLocalRecordSessionFactory`, with their existing profile actor and owner lifetime.
- The factory binds `DataLocalWorkbookAuthority` and creates/persists a canonical
  `DataRecordSession.NewRecordDatabase` using `DataWorkbookRepository`. It owns rows,
  fields, view definitions, permissions and revisions. Boards stores only the stable
  StoreID/WorkbookID/TableID/ViewID reference. No copied records or second Data engine.
- `BoardsDatabaseBlocks` owns session/control lifetime. `BoardsViewModel.Database`
  currently creates the Data artifact before publishing/saving its Board reference;
  `_pendingDatabase` retains failed links and prevents close/history/destructive edits.
- Relative insertion currently disables every database row. Existing top-toolbar
  Tab View/Timeline on a selected database changes its view; that behavior must remain.
  A relative destination is explicitly a *new block*, including when its anchor is a
  database. It must not quietly change the anchor's view.

## Required user flow

1. In Edit mode, open the actual inline plus, hover/focus Above/Below/Replace, and
   choose a destination. The existing transient line/red overlay stays exact through
   type selection. Escape/outside click before choosing a type creates nothing.
2. Choose New database, Tab View or Timeline View. Check destination and Data host
   capability before invoking Data. Capture page, target/list identity, revision and
   editor lifetime. Refuse unsupported spatial edges and concurrent creation first.
3. Create one canonical Data artifact with the existing factory. Until it returns,
   the Board stays unchanged. A failure/cancellation preserves its original content.
   Revalidate origin after the await; never replace changed or retired content.
4. Publish the prepared reference at the selected destination in one canonical Board
   edit. Flow-list boundaries and whole positioned replacement reuse the checked
   insertion transaction. Preserve retained item IDs and exact Undo/Redo. Undo removes
   the Board reference only; it never deletes the independent Data artifact.
5. Save the Board link. A refused/failed link or save retains the created Data reference,
   reports recovery and blocks close/destructive history until resolved. Retrying a
   saved-in-memory link saves that same block, without a second artifact or history edit.
6. A top-toolbar recovery action retries the original destination only while its
   original content/identity still matches. An explicitly chosen new inline destination
   may link an unlinked artifact there; it cannot duplicate an already-published block.
   Changing page/view/selection alone must never silently redirect a late result.
7. Reopen the Board and Data store and verify stable reference identities and Data-owned
   rows. Keep one shared standalone/embedded implementation and the owner's one toolbar.

## Validation plan and limits

Compile each edit unit with the serial guarded SDK runner. Exercise authentic relative
transactions and the actual embedded CUI menu/RecordsUI, using a real local Data factory,
repository and authority with a test local actor in a private temporary profile. Label
explicit delayed/error/callback substitutions separately. Cover cancellation, stale
origins, changed targets, single creation on recovery, failed saves, exact Undo/Redo,
list boundaries, positioned refusal and persistence. Retain every attempt and failure.

All cloud checks are headless ordinary Layer1 partial/not reusable evidence. Desktop was
online at 11:11:30 UTC, but `native_queue_not_ready` / `native_contract_changed` denies
native admission. ROOT-LIVE recovery still reports the absent private projection.
Native PC/ARM, package delivery and full independent acceptance remain unexecuted/held.
Latest usage is approximately 8%, owner-reported at 09:10 UTC; no newer feed is inferred.

## Implemented and checked outcome

`BoardsViewModel.DatabaseInsertion.cs` prepares a Data reference, captures the original
destination and publishes through `ContractSessionAdapter.InsertRelativeAsync`. New
database, Tab View and Timeline View now accept flow Above/Below/Replace, projected
list-row/whole-list destinations and whole positioned replacement. The canonical edit
checks document readability and preserves the previous content on refusal. Existing
toolbar view selection still changes the selected database's view; relative choices
create a new database rather than changing their anchor.

The pending reference blocks duplicate creation and destructive history/close while
unresolved. Page/view changes, retired editors and edited targets do not receive late
insertions. Original-target retry compares retained content/parent identity and captures
a fresh action ticket; explicit inline recovery can choose another destination for the
same unlinked artifact. A published link retries its save without another insertion.
Selecting another named view of the same workbook is valid during save recovery; a
different workbook is refused. Hidden pane callbacks cannot change Board references.

The actual embedded writable presentation opens the authored inline destination menu
with raw pointer/key input, preserves its preview through search, creates a real Data
artifact, edits/saves a record through production RecordsUI and reopens both stores.
Six cases cover Above, Below, Replace, Escape, a Tabber pane and explicit recovery at a
new destination. Twenty-two owning transaction cases cover list/positioned boundaries,
failure/cancellation, stale origin, exact history, view selection and recovery identity.
No existing assertion or test was removed or weakened.

| Final Release validation | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Full Boards app suite (`cycle8-boards-final`) | 415 | 0 | 0 |
| Full Boards contract suite (`cycle8-contract-final`) | 113 | 0 | 0 |
| Standalone build (`cycle8-standalone-final`) | build passed | 0 errors | n/a |

The app suite now includes the real Infrastructure factory dependency. App tests and the
standalone build report the existing WebRtcVadSharp AnyCPU-to-x64 native-DLL warning.
All seven source/test hashes match the final manifest. Final elapsed runner times were
53.046 / 5.006 / 9.012 seconds; peak owned RSS 993,443,840 / 651,276,288 / 353,910,784
bytes. Every attempt kept source unchanged and stayed within the serial resource guards.

## Retained failures and evidence limits

All **14 attempts**, source/resource receipts, exact commands, logs, case names and
failures are retained in `boards-database-insertion-cycle8-2026-10-30.evidence.json` and
the original `/workspace/scratch/9to1-overnight-20261030/cycle8-*` directories.

- Baseline executable: 0/3, all three disabled relative-database routes reproduced.
- Three test compile attempts failed: CS0649 for an unassigned test hook field, and
  CS8602 twice for a captured nullable UI control. No stale no-build pass was used.
- First executable UI attempt: 23/28; five fixture failures from retaining a retired
  inline trigger after focus publication or a pre-projection seed page. Fresh mounted
  identities are now found before raw input; production behavior was not waived.
- Next UI attempt: 24/28; four incorrect cold-value assertions inspected `Records`
  identity mappings, which do not contain values. They now assert the exact reopened
  Data projection's Name cell. The failure captures remain retained, not relabelled.
- Review RED: 28/30; selecting a new view stranded a pending save, and a hidden pane
  could mutate its reference. Both were repaired. Focused successor: 37/37; final
  real menu/recovery successor: 6/6, followed by the full checks above.

Three final and three retained-failure PNGs under
`evidence/boards-database-insertion-2026-10-30/` are actual CUI/Skia renders, visually
inspected at 1180×900 / 96 DPI. Boards explicitly uses the Glow/Dark test fixture;
the Data host port uses its default theme context. Horizontal scrolling after reaching
Save record is retained in the images. These are not a theme matrix or native captures.

The new tests use the actual local Data factory, repository, authority, session and
RecordsUI with a supplied local test actor/host in a temporary profile. Failure cases
explicitly inject pre-create errors, delayed host return/transitions or a refused external
Board save callback. Existing Tabber draft tests retain their previously documented
substituted ports. Neither full desktop composition nor native OS execution was tested.

Atomicity here covers the Board insertion/history edit, not a distributed transaction
across the Data and Board stores. A created artifact remains in Data if linking fails.
Pending-link UI recovery is retained in the live editor; crash/restart recovery of an
unlinked pending operation is not added or claimed. There is no new existing-artifact
picker, Data permission model, record engine or portability/owned-artifact protocol.

BF-37 remains **CHECKED PARTIAL**: relative Freeform box, positioned Above/Below and
interior positioned-list replacement are still unavailable. Next inspect and pin the
actual Freeform box creation/placement flow before choosing its dependency-ready unit.
Broader Tabber CUI migration, lossless Design mappings, legacy toolbar conversion and
two-step attachment remain open. Native queue admission at 11:34:10 UTC is still
`native_queue_not_ready` / `native_contract_changed` despite an online bridge. No native
PC/ARM or independent acceptance claim; package HELD and ARM PC-owned.
