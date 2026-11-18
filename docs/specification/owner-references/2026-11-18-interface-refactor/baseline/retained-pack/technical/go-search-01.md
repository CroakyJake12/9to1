# Go, universal search and entity navigation — Service registry and compatibility handshake; Default surface (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0509 · v2 lines 2533–2533

Home owns/co-ordinates the shared searchable-entity index consumed by Go and Home Library/Search. Apps MUST publish supported searchable entity metadata through a typed indexing contract rather than Home scraping arbitrary app databases.

### V2-B0510 · v2 lines 2536–2536

Search records MUST include stable source app, EntityID/type, title/display metadata, search/index text or vector references as permitted, modification state and current access-scope metadata. Search results MUST re-check current authorisation before exposing sensitive content or opening an entity; stale index access MUST NOT bypass revoked permissions.

### V2-B0511 · v2 lines 2539–2539

On deletion/revocation, apps MUST be able to invalidate/remove affected index records. Home may maintain rebuildable indexes/caches, but the index MUST NOT become the only canonical copy of source content.

### V2-B0512 · v2 lines 2542–2542

Service registry and compatibility handshake

### V2-B2153 · v2 lines 12439–12471

Go
Go is the universal Start/search/action surface for 9to1-OS. It combines the roles of a Windows-style Start menu, system-wide search, application launcher and contextual quick-action surface. Go is an OS primitive, not a separate heavyweight application, and the default taskbar places Go at the left/start position.
Default surface
Opening Go without a query presents a customisable home surface that can contain Pinned items, Recent items, Suggested/contextual items, All Apps, recent files/projects, quick system actions, account/session controls and power controls. Users can reorder, hide, resize or regroup supported sections and choose compact search-first, Start-menu-like or larger dashboard-oriented layouts.
All Apps is a first-class Go view containing installed applications/programs available to the current user/profile. It is separate from the Android-style Launcher app drawer. Go is the primary application-launcher replacement for the 9to1-OS desktop shell.
Search and AI modes
Go provides a Search / AI switcher. Search is deterministic universal search/navigation; AI routes natural-language intent through Dulche while using the same canonical Go providers and typed OS/app actions.
Examples of AI-mode requests include opening the project the user was working on, finding the setting that controls display scaling, launching a group of development tools or locating notes by context. AI mode MUST NOT fabricate applications, files, settings or entities that do not exist and MUST surface ambiguity when multiple plausible targets remain.
Universal provider model
Applications, frameworks and authorised extensions register typed GoProviders rather than requiring Go to hard-code product-specific databases. Providers may expose search, suggestions, recent items, result categories, previews, aliases, quick actions, indexing/change notifications and query-time-only results.
Go may search applications, files/folders, settings, people/contacts, LLM Spaces/conversations where authorised, Boards, Planner tasks/events, Maps places, Shelf items/collections, Stack repositories/branches, Dev projects, browser history/bookmarks where enabled, installed tools, Plugins/extensions and registered system actions.
Canonical identity
Go returns references to canonical entities owned by their source application/framework. Its local index may cache identifiers, search fields, aliases, presentation metadata, permissions and freshness state, but MUST NOT create a competing canonical copy of a file, document, project, place, person or other result merely because it is searchable.
Ranking and context
Ranking may combine exact/prefix/fuzzy match, aliases, keywords, recency, frequency, pins/favourites, current app, active project, current canonical object, Desktop Space, user-enabled Background Learning and provider relevance. Context can change ranking and available actions but never bypass access controls or change the canonical identity of a result.
Typed actions
Results can expose owner-provided typed actions such as Open, Reveal, Share, Navigate, New window, Pin, Create, Toggle, Open with or app-specific commands. Go may invoke these actions directly where appropriate but MUST NOT reproduce another app's private business logic.
Consequential actions remain subject to the underlying OS/app permission and confirmation requirements. Go and Dulche cannot use a quick-action surface to bypass security prompts.
Filters and previews
Go supports optional user-selectable filters/categories such as Apps, Files, People, Settings, Places, Projects and Actions plus provider-defined categories where useful. Results may expose compact CUI previews supplied from canonical provider data.
Indexing, privacy and freshness
Providers declare whether they are locally indexed, queried live or hybrid. They can push changed entities, invalidate deleted entities and request re-indexing. Search results MUST be permission-filtered before display, including for restricted folders/projects, managed user profiles and lock-screen contexts.
Users can disable sensitive providers/categories such as browser history, message contents or private project indexing. Stale/offline remote results must be marked with their freshness state and invalid/deleted results removed rather than accumulating as zombie entries.
Scoped reuse
The same Go framework may be embedded as a scoped picker/search surface in Launcher, Files, Shelf and other apps, and Dulche may query Go for authorised entity discovery. These are scoped views over the same provider contracts, not separate incompatible search engines.
Performance and failure isolation
Opening Go should be effectively immediate. Local providers should stream results incrementally; slow remote providers MUST NOT block applications, local files, settings or cached results. Failure of one provider is isolated and reported only for that provider.
Go acceptance requirements
* Go acts as the primary 9to1-OS Start menu, universal search and quick-action surface, with Go at the default left/start taskbar position.
* Pinned, Recent, Suggested and All Apps views are first-class and deeply customisable.
* Search and AI modes use the same canonical entity/provider/action contracts; AI mode cannot hallucinate unavailable entities.
* Search results remain permission-filtered canonical references rather than duplicate Go-owned objects.
* Provider failures and remote latency do not block local search/launch functionality.
