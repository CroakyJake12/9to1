# Present editor redesign ÔÇö command migration

Status: the canvas regression and compact editor repair pass the bounded cloud-browser checks below. Whole-product acceptance remains open; compilation and native tests alone do not establish acceptance.
Inventory captured before changing Present.cui on 12 October 2026. The existing PresentEditor, repository CAS/recovery, import/export services, route identity and retained canvas remain authoritative.

| Existing commands / workflows | New destination |
|---|---|
| ShellHome; ReturnToStart; ResumePresentation | 9to1 navigation; File ÔåÆ Back to Present start; retained start/resume journey |
| Create; Refresh; Open (saved/recent); ToggleFiles; Close | File ÔåÆ New / Open / Refresh / Close; existing start and library |
| Save; Inspect unknown commit | Header Save/status; File ÔåÆ Save / Inspect saved state |
| ImportPowerPoint; ExportPowerPoint | File ÔåÆ Import .pptx / Export .pptx; import also remains on start |
| Undo; Redo; Copy; Paste; DeleteElement / DeleteObject | Edit menu; undo/redo toolbar; canvas keyboard |
| AddSlide; DuplicateSlide; DeleteSlide | Slide menu; Add slide toolbar |
| SlideUp / MoveSlideEarlier; SlideDown / MoveSlideLater | Slide ÔåÆ Move slide up/down; thumbnail Ctrl+Up/Down and drag reorder |
| PreviousSlide; NextSlide; SelectSlide | Thumbnails, footer navigation and Slide menu |
| ToggleHidden; HideSlide; ShowSlide | Slide ÔåÆ Hide / Show slide (same owner; aliases retained) |
| AddText; AddShape | Insert menu; stable toolbar controls |
| Bold; Italic; FontLarger; FontSmaller | Edit ÔåÆ Text formatting; contextual text toolbar and Properties |
| Grow; Shrink; RotateLeft; RotateRight; RotateObject | Arrange ÔåÆ Transform; numeric contextual Properties |
| MoveObjectLeft/Right/Up/Down | Arrange ÔåÆ Move; canvas keyboard and drag |
| BringToFront; SendToBack; BringForward; SendBackward | Arrange ÔåÆ Order; contextual Properties order menu |
| AlignLeft/Center/Top/Middle; DistributeHorizontal/Vertical | Arrange ÔåÆ Align / Distribute; contextual toolbar menu |
| Group; Ungroup | Arrange; contextual toolbar menu |
| ApplyFill; ApplyStroke; ApplyStrokeWidth | Shape Properties with actual color/width values |
| ApplyTransition | Slide Properties: typed transition selector and duration |
| AddAnimation; RemoveAnimation; AnimationSteps | Object Properties: animation selector/trigger and existing cue list |
| Present; Advance; PlayPrevious; ExitPresentation | Header Present; dedicated playback controls while playing |
| ToggleInspector; ToggleSidebar | View menu; toolbar Properties / Slides, panel close controls |
| Title; SlideTitle; SelectedText; SpeakerNotes | Header title; slide/text contextual Properties; collapsible notes |
| Pointer object selection, Shift multi-select, marquee | Retained canonical canvas, unchanged owner |
| Drag move/resize/rotate, vector-node/control-point edit | Retained canonical canvas, unchanged owner |
| Double-click/Enter text edit, caret/selection, clipboard, IME, commit/cancel | Retained canonical canvas with native focus and original text owner |
| Dirty start/resume, save-before-navigation, same-ID reopen, conflict/unknown-save refusal | Existing session lifecycle, retained and regression tested |
| Read-only/admission/busy/playback input guards | Same owner availability, reflected by new controls |
| Compact inspector and width restoration | Responsive workspace preserving scene/session identity |

All existing command names retain their dispatch implementations, including semantic aliases. The inspector no longer owns import/export, library or slideshow actions. Primary toolbar positions are stable; lower-priority groups go into overflow and never wrap.

## Gaps found by inventory

- Implemented genuine current/previous saved-version history through the existing repository boundary. Restore is undoable and preserves the current CAS base; Save creates a new revision. No unlimited timeline is implied.
- Comments/collaboration have no Present browser owner. Do not show false enabled controls. They remain a broader specification gap.
- The current browser editor exposes text and shapes, but no image/media picker. Existing model APIs alone are not working browser media insertion; do not add placeholder toolbar items.
- Implemented measured Fit and 50ÔÇô250% zoom on the actual retained canvas. Fit fills the bounded editing stage, with centred aspect-preserving paper and canonical document drawing scaled as one unit. Resizing/panels/notes update the measured space.
- Implemented owner bindings for contextual position/size/rotation, opacity, typography, layout and background. Position is relative to slide centre with Y positive upward; stored normalized model coordinates remain compatible.

## Shared architecture

Canonical CUI editor definitions own compact shell rows, toolbar groups, centred icon hit areas, separators and property fields. Shared runtime tooltip/shortcut support and a semantic panel-resize input supply real keyboard/pointer behavior. Present supplies commands, bindings, and specialised document canvas only. No other editor is redesigned in this change. Native menu/select/range providers supply browser semantics. Choice rows own complete pointer hit areas, and equal collection notifications preserve native containers and focus. Thumbnail previews call the same canonical slide drawing path as the editor; they do not draw generic substitute shapes.

## Acceptance

| Criterion | Evidence and boundary |
|---|---|
| Usability | Actual pointer/keyboard editing, notes, menus, Properties, overflow, resizing and zoom/Fit exercised in Chromium. |
| Product fit | Three-row document/menu/one-toolbar arrangement retained; actual editable slide fills and centres in its stage. |
| Functionality | Canvas/text/shape editing, slide lifecycle, history/undo, save/reopen and playback pass bounded runs. Existing PPTX interchange remains lossy; full functionality acceptance is open. |
| Integration | Existing editor, saved-data/CAS identity, retained renderer, service contracts and Home route remain authoritative. |
| Authenticity | Main canvas and thumbnails draw actual owner content; browser checks use real pointer/keyboard input, not command proxies. |
| User fit | The invisible-slide P0 is repaired without replacing the base or reverting the header. |
| Refinement | Desktop/compact screenshots inspected, notes wrap, controls stay in one toolbar and five current theme choices retain the slide. All four brightness appearances and native packaged hosts remain unverified. |

The real PPTX download/import test exposes the existing documented subset: titles/plain text and geometry survive, while shapes are represented as text and notes/styling are lost in the imported copy. The original saved deck stays intact. Do not describe that as a lossless round-trip or interoperability acceptance. Comments/collaboration, browser media insertion, complete layout fidelity, mandatory real donor execution and whole-app gates remain open. No donor, packaged or whole-app release acceptance is claimed.

## Canvas regression and evidence boundary

The retained slide root had an automatic height and measured its empty native content at 48 px. The editor now gives that retained root 100% of the constrained stage. Fit uses the available width and height after the header, thumbnail rail, inspector and notes have taken their actual layout space. A separate uniform content transform scales text, shapes and strokes together; selection geometry uses the same paper bounds. A stationary window pointer cannot author a drag when a layout change moves the stage beneath it. Physical drag and centre-origin/Y-up Properties edits are exercised independently.

Final native runs cover the complete 312-assertion owner workflow, 177 viewport/semantics assertions, 57 direct manipulation/paragraph/colour assertions and 43 durable-history assertions. Shared CUI Runtime 224 tests and native browser accessibility 34+4 checks pass within their source boundaries. These are source/native evidence only.

Actual cloud Chromium publish 20 exercises real text colour/alignment, title/geometry edits, slide add/duplicate/reorder/delete/undo, notes, resizable panels, 75%/125% zoom and Fit (implemented range 50ÔÇô250%), playback, saved history and file transfer. Eleven measured screenshot frames have full 16:9 paper inside the actual stage with less than two pixels of centre error. Three corrected Home ÔåÆ Picture ÔåÆ Home cycles complete without JS/managed exceptions. An earlier harness batch used the wrong Home label and is recorded as a failed selector batch, not passing navigation evidence.

Publish 21 changes only speaker-notes wrapping after publish 20. Its actual 390├ù844, 900├ù650 and 1280├ù900 screenshots, Properties/notes toggles, five theme choices, start/resume and reload/reopen preserve the full owner slide. Ten measured frames pass aspect/centering checks. Re-entry through Home resumes the open workspace; four unnecessary recent-file waits in the appearance harness timed out, while the captured title and actual canvas confirm the correct document. The reload harness also briefly expected Home, while the application correctly restored the Present start route; it then opened the actual recent document. These five wait/click pairs are recorded as harness errors, not application errors or additional workflow passes. No JS/managed exception occurred in publish 20 or 21. Source/artifact hashes and private evidence are retained in the cloud handoff.

Text uses the existing canonical paragraph shaper for wrapping, horizontal/vertical alignment, caret hit testing and selection. Authored point sizes convert to 96-dpi document pixels once and remain independent of text-box height; one slide transform handles zoom. Bounded immutable paragraph caching reuses measurements across paints. Short hex, alpha hex and named colours use the same literal parser as the editor; invalid imported colours fall back to a real document colour instead of looking up a literal as a semantic resource. Browser publish 19 exposed this colour-entry error and remains failed diagnostic evidence.

Browser theme-change completion (choice click to current theme plus two animation frames) measured 196ÔÇô249 ms for five warmed Settings choices in publish 20, versus earlier multi-second observations. Publish 21 measured 281ÔÇô511 ms for four choices while navigating between Settings and the editor. These software-rendered cloud measurements are bounded observations, not approved whole-app performance budgets or cold-start acceptance.
