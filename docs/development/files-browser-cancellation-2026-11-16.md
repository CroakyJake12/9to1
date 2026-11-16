# Website Files cancellation repair - 16 November 2026

## Scope and custody

Base main: `4e6b02fac4bad8f5989e0a945722b1feda8d0ec1` (tree
`55026bda9de2d71f815b97328977155aa58ef5d1`). The owner explicitly resumed
independent edits and tests without Remote Desktop. This change uses a
Git-blob-verified source subset in an isolated container, not a full clone or
the PC checkout. No native coordinator was recreated or modified. Changes are
limited to the existing website Files bridge, regression tests and this report.

Before coding, the required flow was pinned: issue an owned request; cancel it;
drain any late reply without treating it as current authority; cancel and join
an in-progress JSON reader; continue to honour current server denials. This
follows the existing Files README and the stale-reply rule already present in
`wwwroot/browser-files-session.js`.

## Reproduced failures and repair

1. A cancelled request's late 401/403 could revoke an active private group.
   Check cancellation before handling a reply, drain its body and preserve the
   cancellation outcome. An obsolete response cannot trigger session loss or
   start reading private JSON.
2. Cancellation while reading a streamed JSON reply did not itself cancel the
   owned reader. Subscribe to the request signal, retain one cancellation task,
   join it before the request settles, remove the listener and release the lock.
3. With real HTTP fetch, revoking the current group also aborts its denying
   response body. An expected body `AbortError` could replace the typed 403.
   Keep the authoritative denial after revocation; unexpected cleanup errors
   remain errors rather than being silently suppressed.

Fixed same-origin routes, cookies, redirect refusal, JSON size/UTF-8 bounds,
mutation identifiers, upload limits and the unregistered context-probe rule
are unchanged. No server authorization or private storage implementation is
replaced. Cancellation does not assert that a server-side mutation was undone.

## Executed evidence

Runtime: Node.js v22.16.0. Tests import the actual production bridge, not a
reimplementation. Controlled response/stream cases and actual loopback HTTP
transport tests are distinguished below.

| Check | Original source | Repaired source |
| --- | --- | --- |
| Existing eight bridge cases | 8 passed | 8 passed |
| Thirteen new cancellation/stream cases | 4 passed, 9 failed | 13 passed |
| Three new real HTTP transport cases | 2 passed, 1 failed | 3 passed |
| Combined unique cases | Not used as a baseline claim | 24 passed; zero failed, cancelled or skipped |

The complete repaired suite also passed three further serial repetitions.
These are reruns of the same 24 cases, not additional distinct tests. Syntax
checks passed for the production module and both new test modules. The first
candidate's real-HTTP denial failure was retained before the final repair.

Run from the repository root using Node.js 22 or a compatible newer runtime:

```sh
node --check apps/Web/Files/files-browser.js
node --test apps/Web/Files/Tests/*.test.mjs
```

The loopback tests exercise genuine fetch, response-body cancellation and server
disconnection against disposable local HTTP state. They are not authenticated
Google Drive or deployed Files integration tests. The controlled regressions
also verify that oversized and invalid UTF-8 responses still fail and that
split UTF-8 survives a successful read unchanged.

## Evidence limits and continuation

A Chromium/Playwright attempt was blocked before its cases ran by
`net::ERR_BLOCKED_BY_ADMINISTRATOR` when opening the owned loopback fixture.
Browser policy was not changed. Browser execution, rendering and visual review
are therefore NOT VERIFIED by this change.

No .NET SDK was available in this container; no full application compilation,
native runtime test, live deployment check, or authenticated create/save/reopen
acceptance was run. The PC checkout, user profiles, GPU configuration and native
coordinator were not touched. This is a tested website Files transport repair,
not completion of Build/Sites authoring or Files product acceptance.

Next integration check: run the same scenarios in the actual browser host, then
continue the existing canonical Build/Files create -> choose destination -> edit
-> save -> close -> reopen flow. Keep its remaining feature and native/UI gates
separate from this transport evidence.
