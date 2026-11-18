# Automations — graph authoring and durable runs — Automations and shared node editing; Home, Library and draft/active distinction (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1648 · v2 lines 7428–7428

## 6. Automations and shared node editing

### V2-B1649 · v2 lines 7430–7430

### UX27-AUTO-001 — Home, Library and draft/active distinction

### V2-B1650 · v2 lines 7432–7432

**B/D.** Home shows real active/waiting/failed/recent runs and upcoming triggers. Library lists canonical Automations with enabled state, active revision, last result and next trigger where actually calculable. Opening an item shows its graph/definition, not a disconnected dashboard clone. Create has a useful simple When/If/Then route and a real graph route over the same GraphID.

### V2-B1651 · v2 lines 7434–7434

Editing changes a Draft; active schedules continue using the pinned Active revision until validation/activation. The UI clearly labels draft versus active and exposes the diff/validation result. Saving a draft does not publish it or restart an existing Run.

### V2-B1652 · v2 lines 7436–7436

### UX27-AUTO-002 — Node insertion and connector gestures

### V2-B1653 · v2 lines 7438–7438

**B/D.** Right-click empty graph space opens searchable registered nodes at the invocation position. Typing filters by action/event/type and permissions; selecting inserts at that graph coordinate. Dragging a typed port shows compatible targets and a live connection preview; incompatible targets are visibly rejected. Escape cancels without leaving a partial connection.

### V2-B1654 · v2 lines 7440–7440

Preserve the specified Ctrl-click connector deletion, connection context-menu Delete and keyboard alternative. Removing a connector never deletes its nodes. Node selection/move, panning, marquee selection and text-field editing have distinct pointer states. One move/connection change is one undo transaction and survives save/reopen.

### V2-B1655 · v2 lines 7442–7442

### UX27-AUTO-003 — Run visibility and truthful execution

### V2-B1656 · v2 lines 7444–7444

**B/D.** Each run shows TriggerID/event, pinned definition revision, node states, inputs/outputs permitted for display, current wait/approval state, errors and causal/audit links. A pulsing node means the actual runtime is working/waiting in that state, not an animation simulating progress. Run status is not derived from whether the editor is open.

### V2-B1657 · v2 lines 7446–7446

Test execution uses explicit test inputs/sandbox settings where available and distinguishes dry-run from consequential execution. A node that sends, publishes or mutates external data retains its normal permission boundary during a test. No fake sample result may be shown as the outcome of the user’s real run.

### V2-B1658 · v2 lines 7448–7448

### UX27-AUTO-004 — Durable Wait, approval and restart

### V2-B1659 · v2 lines 7450–7450

**B.** Wait/Delay persists continuation state and releases active execution resources according to the runtime contract; it is not only an in-memory timer. Restart resumes from the saved node boundary with the same RunID/revision and does not repeat committed side effects. Approval-required work remains held until its actual approval, regardless of a due-time query.

### V2-B1660 · v2 lines 7452–7452

Verify suspend → stop process → reopen → resume → complete against the real persistent store. Reconcile the previously reported SuspendRunAsync/CompleteRunAsync same-ID risk in current source before accepting the lifecycle. A test that only round-trips continuation columns does not establish durable execution.

### V2-B1661 · v2 lines 7454–7454

### UX27-AUTO-005 — Schedule and enable/disable controls

### V2-B1662 · v2 lines 7456–7456

**B/D.** Schedule editing displays timezone, recurrence, next occurrences and ambiguity for daylight-saving transitions. Enable/disable affects future admissions according to an explicit policy; it does not silently terminate already running work or erase history. A missed trigger has a declared catch-up/skip policy, not uncontrolled replay after an outage.

### V2-B1663 · v2 lines 7458–7458

A cloud-capable workflow must not unnecessarily depend on the visible desktop window. Show the actual executor’s availability and last observation, and distinguish queued, admitted, running and completed. Do not create duplicate timers when the Automation page mounts twice.

### V2-B1664 · v2 lines 7460–7460

### UX27-AUTO-006 — Retry, cancellation and idempotency

### V2-B1665 · v2 lines 7462–7462

**B/D.** Retry identifies which failed boundary is retried and what already completed. A timeout with unknown effect enters reconciliation, not automatic re-execution under a new ID. Cancellation requests stop at supported boundaries and records any work that already happened. No “Cancelled” result may imply external effects were reversed without evidence.
