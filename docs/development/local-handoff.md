# 9to1 local implementation handoff

**Source 77 is published and verified:** commit `395a312170f9ae9900996b8dc82d050f7778c340`, tree `98d69d439998a7f160415ae059cf11911b674683`. The handoff documents are attached in Source 78. Record the handoff commit separately after that commit with `git rev-parse HEAD`; do not embed a self-referencing commit identity. The private packet is prepared at `/dev/shm/9to1-local-handoff-private-20261024/postimages/docs/development/`.

## Read order

1. `AGENTS.md` and `docs/development/workflow.md`.
2. `docs/specification/9to1-development-specification-v2.md` with expected SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`, plus current explicit owner decisions.
3. Current coordination state via Root; the shared JSON projection is not an independent writable ledger.
4. This handoff and the nine Source 77 packet summaries. Confirm exact hashes and exclusive ownership for any later task before editing.

## Source and evidence

Source 77 is `395a312170f9ae9900996b8dc82d050f7778c340` (tree `98d69d439998a7f160415ae059cf11911b674683`), branch `refs/heads/codex/product-implementation77-20261024`. It contains nine composed packets. Source 77 C# compilation, native checks, and current App/Den/Runtime basic checks are **NOT RUN**. Source 76’s App 14.512s, Den 3.222s, Runtime 4.328s basic passes had partial coverage; they are historical, **NOT REUSABLE**, and provide no Source 77 or acceptance evidence.

Source 77 packet themes: Build protected task recovery; Files existing-recipient role/expiry; Assistant Main create/reopen; per-reply Stop; Data shared whole-stroke eraser; Present ink tools and Linux route; Automation archive/restore; Write shared whole-stroke eraser and CUI tools. Preserve the exact packet summaries, postimages and private evidence from Root. Do not place private receipts, credentials, or packet-private payloads in Git.

Root retains the cloud physical source and cloud integration until the local worker acknowledges exact scope and custody transfer. After that ACK, the Sol lead may own its local branch and isolated worktree, commit its scoped changes, and serialize its own local SDK builds under an explicit local SDK allocation. This does not grant access to or authorize interruption of unrelated Thunder services. Preserve original dirty worktrees, checkpoints, packets and failures. Never reset or blindly pull an active checkout. Before editing, ACK the exact commit/tree, new worktree path, claimed paths, starting hashes, exclusive scope, and local SDK/build custody. Coordinate through the supported team adapter: the worker does not write the global ledger directly; an authorized coordinator may CAS the existing ledger. The shared JSON is a nonportable projection, not a local ledger to edit or replace. Do not launch/stop unrelated services, models, gateways, queues or usage controls. Preserve the independent busy Thunder worker and its services. Mercury is revoked; paid fallback is prohibited.

## Canonical product scope and priority

The canonical specification defines all of these phases. Products omitted from the current 11-app estimate are still required; “not individually estimated” does not mean complete. Absorbed product names remain within their owner and must not be counted as separate apps.

| Priority | Canonical scope | Current evidence and gaps |
|---:|---|---|
| 1 | Haven/Home Shell; CUI, nodes/Action Graphs, settings, Home Core services, Dulche, shared runtime, permissions, package lifecycle, model routing | Home Shell rough source estimate 65%; framework/service closure remains incomplete; full Haven acceptance outstanding. |
| 2 | Spaces (including Study, Tasks, Assistants, Research, Shopping, Translate, Space Builder); Voice & Vision | Spaces rough estimate 65%; Voice & Vision not individually estimated in Source77; complete both and dependencies. |
| 3 | Build (including Dev, Sites, Stacks as workspaces, not separate apps) | Build rough estimate 67%; published capsule recovery and broader IDE scope remain open. |
| 4 | Automations | Rough estimate 49%; core graph editing/publication, canonical Run owner/runtime adapter and tests remain open. |
| 5 | Write; Present; Boards; Canvas; Data/shared productivity engine | Tracked estimates: Write 63%, Present 64%, Boards 84%, Canvas 66%, Data 58%; all relevant compiler/native/runtime and remaining feature gaps remain. |
| 6 | Files; web services, accounts, cloud storage | Files rough estimate 60%; new-recipient sharing/account/hosted access producers and full C#/native checks remain open. |
| 7 | Planner; Connect (Mail is its first-class mode/subsystem) | Not individually estimated in Source77; required spec scope remains, untracked does not mean complete. |
| 8 | Cards | Not individually estimated in Source77; required spec scope remains. |
| 9 | Forms | Not individually estimated in Source77; required spec scope remains. |
| 10 | AI Studio | Rough estimate 25%; real Dulche/Den/permissions/context/Replay adapters, canonical CUI and independent acceptance remain open. |
| 11 | Picture; Wave; Motion | Not individually estimated in Source77; required spec scope remains. |
| 12 | Games; Play | Not individually estimated in Source77; required spec scope remains. |
| 13 | Mini Computer | Not individually estimated in Source77; required app remains distinct from shared computer-use/runtime dependencies. |
| 14 | Remaining independently specified apps, including Link, Prototype, Paths, Replay and other retained products | Not individually estimated in Source77; identify exact retained products from the canonical spec, preserve absorbed-product ownership, and complete applicable scope. |

The Source 77 rough estimates below are engineering judgments, not measured feature fractions or acceptance percentages. Accepted-scope values remain frozen. The specific Source 77 gaps are preserved in the JSON companion.

| Tracked surface | Rough implementation | Frozen accepted scope |
|---|---:|---:|
| Home Shell | 65% | 15% |
| Spaces | 65% | 15% |
| Build | 67% | 10% |
| Automations | 49% | 0% |
| Write | 63% | 20% |
| Present | 64% | 15% |
| Boards | 84% | 15% |
| Canvas | 66% | 25% |
| Data | 58% | 15% |
| Files | 60% | 15% |
| AI Studio | 25% | 0% |

Full acceptance is paused until **all canonical apps are 100% implemented** or the owner explicitly resumes it. Continue fresh affected compilation and genuine focused basic smoke checks. Preserve failures and unfinished work. A source estimate, partial compile, screenshot, or basic smoke pass is not full acceptance, reusable evidence, or release readiness. All canonical specification requirements and future native, installation, persistence, shutdown and delivery gates remain.

## OpenCode team runtime

OpenCode cloud v2.1.0 is reported installed. Team launcher v2 is implemented privately and has 26 offline checks; native entitlement, local window, sessions and runtime activation are **NOT RUN**. Exact model IDs and entitlement remain unknown. The intended architecture is Sol 6.1 leading three to four scoped surfaces, Luna workers, Astra supervising every 30 minutes with a five-minute review budget, Sol waking every five minutes, and OpenCode webview team tabs. Root is implementing the supported native API and team adapter; coordinate rather than duplicate that work. Before relying on teams, verify entitlement, an actual local window, real sessions/tabs, worktree isolation, scope enforcement, schedule/wake/budget behavior, supervisor and worker records, and restart recovery from observed output. Root cannot message or start the local worker with the current toolset. A one-time local bootstrap may be needed: start the authorized Sol lead through the installed local OpenCode interface, verify the actual session, then have it ACK the exact Source 78 commit, scope, paths, and custody before edits or builds. If the local session or entitlement is unavailable, report that state without claiming activation.

## Safe Source 78 checkout

Source 77 publication and verification are confirmed. Source 78 handoff branch is planned as `refs/heads/codex/local-team-handoff78-20261024`. After Source 78 is committed and published, obtain `HANDOFF_COMMIT` and `HANDOFF_TREE` from its separate attachment receipt; the receipt records `git rev-parse HEAD` and `git rev-parse HEAD^{tree}`. First inspect the actual local host, repository root, and configured remote names. Supply the real existing clone path, remote name, fresh worktree path, and receipt identities; do not assume cloud paths or guess a remote. The commands then fetch and verify the exact published ref and create a new detached worktree without changing the active checkout. Stop if any identity differs or the destination already exists.

```sh
set -eu
: "${GIT_REPO:?set the actual existing local clone path}"
: "${REMOTE:?set a configured remote name found by inspection}"
: "${WORKTREE:?set a fresh local worktree destination}"
: "${HANDOFF_COMMIT:?set the Source 78 commit from its attachment receipt}"
: "${HANDOFF_TREE:?set the Source 78 tree from its attachment receipt}"
HANDOFF_REF=refs/heads/codex/local-team-handoff78-20261024
hostname
uname -a
git -C "$GIT_REPO" rev-parse --show-toplevel
git -C "$GIT_REPO" remote
git -C "$GIT_REPO" remote get-url "$REMOTE" >/dev/null
test -n "$HANDOFF_REF"
test -n "$HANDOFF_COMMIT"
test -n "$HANDOFF_TREE"
test ! -e "$WORKTREE"
git -C "$GIT_REPO" fetch --no-tags "$REMOTE" "$HANDOFF_REF"
test "$(git -C "$GIT_REPO" rev-parse 'FETCH_HEAD^{commit}')" = "$HANDOFF_COMMIT"
test "$(git -C "$GIT_REPO" rev-parse "$HANDOFF_COMMIT^{tree}")" = "$HANDOFF_TREE"
git -C "$GIT_REPO" worktree add --detach "$WORKTREE" "$HANDOFF_COMMIT"
test "$(git -C "$WORKTREE" rev-parse HEAD)" = "$HANDOFF_COMMIT"
test "$(git -C "$WORKTREE" rev-parse 'HEAD^{tree}')" = "$HANDOFF_TREE"
```

If fetching the exact published ref fails or a commit/tree check differs, stop and reconcile with Root; do not substitute a branch or update the active checkout.

## Checks and restrictions

No product checks were run by the handoff author. The JSON companion was parsed successfully. Root confirmed Source 77 identity and push; review of this final handoff postimage remains pending after Source 78 integration.

Never put credentials or private cloud receipt contents in Git. Do not mutate Root’s active cloud source, checkout or SDK; acknowledged local branch/worktree and SDK work is allowed within transferred scope. Do not write the global ledger directly: use the supported team adapter and authorized coordinator CAS. Do not send external messages, invoke Mercury, use paid fallback, or infer model access, runtime activation, ETA, acceptance or release readiness without observed evidence.
