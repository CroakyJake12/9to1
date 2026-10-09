# Exact browser account review and owner request

Read-only source review: `43669e20a267a2447458aaa5f081987f97d29b05` composition plus `8b4f2a0c071b91be357e48a603e25fff3c6f14ff` serializer correction. No B production source was edited. Maintained Worker fixture is exact `aedc29ec3a0a960e1ded36c0443f1efda4fcf36d`, independently reviewed and runtime-tested, with owner protocol publication `1fcf237e1ae28a7adf2261ee0caab00931c93a46`.

## Reproduced disposal regression

`apps/Web/Accounts/AccountBrowserBindings.cs:263` sets `_disposed` and then invokes outward `_lifetime.Cancel()` before `ClearPrivate()`. A registered cancellation callback that throws prevents private cleanup, closed status and notification. The actual source-linked negative loaded a canonical-shaped scripted profile, made a private draft, started a cancellable read and threw from that read's cancellation registration. After `Dispose` failed, `TryGetValue` still returned loaded account/profile and the private draft. Discovered/executed one negative, failed one, exit 1. This proves retained data in that disposed instance; it does not prove cross-account disclosure or any live provider behavior.

Run from the repository root at this review ref:

```sh
DOTNET_CLI_HOME=/workspace/team-c/evidence/c2/dotnet-home \
NUGET_PACKAGES=/workspace/.nuget/packages AVALONIA_TELEMETRY_OPTOUT=1 \
/workspace/.tools/dotnet/dotnet run --project handoff/team-c/browser-account-review/negative/Negative.csproj -c Release
```

The harness compiles unmodified B bindings/transport contract and actual maintained CUI writable/repeated-item contracts with the Core project. There are no substituted implementation/interface stubs. The transport is explicitly scripted to isolate a cancellation failure; no token, issuer, identity provider or test server is fabricated. Exact source, harness, argv and raw output hashes are in `review.json`. The first broader Runtime-project build failed on unavailable XamlX types and executed zero controls; its full original raw log and hash are preserved separately, not treated as a behavioral result. An initial Node invocation from the wrong checkout also remains recorded. The correct exact-source Node suite passed 33/33, exit 0.

B owner action: preserve disposed ordering, clear all private data and closed state before invoking outward cancellation, then guarantee required lifetime-token cleanup and notification even when cancellation or observers throw. Preserve the original cancellation exception and any cleanup failures; do not mask it with a later notification error or silently report success. Add a regression with the throwing cancellation registration and all private field/repeated-item assertions. Retain confirmed session mutation's explicitly captured caller cancellation behavior and existing disposal/late-result tests.

## Separate configured browser owner seam

`apps/Web/wwwroot/main.js:10` calls `createAccountModule()` without a client. `BrowserFeatureComposition.cs:16` passes no trusted sign-in callback. The shipped browser artifact therefore correctly remains unavailable; it cannot currently execute configured issuer requests. This is a concrete B composition seam, not a .NET RSA/auth_revision blocker. Existing JS `AccountApiClient` matches the Worker route methods, direct current body, profile/sessions wrappers, coarse scopes, exact successor CAS and 204 session mutations.

B must deliver an explicitly configured isolated browser artifact: create the reviewed `AccountApiClient` using its own receiver-launched maintained fixture and generated actual client/account configuration, supply the OAuth owner's verified S256/state/nonce/exact-redirect/resource token flow, inject that client into `createAccountModule`, and supply the trusted sign-in callback to the existing account factory. Connect mandatory private cleanup to existing account context invalidation; the JS module must outlive a captured self-clearing session mutation. Use only the fixture's reviewed local host mapping/certificate plan and explicit loopback test option; do not synthesize tokens, use a native-client relabel, invent actor headers or borrow C credentials across hosts.

Concrete executable continuation after that B delivery: actual browser sign-in/callback, current/profile/session read, CAS save/conflict/null clear and browser reload retaining actual D1 state; other-session/self revocation and sign-out with old-token denial; foreign scope/account/context and cancellation controls. Use the exact reviewed fixture launcher/drain, actual maintained issuer/JWKS and source-owned browser adapter. No configured artifact has yet been received at the reviewed commits, so those actual browser journeys are HELD pending B's source-reviewed composition, not staging availability. Strict .NET RSA and genuine auth_revision, Files actor adaptation, native/provider/production delivery and org/real reserved identity acceptance remain separate unmet gates.
