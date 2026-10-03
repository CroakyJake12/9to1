# Browser host

This host embeds the existing canonical `Home.cui` and references the pinned CUI
runtime and Avalonia browser adapter from source. It does not translate CUI to
AXAML, define a second Home surface, start local Home/Dulche services, or choose
new account/Files HTTP contracts.

`BrowserRouteCodec` maps `HomeFeatureNavigationRequest` to a URL fragment such as
`#/home.library?entityType=app.write&entityId=opaque-id`. IDs and optional action/
deep-link fields round-trip as data; decoding does not execute a requested action.
Fragment routes retain application context through reload without requiring a
production rewrite or changing another team's public routing configuration.
The process-local scroll cache uses the full encoded request address, including
artifact/entity identity, is bounded to 256 destinations, and clears with the
private context. Actual CUI render/scroll restoration requires production browser
verification; source-linked route tests alone do not establish it.

The composition root registers an existing authenticated
`IHomeFeatureRouteHandler` with `BrowserApplication.Surfaces.Register(handler,
viewState => new BrowserCuiSurface(document, bindings, actions, lifetime))`.
The owner returns a successful `HomeFeatureViewState` with the same RouteId.
Registered owners take priority for Home routes as well as application routes.
Owner denial, failure or unavailable results never open the fallback Home view.
Documents and bindings must use canonical product state; action dispatchers must
implement `ICuiActionAvailability` to enable their actual available commands.
An optional final `CuiControlRegistry` on `BrowserCuiSurface` supplies existing
owner control/object renderers to that surface's loader; registrations do not
change route authorisation or action availability.
The shell does not interpret JSON state as a guessed schema or invent providers.

With no authenticated adapters registered, Dashboard/Library/Events can expose
their canonical shell navigation and explicit unavailable provider states.
Unsupported operations and search are disabled. Opening an unavailable route
preserves its address and previous valid view. This is an incomplete browser
host, not an accepted Home implementation or browser parity release.

Account/session/organisation ownership must call `ResetPrivateContext()` before
loading a different context, clear old adapters, then register current authorised
ones. No account credentials or private artifact caches are persisted by this
host. Service worker registration is disabled until an owning team supplies the
approved cache/account separation policy.

`BrowserAccessibilityBridge` reads the current rendered controls' existing
Avalonia automation peers. Its nonvisual browser projection supports native
button invoke providers, ordinary text value providers, and text peers; other
provider roles are explicitly reported as unsupported. Names, disabled and
readonly states, values and help text come from those peers. Password values are
excluded. Focus, invocation and edits use the actual native providers; the bridge
does not define application actions or account policy. Every render replaces its
element generation, and private reset/disposal removes the prior controls. Native
enabled/readonly state and current-root membership are checked again at use.
Full accessibility still requires actual browser and assistive technology checks.

The browser action observer applies a disposable restriction through Avalonia's
existing enabled-property priority store. Removing a capability restriction
restores the owning CUI's current binding, local value or style, including an
owner assignment made while the effective value was already false. Each rendered
surface owns this observer and disposes it before its loader. This does not grant
an action permission; the owner dispatcher continues to enforce availability.

The current composition registers a device-local Wave editor backed by real
IndexedDB transactions and the unchanged canonical Wave project/edit/decoder
sources. Its projects belong to this browser origin; they are not hosted Files
identifiers. Private-context reset removes the presentation while retaining that
local owner session. Full shell disposal removes both private and local owners.
Account Settings uses the maintained account client protocol with an explicitly
unconfigured service module until an owner supplies reviewed authentication.
Write, Files, Sites and Picture are being integrated in subsequent bounded
changes; their full specification requirements remain outstanding.

The native action availability observer applies a disposable value restriction
through Avalonia's public property API. Removing that restriction restores the
owner's actual enabled bindings, local values and styles, including equal-value
assignments while unavailable. It observes current native controls and context
notifications; each rendered surface must own and dispose its observation.

BFCache suspension/restoration clears private views and registered adapters. The
user receives a session-check state and reload entry point; a preserved tab does
not reopen old private content based only on browser history. The account owner
must subsequently revalidate identity before registering private destinations.

The historical `98a0882` checkout lacked vendor build imports. The incoming
`a63d77f` owner candidate restores the broad source build graph. Its exact XamlX
and DBus gitlinks must be materialised before building; old-main failures are not
evidence of candidate failures. Build the pinned browser adapter's JavaScript
from its unchanged `webapp/bun.lock` and `build.js`, then publish the host with
the installed .NET 10 WebAssembly SDK. The SDK uses static web assets rather
than the legacy `AppBundle` target. The host imports the source adapter's actual
asset/GL contracts and retains the native packages' SDK-selected library variants.
The shared CUI helpers initialise the existing primitive theme and Inter font.

The isolated run used root-selected `Bun.Unofficial.Tool` version `1.3.4`; the
donor project itself does not pin that tool. After reserving build resources,
run the following with writable `DOTNET_CLI_HOME`/`NUGET_PACKAGES` and that tool
installed outside the checkout:

```bash
# From the pinned Avalonia.Browser/webapp directory:
/workspace/team-b-tooling/bun/bun install --frozen-lockfile
/workspace/team-b-tooling/bun/bun build.js

# From the repository root:
/workspace/.tools/dotnet/dotnet publish apps/Web/NineToOne.Web.csproj -c Release
```

Serve the actual `apps/Web/bin/Release/net10.0/publish/wwwroot` output read-only.
Do not copy a source shell over incomplete build output or substitute runtime
modules. Successful compilation or local publication does not establish
deployment or product parity.
Deployment, supported-browser policy, actual service negotiation, shared AI bar,
full application surfaces, continuity, visual/accessibility and resource acceptance
remain required gates. Evidence is maintained in the run's B1 ledger.

Isolated verification in the current managed environment:

```bash
DOTNET_CLI_HOME=/workspace/team-b-evidence/sol-happy-20261003/b1/tooling/home \
NUGET_PACKAGES=/workspace/team-b-evidence/sol-happy-20261003/b1/tooling/nuget \
/workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserNavigation.Tests.csproj -c Release

/opt/codex/runtimes/codex-primary-runtime/dependencies/node/bin/node apps/Web/Tests/browser-platform.test.mjs

# Mock DOM/provider safeguards and mutation controls, distinct from actual AX checks:
/opt/codex/runtimes/codex-primary-runtime/dependencies/node/bin/node apps/Web/Tests/browser-accessibility.test.mjs apps/Web/wwwroot/browser-accessibility.js /tmp/browser-accessibility-unit-results.json

# Actual source-linked native peer checks (unit fixtures, not browser acceptance):
/workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserAccessibility.Tests.csproj -c Release

# Actual native capability transitions, owning enabled state and observer disposal:
/workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserActionAvailability.Tests.csproj -c Release

# Actual retained Haven virtual-peer membership, distinct from Write editor acceptance:
/workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserVirtualAccessibility.Tests.csproj -c Release

# Actual CUI dashboard layout; preserve failure until the owning Home source is fixed:
/workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/HomeLayout.Tests.csproj -c Release -- '9to1 Workspace/Home/UI/Home.cui'

B1_BROWSER_EVIDENCE=/workspace/team-b-evidence/sol-happy-20261003/b1/browser \
/opt/codex/runtimes/codex-primary-runtime/dependencies/node/bin/node apps/Web/Tests/run-browser-platform.cjs
```

The DOM runner uses `/usr/bin/chromium`, the installed Playwright module at
`/opt/codex/runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright`
(override with `PLAYWRIGHT_MODULE`), a fresh isolated browser context, and loopback
port 18761. It closes its browser/server and records results, a screenshot and a
trace. Reserve the port/browser resource before running. It also checks that a
controlled test-only duplicate-event mutation is rejected by unchanged assertions.
Its real DOM/history checks validate the JavaScript adapter; controlled BFCache
event dispatch does not validate actual authenticated suspension/restoration.
The source-linked Home contracts in the unit harness are the existing owner
types; test handlers are fixtures and do not establish backend acceptance.
