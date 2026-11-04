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
