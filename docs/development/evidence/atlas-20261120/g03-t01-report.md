# G03-T01 — Boards chrome CURRENT-STATE inventory (read-only investigation)

Repo: `/home/jacob/Documents/GitHub/9to1`. All paths below are relative to that root.
Investigated 2026-11-21. No repo files were modified. Claims I could not verify in code are marked **unverified**.

---

## 1. The actual Boards editing surface and its toolbar chrome TODAY

There are TWO live "Boards" surfaces plus a legacy HUI projection:

1. **CUI Boards editor (the real editing surface, "CakeOS.Apps.Boards.App")**
   - `9to1 Workspace/Boards/app/` — CUI-authored chrome + Avalonia host.
   - Main markup: `9to1 Workspace/Boards/app/Boards.cui` (top bar, sidebar, editor column).
   - Adaptive toolbar markup (the Oct-31 sprint addition): `9to1 Workspace/Boards/app/BoardsAdaptiveToolbar.cui` (416 lines).
   - Hosts that mount it:
     - Standalone: `9to1 Workspace/Boards/app/Program.cs:734` — `RefreshToolbarAndContext()` finds panel `ToolbarHost` and calls `ToolbarBuilder.Rebuild`.
     - Embedded in Haven: `9to1 Workspace/Boards/app/BoardsWritablePresentation.cs:465` (`RefreshToolbar`, same `ToolbarHost`/`ToolbarBuilder.Rebuild`), hosted via `9to1 Workspace/shared/src/Haven.Desktop/Services/BoardsWritableRichDocumentBrowseAppContentSession.cs:3` (`using CakeOS.Apps.Boards.App;`) and `BoardsRichDocumentBrowseAppContentSession.cs` (read surface).
   - Evidence of the Oct-31 delivery: `docs/development/evidence/boards-usability-2026-10-31/README.md` — "adaptive paragraph toolbar and fixed Add/Drawing/AI actions", "fixed actions stay visible while adaptive controls collapse" (`native-wide.png`, `native-final-narrow.png`). Also `docs/development/evidence/boards-single-toolbar-2026-10-30/`.

2. **Haven.Desktop Avalonia `BoardsPage` (secondary/legacy workspace page)**
   - `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Boards/BoardsPage.cs` — a flat `WrapPanel` toolbar of ~20 `ActionButton`s (line 33, 115–139: "+ Page", "Text", "Checklist", "Ink canvas", "Freeform", "Attach", "Save"…), plus `_pageModeToggle` ("Edit mode · switch to View") and `_layoutModeToggle` (lines 31–32, 104–109, 356–368). No Format/Page disclosures, no overflow.
   - Routed as workspace `"boards"` at `9to1 Workspace/shared/src/Haven.Desktop/Interface/Shell/MainView.DocumentWorkspaces.cs:50`. Which surface owners see by default in the current build (this page vs. the CUI editor session) — **unverified** (both code paths exist and are registered).

3. **HUI structured-board projection (not document chrome):** `9to1 Workspace/Boards/hui/` (e.g. `HavenBoardsHuiScene.cs`, `HavenBoardCollaborationCoordinator.cs`). Collaboration coordinator lives ONLY here, not in the CUI editor.

### Adaptive toolbar anatomy (`BoardsAdaptiveToolbar.cui`)

- Root `AdaptiveToolbar` = Grid `ColumnSizes="1fr Auto"` (line 7):
  - Col 0 `AdaptivePill` `Type="Toolbar"` (line 8) — rounded pill (`CornerRadius=15`), `Interactive="{Binding CanEditToolbar}"`. Contains selection-adaptive sections toggled by `Active="{Binding TextMode|TableMode|ImageMode|OtherMode|DrawingMode}"`:
    - `AdaptiveFontSection` (lines 9–42: style select, font/size inputs, case menu),
    - `AdaptiveFormatSection` (43–90: bold/italic/underline/strike, text colour, highlight),
    - paragraph menus (alignment/list/spacing/indent, lines 92–139),
    - `AdaptiveTableSection` (141–158), `AdaptiveImageSection` (160–168),
    - `AdaptiveDrawingSection` (169–212: ink tool/width/colour/view menus),
    - `AdaptiveObjectSection` (213–215),
    - Properties menu `AdaptiveProperties` (217–224: "More properties", Sub/Sup, Clear formatting, Manage styles…),
    - **overflow host** `AdaptivePill_Menu_2` with `OverflowHost="True"` (line 225), trigger "…" `OverflowAdaptiveOverflow` (226) carrying whole duplicate groups: Font-and-style (227–253), Text (254–294), Colours (295–316), Table (317–335), Image (336–346), Drawing (347–390).
  - Col 1 `AdaptiveFixedActions` (399–413): `AdaptiveAdd` "+ Add" (400), `AdaptiveDrawing` toggle (401–406), `AdaptiveAI` (407–412, hard-disabled: `Interactive="False"`, "AI is not connected to this Boards editor").

### How overflow works today

- Framework layout: `framework/CUI/Runtime/CuiToolbarPanel.cs` — measures children in order, collapses non-fitting ones by HIDING THE CELL (never touching authored `IsVisible`); the `OverflowHost="true"` child "is shown only when at least one item was actually collapsed"; exposes `VisibleItemCount`/`HiddenItemCount` (lines ~7–48).
- Marker: `framework/CUI/Runtime/CuiOverflowHosts.cs:9–35` (`OverflowHost="true"` on a child of `Container Type="Toolbar"`).
- Registered property: `framework/CUI/Runtime/CuiControlRegistry.cs:395,409–411,479`.
- The fixed Add/Drawing/AI column sits OUTSIDE the pill (`Column="1"`) so it never collapses (comment `BoardsAdaptiveToolbar.cui:394–398`).
- Behaviour pins: `framework/CUI/Runtime.Tests/CuiToolbarOverflowAndSeparatorTests.cs` (names in §4).

### Toolbar adapter/binding layer

- `9to1 Workspace/Boards/app/AdaptiveToolbar.cs` — loads `BoardsAdaptiveToolbar.cui`, binds state, routes commands. Finds `AdaptiveAdd` and attaches the insert menu: lines 29–30 → `ToolbarBuilder.AttachInsertMenu(insert, owner.ToolbarTarget)`. Command dispatch incl. `ToggleDrawing` (288–289), `Properties` (290), capability gate `RefuseUnsupportedCommand` (83–106).
- `9to1 Workspace/Boards/app/ToolbarBuilder.cs` — `Rebuild` mounts/reuses the strip (23–47); grouped insert menu content: Database/Containers/Text/Custom styles/Content/Other (146–185); keyboard routing incl. Escape-close at 119–145.
- `9to1 Workspace/Boards/app/BoardsViewModel.AdaptiveToolbar.cs` — `ToolbarCapabilities` record (12–25) and adaptive font/colour/spacing/case state.
- `9to1 Workspace/Boards/app/BoardsViewModel.Toolbar.cs` — shared-toolbar targeting for Tabber panes (`ToolbarTarget`, 18–19; `ToolbarChanged`, 8).
- Popup hosting (Boards-owned outside-press dismiss instead of framework light-dismiss): `9to1 Workspace/Boards/app/BoardsPopupHosting.cs` (rationale comment `ToolbarBuilder.cs:60–64`).

### Top-bar chrome in `Boards.cui` (where some spec controls live TODAY)

- `TopBar` (line 27) → `TopBarLeft`: `BoardSwitcher` identity menu (32–60: Save, Save As…, New Board…, Open Board…, All Boards…, Rename…, Icon and emoji…, Trash…, Board properties…, Exit) + `SaveState` (61) + `UndoButton`/`RedoButton` (63–64).
- `TopBarRight` (66): **`EditViewToggle`** pill with `EditModeButton`/`ViewModeButton` (72–81) — comment says it is a "Persistent Edit/View switch in the page chrome … It was reachable only inside the '…' overflow, which the on-screen review on 1 November 2026 showed the owner could not see at all"; **`LayoutModeButton`** (82–86); **`SnapButton`** (89–93); `MoreMenu`/`OverflowButton` (94–105: TabView, Styles…, theme, `CanvasLockToggle`, Board properties…).
- Sidebar `NavPane` (113): search (121), `NavNewPageButton` "+ New page" → `AddPage` (122–124), `NavHost` page tree (125–127, built in `9to1 Workspace/Boards/app/NavBuilder.cs:83–94`), sidebar `AddNewButton` "+ Add" menu — "New page in this section", "New section", Write/Canvas attach/create (128–138).
- Editor column `EditorHost` (142): `BoardsTabBar` board/page tabs + "+" (148–171); `FormattingToolbar`>`ToolbarHost` mount point (172–174); `ContextHost` Properties panel, visible via `AdaptivePropertiesVisible` (175–276: text/table/image/divider property grids); page canvas `PageCard`/`EditorScroll`/`DocumentContentLayer`/`InkOverlayLayer` (277–327).
- Mode commands handled in `9to1 Workspace/Boards/app/BoardsViewModel.Header.cs:68–92`: `SetEditModeOn/Off` ("persistent Edit/View switch in the top-right chrome", 69–75), `ToggleLayoutMode` (76–79), `ToggleSnap` (80–89 — "third independent dimension"), `CloseAdaptiveProperties` (59–61), `RenameBoard` (42–58).

---

## 2. Exact SPEC quotations

File: `docs/specification/owner-references/2026-11-18-interface-refactor/SPECIFICATION.md`. Screen ids are the "Mockups:" anchors listed under each spec subsection.

- **BOARD-UI-001** (line 499): "Use one compact toolbar with direct Add, Drawing, Edit/View and Collaborate controls. Format opens the original text/paragraph/font/list/alignment controls. Page opens Properties, layout lock, snapping and the original page options."
  Screens (§4.2, line 509): `boards-editor`, `boards-add`, `boards-insert`, `boards-file-menu`, `boards-alignment`, `boards-list-options`, `boards-properties`.
- **BOARD-UI-002** (line 501): "Format and Page are real disclosures. The active trigger announces its expanded state. Escape and outside click close a popup; keyboard navigation reaches every enabled control and returns focus appropriately. Opening one popup closes an overlapping sibling."
  Screens: same §4.2 list (line 509).
- **BOARD-UI-003** (line 503): "Preserve the original actions behind their new grouping. Reorganising chrome does not remove styling, drawing, properties, page options or collaboration. The redundant toolbar AI entry is removed because the contextual AI bar already owns that entry point."
  Screens: same §4.2 list (line 509).
- **BOARD-UI-004** (line 505): "Name sidebar creation clearly: New page creates a page/section; toolbar Add and block insertion create content within the current page. The board-name control opens file actions; renaming is an explicit command, not a side effect of opening the switcher."
  Screens: same §4.2 list (line 509).
- **BOARD-MODE-001** (line 515): "View/Edit, layout lock and AI access are separate dimensions. View mode prevents document edits. Layout lock restricts layout manipulation while retaining the established page-ink behaviour. Take actions cannot bypass either permission or read-only restrictions."
  Screens (§4.3, line 521): `boards-context`, `boards-typing`, `boards-editor`, `boards-ink`, `boards-properties`; also referenced by CTX-08 "Boards page/canvas" with `boards-insert`, `boards-ink`, `boards-properties` (line 649).
- Related anchor (line 493, **BOARD-003**): "Remove duplicated board/save labels and a separate decorative mode row… repeated Native page, locked-layout and snapping labels belong in the relevant Page menu." — directly constrains the adaptation (top-bar Layout/Snap labels should move into Page disclosure).

---

## 3. Spec control → current code map

| Spec control | Status | Where today |
|---|---|---|
| **Add (toolbar)** | EXISTS as-is | `AdaptiveAdd` pill button `BoardsAdaptiveToolbar.cui:400`; insert menu wired `AdaptiveToolbar.cs:29–30` → `ToolbarBuilder.AttachInsertMenu` (`ToolbarBuilder.cs:58–206`). Sidebar "+ New page"/"+ Add" are distinct controls (`Boards.cui:122–138`) — matches BOARD-UI-004 split. |
| **Drawing** | EXISTS as-is | `AdaptiveDrawing` selected-toggle `BoardsAdaptiveToolbar.cui:401–406`; command `ToggleDrawing` in `AdaptiveToolbar.cs:288–289`; ink section `AdaptiveDrawingSection` (169–212) collapses into overflow Drawing group (347–390). |
| **Edit/View toggle** | EXISTS, but in different chrome | Top-bar `EditViewToggle`/`EditModeButton`/`ViewModeButton` `Boards.cui:72–81`; commands `SetEditModeOn/Off` `BoardsViewModel.Header.cs:68–75`; gated by `CanToggleEditMode => CanEdit` (`BoardsViewModel.cs:453`). NOT in the compact toolbar pill (spec wants it direct there). The Haven `BoardsPage` has its own variant `_pageModeToggle` (`BoardsPage.cs:31,356–361`). |
| **Collaborate** | MISSING in CUI editor | No "Collaborate"/share control anywhere in `9to1 Workspace/Boards/app/*.cui` or `*.cs` (grep across app returned only unrelated "shared" comments). Collaboration code exists only in the HUI projection: `9to1 Workspace/Boards/hui/HavenBoardCollaborationCoordinator.cs`. Whether a service seam exists to present a Collaborate popup from the CUI editor — **unverified**. |
| **Format disclosure** | EXISTS as inline sections + "…" overflow, NOT as one named disclosure | Format controls are always-inline sections inside `AdaptivePill` (font 9–42, text 43–90, paragraph 92–139, table/image 141–168) plus `OverflowAdaptiveOverflow` "…" menu (`OverflowHost="True"`, 225–392). Spec target = a single "Format" trigger opening those same controls; content exists twice already (inline + overflow), chrome shape differs. |
| **Page disclosure (Properties/layout lock/snapping/page options)** | EXISTS but SCATTERED across three places | Properties: `AdaptiveProperties` menu `BoardsAdaptiveToolbar.cui:217–224` + `ContextHost` panel `Boards.cui:175–276` (visibility `AdaptivePropertiesVisible`, `BoardsViewModel.AdaptiveToolbar.cs:27–28`). Layout lock: `LayoutModeButton` top-bar `Boards.cui:82–86` (+ `CanvasLockToggle` in MoreMenu, 102). Snapping: `SnapButton` top-bar `Boards.cui:89–93`. Page options (tab view/styles/theme/properties): `MoreMenu` `Boards.cui:94–105`; board file options in `BoardSwitcher` (32–60). No single "Page" disclosure exists. |
| **AI entry removal (BOARD-UI-003)** | TARGET EXISTS, must be removed | `AdaptiveAI` (currently `Interactive="False"`) `BoardsAdaptiveToolbar.cui:407–412`. No "contextual AI bar" found in the CUI app — grep for `sparkles|AiBar|AssistantBar` only hits this icon; the spec's "contextual AI bar" presumably refers to Haven shell AI, **unverified** here. |

Accessibility mechanics for BOARD-UI-002: insert menu already implements Escape/outside-press close and full keyboard routing (`ToolbarBuilder.cs:119–145`; `BoardsPopupHosting.cs`), but per-control Escape/focus-return/expanded-state announcements for Format/Page disclosure popups built as `Container Type="Menu"` were NOT individually verified — **unverified**; framework menu behavior would need checking (`framework/CUI/Runtime/CuiControlLoader.cs` menu handling — not read).

---

## 4. Existing tests touching Boards chrome/toolbar

App-level (`9to1 Workspace/Boards/app.tests/`):
- `BoardsAdaptiveToolbarTests.cs` — `Mounted_strip_collapses_sections_but_keeps_fixed_actions_and_restores_them` (overflow collapse preserves Add/Drawing/AI column); `Live_projection_and_actions_use_real_values_and_validated_typed_size`; `Colour_projection_follows_the_actual_selected_runs_and_reports_mixed_values`; `Many_property_notifications_publish_one_final_toolbar_snapshot`; `Mounted_list_row_keeps_supported_formatting_and_refuses_unstored_paragraph_properties`.
- `BoardsSingleToolbarTests.cs` — `One_top_toolbar_formats_the_focused_pane_and_parent_with_exact_history` (single shared toolbar, Tabber pane targeting).
- `BoardsToolbarCapabilitiesTests.cs` — `Mounted_list_typography_and_overflow_edit_the_entire_row_and_reopen`; `List_highlight_none_clears_inherited_surface_and_spacing_case_keep_typing_focus`; `Mounted_paragraph_spacing_changes_layout_and_persists_explicit_zero`; `Contextual_capabilities_guard_hidden_text_commands_on_objects`.
- `BoardsToolbarCapabilitiesLifetimeTests.cs` — toolbar-target lifetime across pane detach/hide.
- `BoardsModeMatrixTests.cs` — pins BOARD-MODE-001 dimensions: `View_mode_refuses_every_mutation_kind_whatever_the_other_two_dimensions_say`; `Locked_layout_constrains_block_placement_only_and_still_permits_ink`; `Canvas_lock_and_layout_mode_are_different_dimensions_and_neither_blocks_page_ink`; `Snap_changes_only_where_a_manipulation_commits_and_never_the_other_two_dimensions`.
- `BoardsUndoOwnershipAndModeChromeTests.cs` — `Page_mode_switch_is_persistent_chrome_and_the_experimental_surface_is_not_offered` locates `EditModeButton`/`ViewModeButton`/`LayoutModeButton` by automation id **in the top bar** (~lines 140–143) — this test must change when the toggle moves into the toolbar.
- `BoardsHeaderInteractionTests.cs` — `Board_name_opens_file_operations_and_rename_updates_the_same_document` (pins BOARD-UI-004 switcher behavior).
- Adjacent: `BoardsInsertMenuRowStateTests.cs`, `BoardsInsertionPreviewTests.cs`, `BoardsRelativeInsertionTests.cs`, `BoardsLandingRegressionTests.cs`, `BoardsVisualTests.cs`, `BoardsTopologyTests.cs`.

Framework overflow pins (`framework/CUI/Runtime.Tests/`):
- `CuiToolbarOverflowAndSeparatorTests.cs` — `Toolbar_with_room_for_everything_shows_every_item_and_presents_no_overflow_host`; `Narrow_toolbar_collapses_exactly_the_items_that_do_not_fit_and_shows_the_authored_host`; `Collapsing_a_toolbar_hides_the_cell_without_rewriting_authored_visibility`; `Toolbar_remeasures_narrow_wide_narrow_without_losing_hidden_item_or_host_width`; `Toolbar_and_separator_are_registered_types_and_overflow_host_is_a_typed_property`.
- `CuiRibbonGroupTests.cs` — ribbon-group overflow host registration (legacy ribbon variant).

Haven-side (service/workspace, not toolbar chrome):
- `9to1 Workspace/shared/tests/Haven.Desktop.Tests/BoardsWorkspaceTests.cs` — `ViewMode_IsPersistedAndBlocksServiceAndTypedApiMutationsUntilEditModeReturns`, `LockedLayoutPreservesAbsoluteCoordinatesAndBlocksPlacementIndependentlyOfEditMode` (pins mode semantics that chrome must not break).
- `UnifiedBoardsInitialRouteTests.cs`, `BoardsRecoveryAcceptanceTests.cs` etc. exist but touch routing/recovery, not the toolbar (spot-checked by name — details **unverified**).

Evidence docs: `docs/development/evidence/boards-usability-2026-10-31/README.md` (adaptive toolbar + fixed Add/Drawing/AI, Boards 469/469 at commit `a0f37f4f`), `boards-single-toolbar-2026-10-30/`.

---

## 5. Minimal adaptation sequence (no code written)

**Step 1 — Re-shape the pill chrome; reuse commands verbatim.**
Edit `BoardsAdaptiveToolbar.cui` only: (a) inside `AdaptiveFixedActions` add the Edit/View toggle and a `Format` trigger + `Page` trigger; (b) convert the existing always-inline format sections into the Format disclosure's popup content — the overflow menu (225–392) already contains full duplicate groups that can be promoted to the disclosure body; (c) build the Page disclosure from the existing `AdaptiveProperties` menu (217–224) plus `LayoutModeButton`/`SnapButton` actions. Route `SetEditModeOn/Off`, `ToggleLayoutMode`, `ToggleSnap` through `AdaptiveToolbar.Context.DispatchAsync` — they already exist in `BoardsViewModel.Header.cs:68–92`, so the adapter change is a command pass-through plus binding keys (`IsEditingMode`/`IsViewingMode`/`LayoutModeLabel`/`SnapLabel`). Remove `AdaptiveAI` in the same pass (BOARD-UI-003).

**Step 2 — Demote old chrome and update pinned tests.**
Once the toolbar owns Edit/View/Layout/Snap: remove `EditViewToggle`/`LayoutModeButton`/`SnapButton` from `Boards.cui:72–93` (keep `MoreMenu` page options for now), update `BoardsUndoOwnershipAndModeChromeTests.Page_mode_switch_is_persistent_chrome…` to find the controls in the strip, re-run `BoardsAdaptiveToolbarTests`, `BoardsModeMatrixTests`, `BoardsSingleToolbarTests`, and the CUI overflow framework tests. Note the 1-November owner-review comment (`Boards.cui:67–71`) that motivated top-bar persistence — the spec now relocates these controls into a visible toolbar disclosure, satisfying the same intent; record that in the change.

**Step 3 — Collaborate control + disclosure behaviour hardening.**
Add the `Collaborate` trigger (spec-mandated, currently missing entirely): if no collaboration seam exists for the CUI editor session (unverified), land it as a real disclosure with explicit unavailable-state first (mirroring the honest-disabled `AdaptiveAI` pattern, but announced), then wire to a service in a follow-up. Verify BOARD-UI-002 behaviours on the two new disclosures: expanded-state announcement on trigger, Escape + outside-click close, focus return, sibling-close — extend the `ToolbarBuilder.cs:119–145` keyboard pattern if the framework menu does not already provide it (check `CuiControlLoader` menu handling first). Update `docs/development/evidence/` screenshots-style evidence after.

Risks to watch: `ToolbarCapabilities` gating (`BoardsViewModel.AdaptiveToolbar.cs:12–25`) must keep View-mode refusal (`AdaptiveToolbar.cs:275`); the Tabber shared-toolbar targeting (`BoardsViewModel.Toolbar.cs`) and the reuse guard `AdaptiveToolbar.TryReuse` (`ToolbarBuilder.cs:43`) assume one strip per pane — test via `BoardsToolbarCapabilitiesLifetimeTests`.
