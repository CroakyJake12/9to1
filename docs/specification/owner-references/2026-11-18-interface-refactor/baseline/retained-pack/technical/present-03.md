# Present — slide authoring and presentation — Present; Host route renders the intended editor (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2805 · v2 lines 14904–14904

## Present

### V2-B2806 · v2 lines 14906–14906

### UX27-TEST-082 — Host route renders the intended editor

### V2-B2807 · v2 lines 14908–14908

**Requirements:** UX27-PRESENT-001, UX27-PRESENT-002 · **Status:** NOT RUN

### V2-B2808 · v2 lines 14910–14910

**Given:** Normal integrated host and standalone candidate from the same source.

### V2-B2809 · v2 lines 14912–14914

**When:**
1. Open 9to1://present through actual host navigation.
2. Inspect runtime surface identity and header/menu/toolbar composition.

### V2-B2810 · v2 lines 14916–14919

**Then:**
- The routed editor—not an unused standalone scene—shows the intended CUI chrome.
- Menus are above exactly one persistent main editing toolbar.
- A direct component-constructor badge assertion alone cannot pass.

### V2-B2811 · v2 lines 14921–14921

### UX27-TEST-083 — Slide list and notes retain content

### V2-B2812 · v2 lines 14923–14923

**Requirements:** UX27-PRESENT-003 · **Status:** NOT RUN

### V2-B2813 · v2 lines 14925–14925

**Given:** Deck with several slides and distinct notes, including one empty note.

### V2-B2814 · v2 lines 14927–14929

**When:**
1. Select/reorder/duplicate slides and edit notes.
2. Resize navigator/notes panels; save/reopen.

### V2-B2815 · v2 lines 14931–14934

**Then:**
- Slide/Notes IDs and text remain correctly paired.
- Empty placeholder appears only for empty notes.
- Canvas remains aspect-correct and dominant with accurate thumbnails.

### V2-B2816 · v2 lines 14936–14936

### UX27-TEST-084 — Text editing versus object transforms

### V2-B2817 · v2 lines 14938–14938

**Requirements:** UX27-PRESENT-004, UX27-PRESENT-005 · **Status:** NOT RUN

### V2-B2818 · v2 lines 14940–14940

**Given:** Slide with overlapping text boxes, a shape and image.

### V2-B2819 · v2 lines 14942–14945

**When:**
1. Select object, enter text editing and drag to select text.
2. Exit editing; move/resize/rotate object at varied zoom.
3. Escape a gesture and undo a completed one.

### V2-B2820 · v2 lines 14947–14950

**Then:**
- Text selection never drags the parent inadvertently.
- Handles/cursors remain aligned and previews update during gestures.
- Cancel/undo restore correct geometry and content.

### V2-B2821 · v2 lines 14952–14952

### UX27-TEST-085 — Real insertion gallery and shared formatting

### V2-B2822 · v2 lines 14954–14954

**Requirements:** UX27-PRESENT-006, UX27-PRESENT-007 · **Status:** NOT RUN

### V2-B2823 · v2 lines 14956–14956

**Given:** Editable slide with a selected object.

### V2-B2824 · v2 lines 14958–14961

**When:**
1. Open Shapes without choosing one; then select/draw a shape.
2. Cancel another insertion.
3. Format from toolbar/inspector/context menu.

### V2-B2825 · v2 lines 14963–14966

**Then:**
- Opening the gallery inserts nothing.
- Confirmed action creates the chosen real object once.
- All formatting surfaces share state and preserve selection.

### V2-B2826 · v2 lines 14968–14968

### UX27-TEST-086 — Playback and save/reopen

### V2-B2827 · v2 lines 14970–14970

**Requirements:** UX27-PRESENT-008 · **Status:** NOT RUN

### V2-B2828 · v2 lines 14972–14972

**Given:** Deck with notes/media and a second display where available.

### V2-B2829 · v2 lines 14974–14977

**When:**
1. Start presentation/presenter mode.
2. Exit, edit, save and reopen.
3. Test unavailable display/media conditions.

### V2-B2830 · v2 lines 14979–14982

**Then:**
- Playback is the actual deck and does not overwrite authoring state.
- Notes privacy follows the canonical presenter/export rules.
- Unsupported platform/display coverage is recorded, not fabricated.

### ANNEX-B0015 · annex lines 43–43

PRESENT-DES-201 — Preserve the existing Present editor and its approved three-row shell; refine, don't restart. A working new feature must NOT replace a compliant CUI layout with older Fluent/native design. Carry its actual commands, storage and integration forward into the approved interface; don't lose old functionality to gain a better-looking shell. Keep the existing slide/object IDs, editing engine, save/reopen/history, media, table/chart support, object manipulation, playback, donor-based interchange and current source services. Where architecture needs correction, modify the narrowest correct owner.

### ANNEX-B0016 · annex lines 46–46

PRESENT-DES-202 — Row 1: rounded red Present identity, editable document title, compact accurate Saved/Saving/Unsaved/Error state, well-aligned genuinely working document-level controls. Row 2: sidebar toggle immediately LEFT of File, with conventional compact File, Edit, Insert, Slide(s), Arrange/Presentation, View and Help menu access (respect the existing command hierarchy). Row 3: ONE unified floating pill/capsule toolbar, subtly brighter/raised, compact and logically grouped, with useful undo/redo/insert/select/format actions, contextual font family/size, clear separators, icon-first buttons, accessible labels/tooltips and real small-screen overflow; never duplicate or stack a second toolbar. Don't hide daily actions solely in Properties, but use the contextual inspector for precision.

### ANNEX-B0017 · annex lines 49–49

PRESENT-DES-203 — The target image's missing left slide rail is an intentional speed shortcut in the mockup, NOT a design instruction. Present MUST show a polished left scrollable rail with accurate actual slide previews, slide number/name metadata, clear understated selected state, clean spacing, truncated long names, reorder and real context actions. Slide names are persisted metadata shown in the rail, not a strange non-removable overlay on the slide. Right-click thumbnail > Rename slide must change only Slide.Title metadata, with undo/save/reopen intact, never a genuine editable title object. The earlier two bottom Slides/Notes buttons remain removed.

### ANNEX-B0018 · annex lines 52–52

PRESENT-DES-204 — Central slide is the real editable, full, usable white slide, default 16:9 when the document is 16:9, horizontally and vertically centred inside the remaining stage, with slightly rounder frame corners. It is not a placeholder or thumbnail substitute. Fit-to-Window computes from measured free viewport after toolbar, left rail, optional right Properties inspector, expanded notes and actual window size. Recompute correctly on slide switch, presentation reopen, pane resize, narrow window and layout changes, preserving transform, hit-test accuracy, content positions and actual selection geometry. The document remains opaque and unblurred even over Professional's Mica surface.

### ANNEX-B0019 · annex lines 55–55

PRESENT-DES-205 — Keep speaker-notes semantics, data, Presenter View/export behaviour and storage contracts, but name their visible editing surface Slide Notes. When expanded it displays the selected slide's real saved notes and allows editing. When collapsed, it leaves a compact distinguishable Slide Notes bubble that reopens the panel. There are no redundant bottom Slides/Notes buttons. Notes may not be blanked by slide switching or replaced with decorative empty-state text when content exists.

### ANNEX-B0020 · annex lines 58–58

PRESENT-DES-206 — The zoom graphic in the rushed target mockup is a pasted screenshot from the PREVIOUS interface; it must be replaced by genuine CUI Professional-styled controls: minus/zoom-out icon, real slider, live percentage, plus/zoom-in icon, and icon-only Fit. Group the controls neatly bottom-right. All share the actual canvas zoom state, including fit scale; updating minimum, switching modes, resizing or changing theme must never reset the slider maximum to 100%. Preserve the configured upper zoom capability (400% where provided). Ctrl+wheel/pinch, manual zoom and Fit must behave coherently; no old screenshot-as-widget, fake percent or redundant text Fit control.

### ANNEX-B0021 · annex lines 61–61

PRESENT-DES-207 — Remove 'Select an object to format or arrange' filler from the normal workspace. Keep real contextual Properties reachable, but do not fill it with unrelated file actions or enormous default panels. Maintain consistent charcoal panel hierarchy, Slide Notes bubble, footer zoom alignment, accurate actual vector icons and content fit. Only working AI/comments/history actions may look enabled; partially implemented functions must be truthfully marked unavailable, without sacrificing existing working features.

### ANNEX-B0023 · annex lines 67–67

PRESENT-INT-201 — Maintain separate hierarchical layers for selected slide ObjectID, a text object's genuinely editable child content and transient hover/selection/handle overlay. Single-click selection, double-click or Enter text editing, body dragging when not editing, escape/cancel, move/resize/rotate, selection clearing and text caret actions MUST follow one deterministic gesture/focus state machine. Hover outlines appear BEFORE click, selection outlines are visibly distinct, side and corner resize handles remain usable, with directional resize/move/rotation/text/insert cursors and real live previews. Object dragging must not accidentally select text; text editing must not move the parent. Correct rotated/overlapping/tiny objects, z-order, pointer capture/offset and cancel/undo boundaries.

### ANNEX-B0024 · annex lines 70–70

PRESENT-INT-202 — Reliable scroll/wheel/zoom; proper menu hit targets. The pointer's hovered rail/notes/inspector/stage scrolls correctly without mysterious input swallowing. Ctrl+wheel and pinch zoom use actual viewport transforms. Menus, galleries, toolbar overflow, colour/font popovers and right-click actions are shared CUI floating controls with full-row pointer and keyboard hit targets, correct layering and focus restoration to the ACTUAL inner text/edit scene. Cut, Copy, Paste and keyboard shortcuts must work immediately after dismissal. Table/chart text input retains full strings, caret, signed numbers and editing focus across per-keystroke data refreshes. Avoid retained-tree detach/reparent and expensive full-document redraw on every keystroke. Updates must call canonical editor commands, save state and history.

### ANNEX-B0025 · annex lines 73–73

PRESENT-AC-201 — Visual check matrix. Inspect the exact running build and take unaltered screenshots at desktop ~1536/1280, tablet ~800 and narrow ~390 pixel widths, plus Properties and Slide Notes open/closed, selection, hover, context menu, shape gallery, toolbar overflow, manual zoom and Fit. Compare to the owner's target reference while correcting its intentional missing thumbnails/old pasted zoom control. Reject clipped popup shadows, malformed SVGs, missing glyphs, native Avalonia chrome, oversized gaps, duplicate toolbars, mis-centred slide, invisible controls, hard-coded colours, poor focus/contrast or fake notes.

### ANNEX-B0026 · annex lines 76–76

PRESENT-AC-202 — Functional and donor tests. Exercise real pointer/keyboard workflows: create/open/import slide deck; select/double-click-edit/drag/resize/rotate; thumbnail rename without touching authored text; notes preservation; shape insertion and formatting; table/chart typing including signed values; Cut/Paste focus; undo/redo; wheel/Ctrl+wheel; zoom 50/75/100/150/200% and configured maximum; Fit after each panel/window size change; save/close/reopen identical objects/IDs; editable PPTX export/import with style/notes fidelity; slideshow playback; and repeated Home→Present→another app→Home navigation. Include accessibility, cross-theme, shared CUI other-app and real LibreOffice Impress comparison. A successful file download or isolated ABI check is not full donor parity evidence.

### ANNEX-B0027 · annex lines 79–79

PRESENT-AC-203 — Startup and performance. The observed 5–10-second blank white 'Opening 9to1' refresh is a failure until investigated and corrected. Earliest rendered surfaces must respect selected CUI theme rather than flashing a native/Fluent white host. Measure cold vs warm startup, initial shell, actually usable Present editor, saved-document reopen, typing/caret latency, theme switch, menus, object drag, zoom and repeated navigation with exact source/build/runtime details. Profile unnecessary service startup, repeated UI markup parsing, theme resolution/resource observer loops, retained row rebuilds and browser material costs. Preserve correct restoration/data while optimising. Use existing STARTUP-UX budgets and note honest misses.

### ANNEX-B0028 · annex lines 82–82

PRESENT-AC-204 — Private cloud browser gate. The existing Cloudflare-Access-protected Present preview is the authorised browser validation surface. It must be reachable independently of the developer's PC; obscurity of the hostname is not authentication. Publish only a verified, hash-matched browser/WASM asset to its previously identified Worker using an authorised deployment route, preserve current Access policy and prior working version, and test exact served build after publication in a legitimately authenticated browser with anonymous access denied. Do not alter unrelated DNS/Workers or leak tokens/cookies. Missing cloud shell, OAuth/credentials, hosted authentication or verified asset pipeline is BLOCKED, not a made-up success.

### SCALABILITY-B0015 · scalability lines 45–45

PRESENT-SCALE-201 — VERY LARGE DECKS. Present MUST lazily create selected-slide and near-slide surfaces, virtualise slide lists/thumbnails, incrementally update slide order/section indexes, retain persistent SlideIDs and NotesIDs, and load embedded media on demand. Inserting/reordering a slide cannot require reconstructing all slide UI. Playback and export must process every applicable slide accurately via bounded batches or truthful progress. No arbitrary native slide-count or file-byte ceiling.
