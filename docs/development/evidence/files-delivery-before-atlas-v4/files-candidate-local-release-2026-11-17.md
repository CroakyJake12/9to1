# Files candidate: offline integrity gate and safe local extraction

17 November 2026. This is a release-preflight utility, not a Google Drive login, alternative frontend, production deployment or native user acceptance. The canonical application remains apps/Web/Files, backed by cloud/files-drive. Google Drive remains the storage provider.

## Owner flow, pinned before implementation

1. Obtain the existing GitHub Actions files-web-candidate ZIP, identifying the exact compiled commit from its successful workflow.
2. Run a local, standard-library-only verification of that archive and the expected full Git SHA, without credentials, network access or modifying source.
3. Reject a mismatched seal, dirty source receipt, unexpected or dangerous outer/inner ZIP members, symlinks, traversal, duplicate paths, oversized assets, missing WASM/bootstrap or any SHA-256/length mismatch.
4. Optionally extract the verified browser files to a fresh local folder, refusing preexisting destinations.
5. Keep the evidence tiers separate: archive verified, static files HTTP-served, full browser runtime, owner account, Google consent, real Drive reads, approved deployment. Only the first two are supported by this tool and its separately recorded HTTP smoke.

The cloud claim is separate from the local Strata agent's interactive Files navigation-test claim. The local worker owns its own source, runtime/SDK custody and harness repair. This utility does not edit the product controller, the provider, the Worker, any navigation harness, user files, secrets or the owner's PC.

## Verified candidate and use

Download from GitHub Actions run 44095155303, artifact 14333663133 (retained through 24 November 2026):
https://github.com/CroakyJake12/9to1/actions/runs/44095155303/artifacts/14333663133

The artifact's exact built SHA, NOT necessarily current main, is:
5d9191025bdf1d680e2d02143a877d544bbd87fe

From the repository root, with the GitHub-downloaded outer ZIP at the indicated path:

    python3 -B eng/development/files_candidate_release_gate.py /path/to/files-web-candidate.zip --expected-commit 5d9191025bdf1d680e2d02143a877d544bbd87fe

Only when the destination directory does NOT exist:

    python3 -B eng/development/files_candidate_release_gate.py /path/to/files-web-candidate.zip --expected-commit 5d9191025bdf1d680e2d02143a877d544bbd87fe --extract /path/to/existing-parent/files-wwwroot-5d919102

Optional STATIC asset-only loopback check after extracting:

    python3 -m http.server 8765 --bind 127.0.0.1 --directory /path/to/files-wwwroot-5d919102

Do not mistake this localhost asset server for Google Drive functionality. The application's genuine Account session, OAuth callback, encrypted token custody and Files API are restricted to the real private https://9to1.uk Worker and same-origin environment. A real Drive frontend additionally requires separate approval for a reviewed deployment, configured owner/account and OAuth, then actual owner Google consent and a read-only live Drive listing test. Never copy credentials to the local preview or bypass permission checks.

## Executed evidence for this isolated release slice

- Python syntax compilation: PASS.
- Local stdlib unittest suite: 21 PASS, 0 FAIL. Genuine disposable positive and adverse ZIP fixtures cover wrong expected source, forged seal/receipt, payload mismatch, symlink, duplicates, traversal, Windows-like path, private filenames, missing WebAssembly, unclean receipt, incorrect aggregate, refusal to overwrite, false runtime/deployment seal flags, corrupt outer ZIP, CLI success/failure.
- Real GitHub Actions 44095155303 ZIP processed by this exact utility: PASS. Full receipt/inner ZIP checked; 346/346 exact byte and SHA-256 matches, 0 missing/extra/mismatch, 87,450,548 uncompressed bytes. Actual inner ZIP SHA-256: 516eb05a34dd2b580f2b16c628adbea813bbe2d4bdbf88f211faae1d25ddb9db. Actual manifest SHA-256: 21eaf7b1c7e6e503a68b4d4ab5a2025a74ac790aaf462c35662726ec7a6d40fb.
- Actual ZIP safely extracted to a fresh disposable folder in the cloud container; 346 extracted files. Four distinct loopback HTTP GET requests successfully served index.html, main.js, _framework/dotnet.js and files-browser.js with appropriate content types and 200 responses.
- A repeated extraction onto the existing output folder returned FAIL and did not overwrite it.
- Earlier CI 44095155303 compiled genuine .NET/Avalonia/CUI WebAssembly and ran actual Chrome against a synthetic account/provider. It does NOT prove any real Google OAuth, Account session, live Drive operation or deployment.
- After that passing candidate, main advanced to the navigation-harness-only 3dd901e8 commit; its CI 44100832836 was red at a deterministic isolated fixture assertion when this release-tool slice began. The acknowledged local Strata worker owns that separate repair, not this cloud scope.

## Current readiness and release boundaries

This tool's JSON always reports artifactIntegrityVerified=true only after every byte passes. It always reports browserStartupVerifiedByThisTool=false, googleOAuthVerifiedByThisTool=false, liveDriveAccessVerifiedByThisTool=false, productionDeploymentVerifiedByThisTool=false, readyToUseAsRealDrive=false, regardless of assertions stored inside the sealed source artifact.

The original ZIP may be verified and extracted locally now, but it is not an independently authenticated Drive frontend. Complete the existing navigation Chrome acceptance, obtain permission for deploying exact reviewed source to the actual private Files Worker, then verify real sign-in/Google consent and read-only Drive listing before authorising any live-data writes. The separately scheduled 19:00 handoff should distinguish built source from deployed and usable Drive access. Never present synthetic fixture success as genuine Google Drive acceptance.

Coordination: source ownership/release receipt in existing GitHub issue #27. No new scheduling, credentials, Cloudflare mutation, hardware/Strata process change, or production deployment by this work.
