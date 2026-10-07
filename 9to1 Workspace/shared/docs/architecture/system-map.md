# System Map

Entry points for the major production systems. "Start reading" names the file
that best explains the design.

| System | Owns / does | Start reading | Key collaborators |
|---|---|---|---|
| Shell & launch routing | Window shell, tabs, `LaunchAppAsync` route table, contextual actions | `src/Haven.Desktop/Interface/Shell/MainView.axaml.cs`, `HavenAppRoutePolicy` | Mode registry, TopRail, page factories |
| Shelf (domain foundation) | Typed canonical launch targets, user collections, membership, deterministic lookup policy | `src/Haven.Core/Shelf/ShelfModels.cs`, `src/Haven.Application/Shelf/ShelfLibraryPolicy.cs` | Target-owning platform and app services; persistence and UI integration remain pending |
| HavenUI framework | `.hui` markup, scene tree, layout, draw commands, input, animations | `src/Haven.UI/Markup/HavenMarkupParser.cs`, `Rendering/HavenSceneRenderer.cs`, README in project | Backend bridge (Desktop) |
| HUI↔Avalonia backend | Renders scenes into an Avalonia control; resolves images/fonts/tokens | `src/Haven.Desktop/HavenUI/Backend/HavenSceneControl.cs` | `HavenAvaloniaThemeResolver`, `HavenDesktopImageResolver`, `HavenUiFont` |
| Prefabs & DynamicUI | Reusable `.hui` components with code-behind; data-driven list rows | `src/Haven.UI/Components/Prefab/Prefab.cs`, `Components/DynamicUI/DynamicUI.cs`; examples in `src/Haven.Desktop/Prefabs`, `DynamicUI` | Scene pages (Chat, Imagine, Studio) |
| Themes & personalisation | 5 themes × 4 appearances, accent override, fonts, avatars | `src/Haven.Desktop/HavenUI/Tokens/HavenThemeCatalog.cs`, `Controls/SurfacePaletteCatalog.cs`, `Services/UserPreferencesService.cs` | `HavenUiResourceApplier`, Settings scene |
| Tidal background | Surface-following animated gradient backdrop | `src/Haven.Desktop/Controls/TidalBackground.*` | SurfacePaletteCatalog per surface |
| Chat | Conversation streaming, tools, attachments; HUI transcript via DynamicUI | `src/Haven.Desktop/Views/Pages/Chat/NewChatPage*.cs`, `ChatHavenScene.cs` | Application chat orchestration, Ollama client |
| Browser | Embedded browsing, safety policies, private profiles | `src/Haven.Desktop/Views/Pages/Browser/BrowserPage.axaml.cs` (+ scene), `src/Haven.Browser` | Infrastructure WebView integration |
| Documents (Write/Data/Canvas/Present) | App workspaces hosted by HUI scenes; Write has an experimental platform-neutral document-engine boundary for a future Linux LibreOfficeKit helper | `src/Haven.Desktop/Views/Pages/{Write,Data,Canvas,Present}`, `docs/architecture/write-libreoffice-engine.md` | Core document models, `IWriteDocumentEngine` |
| Automations & scheduler | Scheduled actions, worker leases | `src/Haven.Application/Automations/*` | AutomationWorker host process |
| GenUI (generated UI) | Model-generated surfaces from validated contracts | `src/Haven.Core/GenerativeUi/*`, `src/Haven.Desktop/HavenUI/GenerativeUi/*` | GenUI rules (`docs/GENUI_RULES.md`) |
| Projector (Android display) | Phone-as-display experiences, execution parity | `src/Haven.Application/Projector/ProjectorExperienceCatalog.cs`, `src/Haven.Android/Projector/*` | GenUI instance store |
| Mesh (device sharing) | Discovery + trusted-peer sync | `src/Haven.Application/Mesh/MeshCoordinator.*.cs` | Security rules |
| Model runtime | Ollama discovery/wake, model registry, residency | `src/Haven.Infrastructure` Ollama clients; `OllamaWakeService` | Preferences (`AutoWakeOllama`, `AlwaysLoaded`) |
| Dulche inference engines | Model-bearing startup and one dispatcher over the existing managed request/frame/context owner | `../../9to1 Models/Dulche Alpha/InferenceEngines/InferenceEngineDispatcher.cs`, `src/Haven.Infrastructure/Models/ManagedDulcheInferenceComposition.cs` | Same coordinator-issued admission, observed llama.cpp endpoint, protected Strata worker/model source and actual whole engine close |
| Persistence | Forward-only SQLite migrations, repositories | `src/Haven.Infrastructure` SQLite services | `docs/ARCHITECTURE_RULES.md` |
| Model governance | Fallback order, per-model personality/nicknames (null nickname = inherit shared), permission policy; fallback switches publish `ExecutionActionType.ModelFallback` events | `src/Haven.Application/ModelGovernance/ModelGovernance.cs`, `src/Haven.Infrastructure/Providers/ResilientProviderRoutingModelClient.cs` | Versioned settings (`models.*`), `ChatSessionService`, Settings governance |
| Actions & plan approval | Default-provider cascade (explicit → attached App → approved plan → project/space → user default incl. Always Ask → sole available → ask), suggested-action heuristics, `<haven-plan>` Follow/Tweak/Reject artifacts | `src/Haven.Application/Actions/ProviderResolution.cs`, `src/Haven.Core/Models/ActionConcepts.cs` | Chat orchestration, planner |
| Agentic safety | SQLite-backed checkpoints (Git-independent), restore-plan replay, undo-last-action, agent.md/AGENTS.md discovery with depth cap and root-first merge | `src/Haven.Application/Safety/CheckpointService.cs`, `src/Haven.Application/Safety/AgentInstructions.cs`, `src/Haven.Infrastructure/Workspace/WorkspaceCheckpointRestore.cs` | Workspace versions, Action Graph |
| Extension tool runtimes | Native-plugin capabilities (`native-plugin:{package}:{capability}`) and MCP tools execute inside the shared tool loop; MCP connection management in Settings | `src/Haven.Application/Extensions/PluginToolRuntime.cs`, `src/Haven.Application/ExternalConnections/McpToolRuntime.cs`, `src/Haven.Infrastructure/ExternalConnections/McpConnectionClient.cs` | `ChatSessionService`, Settings connections |
| Evaluation & judging | Side-by-side dual-model runs (Compare/Critique) and LLM-as-judge scoring that returns null instead of a fabricated score | `src/Haven.Application/DualModel/DualModelService.cs`, `src/Haven.Application/Evaluation/JudgeService.cs` | Testing Labs adapter `src/Haven.Desktop/Services/TrainingJudgeAdapter.cs` |
| Context & memory | Persisted per-conversation context cards (compact summaries protected); Learn Me injection capped by personality Memory References level | `src/Haven.Application/Knowledge/MemoryInjection.cs`; context entries in `ConversationRepository` | Haven Library storage |
| Maps app | Switchable OSM stack: raster tiles + Nominatim geocoding + OSRM routing under mandatory provider terms (attribution, UA, ≥7-day cache, viewport-only fetch, HTTPS; Nominatim ≤1 req/s) | `src/Haven.Desktop/Views/Pages/Maps/*`, `src/Haven.Infrastructure/Maps/OpenStreetMapService.cs`, `OsmRasterTileSource.cs`, `OsrmRoutingService.cs` | `IMapService`, `ITileSource`, `MapsAttribution` |
| Updates | Source-aware orchestration: Store-managed installs never download binaries; direct installs stage packages pending restart and say so | `src/Haven.Infrastructure/Updates/UpdateOrchestrator.cs` | `IUpdateProvider` implementations, versioned settings (`updates.preferences.v1`) |
| Spaces | Built-in persona workspaces (General/Study/Shopping/Research/Agent) routed onto Chat/Tasks storage by kind | `src/Haven.Application/Spaces/SpaceRegistry.cs`, `src/Haven.Desktop/Services/SpaceLaunchPolicy.cs` | Versioned settings, launch routing |
| Canonical Task/Run | Durable Task/context/run identity, revision CAS, typed action acceptance and original-provider custody | `src/Haven.Application/Execution/TaskExecutionCoordinator.cs`, `src/Haven.Core/Execution/TaskCoordinationModels.cs` | `ITaskExecutionRepository`, original runtime/tool owners, fresh Task actor authority |
| Canonical process shutdown | Seal the configured business cohort, join its actual request loops, business producers and frames before provider disposal | `src/Haven.Application/Execution/TaskRunCanonicalProcessRetirementOwner.cs`, `src/Haven.Desktop/App.OriginalCanonicalProcess.cs` | Same coordinator, Agent service, authority and frame owner; process-only Desktop adapter and App shutdown sequence |
| Spaces task controls | Same-task dashboard/widget, steering and source-issued run-control observations | `../Spaces/Source/Tasks/SpaceTaskWorkspaceService.cs`, `src/Haven.Desktop/Views/Pages/Tasks/SpaceTasksDashboardPage.cs` | Same `SpaceRegistry`, coordinator, supplied native readiness; controls require genuine private owner ports |
| Dev in Spaces | Resolve/open the same durable workspace/project/root and execute typed actions under the same Task/Run | `../Dev/DeveloperTaskWorkspaceService.cs`, `src/Haven.Desktop/Views/Pages/Development/DeveloperProjectWorkbenchPage.cs` | `IDeveloperWorkspaceStore`, canonical binding, Workspace tool owner/runtime, original Files identity resolver |

When adding a system: register it here with one line and a start-reading path.

Process shutdown captures the canonical retirement adapter before later startup callbacks. After the whole borrower preflight, App requests this owner first, requests every other borrower, and independently joins their actual close tasks before disposing the provider. Failed startup retains and drains any already acquired canonical owner. A view or observation lease never requests this process retirement. See [Agent observation retirement](agent-observation-retirement.md) for the separate presentation boundary.

These source connections require the configured original services. They do not establish successful runtime shutdown, installed Home authority, native readiness or model-backed recovery. The initial Tasks chat stream still needs its separate source-issued business host/event observation connection; process retirement does not convert page cancellation into observation detachment.

The optional Dulche engine composition is constructed without native startup. Model-bearing startup requires the same current coordinator-issued attempt, while a taskless model-use authorization source remains unavailable. Strata execution additionally requires genuine protected installed-worker and Safetensors model leases and actual hardware/build observations. Expected file hashes, route preferences and compatibility metadata do not issue those leases. The source bridge and compiler controls do not establish native execution, GPU support, runtime readiness or artifact-pinned request dispatch; the existing managed adapter still refuses a nonnull artifact revision without its own residency witness.

The Linux local Task console can explicitly configure one session-only developer Strata artifact tuple through `strata-select <nonsecret JSON>`. `HomeApprovedStrataDeveloperArtifactSource` supplies actual read borrows only after the same current Task/model admission and an individual high-risk Home Accept over the complete artifact/task/run/attempt tuple. The original Home store/profile/claim is checked on each finite use; busy state or changed/revoked intent refuses rather than reentering a writer. No Home lock spans artifact/native work. This does not enrol a publisher, establish system installation or mark Strata Ready. Kernel immutable/fs-verity files and checkpoint protection, complete hashes/inventory, Safetensors parsing, independent build inventory and actual CUDA observations remain mandatory in the existing native source. Build support stays absent unless a separately signed, worker-bound inventory is supplied and its explicit developer public key is included in that exact Home review; requirements never generate capability. Only one admitted artifact tuple is supported per host process; choosing another tuple requires draining and restarting that host, followed by a new individual review. Configuration or approval is not restored from paths, hashes or flags on restart. The registered native workflow remains unverified until actual protected artifacts, eligible current model catalogue/admission and supported CUDA hardware execute it.

## Windows Desktop process-owned Home

`App.OriginalWindowsHome.cs` constructs the actual Windows Home producer over the maintained app paths and OS principal. `WindowsHomeDomainServiceCollectionExtensions.cs` registers the SAME store/profile/permissions/resource/ownership/broker tuple and precreated Files owners into Desktop. The original Files/native Dev factories reuse that tuple; descriptor registration creates no Files root, project, permission or installed peer.

App retains and starts the original Home before shell initialization. Its current shutdown owner preflights Home together with the borrower cohort and closes Home only after business borrowers and actual native windows settle. Failed-startup cleanup keeps Home alive when a borrower remains unresolved. CAKE ID `sub` is independent of the local OS-profile authority. Protected installed-peer admission remains unavailable until an actual approved verifier is supplied. The native permission-presentation consumer and authenticated account UI are separate reviewed composition cohorts.

## Windows CAKE ID account presentation

Desktop links the canonical `apps/Web/Accounts/AccountBrowserBindings.cs`, transport interface and `Accounts.cui` into the existing CUI scene host. Its native owner uses the existing `Accounts/Remote` public-client S256 PKCE/browser/loopback contract and genuine per-user Access credential source. The package must stage the exact pinned official helper at `auth/cloudflared.exe`; absent or incompatible support remains unavailable. No browser cookies, local trusted-profile issuer or installed-peer approval substitutes for a genuine CAKE sign-in.

The process owner captures native account/session/helper custody, preflights the same original joins and drains its account windows while the actual dispatcher is alive. Expiry/context changes clear presentation observations and invalidate the original read generation, including queued publications and follow-up reads. Existing signed mutation/broker and browser lifetime contracts remain owned by their current components. These composition changes require compiler, meaningful privacy controls and genuine Windows login/GUI workflow evidence before acceptance.

## Windows native Home permission review

The Desktop process registers the recovered `HomeNativeApprovalWindowOwner` over its SAME original Windows Home runtime, OS-profile identity and permission service. The Settings front door opens the existing canonical `HomeApprovalCuiSurface` through that retained owner; it creates no Home graph, installed-peer identity, policy or authority token. The existing native CUI host and embedded Home approval resource remain the owning rendering inputs.

Native review originals participate in the current App retirement preflight and borrower drain while the real dispatcher is alive. One-time Accept and Decline continue to recheck the canonical actor, request and full digest. Extended trust keeps its complete warning/audit contract and remains unavailable without the genuine exact-frame presentation source; mounted controls, headless tests and elapsed time do not supply it. Recovered fixtures are preserved verbatim. Compiler, Windows GUI/workflow, genuine extended-trust presentation and installed-authority acceptance remain separate gates.


The current Windows App also binds its actual main shell/window through `App.OriginalWindowsNativeRoutes.cs`. `NativeFilesDesktopRoute.BindOriginalSameProcess` and `NativeCanonicalTaskSceneReadiness.BindOriginalSameProcess` retain that SAME provider/Home and the existing App/window work tokens. `WindowsHomeSameProcessRuntimeObservation` re-reads the real OS-local Home profile around the started runtime's required Core/state/permission service versions and checks current services again after the consumer's final actor/context read. This is a Home-domain observation; installed Home IPC still requires its original protected verifier and compatibility path.

Canonical Tasks keep their separate `HostLocalTaskActorSource`. The original Space Task/Run/context is re-read before and after Home observation and is reused by the maintained Dev factory. The shell's pure retirement preflight includes its actual Files route; failed startup retains even an acquired route whose attachment failed and joins its original sources before Home. These source connections supply no CAKE account, installed-peer identity, native presented frame, root/store grant, model admission or command permission. Real Windows launch/authentication, saved-project workflows, GUI presentation and shutdown acceptance remain unvalidated.

### Same-process Windows developer and explicit personal recovery composition

The Windows Desktop precreates the exact developer READ/setup policy and lazy resource
resolvers before constructing its original `HomeNativeWindowsComposition`. Distinct
Windows typed DI overloads retain that Home state/profile/broker/permissions tuple and
reuse the original Files selection/scope/folder producer and `WorkspaceToolService`
capture factory. They configure the actual setup journal and completion source; no
configuration creates a Files root, project, permission, Task, or profile/account grant.

The existing explicit personal recovery selector remains 0/1. Only requested Windows64
configuration exposes the same SQLite journal, canonical Task actor/coordinator, Home
current-project READ reconciliation, original Files/native selection source, and
current-project execution bridge/consent/commit/pin aliases. The Host-local Task actor
is a distinct canonical Task authority; it is never equated with the Home profile or
CAKE account. The new protected store uses the qualified internal native custody
boundary and observes real SID, file identity, namespace, ACL, and storage sync. Its
configuration stays unverified until those operations genuinely pass.

Desktop captures each owner before startup publication. Whole-cohort pure join
preflight precedes any retirement. Canonical business and view admission seal first;
setup/execution admission then seals while final completion/audit dependencies remain
live. Original business and managed-window work joins before final setup and execution
completions. Only after those originals settle may scope/capture/outcome/journal/READ
and current-project native owners drain, followed by native windows, Home, and provider.
A missing original or unresolved acquisition preserves the live dependencies and
refuses clean acknowledgement. Actual Windows compiler, workflow, native frame, auth,
manual authority, persistence/durability, package/install, and clean-exit evidence remain
separate and unrun; source composition is not their receipt.

### Files product startup

The owning Files Windows executable declares `9to1.InitialApp=files` through the existing FilesWindows publish profile. Actual App startup consumes this declaration after original Windows Home start and shell initialization/session restore, then awaits the existing guarded Files route in the same App work scope. The Home constructor captures the compiled Files mutation policy alongside the original Dev policies before the permission service is built. Metadata selects a route and never supplies actor, store, approval or startup readiness. Files remains setup-required until its actual OS profile has explicitly configured an empty Files directory; the existing `files-configure` Console command is a setup seam, not GUI or IPC acceptance evidence.
