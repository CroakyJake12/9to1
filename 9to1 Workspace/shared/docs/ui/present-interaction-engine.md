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
