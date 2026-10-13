# VISUAL TEST FIXTURE — NOT LIVE ACCOUNT OR MODEL OUTPUT

This separate executable is an **automated visual fixture only**. Every restaurant message, model/speaker name, recent title, composer draft and tool activity is synthetic test data. It is never a live account, real model response or executed tool event. It cannot count toward functional acceptance.

This executable is a **visual-only** test harness. It loads the unchanged `BrowserSpacesFeature`, accepts its actual presentation, clicks a real recent-chat button, and renders the resulting `BrowserChatSession` / `ChatSpaceController` state with the canonical `SpacesShellCuiDocument`, original Markdown renderer, icon adapter, Professional theme and Skia at 1530 × 946.

The only substituted product boundary is `IChatSpaceBackend`. Its deterministic canonical records are based on the owner's restaurant reference transcription. They do **not** prove authentication, persistence, inference, tools, approvals or functional acceptance. No fixture binding dictionary, copied product layout or alternate control tree exists. The live Web project excludes every `**/Tests/**/*.cs` file, including this directory. Unsupported backend operations throw.

From the repository root, using the established build environment and serialized build slot:

```sh
dotnet run --project apps/Web/Spaces/Tests/Visual/BrowserSpaces.Visual.csproj -c Release -p:UseSharedCompilation=false -- --visual-test-fixture \
  /workspace/9to1 /workspace/astra-audit/evidence/browser-chat-visual-N \
  /workspace/9to1/apps/Web/Spaces/Tests/Visual/restaurant.fixture.json
```

The output contains a PNG and JSON with actual control bounds, hit testing, typed owner/controller evidence, backend call history, diagnostics, source hashes and loaded assembly hashes. Geometry checks establish measured containment and real click targets; they are **not** visual parity acceptance. The harness does not add a working-duration block, question/answer controls, reasoning-effort selector or usage indicator where the product lacks them. Those gaps are recorded explicitly. Reference `100%` means reasoning effort, not capacity.

The optional fourth positional argument selects width `1530` or `390`. The optional fifth selects the canonical Professional appearance: `SuperBright`, `Bright`, `Dark` (default) or `SuperDark`. Use a distinct output directory per case. This changes only the isolated test's real CUI preference source; it does not alter any product account preference.

The initial fixture adds a fifth `Nearby Restaurants` recent item to exercise actual navigation; it is a fixture-only difference from the four supplied reference titles. Its messages, selected model and saved draft enter through the original canonical backend contracts. Existing tool activity records are deterministic visual input, never claimed as executed tools. No send or tool action runs.

The first capture is preserved at `/workspace/astra-audit/evidence/browser-chat-visual-1`. Its additive `metadata-clarification.json` corrects an earlier capacity/reasoning wording error without altering its original pixels or source-hash receipt. Later captures use the corrected fixture description.

The next-iteration fixture also routes real pointer movement and physical Tab input through the mounted original message controls. It records resting/hovered/focused action-strip opacity and hit testing, sibling isolation, original control identity, unchanged bounds and restored focus, with separate hover/focus PNGs. These checks retain the exact production host action availability and never enable a refused command. The exact approved disclaimer and typed idle-state suppression are checked in the actual shared product layout. Routed touch is covered separately by the shared CUI runtime tests; this executable does not assert physical touch-device acceptance.

## Strict environment and delivery boundary

The executable refuses to begin without the explicit `--visual-test-fixture` flag, before reading fixture input or creating outputs. Its project is not packable or publishable. The normal Web project excludes all `**/Tests/**/*.cs`; no product composition refers to this executable or its backend. These fixture guards do not establish that real-product acceptance passed.

Review/delivery must use `VISUAL-TEST-FIXTURE-NOT-LIVE.html`, whose permanent high-contrast banner sits outside every unaltered product image and says **VISUAL TEST FIXTURE / NOT LIVE ACCOUNT OR MODEL OUTPUT**. Raw comparison PNGs stay internal. Do not embed those raw images as live-product screenshots. The HTML report repeats synthetic-input scope for each view and preserves original PNG hashes. Capture receipt fields separately identify the automated fixture environment.

P0 provenance was reported before changes. No restaurant seed was found in normal production composition, so no user records or production data were deleted. The correction here prevents fixture evidence being mistaken for authenticated functional output; real-account/model/tool/persistence tests remain independently required.
