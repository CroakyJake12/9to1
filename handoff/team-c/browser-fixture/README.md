# Receiver-owned local Worker fixture handoff

Source and runtime custody ACK: `aedc29ec3a0a960e1ded36c0443f1efda4fcf36d` on `team-c/sol-happy-c2-browser-fixture`. C1 independently reviewed the source, passed nine real Linux controls, and ran the actual Worker/D1 bootstrap with strict kernel reaping. This is ready for each receiver to launch its own isolated fixture. It is not completed browser/provider/native/.NET acceptance. No fixture listener, credential or database is delivered by this record.

Fetch that exact ref and create a receiver-owned detached worktree. Source paths and SHA256 are in `evidence.json`; never substitute earlier `4111`/`843` helpers. Read `cloud/cake-id-auth/BROWSER_FIXTURE.md` at that commit. In its `cloud/cake-id-auth` directory:

```sh
npm ci --cache /workspace/team-c/evidence/c2/npm-cache
python3 -B tests/linux-fixture-custodian.test.py
XDG_CONFIG_HOME=/workspace/team-c/evidence/c2/xdg node tests/browser-fixture.mjs
```

Receiver may choose its own writable cache/XDG paths. The actual launch capability preflight runs before secrets. Check ports and coordinate through root first: C fixture uses 8798, B HTTPS page uses 5096; 5095 remains unstarted. Public ready output supplies only configuration and a private manifest path. Read the manifest directly in the isolated test process, supplying its real generated accounts/client to the owner browser workflow; never print, screenshot, commit or send credentials, cookies, tokens, codes or the local key. Credentials and local D1 remain only in ignored restrictive files until shutdown. All accounts are verified random `example.test` fixtures; no real Jacob provisioning or external email.

Exact supported web callback is `https://client.example.test:5096/callback`. The maintained provider rejects loopback-host web redirects, including HTTPS. B must use browser-local host mapping to loopback and isolated test-certificate trust. No DNS/global trust changes or native-client relabeling are authorized. API origin and access-token resource audience are both `http://127.0.0.1:8798` using existing fixture knobs; historical integration's 5095 audience was not a listener. Use actual discovery, issuer/JWKS, S256 PKCE, exact callback and explicit resource; no bearer or actor fabrication. Existing browser adapter's explicit loopback test option permits this HTTP API.

Ctrl-C/SIGTERM asks the pidfd/subreaper custodian to stop. Only strict creator/descendant identity checks, kernel reaping, original-birth disappearance, an explicit receipt and successful custodian exit authorize state deletion. A failure retains state and remains HELD. Record final drain output and release port custody. Receivers own and stop their fixture; no persistent shared server is currently running.

Raw successful peer/local logs and historical failures are preserved here. The original SDK guard is unchanged; earlier live-quiescence/zombie classification was superseded, not accepted as strict reaping. Browser durable callback/profile/session tests are the next B continuation. Genuine `auth_revision`, strict .NET RSA interoperability, deployed delivery, native acceptance and the Files actor adapter remain unresolved.
