# Persistent local OpenCode teams

This is a one-shot native v2 adapter for the existing coordinator and its existing
ROOT-LIVE ledger, using workflow.transaction and claim. It installs no daemon,
second ledger or programme supervisor. Root retains global integration and guarded
SDK ownership; original cloud packets, worktrees, Thunder thread and evidence stay
in custody. Nothing in this change has launched a server, session or inference.

## Canonical scope and phases

Initial teams each own four surfaces:

| Team | Surfaces |
| --- | --- |
| platform | Home/Haven; CUI/frameworks; Accounts; Build delivery |
| workspaces | Spaces including Assistants; Voice & Vision; Build; Automations |
| productivity | Write; Present; Boards; Canvas |
| data-services | Data; Files; Planner; Connect Mail |

PHASES retains the full fourteen-phase handoff catalogue: (1) Home and frameworks,
(2) Spaces and Voice & Vision, (3) Build including Dev/Sites/Stacks workspaces,
(4) Automations, (5) Write/Present/Boards/Canvas/Data, (6) Files/services/accounts,
(7) Planner/Connect including Mail, (8) Cards, (9) Forms, (10) AI Studio,
(11) Picture/Wave/Motion, (12) Games/Play, (13) Mini Computer, and (14) all other
retained independently specified products including Link/Prototype/Paths/Replay. Create is a tile grouping for Picture/Motion/Wave, never a separate app.
These groups never reduce canonical scope or split shared Home/standalone apps.

Root invokes configure with a credential-free JSON catalogue: teams maps names
to three or four surfaces (initial teams retained; later teams allowed); phases
contains IDs1–14, full surfaces copied from PHASES, and requirements listing
existing ledger task IDs for exact implementation coverage. Scope cannot narrow.
First six priority phases run concurrently. Later phases require all priority and
earlier phases completed against current-source coverage, never rough percentages.
The integration revision comes from existing execution_custody.cloud-owning-v2.revision, validated against its retained source manifest candidate_revision; observed_revision is historical provenance and is not used for phase gates. Referenced tasks must be COMPLETED at that integration revision with retained PASS
implementation-coverage evidence. The evidence artifact JSON contains that exact
source_revision, applicable_scope_complete:true, and a nonempty requirements list
of requirement_id, implemented:true and source_evidence. Root must establish that
the manifest truly covers the applicable specification; flags alone are no proof.

Existing owner_implementation_window.acceptance_paused governs acceptance;
missing or true refuses it. Phase completion does not resume acceptance. Continue
genuine basic compilation and focused smoke checks using existing SDK admission.
Freeze Accepted estimates; retain full scope, CUI, native/install/release gates.

## Confirmed native support and current limits

Installed @opencode/cli is v2.1.0; exact upstream tag is
60c68d145a14127ce633e26aa5fdfb882a7baa6d. Inspected files are
packages/protocol/openapi.json, packages/app/src/shell/tabs/schema.ts,
packages/app/src/shell/titlebar/tab-strip.tsx, packages/app/src/shell/routes/session.ts
and packages/util/src/encode.ts. Native webview session tabs are supported;
separate team-group schema is absent. Keep one genuine titled root leader session
per team as a native tab; Luna workers are independent native root sessions with genuine team metadata. Do not fake tabs or UI.

The existing service was stopped during inspection. Provider IDs, entitlement,
actual sessions, visible tabs and callback adoption are NOT VERIFIED. No model
labels may substitute for API IDs. Mercury/Inception and paid fallbacks are forbidden.

## One-shot commands

All commands use --state EXISTING_LEDGER --expect CURRENT_GENERATION --server
http://127.0.0.1:ACTUAL_PORT before the subcommand. Server must already exist;
the adapter never starts it. Reread generation after every operation.

- configure CATALOGUE_JSON stores the full policy in the same ledger.
- bootstrap NAME BINDING_JSON creates or reconciles a native session.
- bind NAME BINDING_JSON binds an already observed native session.
- pause resumed enables new eligible delivery; pause paused stops new delivery.
- dispatch --jobs JOBS_JSON --capacity CURRENT_COORDINATOR_ALLOCATION executes once.
- tick, prepare, send and reconcile expose the same individual durable stages.
- tabs --server-key ACTUAL_REGISTERED_KEY prints native session links.

The existing coordinator invokes dispatch at each scheduling callback. Jobs are
records with name, text, exact paths and kind (implementation/compile/smoke/
supervision/acceptance). Dispatch ticks, reconciles exact native receipts,
recovers PREPARED work, admits and explicitly queues eligible prompts, then
records a checkpoint. Sol leaders activate every300seconds; bound Luna workers
also have clocks for explicit jobs; Astra activates every1800seconds. Busy or
missed activations coalesce to one pending item. No extra timer is installed;
actual existing-coordinator callback adoption remains NOT RUN.

Capacity comes from the existing coordinator, never an inferred resource grant.
The adapter counts active native sessions and its queued/prepared/uncertain
reservations. Source paths must be inside approved binding scopes and must pass
the existing exclusive claim guard. Heavy SDK/native work still requires the
original immediately checked execution-admission runner and allocations.

## Binding, exact handoff and provider proof

Binding JSON fields: role (leader/worker/supervisor), exact requested label
(GPT-6.1 Sol/GPT-6 Luna/GPT-6 Astra), provider_family allowed by existing ledger,
entitlement:VERIFIED, paid_fallback:false, actual model:{id,providerID,variant?},
valid session_id, phase, exact project_id, location:{directory}, approved
allowed_paths, and evidence plus workspace_identity regular-file identities.
Each file identity contains path, kind, bytes, mode and sha256. Workers also have team and leader_session_id, matching their bound leader. Every worker is a root session with native metadata 9to1_team and 9to1_leader_session matching that association. Leaders and Astra are root sessions. Unresolved deliveries prevent rebinding.

Provider evidence is credential-free JSON with schema
9to1.native-model-entitlement/v1, verified_by:owner or existing-coordinator,
numeric UTC verified_at and expires_at spanning at most1800seconds, exact label,
model, provider_family, entitlement, paid_fallback, and actual native_name.
Every admission rehashes and parses this proof, checks freshness and exact mapping,
then checks catalog ID/provider/name/enabled/active. Root/coordinator must verify
actual entitlement externally; an enabled catalog entry or self-asserted label is
insufficient. Never retain credentials, headers or raw model configuration.

Workspace identity JSON contains project_id, location, revision, tree, generation,
directory_identity:{device,inode}, claimed_paths matching allowed_paths, and
root_writer_handoff_ack:true. Bootstrap and every admission validate immutable
manifest bytes, actual directory identity and actual Git HEAD/tree. This requires
a validated existing isolated worktree and exact-revision ACK; it does not create
worktrees or transfer Root's ownership. Existing claims still refuse conflicts.
Native children inherit their parent location, so this implementation deliberately creates Luna workers as independent root sessions, each in its own validated isolated Git worktree. Binding refuses a worker directory shared with any existing binding. Exact logical file scopes still pass the original exclusive claim guard. Native metadata preserves actual team/report association without pretending parentID gives isolation.

Bootstrap persists a durable UNKNOWN creation intent with supplied valid client
session ID before POST /api/session. Restart first GETs that exact ID and binds
only after live model/project/location/team metadata identity matches. An uncertain
creation is never blindly reposted. Native create supports title, model, location,
metadata and id; model and workspace validation precede the mutation. Worker creation does not set parentID.

## Delivery, recovery and budget

Prepare pins the immutable binding identity and stored session ID. Send rechecks
that binding plus live native model/project/location, workspace generation,
fresh provider proof, pause, capacity and exclusive ownership. It persists UNKNOWN
before POST /api/session/{sessionID}/prompt with stable msg_ ID, delivery:queue
and resume:true. It never uses steer or interrupt. Only a matching native user
receipt with exact session/message IDs and delivery:queue becomes QUEUED.

Reconcile reads the exact pending inbox then exact delivered message. Absence
from inbox is not completion or permission to retry. DELIVERED proves only input
delivery, never completed implementation, a build or acceptance. Unknown requests
remain durable and require supported reconciliation; never clear them to resend.
Original tasks and evidence remain the work outcome authority.

The report NAME TASK_ID ARTIFACT_IDENTITY command retains an exact task/revision-linked factual result without waking any model. Luna reports target their bound Sol leader; Sol summaries target Astra. Dispatch queues pending reports only at the next target cadence (300s/1800s), with real report IDs in native prompt metadata. Upstream bindings require approved report_paths within their owned coordinator scope. Original task/evidence outcomes remain authoritative; a report never marks implementation or acceptance complete.

Astra uses coordinator-owned supervision/report paths; it cannot take worker
implementation files because the same exclusive guard applies. Its prompt gives
a300second work budget and exact checkpoint deadline. The first dispatch-attempt timestamp and deadline are persisted before the side effect and never reset during receipt reconciliation. Dispatch records actual
native active status after the deadline as OVERRUN_CHECKPOINT_REQUIRED or
NOT_ACTIVE_NO_COMPLETION_CLAIM. This is cooperative: inspected prompt API has no
hard wall-clock budget. Existing coordinator must arrange an ordinary queued
checkpoint after overrun. No forced termination or enforceable runtime budget is
claimed; hard enforcement remains a gate if the owner requires it.

Native links use the confirmed base64url registered server-key route and actual
bound root session IDs. Follow each in the native webview to open genuine tabs;
link generation or API session creation alone does not prove visible persistence.

Twenty-six focused offline unit tests use fake API/Git observations and temporary
files in /dev/shm. They cover cadence, coalescing, phase scope/pause, model mapping,
owner conflict, live location changes, rebind refusal, crash-safe bootstrap,
uncertain delivery, normal/lost-receipt stable Astra deadlines, real separate temporary worker files and exclusive claims, next-cadence upstream reports and one-shot queue contracts. These are not actual native
runtime passes, provider entitlement checks or independent product acceptance.

Provider-proof renewal is currently BLOCKED: the inspected native model/provider catalog does not prove subscription entitlement. No supported provider-specific read-only entitlement observer has been verified here. The existing coordinator needs a verified read-only actual-provider observation to renew the trusted expiring proof; do not ask the owner to handwrite fabricated attestations or infer access from prices/model names. Until that observer and actual entitlement are established, bootstrap/activation remains blocked or expires fail-closed. No paid probe is authorised.

Before any new native session POST, local binding preflight validates approved catalogue/name/role, unique supervisor and session assignment, actual bound worker team, independent worktree, explicit valid scope, phase eligibility and existing exclusive claims. New creation fails while team delivery is paused or unknown; reading and binding existing sessions remains allowed. If existing coordinator catalogue capacity is provided, its reservation limit also applies. Invalid names cause zero session POSTs. This is validated offline; native creation remains NOT VERIFIED.

## Optional foreground cadence invoker

Choose exactly one invoker: the existing coordinator callback OR foreground watch.
Both CLI paths share the same nonblocking state-bound invoker lock, so a second
watch/callback refuses. Direct Python users must preserve that same exclusive
invoker rule. No daemon, systemd unit or milestone supervisor is installed.

After actual native capability, model entitlement and existing source ownership
are verified, the ready command is:

```sh
python3 -B eng/development/opencode_teams.py --state EXISTING_LEDGER --expect CURRENT_GENERATION --server http://127.0.0.1:ACTUAL_PORT watch --jobs EXISTING_JOBS_JSON --capacity-file EXISTING_CAPACITY_JSON --interval 30 --stop-file PRIVATE_STOP_FILE
```

The existing coordinator supplies the jobs JSON list and current capacity snapshot
JSON object with capacity. Watch rereads ledger generation, jobs and capacity on
every cycle, invokes the same dispatch, then waits30seconds. No extra inference
occurs without pending cadence/jobs. Busy/UNKNOWN work keeps the same durable
coalescing/reconciliation rules; uncertain work is never blindly retried. The
initial required --expect argument is not reused between cycles: every dispatch
uses its freshly observed ledger generation and CAS still refuses competing writes.

Pause/unknown state, the explicit stop file or SIGINT exits foreground watch
without interrupting native workers. --max-cycles N supports bounded operation.
Offline tests inject clock/wait functions (no real sleeps), verify bounded cycles,
singleton refusal, pause/explicit stop without POST and SIGINT preserving queued
work. This command has NOT been started here; actual runtime remains unverified.
