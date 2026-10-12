# Local integrated account-theme acceptance

This lane runs the real built 9to1 browser application against the maintained local Workerd/D1 CAKE implementation. It does not establish hosted, native-packaged, donor or whole-app acceptance. Account bootstrap uses only generated example.test accounts and the existing local-test-only outbox/admin fixture. No external email or hosted API is used.

## Compatibility decision

Keep the actual app listener at `http://127.0.0.1:8765`. The pinned OAuth provider rejects a loopback-host callback for application_type=web, even over HTTPS. The maintained browser client also requires an HTTPS callback on its own app origin. Therefore the fixture serves those exact application assets through a loopback-only HTTPS proxy at `https://client.example.test:5096`, with registered callback `/callback`. Chromium maps that name only inside the isolated process. No DNS, hosts-file or system certificate-trust change is needed.

The maintained issuer/resource remains `http://127.0.0.1:8798`; its explicit existing isolated-loopback switch is used. The proxy injects only a same-origin public routing configuration script before main.js. It does not inject accounts, theme preferences, identity claims, cookies, bearer tokens or owner callbacks. The app's original PKCE/state/nonce/JWKS/current-account verification and sign-in lifecycle remain in control.

## Prepared source and current evidence

- `tests/theme-browser-acceptance.mjs`: executable custodied fixture wrapper, private certificate preparation, exact asset proxy and bounded lifetime.
- `tests/theme-browser-proxy.mjs`: loopback-only HTTPS asset host; only GET/HEAD; exact Host; no auth/cookie forwarding; actual index/main/WASM retained.
- `tests/theme-browser-signin.mjs`: optional browser-controller helper that reads credentials privately, triggers a real UI action, fills the real CAKE form and accepts the real consent popup. It emits no password, token or callback URL.
- Existing `tests/browser-fixture.mjs`: same maintained OAuth/admin bootstrap; 60-second startup bound for fresh Wrangler; optional `CAKE_BROWSER_FIXTURE_INCLUDE_ADMIN=1` adds the synthetic operator only to its private manifest.

Three proxy boundary tests passed, including real temporary Node HTTP/HTTPS listeners, unchanged WASM bytes, callback-code non-forwarding, credential exclusion and host/write rejection. These are proxy tests, not account-theme browser acceptance. No persistent Workerd, app proxy or integrated OAuth journey has been started for this handoff.

## Start after root clears the browser-runtime hold

First serve the exact integrated application artifact at port8765 and confirm its main.js/compiled assembly contains the coordinated account-theme adapter and all Home/CUI changes. Starting this wrapper does not repair or substitute a broken WASM artifact.

From the exact integrated checkout's `cloud/cake-id-auth` directory:

```sh
CAKE_THEME_APP_UPSTREAM=http://127.0.0.1:8765 CAKE_THEME_FIXTURE_MINUTES=60 CAKE_BROWSER_FIXTURE_INCLUDE_ADMIN=1 node tests/theme-browser-acceptance.mjs
```

Run with a retained stdin/PTY handle. No server starts during preparation or syntax/proxy checks. On actual execution, the wrapper checks exclusive local ports, refuses an existing .dev.vars, checks actual app HTML, then launches the original custodied browser fixture. That fixture creates actual local accounts, verifies them using local captured email, promotes only its synthetic operator through the existing key-guarded test endpoint, signs that operator in, and registers the public client through the maintained OAuth endpoint. It then writes the actual generated accounts/passwords to its mode0600 private manifest under the existing mode0700 random fixture directory. Credentials cannot exist before this bootstrap; no fake placeholder credentials were created.

The ready line supplies only:
- `privateManifestPath`: read inside the browser controller; never print.
- `publicLaunchPath`: public app URL, exact actual client configuration and Chromium arguments.
- local app URL and bounded lifetime.

The public launch arguments map client.example.test to127.0.0.1, disable proxy routing only for this isolated process, and trust only the generated certificate's public-key fingerprint in that process. Do not use a global ignore-certificate-errors switch or global trust-store change. The private TLS key, diagnostics and temporary launch metadata live in a separate mode0700 directory with mode0600 files.

## Real browser sign-in

Use a newly owned Chromium process with the exact `chromiumArgs` from the public launch file, then create two isolated browser contexts (or separate private user-data directories if process isolation is required). Keep each context's browser storage separate. Do not attach these flags to an existing unrelated browser.

Read account0's generated email/password directly from the private manifest in the controller. Do not send them through tool output, Playwright traces/API debug logs or screenshots. Do not preload storageState or add cookies/bearers. In each context:

1. Navigate to the actual application URL from the public launch file.
2. Open Home/Account settings through its actual UI.
3. Click the real Sign in control. Account settings' accessible name is `Open trusted CAKE ID sign-in`; use the actual accessible button, or inspected coordinates if the current Avalonia surface exposes only canvas input.
4. Wait for the actual popup. Enter the synthetic account into the maintained `#sign-in-form` (`#email`, `#password`), click Sign in, then click `#allow` on the real consent form.
5. The original callback owner posts the one-use callback to its original opener; the maintained parent validates code/state/nonce/signatures/account and closes the popup.
6. Inspect the actual account and appearance UI after completion. Do not treat popup closure alone as preference acceptance.

The optional `signInFixtureAccount` helper does steps3–5. Pass a `triggerSignIn` function performing the real UI click; do not dispatch native owner commands, call the JS sign-in owner directly, inject tokens or mutate theme state.

For Agent2's administrator registration-page checks, `syntheticOperator` is optionally present only in the private manifest. Sign it into the real local /sign-in page through the form before inspecting /account and clicking Connect Present. Those local privileges must never be reused as hosted authority. Both ordinary fixture accounts remain nonadmin.

## Acceptance matrix to execute

- ContextA/account0: save a builtin theme and brightness, All Apps accent, Individual Apps map, None defaults; navigate Present/Home repeatedly and inspect actual styling/scope status.
- ContextB/account0: sign in independently, reload saved appearance through the real UI, and observe the same server revision/theme/accent/custom library. Its browser storage starts empty.
- Create a custom theme through the actual creator in A; save through the original owner; reopen in B and inspect all selected fill/font/roundness options. Inspect rendered custom appearance, not only JSON.
- Open two observed drafts, save one, then submit the stale one: preserve the accepted result and show a revision refusal while retaining the unsaved draft.
- Offline A: retained same-account cached appearance may render, but saving cannot report success. Reconnect and use the real refresh action.
- Sign out A: private account scope/library must retire. Sign in account1 through the real form: account0's appearance/library must not become account1's saved preference.
- Let the actual five-minute access token expire, or revoke the session through the real Account UI; late completions must not restore the old account preference.
- Return to account0 in a fresh context and verify persisted authored themes survived signout/account switch. Recheck Present editing/notes/zoom and CUI screenshots at the required sizes.
- Read-only local D1/API evidence may corroborate the UI result. Never seed accountThemePreference rows or call PATCH directly in place of the UI acceptance actions.

Capture screenshots after successful authentication, safe request status/route summaries, current revision/mode and local-only provenance. Do not record cookies, Authorization headers, request bodies containing credentials, OAuth codes, or full storage dumps.

## Stop and preserve custody

Send the retained wrapper stdin the line `stop`, or Ctrl-C through its own PTY. The wrapper closes its owned proxy, asks the fixture to stop through its original stdin, and awaits the existing strict original-descendant reaping receipt. Only that receipt plus successful fixture exit authorizes secret/database deletion. A held or failed drain retains private files and is not success; do not kill unrelated PIDs or delete retained state blindly. The default60-minute deadline follows the same stop path.

Hosted blockers remain separate: the actual hosted theme migration/API, Present CORS entry, genuine administrator-created client, actual public host configuration and real hosted cross-browser account tests have not been established by this fixture.
