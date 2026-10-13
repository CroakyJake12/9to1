# Independent P0 release/fixture isolation review

**Qualified result: PASS for the reviewed release-support guard and fixture labelling scope. No full product acceptance.** No blocking source defect remains in this bounded review. No production/account data was read, deleted, created or modified by the reviewer. All injected leak samples lived in disposable temporary directories and were removed.

## Release guard

Reviewed `apps/Web/Tests/verify-production-isolation.cjs`, its controls, integration in `verify-publish-receipt.cjs`, and the ordinary SDK publication runner. Exact final source hashes are in the adjacent JSON receipt.

Independently executed the final guard suite: **22/22 PASS** (`evidence/p0-production-isolation-independent-3/report.json`). Actual immutable checkpoint35 had346 files, including230 gzip/Brotli variants, with zero known reference fields. The47 restored project references were checked against the actual expected entry project. Exact original asset inventory and project.assets.json remained unchanged.

Meaningful negative controls inject the fixture backend marker under an ordinary filename in UTF-8/UTF-16LE/UTF-16BE, raw/gzip/Brotli; renamed gzip magic; fixture assembly ownership without text; split reference fields across separate assets; malformed compression; a transitive visual project; a graph from a different entry; linked root Compile input; and an otherwise internally consistent publication receipt. Each was refused. Genuine Qwen model names and genuine CUI/Visual code/assembly paths are allowed. A generic Visual blacklist was removed before this final run.

Additional independently authored controls **3/3 PASS** prove the final path normalisation: a legitimate checkout beneath a parent directory named Tests is allowed, while its actual relative Tests source and test-assembly ReferencePath are still refused. These are controlled metadata cases, explicitly not real SDK output.

The gate is in both normal publication before sealing and the existing receipt verifier, so receipt verification scans the exact transported bundle again. Compressed decoding failures and scan-budget overruns refuse qualification. The script scans only explicit public assets/build metadata, never user/account/profile stores.

Limits: this is a known-fixture release guard. It is not a detector for every form of runtime fabrication, arbitrary encoding or renamed semantics. Brotli expansion follows .br assets; gzip also recognises magic bytes. Project-reference closure is checked; the new real SDK root Compile/ProjectReference/ReferencePath capture must run at the next ordinary publication. The current independent actual closure receipt truthfully says rootCompileAndReferenceItemsChecked=false; only negative/controlled evaluated-input cases have run. It does not claim recursive evaluation of every transitive project's Compile items.

## Fixture executable and reporting boundary

Reviewed the P0-only patch against0e24d109 (final Program SHA9be66803029f...), separately from unrelated pending reveal/layout edits. The first executable statement refuses a missing --visual-test-fixture flag with an explicit warning and clean exit2 before reading fixture data or creating output. The project is not packable/publishable and remains excluded from the normal Web compilation. Fixture backend data remains unchanged and useful only in this separate automated visual environment. There is no production route, repository, schema or dynamic-state change in the P0-only fixture patch.

The new report labels every view VISUAL TEST FIXTURE / NOT LIVE ACCOUNT OR MODEL OUTPUT and places its banner outside the unaltered images. Raw PNGs remain internal comparison evidence; the wrapper explicitly refuses their interpretation as live account/model/tool output. Main comparison page labels, dynamic image alt text and replacement-file metadata were corrected consistently after review.

Independently ran the actual comparison HTML in Chromium: **5/5 PASS** (`evidence/p0-fixture-wrapper-independent/report.json`). The prominent banner precedes the image and remains visible after scrolling; the native-pixel control works; the label remains at390px; no remote request/script error/source mutation occurred. The embedded original PNG remains SHA316b5ef587ff73c32754f73452ea8c0e44352044cf7c417f9fa3f8c2e4f261da. Screenshot `labelled-wrapper-1530.png` records the wrapper, not a new product capture. The initial file-URL attempt was refused by browser policy; that refusal was preserved, and the supported loopback static-host test ran without changing browser policy.

The fixture author subsequently compiled the exact HEAD+P0 Program9be668... in an isolated external qualification project (BuildProjectReferences=false;0 warnings/errors), then executed **2/2 no-capture CLI controls**. Missing opt-in exits2 with the explicit warning before any output/input access. Explicit opt-in reaches original invalid-width validation (captureWidth500, expected nonzero -6) before fixture read/output/backend/render. This is not a successful valid-input visual run. The independent reviewer read the exact stderr/build log, confirmed absent input/output paths, and verified all67 recorded file hashes including57 dependency assemblies and executable86a1fbb5.... Receipt: `evidence/p0-fixture-cli-controls/receipt.json`. These controls were executed by the author, not rerun by this reviewer. Existing dependency binaries were pinned, not rebuilt from the P0 source closure.

The generated VisualFixtureReport helper was source-reviewed; the Chromium result above covers the actual existing comparison HTML, not a newly executed C# report-generation run. Its0-warning compilation is now demonstrated by the isolated control build, but no capture/report-write path was exercised.

## Product evidence remains separate

Read the actual immutable signed-out provenance report (`evidence/p0-production-provenance-0e24d109/report.json`):10 bounded product checks passed as recorded, including empty composer/no reference recent rows/no invented model availability; no owner/backend substitution. This is a fresh signed-out browser context, not a newly authenticated empty account. It cannot satisfy the required genuine model/tool/persistence/cross-device workflow. The restaurant visual fixture supplies none of that missing evidence. No full Spaces or visual acceptance is declared.

The parent P0 provenance/acceptance document was independently checked against the packaged source, byte-scan, runtime recovery/catalogue, signed-out Chromium and deployment receipts. No mandatory-test acceptance overclaim was found. Its ten required account/model outcomes remain distinct from the ten signed-out checks.
