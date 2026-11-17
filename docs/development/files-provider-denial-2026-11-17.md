# Files browser item-level Google 403 prerequisite — 17 November 2026

**Source baseline:** `4cc07363c8cece3dfd7d6961709677707e977e25` on `main`.
**Temporary exact ownership:** `cloud-files-provider403-1800`, recorded in issue #27, comment `6956423220`. Only the browser JS bridge, its new JS test file and this report are claimed. Local Strata owns native CUI/Worker, local live-owner checks and the desktop application. Another cloud slice owns the Files full-journey CI workflow. No such files are edited here.

## Reproduced defect and intended flow

A still-authorised 9to1 Files owner may lack access to one Google Drive folder or item. `cloud/files-drive/src/provider.mjs` maps Google's non-quota HTTP 403 to a typed `FilesHttpError(403, 'PermissionDenied', 'Google Drive denied access to this item.')`. `cloud/files-drive/src/protocol.mjs` serialises it with `notStarted:true` and `retryAt:null`. Previously `apps/Web/Files/files-browser.js` interpreted **all** 403s as owner session denial, revoked the entire request group, and called `onSessionLost`, even after `/context` had validated the owner. Other authorised folders then became inaccessible. The old production bridge was reproduced with the public API; in the new focused test file, the two legitimate list/get cases fail before the change while the ten other initial cases pass.

Required boundary: a currently enrolled owner, GET `list` or `get`, and **only** the exact typed, bounded Google item-permission response must preserve the JS request group and return the original response for native interpretation. Actual owner denial (`OwnerOnly` 403 / `SignInRequired` 401), un-enrolled groups, malformed, extra/unknown, oversized or slow 403s, and mutation/context/usage denials must remain fail-closed. A cancelled response must not wake an obsolete owner. Never grant any new permission or initiate another Google request.

## Implementation and test receipt

`files-browser.js` now limits a potential item-denial body's read to 4 KiB and 2 seconds. It requires the existing four response properties (`code`, `message`, `notStarted`, `retryAt`) with the exact Google item denial values. On that one typed GET-only case it passes through the genuine 403 body **without** revoking the group. Unknown 403s still use the original owner-denial path. It retains same-origin credentials, no redirects, cancellation/drain and all existing commands.

Run locally from repository root:

```sh
node --check apps/Web/Files/files-browser.js
node --check apps/Web/Files/Tests/files-browser-provider-denial.test.mjs
node --test apps/Web/Files/Tests/*.test.mjs
```

Recorded in a disposable Node 22.16.0 environment with source blob matching upstream preimage: baseline new test file **10 pass, 2 fail**, demonstrating the defect before modification. After patch, all **48/48** Files JS bridge tests passed (old 30 plus 18 new), including real `127.0.0.1` HTTP responses, malformed/oversized/stalled denial bodies, and cancellation, with 0 failures/cancellations/skips. Production/new test syntax checks passed. No .NET/native build or authenticated live Google Drive call was executed by this slice; those require the owning native/lifecycle worker.

## IMPORTANT: native end-to-end work remains

This is a transport prerequisite, **not a finished user-facing fix**. The current native `apps/Web/Files/FilesDriveBrowserProvider.cs` `CallAsync<T>` still throws generic `PermissionDenied()` for **every** 401/403 before `ReadError` can inspect the body, and `FilesBrowserController.cs` `IsDenied` clears the view for that generic error. Local Strata owns those native surfaces and should add an explicit *item-level* error mapping plus tests while preserving the existing owner-denial clear. Avoid mapping denied items to `ItemNotFound` or changing all 403 handling. End-to-end UI acceptance must verify the second authorised folder remains usable after a denied item, and actual 9to1 owner revocation still clears presentation. Do not call this change complete until that separate native patch is built/tested and the result is observed in a real browser.

No Cloudflare deployment, credentials, user data, PC/GPU, or local Strata source were touched here. An actual live Google Drive frontend requires its separately authorised deployment and owner-consented acceptance evidence; the synthetic provider tests do not supply that proof.
