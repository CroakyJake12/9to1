# Unified 9to1 host implementation tasks

Persistent queue: [GitHub issue #35](https://github.com/CroakyJake12/9to1/issues/35). This file mirrors that child of master #21. Source clauses are incorporated in the existing CURRENT-REQUIREMENTS.md and source manifest; this is an execution record, not another specification.

## Current authorised integration — 16 October 2026
Status: **WORKING**, not whole-suite acceptance. Target: **17–18 October 2026, Europe/London**.

**Subsequent direct owner clarification, 16 October 2026:** “standalone assistants depreciated and folded into spaces”. Assistants is a destination within Spaces; its standalone product is deprecated. This supersedes the older standalone-Assistants paragraphs in the saved V2 snapshot. Preserve existing Assistant identities, conversations, configuration and saved links. Retained legacy executable/route aliases are compatibility entries into Spaces, not another current app or required standalone package. This is a direct chat instruction; no claim is made that the Google document was updated.

This is a child of the existing [master queue #21](https://github.com/CroakyJake12/9to1/issues/21), not a replacement task authority. The saved [V2 specification](https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit) now makes Browse the primary application branded 9to1; the previous main shell's functions are merged into it. Web/search and actual native CUI app surfaces share tabs. All actual applications retain standalone executables sharing canonical resources/services. Preserve the full remaining scope; 9to1OS remains separately staged.

Source read-back: native revision `AHj4eMQi_FaewmbEiK6tLlKz84Cg1W08unRVHfHpzaVmW0UIFui6lhC7E38AItSGnwT_41zWogIYC7dXhlaxOeSBcR7WYMgP5IPnfudwisE`; modified 2026-10-16T19:43:06.150Z. Reconcile existing repository mirror and historical source records without pretending old evidence validates new requirements.

Root Codex owns shared host contracts, integration and independent acceptance. Existing Browse, Home and Files workers retain their owned components. OpenCode is orchestrated on Spaces Home; Chat correctness is transferred to the Home worker after its current test batch. One canonical working tree: `/home/jacob/Documents/9to1`, current branch `opencode/m3-shell-wiring-20261016`. Heavy builds are serialized. No remote merge/deployment claimed.

| Task | Work and acceptance | Dependencies/status |
|---|---|---|
| 9T-HOST-01 | Inventory actual source/builds, app roster, standalone entry points and every old main-shell function; map each to new reachable location; preserve canonical IDs and active ownership. | WORKING; root + component owners, no assumed runnable package. |
| 9T-HOST-02 | Normal packaged 9to1 entry launches unified Browse host; share Home Core/Dulche/accounts/permissions/configuration independently of visible tabs/window; no nested full shell. | WORKING — queued after inventory/contracts. Root integration owner. |
| 9T-HOST-03 | Real native CUI app surfaces and web/search in existing tabs, using typed 9to1:// routing, app policy, canonical resources and original actor lifetime. Preserve internal/web/CUI/privileged distinctions, groups/workspaces/splits. | WORKING — contract inventory. Native app route must not enter web search. |
| 9T-HOST-04 | Shared Home/New Tab sidebar: Apps, Dashboard, Models/Discover; New Tab adds canvas/search/explicit AI. Remaining Home functions reachable. Actual app registrations/providers; honest empty/errors. | WORKING — existing Home CUI implementation under test; shared host wiring pending. |
| 9T-HOST-05 | Every real app has working standalone entry sharing implementation/resources/revisions/permissions; same resource saved embedded and reopened standalone without visible main window. | WORKING — inventory. Canvas admission is currently same-process; Files lacks current standalone launcher; Write entry currently boots full Desktop.App. These are gaps, not accepted fallbacks. |
| 9T-HOST-06 | Forward-compatible migration/session recovery, captured profile/resource identity, unsaved work, background task continuity, close/reopen, draining callbacks. | WORKING — queued; tests must exercise original ownership and interrupted recovery. |
| 9T-HOST-07 | Package and exercise full first journey and per-app/platform functional/visual/keyboard/focus/permission acceptance; exact source/build/platform evidence; independent code review. | WORKING — component tests in progress, packaged journey not yet executed. Windows/Android runtime unavailable here = Unvalidated. |
| 9T-HOST-08 | Continue retained full V2/original/annex backlog after common-host milestone, including complete Spaces/Home/Files/Browse and other actual apps/standalone roster. | WORKING — retained full scope, no reduced definition of finished. Model Groups #34 remains linked with its distinct master-write blocker. |

Implementation defaults (not owner-specific decisions): app selection from empty New Tab fills it; app selection from Home opens an app tab retaining Home; sidebar New Tab selects landing canvas; tab-strip + creates a tab; stable Home entry and configurable tab placement. No invented mandated sidebar order, initial selection or Home closability.

First required packaged journey: **Launch 9to1 → Home → website → native app tab → meaningful operation → save → reopen same canonical resource in standalone executable.**

Component passes establish only exercised paths. Actual Gecko web hosting, independently admitted standalone processes, complete roster and supported-platform packaged acceptance remain open. Record WORKING/CHECKPOINT/BLOCKED/COMPLETED/VERIFIED separately; do not close this issue on compilation or one sample app alone.



<!-- codex-coordinator-cycle:20261017T211932Z -->
## Coordinator checkpoint — 17 October 2026, 22:31 BST

WORKING; acceptance NOT VERIFIED. Existing programme retained, no reset or public deployment. Exact OpenCode V2.0.32 session `ses_eb52e71e1ffeALmh6Ai1D7tLuL`, `opencode-go/beam-free` (live catalogue zero input/output/cache cost), branch `opencode/m3-shell-wiring-20261016`; independently inspected source through `2ba08e0ccb5699b8d87b21d8017ab669ef8da805` plus dirty edits. Actual worker tool activity observed.

Dispatched one bounded correction after an idle/empty-inbox check, then distinct findings for test ordering, existing Chat route composition, and new integration defects. Canonical registry/SpacesHomePage reuse is improvement, not accepted completion: unavailable action host, missing new command handlers, activation/close ownership and restoration remain open at review. Test `072752a5` still releases old load before newer selection completes; original deadlock repair is not repeated as new work. Linux browser permissions/profile isolation/history/lifecycle and actual visual journey remain unverified.

Full-thread inspection PARTIAL: main export first `msg_14ad18e1d0017VRF2Z7m1zevv7`, cutoff `msg_14bbc2405001JDKrTwxEuIU6Z9` at 21:19:45.744 UTC; selected complete chronological chunks and owner corrections inspected; latest pre-dispatch reconciliation through idle `msg_14bbe6ca7001t9jjGa3BIswLkh`. Four predecessor candidates exported but not fully inspected; raw-output gaps recorded. No current child sessions returned. Each cycle must re-read full available history, not use this summary instead.

Evidence: complete compiler log 45 passed / 5 failed (exact tested source not recorded); historical Mail fragments 69 / 3, full raw output unavailable. No independent native/web visual acceptance. Full current source-to-requirement mapping remains incomplete; all existing 1232 source clauses and full latest master/companions retained. Latest Haven+Build pack hashes verified; Professional references inspected, Glow image and standalone Sandbox plugin contract unavailable.

Continuation/deadline guard `9to1-opencode-weekend-guard.service` active, one owner, exact session, persistent STOP/PAUSE, free-price check, duplicate prevention, terminal Monday 19 October 07:00 BST cutoff and active sleep inhibitor. 17 checks and isolated live active interruption/checkpoint verified. Automatic crash recovery and exact three-attempt provider fallback NOT VERIFIED and disabled; failed turns require coordinator recovery. Shared service never restarted. Saved half-hour task ID `9to1-opencode-work-coordinator-prepared-17-october-2026-revised-17-october-2`; next saved check 22:49:31.911 BST. Saved schedule has no UNTIL bound; local guard enforces managed-worker cutoff; app schedule still needs supported deactivation at completion. No 22:00 occurrence claimed.

Durable state/control/coverage: `/home/jacob/Documents/Codex/2026-10-17/new-chat/work/9to1-coordinator/` and `work/9to1-cycle-20261017T211932Z/`. Human checkpoint and suite-level (not full requirement-level) matrix: `/home/jacob/Documents/Codex/2026-10-17/new-chat/outputs/9to1-coordinator-checkpoint-2026-10-17.md` and `9to1-acceptance-checkpoint.csv`. Continue substantive Spaces/Haven repairs and actual journey, then Files and complete suite priority order; no premature Studio diversion.

<!-- codex-continuation-20261018 -->
## Coordinator continuation — 18 October 2026

Branch `opencode/m3-shell-wiring-20261016` remains the active source. Latest verified local commits include `f277b2e3` (selected-Space restoration), `6bdbe221` (Linux WebKit navigation state), `55eb8d81` (canonical Spaces AI draft admission), `4ede0877` (Linux WebKit profile isolation/events/lifecycle), and `2bc88977` (held-load stale result ordering).

Current state:
- Spaces Home canonical CUI and controller fixes are complete and tested.
- Haven Spaces tab reuses canonical `SpacesHomePage`, canonical `SpaceRegistry`, and a real `NewChatPage` Chat binding.
- Spaces selected Space is preserved in the tab address and restored on reopen.
- Linux Haven uses the Browse-based host and WebKit engine; build/publish is reproducible for x64 and ARM64.
- Linux browser site permissions are not exposed by Avalonia `NativeWebView`; permission enforcement remains genuinely unsupported and is not faked.
- `MailWorkspace.cui` is canonical; 3 TLS/network Mail integration tests remain visible.
- CUI compiler has 5 remaining `apps/Web/*` import-root failures.

Exact remaining blockers:
- No desktop automation tool is available to drive/capture the full Haven same-tab journey.
- Spaces Open launches a real Chat page, but Haven has no `ISpacesNavigationHost` for mode/space destination routing beyond that page.
- Linux WebKit permissions are unsupported by the available backend API.

Next executable priority after these dependencies:
- Files same-tab hosting and isolated file/project operations.

## Coordinator continuation — Spaces admission and restoration

- Spaces Haven sessions now perform Home profile/service admission before activating the canonical Spaces page.
- Selected Spaces are persisted through `SpaceRegistry` and restored from the tab address.
- Tab command tasks are joined before page disposal; closing one tab does not dispose shared services.
- Real Chat binding remains via `OverlayChatSessionFactory`; Chat drafts are retained while the tab remains open.
- Linux WebKit profile isolation, committed navigation state, script execution, popup handling and safe disposal are implemented.
- Linux site permissions remain genuinely unsupported by Avalonia `NativeWebView`; no permission callback is invoked or faked.
- Exact blockers remain desktop automation/capture and the unavailable Haven `ISpacesNavigationHost` for non-Chat Space destinations.
