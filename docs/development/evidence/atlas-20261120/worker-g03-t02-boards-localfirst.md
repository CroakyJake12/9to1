# G03-T02 — Boards local-first authoring verification

Repo: /home/jacob/Documents/GitHub/9to1 (read-only investigation; no builds/tests run).
Paths below are relative to `9to1 Workspace/` unless noted.

## 0. Note on task-named test files

All five named test files exist under `shared/tests/Haven.Desktop.Tests/` and were read in full:
`BoardsCreateDestinationRoutingTests.cs` (58 lines), `UnifiedBoardsInitialRouteTests.cs` (142),
`FilesNativeBoardDocumentCreateTests.cs` (525), `NativeHomeFilesBoardCreationIntegrationTests.cs` (560),
`BoardsRecoveryAcceptanceTests.cs` (235). Two other task-adjacent files referenced by the owner
(`LinuxHomeOwnershipTests.cs`, `FilesNativeBrowserServiceLinuxHomeTests.cs`) do **not** exist in this
working tree (verified with find/glob/grep over the whole repo).

## 1. Actual create flow and entry points

There are three authoring surfaces, and one shared Home/Files operation behind two of them.

### 1a. Boards tab inside the unified desktop (entry: `9to1://boards`, tab address authority `boards`)
- `Boards/app/BoardsCreationMenu.cs:23` — sidebar “+ New” menu item **“Board”** dispatches command `NewBoard`.
- `Boards/app/BoardsViewModel.cs:728-731` — `NewBoard`:
  - if `CreateBoardAsync` callback set → local draft path (see 1c);
  - else if no `_homeBoardCreationHost` → `NewBoardRequested` event;
  - else → `CreateAndOpenHomeBoardAsync` (`BoardsViewModel.cs:985-1015`): builds a validated blank draft
    (`CreateNewBoardDraftBytes`, `BoardsViewModel.cs:1018-1027`, `HavenBoardDocumentCodec.CreateNew()`) and
    calls `IBoardsHomeBoardCreationHost.CreateAndOpenAsync`.
- Host implementation: `shared/src/Haven.Desktop/Services/BoardsBrowseAppContentFactory.cs:70-86`
  (`CreateOriginalHomeBoardCreationHost`) → `App.UnifiedWorkspaceHost.CreateAndOpenBoardAsync`
  (`App.UnifiedWorkspaceHost.cs:818-872`). Routing there:
  1. Admission (`:822-828`): the request must come from the current standard (non-private) tab whose address
     is the Boards native app or the Home Boards library, and `origin.ProfileId == _actor?.ProfileId`.
     `_actor` is the **local Home profile** (OS-derived; see §1e) — not a cloud account.
  2. `BoardsWorkspaceFolders.ResolveAppDirectoryAsync(_services, "boards", …)` (`:861`) — resolves the
     workspace Boards folder, **auto-provisioning `~/Documents/9to1` on demand** if Files was never set up
     (`Services/BoardsWorkspaceFolders.cs:8-64` — doc comment: “no connect step, no account, no gate”;
     calls `ConfigureDefaultWorkspaceAsync` at `:48`).
  3. If provisioning produced no folder → `BoardsCreateRouting.FallsBackToRecoverableDraft` →
     `CreateAndOpenLocalBoardAsync` (`:862-867, 737-754`): the board is **still created**, as a recoverable
     draft, and the result message states the real location (`:751-753`). Creation is never gated on Files setup.
  4. Otherwise destination selection: `CreateAndOpenBoardCoreAsync` (`:880-1001`) presents a Files folder
     picker page **hosted inside the Boards window** (`state.Page = …CreateBoardFolderPickerPage`, `:894`,
     `TransientPresentation.PresentAsync`, `:903-908`); the owner selects a folder, names the board, clicks
     Create; `NativeHomeFilesLibrarySource.CreateInSelectedFolderAsync` (`Services/NativeHomeFilesLibrarySource.cs:293-340`)
     calls the Files owner `BoardDocumentCreateOwner.CreateBoardDocumentWithinSourceAsync` with a Home
     **permission review** callback. The review is a local approval window
     (`App.UnifiedWorkspaceHost.cs:1041-1043` → `PresentHomeLibraryReviewAsync` →
     `HomeNativeApprovalWindowOwner.OpenAndObserveDecisionOriginalAsync`, `:1219+`), decided through
     `HomePermissionTrustService` and audited into the local Home state file (`FILES_BOARD_CREATED` record;
     asserted in `FilesNativeBoardDocumentCreateTests.cs:81`). Post-commit failures are settled by typed
     outcomes (`HomeAuditPending`, `CreatedOpenFailed`, `CommittedWithUncertainAudit`, …) so a create is never
     repeated (`BoardsBrowseAppContentFactory.cs:260-519`).
  The routing rule itself is pinned by `Services/BoardsCreateRouting.cs:12-24`.

### 1b. Home entry (Home Library → Boards)
- `App.UnifiedWorkspaceHost.cs:1211-1216`: for the library page `browse://home/library/boards`, the page binds
  `SetBoardCreationHost(CreateOriginalHomeBoardCreationHost(CreateAndOpenBoardAsync, …), BoardsViewModel.CreateNewBoardDraftBytes)`.
- `Views/Pages/Home/HomeLibraryDestinationPage.cs:171-183, 225-229, 465-481`: “CreateHomeLibraryBoard” action
  (and retry/inspect/open-recorded recovery actions) → same `IBoardsHomeBoardCreationHost` as 1a.

### 1c. Standalone Boards app (no Home window)
- `Boards/app/Program.cs:130-141`: creates its own local profile actor (`HomeLocalProfileIdentity` +
  `OperatingSystemPrincipalSource` over `{DataDirectory}/Home/state.json`) and `BoardsLocalDocuments(
  actor.ProfileId, {DataDirectory}/Boards, _ => BoardsWorkspaceStorage.DefaultBoardsDirectory(...))`.
- `Program.cs:213, 813, 817-830`: `CreateBoardAsync` → `documents.CreateAsync()` → open → activate. No
  account, no Files service, no model. `BoardsWorkspaceStorage.cs:11-26` resolves
  `~/Documents/9to1/Boards` (provision-on-demand) or returns null → drafts-only.
- Same landing-page wiring inside the desktop for *local-document* sessions:
  `BoardsWritableRichDocumentBrowseAppContentSession.cs:174-193` (splash → `BoardsWritablePresentation.OpenLandingAsync`)
  and `Boards/app/BoardsWritablePresentation.cs:263-289, 357-360`: `vm.CreateBoardAsync = open(await documents.CreateAsync(…))`.

### 1d. Files entry
- The Files CUI action catalog declares `CreateBoardDocument` / `SaveBoardDocument` descriptors
  (`Files/CUI/Services/FilesServiceActionCatalog.cs:20-21`) and `FilesNativeBrowserActionPolicySource.cs:19-21`
  maps the `files.board.create` action to them — i.e. the policy surface exists in Files, but the only
  production **caller** of `CreateBoardDocumentWithinSourceAsync` found is `NativeHomeFilesLibrarySource`
  (used by the Boards-window picker and Home library). A standalone “New board” command in the Files app’s
  own UI: **not found / unknown** (it may exist in the large vendored Files workspace UI; I did not find it).

### 1e. Does any entry REQUIRE an account, Files connection, or model?
- **Account: No.** The actor is the local OS profile (`Home/Source/Home/Core/HomeLocalProfileIdentity.cs:16-33` —
  Windows SID / `unix-euid`, digest stored as DeviceLocal record `home.local-profile`; comment at `:37`:
  “Local OS profile identity is independent of CAKE AccountID and OrganisationID”). Stronger: the Notes-store
  Boards route **rejects** account-bound actors — `Services/NativeBoardsDesktopRoute.cs:156-158`
  (`actor.AccountId is not null … throw … "This Boards tab is not the actual local Home profile"`).
- **Files connection: No.** The workspace is auto-provisioned (`BoardsWorkspaceFolders.cs` doc + code); when
  even that fails, creation falls back to a recoverable draft instead of blocking
  (`App.UnifiedWorkspaceHost.cs:862-867`, `BoardsLocalDocuments.PlaceInWorkspaceAsync` swallow-and-report,
  `Boards/app/BoardsLocalDocuments.cs:151-178`).
- **Model: No.** The only model touchpoint is optional section-icon generation
  (`BoardsBrowseAppContentFactory.cs:135-141, 172-177`: `modelClient is null || modelSelection is null ? null : …`).
  Create/save/reopen perform no model calls.

## 2. Where boards are persisted; crash behaviour

Four distinct stores (by surface):

1. **Private draft/recovery catalogue (all rich-Board surfaces):**
   `{DataDirectory}/Boards/profiles/{sha256(profileId)}/drafts/{BoardId:N}.9to1board` plus `recent.json`,
   `destinations.json` (`Boards/app/BoardsLocalDocuments.cs:96-121`; `DataDirectory` = `~/.config/Haven` on
   Linux, `HAVEN_DATA_DIR` override — `Haven.Infrastructure/Lifecycle/AppPaths.cs:19-34`). Writes go through
   `JsonFileHavenBoardStore` (“local-first physical document store … written through a same-directory
   validated temporary file and retained previous-version backup”, `Boards/contract/JsonFileHavenBoardStore.cs:36-59`;
   envelope `9to1.board` schema v5). Writes are serialized under `LocalBoardWriteLease`
   (`Boards/contract/LocalBoardWriteLease.cs`).
2. **Files-visible workspace copy:** `~/Documents/9to1/Boards/<Title>.9to1board` (or `…/9to1/Apps/Boards/`
   when the root holds foreign content) — `PlaceInWorkspaceAsync` (`BoardsLocalDocuments.cs:147-210`,
   collision-safe “Roadmap (2)” naming) and `LocalWorkspaceFolders.Provision`
   (`Haven.Application/LocalWorkspaceFolders.cs:159-202`, inspect-never-delete, xdg-documents aware, app-data
   fallback with warning). Draft remains the recovery copy; destination tracked in `destinations.json`.
3. **Files native store (Home/Files-reviewed create/save):** rows+revisions in `{workspace}/.9to1-files/drive.json`
   (`Files/NativeHost/NativeFilesWorkspaceService.cs:103,151,204` — `DurableDriveProvider`), immutable content
   leaves named `.9to1-board-*.content` in the profile-bound boards directory (asserted directly on disk in
   `NativeHomeFilesBoardCreationIntegrationTests.cs:95-107,237,256`), displayed as `Name.9to1board`
   (`FilesNativeBoardDocumentCreateTests.cs:222`). Every save creates a new immutable revision
   (`FilesServiceActionCatalog.cs:20`), reviewed through Home and audited to `{DataDirectory}/Home/state.json`
   (`FileHomeCoreStateStore`, “Crash-safe, revision-checked local Home state storage”,
   `Home/Source/Home/Core/FileHomeCoreStateStore.cs:9-13`).
4. **Notes-store notebook route** (legacy `9to1://boards/notebooks/{id}` tab, third branch of
   `BoardsBrowseAppContentFactory.CreateSession:219-238`): `BoardsWorkspaceService`
   (`Haven.Application/Boards/BoardsWorkspaceService.cs` — `CreateNotebookAsync:127-139`, `SaveAsync:219-223`)
   over `NotesRepository` (`Haven.Infrastructure/Persistence/FileSystem/NotesRepository.cs`):
   `{DataDirectory}/Notes/Documents/{id}/current.haven-notes.json` + `backup.haven-notes.json` + `Versions/`
   (`:47-60, 992-1044, 1095-1108`) and a write-ahead recovery file `current.recovery.json` (`:549`) with
   pending-intent/publication journals and corrupt-file recovery from the last valid copy (`:146-246, 396-442`).

No SQLite board table exists: `Haven.Infrastructure/Persistence/SQLite/` contains no Boards repository
(grep “board” there only matches dashboard code). Board *Database* blocks embed in the board document /
Data workbook services, not a boards table.

**Crash/autosave (rich sessions):**
- `RichBoardSession` (`Boards/contract/RichBoardSession.cs`): 750 ms debounced autosave after every mutation
  (`:26-27, 207, 655-675`); saves write the **recovery (draft) head first**, then the destination
  (`SaveCoreLockedAsync:688-760`), with hash-checked conflict detection — a stale editor’s version is kept
  separately under `…/conflicts/{id}-{guid}.9to1board` (`:746-755`); `DisposeAsync` flushes pending edits so
  “close/switch never loses the last keystrokes” (`:826-855`).
- App close: `BoardsCloseSaveOwner.SaveBeforeCloseAsync` performs the real session save and only an
  acknowledged save permits cleanup (`Boards/app/BoardsCloseSaveOwner.cs:17-39`).
- Process crash: on next start, every `*.9to1board` in the drafts dir is re-recorded into Recent — “Recovery
  does not depend on an index write having completed before interruption” (`BoardsLocalDocuments.cs:115-117`).
  Files-committed drive.json revisions and Home state are already durable (atomic store writes).
- Known bounded loss window: unsaved edits newer than the last autosave tick (≤750 ms debounce) — no
  journaling of raw keystrokes was found.
- Files-create settlement state (`HomeAuditPending` receipt etc.) is retained **in-memory per window**
  (`App.UnifiedWorkspaceHost.cs:801-817`); across a process restart the committed file remains visible in
  Files/Home library, but I found no mechanism that re-adopts the pending settlement after a crash
  (unknown / gap).

## 3. What the named tests prove — and do not

**BoardsCreateDestinationRoutingTests.cs** — pins the owner’s routing rule as pure functions: destination
selection is offered for `Create` iff a Files workspace service is reachable; when provisioning yields no
boards folder the recoverable-draft path (not a gate) applies; resuming a retained create never re-asks or
creates a second board. NOT covered: any I/O, UI, or the wiring itself (it calls `BoardsCreateRouting`
directly).

**UnifiedBoardsInitialRouteTests.cs** — the shared process-entry boundary: compiled product declarations
(boards/canvas/present), route strings (`9to1://boards`), refusal of legacy direct `.9to1board` command-line
open (`:106-113`), one-winner concurrent capture. Header states it “launch[es] no native app and attest[s] no
Home grants”. NOT covered: anything about create/save/persistence.

**FilesNativeBoardDocumentCreateTests.cs** (Linux-only) — real `FilesNativeBrowserService` + real
`HomePermissionTrustService` + real `FileHomeCoreStateStore` + real drive on temp dirs. Proves: reviewed
create publishes the exact revision, reopens byte-identical, audits `FILES_BOARD_CREATED`; stale actor/
retired source/stale folder refuse **with zero disk effect**; declined review creates nothing; lost audit
acknowledgement settles by exact-attempt retry producing exactly one committed file; owner close joins
in-flight creates. NOT covered: the Boards UI (picker page, view model), non-Linux platforms (all skipped),
or crash of the *whole process* (owner drain/join is cooperative, not kill).

**NativeHomeFilesBoardCreationIntegrationTests.cs** (Linux-only) — the actual `NativeHomeFilesLibrarySource`
over the registered Linux Files owner: decline → no board; accept → create, prepared writable address,
lease open, **save through Home-reviewed write producing a new revision and a second CAS leaf** while the old
leaf is untouched (`:94-108`), reopen shows the saved title; audit-ack-loss reuses the same receipt
(without a second create); READ denial keeps the committed file and a fresh authorized open succeeds;
missing physical leaf stays an IO failure. NOT covered: destination selection UX, draft fallback (the host’s
`FallsBackToRecoverableDraft` decision), recovery after full app restart.

**BoardsRecoveryAcceptanceTests.cs** — `BoardsWorkspaceService` semantics (create/rename/pin/sections/pages/
blocks/canvas objects/live components/attachments; 600-block + 300-object board) survive a “fresh reload”.
BUT the repository is a **fake in-memory `CloningNotesRepository`** (`:139-189`) and the attachment store a
test double — it proves service/document-model round-trip fidelity, **not** on-disk persistence, not
`NotesRepository`’s real recovery files, and not the desktop create/save flow.

**Extra relevant (not in the named list):** `Boards/app.tests/BoardsLocalDraftTests.cs` covers the local
draft path with real temp dirs: visible workspace file, no-overwrite naming, blocked folder keeps a
recoverable draft “and says why”, edits save through to the visible file, path-escape refusal, draft-only
durability + reopen, Save As identity retention, stale-tab conflict copies.

**Gaps (all surfaces):** no UI-level end-to-end test (menu click → file on disk) — Home/Files routing is
proven at the service boundary only; no kill -9 / power-loss test; `LinuxFact` tests skip on Windows/macOS,
so the Home/Files reviewed-create path is untested there; no test of the draft → workspace promotion when
provisioning succeeds only on a later attempt; standalone `Boards/app/Program.cs` wiring itself untested
(app.tests cover the view-model layer).

## 4. Verdict

**CONFIRMED (from source), with the caveats below.**

- Create → edit → autosave/save → reopen with **no cloud account, no Files connection step, and no model**
  is implemented deliberately: local OS-profile actor; auto-provisioned `~/Documents/9to1/Boards`; a private
  draft copy under `~/.config/Haven/Boards/...` that always exists first; recoverable-draft fallback so
  creation “is never gated”; model features optional and null-safe. The Notes route even rejects
  account-bound actors and states “Account sign-in does not sync this library”
  (`NativeBoardsDesktopRoute.cs:128,156`).
- The strongest executed evidence is Linux-only service-level (the four Linux tests +
  `BoardsLocalDraftTests`); the full UI path is verified by construction, not by an end-to-end test.
- “Offline”: none of the create/save/reopen code paths touch the network; the Home review is a local
  approval window over a local trust store. I did not run the app, so runtime offline behaviour is
  inferred from source.

## 5. Suggested follow-ups

1. Add an end-to-end (headless) test: Boards tab “+New → Board” with a temp `HOME`/`HAVEN_DATA_DIR`, assert
   draft + `~/Documents/9to1/Boards/*.9to1board` + reopen, no network (socket-blocked fixture).
2. Add a process-kill variant of `FilesNativeBoardDocumentCreateTests` (spawn child, kill after commit,
   assert the committed file reopens and exactly one leaf exists).
3. Cover `NativeHomeFilesLibrarySource.CreateInSelectedFolderAsync` folder-selected-to-disk mapping with a
   real picker selection (currently only the pure routing functions and the source API are tested).
4. Decide/document cross-restart behaviour of retained `HomeAuditPending` creations (currently in-memory only).
5. Either create `LinuxHomeOwnershipTests.cs`/`FilesNativeBrowserServiceLinuxHomeTests.cs` (named in the task
   but absent) or update the task ledger — their intended coverage is unclear.
