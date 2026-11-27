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

### 11:52 GMT implementation checkpoint

The first focused batch at `5c946d4ae` completed: CUI17/17 and Assistants7/7 pass; Browse49/58 with the same nine earlier failures; Spaces rendering9/13 with four expectations tied to the previous always-visible manager. The root corrected explicit-management rendering expectations without dropping their action/source checks and fixes pool-thread controller notifications through an owned dispatcher task retained by session close. A stale queued state cannot overwrite the current projection. Focused rerun and native successor are pending.

The live SQLite migration preserved2,586 archived runs, added only `runs_created`, and reduced observed status to3–5ms and capabilities to19ms. All original logs and receipts remain under the private takeover evidence root. No inference service was restarted or reconfigured.

### 12:26 GMT regression and recovery checkpoint

- The earlier nine Browse route/history fixture failures are resolved: 58/58 at `197eabb83`. Spaces rendered checks 13/13, Assistant routing/configuration 7/7, HUI scroll 39/39 and Markdown typography 6/6 each have their separate retained guard logs. Cohorts overlap; they are not a full-suite claim.
- Immutable candidate `atlas-197eabb837-20261127-120112` was genuinely guarded-published and prepared. The normal managed reload refused because the old app failed original close. The candidate was **not launched**.
- The old run `9fed7058-c2e0-4d9b-a57c-e2cac5578ea7` retains two `NotSupportedException` leaves from restored README/global.json local-source previews with no immutable content. This is an expected preview-unavailability state wrongly promoted by the old wrapper into a retained original fault. The repaired wrapper accepts only issued same-owner empty refusal results after all actual read/readiness checks; genuine I/O failures remain retained.
- Persisted profile backup: `/home/jacob/.local/share/9to1-development/backups/atlas-before-recovery-20261127-121137`; 307 files, 116,573,635 bytes, SQLite online backups and successful quick checks. Manifest SHA-256 `734ee1b1f205006b90d221beaa125e52d2c065bced5e0ebaf0b0694eafc3e6d3`. This does not establish capture of unsaved in-memory state.
- No Force quit, process kill, profile replacement or fabricated successful-close receipt has occurred. Native recovery requires the owner's explicit Force quit choice, a terminal original process/guardian with actual exit receipt, and a tested immutable replacement bound by the documented manual recovery review. A later genuine normal close/reload remains required.
- At `5c2f5c1b5`, Build catalogue 5/5 passes; Browse composer/routes 79/83 and Desktop chat/Files 66/71. Original failed logs are retained (`third-checks-results.json`). Three Browse failures still expected the retired permissions button; one compared layout before the draft was settled. The Files negative fixture asserts the failure before the original close joins it. Options native pointer interaction and deliberate publication-retirement fixture qualification are being investigated/corrected. None of these reruns is claimed before execution.
- Source now also includes sidebar source-refresh reduction, direct retained transcript ordering, actual Attach/formatting exclusion and single Options presentation. Responsive Spaces and finite Assistant viewport work remains isolated until reviewed and checked.
