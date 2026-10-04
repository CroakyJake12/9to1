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

Account/session/organisation ownership must call and await
`ResetPrivateContextAsync()` before loading a different context. Private owners
implement `IBrowserPrivateContextParticipant`: their synchronous revocation hides
state before callbacks, and their asynchronous disposal drains issued work. The
registry gates replacement until every reset settles; cleanup failures stay closed. No account credentials or private artifact caches are persisted by this
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

The accessibility snapshot uses generated JSON metadata. Canonical Wave storage
owns existing private reflection-based serializer options, so this browser host
explicitly enables the SDK's reflection serialization contract and preserves the
Web, Wave engine and Core assembly metadata during trimming. This consumes the
unchanged owner codec; fresh published browser tests must verify the closure.

The current composition also registers device-local Wave and Write. Write uses
the owning structured editor and retained scene, original theme resources and
fonts, the canonical Notes document/validator, and an atomic IndexedDB repository.
Native `.9to1w` chooser/download operations use the unchanged owner codec through
temporary byte staging. This assembly supplies the original `Haven` font resource
identity and must not coexist with a desktop `Haven` executable assembly.
The retained Write edit peer currently lacks an owning value/text provider; its
geometry diagnostics do not establish rich editor accessibility or donor parity.

Owners with drafts implement `IBrowserCloseParticipant`. Explicit `CloseShell`
awaits preparation before removing any routes and then awaits asynchronous
teardown. Failed or cancelled preparation retains the registered owners and draft.
Synchronous clear rejects owners requiring preparation. Private asynchronous owners require `IBrowserPrivateContextParticipant` and are
rejected at registration without that revocation and drain contract.
The browser's ordinary unsaved-change prompt reads actual owner dirty/busy state.
Page termination cannot guarantee awaited persistence; save or explicit close
must finish before leaving. Forced termination/recovery remains an acceptance gate.

Account Settings consumes optional public host configuration described in
[Auth/PUBLIC_CLIENT.md](Auth/PUBLIC_CLIENT.md). The default is unconfigured.
The composed public client verifies genuine maintained-issuer tokens and the
current account API before replacing private adapters, and clears its in-memory
credential supplier on account invalidation and cached-page suspension/restoration.
Configured OAuth, session changes and authenticated BFCache still require actual
browser tests against the maintained service with the exact allowed HTTPS origin.

The current composition registers a device-local Wave editor backed by real
IndexedDB transactions and the unchanged canonical Wave project/edit/decoder
sources. Its projects belong to this browser origin; they are not hosted Files
identifiers. Private-context reset removes the presentation while retaining that
local owner session. Full shell disposal removes both private and local owners.
Account Settings uses the maintained account client protocol with an explicitly
unconfigured service module until an owner supplies reviewed authentication.
Write is now composed for actual browser validation. Files and Sites adapters
are committed but require maintained provider/HTTP activation; Picture, Present
and Forms remain separate unregistered proposals. Their full specification
requirements and all applicable platform journeys remain outstanding.

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
# Select a new output path for each exact source revision. Refuse reuse: SDK
# publication into an old directory can retain stale fingerprinted assets.
B1_PUBLISH_DIR=/tmp/nine-to-one-browser-new-revision
if [ -e "$B1_PUBLISH_DIR" ]; then
  echo 'Choose a new publication directory; this one already exists.' >&2
  exit 1
fi
/workspace/.tools/dotnet/dotnet publish apps/Web/NineToOne.Web.csproj -c Release --output "$B1_PUBLISH_DIR"
```

Serve the actual `$B1_PUBLISH_DIR/wwwroot` output read-only and preserve its exact
coordinator publication receipt. For a reviewed receipt, supply the expected
commit and receipt SHA-256 independently; do not derive trusted values from the
receipt being checked:

```bash
node apps/Web/Tests/verify-publish-receipt.cjs <receipt.json> <actual-wwwroot> <expected40hexCommit> <expectedReceiptSHA256>
```

This read-only check compares every file, size and SHA-256, rejects extra/missing
files and links, and requires a successful publication with unchanged source
inputs. It permits relocating an exact bundle and reports the original output
path. It verifies artifact transport, not signatures, package authority, browser
compatibility or runtime acceptance. The filesystem regression runner uses a
new evidence directory and isolated copies of three bootstrap files:

```bash
node apps/Web/Tests/publish-receipt.test.cjs <receipt.json> <actual-wwwroot> <expected40hexCommit> <expectedReceiptSHA256> <new-evidence-directory>
```

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

# Actual owner-approved CUI dashboard layout, distinct from browser verification:
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

Account/root teardown ordering and remaining CUI/provider gates are described in
[Auth/PRIVATE_CONTEXT_LIFETIME.md](Auth/PRIVATE_CONTEXT_LIFETIME.md).
