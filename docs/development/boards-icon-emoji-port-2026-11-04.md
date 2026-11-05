# Boards icon and emoji system ported from AppFlowy — flow pinned before coding, 4 November 2026

Owner request on 4 November 2026: "can you see the open appflowy instance in firefox? if so, interact
with appflowy to get a better feel of how it works and to port things over to boards, such as its native
icon/emoji system". This document pins the required user flow **before** any code is written, per the
29 October working-method correction ("pin the required user flow in writing before coding it").

## Why this is a specification gap, not only a feature copy

| Authority | Requirement text (abbreviated) | Boards today |
| --- | --- | --- |
| canonical v2 line 9423 | `BoardPage MUST contain at least PageID, BoardID, ParentPageID?, PageType, Title, **Icon?**, SortKey…` | no page-level icon field in `HavenRichPage` |
| canonical v2 line 8439 | "Every Section MUST have stable SectionID, … optional icon/metadata" | satisfied (`IconKey`, `IconColor`, `GeneratedIcon`) |
| canonical v2 line 9421 | `Board MUST contain at least BoardID, Name, **Icon?**…` | no board-level icon |
| canonical v2 line 9417 | "Page title or **icon** changes MUST update visible navigation without changing stable identity" | page icon cannot change; row is fixed |
| UX27-BOARD-012 | tree rows contain "an actual page/section icon" | page rows render a hard-coded `file` glyph at `app/NavBuilder.cs:326` |
| UX27-BOARD-010/011 | landing cards and the board switcher show "board names, **icons**…" | board card uses a fixed `folder` glyph at `app/Boards.cui:321` |

So the surface already has section icons and is missing page and board icons. AppFlowy supplies the
mechanism the specification leaves open (`Icon?` is untyped in v2): one typed icon record per entity,
whose value may be an emoji, a library icon, or an uploaded image.

## What was observed in the owner's live AppFlowy instance (4 Nov 2026)

Captures in `/tmp/opencode/law/native/appflowy/` (`f01`…`f10`), Firefox window `0x05000017`,
AppFlowy cloud at `appflowy.com/home`, Law workspace. Nothing in the owner's workspace was changed:
the icon popover was opened and dismissed with no selection.

1. Sidebar rows show an emoji on a **coloured rounded square** (`f01`).
2. A page's title line shows its icon to the left of the H1 (`c01`, `c03`); the current "Law-Making"
   icon renders as a duotone scales glyph, i.e. from the Icon tab rather than the Emoji tab.
3. Clicking the icon opens an anchored popover with the tab row **Emoji | Icon | Custom | Upload**, a
   search field with a clear ×, a shuffle (random) button, a waving-hand button, **Remove**, and a close ×
   (`f02`).
4. Searching `scales` produced a category header ("Objects") with **zero** rows underneath and **no**
   no-match message (`f03`). This state is deliberately **not** ported.
5. Searching `book` produced real matches (`f08`). The Icon tab holds monochrome line icons grouped
   alphabetically with their own search and shuffle (`f04`). The Upload tab is a drag-drop/Browse target
   (`f06`). The Custom tab is empty until an icon is uploaded (`f05`).
6. The popover dismissed on outside click without changing the page (`f10` confirmed the icon intact).

## What was read in the donor source (`Source/AppFlowy/…/lib/`)

- `shared/icon_emoji_picker/flowy_icon_emoji_picker.dart` — the stored shape is
  `EmojiIconData { FlowyIconType type, string value }` with `type ∈ {emoji, icon, custom/url}`,
  persisted per view as `ViewIconPB { ty, value }`. Tabs default to `[emoji, icon]`; `custom`/upload
  appears where an upload target exists. `enableBackgroundColorSelection` drives the colour square.
  A 46-unit tab row with **Remove** at the right, then a divider, then the tab body. Picking a
  **random** result sets `keepOpen: true` so the popover stays open for another roll; a hand-picked
  emoji closes it. Remove stores `{type: icon, value: ''}`.
- `plugins/base/emoji/emoji_picker.dart` — `EmojiData.builtIn()` (Unicode/CLDR dataset with names and
  categories), 9 emoji per line on desktop, 36-unit buttons with an 8-unit radius, tooltip = the CLDR
  name, category headers, and a synthetic **Recent** category inserted at the front containing up to
  one line of previously chosen emoji (`shared/icon_emoji_picker/recent_icons.dart`).
- `shared/icon_emoji_picker/emoji_search_bar.dart` — clear × clears the query and re-filters; when the
  field is already empty the × unfocuses. Random picks an id from the whole dataset, honouring the
  current skin tone.
- `shared/icon_emoji_picker/emoji_skin_tone.dart` — a five-option skin-tone popover (none + four
  modifiers) whose choice re-renders every modifier-capable emoji and persists as a preference.

## Ported user flow (pinned)

Actors: signed-in owner with a real `.9to1board` open in Boards, Edit mode on.

1. **Open the picker.** In Edit mode, activating a **section row's icon**, a **page row's icon**, or the
   **page icon in the page header** opens one anchored CUI popover (one implementation for all three
   entry points). Sections also keep the existing `Icon and color…` context-menu entry; pages gain the
   same entry in their existing row menu.
2. **Tab row.** `Emoji | Icon | Suggest` at the top of the popover, with **Remove** at the right end and
   a close control. The active tab reflects what the entity currently uses. Choosing a tab never changes
   the stored icon.
   - `Emoji` = Unicode emoji chosen as the icon.
   - `Icon` = the existing closed Boards silhouette family (`boards, section, folder, file, chat, study,
     calendar, target, bookmark, shield, play`) with the existing colour row — this is the owner's
     25 October decision ("a fixed filled-icon family … with contrast adapted to the current
     background"), kept, not replaced.
   - `Suggest` = the existing model-backed `Suggest from section name` geometry flow
     (`IconKey = "generated"`), kept, not replaced.
3. **Emoji tab.** Search field (matched against the CLDR name and the Unicode group), a skin-tone
   control, a random control, category headers, and a Recent group when recent picks exist. A query with
   no matches MUST show an explicit no-match message and MUST NOT leave a bare category header — the
   donor's broken state is not ported. Selecting an emoji stores it and closes the popover; the random
   control stores its pick and leaves the popover open for another roll, like the donor.
4. **Colour.** `IconColor` keeps its existing meaning for vector silhouettes (glyph tint) and is used as
   the rounded square behind an emoji, matching the donor's coloured sidebar tiles. "Theme" clears the
   override and follows the active theme.
5. **Remove.** Clears the icon and returns to the default silhouette for that entity kind (page → `file`,
   section → `section`); it never deletes the entity.
6. **Persistence and identity.** The choice is written through the contract ops, validated, saved through
   the normal `.9to1board` save route, and survives close and reopen. Renaming a page or section MUST NOT
   change its icon or its stable id, and changing its icon MUST NOT change its stable id
   (canonical line 9417, UX27-BOARD-012).
7. **Navigation and surfaces.** A chosen icon renders in the sidebar row, the page header, the landing
   card and the board switcher where an icon slot already exists.
8. **View mode.** With Edit mode off the icon is still displayed but is not activatable.

## Deliberately deferred, with reason (not silently dropped)

- **Upload tab (image icon).** The donor's `custom`/`url` type stores an uploaded image as the entity
  icon. Boards has no entity-icon attachment route yet, and the specification forbids dead illustrative
  buttons ("Visible actions MUST have real implementations or clearly truthful unavailable/capability
  states", canonical line 9397). The tab is therefore **not shown** until the attachment route exists,
  rather than shown and non-functional. Deferred to a follow-up unit; recorded here.
- **Cover images.** Same reasoning.
- **Board-switcher and landing-card icons** are wired only if the contract change lands board-level
  icon support in the same pass; otherwise they stay on their current glyphs and are recorded as open.

## Data model decision

The donor's `{type, value}` collapses into the shape Boards already uses, so one icon representation
serves all three entity kinds instead of two competing ones:

- `IconKey` keeps the closed silhouette catalog and `"generated"`; **adds `"emoji"`**.
- A new `IconEmoji` string, present only when `IconKey == "emoji"`, mirroring how `GeneratedIcon` is
  present only when `IconKey == "generated"`. The validator rejects a non-empty `IconEmoji` under any
  other key, and rejects `"emoji"` with no or invalid content, so the field cannot be used to smuggle
  arbitrary text into an icon slot.
- A stored emoji MUST be a fully-qualified sequence from the Unicode 17 catalogue the app already ships;
  the validator checks membership rather than codepoint ranges, so ZWJ sequences and skin-tone variants
  are accepted exactly as the catalogue spells them.
- `IconColor` unchanged (`null` = theme, otherwise `#RRGGBB`).

This is additive: absent `IconEmoji` means the existing behaviour, so schema-5 files already on disk
remain valid and readable without a migration entry. The codec's version switch needs no new case
because the payload object is the same schema version; the honest label for that is "additive optional
field, no migration required", and it is verified by a round-trip test on a pre-existing file rather
than asserted.

## Emoji data: reuse the first-party catalogue, do not copy the donor's arrays

The donor's 3,223-line `emoji_lists.dart` is AGPL-licensed source; it is not pasted (THIRD_PARTY.md
rule 1). Boards and the composer share one dataset that already exists in this repository:
`Haven.Desktop/Resources/emoji-test-17.0.txt` (Unicode 17, fully-qualified sequences with group,
subgroup and CLDR name) plus `ComposerEmojiCatalogue` / `ComposerEmojiCatalogueQuery`. Boards does not
reference `Haven.Desktop`, so the catalogue moves to `Haven.Infrastructure`, which both already
reference, and `Haven.Desktop.Prefabs` keeps forwarding types so existing composer call sites and tests
are untouched. One catalogue, one search engine, two consumers.

Skin tone is ported as data, not invention: the modifier codepoints `U+1F3FB`–`U+1F3FF` (plus
`U+1F3F4` sequences) are applied to the sequence forms the Unicode file already lists, so only genuine
fully-qualified variants are offered.

## Rendering and accessibility constraints honoured

- The owner's 25 October Boards clarification says: "Do not reuse unrelated symbols for distinct actions
  or replace real vector icons with font-dependent glyphs." That governs **tool and action icons**.
  Toolbar/action icons in this port stay on the maintained vector catalog. An emoji chosen by the owner
  as an entity's identity is content, in the same category as text the composer already inserts, and is
  painted through the CUI runtime's existing emoji font fallback (`CuiNativeHost`: `Noto Color Emoji` on
  Linux, bundled `Noto Emoji` otherwise) rather than a private-use glyph.
- Icon slots keep their geometry: 20-unit page-tree icon slot and 32-unit rows with a reserved
  disclosure slot (UX27-BOARD-002). Adding an emoji must not reflow the label.
- Accessible names come from the CLDR name ("Scales"), so the tree does not read "Page icon" for every
  row and a screen reader does not spell a codepoint.

## Results, with the evidence level named for each claim

| Behaviour | Evidence level | Result |
| --- | --- | --- |
| Board, section and page icon fields; validation; identity preserved | test (`HavenRichIconPortTests`, 18 cases) | **PASS** |
| A board saved before the fields existed still opens with today's defaults | test - every icon property stripped from a real saved document, then reopened | **PASS** |
| Recent icons are board data: most recent first, bounded, validated | test | **PASS** |
| Emoji catalogue: group order, name/group search, exact membership, skin-tone resolution | test (`EmojiCatalogueTests`, 5 cases in `Haven.UI.Tests`) | **PASS** |
| Picker flow: open from a page, tabs, category bounds, search miss reports itself, skin tone changes what is offered and stored, shuffle keeps the popover open, remove restores the default mark, view mode withholds the entry point, prose refused | test (`IconPickerPortTests`, 9 cases) | **PASS** |
| The authored grid, driven by a click on the rendered CUI control rather than by dispatching the command | test (`IconPickerSurfaceClickTests`) | **PASS** |
| The popover as painted by the CUI runtime | headless render (`/tmp/opencode/boards-shots/section-icon-picker-emoji-tab-1440x900.png`) | **PASS** - tabs, colour emoji, skin tones, Remove visible |
| Choosing an emoji in the running application, committed by the application, still present after relaunch | **on-screen native capture** (`/tmp/opencode/boards-emoji/native/frames/v01`-`v04`) | **PASS** - page "Criminal Law" carries the chosen sequence with `recentIconEmoji` alongside; the tree row shows it on screen after reopen; the deliverable board is byte-identical before and after (`07678b69d3a8f43e…`) |

Suites at the time of writing: Boards app tests 625 passed / 1 failed, that single failure being the
deliberately-committed insert-menu keyboard case from ledger row 114 (`cd77c767`), i.e. the unchanged
baseline; Boards contract tests 158/158; `Haven.UI.Tests` 175 passed / 1 failed where the failure
(`Button_hover_tint_uses_its_effective_background_and_skips_transparent_buttons`) was checked by removing my
Haven.UI additions and re-running it - it fails without them, so it is pre-existing and not this work.

Two false reports this pass produced, corrected rather than left in:

1. The first native attempt looked like an unresponsive black window and I recorded it as an application
   failure. It was the machine: 4,111 of 4,111 MiB of swap used with ~2.3 GiB free, the application log
   stopping mid start-up with no exception, and the owner seeing a translucent pane. Relaunched with memory
   freed, the window painted in 0.7-3.5 s. `icon_native2.py` now samples MemAvailable, swap free and the
   application's RSS at every step so the two conditions cannot be confused again.
2. Reading the `.9to1board` immediately after a click showed no icon, which read as the click failing. It was
   the probe: unsaved edits live in the profile draft, and the file is written on the application's save
   route, so the check has to follow a commit.

And one gap in my own test coverage, now closed: the picker tests dispatched `ApplyEmojiIconChoice` straight
to the view model, so they never exercised the authored action binding. The rendered-surface test does, and
that is the difference between "the command works" and "the thing the owner clicks works".

## Second pass: the page header, and where the evidence had to live

The header now carries the displayed page's icon to the left of its title and opens the same shared picker
from that slot. Verified on screen, on a scratch copy of the owner's Law board, in the running application:

| Step | Measured |
| --- | --- |
| Slot located from the frame | saturated 44x44 block at (874, 286), 156 saturated pixels before any change |
| Click on the slot | picker opened, window changed 8.16%, popover measured at left 910 right 1950 top 337 bottom 1561 |
| Emoji cell clicked in the grid | cell (986, 1169), window changed 8.22%, header saturation 156 to 236 |
| Application's own close route | file carries page "Criminal Law" `iconKey=emoji`, `iconEmoji=😀`, `recentIconEmoji=["😀"]` |
| Relaunch | header mark still detected by colour (152 saturated pixels), crop shows 😀 painted left of the title |
| Deliverable | `07678b69d3a8f43e…` byte-identical before and after; scratch deleted |

Frames: `~/Documents/9to1/boards-native-2026-11-04/frames/h01_board_open.png`, `h02_after_header_click.png`,
`h03_after_emoji.png`, `h03b_header_crop.png`, `h04_reopened.png`, `h04b_header_crop.png`, with
`header_run_memory.json` holding MemAvailable, swap free and the application's RSS at every step.

**The evidence location changed, and why.** This machine rebooted on 5 November and `/tmp` was cleared. That
destroyed the earlier drivers (`drive_window.py`, the icon probes) and the v01-v04 frames quoted in the table
above, so those files no longer exist even though their measurements were reported honestly at the time. The
tooling has been gathered in `~/Documents/9to1/boards-native-2026-11-04` - outside Git, outside `/tmp` - from
the copies that survived in `~/Documents/9to1/law-import-2026-11-03/native`. Evidence that only exists under
`/tmp` is not evidence the next person can check.

One correction to what this section first said: it claimed `drive_window.py` had been destroyed and rebuilt.
It had not been destroyed at all - the driver the native passes actually use is committed at
`eng/development/drive_window.py`, and when a reconstructed copy was dropped beside the scripts the committed
one won the import and the reconstruction was dead weight (removed). Only the icon-specific scripts and the
frames were genuinely lost with `/tmp`. Checked rather than assumed: `python3 -c "import drive_window;
print(drive_window.__file__)"` after importing `appctl`, which inserts `eng/development` on the path.

Three corrections in this pass, each found by measurement rather than assumption:

1. **The view model must not build media objects.** My first header version generated the icon image inside
   `RefreshPageIconState`, which runs during the document push - a push that happens on a background thread
   whenever a save or reload is in flight. Nine picker tests then failed with "The calling thread cannot
   access this object because a different thread owns it". The view model now publishes a textual catalog
   reference (`icon:file`, or `icon:file|#RRGGBBAA` when the owner has tinted it) and the UI-thread resolver
   turns it into a drawing, which is what Home already does. While there, the resolver carries the colour
   override into the theme-change observer and chooses the same factory for the first paint as the observer
   uses later, so a mark cannot switch silhouette families under a theme flip.
2. **An inline icon breaks the owner's alignment rule.** Every `Capture_*` test checks that block text aligns
   with the page title at 1 px, and an inline icon indented the title by 44 units - six captures failed on it.
   The row is inset by the icon's width plus its gap, so the mark overhangs into the column's left margin and
   the title returns to the content edge.
3. **Six of those failures were also a lie about the suite.** The plain `Capture_*` tests assert `File.Exists`
   on a frame, but the plain path never saved one: only the style-menu and picker paths called the save. They
   had been passing on PNGs left in `/tmp` by earlier runs. With `/tmp` cleared, every plain capture failed on
   a candidate that rendered fine. The save is now unconditional and happens before the checks, so a geometry
   failure still leaves the picture. Separately, `VisualCaptureTests` and the icon surface tests had no
   xUnit collection attribute, so they raced over process-wide CUI theme state: 13 failures that were not
   candidate defects became 7 deterministic ones once they shared the serialised Boards collection.

One thing that looked like a defect in the fresh frames and is not: the page title reads near-black in the
plain captures (`#111111`) while the dark capture shows `#F8F8FC`. The plain captures run the Light variant
under the dark Glow palette, a combination the owner's app never shows, and committed markup for the title is
unchanged. Recorded rather than "fixed" by recolouring a harness artifact.

## Third pass: the Board itself, and the card that shows it

The Board is now the third target for the same popover, entered from **Board menu → "Icon and emoji…"** (authored
in the `BoardSwitcher` menu beside Rename, in the position the menu's own order puts it). The session surface
exists on all three owners: the interface, the contract adapter - which writes through
`HavenRichNotesOps.SetBoardIcon`, so the contract stays the owner of the field - and the in-memory session the
tests and the embedded host use. The landing card reads the mark from the saved document: an owner-chosen emoji
as text, anything else through the same catalog reference the page header publishes, and a Board whose owner
never chose one keeps the mark it has always drawn.

What the running application showed, in order, with each number measured from the frame:

| Step | Measured |
| --- | --- |
| Board menu opened | the flyout is **its own X window**, 622x867, discovered by diffing viewable windows before and after the click; grabbing the application window could never have seen it |
| Menu rows located | nine text rows found in the authored order, with "Icon and emoji…" at y 580, between Rename… and Board properties… (`b02_menu_popup.png`) |
| That row clicked | the shared popover opened in the application window, panel at 910-1950 x 337-1561, window changed 8.26%; the title reads **"Board appearance · Law"** and Recent shows the emoji just chosen (`c03_landing.png` - labelled for what it actually is, not what was intended) |
| Emoji cell clicked | cell (986,1169), window changed 8.31% |
| Application's own close route | `richNotes` carries `iconKey=emoji`, `iconEmoji` the chosen sequence, `recentIconEmoji` recorded - and **no page or section icon changed**, which is what proves the target was the Board rather than whatever was displayed |
| Deliverable | `07678b69d3a8f43e…` unchanged before and after; scratch deleted; profile board metadata restored |
| Landing view after relaunch | Board menu → "All Boards…" (row five, y 418, needs 2.6 s for the flyout to map on a relaunched instance where 1.3 s was not enough). The **Law card shows the chosen emoji**; the three Untitled boards keep the maintained violet folder mark (`d01_landing.png`) |

Two native-driver faults found and fixed rather than worked around. A menu flyout is a separate override-redirect
window, so the first attempt reported "no popup" while reading the application window's pixmap; and the row
detector assumed light text on a dark surface, which is the icon popover's arrangement but not the menu's - it
is now adaptive against the surface the popup itself reports. The switcher label was profiled on the frame
(x 100-185, y 35-55): the first two attempts clicked the app icon and the status line, which do nothing.

Three faults in the product that the Board target exposed, all pre-existing:

1. `RefreshSectionIconPickerChoices` closed the popover whenever there was no section target. That is why
   opening it for a Board looked like a dead menu row.
2. The Icon tab and the colour row read `section.IconKey` and `section.IconColor`. For a **page** target the
   section is the page's parent, so the popover was marking the section's icon as the page's - wrong entity
   selected, wrong colour ticked. Both now read `CurrentIconSlot()`, one place, whichever entity opened it.
3. Every picker confirmation was written and then overwritten in the same action: the write path set StatusText
   ("Section icon set to books.") and then called the refresh, whose document push sets StatusText from the
   session status. The message that tells the owner which entity changed never reached the status line, for any
   target. Confirmations are queued and published after the refresh now.

The card's presentation members, the item bindings and the menu row are covered by tests, including one that
finds `BoardIconMenuItem` in the rendered surface, checks it is enabled in Edit mode and asserts that invoking
it opens the popover with target `board`. What is still not done at Board level: the board switcher's own label
shows no identity mark - its menu item takes an image `IconSource`, which cannot carry an emoji - and the
landing "Recent pages" list does not show page icons.

## Fourth pass: the landing rows, and the last honest gap

The "Recent pages" row now leads with the page's own mark, read from the saved file. Set through the real entry
points in the running application - Board menu row for the Board, page header slot for the page - then closed
through the application's own route and reopened:

| Measured | Value |
| --- | --- |
| Board menu row clicked | "Icon and emoji…" at y 580 in the flyout window; popover opened; grid cell chosen, window changed 8.30% |
| Header slot clicked | mark located at (874, 286) by colour, 156 saturated pixels before, 236 after the emoji was chosen; popover opened at 910-1950 x 337-1561, window changed 7.85% |
| Commit through the application | Board `iconKey=emoji` with the chosen sequence, **and** page "Criminal Law" `iconKey=emoji` - two entities, both written through the app, neither touched by a file edit |
| Landing view after relaunch | the **Law** board card leads with the chosen emoji and the **"Criminal Law" recent-page row leads with its own emoji**, while the three Untitled boards keep the maintained violet folder mark (`frames/e02_landing.png`) |
| Deliverable | `07678b69d3a8f43e…` unchanged before and after; scratch deleted; profile board metadata restored |

The one Board-level surface still without a mark is the **board switcher label**: an authored menu item takes an
image `IconSource`, and an emoji has no image equivalent that would keep its colour, so this needs a framework
answer rather than a monochrome substitute. Recorded as open, not silently skipped.

## Fifth pass: the board switcher mark needed a framework answer, and the framework got one

The switcher was the last surface with no mark, and it was blocked for a real reason: an authored menu item can
only take an image (`IconSource`), and a colour emoji has no image equivalent that keeps its colour. Under the
standing rule - missing CUI expressiveness is a framework gap to implement, not a licence for a parallel
authoring path or a monochrome substitute - `CuiMenuItem` now has a first-class **`IconGlyph`**: a text mark
that shares the image slot's sizing (`IconSize`, same 36/20 defaults), its non-hit-testable role and its
precedence. Blank means no mark, because a bound empty value is the normal state before an owner chooses
anything; the compiler still refuses an authored empty attribute, so blank arrives only through a binding. A
text run longer than a mark is reported at load (CUIR053) instead of drawing a sentence in the icon box.

Two layout faults appeared only in the running window, not in any headless tree:

1. **The mark hid the Board's name.** The framework already hides a top-level item's label when a mark is
   present; that rule is left exactly as it was, because other surfaces are built on it. A text mark now hides
   the label only for an icon-only item. The first native crop showed `😀▾` with no "Law" at all - the control
   had lost the thing it exists to tell the owner.
2. **Mark and label then overlapped**, the emoji drawn across "Law", because a top-level item declares a single
   column - harmless while a mark always replaced the label. They now get separate columns with an 8-unit gap,
   and the framework test measures that gap by translated position, since bounds are self-relative and a naive
   comparison reads a correct layout as an overlap.

| Measured on screen | Value |
| --- | --- |
| Mark set through the Board menu, committed by the application's own close route | `iconKey=emoji` with the chosen sequence |
| Relaunch, switcher band sampled | saturated mark runs at x 90-126 in the toolbar band, `fileStillCarries` confirms the field |
| Frame | `frames/f02_switcher_crop.png` shows the mark, an 8-unit gap and the name `Law ▾` intact |
| Deliverable | `07678b69d3a8f43e…` unchanged; scratch deleted; profile metadata restored |

A native-process note worth keeping: the first spacing re-check looked unchanged because the launcher runs the
built application, and the margin had only been compiled into the test run. A native re-run needs the
application rebuilt first - `CakeOS.Cui.Runtime.dll` timestamp checked afterwards (21:52) before the pass that
produced the gap.

Scope stated honestly: the switcher carries only the **text** form. Giving that top-level item an image mark
would hit the pre-existing hide-the-label rule, and changing that rule for every CUI app is a framework
decision needing its own evidence, so a Board marked with a vector silhouette shows its name alone in the
switcher while its card, tree row and page header still carry the mark.

## Still open on this surface



- Done as of the second pass above: the page header carries the page's icon and opens the picker from it,
  verified on screen.
- Done in the fifth pass: the switcher shows an owner-chosen emoji mark beside the Board's name, on screen.
  A Board marked with a **vector** silhouette still shows its name alone in the switcher, because the
  framework's pre-existing top-level image rule hides the label - recorded above, not quietly worked around.
- `Suggest` remains section-only, because the model suggestion flow is driven from a section name; the tab is
  disabled rather than hidden inert for a page target.
- Upload (image icon) and cover images remain deferred with the reason recorded above, not dropped.
- Independent acceptance remains paused by owner instruction; none of the above is acceptance.
- Environment note for the next native pass: this PC had its 4 GiB swap completely consumed while the resident
  model host is running. A large board launch can be starved into an unpainted window, which looks identical
  to a rendering defect unless memory is sampled alongside it.

## Implementation units, each compiled before the next

1. Move the emoji catalogue and query engine to `Haven.Infrastructure`; forward in
   `Haven.Desktop.Prefabs`. Compile `Haven.Desktop` + `CakeOS.Apps.Boards.Contract`; run the existing
   composer emoji tests unchanged.
2. Contract: `IconEmoji` on section and page, `"emoji"` key, validator rules, ops
   (`SetSectionIconEmoji` / `SetPageIcon`), `HavenRichPage.IconKey`/`IconColor`, plus codec and validator
   tests including an additive-compatibility round-trip of an existing schema-5 file.
3. Renderer: `BoardsCuiIconResolver` produces an emoji element (text glyph on an optional colour tile)
   next to the existing vector path; `NavBuilder` stops hard-coding the page glyph.
4. Picker: one CUI popover for section and page with the tab row, search, categories, skin tone, random,
   Recent, colour row, Remove; VM commands for both targets; page row menu entry.
5. Native verification on a scratch copy of the Law board: set an emoji on a section and on a page,
   capture on screen, close, reopen, and confirm the icon survived — with the deliverable
   `Law.9to1board` left byte-identical.

Every claim in the final report states its evidence level: test, on-screen capture, or not yet observed.
