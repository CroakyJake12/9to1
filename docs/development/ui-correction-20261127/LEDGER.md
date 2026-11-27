# UI correction pass — 27 November 2026 · ledger

Source of instruction: owner-supplied **9to1_UI_Correction_Pack_20261127.zip** (received
`~/Downloads`, 27 Nov 2026). Execution order per `00_START_HERE.md`:
shared controls → Spaces routes and composer → Build creation/opening → remaining visual
consistency → native qualification. Pack references are Atlas/Specification **v5**; the
26 November migration record (`docs/development/native-migration-20261126.md`) already
lists the Build v5 journey and popup parity as open.

## Baseline (established 27 Nov ~09:05 GMT)

- Checkout `main` at `1fff3418cf5d35eaec956b1f07971741a2355423`, clean tree (the pack's
  morning-build revision `332e415cfb` plus one Dulche resume fix).
- Running managed dev copy: run `4e199c85-1b4d-4c75-9f61-d5802ecb1e61`, source
  `332e415cfb`, runtime `~/.local/share/9to1-development/runtimes/native-migration-332e415cfb-20261127-081701`,
  profile `~/.local/share/9to1-development/profile` (status JSON captured in session).
- Display: X11 `:0`, screen 4384×2370 physical; app window 3288×1659 physical at (10,10)
  (GNOME scale applied; exact scale factor to be recorded per acceptance §1.1).
- Live window observed at 09:12Z showing Build → New project identical in shape to the
  pack's `build-new-project.png` evidence (existing-empty-folder contract, raw provider
  metadata, separate setup-draft identity). Pack evidence therefore reproduces against
  the running build.
- The UI-02 "browser cannot open this destination" message was NOT visible on the live
  window at 09:12Z; it is event-driven (recording shows it persisting 00:00–00:22).
- Evidence directory: `~/.local/share/9to1-development/ui-correction-20261127/`
  (`before/`, `after/`, `logs/`).

## Native acceptance — corrected build 5dd1054360 (candidate `ui-correction-5dd1054360-20261127-102737`)

Published guarded (receipt `receipts/ui-correction-5dd1054360-...-receipt.json`, schema v2,
exit 0, floor honoured, observed min 9625 MiB), frozen, approved
(`approved-ui-correction-5dd1054360.json`, manifest sha 4cf20808…), reloaded through the
managed copy's own close path at 10:35Z; startup clean, DB integrity passed, session restored.
Window 2850×1894 physical at (767,274); screen 4384×2370 (capture/pointer mapping recorded).
Note: XTEST clicks initially hit windows stacked above Haven — the target window must be
raised before each interaction round; keyboard (Ctrl+T) and pointer then delivered.

| Check | Before (332e415, pack evidence + repro) | After (this build) | Evidence |
|---|---|---|---|
| Model picker opens from the header pill | opened, edge-clamped | opens, anchored with margin | `after/native-picker-open.png` (panel-region diff 233k px vs closed) |
| Outside click dismisses root popover | **stayed open** (repro-B) | **closes** via ClosePanel owner | `after/native-picker-after-outside.png` |
| Trigger re-press toggles closed | stayed open (repro-E) | frame identical to closed baseline | `after/native-picker-toggle-close.png` (0-pixel diff) |
| Escape dismisses | worked | still works (panel region clean; only hover tooltip differs) | `after/native-picker-escape.png` |
| Spaces default presentation | full manager form (Parent Space/Context/Model and access/Capabilities) | selection view + action row + explicit **Space settings** entry; definition body absent | `after/native-spaces-landing.png` |
| Explicit Space settings click | n/a | renders; toggle-to-managing click itself unverified (first probe mis-hit Create, which opened the real Space Builder draft, unsaved and abandoned) | `after/native-spaces-settings.png` |
| Stray-tab cleanup | — | extra tab from input testing closed | `after/` + `/tmp/opencode/cleanup.png` |

Not yet qualified natively (queued): multiline/emoji composer behaviours, disposable
Unicode-title save/reopen round-trip (source fixed + unit-compiled), Build name+parent
creation, model settings A256 layout, notifications A treatment, Glow/Professional matrix,
tab-overflow hit areas at 1280×720.

## UI-04 recent-chat compaction (committed slice pending — working tree, 2026-11-27 PM)

Compact navigation on the Spaces sidebar per A046 while keeping the real owner commands:
`SpacesHome.cui` drops the standing Search button and the always-visible instruction line; the
query input now runs `spaces.search-conversations` through the authored CUI **Enter SubmitAction**
(`{Binding Event}` → `CuiInputSubmitRequest`, fenced against the live query revision inside the
existing handler); Clear only renders when a query is typed (`spaces.conversationQueryActive`);
the status text only renders for real states (`spaces.conversationNoticeVisible`) — typing drafts,
results no longer print "Showing the 20/50 …" counts (heading flips Recent chats ↔ Title matches).
Fallback bindings added in `SpacesHomeCuiBindings.cs`. Typing still performs **no** implicit
history read (owner design and assertion preserved).
Tests: `SpacesConversationRouteTests`/`SpacesSavedTitleSearchTests`/`SpacesHistoryAuthorityTests`
56 total — 47 pass; the 9 failures are each present in the stored baseline set
(`/tmp/opencode/spaces-failures-baseline.txt`, fixture disposal/thread-affinity family) —
`logs/test-spaces-submit-search-2.log`. Three call sites now press the real Enter gesture
(`SubmitSearch` helper) instead of the removed button; the intent-parameter path remains
(for `ClearConversationSearch` and direct dispatch).
Native qualification of the compact sidebar and Enter behaviour queued.

## UI-06 state (2026-11-27, this session)

Implemented against the real owners. `_manualFolderPath` stays the canonical final child;
`TemplateProjectName` + `TemplateDestinationParent` bindings derive it (`DeriveTemplateFolderLocked`),
`ValidateCreationTarget` enforces parent-exists / child-absent-or-empty with live
`StartCreationHint` text, and the New-surface picker selects the **parent**. The
`dev.templates.check-and-create` chained command runs the existing review then the existing
single-creation owner (`RunCheckAndCreateAsync` refuses if inputs changed in between); creation
materialises the named child via `Directory.CreateDirectory` before `registerTemplateProject`
and never deletes folders on error paths (an empty child is reused by a retry). Landing CUI now
shows a `dev-landing-identity` panel (name input, parent input + Choose, hint) and a "Starting
point" template section with one footer Wrap (Check and create / Review template files /
Create reviewed project; review status + file list retained below it).

Tests: new owning `Atlas_A285_project_name_and_parent_destination_create_one_child_folder_and_open_it`
(name + parent → derived child created once through the register owner → opened; occupied child
refuses with everything retained); `CanonicalCompatibleTaskContextCompositionTests` 65P/5F —
failure set byte-identical to clean baseline (`/tmp/opencode/dev-base.txt` vs `dev-mine.txt`,
`logs/test-devlanding-a285-2.log`, `logs/test-devlanding-baseline.log`).
`HavenOS.Dev.Tests ~DeveloperLanding` was **1F at baseline** (stale expected-action list missing
`dev.setup.*`); contract refreshed to the full current inventory plus the new command and identity
bindings → now 2/2 PASS (`logs/test-dev-cui-docs-2.log`).

## Stage ledger

| Item | Priority | Status | Notes |
|---|---|---|---|
| UI-01 shared popup lifecycle + containment | P0 | **native pass** (picker set) | outside/toggle/Escape verified on installed candidate; composer emoji/Attach/Options popups still to walk |
| UI-02 persistent navigation/action state | P0 | source+compile; native n/a this session | refusal text now self-clears on accepted navigation or page-idle; original hang condition not present on the live copy (not visible 09:12Z+) |
| UI-03 Spaces route identity | P0 | slices native-passed | definition body behind explicit settings (above); route-by-route headers/groups/specialists walk + A046 auto-entry remain (auto-open variant tried & reverted — see commit note) |
| UI-04 Spaces conversation/context/readable state | P0 | title fixed; recent-chat compaction tests pass vs baseline | context shelf (attach/inspect/remove), model-row separation, recovery grouping still open; verbose search block now compact (see section above) |
| UI-05 Specialists presentation | P1 | pending | A050 cards |
| UI-06 Build creation contract | P0 | **source + tests pass**; native pending | name + parent → derived child, one chained Check-and-create action; see UI-06 state above |
| UI-07 Build starting points/drafts/open | P1 | pending | A286/A108/A124 |
| UI-08 model/settings + notifications | P1 | pending | A015/A256 |
| UI-09 shared visual consistency | P1 | pending | tokens/components |
| Native qualification (03_NATIVE_ACCEPTANCE.md) | — | partial pass above | §1 substantially covered; §2–§4 queued |

## Reproduction log

- 09:12Z `before/live-initial.png`: live Build → New project shows the exact legacy
  creation contract (UI-06 reproduced on installed app).
- 09:20–09:30Z `before/repro-A..E`: model picker on the installed build — outside click
  does NOT dismiss (B), Escape DOES (C), trigger re-press does NOT toggle-close (E).
  Panel shows visible scrollbar + horizontally truncated rows (`Capacity unknown ··`).
  Reproduced with `drive_window.py` on window 0x3e0001b; captures under `before/`.
- 09:16Z `before/popup-model-picker-open.png` misnamed pre-fix frame superseded by repro set.

## Test evidence

- Baseline (clean `1fff3418c`): `Shared_model_panel_bounds_scroll...` 0/3 FAIL at line 83
  (`Pending:` prefix from `f07c06ca6`, test never updated) — `logs/baseline-sharedpanel-test.log`.
- Fixed candidate: same test 3/3 PASS (360/720/1440 widths) with new scenarios —
  outside press dismisses via ClosePanel owner; inside-panel press never dismisses;
  full decision view not dismissed by blank press — `logs/test-sharedpanel-dismissal-9.log`.
- Focused test command per `docs/development/workflow.md` guard + serial SDK lock.

## Unicode/mojibake correction (UI-04 title item)

- Source root cause: `NewChatPage.cs` contained literal `"â€¦"` (cp1252-mojibaked `…`) in the
  conversation-title truncation, plus baked mojibake in ~15 other product files (Boards live
  markers, Present/Write/Settings/Go/UniversalSearch chrome, Call prompt, Notes/Branch
  excerpt ellipses). All corrected deterministically (cp1252 round-trip reversal); two
  Boards files whose tokens were double/triple-corrupted beyond recovery replaced with
  meaning-preserving plain text (documented in diff).
- Historic saved title (`haven.db` conversations row 26 `…ALORAâ€¦`) deliberately NOT
  rewritten (existing history preserved; single known row; app DB must not be written
  while the instance runs). New titles take the fixed path; native save/reopen check queued.

## Decisions and boundaries

- Preserve runtime, provider integrations, histories, project identities, permission
  boundaries and operation state; presentation/behaviour changes go through existing
  owners (pack §Keep the handoff accurate).
- Independence work (`EARLIER_INDEPENDENCE_AUDIT`, migration ledger) stays separate; not
  re-certified here.
- No inference services are started or reconfigured as a side effect; native Send/Stop
  checks stay blocked unless the resident service is genuinely available.
