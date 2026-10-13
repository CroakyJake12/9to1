# P0 fixture provenance and artifact separation

**VISUAL TEST FIXTURE — NOT LIVE ACCOUNT OR MODEL OUTPUT.**

The source and ownership of every illustrative value were reported before modifications. The confirmed source of the restaurant content shown in comparison captures is the isolated automated visual backend. No reachable restaurant seed or fixture fallback was found in normal production composition. The artifact presentation did not make that distinction prominent enough. No production conversation, message, model choice, draft, schema or user record was removed.

## Before: exact source of illustrative values

All line references below describe the audited input/backend, which remain unchanged.

| Value | Source | Actual owner |
|---|---|---|
| Nearby Restaurants | `apps/Web/Spaces/Tests/Visual/restaurant.fixture.json:3` | Synthetic in-memory Conversation, `RestaurantVisualBackend.cs:28–29`; fifth recent row exists only to exercise test navigation. |
| Qwen 3.6 / Qwen | Same JSON:4–5 | Synthetic ModelDescriptor and speaker attribution, backend:30–31,38–39,48–49. Explicitly not a runtime inventory. |
| Can you look for nearby restaurants? | Same JSON:6 | Synthetic user ChatMessage, backend:37. Not user-entered production content. |
| Looking for restaurants now! / Here are the top 5 restaurants that you should try! | Same JSON:7 | Synthetic assistant ChatMessage, backend:38–39. No provider ran. |
| Which one should I pick? | Same JSON:8 | Synthetic ConversationDraft, backend:33. No production draft write. |
| Help Me Set up a bus… / Diet Plans for 2026 / Track Sales Tax / Monster Energy Drink… | Same JSON:10–13 | Synthetic recent Conversation rows, backend:41–42. |
| Searching for restaurants / Creating a Shortlist | Same JSON:16–17 | Synthetic ToolActivity metadata including test completion flags, backend:34–35,39. No tools ran. |
| Worked for 2 mins 32 sec; dietary question/choices; You Answered No; Qwen 3.6 ·100% | Same JSON:21–24 | Explicit descriptions of missing reference semantics, not rendered elapsed-time/question-answer/reasoning state. |

`Program.cs` deliberately reads the JSON and substitutes `RestaurantVisualBackend` only inside the separate `BrowserSpaces.Visual.csproj` executable. It exercises the original BrowserSpacesFeature, BrowserChatSession, ChatSpaceController, host availability and CUI layout. Real controls do not make synthetic input genuine provider or account data. The fixture's provider, tool, create and attachment methods refuse operation; its draft changes stay in memory.

The normal `apps/Web/NineToOne.Web.csproj:25` excludes every `**/Tests/**/*.cs` file. Production `BrowserSpacesRegistration.cs` supplies the real `BrowserChatRuntime` backend under the existing private storage owner; absent composition refuses rather than using this fixture. The independent source audit is `/workspace/astra-audit/P0-MOCKUP-PROVENANCE-INDEPENDENT-AUDIT.md`. Root separately reported a byte scan of 346 immutable production assets, including 230 compressed variants, with zero fixture-string matches; that artifact audit is distinct from this source report.

## After: artifact and executable separation

- The comparison/report wrapper displays **VISUAL TEST FIXTURE / NOT LIVE ACCOUNT OR MODEL OUTPUT** in a prominent persistent banner outside the original product image, and identifies synthetic input throughout. The original PNG pixels were not edited or reconstructed.
- Raw fixture PNGs are internal comparison evidence. Owner-facing delivery uses the labelled wrapper, never an unlabelled fixture image presented as the real product.
- The automated visual executable requires `--visual-test-fixture` before reading input or creating output. Its project is explicitly not packable or publishable. No normal product route or data owner was changed.
- Future fixture runs generate `VISUAL-TEST-FIXTURE-NOT-LIVE.html`, with a repeated banner for resting/hover/focus views, original PNG hashes and an explicit distinction between fixture layout checks and functional acceptance. Receipts identify the isolated verification environment and internal-only raw-image use.
- Fixture/model/tool data itself remains useful isolated test input. It was not conditionally hidden in the product, deleted from user storage or substituted for a missing runtime owner.

## Evidence and limits

Before/after source receipts are preserved at `/workspace/astra-audit/P0-FIXTURE-ARTIFACT-BEFORE.json` and `/workspace/astra-audit/P0-FIXTURE-ARTIFACT-AFTER.json`. These changes do not perform fresh-account, real-model, real-tool or cross-device tests. Existing deterministic captures cannot pass those gates. The last visual trajectory run before the stop found a real message-hover gap defect; it remains unresolved during the P0 source freeze and is not hidden by this reporting correction.

No new visual capture was run for this artifact-separation change. The prepared HEAD-plus-P0 executable compiled with zero warnings and zero errors, using the current compiled dependencies without rebuilding them. Two actual CLI controls passed: absence of `--visual-test-fixture` returned exit code 2 with a clear refusal, and explicit opt-in reached the existing invalid-width validation. Both stopped before fixture input, output-directory creation, backend construction or rendering. The second control deliberately used invalid input and returned a nonzero process status; it is not a successful render. Exact prepared-source, executable and 57 dependency-assembly hashes are retained in [the CLI receipt](receipts/p0-fixture-cli-controls.json).

An independent Chromium check of the delivery wrapper passed five checks covering the external sticky banner, unchanged embedded PNG bytes, native-pixel comparison, compact labelling and absence of external requests/errors. See [the wrapper receipt](receipts/p0-fixture-wrapper-independent.json). The new future-run report writer compiled but was not executed because no fresh capture was authorized. These bounded checks establish fixture admission and artifact labelling only. Full Spaces acceptance remains unmet, and the Productivity Suite must not begin.
