# Map a product action to its owner

| Action | Owning boundary |
| --- | --- |
| Open, select or close a top application tab | Browse workspace and Haven’s route/close coordinator |
| Choose a device appearance | Home preference service and CUI theme resources |
| Send a message in a Space | Chat controller, conversation and its configured model/tool services |
| Edit an existing document | App domain, document repository and the active provider |
| Apply a project or task change | Workspace and task owners with their current actor and revision |

A page can render an action before a platform dependency is available. Expose unavailable, denied and pending states explicitly. An adapter must obtain the original identity and owner receipt before showing success. Use the source register to distinguish Dashboard from New Tab, Temporary from a separate app and local from account scope.

## Windows Desktop process-owned Home

`App.OriginalWindowsHome.cs` constructs the actual Windows Home producer over the maintained app paths and OS principal. `WindowsHomeDomainServiceCollectionExtensions.cs` registers the SAME store/profile/permissions/resource/ownership/broker tuple and precreated Files owners into Desktop. The original Files/native Dev factories reuse that tuple; descriptor registration creates no Files root, project, permission or installed peer.

App retains and starts the original Home before shell initialization. Its current shutdown owner preflights Home together with the borrower cohort and closes Home only after business borrowers and actual native windows settle. Failed-startup cleanup keeps Home alive when a borrower remains unresolved. CAKE ID `sub` is independent of the local OS-profile authority. Protected installed-peer admission remains unavailable until an actual approved verifier is supplied. The native permission-presentation consumer and authenticated account UI are separate reviewed composition cohorts.

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

[Native composition](../../src/Haven.Desktop/App.UnifiedWorkspaceHost.cs) · [Chat](../../../Spaces/Source/Chat/ChatSpaceController.cs) · [Home](../../../Home/UI/Home.cui) · [Product and interaction volumes](../../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
