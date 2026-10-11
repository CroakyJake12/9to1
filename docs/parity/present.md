# Present parity and acceptance

Current reconciliation: 11 October 2026. Status: **CHECKPOINT — not RCVP, Full or independently VERIFIED.**

The earlier September record incorrectly described the app as having no CUI resource or browser adapter. The actual browser entry is `apps/Web/Productivity/Present/`; it uses `PresentDocument / PresentEditor` and the maintained retained document surface. Source presence and earlier focused tests do not establish donor-complete acceptance.

The actual native `9to1 Workspace/Present/Program.cs` launches `Haven.Desktop.App.RunOriginalDesktop`. An isolated `PresentApplication` or test host is not proof of the shipping native route. Native chrome still requires canonical CUI conformance and current packaged runtime evidence.

The LibreOfficeKit donor integration remains a mandatory acceptance gap. Its separate native engine/probe directory is not evidence that the normal native or web entry point uses it. The existing document model must not be described as a substitute satisfying complete donor parity.

The current specification requires APP-AC, LAUNCH-001–006, PARITY-001, CUI-001–002, DESIGN-001, STAGING-001, ACCEPT-001–006 and GOV-001–002 (and WRITE-AC for Write). Acceptance requires a version-pinned exhaustive donor feature inventory, approved stage assignments/exceptions, independent start/editor composition, real retained-session transitions, complete packaged workflows and format fidelity, privacy/accessibility, platform/performance evidence and independent review. None is waived by an attractive shell or a passing focused test.

## Recovered historical worker record

The report below is preserved from the old OpenCode checkpoint `d66d27a1fe0a10238f83d2d318352f39c0fa1a00` during worktree consolidation. Its implementation labels, test totals and environment observations describe that historical worker report only; they are **not current independent acceptance evidence**. Preserve the useful behavioral detail while validating the current integrated source afresh.

---

# Present Parity Record

Audit date: 2026-09-20
Re-audited: 2026-10-10 against the standalone host, native engine, Desktop
surface and browser adapter delivered on `opencode/later-apps-20261009`.

## Sources inspected

| Role | Source |
| --- | --- |
| Donor sketch | `reference/cakeos/phase2b-worker2-app-sketches/apps/Present/App/PresentEngineContracts.cs`, `PresentEngineImpl.cs`, and `Hui/PresentHuiScene.cs` |
| Current standalone host | `9to1 Workspace/Present/PresentApplication.cs`, `PresentAppHost.cs`, `Program.cs`, and `HavenOS.Present.csproj` |
| Current host test | `9to1 Workspace/Present/Tests/PresentAppSurfaceTests.cs` |
| Current native engine | `9to1 Workspace/Present/present-engine/` (engine, semantic layer, worker, smoke executable, and contract tests) |
| Editor | `9to1 Workspace/shared/src/Haven.Application/Present/PresentEditor*.cs` |
| Desktop surface | `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Present/` (`PresentPage*`, `PresentHavenScene*`, `PresentSlideCanvas*`) |
| Browser surface | `apps/Web/Productivity/Present/` (`PresentBrowserSession.cs`, `PresentBrowserFeature.cs`, `PresentBrowserSurface.cs`, `Present.cui`, `Scene/`, `Tests/`) |
| Browser registration | `apps/Web/BrowserFeatureComposition.cs` (`app.present` route) |

## Exact status

| Capability | Donor sketch | Current source | Status | Evidence / blocker |
| --- | --- | --- | --- | --- |
| Standalone desktop host | Sketch app project only | Avalonia `PresentApplication` hosts `PresentPage` through `PresentAppHost` | Implemented | Focused headless test initializes the host and exercises existing document editing/playback objects. |
| Repository, import, and export integration | Donor native wrapper does not establish Desktop DI | `PresentAppHost.CreateDefault` resolves the existing infrastructure services | Implemented | Source inspection; the focused test uses supplied in-memory/no-op services. |
| Slide lifecycle, render, worker protocol, and ODP/PPTX round trip | Donor LOK wrapper contract | Native `present-engine` implements a C++ engine and framed worker | Implemented in the native engine; not wired to the standalone host | The current `.csproj` references existing Desktop/Application/Infrastructure projects, not `present-engine`. No integration claim is made. |
| Element text editing, selection, and move-slide worker operations | Donor sketch exposes broader native methods | Native engine has bounded snapshot-scoped element text replacement and slide move; worker advertises capabilities conditionally | Implemented in the native engine; runtime not revalidated locally | Requires Linux LibreOfficeKit, nlohmann-json, CMake, and fixtures. CMake is unavailable on this host. |
| Finished HUI Present surface | Donor HUI scene sketch | No current host connection to the native HUI/worker path | Not established | The native engine README explicitly excludes a finished HUI visual surface. |
| Browser presentation model | Donor contract is engine-shaped | `PresentBrowserSession` drives the unchanged `PresentEditor` over an injected `IPresentRepository` and introduces no second document model, drawing engine, animation runtime or repository identity | Implemented | `PresentBrowser.Scene.Tests` journey and `PresentDurable.Tests` seed/verify both pass against the real filesystem repository and `.9to1p` codec. |
| Browser canvas editing | Donor HUI sketch leaves several actions as comments | The four unchanged `PresentSlideCanvas` source files are compiled into `NineToOne.Present.BrowserScene`; typed selection, movement, transform, vector and live-text events call the owning editor operations | Implemented | The scene journey types a raw native title through the retained canvas, commits it with Ctrl+Enter and observes the owning undo transaction. |
| Browser authoring commands | Donor HUI sketch exposes a narrower action list | `PresentBrowserSession.DispatchAsync` exposes `Create`, `Refresh`, `Save`, `Close`, `Present`, `Advance`, `PlayPrevious`, `ExitPresentation`, `Inspect`, `Open`, `SelectSlide`, `AddSlide`, `DuplicateSlide`, `AddText`, `AddShape`, `DeleteElement`, `BringToFront`, `SendToBack`, `Bold`, `Italic`, `FontLarger`, `FontSmaller`, `ApplyFill`, `Grow`, `Shrink`, `RotateLeft`, `RotateRight`, `AlignLeft`, `AlignCenter`, `AlignTop`, `AlignMiddle`, `DistributeHorizontal`, `DistributeVertical`, `Group`, `Ungroup`, `BringForward`, `SendBackward`, `Copy`, `Paste`, `MoveObjectLeft`, `MoveObjectRight`, `MoveObjectUp`, `MoveObjectDown`, `ApplyStroke`, `ApplyStrokeWidth`, `HideSlide`, `ShowSlide`, `DeleteSlide`, `MoveSlideEarlier`, `MoveSlideLater`, `Undo` and `Redo`, plus `ExportPowerPoint` and `ImportPowerPoint` | Implemented | The scene journey asserts each control enables only while its owning command is available, including under a read-only route. |
| Slide lifecycle on the browser surface | Desktop `PresentHavenScene` drives `Present.Slide.Delete`, `Present.Slide.MoveEarlier` and `Present.Slide.MoveLater` | The same three operations are exposed as `DeleteSlide`, `MoveSlideEarlier` and `MoveSlideLater` over `PresentEditor.DeleteSlide` and `PresentEditor.MoveSlide` | Implemented | The scene journey asserts the two deck-boundary controls track the selected slide against both deck ends, that moving a slide reorders the retained deck, that both controls re-evaluate after the move, that moving back restores the authored order, and that deleting the selected slide removes exactly it and selects the survivor. |
| Presentation title and speaker notes | Desktop `PresentPage` writes `SetDocumentTitle` and `SetSpeakerNotes` | Both are two-way inputs through `PresentBrowserSession.TrySetValue`, gated on an open editable presentation | Implemented | The scene journey types a title through the header input and observes the canonical document title, types speaker notes through the notes input and observes the slide's notes, and asserts the inputs freeze under a read-only route. |
| Element delete and z-order | Desktop `PresentPage.Phase2` drives `RemoveSelectedElements`, `BringToFront` and `SendToBack` from its selection context menu | The same three operations are exposed as `DeleteElement`, `BringToFront` and `SendToBack` over the editor's own selection state | Implemented | The scene journey adds a text object through `AddText`, asserts all three controls enable while the retained canvas selection is non-empty, deletes it and asserts they disable again, then adds a shape, sends it to the bottom of the stack while asserting it stays selected, brings it back to the front and deletes it so the slide returns to its original object stack. |
| Element text style and object transform | Desktop `PresentPage.Phase2` toggles bold/italic from the selection toolbar and grows, shrinks and rotates the selected object | The same six operations are exposed as `Bold`, `Italic`, `Grow`, `Shrink`, `RotateLeft` and `RotateRight` over `PresentEditor.SetSelectedTextStyle`, `ResizeSelection` and `RotateSelection`, each gated on the live retained selection | Implemented | The scene journey asserts bold and italic reach only the selected text object's own style, that a shape disables both style commands while enabling the transform commands, that grow and shrink move the object's width by the owning delta, and that rotate right/left move rotation to and from fifteen degrees. |
| Retained canvas selection notification | Desktop `PresentPage` re-reads the editor selection on every canvas click | `PresentRetainedSceneControl` raises `SelectionChanged` from `Select` and `SelectMany`, and `PresentBrowserSurface` forwards it to `PresentBrowserSession.RecordRetainedSelection`, which notifies the live bindings | Implemented | The scene journey clicks the body text on the native canvas and observes the selection-dependent controls enable, then clicks empty canvas and observes them disable again. |
| Presentation playback | Desktop `PresentPage.Phase2` starts `PresentPlaybackSession` over the owner document and advances, steps back and ends it | `Present`, `Advance`, `PlayPrevious` and `ExitPresentation` drive the same `PresentPlaybackSession` over a private snapshot, keep the retained canvas on the frame's slide, and freeze authoring, lifecycle and canvas input for the run | Implemented | The scene journey starts a two-slide presentation, asserts the opening frame, the frozen authoring chrome and the disabled native canvas, advances and steps back across the deck with the boundary controls, and asserts that ending the presentation restores the canvas and the authoring chrome. |
| Object arrange | Desktop `PresentPage.Phase2` drives align, distribute, group and ungroup, and the workspace scene drives forward and backward z-order | The same ten operations are exposed as `AlignLeft`, `AlignCenter`, `AlignTop`, `AlignMiddle`, `DistributeHorizontal`, `DistributeVertical`, `Group`, `Ungroup`, `BringForward` and `SendBackward` over `AlignSelection`, `DistributeSelection`, `GroupSelection`, `UngroupSelection`, `BringForward` and `SendBackward`, gated on the owning selection counts and element kinds | Implemented | The scene journey selects two objects through a real Shift-click on the native canvas, asserts align and group enable while distribute still waits for a third object, aligns them onto one left edge, adds a third object by another Shift-click, distributes the three left edges, groups them into one group object, ungroups back to the original objects, and then nudges one object up and back down the stack. |
| Slide visibility for a presentation | Desktop `PresentPage.DesignPlayback` carries a "Hide slide during presentation" toggle over `SetSlideHidden`, and `PresentPlaybackSession` filters hidden slides out of its frame list | `HideSlide` and `ShowSlide` set the current slide's `Hidden` flag through `PresentEditor.SetSlideHidden`, and the slide rail carries both controls plus a `SlideHidden` binding and a `· hidden` suffix on the status | Implemented | The scene journey asserts the controls report the current slide as visible before hiding, hides the current slide and observes the flag plus the swapped controls, shows it again and observes the controls swap back, then hides the opening slide, starts a presentation and asserts the running frame list holds only the visible slide, restores the slide and closes. |
| Object clipboard and nudge | Desktop `PresentPage.Phase2` keeps an `_objectClipboard`, copies with `_editor.CopySelection()`, pastes with `_editor.Paste(...)` and nudges with `MoveSelection(±0.01, 0, snap: true)` / `MoveSelection(0, ±0.01, snap: true)` | The same donor steps are exposed as `Copy`, `Paste`, `MoveObjectLeft`, `MoveObjectRight`, `MoveObjectUp` and `MoveObjectDown` over `CopySelection`, `Paste` and `MoveSelection(deltaX, deltaY, snap: true)`, with a session-scoped clipboard that is cleared whenever a document is attached, gated on a live selection and an editable deck | Implemented | The scene journey copies the styled text object, deletes it, observes `Paste` enable while `Copy` disables, pastes a rebuilt object under a new identity that keeps the bold and italic style, then nudges the selected shape one donor step right, left, down and up and asserts both axes land back on the original coordinates. |
| Element property controls | Desktop `PresentPage.Phase2` edits the selected object's point size and colours from its property chrome | `FontLarger`, `FontSmaller` drive `SetSelectedTextStyle` for the selected text object; `ApplyFill` writes the two-way `FillColor` input into `SetSelectedElementStyle` for the selected shape; `ApplyStroke` and `ApplyStrokeWidth` follow the donor's outline menu, taking `SetSelectedVectorStroke`/`SetSelectedVectorStrokeWidth` on a vector shape and the style path with a transparent stroke and a zero width for a blank colour otherwise | Implemented | The scene journey asserts a shape enables the fill and stroke controls and disables the font size controls, types a colour through the fill input and observes it stay out of the object until `Apply fill`, then types an outline colour and a width of six, observes both stay out of the object until they are applied, and asserts the applied shape carries the typed fill, the typed stroke and the typed width; a text object disables every shape property control while enabling the font size controls. |
| Import and export chrome | Donor contract implies presenter/authoring round trips through the engine | `ExportPowerPoint` writes the open deck through the owner `PresentPptxExportService` and hands the bytes to the browser as a `.pptx` download; `ImportPowerPoint` reads a picked file back through the owner `PresentPptxImportService` and commits it as a new stored presentation | Implemented for `.pptx` | The scene journey exports the open deck, asserts the delivered bytes are a real PowerPoint package carrying `[Content_Types].xml` and `ppt/presentation.xml`, then re-imports those same bytes and asserts the rebuilt deck keeps its slide count and slide title. ODP is not offered by the owner services (`ExportExtensions`/`ImportExtensions` are `.9to1p` and `.pptx`), so no ODP claim is made. |
| File picker | Donor contract implies an open dialog | PowerPoint import opens a real browser file chooser (`input[type=file]`) through the Present transfer bridge; saved presentations still open from the in-page list | Partial | The scene journey feeds the picker a real `.pptx` and observes the rebuilt deck. No donor parity is claimed for opening saved presentations through an OS dialog. |
| LibreOffice donor engine | LOK-backed render/edit/save | Native `present-engine` probes exist and are Linux-only; the production path remains the filesystem `PresentRepository`/`PresentDocument` | Not integrated | Windows workspace has no LibreOfficeKit runtime. Donor integration is a later, separately gated step. |
| CUI parity | No `.cui` artifact | `apps/Web/Productivity/Present/Present.cui` (101 lines) drives the browser surface; the scene journey lowers it through the real retained registry and captures rendered frames | Implemented and validated | Scene journeys: workflow 128, staged admission 14 assertions, all rendered from the authored markup. |

## CUI surface

`Present.cui` declares fifty-two dispatchable commands: `Create`, `Refresh`,
`Save`, `Close`, `ExportPowerPoint`, `ImportPowerPoint`, `Present`, `Advance`,
`PlayPrevious`, `ExitPresentation`,
`Inspect`, `Open` (reached through the `OpenSavedPresentation`
action reference), `SelectSlide` (reached through the
`SelectPresentationSlide` action reference), `AddSlide`, `DuplicateSlide`,
`AddText`, `AddShape`, `DeleteElement`, `BringToFront`, `SendToBack`, `Bold`,
`Italic`, `FontLarger`, `FontSmaller`, `ApplyFill`, `Grow`, `Shrink`,
`RotateLeft`, `RotateRight`, `Copy`, `Paste`, `MoveObjectLeft`,
`MoveObjectRight`, `MoveObjectUp`, `MoveObjectDown`, `ApplyStroke`,
`ApplyStrokeWidth`, `HideSlide`, `ShowSlide`, `AlignLeft`,
`AlignCenter`, `AlignTop`,
`AlignMiddle`, `DistributeHorizontal`, `DistributeVertical`, `Group`,
`Ungroup`, `BringForward`, `SendBackward`, `DeleteSlide`, `MoveSlideEarlier`,
`MoveSlideLater`, `Undo` and `Redo`.

Forty-eight root-scope bindings resolve against the live session: `Title`,
`Status`, `SpeakerNotes`, `SlideStatus`, `Documents`, `Slides`, `FillColor`,
`StrokeColor`, `StrokeWidth`,
`CanCreate`, `CanBrowse`, `CanEdit`, `CanSave`, `CanClose`, `CanUndo`,
`CanRedo`, `CanInspect`, `CanDeleteSlide`, `CanMoveSlideEarlier`,
`CanMoveSlideLater`, `CanDeleteElement`, `CanBringToFront`, `CanSendToBack`,
`CanBold`, `CanItalic`, `CanTransformObject`, `CanAlign`, `CanDistribute`,
`CanGroup`, `CanUngroup`, `CanBringForward`, `CanSendBackward`,
`CanApplyFill`, `CanFontSize`, `CanApplyStroke`, `CanApplyStrokeWidth`, `SlideHidden`, `CanHideSlide`, `CanShowSlide`,
`CanCopy`, `CanPaste`, `CanMoveObject`,
`IsPlaying`, `PlayStatus`, `CanPresent`,
`CanAdvance`, `CanPlayPrevious`, `CanExitPresentation`,
`CanExportPowerPoint`, `CanImportPowerPoint` and
`HasUnknownCommitOutcome`.

`Title`, `SpeakerNotes` and `FillColor` are two-way inputs; every other
control reads a capability binding that is recomputed from `IsActionAvailable`
whenever the session notifies, so the deck-boundary controls re-evaluate after
a move, the selection-dependent controls re-evaluate after a native canvas
click through the retained `SelectionChanged` notification, and the style,
font, fill, transform and arrange controls re-evaluate as the selection
changes, all without rebuilding the surface. Align and distribute gate on two
and three selected objects, group gates on at least two ungrouped objects,
ungroup gates on a selected group object, the fill input and its button gate on
a selected shape, the font size controls gate on a selected text object and
the z-order nudge gates on any selection. While a presentation runs the
availability gate admits only `Advance`, `PlayPrevious` and
`ExitPresentation`, which freezes the authoring chrome and the native canvas
for the whole run. The playback block is a single `If` branch carrying one
`StackPanel`, so the status line and its three controls render as one
overlaying child of the toolbar column. `ExportPowerPoint` gates on an open
presentation with both the owner exporter and the browser transfer bridge
present, and `ImportPowerPoint` gates on the owner importer and the same
bridge, so an unconfigured surface disables both rather than failing after a
click; both are frozen for the duration of a running presentation along with
the rest of the authoring chrome. `DispatchAsync` reports
`InvalidArgument` for an unknown command rather than throwing.

## Local validation

- `PresentBrowser.Scene.Tests` workflow mode: 128 assertions, exit 0.
- `PresentBrowser.Scene.Tests admission` mode: 14 assertions, exit 0.
- `PresentDurable.Tests` seed: 7 assertions, verify (fresh process, same
  `HAVEN_DATA_DIR`): 8 assertions, both exit 0.
- `BrowserActionAvailability.Tests`: 26 checks, exit 0.
- `WriteBrowser.Scene.Tests` regression canaries (shared surface untouched):
  `native-input` 177 and `toolbar` 639 assertions, both exit 0.
- Native engine build/runtime validation requires Linux LibreOfficeKit
  dependencies and CMake, which are unavailable in this workspace.

## Not claimed

Browser durability/donor/full requirements are not established by these
suites. Foreign formats, full launch/authoring chrome, Actions parity,
components, notes identities/rich text, accessibility and the complete
canonical Present requirements remain required and unverified by this
focused adapter.
