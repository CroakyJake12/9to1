# State and Persistence

Haven keeps data local-first. Categories, owners and lifecycles:

## 1. Durable domain data — SQLite (`Haven.Infrastructure`)

Conversations, messages, groups, projects, lessons, planner items, calls,
Apps/modes (by stable ID), agents, instructions, capabilities, run history.
Accessed through repositories defined in `Application`; schemas change via
**forward-only numbered migrations** with fixtures for every prior schema and
a backup/integrity check before migrating. Never renumber persisted enums or
recycle stable IDs. Representative: `src/Haven.Infrastructure` SQLite
services; fixtures in `tests/Haven.Infrastructure.Tests`.

Agentic recovery data is also durable SQLite (migration 23):

- `workspace_versions.haven_sequence` — nullable sequence column backfilled
  from `rowid`, kept populated by the `trg_workspace_versions_sequence`
  insert trigger and indexed per workspace root.
- `agent_checkpoints` — one row per recorded checkpoint (id, optional
  conversation/container ids, `workspace_root`, label, `CheckpointMode`,
  `start_sequence`, `created_at`). Full restores take the before-content of
  each path's earliest mutation after `start_sequence`, ordered by sequence.
  Later before-content reflects intermediate edits, not checkpoint-time state.
  `UndoLastActionAsync` separately reverses only the most recent mutation.
  Recovery works in non-Git directories. Owned by `CheckpointRepository`.

## 2. User preferences — JSON files

| Store | File | Contents |
|---|---|---|
| `UserPreferencesService` | `{DataDir}/preferences.json` | Appearance + theme + accent override + font + avatar flags, model defaults, permissions, voice profiles. Atomic tmp+move writes; malformed JSON falls back to defaults. |
| `MotionPreferencesService` | `%LocalAppData%/Haven/ui-preferences.json` | Reduced-motion toggle. |

Personalisation fields are tolerant: unknown theme names resolve to Glow,
unknown accent palettes disable override, unknown fonts fall back to bundled
Montserrat.

### 2.1 Versioned settings — `{DataDir}/settings.json`

`VersionedAtomicSettingsStore`
(`src/Haven.Application/VersionedAtomicSettingsStore.cs`) persists named JSON
documents as one exportable manifest (`{version, exportedAt, settings}`) with
atomic tmp+move writes, a `.bak` fallback on corruption and export/import for
settings transfer. Keys defined so far (implementations in
`Haven.Infrastructure/Persistence` unless noted):

| Key | Implementing store | Contents |
|---|---|---|
| `models.fallback-order.v1` | `VersionedModelFallbackOrderStore` | Ordered model fallback keys, most preferred first. |
| `models.personalisation.v1` | `VersionedModelPersonalisationStore` | Shared personality defaults plus per-model entries; null personality members mean "use Haven defaults" and round-trip as explicit nulls; blank nicknames persist as null (= inherit). |
| `models.permissions.v1` | `VersionedModelPermissionStore` | Deny-rule model permission policy evaluated by `ModelPermissionEvaluator`. |
| `actions.default-providers.v1` | `VersionedDefaultProviderStore` (`Persistence/DefaultProviderStore.cs`) | Per-category default provider App key or `"ask"` (Always Ask). |
| `updates.preferences.v1` | `VersionedUpdatePreferenceStore` (`Updates/UpdateOrchestrator.cs`) | Background-check toggle and preferred release channel. |

The checkpoint policy (`CheckpointMode`) is engine-owned state on
`CheckpointService.Mode` (default `BeforeFileChanges`) and is not yet exposed
as a settings key — PARTIAL.

Spaces are also persisted through `IVersionedSettingsStore` by
`SpaceRegistry`; they are user content, not preferences. See
`docs/ARCHITECTURE_RULES.md` for state ownership rules.

## 3. Avatar assets — processed local files

`AvatarStore` stores centre-cropped images at `{DataDir}/avatars/user.png`
and `haven.png`. Preferences persist only the enabled flag; the path is a
stable well-known reference. Originals are never uploaded anywhere.

## 4. UI/session state — in-memory or session stores

Tab sessions, split-view geometry, workspace windows: owned by Desktop
services (e.g., `MainView.TabSessions.cs`, workspace session persistence).
Not a settings concern; may be rebuilt safely after a crash.

## 5. Caches and derived state

Model lists, retrieval indexes, generated-UI instances (`GenUiInstanceStore`)
are rebuildable caches or explicitly persisted stores with their own
contracts. Treat as disposable unless the owning service documents
persistence.

## Rules of thumb

- New durable product data → repository + migration in Infrastructure.
- New user setting → field on the existing preferences record + safe default;
  never a second settings store.
- New shared cross-surface policy or default (model routing, permissions,
  provider defaults, update behaviour) → contract in Application +
  `IVersionedSettingsStore` key named `<area>.<name>.v1`; never a bespoke
  settings file.
- New binary asset → process into a stable file under the data directory;
  never embed blobs in preferences.json.


## Canonical Task/Run recovery

`TaskExecutionCoordinator` owns one `TaskExecutionSnapshot` per canonical task.
`ContextId` links the actual conversation or owning context; `ExecutionId` is
that execution's original operation identity. A provider fallback changes the
attempt, never either identity. The existing Agent run history remains a
separate legacy invocation log, not another Task/Run authority.

`ITaskExecutionRepository` persists this snapshot through the owning adapter.
Each write proposes `PersistenceRevision + 1`; an acknowledged compare-and-swap
is required before observations or accepted-state changes are published.
SQLite's additive migration 28 binds the revision column to the JSON revision,
and preserves context, execution and creation identities. A stale write raises
`TaskExecutionRevisionConflictException`; callers must inspect current state
instead of blindly overwriting or replaying completed work. Browser adapters
must implement the same contract on the same model.

Accepted actions retain the actual owning-service acceptance separately from
a successful runtime return. Unknown, failed, superseded or replayed actions
cannot move the last valid accepted-action checkpoint. Physical workspace
checkpoints remain the existing `ICheckpointRepository` records; Task state
stores their identifiers only after checking context ownership.

The admission authority records actor/profile/authentication provenance but
retains the actual attempt lease in process. Permission scopes, route
observations and persisted receipt strings are not capabilities. A restart
cannot reconstruct a live grant from these records. A fresh authorized attempt
requires an actual old-runtime settlement witness and current authority.

One attempt lease spans finite provider/tool frames. The runtime owner retains
each original frame, including its enumerator and cleanup. Before fallback it
seals further frame admission, joins every original, then disposes that same
lease once. A terminal provider failure is eligible only through a live
runtime-issued observation of that exact frame and exception, followed by the
coordinator's acknowledged failure write. Cleanup faults and unacknowledged
siblings remain unresolved; generic failure text or a cancellation response is
not settlement.

UI projections are derived from acknowledged snapshots. Observer faults are
retained separately and cannot erase a durable write. The browser repository,
actual provider/runtime composition, tool-owner acceptance and original
cross-surface recovery controls must run before this framework is accepted as
connected in both web and Windows products.


Explicit task activation and long-lived original custody

OrdinaryConversation remains SendAsync's default. Injecting canonical task services never changes ordinary free/local Chat or its existing Computer, Browser, Automation, MCP, Plugin, Calendar, Workspace and model-access rules. A canonical continuation requires explicit CanonicalAgenticTask intent and actual configured admission/tool owners. The saved Agent task caller declares that intent. Spaces/Tasks callers must select it explicitly. Canonical task requests advertise only tools declared by the real typed owner; unsupported runtimes refuse before effects. Current typed Workspace support is a bounded foundation, and expansion to the other genuine owners remains mandatory unfinished work.

An exact healthy original attempt may release runtime custody only after its actual whole registration/frame/finally/issuer-lease settlement succeeds and the coordinator acknowledges the same terminal or successor CAS. TaskRunOriginalRetirementAcknowledgment has a nonpublic constructor and a self-identity check; copied or late acknowledgments and missing registry lookups refuse. Accepted provider-failure diagnostic originals remain retained under the runtime's explicit capacity policy. Unknown effect, failed/canceled original settlement, cleanup faults and CAS-lost writes never acquire a retirement acknowledgment.

If terminal persistence loses CAS after real original drain, completion retains the actual live settlement Task and authentic issued admission. An explicit retry rechecks fresh command authority, same owner/run/attempt, accepted action plan, steering and checkpoint semantics. A changed completion basis refuses rather than projecting old output as current completion. This live custody is never reconstructed from durable text, IDs, a rebuilt lease, or absence in a fresh process registry. Historical crash recovery remains explicitly unresolved until a genuine owner provides original activation/exit and effect provenance.


Saved Agent invocation binding and supported retry

`AgentTaskRuntimeService` consumes an internal `ChatOriginalAgentInvocation` issued by the actual shared Chat producer. It retains each returned outer iterator MoveNext/Dispose Task and records completion only from the same canonical completion acknowledgement after original drain. The singleton `ChatSessionService.CurrentCanonicalTask` is display state and cannot bind concurrent Agent invocations. PermissionRequired keeps the same canonical Task/Context/Execution suspended and produces no completed invocation receipt.

The existing `agent_runs.activity_json` carries an additive `CanonicalBindingVersion: 1` envelope and `CanonicalTask` display observation. Old activity arrays remain readable; adding a display binding wraps the actual old entries without inserting an activity. Unknown fields remain intact. Numeric Agent statuses zero through four are unchanged; Suspended is appended as five. This JSON never reconstructs an input producer, actor grant, runtime settlement or continuation receipt.

`RetryAsync` uses only a retained live private original and the narrowly supported source-approved never-started continuation. It preserves the same Agent row and canonical IDs and never falls back to `RunAsync` automatically. Historical/invoked/unknown recovery is explicitly unavailable. `RunAsync` remains an explicit new-work action. `HasOriginalUnstartedRetrySource` is deny-only UI availability; every retry still performs fresh owning policy and input checks. Parent/child delegation and accepted-checkpoint recovery remain separate unfinished production seams.


## Spaces and Dev composition boundaries

Spaces task presentations read the canonical `TaskExecutionSnapshot`; `Conversation.Id`
is the actual task context and never an account, profile or Space identifier. Reopening
a task or embedded Dev project resolves the existing Task/Run and saved
`DeveloperProjectReference` (workspace/project/root identity and revisions). It does
not clone a project or start a replacement task. `DeveloperTaskWorkspaceService`
uses the same workspace store, coordinator and typed Workspace action owner/runtime.
Files document identities come from the original Files resolver, not a view-local registry.

Direct typed Dev file edits use the same maintained `CheckpointService` instance
observed by the canonical coordinator. Inside the original admitted tool owner's
body, before physical `apply_change_set` dispatch, Dev observes the actual task
conversation/container and saved project root, awaits `EnsureBeforeMutationAsync`
under the same canonical execution ID, and records that actual checkpoint through
`RecordCheckpointAsync`. The checkpoint write advances this operation's expected
task revision; current original action/actor/workspace checks repeat against that
acknowledged revision before dispatch. Mutation history carries the same actual
conversation and container IDs, so shared restore also works in non-Git roots.

An explicit `CheckpointService.Mode == Off` remains the user's policy. Missing
producer/conversation configuration, unacknowledged saves (including a physical
save followed by a fault), or changed task/workspace/policy observations refuse
before file mutation. Dev retains and joins the actual checkpoint driver Tasks;
accepted-action markers never substitute a workspace checkpoint or authorize
effect replay. Runtime acceptance of this connection remains required.

Steering is an owning coordinator command with fresh actor checks. Pause and stop
require a retained private producer and its actual drain/CAS acknowledgement. A
source-issued observed-resume lease owns only its wait and detach acknowledgement;
its durable host retains the actual business iterator. View retirement seals callbacks
and joins its own work without cancelling that host or retiring the global Dev/Files
services. Agent observation retirement remains the distinct contract documented in
[Agent observation retirement](agent-observation-retirement.md). Snapshot fields,
availability results and lease result metadata do not recreate permission or recovery
authority. Unknown or historical originals remain unavailable.

The native composition proposals call `AddFilesNativeHost` followed by
`AddHavenOriginalNativeDevelopment` before the same provider is built, only when
the supplied original Home/resource registrations are complete. They retain the
same Dev, Task and Files singletons; a missing or partial Home tuple leaves these
routes unconfigured while ordinary routes remain available. Process shutdown must
retain the actual Dev/Files adapters and all canonical business owners, request
retirement, independently join their original Tasks, then dispose the provider and
acknowledge clean shutdown. Startup partial-acquisition cleanup is separate and
cannot acknowledge clean startup or exit.

These composition and presentation additions are source checkpoints. The actual
installed Home Windows startup connection/approved host tuple, configured workspace
trust, complete process-owner handoff and native/browser runtime acceptance remain
required receiving dependencies. A compatibility readiness check is not a rendered
frame, installed authority, successful tool effect or completed durable task.
