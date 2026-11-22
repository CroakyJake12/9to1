# 13. Comic theme

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.rh42mon9x4c

<a id="h.pjkwojx9j86w"></a>

# 13. Comic theme visual and interaction specification

Comic visual authority: the owner's full-size 9to1 Intelligence Setup screenshot supplied on 22 November 2026 is the reference for this theme, reconfirming and sharpening the earlier owner-supplied concept. Its illustrated split-page composition, typography, palette, panel geometry and control details govern Comic. Every later generated miniature alternative proposed during the theme review was explicitly rejected. Sample inference and subscription statements in the artwork are illustrative content, not permanent product capability, pricing, entitlement or provider promises. Comic supersedes Cinematic as an available theme and requires a verified Atlas v4 visual specimen; this section does not claim that the current Atlas already implements it.

[Embedded source image kix.exg2htbauc6w; retained source metadata in native.json and original native document]

![Owner Comic reference](../../../references/comic-owner.png)

Owner-supplied full-size Comic reference screenshot, 22 November 2026 — 9to1 Intelligence Setup. Artwork and example copy establish design direction only; all review-generated Comic alternatives were rejected.

<a id="h.tlbp43goh3rj"></a>

### Theme identity, migration and scope

COMIC-001 — Register Comic under stable theme ID comic. The current built-in roster becomes Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro, Western and Comic, plus Create Theme. Remove Cinematic from every current chooser, onboarding theme gallery and marketing/settings label. Compatibility inputs containing cinematic resolve to comic through an explicit migration record; do not keep a second selectable legacy theme. Preserve original preference provenance so debugging can explain the change. Other saved custom themes do not migrate merely because their names contain the word Cinematic.

COMIC-002 — A theme swap changes shared CUI chrome tokens and declared component variants. It does not change app ownership, source binding, tool grants, inference route, personality, document formatting, authored slide/image/video content, browser page CSS or games' authored artwork. All IX interaction records apply identically under Comic. Saving the theme is a preference transaction independent from saving an open document.

<a id="h.1792warnh8pn"></a>

### Composition and materials

COMIC-003 — Use the authoritative owner reference's full illustrated split-panel composition: a strong diagonal glowing boundary separates a cool blue-black/teal local/technical illustration zone from a vivid plum-magenta cloud illustration zone, fading toward a dark ink lower region. Preserve its full-screen sense of depth and editorial scale: perspective hardware, cloud, luminous orbit and circuitry accents, short uppercase tracked supporting text, and prominent white italic hero labels. The diagonal is a deliberate large panel separator, not a skew imposed on every control. Large onboarding and feature-choice surfaces retain two spacious contrasting panels, not a generic small-card gallery. Dense editors use straight content grids, predictable tabs and toolbars, translating the reference's palette, typographic weight and edge treatment without reducing usable reading or editing space.

COMIC-004 — Backgrounds can combine restrained radial color illumination, fine circuitry/orbit-like lines and sparse dots or star marks. These are decorative assets and never imply a live device, network connection or model operation. Do not add speech bubbles, hand-drawn wobble, aggressive halftone textures or arbitrary comic-book fonts as mandatory features: they are not established by this reference. The intended result is polished, illustrated, energetic graphic panels with high legibility.

COMIC-005 — Cards use a clear ink boundary, modestly rounded or slightly clipped corners and a firm offset shadow where elevation matters. Large feature cards can have a diagonal outer edge while the interactive hit region remains a generous, predictable rectangle within the card. Surface layering uses contrast and outline first; do not rely on blurred glass alone to separate controls. A decorative tilted plate can sit behind a level label, but body text and editing carets stay unrotated.

COMIC-006 — The initial dark token family uses deep ink as the canvas, blue-black and dark plum panel surfaces, luminous cyan and pink/lilac for secondary highlights, near-white readable primary text, muted cool text for secondary labels, and bright lemon yellow for the strongest call to action. Suggested implementation seeds are Ink #080C18, Blue panel #12374A, Plum panel #4B164F, Cyan #7CE5E7, Magenta #A62081, Paper #F2F4FA, and Action yellow #FFF12A. These seeds are design defaults to calibrate against the supplied artwork and accessibility states, not a claim of an exact source color profile. Keep warning, error, success and selection tokens semantically distinct.

COMIC-007 — Light mode is a separately specified adaptation because the supplied mockup illustrates dark mode. Use warm near-white page surfaces, dark ink text/outline, light cyan and pink panels, yellow primary actions with ink labels, and the same strong shadow/diagonal vocabulary. Do not invert the screenshot mechanically. High-contrast mode may replace gradients with solid surfaces and simplify decorative edges while keeping Comic's clear outline and type hierarchy. The Atlas must eventually show dark, light and high-contrast control specimens.

<a id="h.pfyqe191z9eh"></a>

### Typography and information hierarchy

COMIC-008 — Hero headings use a heavy sans-serif italic face, tight but readable spacing, mostly uppercase short labels and a restrained hard ink shadow. Large action labels also use bold italic text. Eyebrows use smaller uppercase tracking as in the mockup's category labels. Body copy, settings descriptions, code, chat messages, long titles and table data use upright readable type. Do not uppercase user-authored text, accessibility names or editable values. Font substitutions must preserve legibility and fit, including multilingual scripts without synthetic distortion.

COMIC-009 — Compact status strips and recommendation badges can use a bright rectangular plate with an optional small star/marker and strong uppercase label. The badge must contain text identifying its meaning. “Recommended” is shown only when a real recommendation policy selected that option and can explain the relevant hardware/needs; decoration cannot manufacture a recommendation. Support text and capability chips describe the current model/provider/device state rather than permanent “frontier”, “unlimited” or training promises copied from the reference.

<a id="h.ssyegwrqfy2z"></a>

### Buttons and interactive states

COMIC-010 — The primary large button uses bright yellow fill, a dark readable bold italic label, an optional trailing arrow, modest corner rounding and a pronounced darker offset base/shadow. The label and arrow are one activation target. A trailing arrow is decorative unless a separate, clearly divided menu segment is explicitly present. On a choice card the button commits its named choice; clicking noninteractive illustration or benefit text does not also commit. Loading replaces the decorative arrow with a truthful progress indicator while preserving the action's label and accessible status.

COMIC-011 — Hover strengthens outline/shadow and may lift a button by at most 2 logical pixels with a short transition. Pointer-down moves the face toward its offset base and reduces the visible shadow; release inside activates under [IX-BASE-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.42wlfc207wc6). Release outside restores the prior visual state and cancels. Keyboard focus uses a persistent contrasting outline outside the button, independent of hover and pressed state. A selected toggle retains a check/pressed indicator after focus leaves. A disabled button uses a muted solid surface and readable reason; it does not remain bright yellow as if actionable. Reduced motion removes travel while retaining contrast/outline feedback.

COMIC-012 — Secondary buttons use an ink/dark panel or paper plate with a strong border and cyan/lilac accent; tertiary/ghost actions use clear text/icon plus a visible hover/focus boundary. Destructive actions use the semantic destructive token and explicit verb, never the generic yellow “recommended” treatment. An error state has icon/text and local explanation. Success, warning and pending use distinct labels/icons as well as colors. No flickering glow or shaking is needed to attract attention.

COMIC-013 — Context menus use a legible rectangular ink/paper panel with a clear outline and restrained offset shadow. The active row has a contrasting solid highlight; checked items retain a visible checkmark. Separators divide meaningful groups without diagonal cuts through labels. Tooltips use the same typography/material family but do not obscure the active editor selection or replace an accessible label. Right-click behavior remains the control's declared menu, and no Comic-specific command is invented.

<a id="h.553taokyr6fz"></a>

### Tabs, dragging, forms and previews

COMIC-014 — Tabs retain distinct app icons, left-aligned names, separate close targets and the host's selected-tab geometry. Comic may add a bold edge, corner accent or short panel-like underline; it must not turn tab labels into overlapping angled cards that shrink hit targets. Active, focused, dirty, loading and attention states remain independently legible. The tab-close hit region does not move during hover animation. Pinned tabs retain full accessible names and context-menu access.

COMIC-015 — A draggable item uses a semi-opaque outlined preview with its title/count. A valid reorder target is an unambiguous high-contrast insertion line; nesting uses an outlined container plus destination label. Invalid targets show a prohibited indicator and reason where useful. Keep the ghost sufficiently quiet to see the target below it. Pressed button shadows and drag shadows are visually distinct. Cancel removes the preview and returns to the unchanged source order. Comic animation cannot make a cancelled move look committed.

COMIC-016 — Inputs keep an upright caret, stable padding, clear border, readable placeholder and explicit focus ring. Validation appears below or beside the field without shifting unrelated controls unpredictably. Sliders have a visible thumb and value; toggles have labels and checked state independent of decorative color. Dense property panels retain straight rows. Numeric values, units, selected sources and actual permissions must remain readable when an accent gradient or illustration is nearby.

COMIC-017 — Live Design previews, charts and documents sit in neutral frames that distinguish authored content from Comic chrome. The preview uses its actual output, not a themed placeholder suggesting progress. A missing/loading preview shows a status region with meaningful labels. The Comic theme does not paint generated assets with its style unless the user explicitly requested Comic-styled content.

<a id="h.p41t7ahpkmny"></a>

### Mobile, performance and acceptance

COMIC-018 — Mobile retains the persistent floating bottom bar, pinning and More catalogue. Give the bar an ink/paper body, strong boundary and restrained elevation; preserve readable icon names and adequate hit regions. Two large diagonal panels become stacked panels on narrow screens, retaining order and choice labels rather than squeezing headings across a diagonal. Keyboard appearance, fold/unfold and external-display transitions preserve current input, open menus and chosen settings. Projector can use larger panel compositions on the external display while the phone remains a usable controller.

COMIC-019 — Decorative lines, gradients and illustrations must not consume resources needed by inference or mobile editing. Provide a static decorative layer and reduced-detail rendering for low-memory devices. Avoid perpetual animation, repeated high-cost blur and animated backgrounds behind text. The theme must preserve system scaling, increased text size, reduced motion, screen readers and focus/selection contrast. Artwork cannot conceal a pending permission or change a command's state.

COMIC-020 — Acceptance covers switching to Comic with a dirty document; opening/dismissing a context menu; left/right/middle-click and dragging a host tab; keyboard focus through primary/secondary/destructive controls; cancelled drag; pending/failed save; source-bound chat controls; disabled model capabilities; sound preview; mobile bottom-bar overflow; 200% text scaling; light/dark/high-contrast and reduced-motion modes. Confirm actual command outcomes are identical to other themes, no authored content changes, and legacy cinematic preferences migrate exactly once. Required new Atlas samples include the supplied-style hero, dense editor toolbar, settings rows, tab strip/menu/drag targets, chat, mobile bar and error/loading/disabled states.
