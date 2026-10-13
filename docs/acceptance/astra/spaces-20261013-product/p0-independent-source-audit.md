# P0 mock-up data provenance — independent source audit

Status: **CHECKPOINT — source provenance audit delivered; mandatory real-account acceptance NOT RUN by this reviewer.**

Reviewed HEAD `0e24d10972384f5389f150daf3ed76ac1666062d` plus the current uncommitted worktree. Exact reviewed source hashes, literal matches and time are in `P0-MOCKUP-PROVENANCE-SOURCE-RECEIPT.json`. No production code, persisted user records or schemas were modified by this reviewer. Findings were reported to root before any correction.

## Finding and limitation

The confirmed source of the restaurant content in the supplied comparison captures is the **isolated visual fixture**, not the production composition found in this audit. The fixture supplies synthetic canonical records through a deliberately substituted `IChatSpaceBackend`. It does not call a provider or execute the illustrated tools. A literal scan of 5,964 source/data/markup files found 18 matching lines for the exact illustrative strings, all in test or visual-test files. This is a bounded search result, not proof of complete functional correctness or absence of every possible synthetic value.

No reachable production restaurant seed, fixture backend registration, or automatic reference-conversation injection was found. Therefore deleting legitimate user data or changing production simply to remove these specific strings is not justified by the current evidence. Root independently owns the immutable deployed artifact/WASM scan; its result must remain separate from this source audit.

## Exact illustrative content and owner

Paths below are relative to `/workspace/9to1`.

| Illustrative data | Exact source | Ownership and reachability |
|---|---|---|
| Nearby Restaurants; original question; two assistant sentences | `apps/Web/Spaces/Tests/Visual/restaurant.fixture.json:3,6,7` | Deterministic visual input only. `RestaurantVisualBackend.cs:28–41` creates in-memory Conversation/ChatMessage records. No production repository write. |
| Qwen 3.6 / Qwen selected model and speaker | Same JSON:4–5; `RestaurantVisualBackend.cs:30,41,51` | Synthetic visual ModelDescriptor with explicit “Not a runtime inventory” metadata. Returned only by fixture catalogue. The other Qwen3.6 matches are native model-name-formatting tests. |
| Four reference sidebar titles | Same JSON:9–14 | `RestaurantVisualBackend.cs:43–44` builds fixture recent rows. A fifth Nearby Restaurants row exists only to exercise fixture navigation. |
| “Which one should I pick?” | Same JSON:8; backend:33 | Fixture ConversationDraft. Normal production empty state starts with `string.Empty`; restored text comes from the actual draft repository. |
| Searching for restaurants / Creating a Shortlist | Same JSON:16–17; backend:34–35,41 | Synthetic ToolActivity objects serialized into fixture message metadata. They were never executed and cannot support functional acceptance. Program:252 identifies them for geometry capture. |
| Worked for 2 mins 32 sec; dietary question; You Answered No; reference100% | Same JSON:21–24 | Descriptions of unsupported reference semantics, not product status values or implemented runtime events. No elapsed duration, interactive question or reasoning percentage is fabricated to fill these gaps. |
| Visual fixture construction | `apps/Web/Spaces/Tests/Visual/Program.cs:32,57,61–63` | Separate executable explicitly reads JSON and injects `RestaurantVisualBackend` into the real layout/controller. `BrowserSpaces.Visual.csproj:8` owns those files; production `apps/Web/NineToOne.Web.csproj:25` excludes every `**/Tests/**/*.cs`. No fixture/reference entry occurs in the immutable0e24 artifact path manifest inspected here. |

The visual executable's use of real layout, controller and availability classes establishes rendering fidelity only. It does not turn fixture messages into genuine provider output. Captures must be labelled **visual fixture, not actual account data** whenever shown to the owner.

## Actual production Chat provenance

- **Composition:** `apps/Web/Spaces/Chat/BrowserSpacesRegistration.cs:15–20` constructs `BrowserChatRuntime(sameOwner)` and supplies its real `Backend`. `BrowserSpacesFeature.cs:75–78` refuses absent composition instead of choosing a fixture fallback.
- **Conversations/sidebar:** `HavenChatSpaceBackend.cs:20–26` calls the real scoped `IConversationRepository`; it filters archived/Temporary/foreign-Space rows. Browser binding `BrowserChatSession.Bindings.cs:53` projects only `state.RecentChats`. Native sidebar reads `_conversations.GetRecentAsync` (`NativeChatSidebar.cs:319,369`) rather than named example rows.
- **Messages/drafts:** backend:57–81 reads the real descriptor, branch, messages, attachments and draft. `ChatSpaceContracts.cs:97–102` has empty initial messages/models/draft. Controller initialization:52–55 reads the backend. Only the create/draft/send paths create normal user records; creation uses “New chat”, an empty-title default rather than an illustrative history.
- **Models:** runtime:37–48 uses the real Ollama client/model inventory. Controller:451–456 selects only a name present in that returned catalogue. No Qwen name is built into the production composition. **Gap:** runtime:48 supplies a preferred-model reader returning null; account-wide preferred-model restoration is incomplete. Selecting the first genuinely returned model is not an invented catalogue or a mock-up selection.
- **Responses:** `ChatSessionSpaceTurnExecutor.cs` invokes the actual shared `ChatSessionService`; `BrowserHostedOllamaClient` delegates to the real Ollama HTTP/stream owner. Controller:376–379 projects actual stream deltas/thinking/tool activity. Saved progress cards come from persisted metadata (`ChatSpaceProjection.cs:43–55`). No production timers manufacture restaurant tool events.
- **Tools:** runtime:67 declares Text only. `BrowserUnavailablePlatformTools.cs` rejects all missing OS/workspace operations and `IsSupported` is false. Attachments are genuinely stored but hosted send refuses them at runtime:61–62 because original approval is not integrated. This is incomplete functionality, not successful simulated tool execution.
- **Account/state:** runtime receives the existing `BrowserPrivateTaskStorage` owner and checks `DemandPrivateContextCurrent`; real account admission is not fabricated in this composition. Browser says chats are local to this account/device, not synced. Cross-device specification acceptance is unmet.
- **Controls:** `BrowserChatSession.cs:174–193` explicitly disables actions without implemented owners; item availability restricts first-party pins to Chat (`Bindings.cs:114–115`). Model/approval/type/Voice/Groups/Specialists/Research controls must not be counted as working just because the shared markup contains them. Actual signed-out compiled checks show Send disabled, Account enabled, empty Chat and no false recovery incident; these do not establish a fresh authenticated account.

## Other first-party Spaces

| First-party Space | Observed production owner/source | Provenance conclusion and real gap |
|---|---|---|
| Chat | Real conversation repository, shared ChatSessionService and actual catalogue as above | No reference seed found. Positive authenticated send/persist/reload still needs actual execution. |
| Study | Native `StudyHomePage.cs:70–94` reads subjects/lessons/assignments from `IContainerRepository`/`IStudyPlannerService`; add subject:123–137 uses entered name. | No example subject/conversation seeding found. Progress is derived from saved lesson metadata: RAG maps to25/60/100 and level points use fixed formulas (`StudyLessonMetadata.cs:27–33,183–204`); this is an implementation-derived metric, not provider progress. Full Study spec validation remains open. Browser Space pin unavailable. |
| Tasks | Native canonical task owner and `SpaceTaskWorkspaceService.cs:31–81` read existing Space/conversation/task snapshots and execution IDs. Legacy Tasks page reads persisted task conversations (`NativeTasksSpacePage.cs:64–76`). | No sample task seeding found. Task command/actual execution ownership is distinct from generic recent-chat display. Browser pin unavailable; no real tool acceptance supplied by this audit. |
| Assistants | Native routes to actual registered product (`MainView.Spaces.cs:62–73`); canonical bridge lists original Den records (`DenAssistantCanonicalBridge.cs:71–75`). | No fixture Assistant inserted. Missing connected apps/knowledge are explicitly Unsupported in the bridge:189. Browser Spaces pin unavailable even though separate Assistants product modules exist. |
| Legacy Agent | Built-in Space definition retains existing ID. Generic configured workspace route; built-in AgentCatalog has Default, Auto, Research Agent configuration presets. | These are reusable definition presets, not created user conversations or running agents. Distinct specified persistent-Agent workflow is not established by a generic Chat route. |
| Shopping | `SpaceRegistry.cs:593` product definition; native generic configured Chat via `SpaceLaunchPolicy.cs:29–36` and `MainView.Spaces.cs:80–109`. | No sample products, comparisons or restaurant history found. Required distinct Shopping workflow remains unimplemented/unverified; instructions alone are insufficient. Browser pin disabled. |
| Research | Same generic route; definition:594 supplies instructions, not research findings. | No fabricated search results or progress found in that route. Distinct research/sources/steering workflow not established. Browser pin disabled. |
| Translate | Space kind defaults to generic configured Chat. Separate `TranslatePage.axaml.cs:107–124` has a real translator result owner and saved preferences:55. | No translation text seeded. The existence of a separate translator does not prove correct Spaces integration. Browser pin disabled. |
| Experiences | Space kind defaults to generic configured Chat. Separate `ExperiencesPage.axaml.cs:49–56,74–81` reads genuine saved GenUI apps/session state. Explicit New action:99 passes a builder instruction. | No fake saved experience list found. User-triggered builder instruction is a template action, not automatic example content. Correct specified Space routing/stateful experience workflow remains unaccepted. Browser pin disabled. |

`SpaceRegistry.cs:583–618` seeds only stable first-party product metadata: names/icons/instructions with empty example pairs/files/capability bindings, no model selected and no conversation/message history. SQLite CatalogRepository seeds built-in prompt/agent definitions; CapabilityRepository seeds capability definitions; GenUiTemplateRepository seeds templates. Those configuration catalogues must not be confused with observed availability/executed effects, but they are not restaurant demo data.

## Reachable native fallback requiring separate truthfulness correction

`ChatPage.axaml.cs:1009–1023` preserves a prior/saved model name after catalogue failure by constructing a ModelDescriptor with empty capabilities and unknown date. It leaves `Models` empty and shows “Ollama unavailable”. `SendCommand:139` stays enabled for a draft and nonnull selection; `SendAsync:1034` calls actual wake/probe and then actual ChatSessionService:1117. Offline/no-autowake is refused; provider failure is displayed at1211. This is a retryable saved preference, not an invented inventory row or AI response.

There is a concrete **truthfulness gap** in wake-success handling: `EnsureSelectedModelAvailableAsync:1266–1270` calls `RefreshModelsAsync`, which catches discovery errors internally, then unconditionally overwrites the status with “Ollama is ready”. A healthy/woken process does not prove the requested model catalogue/selection is available. Correct this without discarding the saved preference. Parent was informed before modification.

Legacy `UseStarterCommand:173` copies a chosen starter into Composer only on explicit action; starter phrases:229–255 are suggestions. They are not automatic conversation seeding. Native transient AssistantStarted records are created from actual stream events; dual comparison messages derive from actual CompleteAsync results, not reference examples.

## Mandatory runtime tests

This reviewer has **not run** the user's ten new mandatory real-product tests. Existing signed-out immutable nine checks and deterministic visual captures are not substitutes for a newly authenticated empty account, genuine model response, real tool execution, persistence/reopen, or cross-device sync. Root owns the deployed artifact/runtime lane. Account/backend/tool availability and incomplete distinct Space implementations must remain explicit blockers. No full acceptance, percentage completion or removal of fabricated production data is claimed.
