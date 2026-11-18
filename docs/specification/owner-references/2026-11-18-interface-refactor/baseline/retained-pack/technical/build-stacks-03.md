# Build Stacks — Branch, Twig and Leaf source control — Domain identity and effective source; Domain creation, hierarchy guards and reparenting (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1815 · v2 lines 9472–9682

Domain identity and effective source
Every source domain has stable DomainID, ProjectID, type (Main, Branch, Twig or Leaf), parent DomainID where applicable, name, creation/base revision, local revision, effective revision, state, visibility, permission policy and Revision. Renaming or moving display labels MUST NOT change identity.
Effective source is a deterministic projection of the domain's inherited ancestor state plus the domain's own deltas, tombstones, renames and Root/Freeze/Subroot rules. For a Leaf, the conceptual order is Main baseline → Branch delta → Twig delta → Leaf delta.
Each effective file/resource can expose lineage: originating ancestor/domain, inherited revision, local override/delta, rename/delete state, Root authority and any paused/conflicted cascade. View lineage MUST use canonical history rather than infer origin from current path text.
Domain creation, hierarchy guards and reparenting
Hierarchy is enforced by the model/API: Main may create Branches; a Branch may create Twigs; a Twig may create Leaves; a Leaf cannot create children. Main cannot be a child and cannot contain Twigs, Leaves or Subroots.
Creating a child records its exact parent/base effective revision. Later parent changes arrive through cascade processing; the recorded base remains available for history, review and conflict calculation.
Reparenting is not a rename. Moving a Twig to another Branch or a Leaf to another Twig requires an explicit Reparent operation that calculates the new base/effective source, previews conflicts, preserves stable DomainID/history and requires the configured permission. Cross-project reparenting is prohibited; use fork/import/migration workflows instead.
Deletion of a parent with active descendants is blocked unless the operation explicitly archives/prunes/migrates the descendants under the corresponding lifecycle rules.
Materialisation and concurrency model
Stack MUST support opening, browsing, building and acting on multiple domains concurrently without requiring user-facing Git worktree switching. Each active execution/editor receives a domain-scoped materialised view produced from canonical effective source.
Implementations MAY use overlay filesystems, virtual projections, content-addressed materialisation, temporary isolated checkout directories or another robust mechanism, but the materialisation is derived state and MUST NOT become a competing source of truth.
Concurrent writes target stable DomainID/resource identity. Two clients or agents editing the same domain/resource use revision/merge conflict semantics; isolated domains may proceed independently. A failed materialisation or cache rebuild MUST be recoverable from canonical Stack/Git/Files state.
Commit history for Branches, Twigs, Leaves and Subroots
Branches use normal native Git commit history where Git-backed. Twig/Leaf/Subroot changes also have ordered canonical Change/Commit records with stable IDs, parent revision, author/initiator, timestamp, message, affected resources and resulting domain revision.
When GitHub-backed, Twig/Leaf/Subroot commit objects are represented through the protected Stack companion storage/manifest so their history is durable without exposing each nested domain as a normal Git branch. The manifest maps stable Stack change IDs to retained Git object/content identities sufficient for reconstruction.
History for a domain distinguishes inherited ancestor changes from locally authored changes. Rewriting or squashing history is explicit and permission-controlled; it MUST NOT silently invalidate descendants, releases or open Change Requests.
Promotion and Change Requests
Normal contribution flow is Leaf → parent Twig → parent Branch → Main. Promotion is explicit upward movement of a reviewed change set; downward inheritance remains the separate cascade mechanism.
A ChangeRequest has stable ChangeRequestID, source DomainID/revision, destination DomainID/revision, included changes/commits, diff, author, reviewers, comments, required checks, approvals, conflict state, policy state, outcome and resulting revision.
Promote from a Leaf normally targets its parent Twig; from a Twig its parent Branch; from a Branch Main. Project policy MAY allow a deliberate bypass, but the UI/API must state which levels are being bypassed, calculate the complete destination diff and record the bypass in audit history.
Promotion never silently includes unrelated descendant or working-tree changes. The source revision being reviewed is pinned. New source edits after review begins create a new revision and invalidate/re-run configured approvals/checks as policy requires.
Completing a Change Request applies the accepted change to the destination, records resulting lineage, then normal cascade semantics propagate the destination update downward. The source domain may remain open, be marked completed or be swept/pruned according to user/project policy.
Review, approvals and merge strategy
Change Requests support threaded review comments, requested changes, approvals, assigned reviewers, CODEOWNERS/ownership-style policies where configured, check requirements and protected-domain rules.
Available integration strategies may include merge commit, fast-forward, rebase or squash only where the target/domain policy and Stack lineage model can preserve stable identity and ancestry. The chosen strategy is explicit in the Change Request outcome.
Protected Main/Branch domains can require approvals, passing checks, signed commits or designated maintainers before promotion. An administrator override, where policy permits it, is explicit and audited.
Cascade implementation and update state
Automatic Main/Branch cascade is represented as canonical ancestor revisions applied to descendant effective-source state, not as an uncontrolled series of user-visible Git merges. Each descendant tracks the highest ancestor revision successfully incorporated for each relevant lineage.
When an ancestor changes, Stack computes the descendant update. Non-conflicting inherited changes become part of effective source automatically; locally owned +A/-D totals remain unchanged. If a resource conflicts, cascade for that affected resource/path pauses and the descendant records UpstreamConflict while unrelated safe updates MAY continue.
Users can inspect pending/inherited upstream changes and the ancestor revisions that caused them. Cascade processing is idempotent and crash-resumable.
Deletion, rename and tombstone inheritance
A descendant deletion is represented as a tombstone bound to canonical resource lineage, not mere physical absence. The tombstone prevents the deleted ancestor resource from reappearing when a later cascade occurs.
Renames/moves preserve stable resource lineage where deterministically known, storing old/new path plus resource identity. A later ancestor edit to the pre-rename resource is mapped to the renamed descendant resource when lineage proves equivalence; ambiguous cases surface a conflict.
Replacing a resource with an unrelated new resource at the same path creates new lineage rather than pretending it is the old object. Tombstones and rename metadata are versioned and included in recovery/migration.
Conflict objects and resolver
Every unresolved conflict is a structured Conflict object with stable ConflictID, ProjectID/DomainID, resource identity/path, base revision/content, upstream side, local side, conflicting regions/properties, lineage, origin operation, state, assigned resolver where applicable and Revision.
Text/code conflict resolution supports three-way views such as Upstream | Result | Local, hunk/region selection, take upstream, take local, combine/edit result, accept all non-conflicting changes and Open in Dev. Structured 9to1 resources SHOULD use semantic conflict resolution when a canonical domain API can represent it faithfully.
Dulche may Summarise or propose Fix with AI through typed actions. AI proposals remain reviewable diffs; Reject restores the pre-proposal resolution state, Accept without Merging stores the resolution, and Accept and Merge completes the pending operation only after explicit user approval.
Root authority model
Root records have stable RootID, owning DomainID, protected resource identities/paths, creation revision, state, provenance/reason, policy and Revision. Root enforcement comes from canonical metadata; roots.json is a persisted projection/manifest, not the only source of truth.
A Root locks descendant override of the protected resource. The owning domain may update its Root through authorised Root workflows, after which descendants inherit the new authoritative state through normal cascade rules.
Root scope may include a file, folder or registered structured resource. Overlapping Root definitions are validated so ownership is deterministic.
Subroot lifecycle
A Subroot is a controlled experimental/update workspace for one or more Roots owned by the same eligible non-Main domain. It has stable SubrootID, owning DomainID, claimed RootIDs, base Root revisions, local proposed changes, checks/review state and lifecycle state.
Within one owning domain, a Root can belong to only one active Subroot at a time. Main can contain Roots but cannot contain Subroots.
Completing a Subroot requires a diff against the owning Root, required tests/checks, conflict resolution and configured approvals. Accepted changes update the owning Root atomically as an auditable Root revision, release the claims and cascade to descendants. Abandoning a Subroot discards the proposal while retaining history/audit. Subroots with unresolved changes/claims cannot be silently deleted.
Freeze lifecycle and permissions
A Freeze is a scoped edit restriction, distinct from Root authority. Freeze records have stable FreezeID, target resource/domain, selected descendant scope, creator, reason, creation revision, optional expiry/condition, policy and state.
New descendants inherit an active Freeze only according to its declared scope policy. Early thaw requires Freeze-management permission and is audited. Automatic expiry moves the Freeze to Expired and restores editability after checking in-progress operations and reconciling queued upstream updates.
Users without Freeze-management permission cannot bypass a Freeze through API, raw Git adapter or alternate client. Emergency administrator override, if enabled by organisation policy, is explicit, requires strong confirmation and is audited.
Files and GitHub dual-authority/synchronisation
When only Files backing is enabled, Files/Stack canonical metadata and source are authoritative. When only GitHub backing is enabled, native Git state plus protected Stack metadata are authoritative according to the adapter contract.
When both Files and GitHub are enabled, the project config explicitly selects authority mode: Stack/Files primary with GitHub mirror, GitHub primary with Files cache/backup, or managed bidirectional synchronisation. Bidirectional mode uses stable object/revision mapping and MUST surface concurrent divergent edits as sync conflicts; last-writer-wins is not an acceptable default.
Each successful sync records source revision, destination revision and mapping checkpoint. Partial sync leaves a resumable SyncConflict/Failed state and MUST NOT claim both backends are current.
Credentials and private remote metadata use shared credential/security infrastructure rather than being stored in project source.
Git compatibility and advanced Git operations
Git remains an interoperable underlying source-control substrate. Stack supports commit, stage/unstage where relevant, fetch, pull/synchronise, push, revert, cherry-pick, tags, signed commits, history, blame, diff, Git LFS, import/migration and mirrors where compatible.
Raw Git operations that would invalidate Stack-managed lineage, dependency refs or Root/Subroot metadata are blocked or routed through an explicit Advanced/Unmanaged operation that explains consequences and repairs/reconciles Stack metadata before normal work resumes.
Checking out an arbitrary Git branch inside a Stack-managed materialisation MUST NOT silently change the active Stack DomainID. Active domain is controlled by Stack's typed SetActive/OpenDomain operations.
Mixed public/private projection
Visibility is attached to canonical domains/resources and inherited according to explicit policy. Public projection includes only content and history proven public for the selected release/domain projection.
Before public GitHub mirroring, forking or source browsing, Stack validates the projected tree and reachable object/history graph. If private content, credentials, private commit ancestry or private Stack metadata would be reachable, publication is blocked until the exposure is resolved.
Changing a resource from public to private does not retroactively erase already-published Git history. Stack warns when historical exposure exists and offers supported remediation/rewrite workflows without pretending the previous public data was never published.
Team permissions and sensitive operations
Permissions can be set at organisation/project and, where needed, domain level. Core roles/capabilities cover view source, contribute, create child domains, commit, promote/create Change Requests, review/approve, manage Roots/Subroots, manage Freezes, manage releases/packages, administer visibility/remotes and prune/purge.
Child-domain permissions inherit from parent policy unless an explicit narrower override is allowed. A child MUST NOT grant broader access to private ancestor material than the parent/project permits.
Sensitive operations such as permanent purge, private→public exposure, protected-domain override, remote replacement and destructive Root history rewrite require elevated permission and stronger confirmation; project policy MAY require 2FA.
CI, build and test checks
Branches, Twigs and Leaves can run revision-pinned checks supplied by Dev, Automations or configured CI providers. CheckRun records provider, source DomainID/revision, workflow/config revision, status, logs/artifacts references and conclusion.
Change Requests show required and optional checks. Protected destinations can require successful build/test/lint/security/package checks before promotion. A check result from an older source revision MUST NOT satisfy a requirement for a newer reviewed revision.
Checks run against the exact effective source of the target domain, including inherited ancestor state, not merely that domain's local delta.
Releases, tags and packages
Release normally targets an approved Main revision. Project policy MAY permit another explicitly selected stable domain/revision, but the Release records its exact source identity and does not float with later domain changes.
Release has stable ReleaseID, source ProjectID/DomainID/revision, tag/version, notes, artifacts, checks, publisher, visibility and publication state. Tags map to immutable source revisions unless an explicit privileged retag operation is performed and audited.
Package/release distribution capabilities inherited from Gitea or shared 9to1 package infrastructure remain available where applicable. Packages record project, version, source/release revision, publisher, permissions and registry metadata so Dev can consume configured private/public feeds.
GitHub mirroring, backup and restore
GitHub mirroring/backup records mirror configuration, last successful checkpoint and source/destination revisions. A mirror is never treated as current merely because the remote exists.
Restore can rebuild Stack state from the authoritative Git repository plus protected Stack manifests/object mappings and configured Files recovery metadata. Missing companion refs are reconstructed from retained object IDs when possible.
If complete reconstruction is impossible, Stack preserves available source/history, marks exact Domains/Roots/Subroots/changes that cannot be recovered and blocks writes that would destroy remaining recovery evidence. It MUST NOT fabricate successful restoration.
Sites and Dev integration
Sites deployments/previews bind to exact Stack ProjectID, DomainID and revision. Branch/Twig/Leaf preview deployments are isolated where policy permits; production defaults to an approved Main release/revision unless explicitly configured otherwise.
Sites source edits are written back as normal changes in the bound Stack domain and become visible in diff/history/Change Requests. Sites MUST NOT maintain a hidden divergent source copy.
Open in Dev opens the exact Stack domain/materialised effective source and provides ProjectID/DomainID/revision context. Dev edits become local Stack changes in that domain. Builds/tests/diffs shown in Stack and Dev reference the same canonical revision identities.
Issues and Boards integration
Stack preserves source-oriented Issues, labels, milestones and repository issue workflows required by donor parity. Issues may link to commits, domains, Change Requests, releases and affected paths/resources.
Boards owns richer planning/project-management workflows. Stack can create/link a Board card from an Issue and show linked Board state, while Boards can reference Stack issues/changes. The products MUST NOT silently duplicate and diverge two full project-management databases.
Automations and webhooks
Stack exposes typed triggers/actions for project/domain/change/review/release lifecycle through Automations. Webhooks/integrations can subscribe to events such as push/change committed, domain created/completed, Change Request opened/updated/merged, check completed, release published and issue changed.
Webhook payloads expose stable IDs and permission-scoped data. Delivery has retry/idempotency identifiers and observable failure state; a failed webhook MUST NOT roll back an already-completed source operation unless an explicit transactional integration contract exists.
Public discovery, contributions and moderation
Public projects expose profile, source/history/releases, contributors, forks, Change Requests and Discover integration without exposing Stack-managed internal storage.
Forking creates independent ProjectID/repository identity and records source-project provenance. Contributions return through a Change Request against an explicitly selected public destination domain and do not grant access to private components.
Moderation includes report/block capabilities, project-maintainer controls and platform policy enforcement for public-facing content. Moderation actions affecting source visibility/history are explicit and audited.
Sweep, prune, retention and purge
Sweep previews the descendant domains and changes to be consolidated into the owning Branch, verifies conflicts/checks as configured, applies accepted descendant changes to the Branch and only then marks/removes completed descendant development domains. It MUST NOT discard unmerged descendant work silently.
Prune removes a Branch/Twig/Leaf from active Stack navigation and requires the configured permission; protected project policy can require 2FA. Main can never be pruned.
Pruned domains enter recoverable Deleted state for the configured retention period. Restore reactivates the same stable DomainID/backing references where retained objects remain valid.
Permanent purge is a separate privileged operation and is blocked while retained objects are referenced by active descendants, releases, Change Requests, audit requirements or recovery points. Purge records what source/metadata was removed and what immutable audit evidence remains.
Audit log
Audit coverage includes project creation/deletion/settings, domain create/reparent/archive/prune/restore/purge, commits and sync/push/fetch operations, Change Requests, approvals/overrides, conflicts and resolutions, Root/Subroot/Freeze lifecycle, permission/visibility changes, public projection, remote/mirror changes, releases/packages, CI/check state changes, moderation actions, autocommit actions and AI-authored actions.
Audit entries identify actor/initiator, stable target IDs, timestamp, operation type, relevant before/after revisions, outcome and linked recovery/change request where applicable. Sensitive values such as credentials are never logged.
Large repositories, binary data and LFS
Repository browsing/history/diff uses virtualisation, paging and lazy content loading for large trees/histories. Stack MUST NOT materialise every domain or download every large object merely to open a project.
Binary files display metadata/history and binary-aware replacement state rather than bogus line diffs. Git LFS or an equivalent configured large-object backend is preserved where applicable; pointer/object availability is validated before claiming a domain/release is complete.
Large-object quota/provider failures surface typed errors and do not silently replace missing binaries with empty files.
Metadata schema migration and disaster recovery
Stack-managed manifests and companion storage use explicit schema versions. Upgrades run idempotent migrations with pre-migration recovery checkpoints where feasible and never rewrite unknown/newer schema versions as if understood.
A migration failure leaves the previous recoverable state and marks the project NeedsMigration/Recovery rather than partially continuing normal writes.
Disaster recovery documentation and APIs cover loss/corruption of local materialisations, Files backing, GitHub companion ref, metadata manifests and sync checkpoints. Recovery prioritises canonical source/history and stable IDs, reports unrecoverable state precisely and never fabricates missing commits/resources.
Dulche and AI-assisted source control
Dulche context includes ProjectID, active/selected DomainID, ancestry/effective revisions, local/inherited changes, Roots/Subroots/Freezes, conflicts, Change Requests, checks, releases and permissions.
Examples include explaining why a file differs from Main, summarising a Twig's owned changes, creating a Leaf for a task, proposing a conflict resolution and preparing a domain for promotion.
AI source mutations, conflict fixes, commits, domain creation and review actions use typed Stack/Dev/Files APIs. AI MUST NOT manipulate Stack-managed companion branches/folders through opaque shell/UI automation when a semantic action exists.
AI claims about build/test/review readiness must reference actual executed check results for the pinned revision.
Stack API
Stack's first-party API catalogue is exhaustive under the global API-parity rule. Minimum canonical entry points include:
9to1.Stack.CreateProject(config);
9to1.Stack.OpenProject(projectID);
9to1.Stack.CreateBranch(projectID, config);
9to1.Stack.CreateTwig(branchID, config);
9to1.Stack.CreateLeaf(twigID, config);
9to1.Stack.SetActiveDomain(projectID, domainID);
9to1.Stack.GetDomain(domainID);
9to1.Stack.GetEffectiveTree(domainID, options?);
9to1.Stack.GetLineage(domainID, resourceIDOrPath);
9to1.Stack.GetDomainChanges(domainID, options?);
9to1.Stack.GetUpstreamChanges(domainID, options?);
9to1.Stack.SyncDomain(domainID, options?);
9to1.Stack.CreateCommit(domainID, changeSelection, message, options?);
9to1.Stack.Push(domainID, options?);
9to1.Stack.Fetch(projectID, options?);
9to1.Stack.ReparentDomain(domainID, newParentID, options?);
9to1.Stack.CreateChangeRequest(sourceDomainID, destinationDomainID, options?);
9to1.Stack.ReviewChangeRequest(changeRequestID, review);
9to1.Stack.ApproveChangeRequest(changeRequestID, options?);
9to1.Stack.MergeChangeRequest(changeRequestID, strategy, options?);
9to1.Stack.Promote(domainID, options?);
9to1.Stack.GetConflicts(scope);
9to1.Stack.ResolveConflict(conflictID, resolution, options?);
9to1.Stack.CreateRoot(domainID, resources, config?);
9to1.Stack.UpdateRoot(rootID, changeSet, options?);
9to1.Stack.CreateSubroot(domainID, rootIDs, config?);
9to1.Stack.CompleteSubroot(subrootID, options?);
9to1.Stack.AbandonSubroot(subrootID, options?);
9to1.Stack.CreateFreeze(target, scope, config?);
9to1.Stack.ThawFreeze(freezeID, options?);
9to1.Stack.CreateRelease(projectID, sourceRevision, config);
9to1.Stack.Sweep(branchID, options?);
9to1.Stack.Prune(domainID, options?);
9to1.Stack.RestorePruned(domainID, options?);
9to1.Stack.Purge(domainID, options?);
9to1.Stack.ConfigureAutocommit(domainID, config);
Generic Files, identity, credentials, Boards, Sites and Dev operations use their owning namespaces where canonical.
Autocommit
Autocommit is disabled by default and must be enabled explicitly per repository/domain. It supports a configured elapsed interval or inactivity duration; settings may display friendly minutes while runtime stores precise duration.
Autocommit detects eligible changes, excludes ignored/prohibited/known-secret material according to project security rules, refuses to run across unresolved conflicts, creates a normal canonical commit with an inspectable message/change selection and records the operation in audit history.
Two policies are distinct: Autocommit locally only, and Autocommit + sync remote. Remote synchronisation occurs only when enabled and permitted. Failed push/sync leaves the commit local with explicit Ahead/SyncFailed state; Stack MUST NOT claim the domain is synced.
Autocommit can be disabled for individual repositories/domains even when enabled at a broader user/workspace level.
Accessibility
Project/domain tree, change lists, diffs, conflict resolver, Change Requests, reviews, Root/Subroot/Freeze controls, releases and audit views are keyboard operable and expose screen-reader semantics.
Addition/deletion/conflict/upstream/local/remote states MUST NOT rely only on colour or icons. +A/-D counts, status text and accessible descriptions identify state.
Three-way conflict and diff views provide line/hunk navigation and textual descriptions usable without pointer-only interaction.
Performance and resilience
Stack virtualises large trees, history, diffs, reviews and audit streams; expensive lineage/effective-source calculations are cached by revision and invalidated deterministically.
Cascade, sync, Sweep, large Change Request checks, mirror operations and recovery run as cancellable/resumable jobs with stable JobID/progress/error state where applicable.
Crash/restart MUST not lose canonical domain metadata, pending conflict state or accepted commits. Derived materialisations/caches may be discarded and reconstructed.
Recovery and typed failures
Structured failures include ProjectNotFound, DomainNotFound, InvalidHierarchy, ReparentConflict, MaterialisationFailed, SourceCorrupt, ManagedMetadataMissing, SchemaVersionUnsupported, MigrationFailed, UpstreamConflict, ConflictUnresolved, RootLocked, RootClaimed, FreezeActive, ChangeRequestOutOfDate, RequiredCheckFailed, ApprovalRequired, SyncConflict, RemoteUnavailable, MirrorFailed, PrivateProjectionViolation, LFSObjectMissing, RestoreIncomplete, PurgeBlocked, RevisionConflict, PermissionDenied and CapabilityUnavailable plus provider-specific Git/Files errors.
Operations that partially affect external remotes or mirrors report exact completed/failed stages and preserve recovery checkpoints. Stack MUST NOT report rollback, restore, sync or merge success unless the resulting canonical and provider state was verified as required by that operation.
Platform capability behaviour
Windows, 9to1-OS and web preserve the same canonical Project/Domain/Root/Subroot/Freeze/ChangeRequest model. Local materialisation, Git executable availability, filesystem semantics, credential providers and build integrations are capability-negotiated.
Where a platform cannot safely materialise or execute a local action, Stack uses an explicitly configured service/remote provider or reports CapabilityUnavailable; it MUST NOT silently change source-control semantics.
When Home is absent at process start, Stack follows the global Install Home/bootstrap contract.
Acceptance requirements
Stack is implementation-complete only when acceptance coverage verifies at least:
* A Main → Branch → Twig → Leaf hierarchy persists stable DomainIDs across rename/reopen and enforces legal parent/child types.
* A child records its base revision and effective source correctly combines ancestor plus local deltas.
* Opening/building two domains concurrently does not require user-facing worktree switching and does not mix their writes.
* Local +A/-D counts exclude inherited ancestor changes, while upstream state remains inspectable.
* A non-conflicting Main change cascades through Branch/Twig/Leaf effective source; a conflicting resource pauses only the affected cascade and creates a structured Conflict.
* A descendant tombstone prevents a deliberately deleted inherited resource from reappearing after a later cascade.
* A tracked rename preserves lineage across a subsequent ancestor edit or surfaces an explicit ambiguity conflict.
* Leaf → Twig → Branch → Main promotion uses revision-pinned Change Requests, required approvals/checks and produces an auditable resulting revision.
* A reviewed source revision changing after approval invalidates/re-runs policy as configured rather than merging unreviewed edits.
* Conflict resolver supports manual three-way resolution and reviewed AI proposals with Reject, Accept without Merging and Accept and Merge semantics.
* Root enforcement blocks descendant override while an authorised owning-domain Root update cascades normally.
* A Subroot claims non-overlapping Roots, runs required review/checks, completes into the owning Root and releases its claims; Main cannot create Subroots.
* Freeze applies only to configured descendant scope, cannot be bypassed by ordinary clients, and early/automatic thaw follows permissions/audit rules.
* Files-backed managed folders/manifests validate on open and damaged derived state can be reconstructed without treating internal metadata as user source.
* GitHub-backed Branches remain native Git branches while Twig/Leaf/Subroot history survives through protected Stack companion metadata without appearing as normal Branches.
* Deleting the GitHub companion ref triggers deterministic recovery where retained objects permit it and otherwise reports exact unrecoverable state.
* Dual Files+GitHub mode records authority/sync checkpoints and surfaces concurrent divergence as SyncConflict instead of last-writer-wins.
* A mixed public/private project cannot publish a projection whose reachable tree/history includes private source or credentials.
* Raw Git operations that would corrupt Stack lineage/managed refs are blocked or require explicit advanced reconciliation.
* Project/domain permissions prevent a child from granting access beyond private ancestor/project policy.
* Revision-pinned CI/check results gate protected promotions and stale check results cannot satisfy a newer revision.
* A Release records immutable source revision/tag/artifacts/check state and package metadata remains linked to its source revision.
* GitHub mirror/backup status exposes the last verified checkpoint and restore does not claim success when objects are unrecoverable.
* Sites preview/production deployment and Open in Dev retain exact Stack ProjectID/DomainID/revision identity.
* Stack Issues can link to Boards without duplicating Boards as a divergent project-management database.
* Automations/webhooks receive typed stable-ID lifecycle events with observable delivery failure state.
* Public forks use independent identity/provenance and cannot expose private Stack-managed storage.
* Sweep previews/consolidates descendant work before removal; Prune is recoverable; Purge is separately privileged and blocked by active references.
* Autocommit is opt-in, does not run over unresolved conflicts/ignored or prohibited content, and distinguishes local commit from remote sync success.
* Large repositories/history use virtualised/lazy behaviour and missing LFS objects are reported instead of replaced silently.
* Metadata migration is versioned/idempotent and migration failure preserves recoverable prior state.
* Audit history covers source, permission, visibility, Root/Freeze/Subroot, review, release, prune/purge, mirror, autocommit and AI actions without logging secrets.
* Keyboard/screen-reader workflows cover domain navigation, diffs, conflict resolution and review.
* Windows, 9to1-OS and web preserve the same canonical Stack model while capability-negotiating local materialisation/Git support.
* When Home is absent at process start, Stack follows the global Install Home/bootstrap contract.
