# P0 native model availability — independent review

Reviewed 2026-10-13 22:25 UTC against working source based on `0e24d10972384f5389f150daf3ed76ac1666062d`. The correction is not yet identified here as a new source commit.

**Scoped verdict: PASS for the reviewed native wake correction.** No remaining confirmed blocker was found in its discovery-failure, requested-model, provider-classification, or held-selection paths. The implementation owner ran the tests; this independent reviewer read both actual native owners, the original wake service, its production registrations and canonical routing semantics, the complete regression fixture, and the resulting logs. No product edits or .NET builds were made by this reviewer.

This is a CHECKPOINT, not full Spaces, native Chat, provider, or runtime acceptance.

## Reproduction and verification

| Evidence | Result | Meaning |
| --- | --- | --- |
| `native-ollama-wake-truthfulness-before.log` | 6 failed, 2 passed, 8 total | Before the behavior correction, failed discovery and missing requested models reached attempted repository mutation. Matching-model positive controls already passed. This first snapshot used untagged local fixture names. |
| `native-ollama-wake-truthfulness-after.log` | 24 passed, 0 failed | Corrected native Page and ViewModel, compiled together. The original 8 cases now use a tagged local name (`qwen3:1.7b`); 16 additional cases cover untagged models, registered remote exclusion, and selection races. |
| `native-legacy-wake-regression-after.log` | 34 passed, 0 failed | The complete existing `LegacyTemporaryConversationTests` class ran with `--no-build` against the same compiled output. These 34 include the 24 targeted cases; they are not 58 distinct cases. |

Logs are under `/workspace/astra-audit/evidence/`. The implementation owner's `native-ollama-wake-truthfulness-manifest.json` matches this reviewer's independently read four source hashes, both principal DLL hashes, and all three log hashes; its adjacent `.patch` preserves the scoped change.

The 24 targeted cases comprise discovery failure (2), empty/nonmatching catalogue (4), matching original descriptor (2), registered remote exclusion (2), untagged local name (2), and held probe/wake/discovery × Page/ViewModel × A→B or A→B→A (12). Failed sends retain the draft and assert zero provider requests and zero conversation writes. Failure/missing-model and held-selection controls reopen the actual preferences service from its temporary disk directory, proving the saved preference remains the intended model rather than merely checking the current service instance.

## Source findings and correction

- `ChatPage.axaml.cs:1012` and `ChatPageViewModel.cs:1119` now return the actual discovery outcome. The wake path refuses on discovery failure without replacing the observed failure with `Ollama is ready.`.
- Post-wake checks require a matching descriptor from the returned catalogue. An empty catalogue or only other models refuses the send, preserves the original descriptor and saved preference, and reports the missing model. A successful result installs the actual refreshed matching descriptor; no other model is chosen as a wake fallback.
- `OllamaWakeService.cs:27` uses the actual registry's registered-prefix interpretation. Raw Ollama tags such as `qwen3:1.7b` no longer bypass local wake merely because they contain a colon. An actual registered other provider is left to its own runtime and does not generate an Ollama-ready claim. This agrees with `ProviderRoutingModelClient.Resolve`, which only recognizes a prefix when the registry resolves it. Production DI supplies the original raw local `IOllamaClient` and the real registry to this service.
- Both `SelectedModel` setters increment a revision. The probe, wake, catalogue and exception paths check that revision before accepting the original preflight or overwriting selection/preferences. A newer choice, including A→B→A, keeps the newer saved intent and refuses the old send. The prior conversation-only presentation guard did not provide this protection.
- A retained descriptor on discovery failure represents saved selection intent. It is not inserted into the observed catalogue or used to claim ready. The original failing catalogue status remains visible.

The tagged-model bypass and held-selection overwrite were independently identified during this review and returned to the CUI owner before the final source and expanded tests were accepted.

## Remaining scope and evidence limits

- The fixture invokes the actual native Page/ViewModel and original `OllamaWakeService` with a controlled `IOllamaClient`. Availability changes from false to true before the service would launch a process. It does **not** prove native Ollama process launch, executable discovery, real installation, model loading, real generation, or Windows operation.
- Positive checks execute the actual private availability preflight, not a complete successful provider turn. Remote exclusion means this local wake owner abstains; it does not prove the remote provider is available.
- The already-responsive probe still preserves its existing direct-success behavior without a fresh catalogue read. This review does not establish current model availability for every path outside the changed post-wake flow.
- Broader application lifecycle, signed-in browser inference/Stop, account-wide preference synchronisation and full Spaces acceptance remain outside this bounded result. No publication or live preview change occurred in this review.

## Source and output identity

Paths below are relative to `/workspace/9to1/9to1 Workspace/shared/`. SHA-256 values were independently read after the stable-source signal and rechecked after the tests:

| File | SHA-256 |
| --- | --- |
| `src/Haven.Desktop/Services/OllamaWakeService.cs` | `3e0244359cb917cfc968df7e7733b6c9480fdf8887db8ba14ac74428513fcded` |
| `src/Haven.Desktop/Views/Pages/Chat/ChatPage.axaml.cs` | `70f94d20d99bbf599d8324a23c990386ab7e168b116f3199f6226621b1221e42` |
| `src/Haven.Desktop/ViewModels/ChatPageViewModel.cs` | `cc6b2957992f1a18ccce8f73660636092b7f797f94c55bbd2a436f1df41301a4` |
| `tests/Haven.Desktop.Tests/LegacyTemporaryConversationTests.cs` | `e58b5af88fd6f10fcfbf8f0bfeb25359026741d61a3d7acf78c4653f022da655` |
| `src/Haven.Desktop/bin/x64/Release/net10.0/Haven.dll` | `3900074cccc39f9ac6b3c7ba8f26a8aeebf18b4da41876ff8e135b56a9a21c72` |
| `tests/Haven.Desktop.Tests/bin/x64/Release/net10.0/Haven.Desktop.Tests.dll` | `79dccd046d36b631008ef49191e8e624ba0e7c406c70f4c92bf6397b159548a2` |

Evidence SHA-256:

- Before log: `de58126a218c45274096b513dafb60633e65cb851ff0053815fdb031cd73ce22`.
- Targeted after log: `ebe10d14d18dfb08f7220fed159c31f77bebcbc01fee49ca51a1d8704866dba0`.
- Full legacy class after log: `d94a9a7eea5546b4bcce1e2c38561509ecee3fb5cff0935a5ee02983b86ea06a`.
