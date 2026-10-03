# Team B browser handoff — partial implementation, full parity blocked

Team B implemented source-based browser foundations, an account transport client over the existing CAKE ID protocol, and an evidence integrity validator. The isolated candidate now compiles and publishes with the real native rendering libraries. Actual local Chromium startup and route checks pass; Home visual layout fails, control accessibility and backend workflows remain unverified. No complete deployed browser app, authenticated cross-client workflow, approved full browser matrix or accepted release is verified.

## Candidate and reviewed source

The current B-owned validation branch is `team-b/astra-browser-validation-20261003`, code commit `83c24b2f0033fb2828169921fc905a327120143b`, based on Team A's provisional `a63d77fe5a9dfea56c938eaa85678a9e170368ec`. It is an isolated validation candidate. Team C remains the sole global release integrator; accepted head is null.

The original `team-b/sol-happy-browser-20261003` source history remains rooted in main `98a08827c9fbc486fe987da73f4aee8398120d31`. Its `4b6413a9d8b17b394daa7db74b05931cba8269f4` source and earlier evidence are retained. Team C's three reproduced review HOLDs on that original history are repaired on the current branch; do not integrate the held source without the repairs.

| Reviewed commit | Change and validation limit |
| --- | --- |
| `8730391bf528e80b16e4cbbebd1bc23ee03eb586` (replayed as `ef3cb6a`) | Strict evidence integrity validator; 32 unit negative controls. It never certifies semantic completeness or product parity. |
| `a9153bc7b9af600dab9cbe724e4cd6a46b8e62b2` (replayed as `edc2932`) | Existing CUI source bootstrap, typed owner routes/registry, history/private-context adapter; original 16 source-linked checks and 7 real DOM checks. Subsequent Home-owner dispatch HOLD is repaired below. |
| `4b6413a9d8b17b394daa7db74b05931cba8269f4` (replayed as `c1173a1`) | Existing CAKE ID account/profile/session wire transport; original 21 checks. Subsequent profile snapshot/revision HOLDs are repaired below. |
| `ebfa07b13966be508bca5a1a47f45da0ef8933ed` | Links the actual newer Home authority types into the regression graph; four added source-link lines, no changed assertions. |
| `f38ffac525e555921d580776086ceb37182ec681` | Snapshots validated profile fields before awaiting tokens; requires exact safe `expectedRevision + 1` success; 28 checks, independent red/green controls. |
| `d4c18169ac52fee2bd0ad47e39da8de7a2a001a0` | Uses the tested production dispatcher so registered Home owners run before fallback; denial/failure never fallback. Imports actual browser asset/GL contracts and existing CUI primitive helpers; 22 route/dispatch checks. Native-library omission in this intermediate build is corrected below. |
| `830f747b70df07797c586f6124890fa5028452b2` | Documents the actual SDK/pinned source prerequisites. Its first real browser startup FAIL is retained, not relabelled. |
| `83c24b2f0033fb2828169921fc905a327120143b` | Positions the owned root container so the actual absolute canvas/native input layers share an origin. The original trusted-pointer failure and isolated diagnostic are retained; fresh production replay recorded separately below. |
| `c74c73c94a0d548653790a1e9d2a899df88e67b3` | Uses the installed SDK `runMain` keep-alive API instead of exiting the UI runtime after `Main`; independent SDK contract review and actual browser replay recorded below. |
| `9697281a71a0e32dc761c2c53fd06c72a98ccb43` | Retains real SDK-selected native Skia/HarfBuzz inputs, removes only the owner's duplicate fixed variants, rejects absent native inputs, and reports startup errors. Actual link/PInvoke proof and independent metadata review retained. |

The host embeds canonical Home CUI and consumes owner contracts/runtime. Unregistered app surfaces and unavailable services stay unavailable. The client consumes the acknowledged Worker protocol at `codex/cake-id-auth-backup-20261002@7cc52df0411e0e9e617918f6c5158283cc3199fc`; it supplies no replacement identity, permission, entitlement, billing or job authority, and has no issuer/client/redirect defaults. Team C's maintained hosted patches require joint integration before use.

## Executed validation and failures

| Executed subset | Observed result and scope | Durable evidence |
| --- | --- | --- |
| Evidence validator | 32/32 unit cases; evidence consistency only | `evidence/coordinator/final-83-checks/validator-unit.log` |
| Actual route/registry/dispatcher source, newer owner contracts | 22/22 custom cases; fixture handlers, no backend authority acceptance | `evidence/coordinator/final-83-checks/navigation.log` |
| Browser platform Node fixtures | 7 custom checks; Node discovers one test file | `evidence/coordinator/final-83-checks/browser-platform-node.log` |
| Account transport | 28 discovered/executed, no failed/skipped; isolated loopback/fake wire fixtures | `evidence/coordinator/final-83-checks/account-api-node.log` |
| Earlier production JS DOM/history adapter in Chromium 151.0.7922.173 | 7/7, independent fresh-profile replay; dedup-removal fault causes two unchanged assertions to fail | `evidence/b6/chromium-dom/` |
| Reviewed Home/profile regressions | Old selector fails new owner test after 16 prior passes; old profile passes 21 old tests but fails 6/7 new cases; fixes independently pass 22/28 and unchanged B6 profile controls 2/2 | `evidence/b1/production-host/`, `evidence/b5/review-defects/`, `evidence/b6/dispatch-owner-review.json`, `evidence/b6/auth-profile-review/` |
| Corrected native build guard | Normal actual target exit 0; suppressing either native input exit 1 with exact required-library error; normal SDK inputs are two real st,simd libraries and one GL export | `evidence/coordinator/native-browser-correction/guard-results.json`, `evidence/b6/native-browser-review/` |
| Current exact source compile/publish | Exit 0; fresh output has 180 files, 35,339,902 bytes; actual native link and PInvoke tables include Skia/HarfBuzz | `evidence/coordinator/pointer-container-correction/browser-publish.json`, `evidence/coordinator/native-browser-correction/native-link-proof.json` |
| Actual published `830f747` browser startup | FAIL: 90-second timeout, Loading, zero canvas, native Skia initialization exception; dependent journeys BLOCKED | `evidence/b1/production-host/run-830f747/` |
| Actual published `9697281` browser startup | FAIL: runtime exited after `Main`, black canvas and stalled live navigation. Cold routes alone did not establish interactive UI; error storm also overflowed the first runner serialization. | `evidence/b1/production-host/run-9697281/runtime-exit-diagnosis.json`, retained trace/screenshots/HTML |
| Actual published `c74c73c` startup subset | 12/12 local startup/typed cold and live routing/history/status checks pass, zero managed/page/request failures. Drawn Home content overlaps: visual FAIL; control screen-reader semantics absent, accessibility unverified. Actual BFCache restoration not observed. No backend/app parity acceptance. | `evidence/b1/production-host/run-c74c73c/` |
| Actual published final `83c24b2` candidate | B1 14/14 startup, trusted enabled/disabled pointer, typed cold/live route/history/status checks PASS; geometry aligns, zero managed/page/request errors. B6 independently repeats the unchanged original failed pointer runner: 5/5 pass, including actual Library and Events clicks and disabled Studio. Home layout FAIL, control semantics absent, authenticated BFCache/services NOT-RUN. | `evidence/b1/production-host/run-83c24b2/`, `evidence/b6/production-smoke/pointer-83c24b2/`, `pointer-fix-independent-review.json` |
| Independent actual published `c74c73c` replay | 9/9 direct cold/live route/history/runtime checks, fresh Chromium profile; real pointer Library navigation then FAILS. Direct fragment checks do not establish pointer interaction. Visual layout FAIL and control accessibility absent. | `evidence/b6/production-smoke/run-c74c73c/`, `pointer-c74c73c/` |
| Unchanged historical Write domain probe | 4 cases, 21 assertions pass; false-assertion control fails; not browser/storage/donor acceptance | `evidence/b2/write-domain-evidence.json` |
| Unchanged historical Wave source probe | 28 self-test assertions plus 9/9 CLI checks; independent 9/9 replay | `evidence/b3/wave-local/`, `evidence/b6/wave-independent-reproduction/` |
| Historical Files executable on 98a | 11/11 custom checks, exit 0; no VSTest-discovery claim | `evidence/b4/files-result.json`, `evidence/b4/logs/files.log` |
| Historical 98a-based host / Sites / Application | Host graph/import failures; Sites/Application CS1593 before discovery, executed zero. These are not alleged failures on a63. | `evidence/b1/logs/`, `evidence/b4/sites-result.json`, `evidence/b5/test-results/dotnet-application.log` |
| Current-source acceptance evidence validator | INCOMPLETE, exit 1: 10,558 defects including three stale-source findings and zero discovered acceptance tests | `evidence/b6/current-source-evidence-rejection.json` |

The `830f747` bootstrap failure revealed that native flags suppressed the transitive libraries as well as duplicate owner inputs. The correction preserves the normal package flags and removes only owner-defined duplicate items. Initial misleading metadata, failed attempted corrections and their real errors remain visible. The failed run's trace, screenshot, console/request observations, source and before-hash manifest are preserved; the old incremental output directory is not claimed wholly immutable.

The `9697281` actual page revealed a second launch error: `dotnet.run()` deliberately exits the .NET runtime after the entry point returns. The current candidate calls `runtime.runMain(config.mainAssemblyName, [])`, whose installed SDK contract keeps the UI runtime alive. The failed black-canvas screenshots, runtime error, trace and separate runner serialization failure are retained. Canvas presence was rejected as a visual pass.

The subsequent genuine pointer test failed even though fragment/history tests passed: Library clicks could not navigate. Trusted-event geometry showed the canvas starting below the status bar while the native input host was anchored at the viewport origin. The isolated CSS diagnostic confirmed the cause, and `83c24b2` adds `position: relative` to the existing root container. A CSS-mutated diagnostic page is not shipped acceptance. Original red tests, event offsets, geometry and exact unchanged independent pointer runner remain preserved.

The first corrected publish reused an SDK output directory containing old content-hash files. A fresh unique destination was published and inventoried for actual testing; no source shell or replacement modules were copied over incomplete output. The earlier WASM0001 Win32 interop warning remains historical build evidence; no warnings or required thresholds were suppressed.

Local checks do not establish deployed workflows, supported-browser parity, mature donor behavior, private cache/session authority, accessibility or measured full-product performance. Controlled fixture BFCache events remain distinct from native lifecycle observations in the actual application runner. The final bundle is rehashed after both implementer and independent browser sessions: all 180 files match its original publish manifest. The same independent pointer test that failed on `c74c73c` passes unchanged on `83c24b2`; its exact runner hash and real event geometry are retained. Native-link/guard evidence retains its earlier exact commit, while the current native-input project source is byte-identical.

The actual current page has visible shared Home controls, but Dashboard content overlays other panels. Root and B6 independently inspected actual screenshots and rejected Home visual acceptance. Owner source evidence identifies the conditional renderer’s `Panel` at `CuiControlLoader.cs:476` and unwrapped Dashboard siblings at `Home.cui:27`; both files remain byte-identical to provisional a63. The precise owner request and hashes are in `evidence/b1/production-host/shared-render-owner-request.json`. Its accessibility tree exposes status and main without control semantics. Native `pageshow.persisted` remained false, so private BFCache restoration is not verified. Incidental favicon 404 and retained SwiftShader WebGL warnings are recorded separately from zero actual runtime errors.

Canonical CAS justification and independent review preserve the erroneous original profile fixture before correcting revision 1 to revision 2.

## Specification and coverage

The [live Google Doc](https://docs.google.com/document/d/1TJx-TNQTHI5hhriRG4ipRjG65ud1ZmPAIYWAsC63kIg/edit) remains the authority. Original maps identify their original revision in `evidence/source/manifest.json`. The last fetched revision is recorded in `evidence/source/recheck/manifest.json`, modified `2026-10-03T17:00:26.970Z`, text SHA-256 `117677d224cbd1d3f909cd63d47b137b04d5ed4cb7e97a7c012b5443a92baca1`.

B6 compared both complete snapshots: all 9,996 records below the future-pass boundary are identical, including text/indices; one future Study App placeholder changed and eight subsequent records shifted. Old maps/runtime evidence keep their original revision. They are deliberately rejected as current-source acceptance instead of silently being relabelled. Snapshots here are audit evidence, not a rewritten specification.

`app-platform-inventory.json` assigns 30 app sections and four Terra-form standalone sections: 33 default browser-required, one exact Mini Computer hosted-compute exemption subject to reevaluation. Launcher stays required while its native-purpose conflict is unresolved. Variants, account services and inherited shared frameworks remain additional capability obligations. Go is an OS primitive with reusable scoped contracts, not a silently exempted app. Future Training Lab/Study App placeholders do not expand this active release.

Complete deployed app workflows verified: **0**. The full mandatory/default-SHOULD/donor/platform semantic denominator is **unknown/incomplete**. The maps preserve requirement states and separate outcomes, but are provisional paragraph/clause maps. Paragraphs, method references, procedures and passing unit tests are not a conformance percentage.

## Shared coordination and remaining gates

Remote `coord/sol-happy-20261003` registers A/B/C. Team A explicitly handed global integration to Team C, which alone writes release/index/accepted-head/main. B owns browser code and this disjoint handoff directory; shared models/CUI/engines remain Team A owned. Only B's coordination record is updated with revision-guarded non-force pushes and remote readback.

Team A's a63 restores all ten previously missing direct browser build inputs. Exact XamlX `009d4815470cf4bf71d1adbb633a5d81dcb2bb52` and DBus `864a05282841bf04006890f04d11d60d1a046aa9` gitlinks were materialized in the isolated validation checkout. Existing source pin `fe2741d62a467c3efa4f0c5f68f7861bc338c99f` is unchanged. A1 acknowledged the maintained `CuiNativeHost.ConfigureFonts` / `InitialisePrimitiveTheme` APIs and exact Runtime dependency in remotely published coordination `5f945e3`; this is owner/source acknowledgement, qualified separately from WASM, mounted/provider or full-product acceptance. Source/provenance and integrated browser behavior remain distinct from compilation. B2's precise Write view/layout and nested-section gaps remain in inspected a63 source; its old domain probe was not relabelled as a63 behavior.

Required remaining gates:

1. Team C independently reviewed and closed the three original P2 source defects at `830f747`, passing 22 route/28 account checks; this is source-fix acknowledgement only. It must review the newer native/runtime fixes, reconcile A/B/C histories and reruns on its exact final integrated code/schema/configuration. Team A resolves the reproduced shared Home/CUI panel overlap and accessible-control exposure, and supplies acknowledged browser-compatible owner bindings and compatible Write view/layout/section contracts; no browser-only models are introduced.
2. Supply real authenticated Files byte-reference/revision transport, account/session/permission/AI/job/billing APIs, isolated accounts, exact staging frontend/backend/configuration identities and a tested native client. Protocol source/local Worker checks do not supply a staging service. Current issuer/.NET signing, resource/envelope and genuine authentication-revision disagreements remain owner contract work.
3. Resolve SH-13 supported browser versions, isolation, performance/recovery and affected commercial/operational decisions. One Chromium run does not establish a support matrix.
4. Make the required Sandbox plugin/skill/Operating Contract callable or resolve this documented capability gate. Exhaustive discovery exposed none.
5. Complete semantic/donor applicability and tests; run clean-profile deployed populated app workflows and real browser → desktop → browser continuity. Verify content, IDs, revisions, permissions/history, conflicts, moves/revocation/recovery, accessibility and declared measured workloads.
6. Verify the actual published test URLs and exact release artifacts. Production cutover, billable activation, live charges and WordPress retirement remain under explicit approval holds.

Team B performed no deployment, paid activation, live charges, production routing change, main merge or persistent execution. The local archive is an inspectable compiled bootstrap with routing and error-state behavior. Its shared Home visual defect, missing service bindings and unverified accessibility prevent a usable complete product or final release claim.

## Reproduction and provenance

Recorded environment: Debian 13 linux-x64, SDK 10.0.401, wasm-tools/runtime 10.0.12, Node 24.19.0, Chromium 151.0.7922.173. Tooling homes, caches, fixtures, ports and outputs were isolated. Bun.Unofficial.Tool 1.3.4 is an explicit root-selected tooling version; the donor did not pin the tool. The checked-in Bun lock and unchanged owner build.js were retained and hashed.

Use the current source branch/commit and materialized pinned gitlinks. Build the unchanged browser adapter webapp with Bun 1.3.4 `install --frozen-lockfile`, then `build.js`. Run the exact logged commands, including:

```sh
python -m unittest discover -s eng/browser/tests -v
node --test apps/Web/Tests/browser-platform.test.mjs
node --test apps/Web/Auth/account-api-client.test.js apps/Web/Auth/account-api-client.profile-review.test.js
dotnet run --project apps/Web/Tests/BrowserNavigation.Tests.csproj --configuration Release
dotnet publish apps/Web/NineToOne.Web.csproj --configuration Release --output /tmp/unique-browser-output
```

Serve only that actual published wwwroot, without substituted responses. The current compiled local archive and its hash are recorded in `evidence/coordinator/pointer-container-correction/local-artifact.json`; no staging URL is implied. Browser screenshots/traces and runner source identify actual local observations and blocked checks.

`package-manifest.json` maps copied artifacts to their original paths, exact bytes and hashes, and pins current B source separately from original source. Worker records preserve original absolute paths/revisions/commit states. Earlier copied metadata and runner sources remain unchanged; later worker revisions are retained separately under `evidence/updates/`, with exact original byte custody recorded. This includes the B2 reconciliation-manifest addition and the B1 runner’s added real-pointer checks. Fixed-path scripts need equivalent isolated paths or a documented substitution; source-linked historical probes must use their recorded source. Do not edit retained identities or relabel a prior run. Tooling/package caches, certificate stores, runtime binaries and mutable fixture directories are excluded from this evidence package. The local compiled bootstrap archive lives separately.
