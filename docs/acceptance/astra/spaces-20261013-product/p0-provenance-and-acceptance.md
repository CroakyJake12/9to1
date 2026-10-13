# P0 mock-up provenance and actual product acceptance

Status: **WORKING — full visual and functional acceptance remain FAILED / UNVERIFIED.** The qualified continuation is now published as [checkpoint36](checkpoint36.md), source `4ee0c10e6d7d2274202acc9aba735f7c648b9ad0`, version `e357b49a-3ca9-40dd-b4a9-05b3e0e58044`. Checkpoint35 remains recoverable. Historical audit findings below retain their original source boundaries; the current compiled candidate also passed the release guard and independent signed-out checks, without satisfying the separate mandatory authenticated tests.

## Confirmed source, reported before correction

The restaurant conversation displayed in the visual comparisons came from `apps/Web/Spaces/Tests/Visual/restaurant.fixture.json` and its `RestaurantVisualBackend`. That separate executable deliberately supplies synthetic records to the real product layout for visual tests. It never ran the illustrated model or tools. Delivering its raw capture made that distinction too easy to miss; this was an evidence-presentation failure. This audit has **not** established restaurant seeding in production.

| Visible example | Source and owner |
|---|---|
| Restaurant question and assistant sentences | Visual JSON; in-memory fixture Conversation and ChatMessage records |
| Qwen 3.6 and Qwen speaker | Visual-only ModelDescriptor and speaker, explicitly not a runtime inventory |
| Four named recent chats | Fixture recent-row list; no production repository write |
| “Which one should I pick?” | Fixture draft |
| Restaurant search and shortlist activities | Synthetic fixture ToolActivity metadata; no executed tools |
| Duration, dietary question, recorded answer and 100% | Listed as missing visual semantics; no corresponding provider event was fabricated |

The independent source audit inspected 5,964 source/data/markup files and traced production composition and the nine first-party Spaces. Its exact-string matches were confined to test files. A separate byte scan verified all346 published assets against the immutable build manifest, expanded230 gzip/Brotli representations, and found none of the specified reference-content or fixture markers. These are bounded audit findings, not universal proof of functional correctness.

No user-created records, account data or persistence schemas were removed. There was no justified production restaurant seed to delete. The test backend remains isolated from the production composition. A release guard now rejects fixture projects, linked sources and known fixture data in raw/compressed public assets; the existing receipt verifier checks transported assets too. Model names and legitimate CUI Visual components are allowed. Visual comparison reports now display a persistent **VISUAL TEST FIXTURE — NOT LIVE ACCOUNT OR MODEL OUTPUT** banner outside the unchanged image.

## Production ownership and outstanding gaps

The ordinary route constructs the real `BrowserChatRuntime` and `HavenChatSpaceBackend`. Conversation rows, messages and drafts come from the scoped conversation repository. Models come from the actual Ollama catalogue through Dulche; the configured private runtime currently has `qwen3:1.7b`. Replies use the shared `ChatSessionService`. Progress is projected from real stream events and persisted metadata. Missing platform tools are explicitly unavailable.

This architecture is incomplete: the browser preferred-model reader does not yet restore the account-wide preference, cross-device conversation behaviour has not passed, hosted attachment sending lacks the required approval integration, real platform tools are unavailable in this browser host, and most distinct non-Chat Spaces journeys are not operational there. Truthful disabled controls remain unmet feature requirements.

A separate native defect was found and corrected in both maintained Chat owners: successful process wake could overwrite a failed catalogue refresh with “Ollama is ready”, and revalidation could silently substitute another model. The correction preserves the saved preference, retains genuine discovery errors, and requires the requested model before sending after wake. It also uses registered provider identity for tagged local names and refuses to overwrite a newer selection during asynchronous loading. This was not evidence of fabricated model replies.

The release guard passed22 independent controls plus3 additional path controls. The labelled comparison wrapper passed5 Chromium checks without changing its original image bytes. Native tests reproduced6 failures out of8 before correction; the corrected expanded24 cases passed, and the complete34-case legacy class passed (including those24). Independent review found no blocker in those bounded corrections. Native test receipts identify the exact working-tree sources and assemblies, including preserved pending CUI dependencies; they are not a clean-checkout build of the later P0-only commit. Actual OS model launch and generation remain unverified. The newly wired SDK evaluated-input release stage still awaits the next ordinary publication.

The isolated fixture executable now requires explicit `--visual-test-fixture` opt-in. The prepared P0-only Program compiled in a separate project with zero warnings/errors against pinned existing dependencies; both no-capture controls passed. A missing flag exits2 before input/output, while explicit opt-in reaches the original invalid-width refusal. No synthetic conversation was rendered in these checks. The future report helper compiled but was not invoked; the five browser checks above cover the existing labelled comparison page. The ordinary product route and its dynamic owners were not replaced.

## Ten mandatory tests

These outcomes refer to the actual compiled product. Controlled unit/component tests and the restaurant visual executable cannot satisfy them.

| # | Required test | Actual result and limit |
|---|---|---|
| 1 | Create a fresh actual account; no example conversations | **NOT RUN / BLOCKED.** No authenticated owner session is available to this executor. A fresh signed-out browser has zero recent rows, but is not a fresh account. |
| 2 | Open Chat with empty composer | **PARTIAL.** Empty text verified in the actual compiled signed-out route. The fresh authenticated account scenario is NOT RUN. |
| 3 | Select a genuinely available model | **NOT RUN in product.** Actual runtime catalogue independently reports `qwen3:1.7b`; signed-in selection has not been exercised. |
| 4 | Send an original prompt | **NOT RUN / BLOCKED** by authenticated product access. No fixture prompt counts. |
| 5 | Genuine selected-model response through Dulche | **NOT RUN / BLOCKED** in the signed-in application. Catalogue and relay checks do not prove a model response. |
| 6 | Sidebar, reopen and persistence | **NOT RUN / BLOCKED** for an authentic new conversation. Repository/component tests are separate. |
| 7 | Reload account, conversation and navigation | **PARTIAL.** Signed-out navigation/reload passed; signed-in account and conversation restoration remain NOT RUN. |
| 8 | Real tool/agent workflow and corresponding progress | **NOT RUN / UNIMPLEMENTED** for required browser platform capabilities. No synthetic activity counts. |
| 9 | Account-wide state across sessions/devices | **NOT RUN; specification compliance UNMET.** No cross-device acceptance evidence; account preference and synchronization work remains. |
| 10 | Provenance audit of other first-party Spaces | **SOURCE AUDIT COMPLETED** for allnine mapped Spaces; no restaurant seeding found. Actual runtime journeys and complete functional acceptance remain UNVERIFIED. |

At `2026-10-13T22:13:38Z`, Chromium151 completed ten bounded checks against the immutable checkpoint35 artifact: no reference content, empty draft, truthful signed-out model unavailability, pointer/keyboard routing, sidebar search/collapse, Home discoverability, resizing and reload. Zero script/network errors were recorded, assets remained unchanged, and browser cleanup settled. **These ten checks are not the ten mandatory account/model tests above.**

## Environment, delivery and estimate

The managed environment interruption stopped the original Ollama/relay processes. The same model, canonical connector and existing protected connection were restored at approximately22:12UTC; the provider catalogue was checked. No positive signed-in inference is implied. The runtime still depends on this managed environment and an existing credential expiring `2026-10-14T19:33:03Z`.

The owner-browser connector is unavailable to this executor, and the preview/account routes retain their existing authentication requirements. No account authority, token or successful session has been fabricated. The next build must not be published until its relevant checks pass. Checkpoint34 remains recoverable; checkpoint35 remains the current deployed source.

The22:00London full-acceptance milestone was missed. P0 audit/isolation corrections are a bounded checkpoint; they do not make Spaces complete. Core authenticated acceptance cannot receive a reliable finish time until a genuine session can be exercised. Full Spaces still contains multiple substantial missing product and dependency families: it is a multi-week programme, not work that can credibly be declared complete tonight. A precise completion date is not established by the current evidence. Productivity Suite work has not begun.

See `p0-independent-source-audit.md` for the detailed owner map; the adjacent receipts preserve exact reviewed source, published-byte findings and the actual signed-out browser run.
