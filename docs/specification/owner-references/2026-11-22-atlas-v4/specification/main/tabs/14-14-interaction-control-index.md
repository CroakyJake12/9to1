# 14. Interaction control index

Source: https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk?tab=t.b2rmbujhva1f

<a id="h.fadu2nvii59z"></a>

# 14. Interaction control index

<a id="h.h1h1w2h1oo9i"></a>

## Normative volumes and precedence

UI-IX-001 — These interaction records are binding parts of the 9to1 specification. The master contains IX-BASE, IX-HOST, IX-HOME, IX-SET, IX-LEARN, IX-SOUND and IX-THEME controls plus COMIC-001–020. The linked volumes supply the full Spaces, editor and app controls. They extend the existing feature/runtime requirements; they do not reintroduce older repository Markdown as authority. Later explicit owner decisions prevail; the pinned Atlas continues to define demonstrated composition except where an owner amendment, including Comic, explicitly supersedes it.

[Spaces interaction reference](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?usp=drivesdk) — complete control records, exact screen bindings and acceptance behavior.

[Editor interaction reference](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?usp=drivesdk) — complete control records, exact screen bindings and acceptance behavior.

[App interaction reference](https://docs.google.com/document/d/1tBC60DTC70QAZha8o7wn1SvgK1V3qL6vvKxDapYExl0/edit?usp=drivesdk) — complete control records, exact screen bindings and acceptance behavior.

UI-IX-002 — Every shipped control must resolve to a named interaction record and a concrete command/target binding. A new button, menu item, draggable handle, editable value or dynamic popup must add/update that binding before release. Explicitly record no-action behavior for unsupported mouse gestures. A screenshot alone, a generic “works normally” clause or the presence of a data-action attribute is insufficient to establish command semantics.

UI-IX-003 — One semantic command serves pointer, keyboard, touch, context menus and accessible alternatives. Shared defaults never broaden permissions, change source policy or repeat external effects. Control-specific records can refine selection/activation behavior, but cannot contradict canonical identity, grants or the runtime admission contract.

UI-IX-004 — The existing 251-screen register remains the pinned visual inventory. The interaction binding register covers those screens and explicitly separates shared inherited chrome, app-specific controls, reference specimens and new owner-requested surfaces. New mobile, source notebook, Dreaming, sound-bank, Assumptions Bank and Comic designs require Atlas additions; their absence from the old snapshot does not remove the requirements.

UI-IX-005 — Acceptance must exercise primary/secondary/middle/double input as defined, drag start/cancel/invalid drop/commit, keyboard and touch alternatives, stale target/revision changes, permission narrowing, failed persistence and recovery. The production result must match the intended command exactly once. Static source and document checks are useful traceability evidence but are not native interaction or GPU/runtime acceptance.

Adoption: Qwen’s later repository-reference reconciliation must link these current volumes and preserve IX/COMIC identifiers. Replace current Cinematic labels with Comic and implement the documented legacy preference migration. Preserve old evidence only as dated provenance. Do not mark interaction acceptance complete from this document update alone.

<a id="h.mskaquzh3p9b"></a>

## Shared bindings

Every eligible hosted screen inherits tab/body/menu/close/order/group/window/split behavior ([IX-HOST-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.aseep3t4mi4o)–007), address and history (008–009), owning document tools (010), contextual AI/access/voice (011), notifications (012), account (013), browser/profile/origin policy (014–015), inspection/query controls (016–017), native window controls (018), and account/browser submenus (019). Their controls retain the active TabID/pane/owner scope. Generic editor Undo/Redo/history and Comments route to [IX-EDITORS-005](https://docs.google.com/document/d/1djno_7ptJQNPuuQyqUWOMxrmdhflZ3GKgHevnCJJBLc/edit?tab=t.yh4mk9q4cw20#heading=h.s4biwivw4a4w)–006 with the real owner adapter; a live session cannot invent a surrogate document.

Settings sidebar repeats one canonical tree across settings screens. Search/Clear/group expanders use [IX-SET-020](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.hxwuuyrvutpa); destination navigation uses [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) plus [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu). Home sidebar routes use [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb). A link’s route identifies its destination and target requirements; Review/Open/Configure never performs the destination action. Repeated fixture rows bind by stable object identity, not displayed names or ordinal position.

<a id="h.uxei5u90hc8n"></a>

## Settings and sidebar destination register

Appearance · Theme and accent preferences → appearance; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Home · Apps → home-apps; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Home · Assistants → home-assistants; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Home · Library → home-library; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Home · Mesh & devices → home-mesh; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Models · Discover → home-models; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Home · Specialists → home-specialists; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

9to1 · New Tab → host-newtab; [IX-HOME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.gjgt7hu8tvyb) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Accessibility → settings-accessibility; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Account & subscription → settings-account; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Storage → settings-account-storage; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · AI → settings-ai; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Admin → settings-app-admin; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · AI Studio → settings-app-ai-studio; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Automations → settings-app-automations; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Boards → settings-app-boards; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Code, Sites and Stacks → settings-app-build; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Canvas → settings-app-canvas; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Decks → settings-app-cards; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Connect → settings-app-connect; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Data → settings-app-data; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Files → settings-app-files; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Forms → settings-app-forms; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Games → settings-app-games; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Launcher → settings-app-launcher; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Flow → settings-app-link; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Maps → settings-app-maps; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Virtual Device → settings-app-mini-computer; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Motion → settings-app-motion; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Story Sequences → settings-app-paths; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Frame → settings-app-picture; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Planner → settings-app-planner; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Play → settings-app-play; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Present → settings-app-present; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Prototype → settings-app-prototype; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Replay → settings-app-replay; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Collection → settings-app-shelf; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Spaces → settings-app-spaces; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Terminal → settings-app-terminal; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Voice & Vision → settings-app-vision-and-voice; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Wave → settings-app-wave; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Write → settings-app-write; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Appearance → settings-appearance; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Apps & updates → settings-apps; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Shared settings → settings-apps-shared; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · General → settings-general; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Bookmarks → settings-haven-bookmarks; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · General → settings-haven-general; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · History → settings-haven-history; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Menus & focus → settings-haven-key-menus; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Navigation → settings-haven-key-navigation; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Models & memory → settings-haven-model-memory; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · New Tab → settings-haven-newtab; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Performance → settings-haven-performance; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Pinned destinations → settings-haven-pinned; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Recent files → settings-haven-recent-files; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Services → settings-haven-services; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Shortcut order → settings-haven-shortcut-order; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Keyboard Shortcuts → settings-haven-shortcuts; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Storage & cache → settings-haven-storage-cache; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Back & forward → settings-haven-tab-history; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Tabs & address → settings-haven-tabs-address; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Installed Models → settings-installed-models; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Layout → settings-layout; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · General → settings-local-general; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Mesh & devices → settings-mesh; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Notifications → settings-notifications; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Permissions & trust → settings-permissions; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Personalisation → settings-personalisation; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Plugins/MCPs → settings-plugins; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Privacy → settings-privacy; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Profile → settings-profile; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Providers → settings-providers; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Sandbox → settings-sandbox; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Security & Privacy → settings-security; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · General → settings-security-general; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Skills → settings-skills; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Subscriptions → settings-subscriptions; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Theme Options → settings-theme-options; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

Settings · Voice → settings-voice; [IX-SET-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.yo6q9ebdaps1) / [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu).

<a id="h.jm15dsv2cbgr"></a>

## Screen-local control bindings

The following rows supplement the inherited shared chrome. Related labels are grouped only when their named record defines each operation. Routes in parentheses are exact design-reference destinations; production resolves the associated real identity. Reference labels and sample values are not fixed production data. Source: pinned Atlas screen catalogue and final available mount refinements; dynamic menus and visual-only editor regions are additionally specified in the control records.

<a id="h.rwdc4hw0ajfr"></a>

### home-apps — Home · Apps

Search apps; Clear app search; Filters: Installed selected; Installed; All apps; Pinned; Clear search — [IX-HOME-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9mlfqqe0dbbg)

Manage apps — [IX-HOME-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r06smngi67ud)

Open Spaces. Drag to reorder, or use Alt + Left / Right. → spaces-chat; Open Boards. Drag to reorder, or use Alt + Left / Right. → boards-editor; Open Write. Drag to reorder, or use Alt + Left / Right. → write-continuous; Open Present. Drag to reorder, or use Alt + Left / Right. → present-editor; Open Canvas. Drag to reorder, or use Alt + Left / Right. → canvas-editor; Open Code. Drag to reorder, or use Alt + Left / Right. → build-code — [IX-HOME-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.u8n6josja9rg)

WriteDocuments → write-continuous; PresentPresentations → present-editor; BoardsPages & ideas → boards-editor; CanvasDrawing & ink → canvas-editor; Code (Code, Sites & Stacks) → build-code; PrototypeInteractive systems → prototype; DataSheets & analysis → data; FilesFiles & collections → files; FormsForms, tests & quizzes → forms; PlannerCalendar & tasks → planner; CardsFlashcards & recall → cards-review; SpacesAI workspaces → spaces-chat; AI StudioAgents & tools → ai-studio; AutomationsScheduled workflows → automations; TerminalCommands & sessions → terminal; Vision & VoiceLive AI sessions → vision-voice; PictureImages & layers → picture; WaveAudio & music → wave; MotionVideo & animation → motion; PlayMatches & challenges → play; GamesGame creation → games; ConnectMessages & Mail → mail; MapsPlaces & journeys → maps; ShelfYour collection → shelf; LinkFlows & binders → link-flow; PathsGuided journeys → paths; ReplaySemantic recordings → replay; Mini ComputerVirtual machines → mini-computer; AdminOrganisation controls → admin; LauncherMobile home → launcher — [IX-HOME-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.z0xp8paxxsly)

<a id="h.ma3l2ksnmwkm"></a>

### home-dashboard — Home · Dashboard

Customise → host-newtab-customise; Ask AI → spaces-chat; All apps → home-apps; Add widget → host-newtab-widgets; Open calendar → planner; Up next options → host-newtab-customise; Open note → boards-editor; Pinned note options → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Search apps or enter an address; Go — [IX-HOME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tgu5gbescslm)

Open Design workspace → boards-editor; Open Study → spaces-study; Open Research notes → write-continuous; Open Today → planner — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

<a id="h.iun75yf172bf"></a>

### home-models — Models · Discover

Connect provider → settings-provider; Discover → home-models; My models2 → home-models-installed; Providers & routing → settings-models — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Model picker — [IX-SPACES-001](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.rf8587emfxvz)

Search model catalogue; Filter models by location [All locations; This device; Cloud]; All models; Chat; Code; Vision; Speech; Image; Video; Reset filters — [IX-HOME-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.1ion84wj6300)

Details for Qwen · Local; Details for Gemma · Vision; Details for Speech · Small — [IX-HOME-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.bhuhi24khcvb)

<a id="h.umqh4yq8zcxb"></a>

### home-library — Home · Library

Recent; Favourites; Shared; All apps; Search Library; Design workspaceBoardsDocuments / 9to1Saved locally2 min ago → boards-editor; A clearer way to learnPresentPresentationsSynced20 min ago → present-editor; Research notesWriteEPQ / DraftsSaved locallyYesterday → write-continuous; Revision trackerDataSchoolSync pendingYesterday → data; Learning mapCanvasPersonalSaved locallyMonday → canvas-editor; Cover studyPictureCreativeSource missingMonday → picture — [IX-HOME-017](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.7u3ax6j3p68r)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.dclghuvmdyit"></a>

### home-mesh — Home · Mesh & devices

Pair device; Device details — [IX-HOME-018](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.4j9db4hhn8hk)

<a id="h.5jmsxjdpvqiu"></a>

### host-newtab — 9to1 · New Tab

Customise → host-newtab-customise; Ask AI → spaces-chat; All apps → home-apps; Add widget → host-newtab-widgets; Open calendar → planner; Up next options → host-newtab-customise; Open note → boards-editor; Pinned note options → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Search apps or enter an address; Go — [IX-HOME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tgu5gbescslm)

Open Design workspace → boards-editor; Open Study → spaces-study; Open Research notes → write-continuous; Open Today → planner — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

<a id="h.pz3bu6wjt45x"></a>

### host-browser — 9to1 · Browser

Explore the journal — [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0)

Split view — [IX-HOST-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.fsut3a7qcnf3)

<a id="h.4p7hj7quazrp"></a>

### account — Settings · Account & subscription

Change icon; Remove — [IX-SET-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r3sy1wfxdjs5)

input; Add pronouns · optional; Add a job title · optional; Save profile — [IX-SET-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.wgqshx7gz019)

Sign out — [IX-SET-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.ez7ezccja1wb)

Manage plan — [IX-SET-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.xaozm0mrtbx2)

Manage (Settings · Storage) → settings-storage — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.zeshkbpzzjmm"></a>

### onboarding — 9to1 · Welcome

Continue locally → home-apps; Sign in to sync → settings-account — [IX-HOME-019](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.e7wwmk7yyrgv)

<a id="h.fm4mbonjbfmo"></a>

### home-models-installed — Models · Installed and saved

Discover models → home-models; Discover → home-models; My models2 → home-models-installed; Providers & routing → settings-models; Manage storage → settings-storage; Manage → settings-storage — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Model picker — [IX-SPACES-001](https://docs.google.com/document/d/1CJf0VXUVLXn1qyj3AYUTOTdxEouxuJfqXc48_jHpApE/edit?tab=t.4u1kzupu761c#heading=h.rf8587emfxvz)

Search your models — [IX-HOME-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.1ion84wj6300)

Details for Qwen · Local; Details for Gemma · Vision; Details for Speech · Small — [IX-HOME-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.bhuhi24khcvb)

<a id="h.v201ctn02hpr"></a>

### home-model-details — Models · Capability and requirements

Discover models → home-models; Explain this topic using my revision notes → spaces-chat; Help me find the bug in this function → spaces-chat; Turn this outline into a clear first draft → spaces-chat; Manage local model → home-models-installed — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Save model; Use model — [IX-HOME-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.bhuhi24khcvb)

<a id="h.8cqui1tfycpu"></a>

### host-newtab-customise — New Tab · Customisation

Add widget → host-newtab-widgets; Done → host-newtab; Accent → appearance-palette; Ask AI → spaces-chat; All apps → home-apps; Open calendar → planner; Open note → boards-editor — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Focus; Balanced; More widgets; Reset layout — [IX-HOME-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.m2m667tk1va2)

Upload; input; Neutral background; Horizon background; Paper background — [IX-HOME-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.dbgts97qvvuw)

Show on Home; input — [IX-HOME-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.pbw28rspf33w)

Move up; Move down; Add shortcut; Open Design workspace → boards-editor; Drag Design workspace; Remove Design workspace shortcut; Open Study → spaces-study; Drag Study; Remove Study shortcut; Open Research notes → write-continuous; Drag Research notes; Remove Research notes shortcut; Open Today → planner; Drag Today; Remove Today shortcut — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

Search apps or enter an address; Go — [IX-HOME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tgu5gbescslm)

Drag Up next; Move up; Move down; Make wide; Remove Up next widget; Drag Pinned note; Remove Pinned note widget — [IX-HOME-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.hittfg8nfqak)

<a id="h.mf2giijxrdec"></a>

### host-newtab-widgets — New Tab · Add a widget

Add widget → host-newtab-widgets; Done → host-newtab; Accent → appearance-palette; Ask AI → spaces-chat; All apps → home-apps; Open calendar → planner; Up next options → host-newtab-customise; Open note → boards-editor; Pinned note options → host-newtab-customise; Close widgets → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Focus; Balanced; More widgets; Reset layout — [IX-HOME-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.m2m667tk1va2)

Upload; input; Neutral background; Horizon background; Paper background — [IX-HOME-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.dbgts97qvvuw)

Show on Home; input; Find a widget; Up next, already shown; Pinned note, already shown; Add Focus timer; Add Continue working; Add My tasks; Add Study progress — [IX-HOME-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.pbw28rspf33w)

Move up; Move down; Open Design workspace → boards-editor; Drag Design workspace; Remove Design workspace shortcut; Open Study → spaces-study; Drag Study; Remove Study shortcut; Open Research notes → write-continuous; Drag Research notes; Remove Research notes shortcut; Open Today → planner; Drag Today; Remove Today shortcut; Add shortcut — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

Search apps or enter an address; Go — [IX-HOME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tgu5gbescslm)

Drag Up next; Move up; Move down; Make wide; Remove Up next widget; Drag Pinned note; Remove Pinned note widget — [IX-HOME-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.hittfg8nfqak)

<a id="h.vo71rqd9e3xv"></a>

### host-notifications — 9to1 · Notifications

Customise → host-newtab-customise; Ask AI → spaces-chat; All apps → home-apps; Add widget → host-newtab-widgets; Open calendar → planner; Up next options → host-newtab-customise; Open note → boards-editor; Pinned note options → host-newtab-customise; Notification settings → settings-notifications — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Search apps or enter an address; Go — [IX-HOME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tgu5gbescslm)

Open Design workspace → boards-editor; Open Study → spaces-study; Open Research notes → write-continuous; Open Today → planner — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

Mark all as read; Close notifications; All 4; Needs attention 2; Approval neededResearch would like to open an external source.Spaces · Permission requestJust now → spaces-running; Sync waitingYour revision tracker is saved on this device.Data · Saved locally8 min ago → data; Update readyA new version is ready when you finish working.Present · Package update21 min ago → settings-apps; Maths revisionA 45-minute session on parametric equations.Planner · Calendar event31 min ago → planner — [IX-HOST-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.2xlp3zfzez5d)

<a id="h.9y0g7psmhas5"></a>

### appearance — Appearance · Theme and accent preferences

ThemeChoose the look and materials of your workspace.ProfessionalChoose theme⌄; Select Professional theme; Select Glow theme; Select Playful theme; Select Frosted Glass theme; Select Bubble theme; Select Metallic theme; Select Retro theme; Select Western theme; Comic (replaces legacy Cinematic fixture) — [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3)

Light; Dark — [IX-THEME-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r2mm5ucajw2h)

High contrast; Reduced motion — [IX-SET-017](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6u5kzs66jqh2)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.vligna3cwnbd"></a>

### custom-theme — Create Theme · Authoring and preview

Edit stops; Colour; Edit — [IX-THEME-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.8gr6lil0k2s7)

Continue; Later — [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0)

Theme Name/Inherits and role styles → [IX-THEME-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.ahnbfjnkbcd3); transparency/blur/roundness → [IX-THEME-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.4cjns9zgqf5); all seven typography role fields and live sample controls → [IX-THEME-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.ev400kk4ow3l). Save theme and Cancel → [IX-THEME-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.8gr6lil0k2s7).

<a id="h.ib7p9gu8yoq2"></a>

### components — Components · Buttons, inputs and control states

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.mdouj84a71xo"></a>

### states — States · Recovery and persistence reference

View save recovery → boards-recovery; New Board → boards; Open file; Retry connection; Request access; Compare versions; Keep both; Retry save; Save a copy — [IX-HOME-020](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.goxw1usj1n7s)

<a id="h.63vtcw2bciu2"></a>

### theme-bubble — Bubble · Material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.7iyg0lpe9rqe"></a>

### theme-western — Western · Material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.w59u8vi05oyp"></a>

### theme-retro — Retro · Material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.1s1pdgu779f8"></a>

### settings-account — Settings · Account & subscription

Change icon; Remove — [IX-SET-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r3sy1wfxdjs5)

input; Add pronouns · optional; Add a job title · optional; Save profile — [IX-SET-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.wgqshx7gz019)

Sign out — [IX-SET-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.ez7ezccja1wb)

Manage plan — [IX-SET-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.xaozm0mrtbx2)

Manage (Settings · Storage) → settings-storage — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.rwq8x16sdni"></a>

### settings-models — Settings · AI & models

Add model; Local model route options; Cloud route options; Fallback policy [Ask before cloud; Local only; No fallback]; Image route [Inherit from Active; Choose a route]; Voice route [Choose Voice Model; Inherit from Active]; Background route [Qwen · Local; Inherit from Active] — [IX-SET-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.vl5hjr3v8s2b)

Never; Sometimes; Often; Always; Background Learning — [IX-SET-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.lr82l7kfqtds)

Discover models → home-models; Manage local models → home-models-installed; Review (Settings · Provider access detail) → settings-provider; Dulche service → settings-advanced; Assistants → home-assistants; Providers → settings-provider; Privacy controls → settings-privacy — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.kr68qcrhblla"></a>

### settings-permissions — Settings · Permissions & trust

Review history → host-notifications; Open Spaces → spaces-chat — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Granted access; Blocked callers; Approval defaults; Read-only; Write; Narrow access; Revoke; Block caller — [IX-SET-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.1q3zl7o1kyex)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.hlavra33zb0"></a>

### settings-apps — Settings · Apps & updates

Browse apps → home-apps — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Review updates; Details — WriteNative 9to1 app · StableInstalled1.8.2Available1.8.3StateUpdate availableDetails; Details — PresentNative 9to1 app · StableInstalled1.8.2Available1.8.3StateUpdate availableDetails; Details — Home CoreShared platform services · StableInstalled1.8.2Available1.8.3StateCore updateDetails; Manage — BoardsNative 9to1 app · StableInstalled1.8.3Available1.8.3StateCurrentManage; Manage — DataNative 9to1 app · StableInstalled1.8.3Available1.8.3StateCurrentManage; Choose app — [IX-HOME-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r06smngi67ud)

Download updates automatically; Install updates [Ask before installing; When apps are closed]; Update channel [Stable; Preview]; Download on metered connections; Retain rollback packages — [IX-SET-006](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.3jeey18bng72)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.7xgs29cwfh9k"></a>

### settings-storage — Settings · Storage

Manage models → home-models-installed; Privacy controls → settings-privacy; Manage resources → settings-account; Review (Files · Local files and details) → files; View device → settings-mesh — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

View location; Review cleanup; New conversation placement [Local; Mesh; Cloud] — [IX-SET-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.p4yeg4vs6pxe)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ufs2ligr01xf"></a>

### settings-mesh — Settings · Mesh & devices

Pair a device — [IX-HOME-018](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.4j9db4hhn8hk)

Personal; Choose folders; Share local model inference; Edit access; Revoke — [IX-SET-016](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.mcp7n9jb6htr)

Review chats → spaces-chat — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.3lap643zvqqk"></a>

### settings-privacy — Settings · Privacy

Cloud processing [Ask before cloud; Local only]; Product analytics; Crash report sharing [Ask each time; Never send]; Prepare export; Review deletion — [IX-SET-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.u6y3c1aedq66)

Review providers → settings-provider; Manage memory → settings-models; Learning settings → settings-models; Storage → settings-storage; Devices → settings-mesh; Inspect connections → settings-provider — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.i1u9gi8m16ai"></a>

### settings-accessibility — Settings · Accessibility

Interface scale [125%; 100%; 150%; 175%]; High contrast; Reduced motion; Always show keyboard focus; Status announcements [Concise; Detailed]; Pointer size; View shortcuts; Larger touch targets — [IX-SET-017](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6u5kzs66jqh2)

Example keyboard focus — [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.6nu61aqpqqdv"></a>

### settings-notifications — Settings · Notifications

Open notifications → host-notifications — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Desktop notifications; Notification sounds; Content previews [When unlocked; Always; Never]; Planner in notification centre; Planner desktop notifications; Planner notification badge; Automations in notification centre; Automations desktop notifications; Automations notification badge; Spaces in notification centre; Spaces desktop notifications; Spaces notification badge; Home in notification centre; Home desktop notifications; Home notification badge; Connect in notification centre; Connect desktop notifications; Connect notification badge; Quiet hours; Quiet while presenting; History & retention — [IX-SET-018](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.5qhudgy4z2sc)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.lz1348825cyg"></a>

### settings-advanced — Settings · Advanced

Export diagnostics; Inspect — Home CoreStateReadyDetailPlatform servicesInspect; Inspect — DulcheStateReadyDetail1 model loaded · 1 endpointInspect; Review repair; Inspect — Package serviceStateReadyDetailStable channelInspect; Inspect — MeshStateReadyDetail2 online · 1 offlineInspect; Keep Dulche available; Model residency [Unload when idle; Keep selected model loaded]; Always-active AI — [IX-SET-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tkw4oeudcbzb)

Open log; Review — Rebuild search indexRecreate the index from authorised originals.Review; Inspect — Validate settingsCheck supported schema versions without discarding unknown fields.Inspect; Automatic diagnostic uploads — [IX-SET-021](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.rkhtastwwlcb)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.rqvxtl7ir19v"></a>

### settings-provider — Settings · Provider access detail

Back to AI & models → settings-models; View routes → settings-models — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Test connection; Choose credential — [IX-SET-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.h52nlw2jajy)

input — [IX-SET-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.m6w2tiicmt8a)

Provider file context [Ask before sharing; Do not share]; Provider access to Persistent Memory; Provider access to Background Learning; Provider background requests — [IX-SET-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.vq7zxa2wfsqc)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.oqi8wbl0rv85"></a>

### theme-reference-professional — Theme refs · Professional

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.fsf1nb3s1yvb"></a>

### theme-reference-glow — Theme refs · Glow

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.oeru8jv53g4d"></a>

### theme-reference-playful — Theme refs · Playful

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.z8b8oijnxwqz"></a>

### theme-reference-frost — Theme refs · Frost

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.ra7vyh1wpuqu"></a>

### theme-reference-bubble — Theme refs · Bubble

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.876pmotqtyxg"></a>

### theme-reference-industrial — Theme refs · Industrial

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.gjsb87vpfkp9"></a>

### theme-reference-retro — Theme refs · Retro

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.rfpq2zuo1p9p"></a>

### theme-reference-western — Theme refs · Western

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.z325jit9qc41"></a>

### theme-reference-synthwave — Theme refs · Synthwave

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.dhuefjey2hbr"></a>

### theme-reference-snug — Theme refs · Snug

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.xi4fqq7ue9qv"></a>

### theme-glow — Glow · Corrected material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.icpkcevisi"></a>

### theme-playful — Playful · Corrected material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.xy7zy7imowh5"></a>

### theme-frosted — Frosted Glass · Corrected material and controls

Reference specimen controls and artwork → [IX-BASE-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.wju0avfyvtg0). Theme selection itself → [IX-THEME-001](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.9ss8ucd0qr3). Comic replaces the legacy Cinematic choice; reference-only named styles retain their documented status.

<a id="h.tv83gri1y5rx"></a>

### home-explore — Home · Explore

Make a game; Write a document; Make a presentation; Draw an idea; Describe what you want to make; Find an app — [IX-HOME-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.l82uq4szqvfi)

<a id="h.mnfpxuo7car"></a>

### appearance-palette — Appearance · Accent palette

Done; Electric blue; Iris; Lagoon; Sunset; Accent colour; Primary; Secondary; Tertiary; Ghost — [IX-THEME-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r2mm5ucajw2h)

<a id="h.l09lsfqnqbvg"></a>

### settings-general — Settings · General

Open (Settings · Appearance) → settings-appearance; Open (Settings · AI) → settings-ai; Open (Settings · Security & Privacy) → settings-security; Open (Settings · Account & subscription) → settings-account — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.t6wa59v15daw"></a>

### settings-appearance — Settings · Appearance

Open (Appearance · Theme and accent preferences) → appearance; Open (Settings · Theme Options) → settings-theme-options; Open (Settings · Layout) → settings-layout; Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.61k456npoe4i"></a>

### settings-theme-options — Settings · Theme Options

Light; Dark — [IX-THEME-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r2mm5ucajw2h)

Choose palette → appearance-palette; Create theme → custom-theme; Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.pqwt4tats3my"></a>

### settings-layout — Settings · Layout

Chat bar position [Off; Floating; Sidebar] — [IX-SET-019](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.hd0n65ignubd)

Customise (New Tab · Customisation) → host-newtab-customise; Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.683il0sle51n"></a>

### settings-ai — Settings · AI

Local model route options; Cloud route options; Fallback policy [Ask before cloud; Local only; No fallback]; Image route [Inherit from Active; Choose a route]; Background route [Qwen · Local; Inherit from Active] — [IX-SET-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.vl5hjr3v8s2b)

Open (Settings · Personalisation) → settings-personalisation; Open (Settings · Plugins/MCPs) → settings-plugins; Open (Settings · Skills) → settings-skills; Open (Settings · Providers) → settings-providers; Open (Settings · Voice) → settings-voice; Open (Models · Discover) → home-models; Open (Settings · Installed Models) → settings-installed-models — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ffl80fkgkcqw"></a>

### settings-personalisation — Settings · Personalisation

Never; Sometimes; Often; Always; Background Learning — [IX-SET-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.lr82l7kfqtds)

Open (Settings · Privacy) → settings-privacy; Open (Home · Assistants) → home-assistants — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.jzg3cv33fd3t"></a>

### settings-plugins — Settings · Plugins/MCPs

Browse (Spaces · Attach menu) → spaces-attachments; Review (AI Studio · Agent tool permissions) → ai-studio-permissions; Review (Settings · Provider access detail) → settings-provider — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.zhlfkvqoksnr"></a>

### settings-skills — Settings · Skills

Browse (Spaces · Attach menu) → spaces-attachments; Configure (AI Studio · Agent builder and test) → ai-studio; Browse (Home · Specialists) → home-specialists — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.x9csb2iz4rts"></a>

### settings-providers — Settings · Providers

Review (Settings · Provider access detail) → settings-provider; Open (Settings · AI) → settings-ai; Open (Settings · Privacy) → settings-privacy — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.6hga1er8gcez"></a>

### settings-voice — Settings · Voice

Voice route [Choose Voice Model; Inherit from Active] — [IX-SET-012](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.vl5hjr3v8s2b)

Open (Voice & Vision · Live session) → vision-voice; Review (Voice & Vision · Sources and capture) → vision-session-sources — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.8isf407rno5x"></a>

### settings-security — Settings · Security & Privacy

Open (Settings · General) → settings-security-general; Open (Settings · Permissions & trust) → settings-permissions; Open (Settings · Sandbox) → settings-sandbox; Open (Settings · Privacy) → settings-privacy — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.vnnx1ueie00"></a>

### settings-security-general — Settings · General

Sign out — [IX-SET-004](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.ez7ezccja1wb)

Review (Settings · Permissions & trust) → settings-permissions; Open (Settings · Privacy) → settings-privacy — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ip21pc7mu9jb"></a>

### settings-sandbox — Settings · Sandbox

Review (AI Studio · Agent tool permissions) → ai-studio-permissions; Review (Terminal · Review an AI command) → terminal-command-review; Open (Virtual Device · Virtual machine) → mini-computer; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.v37yl9am3mul"></a>

### settings-profile — Settings · Profile

Change icon; Remove — [IX-SET-003](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.r3sy1wfxdjs5)

input; Add pronouns · optional; Add a job title · optional; Save profile — [IX-SET-002](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.wgqshx7gz019)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.gk61uaak939k"></a>

### settings-subscriptions — Settings · Subscriptions

Manage (Settings · Storage) → settings-storage — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Manage plan — [IX-SET-005](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.xaozm0mrtbx2)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.um82wj8ljbwj"></a>

### settings-account-storage — Settings · Storage

Manage (Settings · Subscriptions) → settings-subscriptions; Open (Settings · Subscriptions) → settings-subscriptions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.vg4kqtjwjme8"></a>

### settings-local-general — Settings · General

Keep Dulche available; Model residency [Unload when idle; Keep selected model loaded]; Always-active AI — [IX-SET-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tkw4oeudcbzb)

Open (Settings · Storage) → settings-storage; Open (Settings · Apps & updates) → settings-apps; Open (Settings · Installed Models) → settings-installed-models; Open (Settings · Notifications) → settings-notifications; Open (Settings · Advanced) → settings-advanced — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.d1vx5dox77ci"></a>

### settings-installed-models — Settings · Installed Models

Manage local models → home-models-installed; Open (Settings · Storage) → settings-storage; Open (Models · Discover) → home-models — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Model residency [Unload when idle; Keep selected model loaded] — [IX-SET-014](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.tkw4oeudcbzb)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.zfbjpdmqdzzh"></a>

### settings-haven-general — Settings · General

Customise (Settings · New Tab) → settings-haven-newtab; Open (Settings · Tabs & address) → settings-haven-tabs-address; Open (Settings · Bookmarks) → settings-haven-bookmarks; Open (Settings · History) → settings-haven-history; Open (Settings · Keyboard Shortcuts) → settings-haven-shortcuts; Open (Settings · Performance) → settings-haven-performance — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.xuys02bryyfx"></a>

### settings-haven-bookmarks — Settings · Bookmarks

Open (Boards · Native page) → boards-editor; Open (Study · Learning dashboard) → spaces-study; Open (Write · Continuous View) → write-continuous; Open (Planner · Week and unscheduled tasks) → planner; Customise (New Tab · Customisation) → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.6l8c7qpvfwrd"></a>

### settings-haven-history — Settings · History

Back; Forward — [IX-HOST-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xu84phecojhm)

Open (Home · Library) → home-library — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.emx642xtprcd"></a>

### settings-haven-shortcuts — Settings · Keyboard Shortcuts

Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.a0ukikwl277m"></a>

### settings-haven-performance — Settings · Performance

Inspect (Settings · Advanced) → settings-advanced; Open (Settings · Installed Models) → settings-installed-models; Open (Settings · Storage) → settings-storage; Customise (New Tab · Customisation) → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.qlpx0vx0leed"></a>

### settings-haven-newtab — Settings · New Tab

Focus; Balanced; More widgets — [IX-HOME-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.m2m667tk1va2)

Neutral background; Horizon background; Paper background — [IX-HOME-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.dbgts97qvvuw)

Clock & dateDevice timeClock & dateDevice time; ShortcutsPinned apps and pagesShortcutsPinned apps and pages; Up nextPlannerUp nextPlanner; Pinned noteBoardsPinned noteBoards; Focus timerNew TabFocus timerNew Tab; Continue workingLibraryContinue workingLibrary; My tasksPlannerMy tasksPlanner; Study progressStudyStudy progressStudy — [IX-HOME-010](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.pbw28rspf33w)

Browse (New Tab · Add a widget) → host-newtab-widgets — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.dubwv2legult"></a>

### settings-haven-tabs-address — Settings · Tabs & address

Find a tab — [IX-HOST-007](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.2lu0erxskeje)

Choose mode; Focus address — [IX-HOST-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.35b5mrzdf5hl)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.l1homrmz3gj3"></a>

### settings-haven-pinned — Settings · Pinned destinations

Open (Boards · Native page) → boards-editor; Open (Study · Learning dashboard) → spaces-study; Open (Write · Continuous View) → write-continuous; Open (Planner · Week and unscheduled tasks) → planner; Customise (New Tab · Customisation) → host-newtab-customise — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.f8rfwxsjymcx"></a>

### settings-haven-shortcut-order — Settings · Shortcut order

Move up; Move down — [IX-HOME-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.6tngmwwryk1s)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.oddxi01gypa7"></a>

### settings-haven-tab-history — Settings · Back & forward

Back; Forward — [IX-HOST-009](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xu84phecojhm)

Open (Home · Library) → home-library — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.reyc6411uir8"></a>

### settings-haven-recent-files — Settings · Recent files

Open (Boards · Native page) → boards-editor; Open (Present · Editor) → present-editor; Open (Write · Continuous View) → write-continuous; Open (Data · Spreadsheet) → data; Open (Canvas · Drawing workspace) → canvas-editor; Open (Frame · Layered composition) → picture; Open (Home · Library) → home-library — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.7wjh7hsdwy8u"></a>

### settings-haven-key-navigation — Settings · Navigation

Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.5g7zw46y5qxn"></a>

### settings-haven-key-menus — Settings · Menus & focus

Open (Settings · Accessibility) → settings-accessibility — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.8qfx4usoqxro"></a>

### settings-haven-services — Settings · Services

Open Local settings (Settings · Advanced) → settings-advanced; Open Local settings (Settings · General) → settings-local-general — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.1cvuk1rxzz5k"></a>

### settings-haven-model-memory — Settings · Models & memory

Open Local settings (Settings · Installed Models) → settings-installed-models; Open Local settings (Settings · Storage) → settings-storage — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.r17i50xjpznl"></a>

### settings-haven-storage-cache — Settings · Storage & cache

Open Local settings (Settings · Storage) → settings-storage; Open Local settings (Settings · Apps & updates) → settings-apps — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.v90k759uw7ui"></a>

### settings-apps-shared — Settings · Shared settings

Read-only; Write — [IX-SET-008](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.f2hdpuo82co9#heading=h.1q3zl7o1kyex)

Open (Settings · Appearance) → settings-appearance; Open (Settings · Layout) → settings-layout; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.f83rg7f7txb8"></a>

### settings-app-spaces — Settings · Spaces

Open app (Spaces · Conversation) → spaces-chat; Open (Spaces · Conversation permissions) → spaces-permissions; Review (Settings · Permissions & trust) → settings-permissions; Open (Settings · Shared settings) → settings-apps-shared — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.67nt7qaymh4t"></a>

### settings-app-boards — Settings · Boards

Open app (Boards · Native page) → boards-editor; Open (Boards · Contextual properties) → boards-properties; Open (Boards · Board-name file menu) → boards-file-menu; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.oikuw4kk1yj3"></a>

### settings-app-write — Settings · Write

Open app (Write · Continuous View) → write-continuous; Open (Write · Page View) → write-editor; Open (Write · Continuous View) → write-continuous; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.44w7g8xfz4lt"></a>

### settings-app-present — Settings · Present

Open app (Present · Editor) → present-editor; Open (Present · Transitions) → present-transitions; Open (Present · Actions graph) → present-actions; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ueugjb3os2fb"></a>

### settings-app-canvas — Settings · Canvas

Open app (Canvas · Drawing workspace) → canvas-editor; Open (Canvas · Pen presets) → canvas-pen-presets; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.1s0sivkoebng"></a>

### settings-app-build — Settings · Code, Sites and Stacks

Open app (Code · Workspace) → build-code; Open (Code · Completion and diagnostics) → build-code-diagnostics; Open (Stacks · Conflict review) → build-stacks; Review (Settings · Permissions & trust) → settings-permissions; Open (Settings · Shared settings) → settings-apps-shared — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.a3xvckayxie8"></a>

### settings-app-data — Settings · Data

Open app (Data · Spreadsheet) → data; Open (Data · Chart insertion and preview) → data-chart; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.l8v4gct44puu"></a>

### settings-app-files — Settings · Files

Open app (Files · Local files and details) → files; Open (Files · Selected file actions) → files-context; Open (Files · Sheet and organisation types) → files-organisation; Review (Settings · Permissions & trust) → settings-permissions; Open (Settings · Shared settings) → settings-apps-shared — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ihc0shnst4j9"></a>

### settings-app-forms — Settings · Forms

Open app (Forms · Assessment builder) → forms; Open (Forms · Respondent preview) → forms-preview; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.2fztoqz9z2h4"></a>

### settings-app-planner — Settings · Planner

Open app (Planner · Week and unscheduled tasks) → planner; Open (Planner · Agenda view) → planner-agenda; Open (Planner · Edit an event) → planner-event; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.w9w8esda93r9"></a>

### settings-app-automations — Settings · Automations

Open app (Automations · Draft graph and activation) → automations; Open (Automations · Schedule trigger) → automation-trigger; Review (Settings · Permissions & trust) → settings-permissions; Open (Settings · Shared settings) → settings-apps-shared — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.37napf5u9y6z"></a>

### settings-app-picture — Settings · Frame

Open app (Frame · Layered composition) → picture; Open (Frame · Export image) → picture-export; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.okol7whxfn5m"></a>

### settings-app-wave — Settings · Wave

Open app (Wave · Audio timeline and mixer) → wave; Open (Wave · Split stems) → wave-stems; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.e286mopdrmix"></a>

### settings-app-motion — Settings · Motion

Open app (Motion · Video editing workspace) → motion; Open (Motion · Render range and destination) → motion-export; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.wezjkf5qn7sf"></a>

### settings-app-connect — Settings · Connect

Open app (Connect · Mail and a reply draft) → connect-mail; Open (Connect · Schedule send review) → connect-schedule; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.kljt76uidx8x"></a>

### settings-app-maps — Settings · Maps

Open app (Maps · Route and place details) → maps; Open (Maps · Route options and stops) → maps-route-options; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.4je1med94acs"></a>

### settings-app-shelf — Settings · Collection

Open app (Collection · Curated launch collection) → shelf; Open (Collection · Add an item) → shelf-add; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.qpzuyyqms4l0"></a>

### settings-app-ai-studio — Settings · AI Studio

Open app (AI Studio · Agent builder and test) → ai-studio; Open (AI Studio · Agent tool permissions) → ai-studio-permissions; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.qd733bqlowmo"></a>

### settings-app-terminal — Settings · Terminal

Open app (Terminal · Command workspace) → terminal; Open (Terminal · Review an AI command) → terminal-command-review; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.ew7vcyb9v1nr"></a>

### settings-app-vision-and-voice — Settings · Voice & Vision

Open app (Voice & Vision · Live session) → vision-voice; Open (Voice & Vision · Sources and capture) → vision-session-sources; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.4m71ahgcum6m"></a>

### settings-app-cards — Settings · Decks

Open app (Decks · Focused review) → cards-review; Open (Decks · Study overview) → cards-study; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.c52ngxk3z4qe"></a>

### settings-app-play — Settings · Play

Open app (Play · Human and Agent quiz match) → play; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.vcjdz7yh7ute"></a>

### settings-app-games — Settings · Games

Open app (Games · Scene and components) → games; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.vpql6fhvonqo"></a>

### settings-app-link — Settings · Flow

Open app (Flow · Continuous Flow document) → link-flow; Open (Flow · Binder overview and detail) → link-binder; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.pdkkcxwouma"></a>

### settings-app-prototype — Settings · Prototype

Open app (Prototype · Interactive spring explorer) → prototype; Open (Prototype · Edit a typed input) → prototype-parameters; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.91u093sdu6s8"></a>

### settings-app-paths — Settings · Story Sequences

Open app (Story Sequences · Branching learning route) → paths; Open (Story Sequences · Edit a route condition) → paths-branch-rule; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.6mnwxx97jb6l"></a>

### settings-app-replay — Settings · Replay

Open app (Replay · Historical checkpoint) → replay; Open (Replay · Inspect a recorded change) → replay-event-detail; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.3vqfpvifqkkm"></a>

### settings-app-mini-computer — Settings · Virtual Device

Open app (Virtual Device · Virtual machine) → mini-computer; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.h3l4nnbkq41n"></a>

### settings-app-admin — Settings · Admin

Open app (9to1 Admin · People and policy scope) → admin; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.m8ekbuyskm41"></a>

### settings-app-launcher — Settings · Launcher

Open app (Launcher · Mobile home) → launcher; Open (Settings · Shared settings) → settings-apps-shared; Review (Settings · Permissions & trust) → settings-permissions — [IX-BASE-013](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.69w01b258ugu)

Open contextual AI; AI access: Read-only; Start voice — [IX-HOST-011](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.20f3efbn4gcd)

<a id="h.a0o0gm5qjkrb"></a>

### host-devtools-html — Haven · HTML inspector

Return to page → settings-app-launcher — [IX-HOST-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.e93bdgq4u1rd)

HTML; CUI; Refresh snapshot; Filter elements; <div class="app-region">; <div class="workspace sc-workspace">; <aside class="sidebar settings-sidebar sn-sidebar">; <main class="main">; Computed; Attributes — [IX-HOST-016](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xfx3vy41nkqj)

Clear console; Console command — [IX-HOST-017](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.qs6igsjxsnax)

<a id="h.mpqg6fegds3d"></a>

### host-devtools-cui — Haven · CUI inspector

Return to page → settings-app-launcher — [IX-HOST-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.e93bdgq4u1rd)

HTML; CUI; Refresh snapshot; Filter elements; Group · app-region; Group · workspace; Sidebar · sidebar; Main · main; Computed; Attributes — [IX-HOST-016](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.xfx3vy41nkqj)

Clear console; Console command — [IX-HOST-017](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.qs6igsjxsnax)

<a id="h.wrqq7ntc7hj5"></a>

### host-browser-history — Haven · History

Clear history; Settings · Launcher/Settings/Launcher → settings-app-launcher; Settings · Admin/Settings/Admin → settings-app-admin; Settings · Virtual Device/Settings/Virtual Device → settings-app-mini-computer; Settings · Replay/Settings/Replay → settings-app-replay; Settings · Story Sequences/Settings/Story Sequences → settings-app-paths; Settings · Prototype/Settings/Prototype → settings-app-prototype; Settings · Flow/Settings/Flow → settings-app-link; Settings · Games/Settings/Games → settings-app-games; Settings · Play/Settings/Play → settings-app-play; Settings · Decks/Settings/Decks → settings-app-cards; Settings · Voice & Vision/Settings/Voice & Vision → settings-app-vision-and-voice; Settings · Terminal/Settings/Terminal → settings-app-terminal; Settings · AI Studio/Settings/AI Studio → settings-app-ai-studio; Settings · Collection/Settings/Collection → settings-app-shelf; Settings · Maps/Settings/Maps → settings-app-maps; Settings · Connect/Settings/Connect → settings-app-connect; Settings · Motion/Settings/Motion → settings-app-motion; Settings · Wave/Settings/Wave → settings-app-wave; Settings · Frame/Settings/Frame → settings-app-picture; Settings · Automations/Settings/Automations → settings-app-automations; Settings · Planner/Settings/Planner → settings-app-planner; Settings · Forms/Settings/Forms → settings-app-forms; Settings · Files/Settings/Files → settings-app-files; Settings · Data/Settings/Data → settings-app-data; Settings · Code, Sites and Stacks/Settings/Code, Sites and Stacks → settings-app-build; Settings · Canvas/Settings/Canvas → settings-app-canvas; Settings · Present/Settings/Present → settings-app-present; Settings · Write/Settings/Write → settings-app-write; Settings · Boards/Settings/Boards → settings-app-boards; Settings · Spaces/Settings/Spaces → settings-app-spaces; Settings · Shared settings/Settings/Shared settings → settings-apps-shared; Settings · Storage & cache/Settings/Storage & cache → settings-haven-storage-cache; Settings · Models & memory/Settings/Models & memory → settings-haven-model-memory; Settings · Services/Settings/Services → settings-haven-services; Settings · Menus & focus/Settings/Menus & focus → settings-haven-key-menus; Settings · Navigation/Settings/Navigation → settings-haven-key-navigation; Settings · Recent files/Settings/Recent files → settings-haven-recent-files; Settings · Back & forward/Settings/Back & forward → settings-haven-tab-history; Settings · Shortcut order/Settings/Shortcut order → settings-haven-shortcut-order; Settings · Pinned destinations/Settings/Pinned destinations → settings-haven-pinned; Settings · Tabs & address/Settings/Tabs & address → settings-haven-tabs-address; Settings · New Tab/Settings/New Tab → settings-haven-newtab; Settings · Performance/Settings/Performance → settings-haven-performance; Settings · Keyboard Shortcuts/Settings/Keyboard Shortcuts → settings-haven-shortcuts; Settings · History/Settings/History → settings-haven-history; Settings · Bookmarks/Settings/Bookmarks → settings-haven-bookmarks; Settings · General/Settings/General → settings-haven-general; Settings · Installed Models/Settings/Installed Models → settings-installed-models — [IX-HOST-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.e93bdgq4u1rd)

<a id="h.vsl2v6bi5agr"></a>

### host-browser-bookmarks — Haven · Bookmarks

New tab → host-newtab — [IX-HOST-015](https://docs.google.com/document/d/1DoTJEvTbrzXWYvFu2XLswItDW1sIYbQ-7Mw7mkHt8Pk/edit?tab=t.j832uc9l5tk#heading=h.e93bdgq4u1rd)
