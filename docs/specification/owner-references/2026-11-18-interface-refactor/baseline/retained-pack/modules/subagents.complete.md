# Subagents, delegation and task trees

Module ID: `subagents` · 9to1 Development Specification v3

## Prerequisites

- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.

## Overview

Subagents are bounded child executions within a canonical task tree. They expose useful progress, blockers, evidence and outputs without disclosing private internal reasoning or creating independent permission authority.

Current owner overrides: V3-OWNER-008.

## Layouts & visible states

- **V3-SUBAGENTS-LAY-01** — Show parent/child relationships, task objective, configured identity/route, state, scope, budget and outputs. A child detail view provides high-level work events and actionable blockers.
- **V3-SUBAGENTS-LAY-02** — Use real progress/task status; do not invent completion percentages, measured timings or successful tool results.

## Features & behaviour

- **V3-SUBAGENTS-FEA-01** — Delegate scoped context and only the intersection of current parent grants. Enforce depth, concurrency, budget and capability limits.
- **V3-SUBAGENTS-FEA-02** — Support structured joins, result aggregation, cancellation/pause propagation and partial-failure handling. A child’s failure must not erase completed sibling artifacts.
- **V3-SUBAGENTS-FEA-03** — Relevance-based Assistant participation and persistent Teams are separate from anonymous worker delegation; preserve canonical identities when configured collaborators are used.

## Canonical entities & persistence

- **V3-SUBAGENTS-ENT-01** — Persist parent TaskID/RunID, child IDs, delegation inputs, scope/version, status events, outputs and join receipts.
- **V3-SUBAGENTS-ENT-02** — Reconnect to existing child runs after view/runtime restart without launching duplicate work or replaying external actions.

## Permissions & recovery

- **V3-SUBAGENTS-PER-01** — Revocation and budget changes propagate through the tree. An exhausted budget cannot be hidden by spawning another child or bypassing a required review.
- **V3-SUBAGENTS-PER-02** — Expose retry scope and uncertain external results; retry only safe or idempotent work with renewed grants when needed.

## Acceptance checks

- **V3-SUBAGENTS-ACC-01** — Test concurrent children, mixed success/failure, waiting approval, cancelled parent, scope revocation and resumed inspection.
- **V3-SUBAGENTS-ACC-02** — Confirm outputs open the canonical owning artifacts and the activity view contains no private chain-of-thought.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Subagents, delegation and task trees — Subagents and parallelisation; Terminology and boundary (part 01) — `technical/subagents-01.md` (1,836 words; V2-B1963, V2-B1964, V2-B1965…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Subagents, delegation and task trees — Subagents and parallelisation; Terminology and boundary (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1963 · v2 lines 11363–11363

Subagents and parallelisation

### V2-B1964 · v2 lines 11366–11366

Terminology and boundary

### V2-B1965 · v2 lines 11369–11369

Subagent is the Dulche Runtime term for a child execution unit created by a request or another Subagent to delegate work and/or execute independent work in parallel. Subagents are execution primitives, not persistent Dulche Agents and not Den-backed Agent definitions. The public API MUST use the names Subagent and Dulche.Subagent for this capability so callers cannot confuse subagent parallelisation with persistent Agents.

### V2-B1966 · v2 lines 11372–11372

A Subagent MAY execute one or more model/tool steps and MAY create nested Subagents within inherited limits. Every Subagent belongs to exactly one RootRequestID and, except for root execution, exactly one parent request or parent Subagent. Detached/orphan Subagents are outside the current contract; child work MUST remain causally attached to its parent tree.

### V2-B1967 · v2 lines 11375–11375

Subagent definition

### V2-B1968 · v2 lines 11378–11399

Subagent Worker = new Subagent {
    ParentRequestID = requestID;
    Prompt = "Perform the delegated work";
    Model = Auto;
    Endpoint = Auto;
    Context = SubagentContext.Inherit;
    Tools = Inherit;
    Permissions = Inherit;
    FailurePolicy = SubagentFailurePolicy.Collect;
    Budget = new SubagentBudget {
        MaxTokens = ...;
        MaxTime = ...;
        MaxSteps = ...;
        MaxToolCalls = ...;
        MaxCost = ...;
    };
    Limits = new SubagentLimits {
        MaxChildren = ...;
        MaxParallelChildren = ...;
        MaxDepth = ...;
    };
};

### V2-B1969 · v2 lines 11402–11402

Constructing a Subagent object MUST NOT execute work. Construction only defines an inspectable configuration object. Execution begins only when submitted through Dulche.Subagent, preventing object construction from causing hidden model/tool/external side effects.

### V2-B1970 · v2 lines 11405–11405

Canonical API

### V2-B1971 · v2 lines 11408–11421

SubagentHandle Handle = Dulche.Subagent.Start(Worker);
SubagentHandle[] Handles = Dulche.Subagent.StartParallel([WorkerA, WorkerB, WorkerC]);
Dulche.Subagent.Get(subagentID);
Dulche.Subagent.GetTree(rootRequestID);
Dulche.Subagent.ListChildren(parentID);
Dulche.Subagent.Wait(subagentID);
Dulche.Subagent.WaitAll(subagentIDs);
Dulche.Subagent.WaitAny(subagentIDs);
Dulche.Subagent.Pause(subagentID);
Dulche.Subagent.Resume(subagentID);
Dulche.Subagent.Stop(subagentID);
Dulche.Subagent.Restart(subagentID);
Dulche.Subagent.Checkpoint(rootRequestID);
Dulche.Subagent.Restore(checkpointID);

### V2-B1972 · v2 lines 11424–11424

Start creates one child and returns its handle after identity/state allocation. StartParallel submits the supplied definitions as sibling children under the same parent and returns one handle per child in input order. StartParallel grants permission for concurrent scheduling; it does not guarantee simultaneous execution when endpoint, provider or resource limits require queueing.

### V2-B1973 · v2 lines 11427–11427

Wait returns that child’s terminal Result. WaitAll returns one terminal outcome per requested child and MUST preserve child identity even when one or more children fail. WaitAny returns when the first requested child reaches a terminal state and identifies that child. Join operations MUST NOT flatten or discard per-child errors, blockers, logs, token usage, model provenance or tool records.

### V2-B1974 · v2 lines 11430–11430

Identity and state

### V2-B1975 · v2 lines 11433–11433

Every Subagent MUST expose SubagentID, ParentSubagentID?, ParentRequestID, RootRequestID, SessionID, EndpointID, Depth, State, CreatedAt, StartedAt?, FinishedAt?, Model and its effective tool/permission/budget configuration. IDs MUST be stable for the lifetime/history of that execution and MUST NOT be derived from display names, array position or queue position.

### V2-B1976 · v2 lines 11436–11436

Subagent State MUST distinguish at least Created, Queued, Running, Waiting, Pausing, Paused, Completed, Failed, Blocked, Cancelled and TimedOut. Waiting means the Subagent is suspended awaiting dependencies/children and MUST NOT retain an execution slot that prevents those dependencies from running.

### V2-B1977 · v2 lines 11439–11439

Scheduling and parallel execution

### V2-B1978 · v2 lines 11442–11442

Ordinary requests on one endpoint remain serial by default as defined by the Runtime queue contract. Subagents do not silently change that rule. A parent waiting on a child that requires the same endpoint MUST yield its execution slot so the child can run; this is mandatory anti-deadlock behaviour.

### V2-B1979 · v2 lines 11445–11445

Sibling Subagents assigned to different eligible endpoints MAY execute concurrently subject to resource, provider and configured concurrency limits. Siblings assigned to the same endpoint execute serially unless that endpoint explicitly advertises concurrent execution and the request enables the supported parallel mode.

### V2-B1980 · v2 lines 11448–11448

Model = Auto and Endpoint = Auto permit the Runtime scheduler to select an eligible model/endpoint using declared capabilities, permission/privacy constraints and resource policy. An explicitly selected Model or Endpoint MUST NOT be silently replaced merely to increase parallelism; inability to satisfy the explicit selection returns a structured scheduling/capability failure.

### V2-B1981 · v2 lines 11451–11451

MaxParallelChildren is a hard upper bound on concurrently Running children beneath the configured parent scope. Children above that limit remain Queued. Runtime-wide endpoint/resource limits MAY further reduce actual concurrency but MUST NOT increase it above the declared limit.

### V2-B1982 · v2 lines 11454–11454

Context inheritance

### V2-B1983 · v2 lines 11457–11457

Context inheritance is explicit. Supported policies are None, Inherit and Selected(...). Inherit snapshots the authorised parent context at spawn time; it does not create unsynchronised mutable shared conversation state. Selected(...) copies only the declared messages/context objects/references. None starts without parent conversational context except identity/control metadata required by Runtime.

### V2-B1984 · v2 lines 11460–11460

A child Result becomes visible to the parent through an explicit join/result path or another explicitly authorised shared data object. Child completion MUST NOT silently rewrite earlier parent messages or a sibling’s context. Multiple children MUST NOT concurrently mutate the same session history without an explicit revision/conflict mechanism.

### V2-B1985 · v2 lines 11463–11463

Permission and tool inheritance

### V2-B1986 · v2 lines 11466–11466

A Subagent MUST NOT acquire more authority than its parent/caller. Effective child authority is the intersection of the caller’s current authority, the parent’s effective authority and the scopes requested for the child. The same narrowing rule applies to tools, filesystem/data scopes, external integrations, network access, credentials and action approvals.

### V2-B1987 · v2 lines 11469–11469

Permissions = Inherit and Tools = Inherit copy only currently effective parent scopes. Expired/revoked grants are not inherited and no new approval is created by spawning a child. An action that requires approval in the parent context still requires approval when attempted by a child. Subagent creation MUST NOT provide privilege escalation or approval bypass.

### V2-B1988 · v2 lines 11472–11472

Hierarchical budgets

### V2-B1989 · v2 lines 11475–11475

Subagent budgets are hierarchical. Descendant consumption contributes to the ancestor/root aggregate for tokens, time, steps, tool calls and cost. A child MAY request stricter limits than its parent; it MUST NOT enlarge any hard parent/root limit. Parent and root budget exhaustion prevents further descendant work from starting and stops or blocks active descendants at safe boundaries according to the relevant resource type.

### V2-B1990 · v2 lines 11478–11478

SubagentBudget MUST support MaxTokens, MaxTime, MaxSteps, MaxToolCalls and MaxCost where those quantities are enforceable. Runtime resource policy MAY additionally constrain CPU, GPU, RAM, VRAM and endpoint occupancy. Provider-unavailable cost/token telemetry MUST use the Runtime’s existing null/Na rules and MUST NOT be treated as zero consumption.

### V2-B1991 · v2 lines 11481–11481

Actual descendant consumption MUST be attributed to both the child and its ancestor aggregates without double-counting the same invocation. If the system reserves budget before dispatch, reservation and settlement MUST be visible separately from actual settled usage.

### V2-B1992 · v2 lines 11484–11484

Depth, child and concurrency limits

### V2-B1993 · v2 lines 11487–11487

SubagentLimits MUST support MaxChildren, MaxParallelChildren and MaxDepth. MaxChildren counts direct children created by the configured execution scope. MaxDepth is measured from the root request, whose depth is 0; its direct Subagents are depth 1. Creating a child that would exceed a hard limit MUST fail before child execution begins.

### V2-B1994 · v2 lines 11490–11490

The Runtime MUST also enforce implementation-wide safety ceilings even when the caller requests larger values. The effective limit is the minimum of caller/request limits, inherited parent limits and Runtime/provider limits. Limit failures return stable structured codes including MaxChildrenExceeded, MaxDepthExceeded and ConcurrencyLimitExceeded.

### V2-B1995 · v2 lines 11493–11493

Failure aggregation and join policy

### V2-B1996 · v2 lines 11496–11496

Parallel sibling execution MUST retain independent terminal outcomes. One child failure MUST NOT erase successful sibling results. The parent/join operation receives a structured collection containing, for every child, SubagentID, terminal State, Result?, Errors?, Blockers?, FinishReason and relevant usage/telemetry.

### V2-B1997 · v2 lines 11499–11499

SubagentFailurePolicy MUST support at least Collect, FailFast, RequireAll and BestEffort. Collect waits for requested children and returns every outcome. FailFast requests cancellation of still-unfinished siblings after the first terminal failure/blocker while retaining already completed results. RequireAll treats any non-successful required child as a failed join. BestEffort permits the parent to continue with successful children while preserving explicit failed/blocked/cancelled outcomes.

### V2-B1998 · v2 lines 11502–11502

Failure policy changes orchestration/join behaviour only; it MUST NOT suppress logs, errors, blockers or completed side effects. External actions already completed by a failed/cancelled child are not automatically rolled back unless the action itself defines rollback semantics.

### V2-B1999 · v2 lines 11505–11505

Cancellation, pause and restart propagation

### V2-B2000 · v2 lines 11508–11508

Stopping a root request MUST stop all unfinished dependent descendants. Stopping a Subagent MUST stop its unfinished descendants but MUST NOT automatically stop its parent or independent siblings; the parent receives the child’s Cancelled outcome and applies its configured failure policy.

### V2-B2001 · v2 lines 11511–11511

Pausing a parent MUST prevent it from dispatching new child work. Running dependent children follow the parent’s explicit pause policy: PauseChildren requests safe-boundary pause, LetChildrenFinish permits already-running children to complete, and StopChildren cancels them. If no policy is supplied, PauseChildren is the default.

### V2-B2002 · v2 lines 11514–11514

Restart creates a new attempt for the targeted Subagent using the specified/current authorised context and preserves the prior attempt in history. Restart MUST NOT replay already completed consequential tool/external actions automatically. Replacement/cancellation of an ancestor MUST invalidate stale descendant approvals and provisional output.

### V2-B2003 · v2 lines 11517–11517

Persistence and recovery of subagent trees

### V2-B2004 · v2 lines 11520–11520

Dulche Den MUST persist the execution-tree metadata required to inspect and recover Subagent work: stable identities, parent/child relationships, request/attempt revisions, effective configuration, states, checkpoints, results, blockers/errors, tool/action records and durable side-effect evidence. Runtime remains the execution authority; Den is the durable record.

### V2-B2005 · v2 lines 11523–11523

Checkpoint(rootRequestID) creates a durable tree checkpoint for all checkpointable active descendants. Restore(checkpointID) reconstructs the tree from the latest compatible durable state. Exact continuation is allowed only when the underlying model/backend/tool supports it; otherwise Restore MUST expose that it is a continuation/restart from checkpoint rather than claiming byte-for-byte execution resumption.

### V2-B2006 · v2 lines 11526–11526

On crash/restart, completed external/tool side effects MUST be reconciled from durable records before any step is retried. A restored tree MUST NOT repeat a non-idempotent completed action merely because the parent process lost volatile state. Uncertain side-effect state becomes a Blocker requiring reconciliation or approval.

### V2-B2007 · v2 lines 11529–11529

Action Graph and telemetry

### V2-B2008 · v2 lines 11532–11532

Action Graph MUST render Subagent execution as an explicit causal tree/graph. Each Subagent is a distinct node/branch linked to its parent and MAY contain model calls, tool calls, approvals, outputs, child Subagents, blockers and errors. Parallel siblings MUST be visually distinguishable as concurrent branches rather than flattened into a fabricated sequential order.

### V2-B2009 · v2 lines 11535–11535

Subagent graph nodes MUST expose at least SubagentID, state, parent/root identity, model/endpoint, start/end/duration, token/usage data, tools/actions, errors/blockers and child count subject to permissions/redaction. Restart-from-here on a Subagent node creates a new attempt/branch under the existing historical tree and MUST NOT rewrite the original branch.

### V2-B2010 · v2 lines 11538–11538

Runtime FullLog MUST retain causal parent IDs and per-execution sequence numbers. Parallel/nested timing MAY overlap; aggregate timing MUST NOT sum overlapping spans as if they were serial. Token/model/tool aggregation MUST use the existing no-double-counting rules for parent and child invocations.

### V2-B2011 · v2 lines 11541–11541

Structured failures

### V2-B2012 · v2 lines 11544–11544

Subagent operations add stable structured failures including SubagentNotFound, MaxChildrenExceeded, MaxDepthExceeded, ConcurrencyLimitExceeded, BudgetExceeded, DependencyFailed, CheckpointUnavailable and RestoreConflict. These follow the Runtime error contract and MUST NOT rely on free-form string parsing.

### V2-B2013 · v2 lines 11547–11547

Acceptance requirements

### V2-B2014 · v2 lines 11550–11563

Subagent support is implementation-complete only when automated/representative acceptance coverage verifies all of the following:
* One parent spawning and joining one child with preserved parent/child identity.
* StartParallel with multiple siblings on different endpoints demonstrating actual concurrent execution where resources permit.
* Same-endpoint child dependency without deadlock and without silently enabling unsupported backend parallelism.
* Context None/Inherit/Selected isolation, including no sibling context leakage.
* Permission/tool inheritance cannot exceed parent/caller authority and still triggers required approval.
* Nested descendants obey MaxDepth, MaxChildren and MaxParallelChildren exactly.
* Hierarchical token/time/step/tool/cost budgets stop or block descendants at the correct boundary without double-counting usage.
* Collect, FailFast, RequireAll and BestEffort preserve the documented per-child outcomes.
* Root/child stop and parent pause propagation follow the documented dependency semantics.
* Checkpoint + process restart + Restore reconstructs the tree without replaying a completed non-idempotent side effect.
* Action Graph renders parent/child/nested/parallel branches with stable IDs and correct causal relationships.
* Telemetry preserves overlapping parallel spans and model/token/tool attribution without parent/child double counting.
* Subagent API failures return the documented structured error codes.
