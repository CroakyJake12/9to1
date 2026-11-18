# Automations — graph authoring and durable runs — Automations; Draft versus active graph and UI views (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2752 · v2 lines 14744–14744

## Automations

### V2-B2753 · v2 lines 14746–14746

### UX27-TEST-072 — Draft versus active graph and UI views

### V2-B2754 · v2 lines 14748–14748

**Requirements:** UX27-AUTO-001, UX27-AUTO-002 · **Status:** NOT RUN

### V2-B2755 · v2 lines 14750–14750

**Given:** One active workflow and a modified draft.

### V2-B2756 · v2 lines 14752–14755

**When:**
1. Edit via simple and graph modes.
2. Change a node/edge, undo and validate.
3. Inspect which revision a trigger uses.

### V2-B2757 · v2 lines 14757–14760

**Then:**
- Both editors share one graph definition.
- Invalid connections have useful feedback.
- Draft edits do not silently replace the active executable revision.

### V2-B2758 · v2 lines 14762–14762

### UX27-TEST-073 — Durable Wait across process restart

### V2-B2759 · v2 lines 14764–14764

**Requirements:** UX27-AUTO-003, UX27-AUTO-004 · **Status:** NOT RUN

### V2-B2760 · v2 lines 14766–14766

**Given:** Real SQLite store and a workflow with a committed side effect followed by Wait.

### V2-B2761 · v2 lines 14768–14771

**When:**
1. Run to Wait; stop owned test process.
2. Reopen, resume and complete the same RunID.
3. Retry a terminal receipt.

### V2-B2762 · v2 lines 14773–14776

**Then:**
- Continuation resumes at the saved boundary without rerunning the committed effect.
- Suspend/complete does not collide on identity.
- Actual node outputs and Run history persist.

### V2-B2763 · v2 lines 14778–14778

### UX27-TEST-074 — Approval is not inferred from due time

### V2-B2764 · v2 lines 14780–14780

**Requirements:** UX27-AUTO-004, UX27-AUTO-006 · **Status:** NOT RUN

### V2-B2765 · v2 lines 14782–14782

**Given:** Due run waiting for a required permission/approval.

### V2-B2766 · v2 lines 14784–14786

**When:**
1. Execute scheduler sweep and retry the run without approval.
2. Then provide approval through the authorised test path.

### V2-B2767 · v2 lines 14788–14791

**Then:**
- No unapproved side effect occurs.
- Held state remains visible until real approval.
- One eligible continuation proceeds; retries remain idempotent/reconciled.

### V2-B2768 · v2 lines 14793–14793

### UX27-TEST-075 — Timezone recurrence and missed admission

### V2-B2769 · v2 lines 14795–14795

**Requirements:** UX27-AUTO-005 · **Status:** NOT RUN

### V2-B2770 · v2 lines 14797–14797

**Given:** Scheduled workflow spanning a clock-change boundary with declared catch-up policy.

### V2-B2771 · v2 lines 14799–14801

**When:**
1. Inspect next occurrences.
2. Disable future admission, restart scheduler and later re-enable.

### V2-B2772 · v2 lines 14803–14806

**Then:**
- Timezone/ambiguity is visible and admission follows the configured policy.
- No duplicate timer/trigger on reopening UI.
- Disabling future work does not falsely undo completed or running effects.
