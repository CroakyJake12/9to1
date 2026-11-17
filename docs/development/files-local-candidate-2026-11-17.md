# Files local candidate: real Worker + sealed WebAssembly on the development PC

17 November 2026, local Strata PC worker (issue #27 acknowledgement `#issuecomment-6951389578`).
This makes the existing Files application genuinely runnable on this machine with Google Drive
kept as the backend contract. It is not a new frontend, not a second storage layer, and not a
production deployment. Google OAuth, live Drive access and deployment stay separate, later tiers.

## Source identity

- Started from verified clean `main` at `3dd901e876f73a378dedd40407d828ee5658f518`, then
  fast-forwarded to `9cc997fd879ab1b3f22e68e5f939f2514e4b0fc2`; nothing local was ever ahead.
- Browser runtime under test: two bundles. First the sealed CI artifact `files-web-candidate`
  from run 44095155303 (`5d9191025…`, inner `wwwroot.zip` SHA-256
  `516eb05a34dd2b580f2b16c628adbea813bbe2d4bdbf88f211faae1d25ddb9db`). Then this checkout was
  rebuilt locally through the documented pinned path (`run-ordinary-web-publish.py` with exact
  SDK 10.0.401, wasm-tools, Bun 1.3.4): source commit `3f9a3db17`, 346 files, `wwwroot.zip`
  SHA-256 `fa61f9b1b7defc79b9d3d6748dad0a6916632ea2a9963dde4459f3c98f208668`, receipt
  `a3d95e121f003adee169d55ee9043122a82d75959dae3157febb268b1398fb48` — PASS with source and
  public custody verified. All Chrome evidence below was then re-run against the local bundle.

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

One command (opens the app visibly on this desktop with the fixture owner session):

```sh
cloud/files-drive/dev/run-local.sh   # optional first argument: sealed wwwroot directory
```

Or step by step:

```sh
# Optional: rebuild the sealed bundle from this exact checkout (pinned SDK 10.0.401):
DOTNET_ROOT=/tmp/files-dotnet-10.0.401 PATH=/tmp/files-dotnet-10.0.401:$PATH \
  python3 -B apps/Web/Tests/ci/run-ordinary-web-publish.py \
  --expected-commit "$(git rev-parse HEAD)" --output /tmp/opencode/files-web-candidate-local
cd cloud/files-drive
npm ci                                   # pinned toolchain
node dev/local-candidate.mjs \
  --wwwroot /tmp/opencode/files-web-candidate-local/sdk-publish/wwwroot --connect --keep
```

The `--open` mode launches its own visible Chrome; for a manual separate browser, point one at the app with
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
| Locally built bundle in real Chrome | all four CI-parity smokes re-run against `files-web-candidate-local/sdk-publish/wwwroot` | anonymous, disconnected, connected-root and navigation-journey all PASS |
| Full interactive journey | `dev/local-candidate.mjs --journey`: boot connected, folder search/clear, Grid/Details round-trip, create folder, rename (v2 strong-`If-Match` PATCH), resumable upload, download with byte-exact SHA-256 through Chrome's download manager, Google-native PDF export, move, Trash, restore, same-origin thumbnail route (owner 200, folder 404, non-owner 403), reload persistence, disconnect (revoke) and fixture reconnection, all through rendered native CUI accessibility peers | 15/15 PASS on the locally built redesigned bundle with canonical tile icons, screenshots retained per step, zero unexpected provider hosts |

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

Provider layer now additionally covers the same-origin thumbnail route (bounded
64 KiB metered `thumbnail` kind, image-type allowlist, fail-closed): Worker suite
90/90. Production asset drift independently confirmed by `eng/development/files_live_parity_gate.py`
against `https://9to1.uk` on the current sealed candidate: `sealedArtifactVerified=true`,
`publicAssetsMatched=false`, `status=STALE_OR_MISSING` — deploying reviewed current
assets remains a separately approved owner action.

Coordination: repair and dev tools receipted in issue #27; scopes released on publication.

## Haven interactive development instance (same day)

The current native app (modern Boards, Files and the rest of main) is running as
the documented development copy, built from `9abbb98b1441` with
`guarded_lowmem_build.py` (OK, 157.6 s, peak 598 MiB) into the frozen slot
`~/.local/share/9to1-development/runtimes/9abbb98b1-haven-1117`, approved by
`live_preview.py prepare` (`approved-haven-1117.json`) and started through the
reviewed recovery of the retained 28 October failed exit
(`REVIEWED_FAILED_EXIT_RESTART_NOT_NORMAL_RELOAD`). The app's own log shows
`integrity-check-passed` (schema 29) and `startup-complete`.

Relaunch after a normal close:

```sh
export DISPLAY=:1 DOTNET_ROOT=/home/jacob/.dotnet
python3 -B eng/development/live_preview.py start \
  --state-root "$HOME/.local/state/9to1-live-preview" \
  --manifest "$HOME/.local/state/9to1-live-preview/approved-haven-1117.json"
```

After an abnormal exit the state demands a fresh reviewed `recover-failed` using
the recorded `launch.json`/`exit.json` hashes, the same pattern as the retained
`haven-1117-failure-review.json`.

Close the window normally; the helper records the clean exit. `reload` (one-bug
iteration without logout) additionally needs `wmctrl`, not currently installed —
stop/start cycles work without new packages. The installed `/usr/lib/haven`
package and its data are untouched; this copy uses the dedicated development
profile only. Build/run receipts are private under `~/.local/share/9to1-development`
and `~/.local/state/9to1-live-preview`.
