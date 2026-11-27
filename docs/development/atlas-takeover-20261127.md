# Atlas takeover — 27 November 2026

## Owner request and operating window

Jacob requested that Elliot take over from Qwen, make Spaces match the supplied Interface Atlas v5, improve severe app lag, remove or replace legacy presentations, and prepare the app for independence. Work is authorised through the 14:00–15:00 London window today; target a reviewable checkpoint at 14:30 and finish by 15:00. Jacob asked Qwen to wrap up itself. Its latest committed handoff is `ef2083c84` and the tree was clean when inspected.

## Current reference and baseline

- Current design: owner-uploaded `9to1_Interface_Atlas_v5_Complete(4).html`; later v4/v5 override scripts supersede early renderers in the same file.
- Source: existing PC repository `/home/jacob/Documents/GitHub/9to1`, main; retain all Qwen commits.
- Running candidate initially: `5dd10543601360f299b8f431f0b48db74bbc757c`, managed run `9fed7058-c2e0-4d9b-a57c-e2cac5578ea7`, same dedicated profile.
- Carryover evidence: `docs/development/ui-correction-20261127/LEDGER.md`; Qwen fixed native model-picker outside/toggle/Escape dismissal. Build creation and compact history changes are newer than running candidate.

## File ownership

- Root: Spaces entry/routes, Spaces Home CUI, conversation/header/composer presentation, shared-shell integration, guarded builds and managed reload; integration and native pointer/keyboard checks.
- performance_audit: isolated narrow performance patches; exact file list and commit must be reported before integration. No inference reconfiguration.
- qwen_log_check: Atlas comparison and isolated specialist/Assistant presentation batch after explicit file assignment.
- independence_readiness: runtime/source/evidence audit, then a bounded nonoverlapping implementation batch after explicit file assignment.
- Only root runs heavy SDK jobs, serialised using the existing lock and memory guard. Other agents do not interact with the desktop concurrently.

## Acceptance targets

1. Spaces opens the correct conversation/workspace, with management behind an explicit action. Preserve histories, drafts, selection, actual route identity and permission boundaries.
2. Atlas geometry: 60px rail, 232px sidebar, flexible conversation, readable typography, bounded transcript and actual composer space; compact layouts remain reachable.
3. Attach/Formatting/Options/model picker have normal dismissal and focus behaviour, preserve draft and do not cause unintended actions.
4. Measure and remove observed UI lag sources; do not claim a performance improvement solely from a successful build.
5. Real model/task state and recovery remain truthful. UI work cannot certify missing RT026 compaction, dual-GPU ALORA pooling, automatic optimisation, or an end-to-end independent development workflow.
6. Publish the verified source through the normal immutable managed-preview workflow and inspect actual native results. Retain failures and distinguish local checks from release acceptance.

## Continuation

Private current owner/heartbeat: `/home/jacob/.local/state/9to1-development/atlas-takeover-20261127.json`. A scheduled continuation must not write while that owner has a fresh heartbeat (under 15 minutes). Inspect current source and receipts first; never restart Qwen or inference as a side effect. If root is active, perform only independent read-only review or report no update. Stop new development by 15:00 today.

## Progress

- Atlas attachment SHA-256 `9ce46ba954c95ccc025d17f2a5564c9e3b8adf53b4c752650dc725305c1841e6` matches the existing full reference at `/home/jacob/Downloads/9to1_Interface_Atlas_v5_Complete.html`.
- 11:40: integrated repeated-item subscription lifetime and browser layout scan fixes. Corrected four invalid test fixtures; rerun pending.
- 11:40: applied reviewed chronological runs index in a bounded live transaction. 2,586 runs retained. Status now 3.4–5.4ms and inference capabilities 18.5ms, measured after migration; earlier observations were seconds. No daemon or model restart. Exact evidence: private `atlas-takeover-20261127/live-index-migration.json` and `live-endpoints-after-index.json`.
- 11:44: candidate Spaces patch opens ordinary entries into an unsaved conversation, preserves specific restored routes, separates management, aligns rail/sidebar/header/transcript dimensions with Atlas. Guarded tests/native review still pending.

- 11:34: source and live process reconciled; Atlas requirements extracted; performance and independence audits running. No takeover product changes published yet.
