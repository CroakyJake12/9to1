# Files: sealed-candidate versus live hosted JavaScript (read-only)

17 November 2026. This is a **deployment identity check**, not a deployment, Google OAuth, or authenticated Files/Drive acceptance. The owner has observed that 9to1.uk still served an older Files browser asset while newer source and the Google Drive/Files-app hybrid layout were published to GitHub.

## Owner-visible flow

1. Obtain the **actual sealed** `files-web-candidate` GitHub Actions ZIP produced for an exact 40-hex source revision. Use the **build's source revision** from its seal, not a later `main` HEAD.
2. Independently validate its complete seal, manifest and all packaged bytes with the existing `files_candidate_release_gate.py` command.
3. Run `files_live_parity_gate.py` against `https://9to1.uk`. It repeats the full offline verification **before** any network request, then GETs exactly three public scripts, `main.js`, `files-browser.js`, and `browser-files-session.js`, without following redirects or sending cookies.
4. Require `CURRENT_ASSETS_VERIFIED` for three exact size/SHA-256 matches. `STALE_OR_MISSING` means public assets differ or are absent; `ACCESS_GATED_UNVERIFIABLE` is not evidence of a stale bundle.
5. Arrange separately approved deployment if required, then repeat the check. An exact browser script match still does not establish the Worker backend version, Google permissions, OAuth consent or usable Drive CRUD.

From the repository root (replace placeholders with the **actual** build SHA and downloaded artifact path):

    python3 -B eng/development/files_candidate_release_gate.py /path/to/files-web-candidate.zip --expected-commit ACTUAL_40_HEX_BUILD_SHA
    python3 -B eng/development/files_live_parity_gate.py --artifact /path/to/files-web-candidate.zip --expected-commit ACTUAL_40_HEX_BUILD_SHA --origin https://9to1.uk

No secrets, authentication headers, cookies, owner IDs or private file content are sent. Redirects are refused; if a private sign-in gate returns a redirect, that produces an access-gated result, never a misleading mismatch. Production mode only accepts the exact HTTPS origin `https://9to1.uk`. `--allow-loopback-fixture` is test-only for a localhost HTTP server, and may not establish any real deployment outcome. Each asset response is limited to 2 MiB and each connection to a bounded 1–30-second timeout; only identity-encoded JavaScript is compared. The tool never logs body bytes or redirect destinations.

Exit codes: 0 means all three browser assets match a seal-verified candidate; 2 means stale/missing/gated/unverifiable; 1 means invalid input. The output explicitly labels `filesAppAuthenticated`, `googleOAuth`, and `liveGoogleDrive` as `NOT_TESTED`, `deploymentPerformed:false`, and `readyToUseAsGoogleDrive:false`. A green result verifies **three public JS assets only**, not the whole WebAssembly bundle, Worker API, secure session, or live Google Drive.

## Executed verification

`test_files_live_parity_gate.py` contains 13 local stdlib/real-loopback-HTTP cases for exact match, stale bytes, missing assets, 302 refusal, 401/403, wrong MIME or encoding, oversize, unreachable network, URL origin/credential rejection, previously failing seal verifier, invalid manifest data, timeout constraints and separation of localhost from real production. For isolation, these 13 tests mock only the previously independently tested offline candidate verifier; the production script executes the **real** verifier. Existing 21 release-gate tests are still run by the combined CI discovery pattern.

    python3 -m py_compile eng/development/files_live_parity_gate.py eng/development/test_files_live_parity_gate.py
    python3 -B -m unittest discover -s eng/development -p 'test_files_*_gate.py' -v

The cloud container could not resolve GitHub or 9to1.uk directly and made **no real production HTTP requests**. It downloaded the genuine prior GitHub Actions artifact for source `8e7c562239cea8101269e6e809d3374fafcca590` using the connected GitHub tool, checked all 346 inner files against its manifest, then served its three actual JS assets over a disposable loopback HTTP server. All three exact-byte parity comparisons passed. That loopback exercise stubbed only the external release verifier import and does not pretend real production parity or owner acceptance. Fresh GitHub CI must be inspected separately before treating the integrated tests as verified there.

This scope is disjoint from local Strata's Files CUI redesign, Worker thumbnail protocol and local candidate launcher. No production deployment, Google Drive mutations, credentials, PC, GPU or automation schedule was touched.