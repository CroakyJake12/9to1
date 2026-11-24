# Private Drive provider for 9to1 Files

The public-sites Worker forwards the Files browser path and its same-origin `/api/files/v1/` requests through the Files service binding. The Files Worker resolves the current session and authorised Drive operation. When checking a browser issue, distinguish route forwarding, account admission, provider read and displayed folder content. Use the exact target environment’s receipts for hosted status. [Files Worker configuration](wrangler.jsonc) · [Browser Files route](../../apps/Web/Files/README.md)

This Worker serves the existing 9to1 Files browser app at `https://9to1.uk/files/` and its same-origin API at `/api/files/v1/`. The `cake-public-websites` Worker forwards those paths through its `FILES` service binding. Files uses the existing `WEB_SESSIONS` binding to validate the live 9to1 Account session, then checks the exact configured owner. Google consent is separate, and credentials stay encrypted in the Files Durable Object. Keep `workers.dev` and preview URLs disabled.

## Build and verify locally

1. Publish the existing [9to1 Web project](../../apps/Web/NineToOne.Web.csproj) in Release configuration using the repository's [Web publication procedure](../../apps/Web/README.md) and the approved SDK guard for the host. On Windows use the [guarded Windows runner](../../eng/development/guarded_windows_build.py). Stage the successful publication at `.build/browser`, with the published `wwwroot` directly inside it. Use a fresh staging directory to avoid including assets from older builds.
2. Put a generated `.assetsignore` in the staged `wwwroot` containing `**/*.gz`, `**/*.br`, `**/*.map`, and `**/*.pdb` on separate lines. This matches the private release asset manifest. Keep the original published files and relative paths, including `index.html`, `host-bootstrap.js`, and `_framework/`. Wrangler's `ASSETS` binding points at this directory. Worker-first routing ensures the owner check runs before the app shell is served.
3. With the pinned Node dependencies installed by `npm ci`, run `npm test` in this directory. Run `npx --no-install wrangler deploy --dry-run` to check the staged Worker and assets without deploying. The test suite uses local Workerd, SQLite, and controlled provider responses.

The initial integration remains an uncommitted working tree. Before publishing from another checkout, verify that it contains the reviewed source and the exact browser publication intended for that deployment.

## Owner and provider setup

Use a dedicated Google Cloud project with Drive API enabled. Keep the external OAuth app in **Testing** and include only the approved Google owner as a test user. The web client redirect URI is `https://9to1.uk/api/files/v1/oauth/callback`. Configure the Worker with these secret names:

- `OWNER_ACCOUNT_ID`, `OWNER_GOOGLE_SUB`, `OWNER_GOOGLE_EMAIL` for the exact verified 9to1 and Google identities.
- `GOOGLE_DRIVE_CLIENT_ID`, `GOOGLE_DRIVE_CLIENT_SECRET` for the web client in the dedicated project.
- `DRIVE_ENCRYPTION_KEY` for 32 random bytes encoded as base64url.

Preserve the existing cross-script `WEB_SESSIONS` binding and the single SQLite `DRIVE_PROJECT` namespace. Reuse that namespace across versions so reconnects and deployments preserve usage and transfer state. Google Testing refresh tokens for the Drive scope expire after seven days. Reconnect Google Drive in Files when prompted. [Google OAuth token expiration](https://developers.google.com/identity/protocols/oauth2#expiration)

`DRIVE_CONDITIONAL_WRITES_VERIFIED=true` enables Rename, Move, Trash and Restore for this reviewed deployment. Owner-approved live probe `63baae4f-32be-41c3-ad81-071489a2ab23` verified the atomic operation marker, a stale `If-Match` refusal with HTTP 412, and recoverable Trash and restore. If the provider, credentials, or conditional write path changes materially, turn the flag off until a disposable probe verifies the affected actions again. The optional `FILES_PROVIDER_VALIDATION_RUN` switch is only for an explicitly approved owner test.

## Usage safeguards

The default app caps are 50,000 request units per rolling minute, 10,000,000 per rolling day, 100 GB of upload reservations per rolling day, and 200 GB of download reservations per rolling day. The upload cap also bounds new individual files. `DRIVE_APP_LIMITS` can lower these caps. The ledger meters Files traffic through this Worker, including retries and conservative response-size reservations. Other clients and direct Drive use lie outside its measurement. Read back Google's actual project quotas before relying on the caps for a new project. [Google Drive limits](https://developers.google.com/workspace/drive/api/guides/limits)

## New-folder conflict recovery

Folder creation checks the requested name in the actual destination with the bounded, metered provider preflight before allocating an ID. If this Files journal has a recent known commit for the exact destination/name, the worker checks that provider identity directly, even if search is stale. While an earlier same-name Files operation is unconfirmed, a new operation is not dispatched; retry the original submitted operation to reconcile its receipt. An occupied name returns a typed pre-operation conflict. Changing the requested name starts a new intent; it never overwrites the existing folder.

This protects same-name folder creation through this Files worker. It does not create provider-wide name uniqueness against concurrent actions from other Google Drive clients, add copy/replace support, or claim that a timeout can be treated as a rejected write.

Restoring from Trash also checks the original live folder for the exact name before asking the provider to untrash. A conflict leaves both originals untouched and the trashed item recoverable; no replacement is implied.

Metadata edits also use one durable admission per submitted operation ID. Another request for that same intent cannot dispatch a duplicate conditional provider write while the first is pending. After an uncertain reply, retry the original operation ID to reconcile the receipt. Reusing an ID for different input is still refused.
