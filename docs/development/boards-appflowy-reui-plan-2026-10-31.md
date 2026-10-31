# Boards route correction: AppFlowy re-UI first, then transition — 31 October 2026

## Owner decision recorded here

Stated on 31 October 2026:

> the intended boards surface is appflowy re-ui first, then transition that re-ui into the 9to1 boards

> if we rebuild appflowy then turn it into boards, we then don't have to re-add appflowy functionality.
> we then just transition that into what we want 9to1 boards to look and function like and add more
> stuff on top

and, on preservation:

> but also commit the source pulls to remote  → see [donor-snapshot-preservation-2026-10-31.md](donor-snapshot-preservation-2026-10-31.md)

Consequence for the current code: the running CUI Boards is **not** an AppFlowy re-UI. Its own document
`9to1 Workspace/Boards/app/Boards.cui` (42 KB, 2,927 lines across 30 first-party documents) reproduces the
**previous first-party Boards layout** — sidebar with sections and pages, block toolbar, page surface. The
owner saw exactly this and named it: "it's the same as the old boards; doesn't look like a cui rewrite of
appflowy". That is correct, and stage 1 was skipped.

## What the existing "DonorMirror" corpus is, measured

It is not stage 1. Measured over `9to1 Workspace/Boards/CUI/DonorMirror/appflowy`:

- 824 documents, 20,710 lines, 11,627 elements
- **228 documents contain ≤6 elements** — e.g. `shared/af_image.cui` is four empty
  `<Container Type="Vertical Stack">` elements named after Flutter layout arguments

So the corpora are structural transliterations that compile and classify as `mirror` in the surface gate,
and they can never raise an app's evidence tier. A real re-UI re-renders what the donor screen actually
shows, from the donor's own UI source, and is then wired to canonical 9to1 state.

## Measured translation volume (denominators, not estimates)

| Donor UI surface read for the re-UI | files | LOC |
| --- | ---: | ---: |
| AppFlowy board view `lib/plugins/database/board` | 14 | 3,844 |
| AppFlowy grid, cell, calendar, filter/sort widgets | 201 | 28,658 |
| AppFlowy Flutter app UI in total (upper bound) | 1,526 | 243,832 |
| LibreOffice `.ui`/`.hrc` (Write / Present) | 422 / 279 | 191,014 / 198,145 |
| CodeOSS workbench TS/CSS (Build) | 5,172 | 1,926,214 |
| Gitea templates (Build/Sites) | 585 | 29,963 |
| Thunderbird front-end (Connect) | 155 | 74,171 |

## Estimates, labelled as estimates

Basis stated with each figure; the weakest input is the authoring rate.

- **Assumed verified-authoring rate: 60–120 CUI lines/hour**, measured from one working stretch on
  31 October 2026 in which the authored output (3 CUI menu entries in an existing document, a 44-line
  runtime function, 113 lines of new tests) was accompanied by 6 guarded build units, 3 test runs, 4 test
  authoring mistakes corrected, and 4 defects fixed in the on-screen capture/input tools. Treat this as
  the softest number on this page.
- **Boards stage 1** (AppFlowy's board screen re-rendered in CUI, unchanged): **2–4 focused working days**.
  The board view itself is 14 files / 3,844 LOC; the visible screens to mirror are roughly a dozen
  (board page, group column + header, card, hidden groups, card editor/row detail, settings bar,
  add-card affordances).
- **Boards stage 2** (transition that re-UI into 9to1 Boards: canonical Board/Page/Block identity, real
  data, spec-required save/draft behaviour, then additions on top): **a further 2–3 days**.
- **Copying AppFlowy's database-view parity as well** (grid/calendar/cell editors, 28,658 LOC):
  **3–6 weeks**, and this is an open scope question, not a coding unknown — see
  `OPEN-2026-10-31-BOARDS-DONOR-VIEW-SCOPE` in `docs/specification/open-decisions.json`.
- **Whole shipmas scope, all apps' donor re-UI plus transition: roughly 8–14 weeks single-worker.**
  Build/CodeOSS dominates: a faithful workbench re-UI alone is **6–12+ weeks**; the spec-named subset
  (explorer/editor/terminal plus the Sites and Stacks panes) is **~2–3 weeks**. Write/Present at
  **2–4 weeks each** (LibreOffice `.ui` files are mostly dialogs; the editing surface is custom drawing).
  Connect email **~1–2 weeks**. Gitea-backed Sites **~3–5 days** for the authoring surface.

## Why "just converting one language to another" understates the cost — three measured reasons

1. Mechanical transliteration was already tried and produced non-UI stubs (228 of 824 documents above).
2. CUI expressiveness gaps surface per screen and must be fixed in the framework. Measured instance from
   31 October 2026: `Active="{Binding …}"` was applied once at mount, so after a document swap the editor
   surface could never appear; 155 first-party elements gate presentation that way. One blocking gap was
   found on the **first** surface exercised on screen. Fix cost: one working stretch (investigation, fix,
   two regression tests that fail 2/2 without the fix).
3. Verification cost is now real but slow: on-screen capture and input driving only began working on
   31 October 2026, after four tool defects. Each surface needs its own capture pass.

## Queued behind the re-UI (owner instruction, 31 October 2026)

CUI **theme issues — themes do not look as intended** — are deliberately **not** being fixed now.
Evidence that they are real and not assumed: four of the six failures pinned in
`eng/development/cui_test_baselines.json` are theme/appearance tests
(`CuiThemeTests.Applying_global_theme_updates_active_theme_for_subsequent_surface_loads`, and
`CuiSceneAppearanceResourceTests.Opaque_button_states_use_readable_existing_ink_and_same_scoped_backend_brushes`
across four appearance variants). They stay in the queue and re-enter work after the AppFlowy re-UI.
Known separate defect, already pinned, also deferred: `CuiControlLoader.ThemePreferenceChanged` uses the
ambient static `Dispatcher.UIThread`, a cross-thread race.

## The temporary comparison pair, and its retirement condition

The owner asked for `Legacy XX` / `XX` pairs so before-and-after can be tested side by side, and said
"eventually the legacy surfaces will be removed".

- `Boards` — the CUI surface that runs today (first-party layout, plus the stage-one AppFlowy re-UI mounted
  over it). Launch, measured working on this PC:
  ```
  DISPLAY=:1 XAUTHORITY=/run/user/1000/gdm/Xauthority \
  HAVEN_DATA_DIR=/tmp/opencode/boards-cui-evidence/profile \
  "<guarded-dotnet>" "9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll"
  ```
  Linux and Windows targets both exist as of commit `94fc2a3a`. The apphost in that directory does **not**
  start here (no shared .NET 10 framework installed), so the dll is run through the guarded SDK host —
  recorded so nobody mistakes a working launch recipe for a packaged app.
- **Stage-one seam, as of `af15ed29`:** in the running app, File → "AppFlowy re-UI (stage one)" mounts
  AppFlowy's board structure over the same board, and "Leave AppFlowy re-UI" returns. To launch straight
  into it for a side-by-side test, set `BOARDS_STAGE_ONE_PREVIEW=1`; it calls the same
  `ShowStageOneSurfaceAsync`, so there is no second code path.
  Measured on-screen evidence, window `0x420000f`, `AppFlowy stage-one document diagnostics: 0`:
  `/tmp/opencode/e4.png` sha256 `41ccc699a414…` (1:1 crop `e4-crop.png`), after the menu fix `66673e50`.
  Before that fix the same surface rendered the group options inline: `/tmp/opencode/e3.png`
  sha256 `d50eab0faee9…`.
  Use a **separate copy** of the data directory for the second process (`profile-copy` here) so the pair
  never writes the same board file from two sessions.
- `Legacy Boards` — what 9to1 shipped before the donor route. The installed frozen package
  (`haven 0.1.0+test20261026.git59d66496`) **cannot serve as the legacy reference on this PC**: launched
  on 31 October it renders a "Shutdown not complete" diagnostic page reporting repeated
  `/usr/lib/haven/libcakes/canvas_motore_poc.so: cannot open shared object file` failures. The usable
  legacy entry is the source-built `9to1 Workspace/Boards/Host/HavenOS.Boards.Host.csproj`.
- Retirement condition, so this is not mistaken for a permanent fork: when `Boards` reaches the same
  spec behaviour the legacy surface demonstrably had, and the pair has been compared on screen, the
  legacy entry is removed and only the shared implementation remains. The pinned "one implementation per
  app" requirement is unchanged; the pair is a testing scaffold with an end condition.

## Owner direction, 31 October 2026 late evening — supersedes my "route it through CUI Actions" approach

Owner words, in order: *"like u don't even need to use cui actions rn literally just rebuild the interface in
cui"*, *"and make it work"*, *"but you can do stuff in the native rust"*, *"rather than forcing it inside of cui
actions"*, *"just wire up the existing code to new ui"*.

Reading applied: **the new AppFlowy-shaped UI must be laid out in CUI, and it must run the behaviour 9to1 Boards
already has.** Do not invent a second command pipeline for it and do not force capability work into CUI action
syntax. Where behaviour already exists in native/host code, call it. That retires the parallel stage-one command
set (`Donor*`) as the wiring mechanism.

### Why this also explains the input blocker

The stage-one surface runs in a **separate preview host** (`BOARDS_STAGE_ONE_PREVIEW=1`, its own window, log line
`[CUI Boards] Root: Panel — awaiting mounted CUI layout`), while the first-party `Boards.cui` surface is the one
the owner uses and clicks. The parallel host is therefore both the duplicate-implementation problem (pinned owner
rule: one implementation per app) and the leading suspect for the frames never changing. Hypothesis, not yet
proven: mounting the AppFlowy-shaped layout into the working surface removes the problem entirely.

### Measured mapping: new UI element → existing code to call

Verified existing handler cases in `9to1 Workspace/Boards/app/BoardsViewModel.cs` (this is the input-proven
surface): `AddPage`, `AddSubpage`, `AddSection`, `AddCanvasPage`, `AddWritePage`, `OpenRecentPage`, `OpenBoard`,
`NewBoard`, `SaveBoard`, `SaveAsBoard`, `BoardTitleBox`, `PageTitleBox`, `BoardProperties`, `ToggleTheme`,
`ApplySectionIconChoice`, `SuggestSectionIcon`, `NewMenu`.

Verified existing adapter calls in `9to1 Workspace/Boards/app/ContractSessionAdapter.cs`:
`AddSectionAsync` (:1255), `AddPageAsync` (:1271), `RenameSectionAsync` (:1457), `RenamePageAsync` (:1506),
`DeleteSectionAsync` (:1239), `DeletePageAsync` (:1551), `MovePageAsync` (:1582).

| new CUI element | reuse | status |
| --- | --- | --- |
| sidebar `+ New page`, tab `+` | `AddPage` / `AddSubpage` view-model cases | exists, input-proven |
| section `+` (page in that section) | `AddPageAsync(sectionId, title)` | adapter exists; needs the section id argument from the row |
| section header inline rename | `RenameSectionAsync` | adapter exists; **no view-model case found** in the list above → one dispatch case needed |
| section `⋯` → delete | `DeleteSectionAsync` | adapter exists; same note |
| sidebar page row, card open | `OpenRecentPage` + the existing page-selection route into the block editor | exists |
| board name / page title boxes | `BoardTitleBox`, `PageTitleBox` | exists |
| Save / Save As / new / open | `SaveBoard`, `SaveAsBoard`, `NewBoard`, `OpenBoard` | exists |
| dark theme (AppFlowy's default look) | `ToggleTheme` + Home-owned theme state | exists; theme *appearance* defects stay queued behind this work per owner instruction |

Not present anywhere (still genuine capability gaps, to be closed in the host/infrastructure layer rather than by
faking CUI actions): page/board **search**, **filter**, **sort**, board **layout settings**, hidden-section
state, reference views, and the grid/calendar views. The donor's controls for these may now be *rendered* for
visual parity (the owner's "rebuild the interface" instruction overrides my earlier no-dead-controls rule for
stage one) as long as they are labelled in code as not yet wired.

### Next unit, in order
1. Port the AppFlowy-shaped layout (sidebar 268 / tab row 40 / section column 256 / card 226 / footer under the
   cards) into the **working** `Boards.cui` presentation, driven by the existing view-model state and bindings.
2. Delete the preview seam and the `Donor*` command duplicates once the ported surface covers them; keep the
   stage-one document as a reference until then, and keep the `Legacy Boards` comparison pair per the recorded
   retirement condition.
3. Wire the two adapter calls that lack a view-model dispatch case (`RenameSectionAsync`, `DeleteSectionAsync`) —
   host-side, not CUI-side.

## "Wire everything" inventory — 31 October 2026, owner correction

Owner correction: *"no wire everything. but hopefully wiring should be easier than forcing via actions"*. So the
visual-parity allowance above is withdrawn: **every** donor control in the re-UI ends up connected to real
behaviour, by wiring host code rather than by authoring CUI `<Actions>` with bound parameters. Statuses below are
measured against this repository, not assumed.

| donor control in the re-UI | wiring status | where it goes |
| --- | --- | --- |
| sidebar page row, board card → open page | **EXISTS** | `BoardsViewModel` `OpenRecentPage` + the existing selection→block-editor route |
| section `+` new page in that section | **EXISTS** | `ContractSessionAdapter.AddPageAsync(sectionId,title)` :1271 |
| section header inline rename | **THIN** | adapter `RenameSectionAsync` :1457 exists; no `BoardsViewModel` dispatch case → add one |
| section `⋯` → delete section | **THIN** | adapter `DeleteSectionAsync` :1239 exists; dispatch case missing |
| page rename in the tree | **THIN** | adapter `RenamePageAsync` :1506 exists |
| move page between sections | **THIN→GAP** | adapter `MovePageAsync` :1582 exists; the donor's **drag** input has no CUI/render seam |
| tree section collapse chevron | **THIN** | view-state only, host-side; must persist like the donor does |
| hidden sections column + `hide section` | **GAP** | 9to1 Boards has no hidden-section state or per-view layout settings to persist it in |
| board settings panel (card size/layout, show icon/cover, collapse hidden) | **GAP** | no settings model on the board document; needs a persisted per-view layout setting |
| search | **GAP** | measured: no search API anywhere in Boards or the shared layer |
| filter | **GAP** | no board filter model |
| sort | **GAP** | no board sort model |
| open-pages tab strip (switch/close) | **GAP** | the Boards host shows one page at a time; no tab stack exists |
| `+ New page` at the space level | **GAP** | a 9to1 board page always belongs to a section; needs either a root-page data model or an explicit owner decision (see `OPEN-2026-10-31-BOARDS-DONOR-VIEW-SCOPE`) |
| card hover accessories (edit / more options) | **GAP** | no hover seam in the CUI renderer |
| group options popover limited to 168 x 300 | **GAP** | popover max-size is not expressible today |
| dark theme like the screenshot | **EXISTS** | `ToggleTheme` + Home-owned theme state; the *appearance* defects stay queued behind this work per the owner's earlier instruction |
| workspace menu, account cluster, notifications | **EXISTS in Haven, not in the standalone host** | must come from the SAME shared Home-owned controls (pinned one-implementation rule), not a local copy |
| grid / calendar views | **GAP, owner decision pending** | 28,658 donor LOC; `OPEN-2026-10-31-BOARDS-DONOR-VIEW-SCOPE` |

Sequencing I will follow: port the layout onto the working surface → wire the EXISTS/THIN rows (they are mostly one
dispatch case each) → then close the GAPs that the board flow actually needs (tab strip, settings model, hide +
hidden column, search) before taking grid/calendar parity, which is the pending owner decision.

## Route correction after the owner's check question (31 Oct, ~21:15)

Owner asked: *"wait, did you take the current boards ui and wire it up to appflowy"*. Honest answer at that moment:
**not fully.** What `0fd48244`+`1061a0f9`+`56cc58d8` did was keep the FIRST-PARTY Boards layout as the base and paint
AppFlowy's measurements onto it (268 sidebar, inset 8, nav row order, 40 tab strip, `+ New page` calling the
existing `AddPage`). The AppFlowy-shaped interface itself (`AppFlowyBoardStageOne.cui`: page-tree sidebar, tab
strip, board columns with bordered cards and `New page` footers) was still a **separate document in a separate
preview host with a duplicated projection and its own `Donor*` commands** — the duplication the pinned
one-implementation-per-app rule forbids.

Restated route to execute next, in this order:
1. Make the **AppFlowy-shaped presentation** the Boards presentation inside the working host (same window, same
   `BoardsViewModel`, same `ContractSessionAdapter`), instead of a preview seam.
2. Drive it from the working surface's own patterns, copied from live code rather than reinvented:
   `<Action name="OpenRecentPage" command="OpenRecentPage" parameter="{Binding page.Target}" />` with
   `case "OpenRecentPage": if (parameter is RecentPageCard …) await OpenBoardPathAsync(recentPage.Path,
   recentPage.PageId)` (`BoardsViewModel.cs:578-581`) and cards as plain records exposing `Target`/`Id`
   (`RecentBoardCatalog.cs:172-177`).
3. Delete `AppFlowyBoardStageOneSurface.cs` and the `Donor*` commands once the merged surface covers them; keep the
   stage-one document only as the layout reference until then, and keep the `Legacy Boards` pair until the merged
   surface is on screen (retirement condition already recorded).
4. Geometry to carry: sidebar 268, workspace row 32, search 30, new-page 30, tab row 40, tab width 200, section
   column 256, card 226, card margin 3/4, footer directly under the cards.
5. Traps already measured, so they are not rediscovered: grid placement attributes do **not** reach children of
   conditional branches (wrap in one `Container` carrying `Column=`); only registered palette names exist and
   `CuiPanelBrush` measures equal to the workspace material in the current light theme (the queued theme pass is the
   real fix for surface separation); `Repeat` item members need `Target`-style cards.
