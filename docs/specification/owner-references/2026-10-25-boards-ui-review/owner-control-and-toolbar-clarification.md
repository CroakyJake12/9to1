# Owner control hierarchy and Present toolbar — 25 October 2026

The owner clarified: “glow theme is also wrong”, borders appear where they should
not, and “not all elements should be an accented pill”. Reference images show
accent-dependent Primary, Secondary, Tertiary, Text and Negative button roles.
Primary/Secondary use borderless rich fills; Tertiary is quieter; Text has no
container; Negative retains its semantic red. Glow hover morphs toward a brighter
variant with outward glow; click feedback briefly enlarges/bounces. Inputs are
borderless in their ordinary state with an accent outline on focus. Switches use
a rounded track, distinct on/off state and a movable pale thumb. Preserve focus,
reduced-motion settings and meaningful selected/data boundaries. Professional
remains glow-free. Do not hard-code the orange sample as the universal accent.

The Boards/AppFlowy comparison calls for quiet navigation/text/icon controls and
a subdued selected row; blanket borders and accent-filled pills are inappropriate.
The owner then supplied the Present mockup and requested the same kind of toolbar:
a document title/save-status area, text menu row, and one rounded neutral toolbar
containing compact icon groups separated by fine dividers. Dropdown fields have
subdued containers. Formatting and navigation icons are quiet; actual color wells,
selected states and primary actions retain meaningful accent treatment. All real
Board actions remain accessible at narrower widths through supported overflow.
Do not add inert replicas of features pictured only in Present.

These descriptions record visually inspected inline references. The inline source
images are not claimed to be available as local files. The latest navy-shell
clarification remains applicable. Home still owns shared profile/model/notification
controls in embedded mode; standalone and Home use the same Boards implementation.
The canonical specification and other explicit owner requirements remain in force.

## Equation presentation clarification

After viewing the newer capture, the owner said equations should appear inline
like normal text with mathematical formatting, without a default yellow block.
Highlights are an explicit user formatting choice, not an intrinsic LaTeX style.
The observed yellow sample came from `VisualFixtures.BuildMathsAsync`: it assigned
a `Key Formula` custom style with background `#FFFFF3C4` to a paragraph containing
superscript text runs. It was not evidence of an implemented LaTeX renderer.
Use ordinary unhighlighted formatting for that representative sample; preserve
intentional user highlights and separately verify actual LaTeX support before
claiming it works.

## Follow-up on the rendered Boards capture

The owner requested these refinements after the first navy-layout render:

- Make the selected-page accent marker slightly wider and taller.
- Give section labels a larger type size than page labels.
- Align checkboxes with their associated text.
- Put a small, recognizable drag grip to the left of the plus, without its own
  filled background. Make the plus slightly bolder and higher contrast. Preserve
  actual drag, block actions, focus, and accessible hit targets.
- Apply the updated shared Glow theme. Boards defaults to purple unless the
  user's settings override it; retain the canonical per-app preference path.
- Put the Boards app icon in the top-left position formerly occupied by the Board
  title. The current Board name belongs in the sidebar selector shown by the
  earlier mockup; retain renaming and live Board switching.
- Improve icon accuracy and vector quality. Do not reuse unrelated symbols for
  distinct actions or replace real vector icons with font-dependent glyphs.
- Remove the oversized horizontal toolbar scroll rail while keeping every
  command available at smaller widths through supported overflow/navigation.
- Tables inherit the active Glow theme unless the user has explicitly overridden
  table or cell appearance. Preserve intentional saved overrides.

The referenced capture predates the shared Glow patch. That observation is not
evidence that the subsequently integrated theme code has rendered correctly.

## Later live refinements (25 October, after the themed-table capture)

- Horizontal toolbar scrolling is allowed. Keep one row and provide left/right
  arrows over soft shadow backdrops instead of the large scrollbar. Preserve
  keyboard and wheel access to every command; wrapping is superseded.
- The surrounding background should be only slightly darker than the content
  background. Boards uses purple for accents unless explicitly overridden; blue
  selections and active-control fills must not dominate through a Home/Browse
  loader identity or borrowed palette.
- Undo and redo use recognizable icons. Editing/Viewing and Vertical/Free use
  two-position sliding selectors. Vertical/Free are the owner's presentation
  labels for the canonical page placement behavior, not a rename of the separate
  freeform-object position lock. Preserve existing absolute positions and data.
- Make section headings slightly larger again. Section icons are configurable
  and coloured, with contrast adapted to the current background. Offer a fixed
  filled-icon family and a few live name-based generated suggestions in exactly
  that design scheme. Do not report static name matching as actual generation.
- Block insert/reorder controls appear only while their element is hovered or
  focused (including keyboard focus). List Add item follows the same behavior.
  Keep stable layout, accessible hit targets and correct vertical alignment.
- Repair the visibly clipped table border; maintain its themed cell/header
  appearance and explicit user overrides.
- Give the bottom Add page or section action a pill container specifically;
  this does not authorize turning the rest of the interface into pills again.

These later explicit decisions supersede any intermediate wrapping proposal or
static capture. Implementation, source compilation, basic rendering and full
acceptance remain separate states.

- Enlarge subscript/superscript glyphs and place them after the font and font-size
  selectors.
- Remove the redundant topbar Boards / Board / Page breadcrumb; sidebar and page
  title already establish location.
- Once the scrollbar is removed, reduce toolbar height to fit one comfortable
  button row plus padding; allow the document's flexible row to use the freed
  height instead of retaining empty toolbar space.

## Subsequent live review: compact header and softer functional icons

The latest owner requests supersede the earlier top-left brand position and
intermediate layout labels above:

- Put the remaining header actions on one line at roughly the File button's
  height. Place the Boards app icon immediately right of File. Use 16–18px
  undo/redo glyphs in comfortable 32px targets. Remove Appearance, retain its
  settings through the shared menu, and replace More text with an accessible
  three-dot icon. Remove the redundant breadcrumb.
- Keep the Board name in the sidebar selector, with saved state underneath;
  center the combined identity vertically with its icon and chevron.
- Editing/Viewing and Vertical Stacking/Free Placement use the same switch
  treatment, with reduced vertical and increased horizontal container padding.
  Increase the label-to-switch gap and label weight. The native page placement
  mode is distinct from the freeform object lock.
- Make the Board name and Add page or section text bolder. Only the latter
  action is a pill; do not turn ordinary controls into accented pills again.
- Keep the toolbar one compact row with shadow-backed overflow arrows. Make
  the document scrollbar visually thinner, expand the content panel to the
  visible toolbar width, and slightly reduce its corner radius. Let the board
  and sidebar fill the vertical space recovered from the header and toolbar.
- Make the default purple surface warmer (less blue), with visible but soft
  Glow gradients. Respect actual user overrides and retain the separate
  Professional treatment; the surroundings are only slightly darker.
- Use a custom bold vector check, with checked boxes filled in purple or the
  user's accent. Reduce the checkbox/text gap slightly and preserve alignment,
  disabled, focus, high-contrast and indeterminate states.
- Place block drag and insert controls in the left margin, before the text/title
  column. Reveal them only on hover or keyboard focus, not persistent selection.
  Keep content/title alignment and accessible hit targets without moving text.
- Icons should be softer and more colourful, matching the supplied toolbar
  reference. Use meaning-specific colour sparingly and make each symbol
  identify its actual action. Keep consistent optical sizes and silhouettes,
  actual scoped theme/disabled updates, high-contrast legibility and hover labels.
  Indent/de-indent need line-and-arrow glyphs, not unrelated navigation chevrons.

These are implementation requirements, not claims that a screenshot, native
runtime or full acceptance has passed. Earlier captures remain dated evidence.
