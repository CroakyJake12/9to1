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
