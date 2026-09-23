# 9-1 surface map

The user clarified that the specification's **21 Workspace + 11 AI = 32 roadmap labels** do **not** denote 32 unique surfaces. Three labels are not independent surfaces: **9-1 App** is another name for Home; **Product Launch** is the actual project launch later; **Custom Harnesses** is a deferred feature within Studio for users to build custom AI harnesses. The supplied 29-name transcription therefore accounts for **19 Workspace + 10 AI = 29 distinct surface entries**. Do not allocate independent surface-worker lanes to the three extra labels.

## 9-1 Workspace — 19 surface entries

| # | Roadmap entry | Current ownership / boundary |
| --- | --- | --- |
| 1 | Write | Write editor and document contracts. |
| 2 | Present | Present surface; lifecycle changes respect the existing approval decision. |
| 3 | Data | Data workbooks and `IDataWorkbookRepository` donor for Maps/Forms. |
| 4 | Boards | Phase 01 CUI app, rich-board contract and HUI projection. |
| 5 | Canvas | Canvas workspace. |
| 6 | Automations | Shared workflow/runtime and app surface. |
| 7 | Develop | Developer tools/workspace; coordinate with Studio when its deferred Custom Harnesses feature is resumed. |
| 8 | Image | **Pictures** product; one lane, not a second Image app. |
| 9 | Audio | **Wave** product; one lane, not a second Audio app. |
| 10 | Video | **Motion** product; one lane, not a second Video app. |
| 11 | Planner | Planner/Plan workspace. |
| 12 | External App Integrations | Shared integration and connection contracts; coordinate with Settings. |
| 13 | Mail | Mail surface and draft persistence. |
| 14 | Browse | Existing engine-neutral Browse surface, Gecko-first target. |
| 15 | Mesh | Trusted-device discovery/sync ownership. |
| 16 | Maps | Existing OSM stack; saved-place persistence uses Data donor. |
| 17 | Forms | Form surface; submissions use Data donor. |
| 18 | Android Launcher | Android Home activity and device-provider lifecycle; requires Android device validation. |
| 19 | Home | Suite Home, app hub/launcher (also called **9-1 App**) and shared platform control surface. |

## 9-1 AI — 10 surface entries

| # | Roadmap entry | Current ownership / boundary |
| --- | --- | --- |
| 1 | Dashboard | Home dashboard surface; distinct ownership where its files can be isolated. |
| 2 | Spaces | Spaces/Chat state and UI. |
| 3 | Generative UI | Generative UI → CUI conversion, with shared capability/security contracts. |
| 4 | Voice | Home/shared Voice lifecycle and device providers. |
| 5 | Background Learning | Home/shared local-first knowledge and scheduling. |
| 6 | Agents | Agents catalogue and task-runtime integration. |
| 7 | Overlay | Home/shared floating workspace, host and scene. |
| 8 | Translate | Translation surface and service. |
| 9 | Play | Play surface. |
| 10 | Terminal | Terminal experience and shared PTY contract; coordinate with Develop. |

**Three additional roadmap labels, zero additional surfaces:** `9-1 App` → **Home** alias; `Product Launch` → later actual project launch milestone; `Custom Harnesses` → **deferred Studio feature** for user-built AI harnesses. Leave the Custom Harnesses implementation for later.

**Related programme item outside this roadmap transcription:** **Files** is a separately required app phase (05) in the supplied primary sequence and has source under `9to1 Workspace/Files`. Account for it in programme planning without conflating it with a roadmap label.

## Surface-wave admission

Framework-first convergence and explicit file/contract ownership precede parallel surface work. Assign one implementation lane only for an entry with outstanding, independently developable work, a bounded file set, focused build/test acceptance and a safe isolated checkout/worktree. Home-owned AI entries may have separate lanes only where they avoid shared-file collisions. Shared CUI/framework changes have one owner; other lanes report blockers. The three extra labels do not get surface lanes. Refill free slots from the remaining queue; do not create lanes solely to attain a count.

All child workers must be **explicitly configured and verified as GPT-6 Luna**. The current OpenCode child-agent tool exposes no model-selection or model-inspection parameter; until that capability is available and verified, do not spawn a worker and claim Luna compliance. GPT-6 Sol can continue direct framework and integration work.
