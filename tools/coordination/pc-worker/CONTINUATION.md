The pinned PC continuation loop uses the installed OpenCode V2 session APIs and the existing `v2-control.json`. Root owns runtime configuration, approved prompts, milestone policy and activation. This implementation does not create sessions, change models, approve permissions, bypass build/memory guards, or certify product acceptance. The Monday 19 October 22:00 BST delivery target is not an automatic stop.

One persistent runtime directory holds `continuation-state.json`, a bounded event log (1 MiB plus one rotation), and `continuation.lock`. The second owner is refused. Every dispatch also acquires the existing shared `v2-dispatch.lock`, rereads control and exact session/input state, records its deterministic message ID before submission, and reconciles ambiguous submission only through that exact ID and metadata. Never copy canary state into the production runtime directory.

The base configuration still requires the exact `session_id`, `directory`, `model` including variant, `opencode`, `server`, `service_config`, `control_path`, `dispatch_lock_path`, `enabled`, `approved_user_message_ids`, `other_programme_session_ids`, `free_usage_verified`, and hashed `continuation_prompt`/`retry_prompt` entries. Only the pinned local HTTP service on 127.0.0.1 is accepted. Owner-owned PC pricing additionally requires `pricing_basis:"owner_owned_pc"`, `gateway_base_url:"https://ai.9to1.uk/v1"`, and the exact `homepc/strata-dynamic` identity. A catalogue cost of `[]` is accepted only with that explicit ownership verification; nonzero pricing is refused. No provider/model/config response or credential is logged.

A genuinely completed assistant response and inactive succeeded session permit the next authorized task. Product test/tool failures remain product follow-up work, not provider retries. Unknown new user input, unknown queued input, permission/forms, an active configured peer, changed identities, incomplete unqualified responses, and ambiguous dispatches prevent further prompts. Existence of `coordinator_marker_path` parks work; marker text is never treated as authority. A final model reply is not acceptance.

STOP and PAUSE are checked before prompts, probes, recovery and interruption. Recovery and active-outage grants are reloaded after probes and immediately before interruption; provider recovery and service-start grants, including exclusive ownership, are also rechecked after persisted intent and before their mutating calls. STOP/ordinary PAUSE cancel only loop-owned inbox IDs and interrupt only this session with `resume=false`. A `hold_only:true` pause merely parks dispatch. A normal manual interruption cannot auto-resume. An outage interruption can resume only when its durable confirmed receipt matches the same session, user, assistant, inference phase, idle boundary and unchanged control-file hash.

Availability recovery is opt-in:

```json
{
  "recovery_epoch": 0,
  "recovery": {
    "enabled": true,
    "credential_file": "/absolute/private/owner-PC-api-key",
    "retry_base_seconds": 120,
    "retry_max_seconds": 1800,
    "probe_base_seconds": 30,
    "probe_max_seconds": 300,
    "active_outage": {
      "enabled": true,
      "stale_seconds": 120,
      "probe_interval_seconds": 60,
      "unavailable_samples": 3
    }
  }
}
```

Read-only local API failures back off indefinitely at 5, 10, 20, 40, 80, 160, then at most 300 seconds. They no longer permanently stop after five failures. Authentication/configuration errors explicitly park until root fixes the cause and increments `recovery_epoch`; STOP still attempts scoped stopping. Errors retain safe classes and IDs, never secret response bodies.

The installed CLI intermittently truncates large responses when stdout is a pipe: a read-only comparison captured an incomplete 196,608-byte response versus the complete 329,765-byte response for the same completed compaction. CLI stdout now goes to a private automatically closed temporary file before parsing. Each API operation still runs exactly once, including mutations; transport capture does not retry a potentially accepted prompt. Authentication, timeout, envelope validation and ambiguous-dispatch handling remain unchanged.

Malformed JSON, blank replies and missing/invalid top-level data envelopes from supported local read APIs are observation failures. They use bounded API backoff and retry without requiring a manual recovery-epoch change. Logs retain only safe operation and error-class metadata. Real local configuration-file errors, explicit authentication/configuration responses and changed pinned identities still park. An uncertain prompt/interrupt response retains its intent and cannot be blindly resent. The earlier18:51 monitor incident discarded its exact exception; this patch repairs a reproduced classification defect without claiming a proven historical root cause.

A terminal transient provider error permits recovery only after the retry backoff and an authenticated GET to the exact HTTPS `/v1/models` returns `strata-dynamic` with `status.value == "loaded"`. Redirects are refused. Probe reads/timeouts are bounded, and repeated unavailability/not-loaded responses back off without sending generation requests. HTTP 429 honors `Retry-After`, including dates; exposed assistant `retry.at` also delays recovery. Authentication/configuration never triggers model switching or automatic service restart. The default terminal transport-type allowlist is in source and may be narrowed by root through `transient_error_types`.

Unclassified terminal `APIError`/`APICallError` without a status has a narrower path: first retain actual endpoint-unavailable evidence for that exact failed boundary, then permit one recovery when the model becomes loaded. If that error repeats while the endpoint is healthy, park for diagnosis. Arbitrary unknown error classes do not qualify. The optional `outage_probe_error_types` list is coordinator-owned; it must never admit authentication, configuration, permission or product errors merely to remove a hold.

A hanging active generation is considered for interruption only if all exposed tool parts are terminal, there are no permission/forms, all queued inputs are explicitly approved user IDs, and the exposed generation fingerprint has remained unchanged for at least the configured interval (minimum 60 seconds). It then requires at least three separate unavailable endpoint observations (30-second minimum spacing). Qualifying results are a network connection failure or HTTP 502/503/504/521/522/523/524/530. Generic 500, not-loaded, authentication and rate-limit responses do not justify interruption. Any new exposed generation activity resets the proof. Under the dispatch lock it rereads the same fingerprint/user/session, persists intent, and calls supported interruption with `resume=false`. It never intentionally interrupts a running tool/build. An ambiguous interruption is held, never retried blindly.

Automatic context compaction is also an exposed inference phase. The loop reads its actual message status and verifies the effective compaction agent uses the pinned session model and workspace; it never invents missing model or completion fields. An unchanged running automatic compaction can use the same three-observation outage path, with settled tool state, fresh peer/ownership checks and an exact phase receipt. A failed automatic compaction can recover only when the session is inactive with an explicit failed outcome, its real idle time follows phase creation and the approved input, and the recorded error matches the reviewed transient transport/HTTP allowlist. The same backoff, authenticated loaded-model check, known queue preservation and once-per-phase dispatch apply. Completed compactions, manual compactions/cancellations, unknown errors, missing model proof and running tools remain held. An optional compaction-agent lookup failure prevents recovery without obstructing explicit STOP.

After a confirmed own outage, a healthy endpoint permits one idempotent recovery prompt with `resume=true`, preserving already approved queued work in its original order. The recovery prompt is appended, not a duplicate of queued repair text. A terminal qualified provider failure can similarly resume a known queue. Unknown queue entries block both paths. This relies on root's approved retry prompt directing continuation of the saved task and preserved queue.

Optional local service recovery is separate:

```json
{"service_recovery":{"enabled":true,"exclusive_ownership_verified":true,"cold_boot_recovery":true}}
```

It requires root's explicit exclusive-ownership verification, a successful service observation within the previous ten minutes showing no other active session, a positively refused connection to the pinned local port, and absence of any OpenCode process. It then issues only supported `opencode service start`, at most once per five minutes, under the same dispatch lock and RUN check. It never restarts a live/shared service, stops unrelated work, or edits a database. An opt-in cold-boot exception permits the same supported service start after a long laptop shutdown only when a recorded healthy snapshot proves a different prior Linux boot ID, identical owner-control contents and exclusive prior ownership. The current refused port and absence of all OpenCode processes are verified again under the dispatch lock. A healthy snapshot must seed these receipts before shutdown. Pending prompt intents may start the absent service only to reconcile their exact saved message ID; they are never resent blindly. A changed service address, unknown active cohort or ambiguous interrupted model turn remains held. No physical PC/laptop power-loss test has been performed by this implementation.

Milestone configuration:

```json
{
  "milestones": {
    "plan": {"path":"/absolute/plan.json","sha256":"APPROVED_HASH"},
    "state_path":"/absolute/state.json",
    "checkpoint_path":"/absolute/checkpoint.md",
    "commit_policy": {"path":"/absolute/policy.txt","sha256":"APPROVED_HASH"},
    "spec": {"path":"/absolute/canonical-spec.txt","sha256":"APPROVED_HASH"},
    "dirty_turn_limit":3,
    "dirty_seconds_limit":1800
  }
}
```

The plan has `milestones:[{id,dependencies,...}]`; state has `current_milestone_id` and `milestones:{id:{implementation_status,acceptance_status,evidence,commit,...}}`. Implemented dependencies or the helper’s scoped `dependency_evidence` receipts admit the next pending/in-progress entry in plan order; blocked lanes remain visible while independent eligible work continues through root's approved helper. The prompt directs use of that helper to show only the current entry and relevant canonical specification ranges. A null current with eligible work requests the helper’s next selection; unfinished work with no eligible entry yields `held_dependency_blockers`, not a corrupt-config claim. If every milestone is implemented and current is null, the loop quietly waits for independent acceptance. It never upgrades acceptance itself.

At completed turn boundaries the loop observes actual git HEAD, tracked/staged diff, porcelain state, checkpoint hash and the current milestone commit receipt. It records the real commit and observation time. Untracked disposable `work/` evidence does not count as dirty source; tracked changes there do. After three continuation turns without a substantive observed commit, or 30 minutes of observed dirty work, it replaces broad expansion with a focused checkpoint/commit instruction plus root's policy. The instruction requires owned scoped coherent/WIP commits and documented failures; the loop never stages or commits files itself. Empty commits do not reset dirty-work cadence. Three completed turns with unchanged HEAD/diff produce a concrete bounded diagnostic/implementation cue, not forced edits. Observations are frozen per prompt and immutable/input hashes rechecked at admission. An active tool is never interrupted merely to meet this cadence.

Root reviews and activates/reloads only its continuation unit. Observe-only remains the default:

```text
python3 /absolute/continuation.py --root /absolute/runtime --once
python3 /absolute/continuation.py --root /absolute/runtime --execute
python3 -m unittest discover -s /absolute/pc-gateway -p test_continuation.py -v
```

The 101-test continuation suite includes scoped queue/outage/rate/auth/STOP gates, automatic-compaction phase and terminal-failure recovery, real temporary-git cadence checks, and a real subprocess restart fixture using the fake service. The separate milestone-ledger suite has 12 tests. Root ran both suites successfully; the terminal-compaction regression also fails against the preceding source as expected. The subprocess fixture persists backoff, observes unavailable→loaded, dispatches once, restarts without duplication, and honors STOP; it performs no network/inference or live service changes. For an isolated real canary, wait until the product session is inactive, use root's existing canary with `max_dispatches:1` and `exit_after_dispatch_limit:true`, retain exact messages and resulting fixture files, and verify no third dispatch after restart. Never simulate failure by taking down the production PC, shared service or product turn. A root-owned API stub/transport wrapper may supply the failure to a separate continuation runtime; do not alter the production provider or credential.

Limits: installed V2 provides no atomic conditional prompt/interrupt operation, so other dispatchers must cooperate with the shared lock; a fresh-check race cannot be eliminated by this API. The loop observes only exposed tool/event state. It cannot police unrelated clients using the PC endpoint or bound hidden provider SDK retries. Error response headers are not exposed in the installed structured session schema, so only explicit retry metadata and probe headers are available. Existing OpenCode automatic compaction may run normally; this loop does not issue manual compaction, create a successor session, or infer a context overflow from arbitrary errors. An explicit context-limit failure remains a coordinator boundary until its model/pricing/error semantics are verified. Full chronological history review and independent product acceptance remain separate coordinator duties.

The nested `current_observation` is refreshed after every complete successful session snapshot, including during an active generation or tool. It records current session/user/assistant/model and inference-phase identities, phase status/model-proof result, safe error class/status, timestamps and tool ID/name/status only; arguments and outputs are not copied into this status record. Pinned identities and completed dispatch receipts remain separate. A failed snapshot retains the last observation and its original timestamp rather than presenting it as fresh.

## Owner-selected model continuation — 19 October 2026

The owner selects the provider/model/reasoning in the existing OpenCode session.
With `follow_owner_selected_model: true`, continuation observes that selection; it never writes model settings. Choose the model and send exactly **Continue** or **Resume** once after a manual interruption. Automatic continuation follows successful completed turns, retaining the free-usage check, existing milestone queue and commit policy. Paid or unverified pricing stays held. Other manual instructions remain available for coordinator review; Stop/interruption is never automatically retried. PC outage interruption is disabled in this mode; non-PC provider failures are held without PC probes or retries. The existing desktop Resume helper remains PC-pinned; use the existing OpenCode session for model choice and this initial Continue.
