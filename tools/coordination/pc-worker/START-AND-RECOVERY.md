# 9to1 Worker — start and recovery

Your existing worker, instructions and progress are saved on the laptop. You do not need a new chat or to paste the specification again after restarting either computer.

## Normal start from both computers switched off

1. **Turn on the PC and boot Linux.** Connect it to the internet and let the saved Strata model finish loading. Keep the PC awake. Its supplied setup starts the model supervisor, gateway and tunnel automatically.
2. **Turn on the laptop and connect it to the internet.** The 9to1 continuation service is enabled at startup. It waits for the PC model and preserves the existing assignment during connection failures.
3. **Open “9to1 Worker” from the application menu or desktop, then choose “Open worker”.** It opens the existing OpenCode session. The laptop and PC may use different internet connections; no VPN or same-network connection is required.

[Open the existing worker](http://127.0.0.1:49374/server/aHR0cDovLzEyNy4wLjAuMTo0OTM3NA/session/ses_eb01533adffev5jMYgJVLFXfZP). This address works on this laptop after its local OpenCode service starts.

The launcher’s **Status** option checks the connection without starting another model generation. Look for changing tool activity and new responses in the worker, not just an open window. A build or a large context request can take time. Avoid opening a second implementation session against the shared model.

## If it is connected but work has not resumed

Choose **Resume saved work** in the launcher. This is your explicit instruction to continue the existing programme, including after an intentional pause. If the worker is already active, it must not send another prompt.

The resume action checks the saved session, model, instructions and queued work before sending anything. It retains the original task and milestone. If it reports unknown input, an unanswered permission question, or an uncertain previous submission, resolve that specific item rather than repeatedly pressing Resume. An ambiguous submission is retained for reconciliation and is never blindly sent again.

A sudden laptop shutdown can leave OpenCode reporting an interrupted turn without explaining whether you cancelled it. That case deliberately requires this explicit resume action. Ordinary PC gateway outages have automatic recovery; STOP and PAUSE remain effective until you choose to resume.

## What survives a restart

- The same OpenCode session and isolated code worktree, including uncommitted changes that reached disk.
- The canonical local specification v2, owner corrections, CUI-only rule and prohibition on Fluent Avalonia presentation.
- The ordered **90-milestone plan**, requirement evidence, current checkpoint and queued repairs. The plan indexes the full specification; it does not replace it.
- The instruction to reread the applicable specification sections at each milestone and after compaction, test actual behaviour, make scoped commits and continue to the next eligible milestone.
- Commit/checkpoint reminders at turn boundaries after three dirty continuation turns or about 30 minutes of observed dirty work. Long-running tools are not interrupted just to meet that interval. Failed and unrun checks remain recorded; implementation does not mark its own work independently accepted.
- The single shared PC model, the configured **262,144-token context window**, and the revised delivery target of **Monday 19 October, 22:00 BST**. The target is not an automatic stop.

Recent writes not yet saved to disk can still be lost in abrupt power loss. Keep the existing laptop directories and OpenCode data intact; do not reset the worker, delete lock files, reinstall its old configuration, or create a replacement chat as a recovery step.

## If the PC connection stays unavailable

First check that the PC has booted Linux, is awake, and has internet access. Let the model load before retrying. The existing loop backs off between checks, so reconnection is not instantaneous.

On the **PC**, open a terminal and check the three services:

```bash
systemctl --user status homepc-ai-supervisor homepc-ai-gateway homepc-ai-tunnel
```

If they are stopped, start them:

```bash
systemctl --user start homepc-ai-supervisor homepc-ai-gateway homepc-ai-tunnel
```

For the recent error details:

```bash
journalctl --user -u homepc-ai-supervisor -u homepc-ai-gateway -u homepc-ai-tunnel -n 60
```

Then use **Status** on the laptop. These are the service names from your supplied PC setup pack. The laptop’s inference connection cannot turn the PC on, unlock it or repair its operating system remotely.

## If the laptop window does not open

On the **laptop**, use the installed helper:

```bash
9to1-worker status
9to1-worker open
```

For an explicit resumption of the existing assignment:

```bash
9to1-worker resume
```

If needed, inspect the continuation service:

```bash
systemctl --user status 9to1-pc-worker-continuation.service
journalctl --user -u 9to1-pc-worker-continuation.service -n 60
```

The programme uses `/home/jacob/.opencode/bin/opencode` **v2.0.32**. A different `opencode` found on the command path is an older version; do not use it for this programme. Opening the launcher starts only the supported absent local service; it never restarts a live shared service.

## Connection details, if another tool needs them

| Setting | Value |
| --- | --- |
| API format | OpenAI-compatible Chat Completions, with streaming |
| Base URL | `https://ai.9to1.uk/v1` |
| Model ID | `strata-dynamic` |
| OpenCode provider/model | `homepc/strata-dynamic` |
| Reasoning variant | `high`, the highest variant advertised by this configured provider |
| Context / maximum output | 262,144 / 8,192 tokens |
| Existing private API key file | `/home/jacob/.config/homepc-ai/api-key` |

The installed configuration already reads the key file. Keep it private; do not paste it into the worker chat or this guide. Do not reimport the setup ZIP’s older 65,536-token configuration. Its `check_endpoint.py` also runs a generation, so use the new **Status** option while the worker is active.

## Saved locations

| Purpose | Location on the laptop |
| --- | --- |
| Runtime and recovery state | `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/9to1-coordinator/pc-gateway` |
| Owner RUN / PAUSE / STOP control | `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/9to1-coordinator/v2-control.json` |
| Worker code checkout | `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/v2-pc-implementation` |
| Current checkpoint | `work/pc-worker/CHECKPOINT.md` inside the worker checkout |
| Milestone progress | `work/pc-worker/MILESTONE-STATE.json` inside the worker checkout |
| Governing instructions | `AGENTS.md` and `docs/specification/9to1-development-specification-v2.md` inside the worker checkout |
| OpenCode session | `ses_eb01533adffev5jMYgJVLFXfZP` |

These paths explain what is retained; normally the launcher handles access. Avoid editing live state or copying it into a second running loop.

## What has been verified

The laptop continuation service is enabled, user lingering is enabled, and the cold-boot startup repair has passed 100 isolated checks. A healthy observation has saved the current boot and owner-control identities needed for startup after a long shutdown. The live worker remained active after the continuation service was reloaded.

The PC setup pack documents automatic startup and saved model-profile recovery. **Neither computer has been physically rebooted for this check.** Automatic PC power-on after electricity returns depends on the PC’s firmware settings, which have not been inspected. The laptop will reconnect once the PC is back; it cannot restore power itself.
