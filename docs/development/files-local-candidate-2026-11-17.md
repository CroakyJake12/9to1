# Files local candidate: real Worker + sealed WebAssembly on the development PC

17 November 2026, local Strata PC worker (issue #27 acknowledgement `#issuecomment-6951389578`).
This makes the existing Files application genuinely runnable on this machine with Google Drive
kept as the backend contract. It is not a new frontend, not a second storage layer, and not a
production deployment. Google OAuth, live Drive access and deployment stay separate, later tiers.

## Source identity

- Started from verified clean `main` at `3dd901e876f73a378dedd40407d828ee5658f518`, then
  fast-forwarded to `9cc997fd879ab1b3f22e68e5f939f2514e4b0fc2`; nothing local was ever ahead.
- Browser runtime under test: the sealed CI artifact `files-web-candidate` from run 44095155303,
  built from `5d9191025bdf1d680e2d02143a877d544bbd87fe` (inner `wwwroot.zip`
  SHA-256 `516eb05a34dd2b580f2b16c628adbea813bbe2d4bdbf88f211faae1d25ddb9db`, 346 files).
  The two product-relevant commits between that build and current main are CI/test-only for the
  browser bundle; the local .NET WASM rebuild from this checkout is recorded separately below.

## What this is

`cloud/files-drive/dev/local-candidate.mjs` serves the sealed `wwwroot` through the genuine
`cloud/files-drive` Worker (`src/worker.mjs`, unchanged) inside local Workerd via Miniflare, with
the real Durable Object SQLite state, real same-origin `/api/files/v1/*` routes and a disposable
in-memory Google Drive fixture (`local-drive-fixture.mjs`) covering the exact Drive v2/v3 surface
the Worker uses: token/revoke/certs, metadata lists, `generateIds`, client-ID creates, v2 PATCH
with strong `If-Match`, trash/untrash, move, exact-name queries, resumable and multipart uploads,
ranged media reads and exports. A machine-local TLS listener serves the exact
`https://9to1.uk` origin that the Worker pins; Chrome maps only that hostname to it. No real
credentials, no Google network egress, no production contact, and the fixture refuses every
request outside its own endpoints.

## Run it

```sh
cd cloud/files-drive
npm ci                                   # pinned toolchain
node dev/local-candidate.mjs \
  --wwwroot /tmp/opencode/files-web-candidate-run9/wwwroot --connect --keep
```

Open the printed Chrome with
`--host-resolver-rules="MAP 9to1.uk:443 127.0.0.1:<tlsPort>"` and
`--ignore-certificate-errors` (both are dev-only; the certificate never leaves this machine),
set the owner fixture session cookie shown in the SERVING receipt, click
`Connect Google Drive`, and complete the fixture callback the same way
`dev/local-candidate.mjs --journey` does. All state is under a fresh `/tmp/opencode`
directory and is discarded on exit.

For the automated evidence run:

```sh
node dev/local-candidate.mjs --journey --report /tmp/opencode/local-journey-report.json
```

## Executed evidence (this PC, 17 November 2026)

| Layer | Execution | Result |
| --- | --- | --- |
| Worker/provider | `npm test` in `cloud/files-drive` (real Workerd + SQLite, disposable fixtures) | 87/87 PASS |
| Native Files contracts | `dotnet run --project apps/Web/Files/Tests/FilesBrowser.Tests.csproj -c Release` on SDK 10.0.301, isolated disk | 26/26 PASS |
| Sealed bundle in real Chrome (CI parity) | `smoke-public-files-runtime.py` anonymous and disconnected; `smoke-connected-files-runtime.py`; `smoke-files-navigation-journey.py` with Chrome-for-Testing 157 | all PASS, native peer evidence |
| Repair that unblocked it | `test_script_loaded_from_separate_test_file_without_modifying_published_assets` deterministically errored on `3dd910e87` (re-ran the pristine-probe guard after `serve_connected` had already extended `smoke.PROBE`, and double-mutated it); now reads the exact injected served text and additionally asserts on-disk bundle bytes stay unchanged | full routing-validation suite green; CI run 44100832836 was red exactly here |
| Full interactive journey | `dev/local-candidate.mjs --journey`: boot connected, create folder, rename (v2 strong-`If-Match` PATCH), resumable upload, download with byte-exact SHA-256 match through Chrome's download manager, move, Trash, restore, reload persistence, disconnect (revoke) and fixture reconnection, all through rendered native CUI accessibility peers | 11/11 PASS, 11 screenshots retained, zero unexpected provider hosts |

## Explicit limits

- `synthetic-provider tested` only. The Drive side is a fixture; this proves the app, Worker,
  DO state, protocol, bridge and CUI journeys, not Google consent or live Drive behaviour.
- The 9to1 Account session is the existing workerd test fixture DO, not real sign-in.
- Production deployment, real OAuth and live read/write verification keep their separate
  approvals (see `files-candidate-local-release-2026-11-17.md` for the artifact gate).
- Headless Chrome never reports `Page.fileChooserOpened` for the app's programmatic picker;
  the journey attaches the disposable file to that same real input when interception is silent,
  which is a harness workaround, not a product change.
- The in-app `Connect Google Drive` button navigates to the real
  `https://accounts.google.com/o/oauth2/v2/auth`; do not click it in the local browser — the
  fixture callback is completed by `connect()` as the journey does.

Coordination: repair and dev tools receipted in issue #27; scopes released on publication.
