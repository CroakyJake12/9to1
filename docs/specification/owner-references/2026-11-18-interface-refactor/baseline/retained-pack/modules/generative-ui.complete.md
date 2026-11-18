# Generative UI and live artifact output

Module ID: `generative-ui` · 9to1 Development Specification v3

## Prerequisites

- **CUI, themes and shared interaction system** — Typed UI components, theme tokens/materials, layout, focus, motion, accessibility and shared control states.
- **Dulche runtime, requests and execution services** — Canonical AI broker, capability-aware routing, durable Task/Runs, action execution and lifecycle.
- **Artifacts, local persistence and recovery** — Canonical artifacts, revisions, typed edits, local saves, derived exports, sync and recovery.
- **Permissions, approvals and execution scope** — Effective scoped grants, action approvals, policy intersections, revocation and audit.

## Overview

Generative UI uses typed safe CUI components bound to inspectable state/actions. It can appear inline in Spaces and as a canonical output, while app-owned documents and exports retain their real artifact semantics.

Current owner overrides: V3-OWNER-008.

## Layouts & visible states

- **V3-GENERATIVE-UI-LAY-01** — Render meaningful interactive forms, charts, tables, selectors and progress states, with a visible connection to the active task and sources.
- **V3-GENERATIVE-UI-LAY-02** — Live Present/Canvas progress shows semantic work on the canonical artifact and high-level completed/current actions. Generated file cards show name, owner, revision, format and truthful availability.

## Features & behaviour

- **V3-GENERATIVE-UI-FEA-01** — Validate generated component schemas and actions against the safe registry. Arbitrary text, HTML or code is not automatically trusted UI or an executable permission grant.
- **V3-GENERATIVE-UI-FEA-02** — Keep ephemeral conversational state separate from promoted durable artifacts. A promotion/save action creates or updates the owning artifact through its typed API.
- **V3-GENERATIVE-UI-FEA-03** — Open resolves the canonical artifact; Download requires verified durable bytes. Failed PDF/export retains the saved native source and offers retry from the known revision.
- **V3-GENERATIVE-UI-FEA-04** — Preserve interaction state and accessibility during streaming updates; invalid component output must yield a recoverable error without deleting prior valid content.

## Canonical entities & persistence

- **V3-GENERATIVE-UI-ENT-01** — Persist component/state/action IDs where needed, schema version, source Task/Run, artifact ID/revision and export receipt. Generated controls cannot maintain a hidden parallel copy of app state.
- **V3-GENERATIVE-UI-ENT-02** — Store a durable export manifest only after bytes are materialized and verified; a rendered preview is not sufficient.

## Permissions & recovery

- **V3-GENERATIVE-UI-PER-01** — Use current app grants for every generated action. Sandbox untrusted content, constrain external links/actions and recheck revoked source access.
- **V3-GENERATIVE-UI-PER-02** — Retry idempotently and distinguish export-only failure from native-save failure; never rerun unrelated completed generation work automatically.

## Acceptance checks

- **V3-GENERATIVE-UI-ACC-01** — Test interactive state changes, invalid schema, source revocation, narrow layout, keyboard accessibility and reload recovery.
- **V3-GENERATIVE-UI-ACC-02** — Verify Open/Download receipts and failed-export recovery against real bytes and the same canonical source revision.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

Named baseline section(s) retained in the technical-source chapters below.

## Technical reading map

- Generative UI and live artifact output — Generative UI; Rendering and trust boundary (part 01) — `technical/generative-ui-01.md` (1,108 words; V2-B2107).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Generative UI and live artifact output — Generative UI; Rendering and trust boundary (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2107 · v2 lines 11983–12032

Generative UI
Generative UI is a Dulche capability that allows a model or Agent to return arbitrary structured user interfaces when an interface is more useful than prose. Generated interfaces may be visual, interactive and stateful, but MUST be expressed through a validated declarative Generative UI schema and MUST NOT execute arbitrary model-generated application code.
Rendering and trust boundary
CUI is the rendering layer for Generative UI. Generated surfaces render inside a CUI <GenerativeContainer> or equivalent trusted host component using approved CUI primitives, design tokens, theming, layout, accessibility and interaction behaviour. Any authorised 9to1 app may host a GenerativeContainer without implementing its own incompatible renderer.
Dulche may choose the structure and composition of the interface, including novel layouts, but the model output is data/schema rather than executable JavaScript, Python, shell commands, native binaries or arbitrary application code. The Generative UI runtime validates schema version, component types, properties, state bindings, declared actions and capability references before rendering.
Templates
Generative UI provides reusable high-level templates for common interaction patterns. Templates are starting structures, not hard limits; Dulche can instantiate, extend, combine or restyle them through supported schema properties.
* Flashcard set / study deck.
* Table, sortable/filterable data table and comparison grid.
* Quiz, practice exercise and answer/reveal workflow.
* Timeline and step-by-step guide.
* Checklist and progress tracker.
* Card grid / gallery / result browser.
* Form, questionnaire and multi-step wizard.
* Kanban/task-board style layout where appropriate.
* Glossary / FAQ / concept-reference layout.
* Diagram, relationship map and structured visual explainer.
* Other shared templates added by the Generative UI framework or authorised extensions.
Primitive composition
When no template fully fits the task, Dulche can compose a custom surface from approved lower-level primitives. The supported primitive set should include, where appropriate, text/heading blocks, icons or generated illustrations, cards, badges/tags, buttons, inputs, dropdowns, toggles, sliders, chips, lists, tables, grids, tabs, accordions, progress indicators, layout containers, drag containers, drop zones, connectors/relationships, semantic-object views and other safe CUI primitives.
The primitive library is extensible through versioned framework/extension contracts. Unsupported component types MUST be rejected or downgraded safely; a model cannot create a new privileged executable component merely by naming it in the schema.
State and interaction
Generated surfaces may maintain bounded typed state. Supported interactions can include selecting options, sorting/filtering, expanding/collapsing content, progressing through steps, editing fields, revealing hints/answers, checking responses, reordering items, dragging supported objects between declared drop zones, switching views and updating selected data.
State mutations are handled by the Generative UI runtime or by declared typed actions. Interaction support does not grant the model arbitrary code execution or unrestricted access to the host application's process.
Progressive conversational evolution
A generated surface is a first-class conversation artifact and may evolve across turns. Dulche should be able to patch or transform an existing surface instead of rebuilding unrelated output from scratch. Examples include turning a static explanation into an interactive exercise, adding another column to a table, converting notes into flashcards, adding filters, changing a card grid into a comparison view or preserving quiz progress while adding new questions.
Updates must address stable SurfaceID/component/state identities or equivalent schema identities so user interaction state is retained wherever the requested transformation allows it. Partial regeneration is preferred when only part of a surface changes.
Typed actions and side effects
Generated controls bind to declared typed actions rather than arbitrary executable handlers. An action definition includes stable identity, expected input schema, allowed state mutation or API binding, required capability/permission and result/error contract.
Pure presentation and local interaction may execute inside the Generative UI runtime without external side effects. Actions that invoke 9to1 APIs, tools or external services remain subject to the same permission, confirmation, auditing and consequence rules as the underlying capability. Generative UI MUST NOT be a path around security prompts or tool permissions.
Data binding and semantic objects
Generative UI can bind to authorised structured data and canonical 9to1 entities instead of flattening everything into prompt text. Examples include Data ranges, Planner tasks, Maps Places/Routes, Board items, Write selections/documents, search results, translation sets, code diffs or Dulche Action Graph state.
Bindings retain source identity and permissions. A rendered card or table row that represents a canonical object should reference that object rather than silently creating a disconnected copy unless the user explicitly asks for a snapshot.
Ephemeral and saved artifacts
A generated surface may remain ephemeral to the current response/session or be promoted through an explicit supported conversion/save path. Examples include saving generated flashcards as a study resource, converting a generated task board into a Board, inserting a generated table into Write/Data or saving a generated route through Maps. Promotion uses the destination app's canonical API and permissions.
Third-party visual examples and assets
Generative UI may reproduce the functional idea or spatial structure needed to explain a third-party product/game without copying proprietary artwork merely to imitate the source. Where licensed/authorised assets are not available, it should use generic, stylised, text-labelled or newly generated representations sufficient to communicate the concept.
Example: if the user asks how an item is crafted in Minecraft, Dulche may create a visual 3x3 crafting-grid representation using generic/stylised ingredient tokens and an output slot rather than copied official item textures or proprietary interface artwork. In later turns the same surface may be made interactive using approved drag/drop primitives so the user can move ingredient tokens, receive validation, request hints or turn the recipe into a quiz.
Accessibility and visual consistency
Generated UI MUST inherit the host CUI theme/design system and remain accessible. The runtime should enforce semantic roles, labels, focusability/order, keyboard operation, screen-reader information, non-colour-only meaning and accessible alternatives for interactions such as drag/drop wherever the component library supports them.
Failure and fallback
Invalid, unsupported or unsafe generated schemas MUST fail closed without executing arbitrary content. Dulche may repair/regenerate the affected surface or fall back to a non-interactive/native representation while preserving the user-visible answer where possible.
Result contract and acceptance
Result.GeneratedUI contains the generated UI payload when present and returns null when no generated interface is produced. The payload must identify schema version, surface identity, component tree, declared state/bindings and declared actions/capabilities required to reproduce the surface safely.
* Dulche can create arbitrary useful UI structures without arbitrary code execution.
* Built-in templates and lower-level CUI primitives can be combined and extended within the declarative schema.
* Generated surfaces support safe stateful interaction and can evolve across conversation turns without unnecessarily discarding user state.
* All external side effects route through typed permission-aware actions.
* Structured 9to1 data can be bound by canonical identity, and generated artifacts can be promoted through destination-app APIs.
* Generated UI follows CUI theming/accessibility and safely handles invalid schemas or unavailable components.
