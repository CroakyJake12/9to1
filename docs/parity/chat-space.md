# Chat Space implementation and acceptance checkpoint

Status: **WORKING, not completed or verified**. The existing live Chat page remains in service;
the authored CUI Chat page is an integration candidate until its required parity gates pass.
This checkpoint records the uncommitted source on 16 October 2026 and separates execution
evidence from newly implemented behavior.

| Obligation | Implementation | Current evidence |
| --- | --- | --- |
| Stable conversation/branch ownership while navigating or queueing sends | Controller captures identities at submission; the canonical Chat pipeline binds an explicit handle on the original SQLite repository, validates first admission transactionally, and pins later context/response writes | SQLite Debug 9/9 and focused canonical pipeline Debug 29/29 passed; same-branch independent-host overlap policy remains unproven |
| First-send race and recovery without draft replay | One first-chat creation; per-conversation queue; serialized draft snapshots; accepted-input acknowledgement prevents recovery of already-persisted text after generation failure; canonical preflight refusal stops queued work and retains its reason | Prior 9 controller concurrency cases passed; four added recovery/new-chat/preflight cases await current Spaces run |
| Attachments remain on their original message and branch | Selected pending attachment IDs and original user input commit in one transaction; any missing, associated or foreign-branch attachment rolls back the entire write | Actual SQLite tests include successful binding and three rollback cases; pipeline callback tests verify acknowledgement after the same write and before dispatch |
| Action-time safety, model availability and ownership | Backend reloads canonical scope, current branch, safety and governed model inventory before execution; remove/import and message actions use owning services | Nine new controlled adapter cases await execution; these do not substitute for live services |
| Canonical regeneration | The same pipeline admits the exact edited branch input and persists only the new response; no duplicate user input | SQL and pipeline regressions passed in Debug |
| Authored responsive transcript, Markdown, model/branch selectors, reasoning and actions | CUI uses bounded grids, a scrolling transcript with FollowAtEnd, real Markdown, ItemsSource selectors resolved to published stable identities, and complete host action dispatch | Earlier run exposed unmounted rendering fixtures and three scroll assertions; fixture repairs and current rendering checks await rerun |
| Actual Desktop page wiring and lifetime | Page retains controller state on reactivation; original work joins outstanding sends before detaching; real rendered controls drive the controller | Four new maintained Desktop tests, linked by the portable presentation target, await execution |
| Clipboard, edit/delete/forget/branch/regenerate and attachment picker | Host dispatch reaches controller and original services; clipboard dependency is supplied by the shell | Current actual page/action/platform acceptance incomplete |
| Dual-model behavior and canonical history | Candidate explicitly refuses unavailable canonical dual execution | Incomplete; no fake view-only history or direct provider substitute |
| Complete cross-platform/device acceptance | Portable source compiles; no architecture flags changed and no replacement storage graph | Windows runtime, Android, accessibility, visual/device and full startup evidence remain unvalidated |

The latest focused Debug results are **29 Application tests and 9 actual SQLite tests passed,
zero failed/skipped and zero compiler warnings/errors**. The SQLite target links the exact
production repository, migrations and schema, using a temporary real SQLite database. The
application checks execute the original Chat pipeline against controlled transport/repository
boundaries. Release runs and the updated Spaces/Home/Desktop presentation runs are pending the
machine power hold.

The preceding Application run was 24 passed/3 failed, including all three branch cases then
present. No baseline execution was performed. Source comparison showed unchanged ordinary
attachment validation and refusal wording in HEAD and the current tree. Corrections now assert
two total validations with one after acceptance, the same accepted message identity at actual
provider dispatch, and exact JSON property absence instead of a substring matching a legitimate
durable requirement. The failed log and source-comparison evidence are retained separately.

Further concurrency evidence is required for independent hosts sending on the same branch and
for provider usage capture/timing across concurrent branch invocations. These gaps prevent a
full Chat concurrency acceptance claim. The scoped run manifest is
`work/acceptance/chat-accepted-input-debug-evidence.json` in the orchestration workspace;
the earlier failed-run analysis is `work/acceptance/chat-application-failure-analysis.json`.
