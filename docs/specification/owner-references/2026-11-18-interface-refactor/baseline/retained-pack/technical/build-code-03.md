# Build Code — development environment — Build; One canonical project across editing surfaces (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2773 · v2 lines 14808–14808

## Build

### V2-B2774 · v2 lines 14810–14810

### UX27-TEST-076 — One canonical project across editing surfaces

### V2-B2775 · v2 lines 14812–14812

**Requirements:** UX27-BUILD-001, UX27-BUILD-002, UX27-BUILD-004 · **Status:** NOT RUN

### V2-B2776 · v2 lines 14814–14814

**Given:** Existing code/CUI/site project with language diagnostics.

### V2-B2777 · v2 lines 14816–14818

**When:**
1. Edit source, visual properties and hierarchy.
2. Navigate diagnostics and undo a refactor.

### V2-B2778 · v2 lines 14820–14823

**Then:**
- Visual/code views update one source model.
- Diagnostics point to the correct revision/range.
- Generated ordinary UI remains canonical CUI, with no shadow project.

### V2-B2779 · v2 lines 14825–14825

### UX27-TEST-077 — Failed build cannot present a stale success

### V2-B2780 · v2 lines 14827–14827

**Requirements:** UX27-BUILD-003 · **Status:** NOT RUN

### V2-B2781 · v2 lines 14829–14829

**Given:** Known runnable project followed by an intentional compile failure.

### V2-B2782 · v2 lines 14831–14833

**When:**
1. Run, then introduce the failure and Run again.
2. Inspect preview, exit code, diagnostics and Stop.

### V2-B2783 · v2 lines 14835–14838

**Then:**
- Failed build is reported.
- Any retained old preview is explicitly stale, not the new successful candidate.
- Stop affects the owned process only; debug/test adapters remain genuine.

### V2-B2784 · v2 lines 14840–14840

### UX27-TEST-078 — Conflicts and publishing have distinct boundaries

### V2-B2785 · v2 lines 14842–14842

**Requirements:** UX27-BUILD-005, UX27-BUILD-006, UX27-BUILD-007 · **Status:** NOT RUN

### V2-B2786 · v2 lines 14844–14844

**Given:** Disposable branch conflict and site staging deployment.

### V2-B2787 · v2 lines 14846–14848

**When:**
1. Inspect base/local/incoming changes.
2. Exercise the authorised review choice; preview site and then publish explicitly.

### V2-B2788 · v2 lines 14850–14853

**Then:**
- Review/merge/push/deployment statuses are distinct.
- No destructive default or publish-on-preview.
- Agent-authored repairs receive appropriate separate review.
