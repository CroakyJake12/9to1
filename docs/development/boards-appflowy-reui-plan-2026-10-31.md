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

- `Boards` — the CUI surface currently running (first-party layout today, AppFlowy re-UI after stage 1).
  Launch: `HAVEN_DATA_DIR=<profile> <guarded-dotnet> "9to1 Workspace/Boards/app/bin/Release/net10.0/CakeOS.Apps.Boards.App.dll"`
  with `DISPLAY=:1`. Linux and Windows targets both exist as of commit `94fc2a3a`.
- `Legacy Boards` — what 9to1 shipped before the donor route. The installed frozen package
  (`haven 0.1.0+test20261026.git59d66496`) **cannot serve as the legacy reference on this PC**: launched
  on 31 October it renders a "Shutdown not complete" diagnostic page reporting repeated
  `/usr/lib/haven/libcakes/canvas_motore_poc.so: cannot open shared object file` failures. The usable
  legacy entry is the source-built `9to1 Workspace/Boards/Host/HavenOS.Boards.Host.csproj`.
- Retirement condition, so this is not mistaken for a permanent fork: when `Boards` reaches the same
  spec behaviour the legacy surface demonstrably had, and the pair has been compared on screen, the
  legacy entry is removed and only the shared implementation remains. The pinned "one implementation per
  app" requirement is unchanged; the pair is a testing scaffold with an end condition.
