# Browser account presentation

`Accounts.cui` and `AccountBrowserBindings` use the existing CUI binding, explicit input-writeback, repeated-item, action-dispatch and action-availability contracts. The surface belongs in the account part of the existing `home.settings` destination. It does not replace the Home settings authority or add an application route.

The service transport consumes the reviewed `Auth/AccountApiClient` replies unchanged. Account, profile and session bodies stay as server wire JSON; only the unsaved field draft, original edit revision, busy/error state and session confirmation are view-local. There is no account, entitlement, permission, subscription or job authority here.

Supported actions are current-account/profile/session inspection, editing all five profile fields, explicit nullable clears, revision-checked save, conflict recovery, selected-session revocation, other-session revocation and sign-out. Name and Username are visibly required and blank values cannot be dispatched from this view. An optional trusted sign-in callback belongs to the existing OAuth public-client owner. Its return does not grant identity: the view must re-read the real account service. Missing configuration and unavailable/denied/malformed responses cannot display a fabricated account or successful mutation.

Ordinary refresh preserves an unsaved draft. A profile conflict preserves the original draft/revision, shows the reported conflict revision and disables saving until explicit discard/reload. The server remains responsible for username reservation, uniqueness, validation, normalization and permissions. Icon upload and username-availability APIs are not in the acknowledged Worker route set and remain outstanding; the currently supported icon field is the server's HTTPS URL reference. Plan, billing and entitlement workflows remain unavailable pending their source-owner service contracts.

Account/session/context invalidation must dispose this presentation before a new private surface is registered. Disposal cancels profile reads/writes, clears all private records/drafts/session selections and denies old repeated-item lookups. User-confirmed session mutations and trusted sign-in retain **explicit caller cancellation** across their own private-view cleanup. For a session mutation, the reviewed account client captures the original token before cleanup disposes this view and precedes server dispatch. Trusted sign-in cannot refresh or present the old disposed view afterward; the owner registers a fresh Settings adapter only after genuine token and current-account service verification. No later command can use a disposed view. The account module must outlive that self-clearing request; disposing the module itself cancels all its requests. Later account switches still go through the account client's generation invalidation.

The host owner must:

1. Embed `Accounts.cui` as `NineToOne.Web.Accounts.cui`; exclude `Accounts/Tests/**/*.cs` from the Web application's compile glob.
2. Parse that resource through the maintained `CuiRichParser` and supply the resulting document and bindings/actions/lifetime to `BrowserCuiSurface` within the existing `home.settings` route.
3. Supply `AccountBrowserBindings` a UI-thread presentation dispatcher, the wire transport, and the existing trusted sign-in callback when available.
4. Import `Services/account-service.js`, call `createAccountModule` with the configured reviewed `AccountApiClient`, and register it as `nineToOneAccounts` through `runtime.setModuleImports`. Passing no client is a truthful unavailable configuration. Publish the module and reviewed Auth source with the browser assets; no endpoint or token defaults exist.
5. Bind the reviewed client's cleanup callback to the existing `ResetPrivateContext`/`Program.PrivateContextInvalidated` boundary. Revalidate before reopening private surfaces, including BFCache restoration. Preserve the currently confirmed session mutation as described above.

Checks:

```sh
node --test apps/Web/Auth/account-api-client.test.js apps/Web/Auth/account-api-client.profile-review.test.js apps/Web/Services/account-service.test.js
dotnet run --project apps/Web/Accounts/Tests/BrowserAccounts.Tests.csproj
```

The Node and console fixtures exercise transport/presentation logic and parse the actual CUI document. They use scripted service replies and do not establish maintained Worker/D1, OAuth issuer, real browser rendering, persistence/restart, cross-client, billing or full parity acceptance. Those gates require the exact maintained issuer/configuration, owner-provisioned local test client/accounts, and authorized isolated execution resources.
