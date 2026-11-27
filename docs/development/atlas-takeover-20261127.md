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

### 13:18 GMT source and verification checkpoint

- Source through `863b6d2bd` retains all earlier Qwen and takeover work. The maintained ordinary Chat repository now atomically binds the accepted branch, exact input and turn, then refuses a response if that input or turn no longer qualifies. Another tab's branch selection or later input cannot redirect the original response. Regeneration Here/fork repairs only the exact verified unanswered turn. This is not an immutable prompt-history watermark or RT026 completion claim.
- Owning branch infrastructure cohort: **22/22**. Ordinary-service concurrency, Stop, layered cleanup, regeneration and canonical Task preservation cohort: **23/23**. Both passed at `863b6d2bd`, under the serial SDK lock and existing 1,100 MiB guard. Logs: `ordinary-branch-infrastructure-10.log`, `ordinary-branch-stop-regeneration-10.log`; exact timings/counts in `tenth-checks-results.json` under the private evidence root.
- Responsive Spaces sidebar/header integration is committed: 232px wide, 210px below 1,000px, automatic collapse below 750px, with user toggle intent and reversible-close resize reconciliation. Four owning responsive cases pass. A mounted actual Assistant workspace now owns its contextual navigation; the outer generic sidebar/header is suppressed without changing its retained preference. Native desktop verification remains pending.
- Options passes **15/15 keyboard** and **15/15 real-renderer pointer** cases at `86d442822`. The pointer fixture uses actual Skia hit testing, exact one-click delivery, outside dismissal across the popup root, and retained draft/focus/transactional-permission assertions. The default simplified drawing backend uses actual keyboard delivery. Neither backend is claimed as native PC acceptance.
- Assistant rendered first-use, actual Task creation, saved 30-message history, draft save/reopen and resize cohort passes **3/3** at `863b6d2bd`. Unit viewport/routing cohort is **9/10**: at 360x640 with a long Task draft and three attachments, the composer footer is 472px and leaves a 3px transcript slot whose padded viewport is zero. The four secondary actions now occupy exactly two rows, but further compact height correction is required. The fixture retains its full state and positive-viewport assertion; no replacement was published from this failed batch.
- The Files negative fixture now checks the real close/join boundary and exact retained failures; all three owning Files cases pass. Expected local-source preview unavailability remains nonfaulting after original authority checks; actual I/O failures remain authoritative. Old managed run `9fed7058-c2e0-4d9b-a57c-e2cac5578ea7` still runs revision `5dd1054360` after failed normal close. No Force quit or reviewed recovery has been performed. The existing verified persisted-profile backup remains retained.
- A further rail-entry defect was confirmed against V5 IX-SPACES-011/014: the existing retained-page dictionary was written but never read. Repeated rail entry incorrectly created a fresh conversation. A three-file repair is assigned to restore only the exact eligible live page/identity/draft and retain explicit New Chat as the new-conversation action. Separate five-file Assistant navigation work will add one retained 218px wide list/230px compact drawer, preserving the optional Workspace and original focus/identity owners.
- Follow-up live endpoint evidence during guarded SDK work: status median **3.276ms**, maximum **11.459ms**; inference capabilities median **17.003ms**, maximum **17.884ms**, five HTTP200 samples each at 12:47 GMT. These remain endpoint timings, not whole-app interaction latency. See private `live-endpoints-followup.json`.
- Stopped partial-response persistence remains unimplemented on the source-owned ordinary Chat path. Correct implementation needs the source buffer and exact branch handle retained through a finite durable finalization phase before the original task ledger is sealed, followed by a distinct interrupted terminal event after cleanup. A UI catch/save or generic late writer would not establish this. The existing 12,000 MiB independent Build admission floor also remains unmet; no inference reconfiguration or guard reduction is authorised by a successful UI build.


## Final checkpoint — 27 November 2026, 14:57 GMT

**Verified source and prepared replacement; native activation and full independence remain pending.** The product source is `a163b7c6d15720a071077080adfae72501741bed`. All original Qwen work is preserved. The closing documentation commit changes only this ledger and the feature inventory; the immutable replacement remains bound to the tested product revision.

### Owner correction and performance scope

Jacob clarified that 9to1 deliberately supports keeping the model always resident, while other applications remain responsive. This work therefore targets 9to1's unnecessary UI and data-access work. No model was unloaded, inference service restarted, or residency configuration changed. Compiler memory-guard interruptions are separate build constraints and do not establish the cause of interactive lag.

The integrated work removes repeated-item subscription retention, the Browser's global layout-time tree scan, unnecessary sidebar history/extension refreshes, full transcript copies for retained chronological order, and excessive scroll layout/paint invalidation. The scene host no longer invalidates the whole scene on every ordinary pointer move or exit; captured-pointer fallback and actual element hover invalidation remain. Five real Skia tests cover 80 retained rows and 64 same-target moves without additional repaint/measure/subscription counts, hover transitions, a custom captured drawer, and text selection.

Assistant prompt edits now update the prompt and action-availability projection without rebuilding unrelated Assistant, history, model, resource, configuration or attachment rows. Eighteen owning tests preserve collection/row identities and validate actual action states, archived/disabled notices, repeated values and stale-row refusal after a real replacement.

The chronological runs index retained all 2,586 runs. The last live follow-up measured status median 3.276ms and inference-capabilities median 17.003ms, with five successful HTTP200 samples each. These are endpoint timings, not a measurement of complete UI latency. The successor has not yet been activated on the existing profile, so the user's observed lag is not claimed resolved.

### Atlas and retained capabilities

Spaces uses the supplied effective Atlas v5 geometry: 60px rail, 232px sidebar (210px below 1,000px, collapsed below 750px), 66px header and bounded transcript/composer width. Returning through the rail restores the exact eligible retained workspace, identity and draft; New Chat remains the explicit creation action. Management is explicit. The single Options surface and actual Attach/formatting dismissal preserve draft, focus and transactional permission Save semantics.

Assistant Home, identity, contextual navigation and optional Workspace are integrated. The 218px navigation lane and 360px Workspace lane have one canonical owner. Initial attachment discovers the retained authored/content tree before native templates expose visual descendants, without restoring a permanent layout scan. Route changes at unchanged width reconcile the lanes. Disposal joins navigation ownership before child CUI teardown. Compact Task layout retains a positive transcript viewport even with a long draft and three attachments. Actual Task steering, queueing and return through the same Home remain covered.

Expected unavailable local Files previews no longer become retained shutdown faults after valid same-owner reads. Genuine I/O faults remain authoritative. Ordinary Chat completion is bound to its exact accepted branch, input and turn. The Build programme catalogue no longer truncates after 200 entries. These repairs preserve capabilities and saved-agent compatibility; they do not justify deleting unclassified older interfaces.

### Final focused verification

All rows below identify the same unchanged source `a163b7c6d15720a071077080adfae72501741bed`, with no skipped cases. This is **82 focused checks across three cohorts**, not the full repository suite.

| Cohort | Result | Retained evidence |
| --- | --- | --- |
| Assistant navigation, viewport and prompt projection | 28/28 passed | `nineteenth-checks-results.json`, `assistant-navigation-prompt-19.log` |
| Actual Skia pointer, Assistant first-use/history/Task/reopen and four appearance captures | 13/13 passed | `twenty-fifth-checks-results.json`, `desktop-pointer-assistant-25.log` |
| Spaces retained routes and presentation | 41/41 passed | `twenty-fifth-checks-results.json`, `spaces-retained-route-25.log` |

The Desktop cohort produced 32 actual rendered frames, eight in each of Bright, SuperBright, Dark and SuperDark, with original action/view/process joins. The coordinator visually inspected the wide SuperDark Assistant Home and compact SuperBright conversation captures. The fixture uses an explicitly fictional persisted Assistant identity. These are headless native-control renders, not live-PC acceptance or real-model execution.

Earlier failures and guard refusals remain retained. Fixture corrections select the actual authored ordinary-action controls and the real 17px Work identity heading, and accept both mutable and immutable solid brushes; exact expected colours, contrast checks, source ownership and frame counts remain. The initial zero-width lane and close-registration failure were repaired in production, not waived in tests.

Only the SDK child environment was adjusted: the 28-case run used `DOTNET_GCHeapCount=2`; the final Desktop/Spaces runs and publication used `DOTNET_GCHeapCount=1` and `DOTNET_GCConserveMemory=7`. The normal 1,100 MiB guard, full production references and source checks remained. No persistent app or inference configuration was changed.

### Immutable replacement and recovery

- Build: `atlas-a163b7c6d1-20261127-145503`; normal self-contained Linux x64 Release publication passed in 44.8s, guard result OK, exit 0, minimum available memory 1,749 MiB, unchanged 1,100 MiB floor.
- Runtime: `/home/jacob/.local/share/9to1-development/runtimes/atlas-a163b7c6d1-20261127-145503`; frozen and prepared, **not launched**.
- Build receipt: `/home/jacob/.local/share/9to1-development/receipts/atlas-a163b7c6d1-20261127-145503-receipt.json`; SHA-256 `1b2fd1b60a39821f7d3d1fa7de35d930c8e45994d4ef4a41744e40aa398e73bc`.
- Prepared manifest: `/home/jacob/.local/state/9to1-live-preview/approved-atlas-a163b7c6d1-20261127-145503.json`; SHA-256 `2812fe02756b3f7c01fcdc9bcb1dcfd50ad1671952f6f411bac4d08da0738143`.
- Private authoritative final candidate record: `final-prepared-candidate.json` under the takeover evidence root. The earlier `ed6f8ab305` candidate remains available but lacks the final pointer/prompt/navigation work.
- Final status inspection at 14:59 found managed run `9fed7058-c2e0-4d9b-a57c-e2cac5578ea7` at `5dd10543601360f299b8f431f0b48db74bbc757c` had ended at 14:50:08 GMT with actual exit code -15 (termination signal), not a successful normal close. Original process and guardian are absent. This coordinator did not terminate them; the signal source is not established. The earlier Files close fault remains part of the failure history.
- Refreshed saved-profile backup: `/home/jacob/.local/share/9to1-development/backups/atlas-before-recovery-20261127-132123`; 294 files, 108,283,195 bytes, 57 SQLite online backups with successful quick checks; manifest SHA-256 `4e7574de62a153be97263c16872bfebf624fe8905424d07cde11022590ff3169`. This preserves persisted data, not unsaved process memory.

The earlier Force quit question was unanswered, and this coordinator performed no Force quit, process kill or competing native launch. The final terminal-process observation makes that Force quit choice obsolete. `docs/development/live-preview.md` still requires a truthful failed-run review: inspect the actual -15 exit, establish retry safety, bind that review to the exact new manifest and unchanged profile, then use the documented `recover-failed` workflow. The source of the termination is not established and no RETRY_SAFE receipt has been fabricated. Actual existing-profile inspection and a genuine normal managed close/reload remain pending.

### Open acceptance work

1. Native activation, actual existing-profile interaction latency, visual review and subsequent normal close/reload.
2. Remaining Atlas Assistant shared Attach categories, formatting, emoji and Options/permissions integration; specialist Create/Configure routing; fuller selection formatting and Temporary personalisation.
3. Atlas presentation styles (Automatic, Chat, Agentic Chat, Worker and Design) are distinct from execution modes and native visual/text response settings. Do not relabel the latter as proof of the former or alter permissions to imitate presentation.
4. Automatic Assistant check-ins/scheduled work remain unsupported. Saved-agent migration remains a data-preserving capability.
5. Durable interrupted partial ordinary Chat responses remain unimplemented. Exact branch ownership does not supply partial-response finalization or an immutable prompt-history watermark.
6. Full RT-026 compaction strategies, incremental candidate journal, audit, late-event reconciliation and revision-safe activation remain incomplete.
7. Independent Build requires a genuine resident-model coding, verification, review, integration and recovery cycle. The completed workflow qualification uses a labelled scripted CPU fixture. Its existing 12,000 MiB available-memory and 70 GiB disk admission requirements remain unchanged and separate from UI lag and the SDK guard.

No full Atlas parity, complete legacy-interface removal, full-suite pass or independent-development readiness is claimed. Stop new development at this checkpoint before 15:00 London; retain the prepared candidate and actual failed-exit evidence for reviewed native recovery. No Force quit decision remains necessary for the already-terminal original process. Further development falls outside today's agreed work window.
