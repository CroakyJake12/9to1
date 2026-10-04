# Current receiver local PKCE execution proposal

This is a new bounded test of the existing browser-WASM JOSE broker and actual native CUI account surface. It does not reconstruct Team B's missing auth07 driver, relabel anonymous interop7, grant simulated identity, or prove hosted identity, strict .NET RSA/auth_revision authority, owner adoption or global app acceptance. No product, provider, fixture implementation, browser custodian or publisher source changes are included.

The driver reuses the delivered complete `sealed-https-host-explicit-binding.cjs` and `run-owned-account-replay01.py`/`owned-kernel-child-proof01.py` unchanged. The browser custodian's original **unconfigured** caption remains in its raw receipt. Its kernel/held-child proof applies to the newly issued Node/Chromium family only; it never grants custody of the separate issuer fixture. Root alone holds the fresh issuer's original execution session, starts it, sends its known `stop\n`, and verifies strict drain. Original B8798 and B8799 held sessions are separate and remain untouched.

The first actual execution of driver73b0b8c against the retained df547b5c publication is preserved: two PASS, one FAIL at `actual-issuer-signin-form` (`Error`,411ms), four dependent NOT_RUN. Browser startup and immutable normal closure passed. Discovery/JWKS counters incremented only HTTP200, so their zero values do not establish that no request was attempted. The redacted failure does not establish an implementation cause. The original popup, fixture and browser are not reused by this diagnostic successor.

This successor adds only observation: a fixed form-error category (`TARGET_CLOSED`, `TIMEOUT`, `OTHER`), popup close phase and navigation-origin categories, discovery attempt/finish/failure counts and all numeric HTTP status counts, plus at most32 fixed public `#browser-status` code transitions. Unknown codes become `OTHER`; no DOM text or raw error/URL is retained. A read-only MutationObserver observes the existing status element and never overrides product exports, broker callbacks, network responses, rendering or authentication. All seven assertions and successful-protocol counters stay unchanged. These diagnostics can distinguish a pre-discovery failure from rejected metadata; they do not establish authentication success.

An additional actual-browser receiver control calls native `fetch` with the guaranteed malformed absolute URL `http://[` using the Window receiver and a separate plain-object receiver. URL parsing must reject that string before either call; an unexpectedly parseable URL refuses both calls. The control has no usable endpoint, network request, credentials, token or broker hook. It reports only whether the function looks native, synchronous throw versus rejected promise, and fixed `ILLEGAL_INVOCATION`/`INVALID_URL`/`OTHER` classifications. This can establish native function receiver behavior separately from the integration failure; it does not preclaim the broker's actual failure cause.

Original unrun diagnostic9e4769a3 is retained. Its source verifier read a selected path before its regular/no-symlink/realpath guard. This successor moves that guard before the read and uses it for the six test-body reads. A synthetic redirected-source read counter proves refusal before target contents; no real private target is used.

The actual dde012fa diagnostic result is separately preserved: setup-only zero PASS/one cleanup-outcome FAIL/six NOT_RUN, after its own private-fixture contract and sealed HTTPS server creation, before a Chromium version was returned. No fetch/popup/auth observation ran. Its68-byte configured TMPDIR would produce a113-byte Chromium singleton socket path. Matching Chromium151.0.7922.173 POSIX source uses `TMPDIR/org.chromium.Chromium.XXXXXX/SingletonSocket`, requires less than108 bytes on Linux, and emits `Socket path too long:` on refusal. This establishes an unsafe configuration, not the redacted exception's actual cause. The helper now refuses more than62 UTF8 bytes for its canonical execution-root `/tmp` directory before creation or tool/private-file use. The driver checks actual canonical TMPDIR before private read/Chromium spawn. Root selects a short new execution root; no previous execution path or state is changed.

Setup now records exact fixed substeps for source/seal gates, explicit tool contract, result-directory creation, socket path, private contract read, Playwright import, TLS metadata, sealed HTTPS server start, Chromium launch/version/context, public configuration/status observation, local network routing/observers and page creation. A caught launch exception yields only `SINGLETON_SOCKET_PATH_TOO_LONG` when both exact `Socket path too long:` and `SingletonSocket` signatures match; everything else becomes `OTHER`. No arbitrary launch log, private path, raw error or credential is emitted. Chromium arguments, source/head gates, seven acceptance criteria, TLS verification and original browser/issuer custodians stay unchanged.

`source-contract.json` pins the complete current bodies of thirteen actual public broker/bootstrap/CUI owner/consumer files and all92 tracked maintained local issuer files. They come from receiving source `a9dc1a80499b7ddfd925ac7f816633abfb523b0e`; all105 bodies are conserved in the retained df547b5c producer. The publication candidate, test-driver checkout and fixture checkout have separate explicit commit arguments. Actual Git HEAD and every selected working-tree body must equal the supplied commit's Git body. The thirteen public bodies remain bound to the original producer catalogs as well as the current test checkout; all92 fixture bodies remain bound to their own original contract. The six complete test files are pinned to the actual test checkout HEAD and into the browser custodian's before/after inventory. A newer test commit never relabels the original df publication or changes its receipt. No Web rebuild is needed when all105 bodies match this contract.

## Gates before execution

1. Root receives the new producer's **actual** original `wwwroot.zip`, `publish-manifest.json`, `seal.json`, plus unchanged `diagnostics/source-before.json` and `source-after.json`. Bind the actual immutable candidate commit and independently received SHA256/size tuple. No previous B22d bundle or receipt-only substitute is permitted.
2. Root selects a fresh separate checkout containing the exact maintained fixture source. Its existing `cloud/cake-id-auth/node_modules` must resolve to reviewed available dependencies: Wrangler4.146.0, better-auth1.7.7, oauth-provider1.7.7, JOSE6.2.12. No installation is performed by this proposal. Preserve package caches and existing source.
3. Root runs the existing Linux authority preflight and existing fixture using a new original tool session with input held open. `.dev.vars` must be absent before startup. Use port8799 only; client5096 is separate and5095 stays unused. This creates only new fictional `example.test` identities/local D1 email capture and an actual public PKCE client. It does not activate Cloudflare or use a B session.

```sh
cd '<fresh-fixture-checkout>/cloud/cake-id-auth'
python3 -B tests/linux-fixture-custodian.py --check
XDG_CONFIG_HOME='<owned-private-config-directory>' CAKE_BROWSER_FIXTURE_PORT=8799 node tests/browser-fixture.mjs
```

Run the second command through a **fresh `exec_command(tty:true)`**, save that returned session identifier and its public READY path, and retain the original input. Never print the private manifest's client ID, passwords, local key, tokens or account/session UUIDs. The preparation helper carries its exact path opaquely and never opens it.

4. With the actual READY path and actual retained artifacts, prepare a fresh private root. All supplied SHA values refer to original received bytes, not modified local receipts:

```sh
python3 -B apps/Web/Tests/LocalAuth/prepare-local-auth-plan.py \
  --candidate '<actual-new-producer-40-hex-commit>' \
  --test-source-commit '<actual-reviewed-diagnostic-test-checkout-40-hex-commit>' \
  --fixture-source-commit '<actual-fresh-fixture-checkout-40-hex-commit>' \
  --manifest '<received>/publish-manifest.json' --manifest-sha '<actual-original-receipt-SHA256>' \
  --seal '<received>/seal.json' --seal-sha '<actual-original-seal-SHA256>' \
  --archive '<received>/wwwroot.zip' --archive-sha '<actual-original-ZIP-SHA256>' \
  --source-catalog '<diagnostics>/source-before.json' \
  --source-catalog-after '<diagnostics>/source-after.json' \
  --fixture-checkout '<fresh-fixture-checkout>' \
  --fixture-manifest '<exact-own-READY-private-manifest-path>' \
  --execution-root '<fresh-owned-private-execution-root>' \
  --node /opt/codex/runtimes/codex-primary-runtime/dependencies/node/bin/node \
  --playwright-module /opt/codex/runtimes/codex-primary-runtime/dependencies/python/lib/python3.12/site-packages/playwright/driver/package \
  --chromium /usr/lib/chromium/chromium
```

The supplied Chromium is the genuine executable, rather than `/usr/bin/chromium`'s launcher. Existing Playwright1.62.0 is used without downloads. The helper verifies original producer tuple/full ordered ZIP/CRC/body inventory before extraction. It conserves every body and changes only `publishRoot` in a separately named local receipt; the original receipt/seal/ZIP remain pinned. Complete source catalogs must be equal, recorded changes empty, and current consumer plus all92 issuer source bodies exact. Invalid version/hash/source gates precede any private fixture read, browser import/listener or request.

The helper creates a0700 TLS directory/0600 key with exactly `client.example.test` SAN, checks current validity and derives the public SPKI. Chromium uses only that SPKI exception, never global certificate bypass. It pins original artifacts, source, public installed browser/Playwright bodies and tools. The plan retains a240-second deadline,192,000,000-byte output/temp cap,384,000,000-byte launch floor and256,000,000-byte live reserve from the unchanged custodian. No browser or issuer starts during preparation. Failure retains its fresh preparation directory for review.

The explicit invocation clears `DEBUG`, `PWDEBUG`, `NODE_DEBUG` and `NODE_OPTIONS` before the custodian starts Node. Preparation subprocesses and browser launch use the same guard; the driver also clears diagnostics before importing Playwright. Inherited PW API debug output must never print credential-filled form values. Original proposal `d9d143d5` remains unrun and preserved; this source successor closes that diagnostic path.

5. Root reviews `invocation.json` and the exact printed `shellCommand`, then executes that command once. It invokes the existing original browser custodian with an independent plan hash and the new driver; the original issuer tool session remains held separately. The driver reads only the exact own0700 run/0600 private manifest after public gates. It checks fixture/fictional identity/discovery contracts locally and never exports their values.

## Seven new bounded cases

- Actual mounted native CUI is Ready within a single45-second startup budget, has no private fields and enables its configured Sign in button.
- A physical click in the real native canvas opens the actual issuer sign-in form. Own fictional credentials are filled locally, real consent is allowed, and the existing consumer performs a genuine S256/code/state/nonce/resource-bound PKCE exchange, JOSE checks and server self verification. No broker/native override, generated token or manual bearer request is used.
- The actual freshly registered native private owner automatically reads current/profile/sessions. Real200 response bodies are compared in memory with the newly created fictional subject; only booleans/counts are retained. Native private fields must actually appear.
- Physical native review and confirmation cause the existing authenticated client to clear private views and receive an actual server204 signout acknowledgment.
- The retained old native peer is refused; a physical tokenless Check causes no new bearer request/private grant. The subsequent actual owner/broker join also verifies that request count stays unchanged.
- Actual `CloseShell` returns true after its native/JS broker join, native AX becomes empty, the semantics projection is removed, and repeated close remains true.
- Context, Chromium and HTTPS server close normally; client5096 can be rebound and closed. Whole original assets, source, private fixture bytes/identity and TLS key metadata stay unchanged. The separate unchanged custodian must additionally show exactly one held top Chromium launcher, no forced cleanup, no live original family and real final kernel ECHILD.

Failure stops dependent cases and preserves FAIL/NOT_RUN. Reports contain only known scope, case/stage/type, counts, source hashes and booleans. No raw exception text, URLs/queries, request headers, bearer/token response bodies, screenshots, traces, DOM dumps, credentials or UUIDs are emitted. Network is restricted to the exact local issuer and HTTPS client origins. Rendering/geometry failure remains failure; no hardcoded click coordinates, font fallback or oracle weakening is allowed.

## Issuer shutdown and evidence

After the browser custodian returns, Root sends **`stop\n` through the same freshly issued original fixture `write_stdin` session** and waits for real exit0 and DRAINED `strictReaped:true`/`originalsDisappeared:true`. The existing helper deletes only its own matching vars/run after strict proof. Unknown session, nonzero exit, missing proof or browser forced cleanup means held failure; retain state and never signal guessed PIDs/PGIDs, write foreign FDs or delete old state.

Publish only selected new `results/results.json`, browser custody receipt, safe runner stdout, invocation/public-binding/plan source hashes and original public artifact/source custody. Exclude the entire issuer checkout's `.local-run`, `.dev.vars`, DB/logs/private manifest, TLS key, browser profiles/temp/cache and package trees. Original negatives remain independently retained. A driver pass plus both actual custody receipts establishes only these seven new local browser/native-CUI cases; it cannot close .NET authority or global/native/hosted acceptance.

Closest source/preflight checks:

```sh
node --check apps/Web/Tests/LocalAuth/run-local-auth-current.cjs
node --test apps/Web/Tests/LocalAuth/preflight.test.cjs
python3 -B apps/Web/Tests/LocalAuth/preparation_test.py
```

These are refusal/inventory/privacy guards, not simulated sign-in or browser acceptance. The diagnostic successor's actual seven-case execution remains **NOT_RUN** until Root binds the conserved original published bodies to this independently reviewed test commit and starts another own fresh issuer. The first73b0b8c failure and both original custody closures remain historical evidence.
