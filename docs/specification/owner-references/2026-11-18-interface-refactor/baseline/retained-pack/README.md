# 9to1 Development Specification v3

This pack contains the current app contracts, exact reference designs and the retained technical specification. The illustrated Word specification and standalone Spaces Word document are provided separately.

## Use one module at a time

1. Open `modules/<module>.md` for its prerequisites, overview, current rules and exact designs.
2. Use that module's technical reading map to load the smaller chapters needed for the task.
3. Use `modules/<module>.complete.md` when you need the module's entire retained technical contract in one copyable file.
4. Read prerequisite contracts as shared interfaces; a dependency does not require every dependent app UI to be completed.

For Spaces, start with `modules/spaces.md`. Its prerequisites cover CUI, Dulche, Node Editor, the Assistants system and the canonical identity, permission and persistence interfaces. The chapter includes generated UI, thinking, subagents, live artifacts and generated files.

The current master document is organised by the same modules. Its images and the PNGs here come from the same offline atlas source.

## Current authority

Latest owner revisions and the v3 override ledger control within their stated scope. Only presentation slides 1–16 are authoritative. The older Study behaviour is retained inside the current wrapper. The eight current themes are Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western. Cinematic is labelled legacy compatibility.

The `source/` directory preserves the baseline and incorporated documents. `catalog/` accounts for every source block; `history/` preserves replaced wording and historical process instructions. These directories are provenance and technical reference, not a command to read all material before every task.

## Module index

- [Authority, scope and implementation reading map](modules/authority.md) — 0 design states; 4 technical chapters.
- [Platform, app registry and host integration](modules/platform.md) — 0 design states; 4 technical chapters.
- [CUI, themes and shared interaction system](modules/cui.md) — 5 design states; 4 technical chapters.
- [Account, identity and resource entitlements](modules/identity.md) — 2 design states; 2 technical chapters.
- [Privacy, data boundaries and context isolation](modules/privacy.md) — 0 design states; 1 technical chapters.
- [Permissions, approvals and execution scope](modules/permissions.md) — 0 design states; 1 technical chapters.
- [Artifacts, local persistence and recovery](modules/persistence.md) — 0 design states; 1 technical chapters.
- [Shared productivity engine and canonical embeds](modules/productivity-engine.md) — 0 design states; 1 technical chapters.
- [Node Editor — shared graph framework](modules/node-editor.md) — 0 design states; 1 technical chapters.
- [Shared AI controls and artifact collaboration](modules/shared-ai.md) — 0 design states; 1 technical chapters.
- [Dulche runtime, requests and execution services](modules/dulche.md) — 0 design states; 4 technical chapters.
- [Models, discovery and universal model routing](modules/models.md) — 4 design states; 3 technical chapters.
- [Den, memory and knowledge banks](modules/den-memory.md) — 0 design states; 2 technical chapters.
- [Subagents, delegation and task trees](modules/subagents.md) — 0 design states; 1 technical chapters.
- [Global Assistants, Specialists and collaborators](modules/assistants.md) — 4 design states; 3 technical chapters.
- [Generative UI and live artifact output](modules/generative-ui.md) — 0 design states; 1 technical chapters.
- [Skills, Plugins, MCP and capability packages](modules/plugins-mcp.md) — 0 design states; 1 technical chapters.
- [External integrations and provider connections](modules/integrations.md) — 0 design states; 1 technical chapters.
- [Go, universal search and entity navigation](modules/go-search.md) — 0 design states; 1 technical chapters.
- [Cloud services, infrastructure and billing contracts](modules/cloud-services.md) — 0 design states; 3 technical chapters.
- [Home — Apps, Dashboard and global systems](modules/home.md) — 4 design states; 2 technical chapters.
- [Settings — complete page family](modules/settings.md) — 12 design states; 1 technical chapters.
- [Notifications and ecosystem activity](modules/notifications.md) — 1 design states; 1 technical chapters.
- [Mesh devices, replication and remote capabilities](modules/mesh.md) — 1 design states; 1 technical chapters.
- [Browse and the native/browser tab host](modules/browse.md) — 4 design states; 3 technical chapters.
- [Admin — organisation scope and policy](modules/admin.md) — 1 design states; 1 technical chapters.
- [Files and ten organisational folder views](modules/files.md) — 4 design states; 4 technical chapters.
- [Connect — conversations, contacts and Mail](modules/connect.md) — 4 design states; 3 technical chapters.
- [Spaces — conversations, workspaces and custom Space Builder](modules/spaces.md) — 31 design states; 11 technical chapters.
- [Write — document authoring](modules/write.md) — 6 design states; 4 technical chapters.
- [Present — slide authoring and presentation](modules/present.md) — 9 design states; 3 technical chapters.
- [Boards — seamless pages, blocks and ink](modules/boards.md) — 9 design states; 7 technical chapters.
- [Canvas — drawing, objects and semantic composition](modules/canvas.md) — 7 design states; 3 technical chapters.
- [Data — worksheets, tables and explicit AI approval](modules/data.md) — 4 design states; 3 technical chapters.
- [Planner — calendars, burrows and tasks](modules/planner.md) — 3 design states; 2 technical chapters.
- [Automations — graph authoring and durable runs](modules/automations.md) — 2 design states; 3 technical chapters.
- [Forms — builder, logic and responses](modules/forms.md) — 2 design states; 2 technical chapters.
- [Cards — sets, editing and study review](modules/cards.md) — 3 design states; 1 technical chapters.
- [Picture — image and layer editing](modules/picture.md) — 3 design states; 2 technical chapters.
- [Wave — multitrack audio production](modules/wave.md) — 3 design states; 2 technical chapters.
- [Motion — video, composition and animation](modules/motion.md) — 3 design states; 2 technical chapters.
- [Build Code — development environment](modules/build-code.md) — 2 design states; 3 technical chapters.
- [Build Sites — responsive site authoring](modules/build-sites.md) — 2 design states; 1 technical chapters.
- [Build Stacks — Branch, Twig and Leaf source control](modules/build-stacks.md) — 2 design states; 3 technical chapters.
- [AI Studio — harnesses, agents and evaluations](modules/ai-studio.md) — 2 design states; 2 technical chapters.
- [Terminal — Command and AI modes](modules/terminal.md) — 2 design states; 2 technical chapters.
- [Mini Computer — VM and remote desktop control](modules/mini-computer.md) — 1 design states; 1 technical chapters.
- [Games — scene and runtime development](modules/games.md) — 1 design states; 2 technical chapters.
- [Play — structured games and competitions](modules/play.md) — 1 design states; 1 technical chapters.
- [Vision & Voice — shared live session system](modules/vision-voice.md) — 2 design states; 2 technical chapters.
- [Dulche Translate service](modules/dulche-translate.md) — 0 design states; 1 technical chapters.
- [Maps — search, places and routes](modules/maps.md) — 2 design states; 2 technical chapters.
- [Shelf — cross-app library and collections](modules/shelf.md) — 2 design states; 1 technical chapters.
- [Launcher — mobile and OS home surface](modules/launcher.md) — 1 design states; 1 technical chapters.
- [9to1OS — desktop, Go and native system surfaces](modules/os.md) — 1 design states; 2 technical chapters.
- [Link — Flow and Binder](modules/link.md) — 4 design states; 1 technical chapters.
- [Prototype — Design, Logic and Run](modules/prototype.md) — 2 design states; 1 technical chapters.
- [Paths — branching guided experiences](modules/paths.md) — 2 design states; 1 technical chapters.
- [Replay — semantic history and inspection](modules/replay.md) — 2 design states; 1 technical chapters.
- [Variants — integrated comparison and merge](modules/variants.md) — 1 design states; 1 technical chapters.
- [Delivery governance and historical process](modules/delivery.md) — 0 design states; 2 technical chapters.
- [Cross-app acceptance and evidence](modules/acceptance.md) — 0 design states; 1 technical chapters.

## Reading boundaries

The current primary chapters are concise entry contracts. Detailed source requirements, data models, APIs, recovery semantics and donor parity remain in the named technical chapters. Source accounting does not imply that every historical sentence has been independently rewritten or that production implementation and acceptance tests are complete.
