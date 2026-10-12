# Present interaction engine

The Present UI and interaction lanes share the same `PresentEditor`, document IDs,
selection, mutation history and specialised CUI Object renderer. This is an extension
of those owners, not a second editor. See [Building UI](building-ui.md) and the
[system map](../architecture/system-map.md).

## Ownership and hierarchy

`PresentElement` already separates object identity, position/dimensions, text and
formatting. The text-box hierarchy did not require a replacement document model.
Object selection and content editing are separate modes of `PresentSlideCanvas`:
single-click selects, a selected object's body drags, double-click or Enter edits,
and Escape commits the text then returns to selection. Handles and the border can
leave text editing and start manipulation. Cancellation of a transform discards its
transient preview; pointer moves never append document history.

Groups retain their existing `ParentGroupId` graph. Hit testing and drawing use the
same parent stacking order. A group operation transforms its actual descendants;
copy/paste remaps the complete graph, and deleting a group removes its descendants.
Group movement, resizing, rotation, alignment, distribution and stacking each use
one existing owner mutation. `PresentInteractionTransforms.ProjectChild` is shared
by live preview and committed group transforms.

The owner still requires its existing body-text element. A newly created or cleared
slide can therefore contain an empty, selectable body-text object. This compatibility
behaviour is not a duplicate slide renderer or development fixture.

`PresentSlide.Title` is the navigation name, independent of all authored text.
Rename and its preview change that metadata through the original owner history;
normalization, editing or deleting a title object never replace it. Duplication
names the copied slide without rewriting its content. A real title-role text
object retains the same selection, movement, editing and deletion semantics as
other authored text. The canvas and thumbnail preview paint only real elements;
the thumbnail label displays the metadata. There is no synthetic canvas title or
invisible title editing region. PPTX stores the name in `p:cSld/@name`; real title
objects export separately with their geometry and style. External files without
name metadata may seed their initial name from the imported title placeholder.
The `slide-metadata` and `title` native checks cover independent names/content,
undo/redo, physical editing, retained rendering and repository/PPTX round trips.

## Input and rendering

`IHavenHoverInputTarget` receives uncaptured local hover positions independently of
captured gestures. Leaving a surface clears hover without clearing selection.
`IHavenCursorInputTarget` remains the backend-neutral cursor contract. Native cursor
mapping and capture live in `HavenSceneControl`; no platform input types were added
to the shared document model.

The canvas reports `PresentInteractionState`, `HoveredObjectId` and `LiveGeometry`.
`InteractionChanged` reports transitions; `GeometryPreviewed` permits consumers to
read transient geometry without committing it. Hover, caret and geometry changes
invalidate paint only; they do not request CUI structure/style reconciliation or
layout. Ordinary rectangles expose eight handles and rotation. Tiny objects reduce
overlapping handle presentation, and hit testing chooses the nearest visible handle.
Rotated cursors and resize deltas use the same object-space transform.

The existing registered `PresentRetainedSceneControl` is the UI boundary:

- `BeginShapeInsertion(string presetId, DocumentVectorShape preview)` starts a
  transient preview using the actual shared geometry.
- `ShapeInsertionRequested` emits `(presetId, x, y, width, height)` in normalised
  slide coordinates once after a click or drag. The UI invokes its existing owner
  insertion command after rechecking the current document/slide authority.
- `CancelInteraction()` cancels manipulation/insertion/pan without changing text
  contents or persisted object formatting.
- `ZoomRequested` emits an actual authored-slide scale. The session remains the
  authoritative zoom owner; the control never maintains a competing persisted value.
- `SetZoom(double, bool fitToWindow)` distinguishes Fit from 100%. The older
  single-argument overload retains its old relative-Fit behaviour for callers that
  have not migrated. `EffectiveZoom` reports the currently rendered scale.

Manual zoom uses slide inches × 96 logical pixels and does not change when panels or
the window resize. Fit measures the actual constrained editing stage, preserves the
slide aspect ratio and centres it. The session's `IsZoomed` binding enables the CUI
scroll viewport for every manual scale, including 100% and smaller scales that can
overflow a narrow stage. Ctrl-wheel and backend pinch request the same zoom state;
window-space anchoring survives creation of the first scroll wrapper when leaving
Fit. Ordinary wheel events continue to the appropriate owning scroll container.
Middle-button and Space-drag pan that viewport. Capture loss, focus leaving the
canvas, window deactivation and explicit zoom changes clear transient pan state.

## Validation boundary

`apps/Web/Productivity/Present/Tests` contains the native retained CUI `engine`
workflow. It exercises real pointer/key/wheel routing, hover, all eight rotated
handles, cancellation, actual 50/75/100/150/200% geometry, initial Fit-to-manual wheel
anchoring, ordinary scroll, pan/focus interruption, group transforms and hierarchy,
insertion preview, text editing, undo and repository save/reopen. Out-of-viewport
handle cases use the same original input router with measured local coordinates.
`HavenHoverInputTests` covers the neutral hover/capture boundary.

Native headless frames and those tests do not establish browser/device acceptance,
physical trackpad support, LibreOffice Impress parity or whole-application VERIFIED
status. The integrated application must still undergo the requested browser visual
and functional workflows, all applicable appearance/platform checks and donor gates.

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
visibility at 1280×900 and 800×700. Failed pre-repair native evidence is retained
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
