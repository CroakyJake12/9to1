# CUI, themes and shared interaction system — Exclusive markup and interface ownership; Conformance gates (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2268 · v2 lines 13352–13352

CUI-001 — Exclusive markup and interface ownership. All 9to1 UI markup is canonical CUI. AXAML/XAML and Avalonia native markup MUST NOT parse as CUI, including aliases or silent conversion. CUI owns ALL visible UI: navigation, menus, dialogs, toolbars, context menus, overlays, notifications, settings, controls and specialised-surface UI. Avalonia is permitted only as an internal cross-platform rendering/windowing/input/accessibility infrastructure layer, never a public design language or uncontrolled native-themed interface. Specialised donor engines may compute, render content or perform domain operations behind typed CUI adapters; they may not surface independently styled UI. The canonical versioned CUI component/property registry defines allowed syntax; unknown components/properties fail validation. Native-looking public components and AXAML leakage fail acceptance regardless of functional tests.

### V2-B2269 · v2 lines 13355–13355

CUI-002 — Conformance gates. Build/CI MUST parse all public UI definitions using the CUI grammar and registry, reject native markup vocabulary and attached-property syntax, inspect runtime UI boundaries, and smoke-test real rendered packaged apps. Valid registered CUI properties must not be rejected merely because their names resemble internal implementation details. Any specialised engine adapter must document lifecycle, content/selection/action contracts, focus/input, accessibility, errors, theme, state and recovery. CUI controls every visible UI affordance, including engine-related context controls. Internal platform primitives are permitted only when visually and behaviourally controlled by CUI.

### V2-B2639 · v2 lines 14406–14406

## Shared UI

### V2-B2640 · v2 lines 14408–14408

### UX27-TEST-050 — Consistent icons and control states

### V2-B2641 · v2 lines 14410–14410

**Requirements:** UX27-SHARED-001, UX27-SHARED-002, UX27-SHARED-004 · **Status:** NOT RUN

### V2-B2642 · v2 lines 14412–14412

**Given:** Representative toolbar, tree, dialog and disabled/busy commands.

### V2-B2643 · v2 lines 14414–14416

**When:**
1. Inspect normal, hover, press, focus, selected, mixed, disabled and busy states at actual display sizes.
2. Activate with pointer and keyboard.

### V2-B2644 · v2 lines 14418–14421

**Then:**
- Icons render with correct bounds/weight and semantic names.
- States are distinguishable; one valid activation commits once.
- Unavailable actions expose reasons without impersonating working controls.

### V2-B2645 · v2 lines 14423–14423

### UX27-TEST-051 — Dark/light contrast without topology change

### V2-B2646 · v2 lines 14425–14425

**Requirements:** UX27-SHARED-005, UX27-SHARED-006 · **Status:** NOT RUN

### V2-B2647 · v2 lines 14427–14427

**Given:** Boards, Library and editor with selected content and open navigation.

### V2-B2648 · v2 lines 14429–14431

**When:**
1. Switch all supported theme/brightness combinations.
2. Inspect ordinary text, icons, selection and focus against computed surfaces.

### V2-B2649 · v2 lines 14433–14436

**Then:**
- Readable labels/title do not become dark-on-dark.
- Layout, selection and content remain stable.
- Record measured contrast where applicable; no blanket conformance claim from one screenshot.

### V2-B2650 · v2 lines 14438–14438

### UX27-TEST-052 — Reduced motion preserves the result

### V2-B2651 · v2 lines 14440–14440

**Requirements:** UX27-SHARED-007, UX27-SHARED-008 · **Status:** NOT RUN

### V2-B2652 · v2 lines 14442–14442

**Given:** Menus, sidebars, tree expansion and drag reflow under ordinary motion.

### V2-B2653 · v2 lines 14444–14447

**When:**
1. Measure displayed transitions.
2. Enable reduced motion and repeat.
3. Trigger an operation that completes immediately.

### V2-B2654 · v2 lines 14449–14452

**Then:**
- Declared timing defaults apply where no owner-specific token overrides them.
- Nonessential movement is removed/reduced without missing final state or focus.
- No artificial minimum spinner delay or fake progress is introduced.

### V2-B2655 · v2 lines 14454–14454

### UX27-TEST-053 — Outside click Escape and navigation dismiss popups

### V2-B2656 · v2 lines 14456–14456

**Requirements:** UX27-SHARED-009, UX27-HOST-007 · **Status:** NOT RUN

### V2-B2657 · v2 lines 14458–14458

**Given:** Open account, block, colour and nested menu variants.

### V2-B2658 · v2 lines 14460–14462

**When:**
1. Click outside; press Escape; switch tab/route; close the target.
2. Repeat with a child menu.

### V2-B2659 · v2 lines 14464–14467

**Then:**
- Correct overlay closes and focus returns sensibly.
- Child handling does not prematurely close the parent when interacting inside it.
- No account popup remains orphaned across unrelated routes.

### V2-B2660 · v2 lines 14469–14469

### UX27-TEST-054 — Popup near viewport edges

### V2-B2661 · v2 lines 14471–14471

**Requirements:** UX27-SHARED-010 · **Status:** NOT RUN

### V2-B2662 · v2 lines 14473–14473

**Given:** Long menu near bottom/right of a small scaled window.

### V2-B2663 · v2 lines 14475–14477

**When:**
1. Open, keyboard-navigate and scroll the menu.
2. Resize while it is open.

### V2-B2664 · v2 lines 14479–14482

**Then:**
- Popup flips/constrains within the viewport, with controlled internal scrolling.
- Focused items remain reachable and visible.
- Page content does not unexpectedly consume menu scroll.

### V2-B2665 · v2 lines 14484–14484

### UX27-TEST-055 — Scaling touch targets and overflow

### V2-B2666 · v2 lines 14486–14486

**Requirements:** UX27-SHARED-003, UX27-SHARED-018, UX27-SHARED-019 · **Status:** NOT RUN

### V2-B2667 · v2 lines 14488–14488

**Given:** Dense toolbar and nested tree at multiple scales and coarse-pointer mode.

### V2-B2668 · v2 lines 14490–14492

**When:**
1. Resize from wide to narrow and inspect icon hit areas.
2. Invoke overflow actions with keyboard/touch.

### V2-B2669 · v2 lines 14494–14497

**Then:**
- Commands move to planned overflow rather than wrapping uncontrolled rows or clipping.
- Effective touch targets are usable.
- Accessible labels and focus order remain coherent.

### V2-B2670 · v2 lines 14499–14499

### UX27-TEST-056 — Scoped error and truthful retry

### V2-B2671 · v2 lines 14501–14501

**Requirements:** UX27-SHARED-014, UX27-SHARED-017 · **Status:** NOT RUN

### V2-B2672 · v2 lines 14503–14503

**Given:** A local editor with an optional unavailable provider.

### V2-B2673 · v2 lines 14505–14507

**When:**
1. Trigger the provider action, then continue local editing.
2. Retry with changed state.

### V2-B2674 · v2 lines 14509–14512

**Then:**
- Error identifies affected action/cause instead of repeating a global generic banner.
- Unrelated local editing remains usable.
- Retry revalidates state and cannot duplicate an uncertain external effect.

### V2-B2675 · v2 lines 14514–14514

### UX27-TEST-057 — Undo through menus and gesture previews

### V2-B2676 · v2 lines 14516–14516

**Requirements:** UX27-SHARED-011, UX27-SHARED-013 · **Status:** NOT RUN

### V2-B2677 · v2 lines 14518–14518

**Given:** Document with selected text and selected object in separate runs.

### V2-B2678 · v2 lines 14520–14522

**When:**
1. Apply a menu formatting action; Undo.
2. Perform a long move with many previews; Undo.

### V2-B2679 · v2 lines 14524–14527

**Then:**
- Tool interaction retains intended target.
- One conceptual change yields one logical undo, independent of preview/sample count.
- Undo updates all views of canonical state.

### ANNEX-B0001 · annex lines 1–1

﻿9to1 Development Specification v2 — Shared CUI Professional, Present Design, Interaction & Theme Preferences

### ANNEX-B0002 · annex lines 4–4

NORMATIVE OWNER-APPROVED ANNEX • 12 October 2026. This annex is part of the active 9to1 V2 specification and is intended to be referenced from the original V2 document. It incorporates the owner's Present reference image and requirements confirmed through Astra and this discussion. It supplements the existing October 12 PRESENT-UX-101–106, PRESENT-INT-101–107, CUI-EDITOR-101–105, STARTUP-UX-101–103, PRESENT-AC-101–104, CUI-THEME-001–004, APP-AC/ACCEPT, DESIGN-001 and donor-parity rules. Its specific newer compatible instructions prevail within their named scope; the four-part product delivery plan and all unrelated prior requirements remain.

### ANNEX-B0003 · annex lines 7–7

A. CUI is the sole user-interface design authority

### ANNEX-B0004 · annex lines 10–10

CUI-PRO-201 — Avalonia and Skia are retained as backend technology for rendering, input, layout, text shaping, windowing, composition and accessibility. They are NOT allowed to supply a visible FluentTheme, SimpleTheme, default Avalonia/AXAML template, selector/resource precedence, inherited popup chrome or donor styling for first-party CUI surfaces. All active native and browser CUI-hosted applications, including Present, MUST use CUI-owned typed templates, materials, semantic tokens, states and lifecycle. Audit the actual application startup, resource dictionaries, templated subclasses, menus, popups, tooltips, sliders, scrollbars, numeric inputs, accessibility and specialised canvases. Reject missing CUI templates rather than falling back to native. Legacy native Present entrypoints still using Fluent must be migrated to compliant CUI appearance without losing existing behaviour, or explicitly quarantined as incomplete; deleting Fluent imports while breaking control behaviour is not a fix.

### ANNEX-B0005 · annex lines 13–13

CUI-PRO-202 — Professional reference. The owner's target image specifies a calm, neutral charcoal/superdark editor, darker inset stage, subtly brighter and softly elevated floating tools, white document title, muted grey secondary text, Montserrat throughout ordinary UI, disciplined whitespace, clear logical grouping, restrained outlines, slightly rounded inputs and generously rounded menus/floating surfaces. Shared icons should be coherent vector assets with consistent weight, precise alignment and honest hit areas. Hover, focus, pressed, selected, disabled and validation states must be visible, consistent and accessible. Avoid Windows-Fluent blue/grey tint, native-style widgets, mismatched fonts, uneven padding, accidental wrapping or controls made to look professional only through application-local colour patches. These principles belong in shared Professional tokens/templates, not separate hard-coded per-app workarounds.

### ANNEX-B0006 · annex lines 16–16

CUI-PRO-203 — Mica/backdrop material is exclusive to Professional for now. Apply subtle Gaussian/Mica-style blur and material finish only to the underlying workspace/backdrop and compatible chrome immediately above it. The authored slide itself and every document object, image, foreground text, PDF/exported artifact and accessibility surface must remain crisp and retain document-authored colours. Present's slide corners are slightly softer; its floating toolbar is brighter than the background. Honour high-contrast, transparency and reduced-motion options with clear opaque fallback. Browser/WASM performance and crashes are hard gates: do not create thousands of tiny grain canvases, trigger heavy repeated filters or freeze on document opening; cache/retain drawing graphs and provide safe feature detection.

### ANNEX-B0007 · annex lines 19–19

CUI-PRO-204 — Native-control-leakage regression checks. Prove at runtime and source boundary that Fluent/Simple cannot win against CUI for active UI, including subclassed controls and overlay/popups. Whole menu rows must be clickable; shadows cannot clip; unsupported symbols must not appear as square glyphs. Changing themes while a text field is focused must preserve caret, selection, typed content and ongoing operation state. Scope the current theme correctly to canvas chrome and thumbnails, while never altering user-authored document content. Test light, dark and high-contrast variants plus at least one representative other app for shared control changes.

### ANNEX-B0008 · annex lines 22–22

B. Themes, account-wide preferences and app-specific accents

### ANNEX-B0009 · annex lines 25–25

CUI-PREF-201 — Current Home Appearance/CUI built-in choices are Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, plus Create Theme. This active picker supersedes earlier lists containing only five built-ins. Preserve compatibility with previously saved theme enum identities and unsupported historical choices, including Cinematic, through explicit migration or truthful legacy handling; do not corrupt saved account preferences. Themes are whole visual systems, not merely colour palettes, and include inherited templates, typography, geometry, materials, focus and motion. Keep user_preference, scoped nested themes and typed CUI property precedence.

### ANNEX-B0010 · annex lines 28–28

CUI-PREF-202 — Theme selection, custom-theme definitions, accent override modes and colours are account-wide when authenticated. Home's canonical revisioned, permission-checked settings owner is the source of truth, available through typed get/set/subscribe. A device holds an account-specific offline cache only; it must not become a competing preference source. Check credentials/session scope, cross-account isolation, concurrent-save conflict handling, restart, device-to-device propagation, two signed-in sessions, offline fallback and sign-out. Do not erase unrelated theme fields when changing one selection. Do not present an unconfigured preview OAuth integration or local-only mock account store as production-ready sync.

### ANNEX-B0011 · annex lines 31–31

CUI-PREF-203 — Override Accent Colours? modes: None [default], All Apps, Individual Apps. Under None, preserve original app accents: Write blue; Present red; Data green; Boards ultraviolet; Picture pink-red; Wave cyan; Motion orchid. Under All Apps, Home directly exposes one selected accent which is transformed according to the chosen theme's treatment (e.g. a glowing gradient in Glow) consistently across all applicable apps. Under Individual Apps, expose a per-app colour selector inside that app; all choices still persist account-wide through Home. Resolve correct app identity in browser/native routes rather than always using Home's colours or a fixed purple under Professional. Respect other themes and accessibility settings.

### ANNEX-B0012 · annex lines 34–34

CUI-PREF-204 — Create Theme must offer Theme Name; independently authored Primary, Secondary and Tertiary Accent Type: Solid Colour, Gradient or Pattern. For solid expose a genuine colour chooser; for gradient expose a real gradient creator with editable colour stops and supported types including linear and radial; for pattern expose a real pattern builder. Expose per-accent authored colours, transparency and Gaussian background blur as supported by CUI. Include Main Font, Primary Button Font, Secondary Button Font, Tertiary Button Font, Heading Font, Body Font, Code Font — each with Family, Weight and Size. Include Button Roundness in the inclusive 0–100% range. Actual preview, validation, cancel-without-save, persistence, reload, inheritance and safe invalid-property fallback are required. Hover Effects and Custom Cursors remain explicitly LATER enhancements, not mandatory completed editor options now. Unknown material types must surface typed unavailability rather than simulated success.

### ANNEX-B0013 · annex lines 37–37

CUI-PREF-205 — On a custom theme, an app accent override recolours only the PRIMARY authored accent, preserving its original gradient topology/stop positions, pattern geometry, transparency and Gaussian blur. Secondary and Tertiary accents retain their authored colours. Verify real rendering and native/browser changes, per-app identity, local offline resolution and account synchronisation. Theme changes never recolour saved slide/document content.

### ANNEX-B0014 · annex lines 40–40

C. Present visual reference and workspace composition

### ANNEX-B0022 · annex lines 64–64

D. Direct manipulation, editor responsiveness and acceptance

### ANNEX-B0029 · annex lines 85–85

APP-VIS-201 — Shared nonregression. Integrating new features MUST preserve the strongest compatible existing functionality AND currently specified CUI layout, design, data identities and interactions; donor features are migrated into compliant controls rather than importing an obsolete UI. Shared theme changes need representative regression in other apps and built-in/custom themes. Independent acceptance of real packaged/deployed workflows is mandatory under the existing seven APP-AC gates: Usability, Product fit, Functionality, Integration, Separation/authenticity, User fit, Refinement. Checkpoints/compiles/unit tests/single screenshots are not VERIFIED.

### ANNEX-B0030 · annex lines 88–88

End of normative 12 October 2026 Professional / Present / Theme Preferences annex
