# Parallel worker admission verification — 2026-10-19 22:05 UTC

Requested: launch up to ten additional Beam workers through the installed OpenCode version's
supported interfaces, within the existing account's permitted usage and concurrency.

Result: **not admissible from this worker with the controls that exist.** The exact blocking
limitations are below, each verified rather than assumed. No safeguard was disabled and no
replacement launcher was created.

## 1. Verified baseline and ownership (this part is ready)

- Branch `opencode/v2-pc-sequential-20261018`, HEAD `9fc92762`. Base main `06a39628`.
- One active implementation worker (this session). Nothing is pushed or merged. The Haven/Spaces
  owner surface is untouched by the proposed lanes.
- Avalonia donor submodules remain at their pinned gitlinks (`XamlX` `009d4815470cf4bf71d1adbb633a5d81dcb2bb52`,
  `Avalonia.Dbus`→`Avalonia.DBus` `864a05282841bf04006890f04d11d60d1a046aa9`).
- Latest runnable build: `9to1 Workspace/shared/src/Haven.Desktop` builds with **0 errors** at
  `9fc92762`. Runnable artifact and launch recipe are in `work/pc-worker/CHECKPOINT.md`.
- Actual test results at `9fc92762`: `HavenOS.Browse.Host.Tests` 257 passed / 3 failed (the three
  Windows-only guards), `HavenOS.Spaces.Tests` 220/220.

## 2. Blocker: no supported launch interface is reachable from this worker

The installed OpenCode exposes to this worker only: model search, MCP resource list/read (no servers
are configured, so both return empty), session rename/move, and worktree create/list/remove. There is
**no session or agent creation interface** in that surface.

- `~/.config/opencode/` contains `opencode.jsonc`, `service.json`, `package.json`, `node_modules` —
  there is no `agent/` directory, so no subagent is defined for this install.
- The `opencode` binary is **not on PATH** in this environment (`command not found`), so no CLI
  session can be started from here either.
- Creating my own launcher, spawner or supervisor to work around this would be exactly the
  "replacement supervisor" and "new orchestration infrastructure" that was excluded.

## 3. Blocker: the existing coordination control is single-worker by construction

`work/9to1-coordinator/pc-gateway/worker-loop/config.json` binds exactly one worker:

- `session_id = ses_eb01533adffev5jMYgJVLFXfZP`
- `model.id = strata-dynamic`, `model.providerID = homepc`, `model.variant = high`
- `follow_owner_selected_model = true`

There is one session slot and one model binding, and `milestone-ledger.py` admits exactly one current
milestone with one in-flight record. It has no registration path for additional concurrent workers.
Extending it would be new orchestration work, not a supported existing control.

## 4. Blocker: host capacity fails the verification gate

- `nproc` = 12; memory 15 GB total, **~6 GB available**.
- A single `Haven.Desktop` solution build already takes ~3 minutes under the preserved `-m:1`
  safeguard, and the standing memory/OOM/build-concurrency guards exist because of observed failures.
- Ten workers building and testing this solution concurrently would breach the preserved build limits
  and is the known OOM path on this host. Heavyweight builds and native GUI tests must not run
  concurrently without verified capacity, and capacity is not there.

## 5. Beam availability is not the execution path

`opencode/beam-free` and `opencode-go/beam-free` do exist as free models. However the owner's standing
decision routes this programme through the single shared Home PC model behind the authorised gateway,
and the retired Beam sessions and their automatic retry loops must not be resumed blindly. The
permitted concurrency of the free Beam tier cannot be verified from this worker, and switching to it
would risk bypassing the account/usage guard rather than satisfying it.

## What is safe to do next (no new infrastructure required)

The coordinator, which owns the worker-loop config and the ledger, can admit lanes by giving each a
worktree from `9fc92762` plus the exact allowed paths below. `worktree_create` is a supported
interface available here if pre-staging isolated workspaces is wanted; that alone does not create
workers.

Lanes whose dependencies are established enough to admit:

1. **Automations durable Wait runtime half** — allowed paths:
   `9to1 Workspace/shared/src/Haven.Application/Automations/**`,
   `9to1 Workspace/Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs`.
   Contract already landed at `35c881d8`: `IAutomationRunContinuationStore.SuspendRunAsync` /
   `GetResumableRunsAsync`, migration 29 columns. Real tests exist.
2. **Automations Library/Home dashboard projection** — read-only over the same repository; no shared
   file conflicts with lane 1.
3. **Independent testing/review lane** — read-only; owns its own evidence directory only.

Lanes that must NOT be admitted yet, because their shared contracts are not established and they would
need a designated shared owner first: Build, Write, Present, Boards, Canvas, Files, accounts/cloud.
Each of those still depends on the Haven/CUI foundations and the shared Node Graph Framework, and
inventing reduced private frameworks to make them look finished is prohibited.

Shared integration must go through one designated integration owner in an isolated integration
workspace. Nothing may merge into this worktree.

## Remaining requirements on the current owner surface (unchanged, accurately labelled)

- R02 sidebar customisation: read path and host-navigation reuse landed at `9fc92762` but the
  projection is **not triggered**, so the sidebar still renders nothing. The trigger regressed
  `SpacesDetailsRouteTests.Actual_queued_read_failure_stays_faulted_and_only_observes_status_for_the_same_current_selection`
  (both variants) and is disabled with the failing test named in code.
- Only address-style sidebar targets can open; `Automation` and `GenerativePage` target kinds are not
  implemented.
- The previously unnamed intermittent Browse-host failure is now identified:
  `SpaceDetailsPageTests.Held_actual_save_keeps_new_typing_and_refuses_preparation_until_the_actual_operation_settles`
  (passes 3/3 in isolation, flakes under full-lane load).
- Independent acceptance remains **NOT_RUN** for R01 and R02.
