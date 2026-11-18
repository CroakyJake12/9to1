# Feature inventory — full refactor to the 18 November specification

Living document required by QWEN_STARTER_PROMPT.md and HANDOFF-007. Classify every existing capability before it is touched:
**Conforming** · **Adaptation** (keep behaviour, change presentation/placement) · **Conflict** (cite the specific requirement being violated) · **Additional compatible feature** (preserve) · **Incomplete** (specified, not implemented).
Evidence levels: `code` (source inspected) · `test` (focused check passes) · `native` (running dev copy observed) · `not observed`.

Product authority: `docs/specification/owner-references/2026-11-18-interface-refactor/` (SPECIFICATION.md `b6cd0617…`, Atlas v3.3 `75c0abad…`, SOURCE_PRECEDENCE.md).

## Actual implementation map (code-observed 18 Nov)

| Area | Real location | Notes |
| --- | --- | --- |
| Haven desktop shell (running app) | `9to1 Workspace/shared/src/Haven.Desktop/` (Views/Pages, Views/Shell/TopRail), `Haven.Application`, `Haven.Browser`, `Haven.Infrastructure` (BrowserTabHostManager), `Haven.UI`, `Haven.SharedHeader`, `Haven.Core` | AXAML-based host; solution `9to1 Workspace/shared/Haven.sln` |
| CUI framework | `framework/CUI/`, plus per-app CUI projects (e.g. `9to1 Workspace/Cards/CUI`, `Data/Cui`) | eng/9to1.ps1 `-Component cui` check lane; `eng/development/ci_cui_owned.py` |
| Home | `9to1 Workspace/Home/` (Source, UI, NativeUI, extensive Tests) | model picker tests exist (HomeModelPicker*) |
| Spaces / composer | `9to1 Workspace/Spaces/` | — |
| Boards | `9to1 Workspace/Boards/` (app, hui, Host, contract + tests); `Haven.Desktop/Views/Pages/Boards/BoardsPage*.cs` | appflowy donor packaging: `9to1 OS/packaging/boards-appflowy/` |
| Write / Present / Canvas / Cards / Data / Forms / Files / Planner / Studio / Sites / Stacks / Terminal / Wave / Picture / Motion / Connect / Mail / Assistants / Accounts / Browse / Build / Dev / MiniComputer / Root | matching `9to1 Workspace/<Family>/` folders | each has its own projects/tests |
| Web line | `apps/Web/` (Blazor WASM, needs `wasm-tools` workload — not installed locally; built in cloud/CI lanes) | separate host composition |
| Avalonia Home app | `apps/Home/src/AvaloniaHome/` | separate host |
| Android / 9to1OS | `9-1 OS (Android)/`, `9to1 OS/` (packaging recipes incl. Dulche llamacpp, shell, welcome) | delivery lane |
| Cloud services | `cloud/` (files-drive Node service, accounts, billing, cake-id-auth, public-sites) | owner's Drive work merged 17 Nov |
| Donor engines | `9to1 Workspace/Browse/Source/FirefoxGecko/`, Boards appflowy, Canvas rnote etc. | boundary: adapt behind contracts, never opportunistically refactor |

Build/check entry points: `eng/9to1.ps1 restore|build|test|verify|package-linux|package-windows -Component core|cui|home|spaces|shared`; guarded heavy builds via `eng/development/guarded_lowmem_build.py` (memory floor for the resident model host); owned-surface checks in `eng/development/checks.json`.

## Slice status

| Slice | Requirement IDs | Status | Evidence |
| --- | --- | --- | --- |
| S0 authority ingest + baseline | HANDOFF-001/002 | **done this session** (package verified 518/518; AGENTS.md + provenance.json + open-decisions.json updated; Haven.sln Release build running under memory guard — result recorded in build-baseline.md) | command output |
| S1 Haven shell tabs | HAV-SHELL-001, HAV-TAB-001–004, CTX-01, QA-01, THEME-004 | **implemented (this commit)** — visible X with accessible name on every closeable tab in both the CUI scene strip (shared TabStrip) and the AXAML twin; icon + left-aligned bold title; close selects right→left; closing the last tab opens the New tab surface; focus moves to the resulting tab; CTX-01 items added (New tab, Reload enabled per tab kind, Pin/Unpin, Mute disabled, Reopen closed disabled) | `code`+`test`: Haven.Desktop net10.0 Release 0 errors; Haven.UI.Tests 176/177 with new close-control test passing; sole failure proven pre-existing at pristine 7d6972325 (stash-and-rerun). Native on-screen retest NOT RUN — owner's running copy is the gate |
| S2 design system (accent treatments, Professional controls, scrollbars, typography) | THEME-001–005, CUI-001–004 | not started | not observed |
| S3 Home/New tab start surface | HOME-001–006, CTX-02 | not started (Home exists with dashboard/model-picker services + tests; conformance unverified) | not observed |
| S4 model picker | MODEL-001–006, CTX-10 | not started (HomeModelPicker* CUI controller/projection tests exist — prior slice work; verify vs new contracts) | not observed |
| S5 Settings three-tier taxonomy | SET-001–005, SET-ROW-001–004, 5.1.1 map, CTX-09 | not started | not observed |
| S6 contextual AI bar + per-app entry points | AI-BAR-001–004, CANVAS-002, APP-DATA-003 | not started | not observed |
| S7 Boards chrome + save/recovery | BOARD-001–003, BOARD-UI/MODE/SEL/CTX/SAVE | not started (BoardsPage + appflowy host; .9to1board codec + prior reopen tests) | not observed |
| S8 Spaces composer/run states | SPACE-UI-001–014, ASSIST-001–005, AUTOSAVE-001, SPACE-001–015 | not started | not observed |
| S9 browser tools/manage/inspectors | BROWSE-001–005, CTX-11/12 | not started | not observed |
| S10+ app-family sweep | 8.1–8.13 (WRITE, PRESENT, CANVAS, LEARN/Cards, DATA/FORMS, FILES/LINK, PLANNER/AUTO, MEDIA, BUILD/AI-STUDIO/GAMES, CONNECT/LIVE/MAPS/SHELF/PLAY, PROTOTYPE/PATHS/REPLAY, TERMINAL/VM/ADMIN/OS/LAUNCHER) | per-app status pending investigation; families exist as projects above | not observed |

## Capability register (per surface, filled before touching)

(Completed slice by slice; each row gets requirement IDs, Screen IDs, classification and evidence.)

### S1 shell tabs — code observations (18 Nov, `code` evidence, behaviour not observed)
- Shell tab strip: `Haven.Desktop/Views/Shell/TopRail/TopRail.axaml(.cs)` — TabStrip StackPanel inside a horizontal ScrollViewer with chevron scroll buttons; `BuildTab` renders centred bold title + length-heuristic underline, **no icon and no visible close X** (close only via right-click menu items: Rename tab / Close tab, `TabCloseRequested` → `MainView.axaml.cs:3321`). Conflicts HAV-TAB-002 (icon, left-aligned bold title, separately-focusable named X) and QA-01; THEME-004 (left align). `TopRailTab` record already carries `IconKey`/`IsCloseable` — presentation adaptation, data exists.
- Parallel implementations to reconcile: `TopRailFinalScene.cs` (popup menu incl. Close tab destructive-separated) and `TopRail.HavenScene.cs` (forwards `_havenOwnedScene.TabCloseRequested`) — one shell tab implementation must win (one-implementation-per-app rule).
- Browser web-tab strip (web content tabs): `Views/Pages/Browser/BrowserHavenScene.cs` DynamicUI "BrowserTab" template — routed Select + close "x" per tab; `BrowserPage.CloseTabAsync` selects right-then-left (index clamp) and creates a New tab surface when the last tab closes ≈ HAV-TAB-003, but focus movement to the resulting active tab and the recently-closed reopen extension are not implemented; tab context menu CTX-01 (Duplicate/Pin/Mute/Move/Close others/Close to right/Reopen) absent.
- Closest spec text: HAV-SHELL-001, HAV-TAB-001–004, HOME-001, CTX-01, INPUT-001–008, THEME-004. Screens: host-newtab, home-apps/models/assistants.
- Pre-existing failure recorded (not from this slice): `HavenUiFoundationTests.Button_hover_tint_uses_its_effective_background_and_skips_transparent_buttons` fails at pristine 7d6972325 (verified by stash-and-rerun, not by inspection). Left failing and reported rather than edited away; scheduled for the S2 hover/tint pass.
