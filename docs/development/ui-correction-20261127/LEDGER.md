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

## Stage ledger

| Item | Priority | Status | Notes |
|---|---|---|---|
| UI-01 shared popup lifecycle + containment | P0 | source+tests pass; native re-check pending reload | Backdrop outside-press root-popover dismissal implemented in shared `HavenSharedPanelBackdropDismissal`, wired by production host + owning headless test (3/3 pass). Escape/trigger/close already worked on live build; outside-click reproduced failing and is the fixed path |
| UI-02 persistent navigation/action state | P0 | source done; native pending | `UnifiedHomePage` refusal text now clears when navigation proceeds or this page's actions go idle (generation-fenced); accepted navigations clear it too. Root hang itself not reproduced in-session (Browse `_busy` window had cleared by 09:12Z) |
| UI-03 Spaces route identity | P0 | in progress | Legacy form owner `Spaces/UI/Home/SpacesHome.cui` |
| UI-04 Spaces conversation/context/readable state | P0 | title source FIXED | `â€¦` mojibake literals corrected in product source (see log); Spaces shell work continuing |
| UI-05 Specialists presentation | P1 | pending | A050 cards |
| UI-06 Build creation contract | P0 | pending | `Dev/UI/DeveloperLanding.cui` vs A285 — reproduced live at 09:12Z |
| UI-07 Build starting points/drafts/open | P1 | pending | A286/A108/A124 |
| UI-08 model/settings + notifications | P1 | pending | A015/A256; also: `More settings` reachable-by-scroll assertion updated (pending rows moved it below 420 fold); full A015 material parity remains open |
| UI-09 shared visual consistency | P1 | pending | tokens/components |
| Native qualification (03_NATIVE_ACCEPTANCE.md) | — | pending | Live copy 332e415cfb; reload per managed workflow after fixes |

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
