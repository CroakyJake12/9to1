# M3 Chat Space behavioural delta — measured before any surface swap

**Date:** 2026-10-16 · **Branch:** `opencode/m3-shell-wiring-20261016`
**Supersedes nothing.** Companion to `M3-SCOPE-2026-10-14.md`, which named shell wiring as the
largest remaining M3 item and asked for the delta to be established before proposing a swap.

## 0. What this document is, and is not

This is a **measured code delta** between the live Chat surface and the CUI Chat Space, written
after `ChatSpacePage` was built and exercised against real domain state. It replaces the
six-item shortlist the previous pass carried.

It is **not** a visual comparison and **not** a migration approval. No approved tolerance exists
(O22); no frame comparison was run; the native capture route is itself among the pre-existing
native failures. Every row below is cited to source, not to appearance.

**No swap is proposed.** `ChatHavenScene` remains the live surface.

**Update 2026-10-16 (later pass):** Phase 2 (Markdown, ScrollView/FollowAtEnd) and most of Phase 3
(streaming, all six message actions, safety lock, thread settings, dual model, edit mode) are now
implemented and committed on this branch by the combined worker. The remaining open items are
§3.7 (follow-at-end is now a Container property, resolved), §3.9 (avatars), §3.10 (streaming
presentation is now incremental, resolved), §3.11 (tool activity interleaving vs nesting) and
the visual-evidence route. See §8 below.

## 1. Method

| | |
|---|---|
| Live surface | `ChatHavenScene` (+ `NewChatPage` orchestration), HUI Prefab/DynamicUI |
| Candidate surface | `ChatSpace.cui` + `ChatSpaceCuiBindings` + `ChatSpaceController`, hosted by `ChatSpacePage` |
| How measured | Reading both surfaces' sources and enumerating every capability the live surface exposes |
| Evidence quality | Compile + headless-render + controller tests (92/92 Spaces, 349/349 CUI Runtime). **Not** a running native comparison. |

Coverage asymmetry worth stating plainly: the CUI surface is now verified where it exists, but
the live HUI surface **cannot be executed on this Linux host** (see §5). So this is a
source-level delta, not a two-sided behavioural observation.

## 2. Covered by the CUI surface

| Capability | Evidence |
|---|---|
| Recent chat list + open | `ChatSpace.cui:6-11`; `ChatSpaceController.OpenChatAsync` |
| New chat | `ChatSpace.cui:6`; `ChatSpaceController.NewChatAsync` |
| Transcript rendering, chronologically ordered | `ChatSpaceProjection.cs:27-39` (D04); `ChatSpaceCuiRenderingTests` |
| Keyed repeat preserving row identity across re-rank | `ChatSpaceCuiRenderingTests.Transcript_reorder_...` (asserts order **and** instance identity) |
| Live re-render on republish | `ChatSpaceCuiHostRenderingTests.Republished_state_re_renders_...` |
| Host publication + readiness handshake | `ChatSpaceCuiHostRenderingTests.The_shell_host_path_publishes_...` |
| Composer draft round-trip | `ChatSpaceControllerRoutingTests.The_saved_draft_survives_a_reload_...` |
| Attach / remove attachment, persisted into the draft | `...An_empty_composer_with_an_attachment_is_still_sendable`, `...Removing_an_attachment_...` |
| Model selection (governed inventory, unknown refused) | `...Send_is_refused_until_a_governed_model_is_selected` |
| Branch create / switch | `...Branching_and_switching_reach_the_backend_...` |
| Regenerate | `ChatSpaceController.RegenerateAsync` |
| Send gating (model + content/attachment) | `ChatSpaceComposer.CanSend`; `...Send_is_unavailable_until_...` |
| Citations, per-message attachments, tool activities (nested) | `ChatSpace.cui:27-35` |

## 3. Gaps — capability present live, absent in CUI

Ordered by how much user-visible behaviour is lost. "Not modelled" means the capability does not
exist anywhere in `ChatSpace.cui`, `ChatSpaceCuiBindings`, `ChatSpaceViewState` or
`ChatSpaceController`.

### 3.1 Markdown rendering of message bodies — **not modelled**

`ChatHavenScene.cs:597` binds each message body to a `Markdown` component
(`item.GetComponent<Markdown>("Body")`), with code-block actions wired through
`MarkdownCodeActionRequested` (`ChatHavenScene.cs:24`, `:597`).

`ChatSpace.cui:26` renders `<Text text="{Binding message.Content}">`. CUI has no Markdown element
— `Markdown` was one of the invented elements removed when the source was made canonical
(`M3-SCOPE-2026-10-14.md`, §0).

**Impact:** every code block, table, list and link in a response renders as raw source text.
This is the single most visible regression and it is a *framework capability gap*, not a markup
gap.

### 3.2 Per-message actions: 2 of 6 — **not modelled**

`ChatMessageAction` (`ChatHavenScene.cs:13-21`) declares Edit, Copy, Branch, Delete, Regenerate,
Forget. `ChatSpace.cui:37-38` offers only Branch and Regenerate.

**Missing:** edit, copy, delete, forget. Copy and delete are deliberately still available while a
conversation is safety-locked (see 3.8), so this is also an accessibility/lock-path regression.

### 3.3 Assistant reasoning ("thinking") row — **not modelled**

`ChatHavenScene.cs:571-575` computes `THINKING` and `THINKINGVISIBILITY` for assistant messages.
`ChatSpace.cui` has no equivalent. `ChatSpaceMessage` (`ChatSpaceContracts.cs:12-22`) carries no
thinking field.

**Impact:** reasoning traces are invisible.

### 3.4 Send queueing — **not modelled**

`ChatHavenScene.SendQueue.cs:8-93` holds `Queue<string> _queuedComposerMessages`, a
queue-aware send handler and a `StartQueuedSendPump` timer.

`ChatSpaceController.SendAsync:132` returns immediately when `IsSending` is true, and
`ChatSpaceComposer.CanSend` is false while sending. There is no queue: a message composed during a
response is rejected rather than queued.

### 3.5 Threading settings — **not modelled**

`ChatHavenScene.ThreadSettings.cs:14-49` — enable gate, invocation handler and per-setting
selection. No counterpart in the CUI view state.

### 3.6 Dual-model selection — **not modelled**

`ChatHavenScene.DualModel.cs:35-98` — bar, activation, second-model assignment, choice list.
`HavenChatSpaceTurnExecutor.ExecuteAsync` passes **`DuoMode.Solo` unconditionally**.

**Impact:** side-by-side model comparison is unavailable, and `NewChatPage` explicitly refuses
canonical Tasks in dual mode (`NewChatPage.cs:1098-1102`). Any future CUI Tasks surface inherits
that restriction silently unless modelled.

### 3.7 Follow-at-end scrolling — **lost, known**

`ChatHavenScene.cs:371` `ScrollToEnd()` sets `Messages.ScrollY = Messages.MaxScrollY`; during
streaming it tracks user position via `IsFollowingLatest()` and only auto-scrolls when already
following (`NewChatPage.cs:1476`, `:172-181`, with a 1 s `DispatcherTimer`).

The CUI transcript is a `Container` (`ChatSpace.cui:22`) because CUI has no canonical scroll
container. There is no scroll position, therefore no follow-at-end and no "user scrolled up"
tracking.

**This is framework work, not markup work.** Do not attempt it by editing `ChatSpace.cui`.

### 3.8 Conversation safety lock — **not modelled**

`ChatHavenScene.SetSafetyLocked` (`:179-186`) disables composer, send and add-menu while allowing
copy and delete; `NewChatPage.EnsureConversationMayActOriginalBodyAsync:888` gates every mutating
operation behind `IConversationSafetyService`.

`ChatSpaceController` has no safety concept. Every send/branch/regenerate is unconditional.

### 3.9 Message avatars — **not modelled**

`ChatHavenScene.ApplyAvatarValues:580-589` renders a per-role avatar gated on personalisation
preferences and avatar availability.

### 3.10 Streaming and progress presentation — **not modelled**

`NewChatPage` maintains streaming-message sets, thinking start/end ticks, a 1 s progress timer
(`NewChatPage.cs:169-182`) and per-delta flushing. `HavenChatSpaceTurnExecutor` drains the stream
and discards it, then the controller re-reads from the repository.

**Impact:** the CUI surface shows no incremental response. It repopulates after completion.
`ChatSpaceComposer.IsSending` does gate the Send button, but there is nothing to watch.

### 3.11 Tool activities: interleaved vs nested — **behavioural difference**

`ChatHavenScene.cs:511` inserts `ChatToolActivity` into the **same** `"Messages"` container at an
explicit ordinal, interleaving tool cards with messages in one chronological stream.

`ChatSpace.cui:33-35` nests tool activities **inside** each message's container. Order is
therefore correct, but presentation differs: the distinct Grid card with its own surface
background and status/detail/meta triple (`M3-SCOPE-2026-10-14.md` §1.3b) becomes plain text
under the message.

### 3.12 Select controls cannot show their bound value — **known, unresolved**

`CUIR007` refuses two-way binding on `ComboBox`, so `Input Type="Select"` cannot display its
current value. Both the model picker (`ChatSpace.cui:17`) and the branch picker (`:19`) are
affected. The model name is shown as separate text (`:18`); the **branch picker has no such
fallback at all**.

Selection is dispatched as `SelectedIndex` (`CuiControlLoader.cs:1729`), so the host resolves an
ordinal against the currently published list (`ChatSpacePage.ResolveModelName`). This is correct
while list and bound state stay in step — they are driven by the same publication — but it is
positional, not identity-based, and would mis-resolve if a future host bound a stale list.

### 3.13 Add-menu catalogue — **not migrated**

`ChatAddMenu.hui` plus `ChatAddMenuSurface` (`ChatHavenScene.cs:97-103`) provides agents,
capabilities, prompts and apps with search and mention completion. The CUI surface has no
equivalent; its `RecentSearchQuery` filters nothing (`ChatSpaceCuiBindings` stores the query and
`ChatSpaceController` never reads it).

## 3.5 Resolved since the first delta pass

| Gap (original §3) | Resolution | Evidence |
|---|---|---|
| 3.1 Markdown bodies (framework gap) | `CuiMarkdownView` exposed as first-class CUI element `Markdown` | commit `a26119d4`; `CuiMarkdownElementTests` 4/4 |
| 3.7 Follow-at-end scrolling (framework gap) | `Container` gains `VerticalScrolling` + `FollowAtEnd` | commit `f5446e9e`; `CuiFollowAtEndTests` 4/4 |
| 3.2 Message actions (2 of 6) | All six: copy, delete, forget, edit, branch, regenerate | `ChatSpaceController.Actions.cs` |
| 3.3 Reasoning row | `message.HasThinking` / `message.Thinking` rendered | `ChatSpace.cui:55-60` |
| 3.4 Send queueing | `TurnQueue` with `MaxSendQueue=8` and queued pump | `ChatSpaceController.Sending.cs` |
| 3.5 Threading settings | `SetThreadActionMode`, `SetThreadResponseMode` | `Actions.cs` |
| 3.6 Dual-model | `ToggleDualModel`, `SelectDualSecondModel`, `SelectDualMode` | `Actions.cs` |
| 3.8 Safety lock | `IsSafetyLocked` gates send/attach/edit/forget; copy+delete remain | `Actions.cs` CanMutate/TryBeginMutation |
| 3.10 Streaming | `IProgress<ChatStreamEvent>` forwarded, overlay merged per event | `Sending.cs` Stream() |
| 3.12 Select bound values | `SelectedIndex` two-way via `SelectedModelIndex`/`SelectedBranchIndex` | bindings + controller |
| 3.13 Add-menu | still not migrated | — |

## 8. Independent acceptance evidence

- `ChatSpacePresentationAcceptanceTests` — 5/5 (viewport + composer placement, selector identity + stale-write refusal, search filter, reasoning/edit binding, safety-lock permissions).
- `ChatSpaceCuiRenderingTests` — 8/8 (render, reorder identity, command refusal, send gating, model label, update-draft).
- `ChatSpaceCuiHostRenderingTests` — 3/3 (host publication, republish re-render, degraded handshake).
- `ChatSpaceControllerRoutingTests` — 7/7.
- CUI Runtime — 349/349 (Markdown + FollowAtEnd included).
- CUI Compiler — 43 pass / 7 fail (unchanged baseline).
- Spaces — 92/92 (with `SpacesTestApp` loading the primitive theme).

## 9. Remaining open items

1. **Avatars** (§3.9) — still not modelled in `ChatSpace.cui` or `ChatSpaceMessage`.
2. **Tool-activity interleaving** (§3.11) — still nested under the message rather than interleaved in the transcript stream.
3. **Visual evidence** — `NativeAssistantCanonicalVisualCaptureTests…emit_opt_in_captures` is in the pre-existing native failure band; no frame comparison exists for any CUI surface.
4. **Native Chat parity proof** — `Haven.Desktop` does not build on this host because of unrelated UCML in-flight edits, so `ChatSpacePage` is compile-verified only, not executed.
5. **Swap decision** — still `NOT READY FOR SWAP`. Avatars and visual evidence are the gating items; the framework prerequisites (Markdown, ScrollView) are now satisfied.

## 4. Corrected from the previous pass

| Claim | Status |
|---|---|
| "21 production `.hui` templates" | **20** embedded under `Prefabs/` + `DynamicUI/`. 8 are Chat-family. |
| Transcript loss is the only markup gap | Understated. §3.1 (Markdown) is a framework capability gap of the same order as the missing `ScrollView`. |

## 5. Verification status of this document

| Claim class | How checked |
|---|---|
| CUI surface compiles | `CuiCanonicalApplicationCompilationTests` — `ChatSpace.cui` not among the 7 compiler failures |
| CUI surface renders real state | `ChatSpaceCuiRenderingTests`, `ChatSpaceCuiHostRenderingTests` (headless, ARM64) |
| Controller behaviour | `ChatSpaceControllerRoutingTests` (7) |
| Handoff baselines | CUI Runtime 349/349; Spaces 92/92; CUI Compiler 43 pass / 7 fail (unchanged) |
| Live-surface behaviour | **Not executed.** `Haven.Desktop.Tests` emits `PlatformTarget x64` and its VSTest `testhost` needs an X64 `dotnet` host; only the ARM64 runtime exists here. Requires qemu-user + binfmt registration (root). |
| Visual comparison | **None.** No frames captured or compared. |

## 6. What blocks the swap

Not negotiable by markup work:

1. **Markdown body rendering** (§3.1) — needs a CUI rich-text/Markdown capability.
2. **Follow-at-end scrolling** (§3.7) — needs a CUI scroll container.

Negotiable but required before removal of `ChatHavenScene`, because they are user-visible
capabilities that would silently disappear:

3. Per-message edit/copy/delete/forget (§3.2)
4. Send queueing (§3.4), threading settings (§3.5), dual-model (§3.6)
5. Reasoning row (§3.3), safety lock (§3.8), avatars (§3.9)
6. Streaming presentation (§3.10)

`HavenPrefabCatalog.FromAssembly` and `HavenDynamicUITemplateCatalog.FromAssembly` remain
load-bearing and must not be removed. `ChatToolActivity.hui` and the two message templates are
still the only consumers of the per-item `Template` capability added last pass, which has no
production caller yet — worth noting before that capability is judged unused.

## 7. Suggested order

1. Repair the native capture route (`NativeAssistantCanonicalVisualCaptureTests…emit_opt_in_captures`),
   so a CUI surface can produce visual evidence at all. Until then every row in §3 is argued from
   source only.
2. Get an executable native surface on this host (x64 runtime + qemu/binfmt) so both sides of the
   delta can be observed rather than inferred.
3. Decide the ScrollView and Markdown questions as framework work, since they gate the swap and
   cannot be answered in markup.
4. Only then model §3.2–§3.6.