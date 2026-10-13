# Actual browser Chat visual fixture

This executable is a **visual-only** test harness. It loads the unchanged `BrowserSpacesFeature`, accepts its actual presentation, clicks a real recent-chat button, and renders the resulting `BrowserChatSession` / `ChatSpaceController` state with the canonical `SpacesShellCuiDocument`, original Markdown renderer, icon adapter, Professional theme and Skia at 1530 × 946.

The only substituted product boundary is `IChatSpaceBackend`. Its deterministic canonical records are based on the owner's restaurant reference transcription. They do **not** prove authentication, persistence, inference, tools, approvals or functional acceptance. No fixture binding dictionary, copied product layout or alternate control tree exists. The live Web project excludes every `**/Tests/**/*.cs` file, including this directory. Unsupported backend operations throw.

From the repository root, using the established build environment and serialized build slot:

```sh
dotnet run --project apps/Web/Spaces/Tests/Visual/BrowserSpaces.Visual.csproj -c Release -p:UseSharedCompilation=false -- \
  /workspace/9to1 /workspace/astra-audit/evidence/browser-chat-visual-N \
  /workspace/9to1/apps/Web/Spaces/Tests/Visual/restaurant.fixture.json
```

The output contains a PNG and JSON with actual control bounds, hit testing, typed owner/controller evidence, backend call history, diagnostics, source hashes and loaded assembly hashes. Geometry checks establish measured containment and real click targets; they are **not** visual parity acceptance. The harness does not add a working-duration block, question/answer controls, reasoning-effort selector or usage indicator where the product lacks them. Those gaps are recorded explicitly. Reference `100%` means reasoning effort, not capacity.

The initial fixture adds a fifth `Nearby Restaurants` recent item to exercise actual navigation; it is a fixture-only difference from the four supplied reference titles. Its messages, selected model and saved draft enter through the original canonical backend contracts. Existing tool activity records are deterministic visual input, never claimed as executed tools. No send or tool action runs.

The first capture is preserved at `/workspace/astra-audit/evidence/browser-chat-visual-1`. Its additive `metadata-clarification.json` corrects an earlier capacity/reasoning wording error without altering its original pixels or source-hash receipt. Later captures use the corrected fixture description.
