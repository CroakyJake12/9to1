# CUI, themes and shared interaction system — Context-menu coverage; Professional app theme (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0037 · v2 lines 105–105

CUI-EDITOR-102 — Context-menu coverage. Right-click on a selected shape/text/image or other object MUST open a context-specific menu with applicable Cut, Copy, Paste, Duplicate, Delete, Arrange/z-order, Align, Group and Format operations; text-editing right-click MUST offer text-appropriate commands; slide thumbnails MUST offer new, duplicate, delete, reorder/layout and related slide operations; empty canvas areas MUST offer relevant Paste/Insert/slide options. Each menu targets the intended ObjectID/SlideID and must position near its anchor without clipping; keyboard invocation/navigation, focus, escape/outside dismissal, checked/disabled/submenu states and correct z-order are mandatory. The same canonical commands must power context menus, toolbar, shortcuts and inspector.

### V2-B0038 · v2 lines 108–108

CUI-EDITOR-103 — Professional app theme. Use the supplied dark Unsloth Library reference for the overall restrained, modern, spacious professional aesthetic: cohesive dark palette, calm hierarchy, deliberately aligned title/status/header actions, subtle surfaces, softer appropriate rounding, consistent typography and clean panel separation. Do not copy external branding. The selected/hovered states must remain accessible and sufficiently visible. Use the 9to1 CUI theme's declared tokens; do not introduce one-off native-control overrides to imitate screenshots.

### V2-B0039 · v2 lines 111–111

CUI-EDITOR-104 — Icons must be reliable. Icons SHOULD be coherent, moderately rounded, optically centred, consistent in stroke/weight and recognisable, optionally with restrained semantic colour accents. Prefer proven audited icon assets to improvised generated SVG paths. Validate custom icons for viewBox/path integrity, clipping, scaling, stroke, alignment and normal/dark/focus/disabled presentation at ACTUAL toolbar pixel size. A syntactically valid SVG that looks broken is an acceptance failure.

### V2-B0040 · v2 lines 114–114

CUI-EDITOR-105 — Shared abstractions not forced layouts. Reusable CUI EditorShell/EditorToolbar, grouped commands, contextual inspector, menu/popover, insertion catalogue, selection-adorners and viewport contracts SHOULD be extended/shared where justified, with clear source ownership and typed interfaces. This sets shared standards for Write, Present, Data, Boards, Canvas and relevant apps, but MUST NOT force a Present three-row header or object-selection model onto applications whose category workflows require other layouts. Respect existing authoritative CUI ten-primitive language and registered Object Type integrations. Avalonia MAY supply underlying rendering, input, windowing and specialised engine integration behind CUI; it MUST NOT be the default public styling/control vocabulary or override CUI states. Diagnose actual code and renderer boundaries rather than inferring native leakage solely from screenshots.

### V2-B0243 · v2 lines 1168–1168

## 1. Shared visual, input and state contracts

### V2-B0244 · v2 lines 1170–1170

### UX27-SHARED-001 — Complete command contract

### V2-B0245 · v2 lines 1172–1172

**B/D.** Every visible command MUST have a stable command identifier; a semantic label; a validated icon where useful; an accessible name; keyboard/touch activation; its target object IDs; enabled/disabled/busy rules; permission and revision checks; a specified result; and an error/undo policy. The toolbar, menu, shortcut, contextual popup, AI action and automation API must invoke the same domain action instead of implementing separate mutations. A disabled command must expose a concise reason without requiring it to run. A command is not implemented because a button, handler name or empty task exists.

### V2-B0246 · v2 lines 1174–1174

Verification MUST activate representative commands from more than one surface, compare resulting canonical state, undo, save and reopen. Toolbar state must track external/API edits as well as local clicks.

### V2-B0247 · v2 lines 1176–1176

### UX27-SHARED-002 — Control-state matrix

### V2-B0248 · v2 lines 1178–1178

**B/D.** Implement distinct normal, hover, pressed, keyboard-focus, selected, checked, mixed, disabled, busy, error and unavailable states where meaningful. Hover must not masquerade as selection. Pointer-down shows pressed feedback but does not commit a consequential action; ordinary buttons commit once on valid release/activation. Moving out while pressed cancels activation unless the control’s gesture contract says otherwise. Space/Enter activation must not also cause a second pointer action. Focus remains visible independently of hover and selected fill.

### V2-B0249 · v2 lines 1180–1180

Busy state preserves the control’s size and accessible label, prevents duplicate submission where necessary, and exposes cancellation only when actually supported. Never replace content with a spinner so early that the user loses the target or cannot determine what is pending.

### V2-B0250 · v2 lines 1182–1182

### UX27-SHARED-003 — Geometry and scale defaults

### V2-B0251 · v2 lines 1184–1184

**D.** Use logical display units through CUI’s registered layout system, not hardcoded physical-screen coordinates. Where no more specific approved design exists, use a 4-unit spacing scale; compact row heights of 32 units; ordinary toolbar hit areas of 32×32; 16–20-unit glyphs; and 44×44 effective hit targets in a touch/coarse-pointer layout. Allow density and accessibility settings to increase sizes. A glyph’s visual bounds and its hit target are separate.

### V2-B0252 · v2 lines 1186–1186

Test at 100%, 125%, 150% and 200% display scaling, at representative narrow and wide windows, and when moved between monitors with different scale factors. Logical dimensions in this document must be mapped through the canonical CUI property system; they do not introduce a second public layout language.

### V2-B0253 · v2 lines 1188–1188

### UX27-SHARED-004 — Icon identity and quality

### V2-B0254 · v2 lines 1190–1190

**O/B/D.** Application icons use the owner’s rounded, filled, colourful, distinct design direction with light/dark variants. Boards uses the purple window-like identity; Data retains its teal identity; Spaces uses the agreed speech-bubble/Space-type composition when the actual approved asset is available. Do not substitute an unrelated donor logo, missing-glyph box, Unicode symbol or unreviewed generated path and call it the approved icon.

### V2-B0255 · v2 lines 1192–1192

Command icons belong to one coherent family, with optical alignment and appropriate stroke/fill weight. Pair unfamiliar or destructive symbols with text in their menus. Test actual assets at 16, 20, 24 and 32 logical units, under light/dark, hover, selected and disabled states. Decorative icons are excluded from screen-reader naming; icon-only controls receive meaningful labels.

### V2-B0256 · v2 lines 1194–1194

### UX27-SHARED-005 — Theme changes alter appearance, not topology

### V2-B0257 · v2 lines 1196–1196

**O/B.** Professional/Glow and light/dark choices resolve through CUI tokens. Theme switching may change palette, typography tokens, elevation and permitted surface treatment; it MUST NOT relocate the Boards footer Add action to the header, flatten its page tree, replace page controls or change document layout semantics. Keep active page, selection, scroll position and open document identity stable. Theme application is one coordinated update, not a frame with old text colours on new backgrounds.

### V2-B0258 · v2 lines 1198–1198

User-authored document colours/fonts and embedded artifact styling remain content, not app-theme values. Professional defaults follow the existing reference typography, including Montserrat where prescribed; code retains an appropriate monospace font. Do not manufacture one universal pill-shaped interface or overwrite explicitly different app layouts.

### V2-B0259 · v2 lines 1200–1200

### UX27-SHARED-006 — Contrast and readability

### V2-B0260 · v2 lines 1202–1202

**B/D/R.** Test real computed foreground/background pairs, not merely token names. Normal informative text targets at least 4.5:1 contrast; large text at least 3:1; essential control boundaries/focus indicators must remain distinguishable. A pale sidebar caption or dark title must not disappear after a theme switch. Disabled appearance must still be identifiable as a control, with an accessible reason; reduced opacity must not be applied to an entire container containing active children.

### V2-B0261 · v2 lines 1204–1204

Selected tabs default to the owner’s light grey/white surface with black text unless an explicit accent override applies. The override must retain readable contrast and selection distinction. Dark themes are not permission for dark-on-dark labels. These are product requirements informed by W3C guidance, not a claim of complete WCAG certification.

### V2-B0262 · v2 lines 1206–1206

### UX27-SHARED-007 — Motion token table

### V2-B0263 · v2 lines 1208–1208

**D.** Define shared named motion tokens instead of ad hoc per-control animations: `Immediate=0 ms`, `Feedback=100 ms`, `Reveal=140 ms`, `Reflow=180 ms`, `Panel=220 ms`. These are authored 9to1 defaults, not measured Notion/AppFlowy timings. Use restrained ease-out for entering elements and ease-in for dismissal; interactive drag and ink track live input without waiting for an easing cycle. A new action can interrupt a transition without dropping input or replaying the old action.

### V2-B0264 · v2 lines 1210–1210

Hover/focus colour transitions use Feedback; popovers use Reveal with at most a small opacity/4-unit translation; tree expansion and post-drop settling use Reflow; sidebar collapse uses Panel. Do not animate text caret positioning, text entry, file-save truth, permission grants or block identity. Motion must not create extra blank document space or move the hit target away from its rendered control.

### V2-B0265 · v2 lines 1212–1212

### UX27-SHARED-008 — Reduced motion and truthful progress

### V2-B0266 · v2 lines 1214–1214

**B/D/R.** Honour operating-system reduced-motion preference and a persistent app override. Remove nonessential translations, zooms, parallax and spring/bounce effects; retain immediate state changes and brief fades only where acceptable to the chosen setting. A loading animation denotes actual pending work, never fabricated progress. Determinate progress requires a real denominator; otherwise use an honest indeterminate state with the operation name and cancellation/retry where supported.

### V2-B0267 · v2 lines 1216–1216

Background activity must not continuously animate every hidden tab. Pause unnecessary off-screen presentation work while preserving actual background tasks. Motion settings do not disable authored animation playback in a media/presentation preview; that preview has its own explicit controls.

### V2-B0268 · v2 lines 1218–1218

### UX27-SHARED-009 — Popover lifetime and outside dismissal

### V2-B0269 · v2 lines 1220–1220

**O/B/D.** A popup has an owner surface, anchor, target identity, focus-return target and lifetime. Open only one unrelated popup chain per window; submenus remain a chain. Outside pointer-down, Escape, activating a different tab/app, destroying the owner, or losing the target closes applicable transient popups. A profile menu must not remain over a different app after navigation. Updating theme or layout repositions the existing popup correctly or dismisses it; it must never become an orphaned overlay.

### V2-B0270 · v2 lines 1222–1222

The outside-dismiss click must not accidentally activate a destructive background action. Escape closes the deepest submenu first, then its parent; it cancels a staged operation rather than silently committing. After dismissal return focus to the originating control or the nearest valid owner if that control no longer exists. Permission dialogs follow their explicit security contract and are not silently dismissed as ordinary menus.

### V2-B0271 · v2 lines 1224–1224

### UX27-SHARED-010 — Popup placement, keyboard and scrolling

### V2-B0272 · v2 lines 1226–1226

**B/D/R.** Anchor menus to the invoking control or pointer, with an 8-unit viewport safety margin by default. Flip above/below or left/right when necessary, then clamp without detaching the perceived anchor. Long menus have bounded internal scrolling; the document must not scroll underneath while the menu consumes wheel input. Arrow keys move between eligible items, Enter/Space activate, Escape dismisses, and type-ahead works where appropriate. A submenu opens on an intentional hover delay or keyboard action, not every incidental pointer crossing.

### V2-B0273 · v2 lines 1228–1228

Checked and mixed states are conveyed visually and accessibly. Shortcut labels align in a separate column. Disabled items can expose reasons without executing. Use normal native accessibility semantics on native platforms and equivalent ARIA semantics on web, rather than styling arbitrary text to resemble a menu.

### V2-B0274 · v2 lines 1230–1230

### UX27-SHARED-011 — Focus and selection are preserved through tools

### V2-B0275 · v2 lines 1232–1232

**B/D.** Text selection belongs to the editor model, not merely a transient native control focus. Opening the formatting island, colour picker or link popup preserves the exact selected range and direction. Apply affects that range, not the whole page or a newly focused empty field. Closing the tool returns the caret/selection to the document and reveals it without scrolling to the page top. A selection changed by an external revision is revalidated before applying, with a clear conflict result if the target is stale.

### V2-B0276 · v2 lines 1234–1234

Focus order follows visible hierarchy. Do not install global key handlers that consume ordinary text, IME composition or table navigation. Every popover, drawer and modal has an explicit focus policy, and no invisible control remains in the tab order after its surface closes.

### V2-B0277 · v2 lines 1236–1236

### UX27-SHARED-012 — Scroll ownership and coordinate transforms

### V2-B0278 · v2 lines 1238–1238

**O/B.** Wheel, touchpad, touch and scrollbar gestures target the hovered/active scroll container according to the platform convention. Apply OS natural-scroll direction once, never negate it again in an embedded engine. Preserve high-resolution fractional deltas rather than truncating small trackpad movement to zero. Boundary chaining is deliberate: a short menu scrolls itself; a page scrolls when the menu is closed; a canvas zoom gesture must not scroll the parent unexpectedly.

### V2-B0279 · v2 lines 1240–1240

Persisted content uses domain coordinates. Screen/DPI/viewport/scroll/zoom transforms are composed once and inverted consistently for hit testing. Use the same transform for content, selection handles, drag previews and ink. Test zoom, monitor-scale changes and nested surfaces rather than adding unexplained offset constants.

### V2-B0280 · v2 lines 1242–1242

### UX27-SHARED-013 — Undo transaction boundaries

### V2-B0281 · v2 lines 1244–1244

**B/D.** One split, block move, page move, completed pen stroke, drag-resize, format action, insertion or accepted AI patch is one logical undoable transaction unless a richer existing domain contract specifies otherwise. A drag preview is not hundreds of separate undo entries. Text typing groups coherently, but crossing into a distinct action closes the group. Undo restores identities, relationships, text marks, content and selection as far as the domain permits; Redo reapplies the intended operation without duplicating a node.

### V2-B0282 · v2 lines 1246–1246

External side effects such as sending mail, publishing a site or deleting a remote object are not automatically reversible document edits. Their UI must describe the actual reversal/cancellation boundary. Never display an Undo control that merely hides a toast while leaving an unintended external action unexplained.

### V2-B0283 · v2 lines 1248–1248

### UX27-SHARED-014 — Error scope and recovery

### V2-B0284 · v2 lines 1250–1250

**O/B.** An unavailable browser engine affects web tabs, not local Board creation. An unavailable model affects AI generation, not ordinary editing. An unavailable cloud account affects that account’s resources, not local files. Present errors at the narrowest owning surface with a named failed operation, preserved input, and genuine retry/recovery options. A permanent window-wide generic “action could not complete” banner is not adequate diagnosis for every later action.

### V2-B0285 · v2 lines 1252–1252

Retry must reuse or reconcile the operation identity where duplicate effects are possible. Do not silently treat a timeout as failure to execute. Closing an error notification does not clear a real permission hold, erase failed-save state or falsely mark an artifact saved.

### V2-B0286 · v2 lines 1254–1254

### UX27-SHARED-015 — Save-state vocabulary

### V2-B0287 · v2 lines 1256–1256

**B/D.** Distinguish at least `New local draft`, `Changes pending`, `Saving locally`, `Saved locally`, `Sync pending`, `Synced`, `Save failed`, `Read-only` and `Conflict` where applicable. Display the destination and last durable revision in details without crowding the main workspace. A cloud upload pending after a successful local commit is not “not saved locally”; a failed local write is never “Saved”. No hardcoded “Saved · Just Now” label.

### V2-B0288 · v2 lines 1258–1258

When closing a document with unsaved durable state, preserve a recovery draft and offer appropriate Retry/Save As/Discard/Cancel choices. Discard affects only the explicitly selected unsaved change set and must not delete unrelated saved files or owned embedded artifacts.

### V2-B0289 · v2 lines 1260–1260

### UX27-SHARED-016 — Rendering must reflect actual route and build

### V2-B0290 · v2 lines 1262–1262

**O/B.** UI changes count only when the route the user opens instantiates the changed surface in the identified installed build. Record application route, resolved registered surface, source commit, package/build identity and canonical document target. A standalone scene test or directly constructed page does not prove the host route activates it. Launch through the real user entry point and verify navigation, content, focus and persistence on that path.

### V2-B0291 · v2 lines 1264–1264

Prevent multiple obsolete implementations from silently providing different behaviour under the same public app name. Shared services remain canonical; standalone and hosted entry points use the same domain model and supported surface implementation. Diagnostics may expose route/build identity without becoming ordinary document content.

### V2-B0292 · v2 lines 1266–1266

### UX27-SHARED-017 — Loading, empty and offline states
