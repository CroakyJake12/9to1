# Supplied v3 CUI excerpt — apply v3.1 reference amendment



Baseline source wording, not independent acceptance or the full technical module pack.

## CUI themes and shared interaction system

Module cui    Current contract and exact reference designs

## Prerequisites

Authority, scope and implementation reading map. Current source precedence, narrow owner overrides, source accounting and the required module structure.

## Overview

CUI is the single public UI system for every 9to1 surface. The current Professional annex defines the canonical restrained default; theme identity changes materials, typography and motion without changing app topology or repainting authored content.

Current overrides: V3-OWNER-001, V3-OWNER-005, V3-OWNER-010, V3-OWNER-017.

## Layouts and visible states

V3-CUI-LAY-01  Offer Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western, plus Create Theme. Cinematic is an explicit legacy compatibility choice rather than a ninth current default.

V3-CUI-LAY-02  Preserve each theme’s character: Professional restrained dark charcoal; Glow luminous accent-dependent controls; Playful soft coloured surfaces; Frosted Glass translucent layered material with a legible fallback; Bubble watery dimensional surfaces and gentle wobble; Metallic brushed reflective material; Retro monospace; Western wood and sepia.

V3-CUI-LAY-03  Use Montserrat by default except the explicit Retro monospace treatment or authored document fonts. Use the exact Professional annex tokens, geometry and states where specified rather than inventing near-equivalent colours.

V3-CUI-LAY-04  Show normal, hover, pressed, selected, disabled, focused, loading, validation, destructive and reduced-motion states. Icon and text contrast must remain legible over every theme material.

V3-CUI-LAY-05  Use native platform emoji rendering for emoji characters. Keep application glyphs and identity marks coherent, distinct from decorative emoji.

## Features and behaviour

V3-CUI-FEA-01  Use CUI public primitives and typed templates. Avalonia and donor engines are internal implementation details; do not expose raw AXAML or third-party controls as a competing public UI language.

V3-CUI-FEA-02  Keep one deliberate primary scroll container per workspace, bounded panels and predictable focus/keyboard order. Popovers reposition at viewport edges; Escape/outside dismissal returns focus appropriately.

V3-CUI-FEA-03  Shared menus, tooltips, sliders, switches, inputs, tabbars, avatars, toasts, dialog surfaces and selection controls inherit the same theme and semantic action variants.

V3-CUI-FEA-04  App-specific content retains its own colours: slides, pages, photographs, charts, canvases and web previews do not receive theme-tint filters.

V3-CUI-FEA-05  Reduced motion disables wobble, bounce and ornamental animation while preserving state changes and feedback. Transparency and material effects need performant, accessible fallback modes.

## Canonical entities and persistence

V3-CUI-ENT-01  Store theme identity/version, compatible custom tokens, accent, typography/density and accessibility preferences in the Home-owned account/device preference contract. Space-specific overrides are explicit and reversible.

V3-CUI-ENT-02  Keep semantic tokens separate from app identity accents and document content. Theme switching must not mutate artifact revision or user-authored styles.

## Permissions and recovery

V3-CUI-PER-01  Custom themes are validated against supported tokens and accessible contrast/focus requirements; importing one is not permission to execute arbitrary UI code.

V3-CUI-PER-02  Restore a valid baseline if a theme resource is missing, incompatible or fails to load; preserve the user’s chosen identity for recovery.

## Acceptance checks

V3-CUI-ACC-01  Review all eight current themes, a custom theme and Cinematic compatibility with keyboard focus, mixed selected/disabled states, narrow screens and reduced motion.

V3-CUI-ACC-02  Verify the Professional annex’s menu/toolbar/selection/notes and Present geometry exactly; compare rendered state captures rather than only token files.

V3-CUI-ACC-03  Change themes while editing and verify selection, drafts, scroll, runtime state and authored content survive unchanged.

## Exact design states

The following figures are the current reference for this module. Their interface geometry and notes are part of this contract. Each figure has a stable screen ID shared with the atlas and source pack.

Create Theme · Authoring and preview  [custom-theme]

Components · Buttons, inputs and control states  [components]

Bubble · Material and controls  [theme-bubble]

Western · Material and controls  [theme-western]

Retro · Material and controls  [theme-retro]

## Technical reading map

Use these smaller chapters for detailed APIs, data models, donor integration, platform and recovery requirements. The complete Markdown module includes them in source order.

CUI, themes and shared interaction system — Context-menu coverage; Professional app theme (part 01). technical/cui-01.md (2,346 words).

CUI, themes and shared interaction system — Responsive layout and no control explosion; Accessibility and discoverability (part 02). technical/cui-02.md (1,333 words).

CUI, themes and shared interaction system — Basic markup and Container types; Comments, whitespace and escaping (part 03). technical/cui-03.md (6,058 words).

CUI, themes and shared interaction system — Exclusive markup and interface ownership; Conformance gates (part 04). technical/cui-04.md (1,918 words).

## Source clause IDs

CUI-EDITOR-102, CUI-EDITOR-103, CUI-EDITOR-104, CUI-EDITOR-105, UX27-SHARED-001, UX27-SHARED-002, UX27-SHARED-003, UX27-SHARED-004 and 52 additional IDs in the module source and source ledger.

## Create Theme Authoring and preview

custom-theme    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Primary, Secondary and Tertiary each independently choose Solid Colour, Gradient or Pattern, with authored transparency and supported blur.

2. All seven font roles expose family, weight and size. Button Roundness accepts the full 0–100% range.

3. This custom theme inherits Professional, so supported backdrop blur can be authored. Preview does not modify the saved preference until Save.

4. An app override recolours only Primary, preserving gradient stops, pattern geometry, transparency and blur; Secondary/Tertiary remain authored.

5. Hover Effects and Custom Cursors are later enhancements and are not presented as enabled controls.

Source: CUI-PREF-204–205; CUI-PRO-203; CUI inherited themes and typed fallback

## Components Buttons inputs and control states

components    Professional dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Primary, Secondary, Tertiary, Negative and Text buttons retain their semantic hierarchy in Professional and Glow. Accent-dependent colours resolve through the selected theme.

2. Hover morphs, pressed feedback and focus are distinct. The slide’s bounce becomes a restrained interruptible press response; reduced motion removes movement.

3. Five-second destructive hold is one optional pointer path. A keyboard/single-activation review dialog provides an equally explicit accessible confirmation route.

4. Input focus, invalid and disabled states remain distinct. Active controls never inherit disabled parent opacity.

5. Shared Feedback/Reveal/Reflow/Panel durations are 100/140/180/220 ms draft defaults; input, caret, save truth and permissions do not wait for animation.

6. Professional/Glow component examples preserve the deck’s blue/orange palette. Dark labels on bright Glow/red hover fills retain contrast. The six other themes are new design definitions; Professional material always has an opaque fallback.

Source: Authoritative PPT slides 1–6; UX27-SHARED-002–008/019; CUI-THEME-004

## Bubble Material and controls

theme-bubble    Bubble dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Water-like highlights and refracted edge light give controls depth. Gloss is restricted to control surfaces; text retains stable contrast and authored paper colours stay unchanged.

2. A click applies one 520 ms damped wobble to the activated control. State and action feedback occur immediately; animation never delays execution, save or permission changes.

3. Reduced Motion and the operating-system preference both remove movement. There is no ambient loop, floating layout or full-screen blur.

4. Light mode uses pale water surfaces and dark teal text. High contrast omits material decoration; focus and selected states remain separately visible.

5. The sample document is authored content: its cream paper, type and ink remain identical under every theme. Theme selection does not edit the document or grant any tool permission.

Source: Owner theme refinement, 28 October 2026; CUI-PREF-201–205; CUI-THEME-001–004; Retro category reference: https://opencode.ai/docs/themes/

## Western Material and controls

theme-western    Western dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. The build embeds one original photographic wood texture in --western-wood-image. Host, address and selected chrome use that same asset at preserved aspect ratio with strong readability overlays.

2. Sepia applies to chrome icons only. App symbols remain recognisable; source images, authored slide/document content, graphs, ink and Assistant portraits are never filtered.

3. Dark mode uses an inlaid dark address bar; light mode uses a pale waxed-wood treatment. Subtle inset edges and grain create material detail without obscuring labels.

4. No layout, toolbar tier, control location or navigation topology changes. Reduced Motion remains honoured; wood and the slight vignette are static.

5. The sample document is authored content: its cream paper, type and ink remain identical under every theme. Theme selection does not edit the document or grant any tool permission.

Source: Owner theme refinement, 28 October 2026; CUI-PREF-201–205; CUI-THEME-001–004; Retro category reference: https://opencode.ai/docs/themes/

## Retro Material and controls

theme-retro    Retro dark    1440 by 900 logical pixels

Exact reference composition from the offline atlas.

## Implementation rules

1. Modern terminal conventions inform the charcoal panels, monochrome hierarchy, monospace chrome, sharp control cells and hard offset shadows. OpenCode’s public theme documentation is a category reference, not a product or branding copy.

2. Static scanlines and dither are limited to chrome at low opacity. They do not cover document pixels, charts, authored colour, video frames, images or ink.

3. The optional 160 ms active response is a single small text-shadow displacement, without flashing content. Reduced Motion removes it; there is no continuous scan animation.

4. App accents and semantic status colours remain distinct. Light mode uses a legible paper-terminal palette; themes retain the same toolbar/container geometry.

5. The sample document is authored content: its cream paper, type and ink remain identical under every theme. Theme selection does not edit the document or grant any tool permission.

Source: Owner theme refinement, 28 October 2026; CUI-PREF-201–205; CUI-THEME-001–004; Retro category reference: https://opencode.ai/docs/themes/

## Professional theme across the editor

The same Present document and geometry in Professional.

Professional uses restrained dark or light surfaces and the app accent. It is the only current theme that may request Gaussian or Mica material; this reference shows the opaque performance fallback.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Glow theme across the editor

The same Present document and geometry in Glow.

Glow uses luminous accent boundaries and controlled highlights. Keep foreground labels readable and the native app identity stable.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Playful theme across the editor

The same Present document and geometry in Playful.

Playful uses warmer colours, softer corners and expressive accents. Preserve the hierarchy, control meanings and density of the current layout.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Frosted Glass theme across the editor

The same Present document and geometry in Frosted Glass.

Frosted Glass uses layered colour, translucent-looking borders and surface highlights. Do not introduce a Gaussian filter under the current material rule.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Bubble theme across the editor

The same Present document and geometry in Bubble.

Bubble uses watery depth, glossy rounded controls and dimensional bubbles. Button presses may wobble for 520 ms. Reduced motion replaces the wobble with a stationary pressed state.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Metallic theme across the editor

The same Present document and geometry in Metallic.

Metallic uses restrained brushed-looking highlights, fine edges and cool neutral surfaces. Keep focus and selected states distinct from decorative reflections.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Retro theme across the editor

The same Present document and geometry in Retro.

Retro uses terminal-style monospace chrome, ANSI accents, crisp rules and restrained raster effects. Authored document fonts and colours stay unchanged.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.

## Western theme across the editor

The same Present document and geometry in Western.

Western uses the bundled wood texture on host and selected control surfaces, warm paper tones and sepia chrome icons. It suggests an old film without tinting authored document content or running an expensive continuous filter.

All eight current themes have dark and light appearances in the atlas. App identity, semantic commands, permission boundaries, control topology and authored content remain stable. Theme selection does not create a document revision.

Source: CUI-PREF-201 to CUI-PREF-205, incorporated Professional annex and current owner theme directions.