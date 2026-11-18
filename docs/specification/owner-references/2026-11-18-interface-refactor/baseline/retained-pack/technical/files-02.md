# Files and ten organisational folder views — Sharing and ACLs; Search, metadata and previews (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0621 · v2 lines 2972–2972

Normal hosted deletion MUST be soft/recoverable. Trash records MUST preserve stable item ID, original parent/location, deletion timestamp and restoration metadata. Permanent purge is a distinct high-risk operation.

### V2-B0622 · v2 lines 2975–2975

Deleting a Files container or reference MUST NOT silently cascade-permanently-delete separately owned embedded/project artifacts unless the owning app explicitly defines cascade semantics and Files presents the impact before execution.

### V2-B0623 · v2 lines 2978–2978

Sharing and ACLs

### V2-B0624 · v2 lines 2981–2981

Hosted files/folders MUST support first-class sharing using stable account/team/organisation principal IDs. Required ACL concepts include owner, direct grants, inherited grants and viewer/editor-style capability roles or their equivalent typed scopes.

### V2-B0625 · v2 lines 2984–2984

Folder permission inheritance MUST distinguish inherited from direct grants. Moving an item between differently permissioned folders MUST calculate/result in an explicit effective permission change and MUST NOT silently retain/remove direct grants contrary to the configured inheritance policy.

### V2-B0626 · v2 lines 2987–2987

Shared with Me MUST reference canonical objects owned elsewhere rather than cloning them into the recipient’s Drive. Revocation affects future access immediately subject to already-downloaded/offline-data retention policy.

### V2-B0627 · v2 lines 2990–2990

If public/link sharing is supported, link shares MUST use revocable ShareID/token state with explicit permission level and optional expiry/password where implemented. Raw file URLs MUST NOT themselves imply authority.

### V2-B0628 · v2 lines 2993–2993

Search, metadata and previews

### V2-B0629 · v2 lines 2996–2996

Files MUST support filename/folder search plus indexed metadata/content search where provider/file-type policy permits. Files publishes supported searchable entities to Home’s shared index; current permissions are rechecked when rendering/opening results.

### V2-B0630 · v2 lines 2999–2999

Common item metadata MUST include provider/location, type, size, relevant timestamps, owner/sharing, sync/offline state, current revision and hashes/integrity data where available. App-specific metadata MUST be namespaced/versioned.

### V2-B0631 · v2 lines 3002–3002

Files MUST provide preview/thumbnail contracts. Owning apps/providers MAY register read-only preview renderers for native artifacts. Preview MUST NOT mutate the source artifact or require launching a full app window unless no lightweight preview capability exists.

### V2-B0632 · v2 lines 3005–3005

Open routing, context actions and drag/drop

### V2-B0633 · v2 lines 3008–3008

Home’s app/file-handler registry determines default/open-with routing. Native 9to1 artifacts MUST expose enough type/version metadata that routing is not dependent solely on filename extension. Users MAY choose alternate compatible handlers where supported.

### V2-B0634 · v2 lines 3011–3011

Installed apps MAY contribute typed registered context actions based on item type/capability, such as Open in Write, Add to Board, Open in Pictures, Create Stack from folder or other ecosystem actions. Context actions MUST be declared through Home’s registry and MUST NOT inject arbitrary untrusted shell code into Files.

### V2-B0635 · v2 lines 3014–3014

Drag/drop and clipboard operations MUST preserve operation intent, source identity and provider capability. Cross-provider drag MUST make copy-vs-move semantics explicit. A cut/paste MUST NOT delete the source until destination commit succeeds.

### V2-B0636 · v2 lines 3017–3017

AI bar and privacy

### V2-B0637 · v2 lines 3020–3020

Files MUST host the global persistent contextual AI bar. The Files context snapshot includes current location/provider, selected FileIDs/FolderIDs, artifact types, sync state and permissions. AI requests use typed Files/owning-app/Stack actions for operations such as organise, rename, move, convert, summarise or create a Stack.

### V2-B0638 · v2 lines 3023–3023

Selecting a private local file MUST NOT silently upload file content to a cloud model. Dulche’s local/cloud privacy and permission rules apply. Context handoff MUST identify the exact file/revision being provided and whether bytes/content leave the device.

### V2-B0639 · v2 lines 3026–3026

Files MUST remain fully functional with AI disabled/unavailable. AI is additive and MUST NOT replace essential manual file-management UI.

### V2-B0640 · v2 lines 3029–3029

Operation journal, undo and change feed

### V2-B0641 · v2 lines 3032–3032

Consequential filesystem operations MUST be recorded in a durable operation journal with OperationID, actor/caller, target object IDs, source/destination where applicable, base/result revisions, state and structured error/result. The journal supports crash recovery, sync reconciliation and auditability.

### V2-B0642 · v2 lines 3035–3035

Safely reversible operations such as rename, move and soft-delete SHOULD expose undo by performing a real inverse storage mutation against current revision checks. Undo MUST NOT be UI-only fiction and MUST fail explicitly if intervening changes make the inverse unsafe.

### V2-B0643 · v2 lines 3038–3038

Files MUST expose a resumable typed change feed with stable event IDs/cursors for create/change/move/share/delete/restore/sync-state and other supported filesystem events. Automations/watchers can resume from a cursor without arbitrary full rescans.

### V2-B0644 · v2 lines 3041–3041

Properties and sync diagnostics

### V2-B0645 · v2 lines 3044–3044

A Properties/details surface MUST expose stable identity, path/location, provider, type, size, timestamps, owner/sharing, sync/offline state, current version/history access, app association and provider-specific metadata where available.

### V2-B0646 · v2 lines 3047–3047

Files MUST provide clear sync diagnostics for 9to1 Drive, including current connectivity/server contact, pending uploads/downloads, pending structural operations, conflicts, paused/error state, offline-pinned usage and local cache usage. Item-level errors MUST expose local/base/cloud revision information where relevant and provide Resolve/Retry/View details actions.

### V2-B0647 · v2 lines 3050–3050

Safety and filesystem edge cases

### V2-B0648 · v2 lines 3053–3053

Provider adapters and local materialisation MUST define handling for illegal/reserved filenames, case sensitivity, path length, path traversal, symlinks/reparse points and unsupported metadata. Remote names MUST NOT escape the intended local sync root or overwrite unrelated local paths.

### V2-B0649 · v2 lines 3056–3056

Filesystem shortcuts/symlinks MUST remain distinct from 9to1 artifact references/shortcuts. Copying a reference MUST NOT accidentally clone the referenced canonical artifact unless the user explicitly chooses duplication.

### V2-B0650 · v2 lines 3059–3059

Files API

### V2-B0651 · v2 lines 3062–3099

9to1.Files.ListLocations();
9to1.Files.GetProviderCapabilities(locationID);
9to1.Files.List(locationID, parentID?, query?);
9to1.Files.Get(itemID);
9to1.Files.CreateFolder(parentID, name);
9to1.Files.CreateArtifact(parentID, artifactType, config?);
9to1.Files.Rename(itemID, name, expectedRevision?);
9to1.Files.Move(itemID, destinationFolderID, expectedRevision?);
9to1.Files.Copy(itemID, destinationFolderID, policy?);
9to1.Files.Delete(itemID);
9to1.Files.Restore(itemID);
9to1.Files.Purge(itemID);
9to1.Files.Upload(destinationFolderID, source, options?);
9to1.Files.Download(itemID, destination, options?);
9to1.Files.Transfer.Get(transferID);
9to1.Files.Transfer.Pause(transferID);
9to1.Files.Transfer.Resume(transferID);
9to1.Files.Transfer.Cancel(transferID);
9to1.Files.Sync.GetState();
9to1.Files.Sync.GetItemState(itemID);
9to1.Files.Sync.Now();
9to1.Files.Sync.Item(itemID);
9to1.Files.Sync.Folder(folderID);
9to1.Files.Sync.Pause();
9to1.Files.Sync.Resume();
9to1.Files.Sync.SetAvailability(itemID, CloudOnly | AvailableOffline | AlwaysAvailable);
9to1.Files.Sync.ListPending();
9to1.Files.Sync.ListConflicts();
9to1.Files.Sync.ResolveConflict(conflictID, resolution);
9to1.Files.GetVersions(itemID);
9to1.Files.RestoreVersion(itemID, versionID);
9to1.Files.Share.Get(itemID);
9to1.Files.Share.Grant(itemID, principalID, role);
9to1.Files.Share.Revoke(itemID, principalID);
9to1.Files.Search(query);
9to1.Files.Changes.Subscribe(cursor?);
9to1.Files.Changes.GetSince(cursor);
9to1.Files.CreateStack(folderID, options?);

### V2-B0652 · v2 lines 3102–3102

These are minimum canonical entry points. Every additional donor-parity and 9to1-native user-visible operation MUST have an equivalent typed Files or owning-app action under the global API-parity rule.

### V2-B0653 · v2 lines 3105–3105

Failure behaviour

### V2-B0654 · v2 lines 3108–3108

Files MUST return structured failures including ItemNotFound, RevisionConflict, NameConflict, ConflictRequiresDecision, PermissionDenied, ProviderUnavailable, ProviderCapabilityUnsupported, QuotaExceeded, Offline, TransferInterrupted, IntegrityFailed, SyncConflict, FileInUse, StorageUnavailable, InvalidName, DestinationUnavailable, HomeServiceUnavailable and structured partial bulk results in addition to the global error contract.

### V2-B0655 · v2 lines 3111–3111

Platform behaviour

### V2-B0656 · v2 lines 3114–3114

Windows and 9to1-OS native Files expose local, removable, network, hosted and connected-provider locations according to platform/provider capability. Web Files exposes hosted/provider-accessible content but MUST NOT pretend to have arbitrary access to the browser user’s local filesystem. Browser local upload/download uses explicit browser-authorised file/folder selection.

### V2-B0657 · v2 lines 3117–3117

Acceptance requirements

### V2-B0658 · v2 lines 3120–3144

Files is implementation-complete only when acceptance coverage verifies at least:
* The CUI port accounts for applicable Files/WinUI donor functionality and preserves its core layout/workflows/QoL through a donor-parity manifest.
* 9to1 Drive appears as a normal first-class location and the same FileID opened via web and desktop represents the same canonical hosted object.
* Rename/create/move on one connected client appears live on another client without manual refresh and preserves stable item identity.
* Moving/renaming a file while it is open in its owning app does not break the open document because the app remains attached by FileID.
* Manual Sync now flushes eligible pending operations, retrieves server deltas and reports unresolved errors/conflicts.
* A CloudOnly file hydrates on demand; AvailableOffline works without network; AlwaysAvailable survives restart/cache cleanup.
* Free up space refuses to evict unsynced content and verifies durable cloud revision before eviction.
* Offline structural operations survive device restart and reconcile correctly after reconnect.
* Two conflicting concurrent binary/raw-file edits preserve both revisions or create an explicit conflict; no version is silently lost.
* Two collaborators concurrently reorganising the same hierarchy receive deterministic structural conflict behaviour rather than blind last-write-wins.
* A large resumable upload survives Files/Home restart and continues from validated committed progress.
* A failed transfer integrity check is reported as failure and never as completed.
* Version history and restore preserve later history and identify actor/source/revision.
* Trash/restore/purge behave distinctly and purge uses the elevated destructive flow.
* Sharing/ACL changes propagate to connected clients and revocation prevents future authorised access.
* Creating Write/Present/Data/Board/Canvas artifacts from Files invokes the owning app and creates canonical artifacts.
* Create Stack here/from this folder produces the expected Stack project without duplicating existing source.
* Owning-app real-time collaboration commits durable Files revisions without Files attempting semantic content merges.
* Provider capability differences disable/report unsupported operations rather than pretending support.
* Bulk operations return exact succeeded/failed/skipped outcomes and never silently lose items.
* Private local content is not sent to a cloud AI merely because it is selected in Files.
* Change-feed cursor resume does not require a full filesystem rescan and does not duplicate consequential events.
* Home crash/recovery preserves pending sync journal state and does not create duplicate operations or storage corruption.
* A very large hierarchy can process ordinary incremental changes without complete rescans for each event.
