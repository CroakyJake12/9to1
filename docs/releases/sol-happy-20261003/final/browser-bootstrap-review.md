# Immutable Team B browser bootstrap review

Reviewed commit: a9153bc7b9af600dab9cbe724e4cd6a46b8e62b2, 17 new files / 1027 lines. All changed files read at immutable revision, plus owning HomeFeatureContracts/HomeCuiScene/Home.cui, Home project dependency graph and CUI control-loader action wiring. No release/source edits. Detached source checkout /workspace/team-c/c1-review-b1 is exactly a9153bc.

## Actionable finding: registered authenticated Home surfaces never dispatch (P2)

apps/Web/BrowserApplication.cs:67-72 calls BrowserHomeContext.Open before BrowserSurfaceRegistry.OpenAsync and returns for Dashboard, Library and Events. BrowserHomeContext.cs:11-15 and :30-37 accepts those three routes unconditionally. Therefore even after the advertised composition-root registration of an authenticated IHomeFeatureRouteHandler for home.library, home.events or home.dashboard, opening that fragment never invokes the owning handler; the unavailable-context bindings and HomeServiceUnavailable status are always rendered. This is a registration/integration defect, not proof of a current unauthorized data access. README tells owners to register authenticated adapters, but those Home route adapters are structurally unreachable. Request B to consult registered owner routes before unavailable Home fallback and add an application-level test proving a registered Home handler actually opens. Keep fallback honest when no owner exists; do not create alternate Home services.

Existing isolated tests do not detect this: their registration tests call BrowserSurfaceRegistry.OpenAsync directly, bypassing BrowserApplication.OpenFragmentAsync and BrowserHomeContext. The finding follows the actual unconditional control flow; full application execution remains blocked/unverified and is not claimed here.

## Boundaries inspected

BrowserRouteCodec uses actual HomeFeatureNavigationRequest, bounded canonical route syntax, whitelisted fields, duplicate-field rejection, percent syntax/control-character checks and lossless opaque data encoding. Action/deepLink remain data, forwarded to owner handler; there is no action executor or provider credential in this patch. Navigation data is untrusted; registered owner adapters still must enforce authenticated caller/account/origin/scopes before consequential actions. No deployed owner adapter is registered in this commit, so caller-origin/permission enforcement remains a dependency, not verified integration. No postMessage/origin bridge was introduced.

BrowserSurfaceRegistry reuses HomeFeatureNavigationHost and refuses absent/failed/mismatched views; clearing context rejects late completed private views. BrowserApplication cancels superseded navigation, disposes abandoned lifetimes, clears rendered state/adapters/scroll cache on explicit private-context reset. JavaScript BFCache handlers invalidate context and render session-check state. Their controlled event tests do not prove real authenticated BFCache restoration. Status text is set via textContent, not HTML; unregistered destination returns explicit unavailable state and preserves intended URL rather than silently opening another artifact.

The host embeds canonical Home.cui and references existing Home/CUI/Avalonia browser source projects. It does not create alternate Home services, account models, permission broker, AI model provider or CUI semantics. BrowserHomeContext is a clearly unavailable presentation binding. The entire HavenOS.Home source reference also pulls shared/native/model project graph; Team A must validate actual browser-compatible graph/vendor build and packaged runtime. Source references alone are not a successful WASM build or feature parity.

## Independent test replay

Command: DOTNET_CLI_HOME=/workspace/team-c/evidence/c1/review-b1/dotnet-home NUGET_PACKAGES=/workspace/team-c/evidence/c1/review-b1/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1 /workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserNavigation.Tests.csproj -c Release, cwd /workspace/team-c/c1-review-b1.

Observed: 16 discovered/executed/passed, 0 failed; exit 0. Raw log /workspace/team-c/evidence/c1/review-b1-unit.log. Source-linked real codec/registry with owning source contracts and fixture handlers; backend/browser acceptance NOT-RUN. This does not exercise full BrowserApplication render/dispatch.

Command: node apps/Web/Tests/browser-platform.test.mjs, same cwd. Observed 7 discovered/executed/passed, 0 failed, exit 0. Mock window/document tests; real-browser acceptance NOT-RUN. A real-browser runner exists in the patch with isolated browser contexts and negative detector, but this reviewer did not reserve/run its shared Chromium/18761 resources. No authenticated runtime, supported-browser matrix, actual application rendering, donor/UI parity, cloud services or deployed-provider acceptance was run.

Review disposition: source review ready with one actionable registration defect; root determines whether a provisional incomplete bootstrap is integrated while B fixes it. No full-browser, provider or semantic completion claim.
