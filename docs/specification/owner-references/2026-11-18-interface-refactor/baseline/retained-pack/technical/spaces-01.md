# Spaces — conversations, workspaces and custom Space Builder — Spaces, composer, models and agent visibility; Space entry and retained sidebars (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0749 · v2 lines 3688–3689

<!-- UX27:BEGIN SECTION-05 -->
## 5. Spaces, composer, models and agent visibility

### V2-B0750 · v2 lines 3691–3691

### UX27-SPACES-001 — Space entry and retained sidebars

### V2-B0751 · v2 lines 3693–3693

**O/B.** Selecting a Space from the chosen rail/dropdown opens its actual conversation environment directly, with the selected Space’s contextual sidebar intact. Do not insert an unnecessary management/details screen before chat. New Chat creates a conversation inside the current Space, not a new Space. Conversation rows resolve stable IDs and show the correct title/status without mixing histories from other Spaces.

### V2-B0752 · v2 lines 3695–3695

The rail/dropdown preference persists and both expose equivalent navigation. Switching Spaces atomically updates context, history, model policy and tools while retaining each conversation’s draft. The Toolbox/Space-manager affordance remains a meaningful control, not an unexplained lone plus sign substituted for the approved layout.

### V2-B0753 · v2 lines 3697–3697

### UX27-SPACES-002 — Composer shelf layout and activation

### V2-B0754 · v2 lines 3699–3699

**O/B/D.** Replace a single static Send button with the owner’s contextual action shelf: at most five active contextual items, a primary visible action with the next item visually tucked/peeking, and a dot position indicator. Support the configured horizontal/vertical scroll or swipe browsing direction without triggering the selected action merely because the shelf was scrolled. Buttons have distinct semantic labels/icons and accessible position/state.

### V2-B0755 · v2 lines 3701–3701

The shelf’s visual stack is not a queue of already submitted work. Browsing changes the selected control, not the conversation. Activation uses the currently displayed action and a current-state guard; if the run state changes between pointer-down and release, cancel or revalidate rather than sending the wrong command. Cyclic wrapping and extra unrequested actions are not inferred from the original owner examples; the default is bounded navigation.

### V2-B0756 · v2 lines 3703–3703

### UX27-SPACES-003 — Composer action-state table

### V2-B0757 · v2 lines 3705–3705

**O/B/D.** Required owner examples and safe default completion:

### V2-B0758 · v2 lines 3707–3715

| Runtime/composer state | Shelf actions | Meaning |
|---|---|---|
| Idle, nonempty draft | Send, Voice | Send starts one new request with the retained draft/context |
| Idle, empty draft | Send disabled with reason, Voice | No empty accidental request; Voice starts only through its permission flow |
| Agent running, nonempty draft | Steer, Stop, Queue, Voice | Steer targets the active run; Queue creates ordered later work; Stop requests cancellation at its supported boundary |
| Agent running, empty draft | Stop, Voice; Steer/Queue unavailable until content exists | Do not fabricate an empty steering message |
| Response stopped/paused at a resumable state | Resume, End Pass | Resume continues the same eligible run; End Pass closes it deliberately without erasing prior artifacts |
| Permission/form hold | Contextual status and permitted user controls | A shelf action must not grant permission or answer the form implicitly |
| Offline/provider unavailable | Retained draft plus truthful retry/configuration state | No fake send success; no automatic paid provider change |

### V2-B0759 · v2 lines 3717–3717

The rows completing unspecified empty-draft states are D defaults, not earlier owner quotations. Preserve more specific supported runtime contracts. The text draft is cleared only after the corresponding message/queue admission is established, not when the button animates.

### V2-B0760 · v2 lines 3719–3719

### UX27-SPACES-004 — Steer, Queue, Stop and Resume semantics

### V2-B0761 · v2 lines 3721–3721

**B/D.** Steer durably associates the new instruction with the active request and presents it at a supported safe boundary. It is not a hidden Replace/Restart operation. Queue retains order, stable message IDs and the target context; its admission receipt is distinct from execution. Stop displays Stopping until cancellation/settlement is actually confirmed, preserves partial results and does not declare every side effect undone.

### V2-B0762 · v2 lines 3723–3723

Resume is available only for an eligible paused/stopped run under current permissions. End Pass preserves source/artifacts/history and closes the pass intentionally; it is not deletion. Repeated clicks or transport retries reconcile the same request rather than duplicate work. Explain unavailable operations instead of presenting dead controls.

### V2-B0763 · v2 lines 3725–3725

### UX27-SPACES-005 — Drafts, attachments and input keys

### V2-B0764 · v2 lines 3727–3727

**B/D.** Draft text, attachments, selected context and chosen responder survive tab/Space changes and supported restart recovery. Enter/Shift+Enter in the chat composer follow its documented send/newline preference and are not inherited from Boards block editing. IME commit never accidentally sends a request. Attaching a local file uses authorised local access without requiring an unrelated cloud account.

### V2-B0765 · v2 lines 3729–3729

Each attachment has a visible name/type, ingestion/upload state and remove control. Failed processing does not appear Ready. Removing one attachment preserves the others and the draft. Image/audio/video capability must be truthful for the selected model; a text-only route cannot silently claim to analyse pixels or sound.

### V2-B0766 · v2 lines 3731–3731

### UX27-SPACES-006 — Message rendering and actions

### V2-B0767 · v2 lines 3733–3733

**B/D.** Render actual Markdown/rich content with distinct headings, paragraphs, code, lists, links, tables and citations. Streaming updates the existing message rather than duplicating its text or scrolling the reader to the bottom after they intentionally scroll up. Code blocks preserve whitespace and copy exactly the code; wide tables/code have deliberate horizontal scrolling without widening the entire conversation.

### V2-B0768 · v2 lines 3735–3735

Per-message actions are contextual and target a stable message/revision: copy, applicable edit/retry/regenerate/branch/read-aloud/share operations from the canonical feature set, with unavailable actions explained. Editing an earlier message shows the affected continuation/branch semantics before mutation. A visual action icon without a functioning command does not satisfy message-action completeness.

### V2-B0769 · v2 lines 3737–3737

### UX27-SPACES-007 — Summary, tasks and status truth

### V2-B0770 · v2 lines 3739–3739

**O/B.** Keep the collapsible Summary surface in the agreed top-right placement, with Action Graph access/toggle rather than a second competing progress database. Task Space defaults to its own tasks, with All Tasks as an explicit filter. Working, completed, checkpoint, permission, stalled and blocked states use distinct meaningful icons/labels; a checkpoint badge clears according to the viewed-state contract, not simply because time passed.

### V2-B0771 · v2 lines 3741–3741

Sort active/actionable work coherently and preserve stable task identity through filtering. A live process or growing token count is not completion. Show submitted instruction, queue admission, running work, saved checkpoint and independently accepted output as different evidence states.

### V2-B0772 · v2 lines 3743–3743

### UX27-SPACES-008 — Custom sidebar targets and DEC-014

### V2-B0773 · v2 lines 3745–3745

**O/B.** An extension entry has stable SidebarItemID, origin/package identity, target kind, configured target, permitted Spaces and current permission/revision state. Supported targets include URL/page, authorised app surface, authored CUI, GenerativeUI and declared Automation/action. The row’s icon/label aligns with ordinary navigation; users do not need a separate Plugin panel just to activate a permitted entry.

### V2-B0774 · v2 lines 3747–3747

Use the established typed host/session navigation/action adapter. Visibility is not execution permission. Detaching/revoking an extension removes its active contributions and prevents orphaned actions without deleting unrelated conversations. No arbitrary string execution, guessed URI fallback or inactive rows presented as implemented functionality.

### V2-B0775 · v2 lines 3749–3749

### UX27-SPACES-009 — Model Groups and lazy selection

### V2-B0776 · v2 lines 3751–3751

**O/B.** Preserve the owner’s 16 October Model Groups decision where not yet consolidated: one stable group identity can contain different model versions, quantisations, providers/backends and role/modal capability profiles. Named levels may be Low/Medium/High/Ultra or other owner-defined labels; they are not necessarily numerical reasoning tiers. Persist the group and member routing in Den for cross-app use.

### V2-B0777 · v2 lines 3753–3753

Selecting a member changes the intended responder; it does not immediately load/unload large models. Resolve and load the selected route on Send under capability/resource/permission checks. An active run remains pinned to its actual model/configuration; a later selection affects later work unless an explicit supported handover occurs. Display the effective backend/member so a group label does not conceal a paid or incompatible route.

### V2-B0778 · v2 lines 3755–3755

### UX27-SPACES-010 — Reasoning effort is not confidence or quota

### V2-B0779 · v2 lines 3757–3757

**O/B.** Retain the approved display mappings for models exposing discrete reasoning effort: three levels map to 35/70/100%; four to 25/50/75/100%; five to 20/40/60/80/100%. These percentages select supported effort tiers; they are not measured intelligence, correctness probability, usage consumption or a promise of continuously adjustable internal reasoning.

### V2-B0780 · v2 lines 3759–3759

Expose only supported levels and distinguish unavailable/disabled reasoning. The UI label and tooltip identify effort. A model-group level and a reasoning level remain separate concepts. The actual selected provider tier is recorded with the request so later evidence is not inferred from a rounded display percentage.

### V2-B0781 · v2 lines 3761–3761

### UX27-SPACES-011 — Usage display and unknown data

### V2-B0782 · v2 lines 3763–3763

**O/B/D.** Usage is separate from reasoning effort and context usage. Hide provider-allowance meters for local models when no such allowance applies. When a likely metered provider supplies no usable data, show the agreed M/unknown indication rather than an invented percentage. Home model settings support pinned/custom limits with explicit unit and provider/model scope; custom values are labelled user-defined, not provider-reported.

### V2-B0783 · v2 lines 3765–3765

Where several real limits apply, show the currently constraining/soonest-blocking limit with details for the others, subject to a user pin override. Never compare raw remaining messages with remaining tokens as if they were the same unit. Any time-to-exhaustion estimate must expose its basis and uncertainty. Retain last-observed timestamps and distinguish stale data from a current allowance reset.

### V2-B0784 · v2 lines 3767–3767

### UX27-SPACES-012 — Research, Shopping and Study surfaces

### V2-B0785 · v2 lines 3769–3769

**O/B/D.** Research presents a plan, source scope, actual run status and report/artifact outputs. Retain the established Quick/Extended/Deep/Custom options where supported, labelling duration as a selected budget rather than guaranteed execution. Shopping separates strict matches, partial/similar matches and other contextual suggestions, with currency/source/freshness and the reason a result qualifies. Do not silently replace a constrained result with a sponsored or merely adjacent item.

### V2-B0786 · v2 lines 3771–3771

Study presents Topics, practice, revision materials and recorded progress from actual answers. Showing content does not count as mastering it. These Space types keep their distinct typed interfaces while sharing conversation, permissions and artifact models; they are not only different system prompts over an identical empty chat screen.

### V2-B0787 · v2 lines 3773–3773

### UX27-SPACES-013 — Assistants, Main chats and teams

### V2-B0788 · v2 lines 3775–3775

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

**O/B.** Assistants is the global system available through Home and editors, with its canonical conversation views retained in Spaces. Each persistent Assistant has its permanent Main conversation and stable Agent identity; renaming or changing an avatar does not replace either. Team interactions use the canonical Dulche coordination and visibility policy, including the agreed All/Relevant/None presentation setting for Assistant-to-Assistant chat where applicable.

### V2-B0789 · v2 lines 3777–3777

Presentation filtering does not hide consequential actions from the audit/permission record. Team membership does not grant every member access to all private context. New or restored conversations use the correct Assistant, tools and source scope without manufacturing another independent runtime.

### V2-B0790 · v2 lines 3779–3779

### UX27-SPACES-014 — Voice & Vision and context indication

### V2-B0791 · v2 lines 3781–3781

**B/D.** Listening, recording, screen sharing and camera use are visibly distinguishable and stop through real controls. Device selection, permission denial, reconnect and end states preserve session identity and transcript where policy allows. Typing during Voice remains available through the shared composer without unexpectedly ending the session. Captions and transcript edits are not silently treated as new commands.

### V2-B0792 · v2 lines 3783–3786

Show which screen/window/source is shared and whether processing is local/cloud. A model that cannot process video/images must receive an appropriate supported representation or a truthful limitation; it must never claim visual inspection from a filename alone. Preserve the base privacy and ephemeral-audio rules.
<!-- UX27:END SECTION-05 -->
Spaces
Product role

### V2-B0793 · v2 lines 3789–3789

9-1 Spaces is the conversational, research, study and agentic-work environment across the 9to1 ecosystem. A Space is a persistent context and behaviour container with stable identity, conversations, sources, model policy, memory policy, connected capabilities and permissions. Spaces operate on canonical app entities through their owning typed APIs and MUST NOT duplicate mutable 9to1 artifacts merely to make them conversational.

### V2-B0794 · v2 lines 3792–3792

Spaces MUST support cross-app actions including Write document mutation, Board/embedded-Canvas editing, Data updates, Planner mutation, Maps route creation, media/project-file actions, Stack operations and Automation coordination whenever the target app exposes the corresponding API. Consequential mutations inherit the target action’s permission/review contract. Spaces MUST NOT fall back to raw UI automation when a typed action exists.

### V2-B0795 · v2 lines 3795–3795

Built-in Space types

### V2-B0796 · v2 lines 3798–3798

The built-in first-party Space types are Chat, Study, Tasks, Agent, Shopping, Research, Translate and Experiences. These are distinct product environments with protected first-party behaviour and UI; they are not merely system-prompt presets.

### V2-B0797 · v2 lines 3801–3801

Chat is the broad general-purpose conversational environment for ordinary questions, app actions, files, web research where enabled, shared objects, tools and agentic work.

### V2-B0798 · v2 lines 3804–3804

Study is the learning environment and MUST support source-backed explanation, topics, practice questions, quizzes, flashcards, progress/weak-area tracking and review scheduling. Study progress MUST be based on recorded interactions/answers rather than inferred solely because material was shown to the user.

### V2-B0799 · v2 lines 3807–3807

Tasks is the goal-oriented work environment and MUST expose structured objective, plan/steps, current step, dependencies, blockers, approvals, outputs, status and run state. Tasks are individual work items/runs and are distinct from Automations, which own recurring/event-triggered execution.

### V2-B0800 · v2 lines 3810–3810

Agent is the environment for interacting with and supervising persistent 9to1 Agents. It MUST surface the selected Agent identity, state, runs, relevant memory/tools and Action Graph access while preserving the Agent definition and permissions owned by the Agents/AI Studio/Dulche contracts.

### V2-B0801 · v2 lines 3813–3813

Shopping is a product-discovery/comparison environment for requirements, candidates, research, saved items and purchase-related context. It MUST preserve source/provenance and MUST NOT treat generated claims as product facts.

### V2-B0802 · v2 lines 3816–3816

Research is the deeper source-oriented research environment for research plans, source sets, citations/provenance, findings, notes and structured outputs. It MUST make source scope and outside-web access explicit.

### V2-B0803 · v2 lines 3819–3819

Translate is the conversational translation/localisation environment backed by Dulche Translate. It uses the normal Space conversation, context, sources, Plugins, Skills, Agents, memory, model policy and permissions rather than a special two-box translator UI. Its translation-specific persistent control is the selected target-language set shown immediately above the standard Space chatbar; there is no separate Translate-specific context row because current artifact/selection/files/sources and semantic app context use the normal Spaces context architecture. Translate supports one or many target languages, conversational refinement, structured Translation Sets/variants, artifact-aware translation and authorised agentic follow-up actions through normal typed owning-app APIs.

### V2-B0804 · v2 lines 3822–3823

For conversational translation into multiple target languages, each language variant MUST render as its own assistant speech bubble rather than being collapsed into one combined multi-language response card. The language/locale name is shown directly above the corresponding bubble, for example French above the French translation bubble, Spanish above the Spanish bubble and German above the German bubble. The bubbles remain siblings of the same TranslationSetID/request so retry, edit, regenerate, apply or publish operations may target one language without losing the others.
Experiences is the conversational stateful environment for open-ended interactive experiences such as roleplay, simulations, interactive stories, mysteries, fictional worlds, scenario training and other AI-driven experiences that do not require the formal contestant/scoring structure of Play. It uses the normal Space conversation, context, Agents, Skills, Plugins, model policy, permissions, memory and Generative UI architecture.

### V2-B0805 · v2 lines 3826–3826

Every active Experience MUST maintain canonical structured experience state rather than treating generated narrative prose as the sole source of truth. Experience state MAY include world/scene/time, characters, relationships, inventory/resources, objectives, flags, timeline, hidden information, rules and typed domain-specific state. When the narrative says a state-changing event occurred, the corresponding canonical state transition MUST be committed before future turns rely on it.
