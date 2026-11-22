# Files browser surface

Files renders its authored CUI surface through `FilesBrowserController`, which calls the registered `IFilesProvider`. The same-origin Drive provider supplies authorised rows and operation results. Test navigation by opening a real permitted folder, returning to its parent and reopening the target through the host route. The approved November 17 layout is preserved as visual provenance for the later Atlas pass. [Controller](FilesBrowserController.cs) · [Layout decision](../../../docs/specification/owner-references/2026-11-17-files-layout/owner-correction.json) · [Current source register](../../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)

The Files route uses `app.files`. A folder deep link carries `EntityType="folder"` and its canonical `HostedItemId`. On success, `FilesBrowserRoute` returns a `BrowserCuiSurface` with the actual controller and a disposable private view lifetime. The authored `Files.cui` keeps the Drive sidebar, breadcrumb, command bar, grid/details switch and item-count footer on the same provider state.

The Web host registers `FilesBrowserFeature` after the private same-origin API verifies the owner. It supplies the canonical Drive provider and the browser request group's revoke and drain callbacks to the existing route. The server owns the Google credentials and checks the active 9to1 session.

Supported workflow: list a provider folder, select an actual item, create a folder, open it, create a child, navigate up, rename a selected item, close, reopen by stable identity. Folder creation and rename use canonical `FilesOperationId`, `HostedItemId`, `FilesRevisionId`, provider capabilities and the provider's actual errors. Rename submits the exact base revision captured when editing began. Name and revision conflicts retain the draft. An uncertain mutation reply locks the intent and retains its operation key for retry; it does not invent a successful result or generate another create ID. Results must acknowledge `Committed` and a canonical result revision before the UI says Saved.

Rows use explicit CUI bindings and canonical identity keys without reflection. The UI renders at most 500 rows; further explicit pagination replaces that window. Selection must originate in the current page. Disposing a private view cancels its requests, clears rows, selection and drafts, and disables actions.

Root, back and forward navigation retain canonical folder IDs and applied searches in a bounded 128-visit history. Reopening re-reads current names and permissions, so renaming a folder does not break navigation. Search uses the actual provider's folder-scoped query and is disabled without its declared Search capability. Clearing a query restores the folder listing. Name ascending/descending and newest sorting affect the loaded window only; the UI labels this boundary explicitly. Retired rendered rows and items moved out of the displayed parent cannot become active selections. Private search/history is cleared on denial and teardown.

The connected Drive location adds move, recoverable Trash and restore, resumable uploads, download and supported Google Workspace exports. Before connection, Files explains why it needs Drive access and links to the Files privacy notice. When Google has not confirmed revocation after a successful local disconnect, Files clears the provider view and reports the two outcomes separately.

`Tests/FilesBrowser.Tests.csproj` is a custom executable, not a VSTest project. It links the production controller/route/CUI and calls the canonical `DurableDriveProvider` against caller-specified isolated disk state:

```sh
dotnet run --project apps/Web/Files/Tests/FilesBrowser.Tests.csproj -- /absolute/isolated/evidence/data
```

Expected 24 tests. The lost-reply cases discard one reply after the real engine commits to disk, as an exception or a typed unavailable error, then verify idempotent recovery and exactly one journal event. A foreign committed receipt cannot acknowledge another intent. Access-denial cases clear private folder names, rows, drafts, searches and history. Navigation/search/sorting checks reopen actual canonical identities, re-read renamed folders and refuse moved or retired rows. The disconnect cases inject the two transport outcomes and use real durable rows to verify what the controller clears or restores. These checks cover local controller behavior. Browser execution and live provider acceptance require separate evidence.

Pending navigation cancellation disposes its private controller before rendering. A renderer consumes the lease once and detaches navigation cancellation. Unsupported `DeepLink` and `ModelPickerTarget` contexts are rejected with the original request preserved. Individual presentation observer failures are traced by exception type, allowing later observers and commands to continue; they cannot change durable save success.

Teardown clears private presentation before owner cancellation callbacks and disposes the token source even if a callback fails. The disposal case verifies a real durable commit plus throwing callback cannot retain the private draft or replay the mutation.
