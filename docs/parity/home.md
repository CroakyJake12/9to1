# Home implementation and acceptance

Authority: current Development Specification V2, Home section, plus
`9to1 Workspace/shared/docs/CURRENT-REQUIREMENTS.md`. Updated 16 October 2026.

## Actual product routes

The shared desktop `MainView.OpenHomeAsync` opens the existing dashboard in the new shell edition.
The dashboard retains its original repositories, saved page/widget identities, live widget canvas,
drag/resize, manual undo/redo, typed app callbacks and editing assistant. Its rendered shell comes
from `Views/Pages/Home/Dashboard.cui`; AXAML is only the activation wrapper. It implements the shell's
activation interface, so switching tabs stops the timer and cancels pending refresh/edit work. The
timer refreshes observed data while active. A generated proposal cannot overwrite a manual layout
or page change made while that proposal was being generated.
The widget toolbar is authored CUI with wrapping controls. Reopening Home during a cancelled
refresh waits for that read to finish and starts a new read, rejecting its old result.

The shared dashboard's Discover action opens `HomeDiscoverPage` and its `Discover.cui` surface.
It reads the same registered provider instances used by model routing, with provider-scoped
identity, search, primary Chat/Image/Voice categories, advanced Audio/Video, locality filters and
refreshed comparison. Local-only policy is checked before and after remote catalogue reads.
Provider failures are visible; unreported install state, voice quality, cost, hardware fit and
residency remain unknown. Provider management opens the existing shared model settings.
Catalogue access does not grant installation or invocation authority.

The separate Windows Home host in `apps/Home` renders `Home/UI/Home.cui`. Dashboard, settings,
permissions and Events use the original Home composition/profile. Events projects the canonical
permission audit instead of maintaining a second journal: only request events bound to the current
original profile are exposed, with source AuditID, timestamp, caller and a typed permission-request
deep link. Profile changes revoke the view and prevent stale opens. Historic requests remain
read-only; opening an event never approves or repeats an action.

## Shared Home behavior

`HomeDashboard` projects actual package observations, including recovery and cancellation.
`HomeDashboardLayoutService` retains revision checks, independent AI creation/reorder preferences,
provider metadata, protected tiles, history and reversible audited AI edits. Provider refreshes
recheck permissions before publishing. The CUI projection follows semantic ordering and removes
hidden, removed or reconfigured cached tile content; superseded/cancelled searches, Events reads
and tile refreshes cannot replace newer presentation state.

Library keeps canonical app/artifact identity and revalidates read/open permissions. It rejects
provider deep links naming another app/artifact. Invalid sort/pagination queries return structured
failures. Events distinguishes unavailable providers from a real empty history and rejects inverted
date ranges.

## Verified evidence

Linux ARM64, .NET SDK 10.0.402:

| Command/project | Result |
| --- | --- |
| `dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" -c Debug` | 219 passed, 9 Windows-only skipped, 228 total |
| Same project, Release | 219 passed, 9 Windows-only skipped, 228 total |
| Focused ecosystem, CUI projection and permission activity tests, Debug | 13 passed |
| Real registered-provider Discover adapter tests, Debug | 5 passed |
| Web Home managed owner/presentation suite, Debug and Release | 35 passed in each configuration |
| Actual shared Home dashboard/Discover views and retained widget canvas, Debug and Release | 25 passed in each configuration |
| `apps/Home/src/AvaloniaHome/AvaloniaHome.csproj`, Windows cross-build, Debug and Release | Both succeeded, 0 warnings/errors; runtime Unvalidated |
| `/usr/lib/chatgpt/resources/cua_node/bin/node --test apps/Web/Home/Tests/home-local-preferences.test.mjs` | 6 passed |

Build flags: `-m:1 -p:BuildInParallel=false -p:RestoreDisableParallel=true
-p:UseSharedCompilation=false --disable-build-servers`. The full Debug run reused its just-built
output with `--no-build --no-restore`; Release performed its own build. Both current build logs
contain no warnings/errors. This covers the domain and registered test paths only.
The JavaScript checks use the unit Web Locks harness; they do not establish multi-tab browser
acceptance. The Discover adapter checks are included in the current full domain counts.

The web checks use scripted actor/wire replies. They establish managed owner and presentation
behavior, not real issuer, IndexedDB, multi-tab browser or native acceptance.

`Home/Presentation.Tests` links the same maintained desktop dashboard/Discover render/layout tests
to the actual portable desktop product, using its production CUI primitive theme and runtime in a
host-architecture headless test Application. The full shared App bootstrap requires explicitly
x64 `Haven.Infrastructure`, which cannot load on this ARM64 host. The new test host does not change
or replace the original Windows/x64 test target or establish full app startup acceptance.
The Debug and Release presentation suites passed all 25 cases each, including both 1280px and 390px viewports in
SuperBright, Bright, Dark and SuperDark, actual model selection/filter/comparison actions, existing
dashboard editing callbacks, stale-refresh rejection and narrow-to-wide saved-layout preservation.
These are headless behavior/layout checks, not device,
pixel-level, screen-reader or full desktop startup acceptance.

## V2 acceptance matrix

| Required behavior | Implementation/evidence | Acceptance |
| --- | --- | --- |
| Home landing route, source-backed live briefing | Shared desktop now opens the retained dashboard and original repositories; domain projection and actual content-view render tests passed; full host startup remains unvalidated | In progress |
| Stable Home/Apps/Library/Events/Discover/Mesh/Settings navigation | Home and Discover connected in shared desktop; Home/Settings/Permissions/Events connected in native candidate; other destinations still need production integration | Incomplete |
| Manual add/remove/hide/resize/reorder, persistence, protected tiles, reversible AI | Canonical Home domain and retained canvas checks passed; full cross-device conflict and permission behavior needs runtime evidence | In progress |
| Independent AI-generation/reorder controls and source authorization | Home domain and candidate controls exist; stale projection/permission revocation tested; parity with shared desktop preferences still required | Incomplete |
| Native package catalogue/install/update/repair/rollback/launch | Domain covered; installed producer/package provider not connected end-to-end in candidate host | Incomplete; Windows runtime Unvalidated |
| Cross-app Library using shared index and current authorization | Contracts and guarded query/open behavior tested; no concrete shared cross-app index provider connected | Incomplete |
| Typed Events with stable source identity | Real permission journal projection connected in candidate; other ecosystem event sources and shared desktop timeline needed | In progress |
| Multi-provider Discover, search/filter/compare | Real registry adapter tests passed (5); shared desktop CUI render/filter/comparison passed across both widths and all four appearances; live endpoint acceptance remains | In progress |
| Discover install/connect and full Voice metadata/recommendations | Domain contracts exist; provider metadata/action integration remains partial and unknown values are explicit | Incomplete |
| Universal Model Picker, foreground/background routes, permission-bound management | Canonical route/identity services have existing domain tests; complete current V2 Model Groups and production picker integration remain | Incomplete |
| Fresh-profile welcome, secure sign-in return, live model recommendations/download | No complete first-run flow on exact shared desktop route | Incomplete |
| Home Core/permissions/account/Mesh/notifications/search/productivity shared ownership | Existing owning services have domain coverage; complete hosted/native/client integrations require further evidence | Incomplete |
| Required web Home with secure identity, persistence and shared services | Managed owner/presentation 35 passed in each configuration, local-preference unit harness 6 passed; real issuer/IndexedDB/browser acceptance remains | Partially tested; browser Unvalidated |
| Windows/Android rendering, all appearances, input/accessibility, tray/background/reboot | Linux host tests cannot establish these required device outcomes | Unvalidated |

## Remaining acceptance

### Unified-host child lifetime checkpoint (16 October 2026)

The dashboard and Discover now expose `TryPrepareToCloseAsync`, `CloseAndDrainAsync`, and the
cached `OriginalClose` task for the unified Home content owner. Dashboard editor drafts veto
preflight without discarding their content. Actual refresh/provider tasks, admitted saves,
timer callbacks and loader actions remain owned until they finish; retirement cancels reads
before joining them, and already-admitted saves retain their store acknowledgement. Discover
UI events report operation failures without letting them escape through async-void handlers.
The existing original-work owner retains cancellation/failure evidence for external close.

Four new actual-view tests cover held dashboard and provider work, editor veto with a held save,
and a refresh-button event crossing retirement. The close APIs compiled in the shared Desktop
graph; the latest Discover event-handler repair still needs compilation. The updated
**29-case presentation suite is not yet executed** because the
machine entered a power hold. The earlier 25/25 Debug and Release results above apply to the
frozen pre-lifetime/pre-unified-host batch; they do not certify this newer source graph.

Home is **WORKING, not COMPLETED or VERIFIED**. Connected native package installation/updates,
cross-app Library index, complete Discover/model management, Mesh/account shared-service
integration, all required Home navigation destinations, welcome flow and full cross-app briefing
still need end-to-end evidence on their actual product routes. The separate Windows candidate
host explicitly disables unconfigured providers; unavailable states do not satisfy completion.

Windows Home/package launch, tray/reopen/reboot and protected IPC need a Windows runtime. Android
launcher selection, Home intent, app launch, lifecycle and reboot need an Android runtime/device.
These remain **Unvalidated** here. All appearances, theme/accent changes, touch/keyboard,
accessibility, reduced motion and exact-artifact acceptance require their own observed evidence.
