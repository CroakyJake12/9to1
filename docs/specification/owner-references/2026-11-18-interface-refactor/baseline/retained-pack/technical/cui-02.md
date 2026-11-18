# CUI, themes and shared interaction system — Responsive layout and no control explosion; Accessibility and discoverability (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0293 · v2 lines 1268–1268

**B/D.** An empty collection shows an explanation and an immediately usable create/open action, not a blank rectangle. Loading shows only the region awaiting data and retains safe navigation. Failure does not impersonate empty data. Offline/local-only state explains which optional services are unavailable without forcing an account wizard over a usable local editor. Restoration shows the actual target, including unavailable references, without fabricating placeholder documents as the recovered originals.

### V2-B0294 · v2 lines 1270–1270

Skeletons reserve plausible dimensions only while data is genuinely pending, then release them. Do not leave a giant fixed-height placeholder inside an editable block. A loading message is not persisted as user content and must not duplicate on each render.

### V2-B0295 · v2 lines 1272–1272

### UX27-SHARED-018 — Responsive layout and no control explosion

### V2-B0296 · v2 lines 1274–1274

**B/D.** Each toolbar declares priority groups and an overflow policy. At reduced width, move lower-priority groups into a labelled overflow menu before clipping or adding uncontrolled rows. Keep essential create/save/mode controls reachable. Do not render every possible action for every cell/block simultaneously. Contextual tools replace the relevant group in the existing tool surface; they do not add a second wall of controls.

### V2-B0297 · v2 lines 1276–1276

Remember user-resized panes within sensible bounds. Sidebars scroll independently of the document; fixed footers remain visible. An accessibility zoom setting may require a more spacious layout, but must not silently remove actions. Test narrow windows with long titles and translated labels.

### V2-B0298 · v2 lines 1278–1278

### UX27-SHARED-019 — Accessibility and discoverability

### V2-B0299 · v2 lines 1280–1280

**B/D/R.** Every pointer gesture has a practical keyboard/menu alternative. Trees expose hierarchy and expanded/selected state; sliders announce label/value/range; icon buttons have action names; canvas objects have meaningful semantic navigation where supported. Do not make hover the only way to discover an essential action on touch devices. Tooltips show a label, shortcut and useful disabled reason, with a default pointer delay of 400 ms; keyboard focus can expose the same information without a precision hover.

### V2-B0300 · v2 lines 1282–1282

Tree focus and the page currently open are separate until activation; keyboard navigation must not trigger an expensive page load on every arrow press. Native accessibility adapters and web semantics require actual testing rather than accepting a label property in source as proof.

### V2-B0301 · v2 lines 1284–1284

### UX27-SHARED-020 — Performance without content loss

### V2-B0302 · v2 lines 1286–1286

**B.** Preserve the 20 October scalable-content annex: no arbitrary document/page/slide/block count cap used to hide an inefficient implementation. Prioritise input, selection, current-region edits and scrolling over distant rendering/indexing. Virtualise and hydrate by stable IDs, keep caches bounded, and use incremental persistence. Unrendered content remains real, searchable and exportable.

### V2-B0303 · v2 lines 1288–1316

Measure input/frame/save/open behaviour on declared hardware and workloads. New motion timings above are presentation defaults, not performance guarantees. No pass is earned by silently dropping strokes, skipping blocks, truncating exports, disabling recovery or lowering canonical document fidelity.
<!-- UX27:END SECTION-01 -->
CakeUI (CUI)
Purpose, design philosophy and framework boundary
CakeUI (CUI) is the authoritative 9to1 user-interface framework and .cui is the only final application-markup format for first-party 9to1 applications. CUI is intentionally small, opinionated and quick to learn: ordinary application authors work with a fixed compact element vocabulary, Types, properties, Themes, Definitions, Prefabs, variables, Styles/actions and a small set of structural language constructs rather than an Avalonia-sized public control taxonomy.
CUI is derived from a pinned Avalonia source fork for mature low-level rendering, composition, text measurement, layout foundations, input, focus, accessibility, windowing and platform integration. Avalonia remains an implementation foundation; it is not the public app-authoring framework. CUI MUST compile directly to the CUI/Avalonia runtime representation and MUST NOT translate .cui into AXAML as an intermediate authoring/runtime contract.
Shipping first-party app projects MUST stop recognising .axaml and .hui as accepted application markup. C# and other implementation code remain valid for domain logic, services, specialised controls/renderers and platform integration; ordinary visible application structure and styling are authored through CUI.
CUI-CONF-001 — Public language boundary. A .cui extension alone proves nothing. Shipping authoring registries, schemas, parser/runtime loaders, code generation, hot reload, samples, designer output and documentation MUST expose the canonical CUI vocabulary and typed properties, not donor controls disguised by a file rename. Use Text, not TextBlock; Container Type="Grid", not Grid; Container Type="Vertical Stack" or "Horizontal Stack", not StackPanel. Layout and scrolling use the registered CUI Container properties, including RowSizes, ColumnSizes, Row, Column and independent scrolling, not public RowDefinitions/ColumnDefinitions or Grid.Row-style attached-property syntax. Do not reject valid registered CUI properties merely because a donor has a similarly named property.
CUI-CONF-002 — No permanent legacy aliases. Reject Grid, StackPanel, WrapPanel, ScrollViewer, TextBlock and Border as built-in public element tags, including donor-namespace/reflection escape routes. Border remains a valid registered CUI property, not a primitive tag. Custom (Definition) components remain supported, but MUST NOT be used to re-export the forbidden donor taxonomy as aliases. Import symbol aliases and the explicitly specified CUI shorthand/ID aliases remain valid. An explicit one-time source migration tool may read legacy input and emit canonical CUI with diagnostics; production compilation/loading MUST NOT silently accept or fall back to legacy syntax.
CUI-CONF-003 — Internal donor boundary. Keep the pinned Avalonia fork, its provenance/licensing and required mature low-level capabilities. CUI compiles directly to its own typed representation and runtime binding, never through generated AXAML. Native framework objects, donor-private assets and specialised host implementation code stay behind reviewed renderer/platform adapters; they do not define an alternative first-party application UI. Inventory every first-party shell, page, menu, dialog, popup, inspector and shared component and migrate its ordinary structure/styling to CUI. No generic Object wrapper around an entire legacy app, second hidden UI tree or swallowed parser failure may bypass this boundary.
The framework MUST make common UI concise without becoming ambiguous. Shorthand may reduce typing but MUST NOT change semantics or permit guessing. If a shorthand token has more than one valid resolution, compilation fails and the developer must use an explicit form.
Donor, source and compatibility contract
The implementation MUST obtain and include the selected appropriate Avalonia source revision in the CUI source tree/build graph under the global donor-source contract. The exact upstream repository, selected revision/release, licence obligations and local modifications MUST be traceable.
CUI MUST maintain an upstream-parity manifest for the low-level Avalonia capabilities it relies on and a CUI property-parity manifest for broadly applicable visual/layout/property families derived from Avalonia and mature CSS concepts. A feature being absent from CUI's small primitive element vocabulary is not permission to omit mature framework capability when it can be represented through Type, property, Definition, Prefab or Object hosting.
The public CUI language/runtime is versioned independently from Avalonia. Upstream Avalonia changes MUST be adapted behind CUI contracts and MUST NOT silently alter public CUI syntax or semantics.
CUI-CONF-004 — Authentic specialised surfaces. A registered Object Type may host an actual donor-backed document, canvas, browser or media engine; for example Object Type="WriteDocumentSurface" is a valid integration shape when that Type is registered. Its contract MUST expose the applicable typed content/selection/actions, lifecycle, input/focus, accessibility, theme integration, errors and canonical state. CUI owns surrounding app controls and chrome; the engine owns its specialised content semantics. Neither a static image nor a generic text box is a substitute for a required specialised engine. CUI themes MUST NOT rewrite user-authored document styling or other artifact content.
Core language vocabulary
The built-in visual/structural vocabulary is exactly the ten elements listed below: Page, Container, Text, Image, Audio, Video, Object, Button, Input and Layer. Specialised products extend CUI through registered Type, (Definition), Prefab or Object Type rather than expanding the primitive donor-control taxonomy. Nonvisual/declarative language constructs, including Anchor placement declarations, are separate from these ten elements and retain their specified semantics.
Core visual/structural elements:
* Page — root navigation/render surface and top-level UI scope.
* Container — general layout/composition element; layout behaviour is selected primarily through Type and properties.
* Text — textual content element.
* Image — image content element.
* Audio — audio/media element where a visual/controller surface is required.
* Video — video element.
* Object — host for specialised renderers/objects such as graphs, Canvas surfaces, browser/media/document engines and other non-primitive content.
* Button — standard action control.
* Input — standard input family; Type selects text, number, checkbox, slider and other registered input behaviours.
* Layer — rendering-depth container; Layer is also available as an element property.
