# 9to1 — Continuous development relay (proposal)

Status: **PROPOSED / NOT DEPLOYED**. Created 2026-10-11. This document defines the handover contract; it does not install Windows services, start Strata/OpenCode, or create Codex Cloud scheduled tasks.

## Objective and source of truth

Coordinate daytime **Strata + OpenCode** on the owner's Windows PC with an overnight **Codex Cloud** development shift. Continue real work across shifts without losing partial source, duplicated assignments, false verification, or overwritten edits.

The authoritative product scope is the Google Doc **9to1 Development Specification v2**, beginning with the 2026-10-10 four-part directive. The original specification and amendments still apply. The deliverable is NOT a simplified prototype. Preserve the Part 1 full / RCVP, Part 2, Part 3 and Part 4 distinctions. Keep the independent acceptance gates and donor parity requirements.

The repository's main branch and legacy migration ledger may lag active private development trees. **Never** infer that main, any historic packet, a green CI badge, or a successful compile is the latest complete project state. First reconcile actual source custody and recover active overlays.

## Shift selection

- **Local shift:** only after the owner returns home from college (allow 45 minutes after the final non-EPQ lesson), or when at home on a non-college day, and the local PC is available. No pre-college local Strata/OpenCode work, even on late-start college days. Provisional weekend window 07:30–22:00 Europe/London.
- **Cloud shift:** all remaining hours, including mornings before college, all class/commute hours, nights after 22:00, and verified owner absences. Cloud may begin/continue its own isolated work before local shift starts; switching ownership of local work still requires a durable remote handoff.
- Presence should be determined by an explicit owner-set availability flag and local runner health, **not** geolocation, calendar speculation, or simply whether a laptop is online. The school timetable and exact local user home-directory paths are private information and must not be committed to this public repository. Keep exact school/commute schedule and working launch commands in local private configuration.
- At the handover time, request a graceful local checkpoint. Do not kill a process before preserving dirty changes. If shutdown is unsafe, delay transfer and report why.
- A switch is **not** complete until the receiving shift has acknowledged the exact handover revision.
- Use Europe/London with DST-aware timestamps. Do not hard-code UTC+1 year-round.

## Ownership and conflict prevention

1. Use separate named branches/worktrees for Strata and OpenCode, with explicit file/component ownership. No two writers work on the same owned path without an acknowledged transfer.
2. Cloud must take custody only of tasks whose local writers released ownership in the remote handoff. Otherwise choose independently unclaimed work or perform read-only inspection.
3. Before handing off, commit **all** task changes to a recoverable branch. Include untracked files needed for reconstruction, but never commit secrets, user data, caches, credentials, or large accidental artifacts.
4. Capture the branch, exact commit SHA, clean/dirty status, modified/untracked path inventory, tests actually run, known failures, deferred checks, and next instructions. Never describe inaccessible local changes as preserved.
5. Push and verify that the receiver can fetch the branch and any necessary approved artifacts. A local commit alone is NOT a remote handoff.
6. Treat a shared GitHub handoff file as an audit record, **not** a lock. Automatic deployment needs a central atomic lease / heartbeat service with expiration, compare-and-swap and fencing tokens; a JSON file without atomic arbitration is insufficient.
7. No unattended force pushes, branch deletion, merges into main, spec weakening, or acceptance promotion. Keep contributions in branches/draft PRs until reviewed/accepted.

## Recommended roles

- **Strata:** coordinator for the local shift; breaks Part 1 tasks into bounded packages, maintains ownership and integration queue, reconciles original donor requirements.
- **OpenCode:** executes assigned implementation and targeted build/test lanes in separately checked-out worktrees; records evidence honestly.
- **Codex Cloud:** after successful transfer, picks the highest-priority released item, implements and tests in its own branch/workspace, submits recoverable progress and a morning report.
- **Independent acceptance:** tests actual user workflows after implementation. Implementer tests do not replace independent acceptance.

## Transfer contract

Use `handoff.template.json` as the shape of each checkpoint, stored in a durable repo-accessible location. The active checkpoint must contain:
- `handoff_id`, `from`, `to`, timestamp, status, spec version and canonical repo identity
- source base, exact branch and commit SHA, artifact identities, outstanding dirty changes
- tasks, owner/lease/fencing reference, acceptance stage and evidence paths
- verified commands + exit codes, failures, blockers, next executable actions
- receiver acknowledgment with timestamp + exact handoff ID

States: `PREPARING`, `READY`, `ACKNOWLEDGED`, `BLOCKED`, `CANCELLED`. `READY` requires verified remote source. A receiver must fail closed on stale or missing data.

Task stage and worker status are separate:
- Task/worker: `WORKING`, `CHECKPOINT`, `COMPLETED`, `VERIFIED`, `STALLED`, `BLOCKED`.
- Product acceptance: `IMPLEMENTED`, `BUILD-VERIFIED`, `PACKAGEABLE`, `PACKAGED`, `SMOKE-PASSED`, `ACCEPTANCE-PASSED`, `EXTENSIVELY VERIFIED`. `RCVP-VERIFIED` and `FULL-VERIFIED` must be demonstrated, never inferred.

## Local → Cloud

1. Stop assigning new overlapping work; allow bounded running operations to checkpoint.
2. Record current source, branch and dirty inventory. Protect all source first; no destructive reset.
3. Run relevant tests; record raw commands/results, separate NOT RUN from FAIL.
4. Commit, push and verify fetchability for each released branch; upload hashes/manifests for essential non-Git artifacts.
5. Write checkpoint with explicit released ownership, next tasks, limitations, and supporting evidence.
6. Only after cloud acknowledges the revision, make local agent writers dormant.

## Cloud → Local

1. Cloud stops opening new work near the transition boundary.
2. Commit and push current results in a dedicated branch (or update its draft PR).
3. Record acceptance scope, unverified items, blockers and exact next command.
4. Local coordinator verifies remote head and reviews merge conflicts before any integration.
5. Only then local Strata/OpenCode claim their next tasks.

## Automation deployment gates

- Phase A (this PR): reviewable protocol, worker prompts, handoff template.
- Phase B: owner configures Strata/OpenCode install paths, local commands and presence toggle; implement a Windows Scheduled Task or service plus graceful process checkpoint and logs. The 2026-10-11 supplied *working* Strata baseline is a 524288-context IQ2_XS Qwen setup on port 8081 using Codex profile strata, verified /health fields, and model_auto_compact_token_limit=450560. Preserve that command behavior and plugins allowlist; keep absolute user paths in private local config. OpenCode launch command still requires configuration.
- Phase C: configure a **published** Codex Cloud environment with access to the correct repositories/toolchains, and select a supported scheduling/trigger mechanism. **Do not claim that GitHub Actions or this repo can silently launch paid-plan Codex Cloud tasks without a verified supported integration.** ChatGPT Scheduled Tasks and Codex Cloud environments are separate capabilities; verify the chosen cloud execution surface end to end.
- Phase D: implement a central atomic lease/heartbeat store and test timeout, offline, overlapping-start and failed-handoff scenarios.
- Phase E: run two supervised local/cloud transition rehearsals; only then enable unsupervised rotation.

## Daily report

Produce a morning report with exact source hashes, changed features users can exercise, tests/real verification, failed gates, unresolved tasks, queued Part 1 next work, current owner and next handoff target. Do not report idle time as progress, or repeat identical work just to consume the shift.
