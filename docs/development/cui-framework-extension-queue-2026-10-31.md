# CUI framework extension queue — measured from the Stage A mirrors (2026-10-31)

Every number below is measured over the committed mirror gap reports (`*/CUI/DonorMirror/*/gaps.json`): **33,014 gap reports across 295 distinct constructs** in 10 corpora. This is the work list for making CUI able to express the donors it must replace, in the owner's order: (1) mirror the donor UI in CUI unchanged, (2) then adapt that mirror to the specification.

Nothing in this document is accepted, rendered or run. It is a queue with evidence, and it does **not** authorise replacing a donor construct with a narrower one: each row must reach parity or an owner decision.

| # | CUI construct needed | Occurrences | Demand seen in | Donor constructs | What CUI must add |
|---|---|---:|---|---|---|
| 1 | `toolbar with overflow` | 6,878 | Present/DonorMirror (3512), Write/DonorMirror (2040), Data/DonorMirror (1309) | GTK `GtkToolbar`/`GtkOverflow`, LibreOffice `ToolBar`, AppFlowy `OverflowBar`, XUL `toolbar` | A Container Type with measured overflow: items that do not fit collapse into an overflow control that re-presents them as menu items. Today mirrors must approximate a toolbar as a plain horizontal stack, which loses the donor's real behaviour. |
| 2 | `notebook bar (ribbon) group` | 4,046 | Present/DonorMirror (2093), Write/DonorMirror (1155), Data/DonorMirror (798) | LibreOffice `NbToolBar`, `notebookbar` XML | A tabbed command bar with named groups: tab strip + group containers + group overflow. This is what Write/Present/Data need to reach LibreOffice command-bar parity. |
| 3 | `property not modelled` | 2,910 | Build/DonorMirror (1062), Files/DonorMirror (517), Connect/DonorMirror (506) | all dialects | Mixed bag: each entry names a donor attribute CUI has no property for. These need individual review and must not be bulk-accepted; the emitter already records which element rejected which name. |
| 4 | `optional section` | 2,263 | Present/DonorMirror (1313), Write/DonorMirror (598), Data/DonorMirror (352) | GTK `<section>` in menus/header bars, XUL `<hbox optional>` | A container whose presence is conditional on the donor model (a group that may be absent). Needs a CUI conditional-section construct distinct from `If`, because it is a layout region, not a boolean branch. |
| 5 | `separator` | 1,626 | Present/DonorMirror (802), Write/DonorMirror (402), Data/DonorMirror (304) | GtkSeparator, XUL separator, Flutter Divider | A first-party separator construct (visual and, in menus, semantic). Currently mirrored as an empty container, which loses the separation and misleads layout measurement. |
| 6 | `custom donor widget` | 1,479 | Boards/DonorMirror (1423), Canvas/DonorMirror (56) | Flutter packages, rnote custom widgets | The donor's own composite control (AppFlowy `FlowyButton`, rnote `RnSidebar`). CUI needs the equivalent construct authored; the row is the demand signal, and the mirror refuses to chase a definition that lives in another class. |
| 7 | `unknown donor construct` | 1,003 | Build/DonorMirror (470), Connect/DonorMirror (375), Browse/DonorMirror (158) | GTK/XUL custom elements | Donor element with no CUI equivalent at all yet; each needs a decision, not an approximation. |
| 9 | `donor icon asset (FlowySvg)` | 758 | Boards/DonorMirror (758) | AppFlowy FlowySvg asset names | Icon resolution through CUI's approved icon family with a named-asset fallback; 758 uses means the donor's asset naming scheme must map to our icon registry. |
| 10 | `live variable / binding expression` | 647 | Boards/DonorMirror (378), Build/DonorMirror (269) | Flutter BlocBuilder/Provider, Go templates, XAML bindings | CUI's live-variable/binding contract for values that come from donor model state; owner has said this is the ideal end state, not a hard gate yet. |
| 11 | `theme colour token` | 596 | Boards/DonorMirror (596) | Flutter Colors/Theme.of, XAML StaticResource brushes | Theme role mapping so donor palette tokens resolve to CUI theme roles instead of literal colours. |
| 12 | `fixed vertical spacer` | 581 | Boards/DonorMirror (581) | Flutter VSpace/HSpace/SizedBox | Spacer/spacing construct so fixed gaps survive measurement instead of becoming empty containers. |
| 13 | `toolbar overflow` | 523 | Present/DonorMirror (270), Write/DonorMirror (135), Data/DonorMirror (118) | as above | Same construct as above; counted separately because two dialects spell it differently. Merge in the queue, not in the data. |
| 14 | `fixed horizontal spacer` | 517 | Boards/DonorMirror (517) | Flutter HSpace/SizedBox | As above on the other axis. |
| 15 | `inline text run` | 474 | Build/DonorMirror (221), Boards/DonorMirror (210), Connect/DonorMirror (36) | Flutter TextSpan, GTK labels with markup | Inline runs inside one text block (bold/coloured spans) — TextSpan/RichText and LibreOffice label markup. |
| 16 | `menu button` | 459 | Present/DonorMirror (212), Write/DonorMirror (132), Data/DonorMirror (115) | XUL menubutton, GtkMenuButton, Flutter PopupMenuButton | A button whose purpose is to open a typed menu, with the menu declared as its own construct. |
| 17 | `computed size` | 437 | Boards/DonorMirror (437) | all dialects | Sizes expressed as donor expressions; needs CUI's typed length/binding form rather than a dropped value. |
| 18 | `flex share sizing` | 433 | Boards/DonorMirror (433) | Flutter Expanded/Flexible, XAML RowDefinitions='*' | Proportional share sizing (Flutter Expanded/Flexible, XAML star rows) distinct from fixed lengths. |
| 19 | `donor text wrapper (FlowyText)` | 428 | Boards/DonorMirror (428) |  |  |
| 20 | `link action` | 408 | Build/DonorMirror (379), Connect/DonorMirror (21), Browse/DonorMirror (8) | HTML/XUL label xul:href, GtkLinkButton | A hyperlink action with a target, distinct from a button command. |
| 21 | `distributor alignment` | 399 | Boards/DonorMirror (399) | Flutter mainAxisAlignment/crossAxisAlignment, GTK pack/child properties | Container-level distribution (space-between/evenly, cross-axis alignment) which CUI has no property pair for; children can be aligned but the group cannot be distributed. |
| 22 | `fixed zero-size spacer` | 366 | Boards/DonorMirror (366) |  |  |
| 23 | `conditional rebuild on state` | 332 | Boards/DonorMirror (332) | Flutter BlocBuilder/StreamBuilder | Subtree rebuild driven by model state; overlaps the live-variable work. |
| 24 | `button row` | 296 | Write/DonorMirror (132), Data/DonorMirror (131), Present/DonorMirror (33) |  |  |
| 25 | `insets expression` | 288 | Boards/DonorMirror (288) |  |  |
| 26 | `item collection (list/tree/table)` | 260 | Write/DonorMirror (69), Build/DonorMirror (61), Data/DonorMirror (47) | GtkTreeView, XUL tree, XAML ListView, Flutter ListView | Real list/tree/table constructs with selectable rows and columns, instead of a plain container. |
| 27 | `radio menu item` | 251 | Write/DonorMirror (95), Present/DonorMirror (89), Data/DonorMirror (67) |  |  |
| 28 | `gesture handling (tap/drag/long-press)` | 240 | Boards/DonorMirror (240) |  |  |
| 29 | `window and dialog chrome` | 240 | Data/DonorMirror (114), Write/DonorMirror (99), Present/DonorMirror (27) |  |  |
| 30 | `scroller with explicit bars` | 212 | Write/DonorMirror (102), Data/DonorMirror (70), Present/DonorMirror (27) |  |  |

## Grouped picture

- **Command surfaces dominate**: toolbar-with-overflow 7,401 combined, ribbon/notebook group 4,046, menu button 459, tab strip ~50, toggle button — this is the office-suite command bar family and it is one design problem, not five.
- **Structure next**: optional section 2,263, separator 1,626, spacers 1,464 combined, split view, status bar, header bar, item collection 260.
- **Values and state**: live variable/binding 647, computed size 437, flex share sizing 433, conditional rebuild 332, theme colour token 596. The owner's guidance is that CUI's live-variable system is the ideal end state for these, not a hard gate yet, which is why they rank below the structural work.
- **Donor-specific residue**: custom donor widget 1,479 and unknown donor construct 1,003 are decisions, not gaps: each names a control the donor defines itself. They must be reviewed one by one; 'approximate as container' is a Stage A holding place.

## Verification plan for each unit

1. Register the construct in `framework/CUI/Runtime/CuiControlRegistry.cs` (Container Type and any new typed properties) — the mirror emitter reads this contract live, so the mirror gains the construct in the same change.
2. Implement it in the CUI-owned runtime (`CuiContainerControl` panel selection plus a runtime control where measurement is involved) — never a donor control, never Avalonia Fluent templates.
3. Add runtime tests in `framework/CUI/Runtime.Tests` for the *behaviour* (e.g. an overflow menu really contains the items that did not fit at the measured width), not just for parsing.
4. Re-run the mirrors and the compile probe; record the drop in gap reports as the measured effect, and keep any remaining approximations visible.

## Progress against this queue — measured 31 October, later the same day

**Items implemented: `toolbar with overflow` and `separator`.**

- Framework (CUI-owned, no donor control, no Fluent template): `CuiToolbarPanel` measures its children and
  collapses exactly the items the measured width cannot hold, presenting the author's `OverflowHost="true"`
  surface only when something actually collapsed; `CuiSeparatorPanel` draws the rule from the theme-owned
  `CuiLineBrush`/`CuiLineStrongBrush`, observed live; `CuiOverflowHosts` is the framework marker the loader
  fills from the authored property, so the marker cannot be set from outside CUI and survives re-parenting.
  Registry gained Container Types `Toolbar` and `Separator` plus the typed boolean property `OverflowHost`;
  the mirror emitter reads that registry live, so the mirrors gained the constructs in the same change.
**Item implemented later the same day: `notebook bar (ribbon) group`** — `Container Type="Ribbon Group"`
with an authored `GroupLabel` caption and the same measured collapse-to-overflow as the toolbar
(`framework/CUI/Runtime/CuiRibbonGroupPanel.cs`, `CuiContainerControl.SetGroupLabel` which errors explicitly
on a container type that cannot honour it, `CuiLayoutProperties` route, 4 tests in
`CuiRibbonGroupTests`, all passing in isolation; full-suite failure counts stayed inside the flaky
theme-refresh band 8-13 with none of my classes failing). LibreOffice's `sfxlo-NotebookbarToolBox` maps onto
it. Measured: **4,046 Ribbon Group elements** emitted and that gap construct now **zero**. Recorded
limitation rather than a fudge: `GroupLabel` is emitted **0** times today, because the office tool boxes
carry no `label` property — the donor puts the caption in a sibling `GtkLabel`, so hoisting it into
`GroupLabel` is Stage B adaptation.

**Two accounting corrections found by measurement, both recorded rather than absorbed.**

- `--exclude test` matched **substrings**, silently dropping 20 genuine donor UI files, including
  `swriter/ui/footnotesendnotestabpage.ui`, `scalc/ui/chisquaretestdialog.ui`, `scalc/ui/ttestdialog.ui` and
  `scalc/ui/ztestdialog.ui` - not test fixtures at all ('test' inside 'sendnoTESTabpage'). Exclusion now
  matches path segments (or an explicit `dir/subtree` fragment). Documents **1,764 → 1,784**, all compiling
  clean.
- Gap records are now classified by **kind**: of **25,376** records, **20,767** are genuine CUI framework
  gaps and **4,609** are donor plumbing (web attributes like `form`/`method`/`defer`/`data-*`, and inline
  script/style/observer hooks). The earlier single figure conflated them and overstated the queue; the
  tables below now separate them, and `gaps.md` in each corpus has its own plumbing section.
- A real emitter bug surfaced on the way: hyphenated keys in the XUL property map were **unreachable**
  because the lookup strips hyphens, so 82 mapped `aria-label` and 58 `data-l10n-id` attributes were being
  reported as unmapped and dropped. Accessibility name, tooltip and accesskey map through now, and
  localisation keys are recorded as their own CUI gap instead of being swallowed.

- Measured effect on the corpora: gap records **33,014 → 25,113 → 21,067 → 25,376**, the last rise being
  the newly-visible plumbing records, while the **CUI-only** queue fell to **20,767**; `toolbar with overflow` and `separator`
  are now **zero** and **433 Toolbar**, **1,639 Separator** and **4,046 Ribbon Group** elements are emitted. Documents **1,758 →
  1,764**, all compiling clean (`--strict`, 0 errors, exit 0, registry 2.0).
- Relabelling is counted, not hidden: `menu button` rose 459 → 1,537 because LibreOffice's
  `sfxlo-PriorityHBox`/`PriorityMergedHBox`/`DropdownBox` are now named for what they are (the office's own
  overflow/dropdown machinery) instead of being lumped in with toolbar overflow.
- Tests: `framework/CUI/Runtime.Tests/CuiToolbarOverflowAndSeparatorTests.cs` — 6 tests, all passing in
  isolation, asserting measured behaviour: 4 items in a 220-wide toolbar stay visible with no overflow
  control; the same document at 140 wide keeps exactly 2 visible and collapses 2, the host appears, the
  survivors keep document order inside the measured width, and a collapsed item's **authored** `IsVisible`
  is untouched (only the layout cell is hidden). Python mirror suite 47 → 50 with the mapping pinned for
  GTK, XUL and Dart.
- Runtime suite state, measured rather than assumed: the same binary run four times gave
  **Failed = 13, 12, 10, 9** out of 749 — the failures move around inside the theme-refresh family, so they
  are **flaky**, not new: `CuiControlLoader.ThemePreferenceChanged → RefreshTheme` throws
  `InvalidOperationException: The calling thread cannot access this object because a different thread owns it`.
  `CuiThemeTests` alone: 57/57 pass. My new class alone: 6/6 pass. This family is **not run by CI at all**
  (`9to1 Workspace/shared/Haven.sln` contains no CakeOS.Cui project), which is how it stayed red. Two owed
  follow-ups: put the CUI runtime suite under a pinned-failure CI ratchet, and fix the cross-thread theme
  refresh. Neither is waived.
- Build safety: these builds ran under the new `eng/development/guarded_lowmem_build.py` (floor 1,100 MiB
  MemAvailable, idle CPU/IO priority, server GC off, `-m:1`, shared compilation off, whole process group
  killed on breach). Measured worst case: peak process-tree RSS 357 MiB, minimum MemAvailable 5,246 MiB —
  so the earlier "blocked on memory" deferral was **caution, not measurement**, and is corrected here.

## Status and the constraint that defers step 2

Steps 1-4 are **not started**. The framework build/test for this unit is blocked on this PC right now, measured at 10:2x on 31 October 2026: MemAvailable ≈ 5.5 GiB of 62.4 GiB total, with 3.5 GiB of 4.1 GiB swap already in use, because this OpenCode session *is* the resident model host (~45 GiB resident). Building the vendored Avalonia fork plus the CUI runtime test project risks the OOM conditions the memory guards exist to prevent, and the resident host must never be stopped. Zero-build work (donor custody, mirrors, gates, decision records) continues; the framework unit runs when memory headroom returns, with the guarded entrypoint, `-m:1`, shared compilation off and serial SDK admission.

Source at measurement: `afb4006d697a1759226ce3e3e12207f33548b287`. Regenerate with: `python3 -B eng/cui-mirror/check_corpora.py` plus the gap aggregation over `9to1 Workspace/*/CUI/DonorMirror/*/gaps.json`.
