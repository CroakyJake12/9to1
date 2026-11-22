# Present document interactions

Slide selection, object selection and text editing belong to the same Present document. Keep stable slide and object identities while a drag, resize, keyboard edit or text edit is pending. Commit one logical operation to the existing editor and its undo history.

Project a temporary drag affordance in the current view. Deleting the active object, switching slides or losing the original document retires that operation. Verify pointer and keyboard routes against the same document, including after a save/reopen.

The contracts below cover compound objects, text and viewport ownership. Preserve author-specified slide content when the application theme changes.

## Structured document objects

The shared canvas draws tables and charts from the existing `haven.present.table.v1`
and `haven.present.chart.v1` payloads. Rendering does not mutate those payloads.
Cell text, header weight and each cell's fill colour are retained. Cell clips prevent
long text from painting into neighbouring cells. These are document objects, so
colours and fonts come from the presentation theme rather than application chrome.

Column and Bar use a shared signed zero baseline and cluster every series. Line
connects each series independently, Area adds a translucent fill to zero, and
Scatter draws independent markers at the owner model's categorical X positions.
The current model has no separate numerical X-value array; the renderer does not
invent one. Zero values do not acquire artificial nonzero bars. Scaling handles
finite extremes without overflowing an intermediate `max - min`.

Pie and Doughnut render one labelled pie/ring per series, with the category legend
controlled by `ShowLegend`. Sector areas encode absolute magnitude, as ordinary
pie charts do; signed numeric labels retain negative source values. A zero-only
series displays zero, and a single nonzero value produces a real full circle.
All existing series/category limits are rendered, though dense data naturally
requires enlarging the chart for readable labels. The existing free-form `Style`
value remains preserved; no undocumented style-name interpretation is introduced.

`PresentStructuredRenderingChecks` exercises the production retained renderer in
the native headless application, saves screenshots of all seven chart types and
edited tables, checks the actual emitted geometry against signed source values,
and saves/reopens the document. This focused native evidence does not establish
browser, packaged-device, PowerPoint round-trip or LibreOffice Impress parity.

## Text-bearing shapes

Plain and vector shapes paint their existing `PresentElement.Text` with the same
clipped, rotated text renderer used by text objects, preserving the owner's
`TextStyle` and opacity. A single click selects the shape for movement; double
click or Enter enters the existing text editor. Typing, commit, undo and redo use
the original owner transaction. Empty shapes acquire real text only when typed.

The `shape-text` native regression imports and saves the independently authored
24-slide fixture `operations-review-24.pptx`, confirms all 96 text-bearing shapes
remain in the repository, exercises real pointer/key input and save/reopen, and
captures the running native interface. The current importer retains one uniform
text style per shape; mixed source runs and text insets are not represented by
that model. These native checks establish neither mixed-format PPTX fidelity nor
browser/device or LibreOffice Impress acceptance.

## Thumbnail wheel and viewport ownership

The thumbnail navigator receives logical scroll pixels from the shared input
router; the native backend has already converted wheel direction and magnitude.
Positive Y scrolls downward once. Its native host accepts input over unpainted
gaps and the gutter as well as rendered thumbnails. Browsing does not select or
mutate a slide, and an unchanged session/status refresh preserves the offset.
Selection changes and actual viewport-size changes bring the selected thumbnail
back into view; Home/End also reveal an already selected slide after browsing.
The existing CUI scrolling composition and renderer remain in place.

The `thumbnail-wheel` native regression uses the independently authored 24-slide
PPTX, real window wheel/pointer/key input and current canonical CUI. It checks
bidirectional scrolling, gutter routing, dynamic visible automation items, exact
model preservation, read-only browsing, notification stability and last-slide
visibility at 1280├ù900 and 800├ù700. Failed pre-repair native evidence is retained
outside the repository. Browser29 must independently retest these workflows;
these native results alone are not browser, donor or whole-app acceptance.

## Scoped CUI resources in retained scenes

Retained scene chrome resolves semantic brushes through the native scene's
resource ancestry, including the original CUI Object's explicit theme scope.
The existing application resources remain the inherited fallback. A scoped
resource change invalidates painting without recreating the scene, changing the
editor selection or reconciling the document. Canvas selection handles, guides,
caret and thumbnail highlights therefore consume the same canonical CUI palette
as their owning controls. Literal authored slide colours remain document data;
this adapter does not recolour them or introduce a Present-specific palette.

The `scoped-theme` regression loads the current canonical Present CUI with
explicit Object themes, checks actual native pixels for handles and thumbnail
highlights, changes the scope live, and checks literal document pixels and exact
repository preservation. This establishes the scoped resource bridge only;
browser/device and whole-app visual acceptance remain separate requirements.

[Transforms](../../src/Haven.Application/Present/PresentEditor.InteractionTransforms.cs) · [Text editing](../../src/Haven.Application/Present/PresentEditor.TextEditing.cs) · [Editor layout](present-editor-redesign.md)
