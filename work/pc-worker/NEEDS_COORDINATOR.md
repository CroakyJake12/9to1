# NEEDS_COORDINATOR — PC implementation worker

Created: 2026-10-19 (turn boundary). Branch: opencode/v2-pc-sequential-20261018. HEAD: e527da25.

## Why this marker

All remaining executable implementation is either (a) blocked on a foundational slice that cannot be completed in this session, or (b) awaiting independent acceptance. No further dependency-ready engineering work remains that can be safely completed without the blocked foundations.

## Blocked milestones (precise)

1. **F01 — Authority, capability manifest and permission foundation** (blocked).
   - Blocker: foundational authority/capability/permission foundation requires reading spec 1-1076 and reconciling source; not implementable in a single bounded slice. Partial spec reconciliation done (lines 1-560).
   - Unblock: complete reading spec 1-1076, reconcile exact source, implement authority/capability-manifest/permission foundation with smoke evidence.
   - Impact: F01 blocks F02-F09, H01-H06, S01-S09, V01-V02, B01-B08 and most S2-* milestones (transitive dependency).

2. **R01 — Repair actual Haven shutdown after rejected native admission** (blocked on remaining sub-items).
   - Core repair DONE and verified: typed settled-action-failure contract (c17761e2); real apphost process exits 1s (was 40s+); relaunch restores.
   - Remaining: (a) user-visible CUI recovery experience for genuine unresolved close obligation (SHUTDOWN-ASSIGNMENT item 7 / spec 1791); (b) unsaved-draft close-preparation journey on real UI route (cancel/save/discard/veto).
   - Unblock: implement CUI-authored recovery experience + run unsaved-draft journey.

3. **R02 — Repair actual Spaces entry and both retained sidebars** (blocked on remaining sub-items).
   - Done: canonical absorbed-app catalogue projection (8a989523); offline model-inventory degraded state (d450f4bf).
   - Remaining: (a) Spaces entry publishing SpacesHomePage with icon-only rail + contextual chat/history sidebar + new-chat per Space; (b) selection rollback on queued-open failure; (c) management return-chat identity persistence; (d) native verification of entry/sidebar/new-chat/switching/restart/alias preservation.
   - Unblock: implement Spaces entry with both retained sidebars and explicit new-chat per Space, coherent selection rollback, management return-chat identity; run native verification.

## Awaiting independent acceptance (NOT_RUN/PENDING_REVIEW)

- R03 — Complete genuine CUI Home Apps authoring (implemented, PENDING_REVIEW). Native proof: 29 tiles, search, no-match, clear, Open Files.
- R04 — Repair New Tab search selector clipping (implemented, PENDING_REVIEW).
- S2-EXCLUSIONS — Deferred scope registry (implemented, PENDING_REVIEW).
- S2-DECISIONS — Open decision registry (implemented, PENDING_REVIEW).

## Additional open gates (recorded in ledger)

- Launcher New Tab -> Apps -> app same-tab journey: NOT RUN (after tab-identity correction).
- Canonical absorbed-app catalogue: IDs/pins/aliases preserved (done).
- Spaces new-chat with both rails: OPEN.
- Measured tab/Space latency: OPEN.
- Haven.Infrastructure.Tests: BLOCKED (x64 testhost on linux-arm64).
- Haven.Desktop.Tests: NETSDK1100 on Linux.
- AI PC poweroff: NOT SCHEDULED (no trusted remote management channel on this host).

## What the coordinator should do

1. Dispatch independent acceptance (Astra) for R03, R04, S2-EXCLUSIONS, S2-DECISIONS.
2. Schedule the foundational F01 slice (authority/capability/permission) as a dedicated multi-turn work item; it unblocks F02-F09, H*, S*, V*, B*, S2-apps.
3. Schedule R01 remaining sub-items (recovery UX, unsaved-draft journey) and R02 remaining sub-items (Spaces entry, sidebars, management return-chat) as dedicated slices.
4. Root/owner must schedule AI PC poweroff through their own channel (no trusted remote management exists on this host).

## No remaining dependency-ready implementation

`milestone-ledger.py next` returns "no eligible implementation; acceptance/blockers remain". All blocked milestones have precise unblock conditions recorded in their block records. No implementation subagents or concurrent model requests were used.
EOF
echo done