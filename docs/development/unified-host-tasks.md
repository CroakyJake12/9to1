# Unified 9to1 host implementation tasks

Persistent queue: [GitHub issue #35](https://github.com/CroakyJake12/9to1/issues/35). This file mirrors that child of master #21. Source clauses are incorporated in the existing CURRENT-REQUIREMENTS.md and source manifest; this is an execution record, not another specification.

## Current authorised integration — 16 October 2026
Status: **WORKING**, not whole-suite acceptance. Target: **17–18 October 2026, Europe/London**.

This is a child of the existing [master queue #21](https://github.com/CroakyJake12/9to1/issues/21), not a replacement task authority. The saved [V2 specification](https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit) now makes Browse the primary application branded 9to1; the previous main shell's functions are merged into it. Web/search and actual native CUI app surfaces share tabs. All actual applications retain standalone executables sharing canonical resources/services. Preserve the full remaining scope; 9to1OS remains separately staged.

Source read-back: native revision `AHj4eMQi_FaewmbEiK6tLlKz84Cg1W08unRVHfHpzaVmW0UIFui6lhC7E38AItSGnwT_41zWogIYC7dXhlaxOeSBcR7WYMgP5IPnfudwisE`; modified 2026-10-16T19:43:06.150Z. Reconcile existing repository mirror and historical source records without pretending old evidence validates new requirements.

Root Codex owns shared host contracts, integration and independent acceptance. Existing Browse, Home and Files workers retain their owned components. OpenCode is orchestrated on Spaces Home; Chat correctness is transferred to the Home worker after its current test batch. One canonical working tree: `/home/jacob/Documents/9to1`, current branch `opencode/m3-shell-wiring-20261016`. Heavy builds are serialized. No remote merge/deployment claimed.

| Task | Work and acceptance | Dependencies/status |
|---|---|---|
| 9T-HOST-01 | Inventory actual source/builds, app roster, standalone entry points and every old main-shell function; map each to new reachable location; preserve canonical IDs and active ownership. | WORKING; root + component owners, no assumed runnable package. |
| 9T-HOST-02 | Normal packaged 9to1 entry launches unified Browse host; share Home Core/Dulche/accounts/permissions/configuration independently of visible tabs/window; no nested full shell. | WORKING — queued after inventory/contracts. Root integration owner. |
| 9T-HOST-03 | Real native CUI app surfaces and web/search in existing tabs, using typed 9to1:// routing, app policy, canonical resources and original actor lifetime. Preserve internal/web/CUI/privileged distinctions, groups/workspaces/splits. | WORKING — contract inventory. Native app route must not enter web search. |
| 9T-HOST-04 | Shared Home/New Tab sidebar: Apps, Dashboard, Models/Discover; New Tab adds canvas/search/explicit AI. Remaining Home functions reachable. Actual app registrations/providers; honest empty/errors. | WORKING — existing Home CUI implementation under test; shared host wiring pending. |
| 9T-HOST-05 | Every real app has working standalone entry sharing implementation/resources/revisions/permissions; same resource saved embedded and reopened standalone without visible main window. | WORKING — inventory. Canvas admission is currently same-process; Files lacks current standalone launcher; Write entry currently boots full Desktop.App. These are gaps, not accepted fallbacks. |
| 9T-HOST-06 | Forward-compatible migration/session recovery, captured profile/resource identity, unsaved work, background task continuity, close/reopen, draining callbacks. | WORKING — queued; tests must exercise original ownership and interrupted recovery. |
| 9T-HOST-07 | Package and exercise full first journey and per-app/platform functional/visual/keyboard/focus/permission acceptance; exact source/build/platform evidence; independent code review. | WORKING — component tests in progress, packaged journey not yet executed. Windows/Android runtime unavailable here = Unvalidated. |
| 9T-HOST-08 | Continue retained full V2/original/annex backlog after common-host milestone, including complete Spaces/Home/Files/Browse and other actual apps/standalone roster. | WORKING — retained full scope, no reduced definition of finished. Model Groups #34 remains linked with its distinct master-write blocker. |

Implementation defaults (not owner-specific decisions): app selection from empty New Tab fills it; app selection from Home opens an app tab retaining Home; sidebar New Tab selects landing canvas; tab-strip + creates a tab; stable Home entry and configurable tab placement. No invented mandated sidebar order, initial selection or Home closability.

First required packaged journey: **Launch 9to1 → Home → website → native app tab → meaningful operation → save → reopen same canonical resource in standalone executable.**

Component passes establish only exercised paths. Actual Gecko web hosting, independently admitted standalone processes, complete roster and supported-platform packaged acceptance remain open. Record WORKING/CHECKPOINT/BLOCKED/COMPLETED/VERIFIED separately; do not close this issue on compilation or one sample app alone.

