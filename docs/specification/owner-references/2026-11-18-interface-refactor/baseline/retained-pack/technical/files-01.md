# Files and ten organisational folder views — Files — optional integration, mature file-manager behaviour; Navigation and selection (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0548 · v2 lines 2757–2758

<!-- UX27:BEGIN SECTION-12 -->
## 12. Files — optional integration, mature file-manager behaviour

### V2-B0549 · v2 lines 2760–2760

### UX27-FILES-001 — Navigation and selection

### V2-B0550 · v2 lines 2762–2762

**B/D.** Preserve the selected Files donor’s mature sidebar, tabs, breadcrumbs/address entry, back/forward, search, list/grid/details views and keyboard selection behaviour through CUI. File/folder icons, type, modified state, availability and selection are legible at the chosen density. Double-click/Open activates the intended item; rename is deliberate and does not accidentally open it.

### V2-B0551 · v2 lines 2764–2764

Tab history and view preferences persist by location where appropriate. Network/provider failure affects that location, not unrelated local browsing. A folder named Boards is an ordinary folder, not a hidden prerequisite imposed on the Boards app.

### V2-B0552 · v2 lines 2766–2766

### UX27-FILES-002 — File operations and conflict dialogs

### V2-B0553 · v2 lines 2768–2768

**B/D.** Copy/move/rename/delete create real operations with progress, destination, conflict policy and cancellation semantics. Drag/drop indicates copy versus move and validates cross-provider behaviour. A conflict dialog distinguishes replace, keep both, skip and scoped apply-to-all where supported; it must not silently overwrite because a name collision exists.

### V2-B0554 · v2 lines 2770–2770

Trash is distinct from permanent delete. Undo reflects the actual provider’s recoverability, not a decorative button. Long operations continue through the supported operation service rather than disappear when the current folder view closes.

### V2-B0555 · v2 lines 2772–2772

### UX27-FILES-003 — Open with and owning-app semantics

### V2-B0556 · v2 lines 2774–2774

**B.** Opening `.9to1board` delegates to Boards’ canonical artifact parser/editor, with narrow current access. Files may index or synchronise the file but does not own its semantic block/ink merge. Open With and Reveal actions expose actual registered handlers and locations. A stale registration is repaired explicitly, not hidden by opening a different file with a matching title.

### V2-B0557 · v2 lines 2776–2776

A user can create/open/save a board without ever connecting the Files UI. Optional hosted Files/cloud capabilities remain available through their proper adapters and consent, without replacing ordinary local Save As.

### V2-B0558 · v2 lines 2778–2778

### UX27-FILES-004 — Sync, availability and offline status

### V2-B0559 · v2 lines 2780–2780

**B/D.** Distinguish local-only, available-offline, cloud-only, uploading/downloading, conflict, revoked and unavailable items. A cloud-only entry does not falsely appear fully local; a local revision waiting to sync is not lost. Show the actual provider/account and last refresh, with bounded retries and actionable errors.

### V2-B0560 · v2 lines 2782–2785

Structural file changes use Files’ canonical identity/revision model; semantic document changes remain with the owning app. Do not resolve simultaneous edits of rich artifacts through arbitrary byte-level last-write-wins. Retain private thumbnails/indexes according to current access and privacy policy.
<!-- UX27:END SECTION-12 -->
Files
Product role and donor-first implementation

### V2-B0561 · v2 lines 2788–2788

9-1 Files is both the native CUI file-manager app and the 9to1 hosted storage service. The desktop/native product is donor-backed by Files/WinUI and MUST first reach behavioural/layout parity with the selected appropriate donor source before 9to1-specific extensions are treated as complete. CUI replaces the rendering/framework layer; the donor’s mature information architecture, workflows, interaction semantics and quality-of-life behaviour remain the baseline unless a scoped donor-parity exception is recorded.

### V2-B0562 · v2 lines 2791–2791

The parity baseline includes applicable donor navigation/sidebar structure, toolbar/commands, tabs, back/forward history, breadcrumbs/address bar, file/folder views, details/grid layouts, sorting/grouping, context menus, properties, previews, search, tags/favourites, drag/drop, clipboard, archives, network/location handling, keyboard shortcuts, selection behaviour, file-operation UX, progress/conflict dialogs, session restoration and other user-visible donor behaviours available in the selected legally usable source.

### V2-B0563 · v2 lines 2794–2794

A donor-parity manifest MUST identify the selected donor revision and classify each applicable donor feature as Preserve, Replace with 9to1-native equivalent or Not applicable. Proprietary donor AI is not copied; equivalent AI functionality uses Dulche/9to1 APIs. Subscription/account infrastructure specific to the donor is not inherited unless independently required by this specification.

### V2-B0564 · v2 lines 2797–2797

Authority and ownership boundaries

### V2-B0565 · v2 lines 2800–2800

Files owns file/folder identity, hosted storage, locations/providers, structural hierarchy, transfers, sync, versions, sharing/ACLs, Trash/recovery, file-operation history and filesystem-level collaboration. Files does NOT own semantic editing/collaboration inside another app’s document/project format.

### V2-B0566 · v2 lines 2803–2803

The owning app remains authoritative for semantic file content and live in-document collaboration: Write owns Write-document collaboration, Present owns presentations, Boards owns Boards content, Canvas owns Canvas content, Data owns workbook/database semantics and Stack owns Git/Stack source-control semantics. Files stores and synchronises durable revisions produced by those apps but MUST NOT attempt to merge paragraphs, spreadsheet cells, Board blocks, Canvas strokes or Git histories generically.

### V2-B0567 · v2 lines 2806–2806

Raw/unowned binary/text files use Files’ revisioned-content storage model. When two clients concurrently modify bytes and no owning semantic merge engine exists, Files MUST preserve both conflicting revisions or expose a structured conflict; it MUST NOT silently choose the newest wall-clock timestamp.

### V2-B0568 · v2 lines 2809–2809

Canonical identity and item model

### V2-B0569 · v2 lines 2812–2812

Every 9to1-hosted file and folder MUST have a stable FileID or FolderID independent of name, path, sort position or parent. Rename and move MUST preserve identity. Paths are human-facing projections of hierarchy and MUST NOT be used as canonical identity by other 9to1 apps.

### V2-B0570 · v2 lines 2815–2815

Hosted file metadata MUST include at least FileID, ParentFolderID, Name, ItemType, content/media type, owner/scope, size, created/modified timestamps, current content revision, sync/availability state, sharing state and version metadata. Folders MUST expose equivalent stable identity and ordered/queriable child relationships.

### V2-B0571 · v2 lines 2818–2818

Synced local materialisations of 9to1 Drive items MUST retain metadata linking the local file to the canonical hosted FileID and base/current remote revision. Identity MUST NOT be reconstructed from pathname alone.

### V2-B0572 · v2 lines 2821–2821

Locations and provider abstraction

### V2-B0573 · v2 lines 2824–2824

Files MUST expose all storage roots through one typed Location/Provider abstraction. Required native location classes include 9to1 Drive, local disks, removable storage and supported network locations. Connected cloud providers MAY be added through adapters.

### V2-B0574 · v2 lines 2827–2827

Every provider MUST declare capabilities explicitly, including read, write, rename, move, copy, trash, permanent delete, versions, sharing, offline availability, search, streaming, server-side copy/move, change feeds and any provider-specific constraints. The UI and APIs MUST disable/report unsupported operations rather than pretending parity exists.

### V2-B0575 · v2 lines 2830–2830

Provider authentication and secrets MUST be stored through secure credential references. Provider-specific OAuth/tokens MUST NOT be persisted in plaintext Files configuration or logs.

### V2-B0576 · v2 lines 2833–2833

Desktop shell, tabs and views

### V2-B0577 · v2 lines 2836–2836

The CUI desktop shell MUST preserve the donor’s applicable tabbed browsing, navigation history, breadcrumbs/address bar, command toolbar, sidebar/location hierarchy, selection behaviour and view-management semantics. Session restore MUST reopen the prior tab/location set where available; unavailable removable/network locations restore into an explicit unavailable state rather than being silently discarded.

### V2-B0578 · v2 lines 2839–2839

Grid/list/details and other applicable donor view modes MUST be supported. Sort, group, visible columns, thumbnail size and compatible folder/view preferences MAY persist per-folder or per-location according to provider capability.

### V2-B0579 · v2 lines 2842–2846

Folder colour customisation
Files MUST support native folder-colour customisation independently of any OneDrive-specific donor capability. Users can choose from presets or an arbitrary colour using a full colour wheel/picker, apply the chosen presentation to a folder and reset it to the default appearance.
Folder colour is Files presentation metadata associated with the stable folder identity, not a pathname-only hack. It must work for local folders and 9to1-hosted folders and should work for connected-provider folders where provider capability or Files-owned metadata permits. A provider that lacks native folder-colour metadata MUST NOT force Files to disable the feature if the presentation override can safely be stored by Files.
Where compatible with sync/account policy, folder-colour metadata may follow the canonical folder across devices. Files MUST expose the same typed operation to Dulche and Automations, enabling actions such as changing selected/project folder colours without GUI automation.
Native 9to1 creation and project integration

### V2-B0580 · v2 lines 2849–2849

Files MUST replace Office-centric creation defaults with first-class 9to1 creation entries. The toolbar/context New menu MUST include at least Folder, Write document, Presentation, Spreadsheet/Data artifact, Board, Canvas and Stack project when the current location/provider can store the requested artifact.

### V2-B0581 · v2 lines 2852–2852

Creation MUST invoke the owning app’s canonical API rather than having Files manufacture private document internals. For example, creating a Write document from Files calls the Write creation API and receives the resulting canonical ArtifactID/FileID. Files owns placement/storage; the owning app owns document structure.

### V2-B0582 · v2 lines 2855–2855

Files MUST support Create Stack here and Create Stack from this folder for appropriate folders. The latter registers/imports the existing source location as a Stack project rather than creating a nested duplicate source tree. Stack identity/backing remains owned by Stack; Files supplies the selected backing location/reference.

### V2-B0583 · v2 lines 2858–2858

App/project folders MAY expose a project-aware representation so users see a meaningful project entity instead of internal managed metadata by default. Advanced inspection MAY reveal underlying files when the owning app permits it. Files MUST respect app-managed/protected metadata markers and warn/block casual destructive edits that would corrupt managed project state.

### V2-B0584 · v2 lines 2861–2861

9to1 Drive integration

### V2-B0585 · v2 lines 2864–2864

9to1 Drive MUST appear directly in the normal Files sidebar/location tree alongside local drives and other providers. Hosted storage MUST NOT open into a separate incompatible cloud-files UI. Desktop, web and owning apps address the same canonical hosted objects by stable IDs.

### V2-B0586 · v2 lines 2867–2867

A file uploaded through cakemods.com and later opened in desktop Files MUST refer to the same FileID/content revision, not a separately imported desktop copy. Local materialisation is a cache/replica of the hosted object associated with FileID/revision metadata.

### V2-B0587 · v2 lines 2870–2870

Hosted storage MUST expose quota/allocation, used space and pending/reserved upload space where applicable. Quota exhaustion MUST fail structurally and MUST NOT leave invisible orphan uploads.

### V2-B0588 · v2 lines 2873–2873

Rapid push-based sync architecture

### V2-B0589 · v2 lines 2876–2876

Sync is a core correctness requirement. 9to1 Drive MUST use event/push-driven incremental synchronisation as the normal connected path. Polling/full rescans are recovery/fallback mechanisms and MUST NOT be the primary source of ordinary change propagation.

### V2-B0590 · v2 lines 2879–2879

The server MUST produce ordered change events for filesystem-level changes including create, rename, move, metadata change, content-revision commit, delete, restore, share/ACL changes and relevant availability/sync metadata changes. Connected clients maintain a durable SyncCursor/last acknowledged server revision and apply deltas incrementally.

### V2-B0591 · v2 lines 2882–2882

On reconnect, a client submits its last durable cursor plus pending local operation journal state; the server returns deltas since that cursor, the client reconciles queued local operations, then resumes the live stream. Duplicate/replayed operation IDs MUST be idempotent and MUST NOT perform consequential mutations twice.

### V2-B0592 · v2 lines 2885–2885

Ordinary metadata operations such as rename/move/create MUST be pushed immediately after commit and processed continuously by connected clients. Intentional multi-minute delays for normal metadata propagation are non-conformant. Foreground/open artifacts and explicit manual sync receive scheduling priority over background hydration/prefetch.

### V2-B0593 · v2 lines 2888–2888

Optimistic local operations

### V2-B0594 · v2 lines 2891–2891

User-initiated structural operations SHOULD feel immediate. Files MAY optimistically apply a local rename/move/create to the UI before the remote round trip, but MUST record the operation durably as Pending with OperationID, expected/base revision and inverse/recovery metadata.

### V2-B0595 · v2 lines 2894–2894

If the server accepts the mutation, the operation becomes committed/synced and remote clients receive the corresponding event. If validation fails, Files MUST reconcile/reverse the optimistic state and display the structured reason; it MUST NOT leave the UI pretending the server accepted a rejected mutation.

### V2-B0596 · v2 lines 2897–2897

Sync and availability state

### V2-B0597 · v2 lines 2900–2900

Hosted items MUST expose explicit sync/availability state rather than a generic spinner. Required states include CloudOnly, Hydrating, AvailableOffline, AlwaysAvailable, LocalChanges, Uploading, Synced, Conflict, Paused and Error. Internal implementations MAY maintain separate content and metadata states so lightweight metadata can become synced while content transfer continues.

### V2-B0598 · v2 lines 2903–2903

CloudOnly means metadata is locally visible but content bytes are not guaranteed local. AvailableOffline means content is currently materialised and may be evicted by managed cache policy only when safe. AlwaysAvailable means the user has pinned the content and Files MUST retain a local copy until the user changes that policy or storage becomes critically unavailable and the user explicitly resolves the situation.

### V2-B0599 · v2 lines 2906–2906

Opening, previewing or an owning app requesting bytes for CloudOnly content triggers hydration on demand. Hydration MUST expose progress/failure and MUST NOT claim the item is locally available before durable content validation completes.

### V2-B0600 · v2 lines 2909–2909

Manual sync and sync-on-demand

### V2-B0601 · v2 lines 2912–2912

Files MUST provide visible manual sync controls, including Sync now, Sync this item, Sync this folder/selection, Pause sync, Resume sync, retry eligible failures and refresh/reconcile from cloud. Manual sync is an acceleration/reconciliation control, not a substitute for reliable automatic sync.

### V2-B0602 · v2 lines 2915–2915

Sync now MUST flush eligible pending local mutations, request current server deltas since the durable cursor, retry eligible transfers, reconcile conflicts that have deterministic policy and report the resulting pending/error/conflict state. It MUST NOT discard unsynced changes merely to match cloud state.

### V2-B0603 · v2 lines 2918–2918

Offline-first behaviour

### V2-B0604 · v2 lines 2921–2921

Locally materialised files/folders MUST remain usable offline according to their owning app/file-format capabilities. While offline, Files MUST allow safe queued structural operations including create folder, rename, move, delete/restore and reorganisation, and it MUST allow edits to locally available content through the owning app.

### V2-B0605 · v2 lines 2924–2924

Offline mutations enter a durable ordered operation journal that survives Files/Home/process/device restart. On reconnect, operations are reconciled against remote revisions/permissions before commit. Blind last-write-wins based solely on timestamps is prohibited for conflicts.

### V2-B0606 · v2 lines 2927–2927

Free up space MUST verify that the canonical hosted revision is durable and equivalent/newer before evicting local bytes. Items with unsynced local content MUST NOT be evicted. AlwaysAvailable items MUST NOT be evicted by normal cache cleanup.

### V2-B0607 · v2 lines 2930–2930

Real-time filesystem collaboration

### V2-B0608 · v2 lines 2933–2933

Files MUST provide real-time collaboration for filesystem structure and Files-owned metadata. Connected authorised clients viewing the same hierarchy should receive live create, rename, move, delete/restore, share/ACL and metadata updates without requiring manual refresh.

### V2-B0609 · v2 lines 2936–2936

Structural mutations MUST carry stable OperationID, actor/caller identity, target object ID, base/expected revision, operation type and result revision. Simultaneous incompatible structural edits MUST produce a deterministic RevisionConflict/Conflict object rather than silently letting whichever request arrived last overwrite the other.

### V2-B0610 · v2 lines 2939–2939

If an owning app has a file open by FileID and another collaborator moves/renames that file, Files MUST publish LocationChanged/metadata events while preserving FileID. The owning app continues editing the same canonical file without losing attachment simply because the path changed.

### V2-B0611 · v2 lines 2942–2942

Semantic collaboration inside a file is exclusively owned by the file’s owning app. Files receives/commits durable revisions from that collaboration layer and exposes version/storage metadata, but it MUST NOT run a second generic content-collaboration engine over the same file.

### V2-B0612 · v2 lines 2945–2945

Transfers

### V2-B0613 · v2 lines 2948–2948

Copy, move, upload, download and provider-transfer operations MUST be represented as durable Transfer jobs with TransferID, item set, source/destination, bytes/items totals where known, progress, state, retryability, error list and pause/resume/cancel support where technically possible.

### V2-B0614 · v2 lines 2951–2951

Hosted uploads/downloads MUST use resumable/chunked transfer when the service/provider supports it. Files/Home restart MUST resume from validated committed chunks where possible rather than restarting large transfers from zero.

### V2-B0615 · v2 lines 2954–2954

Transfer completion MUST include integrity validation where hashes/checksums are supported. Integrity failure is a failed transfer and MUST NOT be represented as successful-with-warning.

### V2-B0616 · v2 lines 2957–2957

Destination conflicts MUST use explicit policy: Ask, Replace, KeepBoth or Skip, with ApplyToRemaining where appropriate. Automated callers MUST specify a policy or receive ConflictRequiresDecision.

### V2-B0617 · v2 lines 2960–2960

Version history and durable revisions

### V2-B0618 · v2 lines 2963–2963

9to1 Drive files MUST have durable version history. Each durable revision MUST include VersionID/RevisionID, timestamp, actor/source, parent/base revision where relevant and content identity/hash where available. Restoring an older revision MUST create a new current revision or otherwise preserve the later history rather than deleting it.

### V2-B0619 · v2 lines 2966–2966

Owning apps MAY commit incremental/semantic collaboration checkpoints, but Files stores the durable file/artifact revision boundary supplied by the app. Files MUST not reinterpret app-specific internal history as generic filesystem versions unless the owning app explicitly publishes those revisions.

### V2-B0620 · v2 lines 2969–2969

Trash, delete and recovery
