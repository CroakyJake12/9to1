# Files browser surface

`Files.cui` is the app's CUI surface; `FilesBrowserController` calls the existing `IFilesProvider` directly. `FilesBrowserRoute` opens `app.files`, optionally with `EntityType="folder"` and the canonical `HostedItemId` GUID. Its successful view yields `BrowserCuiSurface` with the actual document/controller and a disposable private view lifetime.

The host supplies a permission-checked provider adapter and its authenticated actor. The UI does not authenticate accounts, establish ACLs, instantiate a native disk store in WASM, or choose an API URL. Register `new FilesBrowserRoute(provider, authenticatedActor, parsedFilesDocument)` using `registry.Register(route, route.CreateSurface)` only when that real adapter is available. Dispose the route on account-context teardown, as well as the surface lifetime. Root owns the Web project reference, embedded resource and composition registration.

Supported workflow: list a provider folder, select an actual item, create a folder, open it, create a child, navigate up, rename a selected item, close, reopen by stable identity. Folder creation and rename use canonical `FilesOperationId`, `HostedItemId`, `FilesRevisionId`, provider capabilities and the provider's actual errors. Rename submits the exact base revision captured when editing began. Name and revision conflicts retain the draft. An uncertain mutation reply locks the intent and retains its operation key for retry; it does not invent a successful result or generate another create ID. Results must acknowledge `Committed` and a canonical result revision before the UI says Saved.

Rows use explicit CUI bindings and canonical identity keys without reflection. The UI renders at most 500 rows; further explicit pagination replaces that window. Selection must originate in the current page. Disposing a private view cancels its requests, clears rows, selection and drafts, and disables actions.

Root, back and forward navigation retain canonical folder IDs and applied searches in a bounded 128-visit history. Reopening re-reads current names and permissions, so renaming a folder does not break navigation. Search uses the actual provider's folder-scoped query and is disabled without its declared Search capability. Clearing a query restores the folder listing. Name ascending/descending and newest sorting affect the loaded window only; the UI labels this boundary explicitly. Retired rendered rows and items moved out of the displayed parent cannot become active selections. Private search/history is cleared on denial and teardown.

Upload/download, shares, permissions, trash/restore, organisation types, Project/Section/Appearance, content editors and owning-app creation are not exposed by this surface. These require their real owner interfaces/transport and independent tests. This surface does not claim complete Files parity.

`Tests/FilesBrowser.Tests.csproj` is a custom executable, not a VSTest project. It links the production controller/route/CUI and calls the canonical `DurableDriveProvider` against caller-specified isolated disk state:

```sh
dotnet run --project apps/Web/Files/Tests/FilesBrowser.Tests.csproj -- /absolute/isolated/evidence/data
```

Expected 22 tests. The lost-reply cases discard one reply after the real engine commits to disk, as an exception or a typed unavailable error, then verify idempotent recovery and exactly one journal event. A foreign committed receipt cannot acknowledge another intent. Access-denial cases clear private folder names, rows, drafts, searches and history. Navigation/search/sorting checks reopen actual canonical identities, re-read renamed folders and refuse moved or retired rows. These fault boundaries do not replace storage. These checks establish backend-local/controller behavior only; they do not establish browser execution, authenticated HTTP transport, staging acceptance, or platform acceptance. The Web project's default compile glob must exclude `Files/Tests/**/*.cs` before integration.

Pending navigation cancellation disposes its private controller before rendering. A renderer consumes the lease once and detaches navigation cancellation. Unsupported `DeepLink` and `ModelPickerTarget` contexts are rejected with the original request preserved. Individual presentation observer failures are traced by exception type, allowing later observers and commands to continue; they cannot change durable save success.

Teardown clears private presentation before owner cancellation callbacks and disposes the token source even if a callback fails. The disposal case verifies a real durable commit plus throwing callback cannot retain the private draft or replay the mutation.
