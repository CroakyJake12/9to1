# Platform, app registry and host integration — Launch Screens; Distinguish screen purposes (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0325 · v2 lines 1791–1796

A model may cross an app boundary only when that action is authorised by user intent: explicit @ invocation, an explicit instruction naming the target capability/app, or an unambiguous requested outcome whose canonical owner is another 9to1 app. Implied intent is semantic, not opportunistic; unrelated app access remains out of scope.
When a target 9to1 app owns the requested artifact/action, the originating model MUST use the target app's typed API/shared semantic action contract and MAY ask Home to launch/reveal the target app so the user can observe progress. For example, a request to create a presentation routes to Present and creates/edits a canonical Present artifact rather than fabricating a slide-like object in the originating app.
Computer Use MUST NOT be selected automatically as a fallback when typed app execution is unavailable, inconvenient or fails. It is usable only after explicit @Computer Use invocation and remains subject to the Computer Use eligibility, permission, game-exclusion and Autoreview rules.
Model-driven typed edits MAY be visualised in the target app with live object motion, selection highlights, text insertion and a synthetic/virtual cursor. This visualisation is not evidence that Dulche is literally controlling the pointer; the typed action/result remains authoritative and the synthetic cursor MUST be visually distinguishable from the user's actual input cursor.
Launch Screens
A launch/home screen is the app’s distinct start experience: the equivalent of the modern Microsoft Word start page shown in the reference image, not simply an empty editor or project view, nor a header, banner or introductory section above the working app. It appears when the app is opened without an existing artifact/session to restore, and whenever the user deliberately returns to the app’s Home/start surface. The following requirements clarify the existing launch-screen contract; they do not add an unrelated startup delay, alter the four-part delivery plan or replace any product-specific capability.

### V2-B0326 · v2 lines 1799–1804

LAUNCH-001 — Distinguish screen purposes. An app’s launch/home screen is its functional starting place for choosing or creating work; it is not the platform Home dashboard. A temporary startup/loading splash, first-run onboarding, launch/home, backstage/file menu and active editor/workspace are different experiences and MUST retain their specified purposes. A loading logo or progress page does not satisfy the launch-screen requirement. Do not invent extra screens where none are specified or turn every toolbar, inspector or embedded panel into a separate page. Preserve the existing first-run and backstage contracts.
LAUNCH-002 — Independent composition and app identity. Each required launch screen MUST have its own purposeful layout, hierarchy, recognisable app identity and the real app-specific entry points listed below or in its product section. Its primary task is choosing/creating/opening work; the editor’s primary task is working on the active artifact. Shared CUI themes, shell elements and reusable launch components are encouraged, but changing only the heading/logo on an otherwise unchanged editor or generic command dashboard is insufficient. A hero, Recent strip, welcome card or collapsible introduction attached above the editor is not a launch screen. The start experience must be composed for the available work region, not merely reserve a top strip while the unused editor remains below it.
LAUNCH-003 — Explicit active-view ownership. Model launch/home and the active workspace as distinct CUI view states with explicit navigation and activation. They may reuse the same window, app shell, engine and canonical session; separate processes or OS windows are not required. Within the same active work region, show and activate the appropriate experience rather than vertically stacking both. An inactive editor must not occupy launch layout space or receive pointer input, document shortcuts, keyboard focus or accessibility navigation; inactive launch-only controls must likewise not remain attached to the editor. Hiding a screen by opacity, covering it with a banner or scrolling it off-screen while it remains interactive is failure. Shared chrome explicitly required by the specification may remain. Separate windows/tabs and intentionally specified embedded editors retain their own active-view scopes.
LAUNCH-004 — Real transitions without data loss. Create, Open, Import and applicable template/recent/shared actions MUST establish or resolve the real authorised canonical artifact/session and transition to its functioning workspace, not just change the page heading or reveal an already-stacked test editor. Cancellation or failure leaves a coherent recoverable view with an actionable error and does not fabricate successful entry or discard existing work. Direct artifact opening and supported session restoration go to the requested/restored workspace without forcing a detour through the launch screen. Deliberate return to the app’s start surface and resumption of work preserve canonical identity, unsaved content, undo history and applicable selection/view/session state according to the existing lifecycle and recovery contracts. Navigation alone is not permission to close, delete, reset or silently save over content. A genuine close/discard operation retains its existing confirmation and durability rules. Separating views does not require destroying the retained engine/session, adding extra clicks to direct-open flows or imposing artificial splash delays.
LAUNCH-005 — Shared implementation, complete coverage. Implement reusable canonical CUI navigation, activation and launch composition through the existing shared runtime, with each app binding its own real storage/model, capabilities and identity. Do not create a competing navigation framework or duplicate document stores. Inventory every application for which any specification section requires a launch/home screen, including applicable native and web counterparts, and verify its real shipping entry point. Reuse must preserve product-specific workflows, rather than flattening every app into the same dashboard. A separate source file, route name, boolean flag or standalone sample is not evidence that the actual product presents distinct screens. This is a required product-boundary correction at each applicable RCVP/Full stage, not optional visual polish.
LAUNCH-006 — Runtime and transition acceptance. Independently inspect the exact launched app/artifact under APP-AC. Cover launch without a restorable artifact/session; real empty and populated start states; applicable Create/Open/Import/template/recent actions into the workspace; direct artifact opening; supported restoration; deliberate return to start and resumption; and cancelled/failed entry. Capture start-screen and workspace screenshots separately and inspect the transitions, active command scope, pointer handling, focus order and accessibility state. Verify that document shortcuts cannot modify a retained inactive document while the user types or navigates on the launch screen, and that returning preserves the required content/session state. Test the declared supported window sizes, themes and native/web surfaces through the existing focused-smoke and release matrices. Include a regression rejection case for launch content stacked above an editor, including an editor reachable only by scrolling or invisible but still interactive. Merely finding expected labels/buttons or passing a route/unit test does not pass. Missing screen identity, an active-view leak, data loss or a header masquerading as a launch page blocks the relevant application acceptance.

### V2-B0327 · v2 lines 1807–1807

For every app listed below, the launch screen MUST implement each capability in the following list when that capability exists for the product. If a capability is not applicable, the product section MUST explicitly state the exclusion and reason rather than silently omit it:

### V2-B0328 · v2 lines 1810–1816

* Create a blank document/project/artifact.
* Templates, starter projects and a way to browse more templates where the app supports them.
* Recent items, with useful location/path and modified-state information.
* Favourites/pinned items.
* Shared with Me or equivalent collaborative content where applicable.
* Search across the app’s relevant files/projects.
* Open/import entry points for existing files/projects and supported external formats.

### V2-B0329 · v2 lines 1819–1819

The launch screen must use CUI and the app’s real storage/project model. Recent, favourite, shared and template entries must be functional data-backed surfaces rather than static mock content. Apps may add app-specific quick actions, onboarding/tours or account/storage indicators where useful.

### V2-B0330 · v2 lines 1822–1831

The following apps MUST include a launch/home screen conforming to LAUNCH-001–006. The same contract also applies wherever another product section requires a launch/home screen, including later additions to the application roster; this list is not an exemption for those products:
* Write
* Present
* Data
* Canvas
* Boards
* Picture
* Wave
* Motion
* Forms

### V2-B0331 · v2 lines 1834–1836

* AI Studio
Web Apps
Cakemods.com MUST host a web version of every 9-1 Workspace/AI product that is not explicitly listed as web-exempt. Web surfaces MUST share the same CAKE ID identity, canonical artifact formats, domain APIs, permission model and underlying server-side entities as desktop equivalents. Browser-specific UI/layout MAY differ, but it MUST NOT create a separate incompatible product model.

### V2-B0332 · v2 lines 1841–1841

The default rule is to provide a web version whenever the product’s core value can sensibly work in a browser. Browser-specific capability limits are acceptable when they are explicit and the useful core product remains intact; a single desktop-only feature is not by itself a reason to omit an otherwise useful web app.

### V2-B0333 · v2 lines 1844–1844

Web exemption is permitted only when the product’s core function requires infrastructure, privileged device access or hosted compute that CAKE does not currently provide. Each exemption MUST state the blocking capability/infrastructure. Exemptions MUST be re-evaluated when that blocker is removed; exemption is not a permanent product classification.

### V2-B0334 · v2 lines 1849–1851

Current exemption:
* Mini Computer — a useful web version would require hosted virtualisation/server compute that CAKE does not currently have capacity to provide.
* Browser - Having a browser inside a browser is silly.

### V2-B0335 · v2 lines 1854–1854

Mini Computer may gain a hosted web mode in the future as a 9to1 subscription capability if sufficient server compute is available. The same principle applies to future apps whose meaningful web implementation requires paid hosted compute.
