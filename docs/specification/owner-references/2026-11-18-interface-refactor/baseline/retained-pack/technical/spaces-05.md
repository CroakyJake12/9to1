# Spaces — conversations, workspaces and custom Space Builder — Specialised Spaces retain their own workflows; 9to1 V2 — SPACES UI DECISIONS & WORKING SPECIFICATION (part 05)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2744 · v2 lines 14719–14719

**Given:** Three-/four-/five-tier model fixtures, a local model and a metered provider with unknown usage.

### V2-B2745 · v2 lines 14721–14723

**When:**
1. Change effort and compare displayed tiers.
2. Switch models; inspect usage source/timestamp and custom pin.

### V2-B2746 · v2 lines 14725–14728

**Then:**
- Mappings match the explicit effort table.
- Local allowance meter is absent where inapplicable; unknown usage is not invented.
- Units and custom/provider-reported limits remain distinct.

### V2-B2747 · v2 lines 14730–14730

### UX27-TEST-071 — Specialised Spaces retain their own workflows

### V2-B2748 · v2 lines 14732–14732

**Requirements:** UX27-SPACES-012, UX27-SPACES-013, UX27-SPACES-014 · **Status:** NOT RUN

### V2-B2749 · v2 lines 14734–14734

**Given:** Research, Shopping, Study, Assistants and Voice fixtures with authorised providers.

### V2-B2750 · v2 lines 14736–14737

**When:**
1. Exercise one real plan/report, constrained result group, practice response, Assistant Main chat and Voice permission transition.

### V2-B2751 · v2 lines 14739–14742

**Then:**
- Specialised canonical state exists beyond a changed system prompt.
- Outputs, progress and team visibility follow their domain rules.
- Recording/vision availability and consent are truthful.

### COMPANION-B0001 · companion lines 1–3

9to1 V2 — SPACES UI DECISIONS & WORKING SPECIFICATION
Recorded: 12 October 2026
STATUS: OWNER-APPROVED DECISIONS / WORK IN PROGRESS. SPACES IS NOT COMPLETE. NOT IMPLEMENTATION/ACCEPTANCE EVIDENCE.

### COMPANION-B0002 · companion lines 6–9

AUTHORITATIVE CONTEXT
Master V2: https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit
Evergreen any-stage handoff: https://docs.google.com/document/d/1BWekbHUO0BqUSoIubtBacOcu5ftGztRd-blCJcLyGW0/edit
This is the committed COMPANION ADDENDUM for the Spaces UI decisions in our 12 October review. The master V2 remains the baseline for features not changed here. This companion supersedes inconsistent earlier wording only on explicitly owner-approved points. Treat PROPOSED and OPEN decisions differently from CONFIRMED. It exists separately because repeated Google Docs edits to the very large master V2 were rejected; do not falsely report it as embedded into V2. Later owner-approved addenda override this document in their stated scope.

### COMPANION-B0003 · companion lines 12–13

1 — MOCKUP SCOPE AND CUI THEMES
CONFIRMED: The supplied Chat and Present screenshots depict the default CUI PROFESSIONAL theme. ONLY the two explicitly labelled Glow mockups depict GLOW. Professional: dark, restrained, spacious charcoal UI with Montserrat, coherent optical alignment and softly rounded controls. Glow: warm orange/brown accent-dependent controls, clear Primary/Secondary/Tertiary/Negative/Text variants, animated hover transitions, bounce/brightness on click, stylised input/slider/switch states. No leaking Glow into Professional. All app-facing UI, menus, popovers and keyboard/focus states belong to CUI, not to uncontrolled native Avalonia themes; Home's current theme preference applies across apps. Screenshots are design references and no proof of implementation.

### COMPANION-B0004 · companion lines 16–18

2 — WHAT SPACES IS
CONFIRMED: The Chat screenshots show the SPACES application. Chat is a Space inside it, NOT a separate Chat app. The far-left slim rail contains pinned Space destinations; toolbox/briefcase at bottom is MANAGE SPACES, NOT a fourth-party app. Switching this rail selects a Space; the next region is that Space's CONTEXTUAL SIDEBAR; the large region is the active Space's workspace. Keep V2's supported rail/dropdown switcher preference and stable SpaceID/pinning/reorder/discovery. The Space sidebar is USER-CUSTOMISABLE, and the screenshot's New Chat, Chat Groups, Specialists, Recent Chats arrangement is only its DEFAULT Chat layout, not a fixed or compulsory menu order. Keep V2/LE-05 custom sections, item contributions, Plugins, Automations, other-Space references, drag/keyboard ordering, policy-driven sidebar rules and precedence Space Overrides > User Preferences > Default.
CONFIRMED: The contextual Space sidebar is COLLAPSIBLE independent of the far-left rail, expanding the workspace while keeping reopen access. Preserve active conversation, drafts, recent chats, focus, user customisations, task/run and voice state, with appropriate responsive drawer behaviour. Space-specific layouts remain genuine distinct experiences, not generic Chat with a different name.

### COMPANION-B0005 · companion lines 21–23

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

3 — ASSISTANTS AND SPECIALISTS
V3 OWNER REVISION: ASSISTANTS and SPECIALISTS are global systems available through Home and editors. Spaces retains their canonical conversation views; no separately packaged mandatory Assistants application is implied. This changes placement, not product substance. Preserve persistent Assistant identity, multiple threads, memory with permission, configuration, Tools/Plugins/Skills, model routing, Files/context, background tasks, permitted proactivity, Dev handoff, subagents, permissions, Mini Computer where supported, recovery, checks and history. A renamed generic Space is not enough. Do not silently remove older Agent compatibility contracts.
CONFIRMED: The Chat sidebar SPECIALISTS item opens primarily the user's LIST OF AVAILABLE/OWNED SPECIALISTS. Clicking a Specialist starts a real Specialist conversation with the correct canonical identity, configured job/instructions, model/tool and permission policies. Specialist creation, discovery and configuration belong principally to HOME and other owner interfaces, especially AI STUDIO's Specialist Builder; the Chat list may link there. Specialists are job-specific reusable configured identities, distinct from personal persistent Assistants and generic/delegated agents.

### COMPANION-B0006 · companion lines 26–31

4 — CHAT GROUPS (NOT MULTI-AGENT CHATS)
CONFIRMED: “CHAT GROUPS” is the Chat Space's Projects-style ORGANISATION page containing two selectable container types: GROUP and PROJECT. Neither is a Space and neither is inherently a multi-agent conversation. Both organise canonical member chats without copying or deleting source history.
GROUP: a lightweight bundle of member chats with Group custom instructions and special emphasis on permission-aware SHARED CONTEXT among member conversations. Context retrieval must be traceable and not indiscriminate full-history stuffing.
PROJECT: includes Group features PLUS SOURCES, RULES governing feature availability (e.g. voice, Plugins, permissions and similar switches) and FOLDER/RESOURCE CONNECTIONS to LOCAL, SERVER, REMOTE and CLOUD sources. Show source scope, status and owner; use authorised Home, Files, provider and permissions APIs; a Project cannot override mandatory platform/user security policies.
ONE-WAY CONVERSION: GROUP → PROJECT, preserving members, conversations, context, instructions and identity/history wherever safe. PROJECT → GROUP NOT SUPPORTED, because of Project-only features/settings, and APIs must reject downgrades. Container removal must not silently delete member chats.
PROPOSED UI (NOT YET APPROVED): searchable list/tiles of groups/projects, create actions, member chat list, configuration section, source status and context information.

### COMPANION-B0007 · companion lines 34–43

5 — CHAT TYPE AND LOCATION DROPDOWN
CONFIRMED OWNER CONCEPT: Create/active Chat header has compact icon+label dropdown near TOP-RIGHT (illustrated “Normal ▾”), with two distinct labelled sections:
CHAT TYPE: Normal / Multi-Agent / Temporary
CHAT LOCATION: Local / Mesh / Cloud
These are separate dimensions from Space identity, Group/Project container, selected model and each other. Exact header arrangement/icon remains to review.
NORMAL: ordinary single-model-route conversation.
MULTI-AGENT: shared canonical conversation with multiple AI participants (models/Assistants/Specialists where available and explicitly selected); this is NOT Chat Groups. Message provenance and permission scopes must be maintained. Specific coordination, participant picker, automatic turn-taking and whether human members join have NOT yet been owner-approved in detail.
TEMPORARY: visibly distinct, ALWAYS LOCAL-only until user saves/converts. V2 prohibits persistent Memory creation/reinforcement, Mesh/Cloud transfers and durable cross-Space memory from temporary chats. Remote model execution is separately governed by consent/privacy; LOCAL STORAGE is not local inference. Whether existing memories can be read remains an OPEN question. Do not automatically turn temporary into a permanent chat.
OWNER-APPROVED CONVERSIONS: Temporary → Normal; Temporary → Multi-Agent; Normal → Multi-Agent. Temporary conversion first establishes a durable LOCAL chat, with separately authorised Mesh/Cloud promotion only if requested. Conversions preserve canonical ConversationID/history where safe, do not replay tools or retroactively generate past AI participant replies. No other conversions (e.g. Multi-Agent→Normal or Normal→Temporary) were approved.
LOCATION: Local=current-device storage; Mesh=authorised replica across scoped devices; Cloud=eligible Mesh + encrypted account cloud backup per V2 LE-07. Preserve entitlement and exact quota threshold, security, permission, device list, transfer state, conflict and retry rules. Temporary disables Mesh/Cloud until saved.

### COMPANION-B0008 · companion lines 46–50

6 — PROFESSIONAL CHAT, COMPOSER, TOOLS AND REQUEST WORK
REFERENCE: desktop has narrow Space rail (~70px), Chat sidebar (~280px), compact application header (~80px), large charcoal conversation surface and floating bottom composer; these are APPROXIMATE REFERENCE PROPORTIONS, never fixed-window assumptions. User messages on right, AI responses on left, readable lower-emphasis tool/work status; global Plain Text/Speech Bubbles and alignment preferences from V2 still apply.
REFERENCE: compact pill composer (+ left, Ask Anything input, white waveform Voice toggle right); expanded multiline composer with send arrow at far right, + lower left, Aa formatting and relevant approvals controls. V2 requires Aa and emoji controls when focused AND expanded, not in mini view. Preserve draft through navigation, correct focus, selection and scroll-to-latest behaviour.
REFERENCE: + opens floating Attach to Chat picker with Files, Skills, Plugins & Apps, Specialists, Assistants. It must reuse the canonical Home-owned searchable @ resources/invocation model and stable identity-backed chips; example screenshot has an invoked Chrome chip. Resource/permission scope must be real, not invisible tokens. Work progress may show real elapsed time, steps, an inline quick question/choice, recorded answer, real output. “Resolve Issues” represents genuine actionable errors, not an always-on warning. Preserve Stop/Resume/Retry/Regenerate semantics.
CONFIRMED CORRECTION (13 OCT): The model pill's “100%” is the selected REASONING EFFORT, not usage. Canonical discrete presets: 3 tiers 35/70/100; 4 tiers 25/50/75/100; 5 tiers 20/40/60/80/100. A separate two-colour usage/context progress circle belongs immediately left of the model picker. The four top-right controls were interpreted as Universal Model Picker, Search, Notifications, Profile (still an INTERPRETATION rather than owner-confirmed control semantics). “Ask for Approval” exact composer semantics remain OPEN.

### COMPANION-B0009 · companion lines 53–56

7 — CHAT VOICE MODE = FLOATING PILL OVER CANONICAL VISION & VOICE
CONFIRMED: The white waveform on compact composer toggles VOICE MODE, not a voice/model picker and not merely dictation. It uses FULL existing Vision & Voice APIs and Dulche voice/vision runtime, with same ConversationID/SpaceID/Voice SessionID, enabling real two-way speech, models/voices, tools, camera, screenshots, live screen/window/region sharing, captions, typed text and supported Conversational / Monologue / Live Listener / Live Translate modes.
CONFIRMED: DO NOT navigate directly to standalone Vision & Voice or display an identical full-page Voice interface by default. Instead show a compact, floating, icon-first VOICE PILL within Chat while the conversation stays visible and usable. Preferred direct icons: mute/unmute, share screen, camera, captions, pause/resume, end; show meaningful live waveform/listening-speaking state, concise selected current VOICE and VOICE MODE selectors, ellipsis for extra devices/settings. Optional explicit “Open in Vision & Voice” may show full app and preserve SAME session.
PRIVACY: voice toggle never silently turns on camera/screensharing; explicit permission and persistent capture indicators are mandatory. Stop sharing must stop frames reaching runtime; model capability and local/cloud constraints respected; proper keyboard/touch access, focus, responsive/motion fallback and genuine failure/recovery states. No second proprietary speech/capture stack.

### COMPANION-B0010 · companion lines 59–67

8 — FLOATING SUMMARY AND OPTIONAL TABS
CONFIRMED CORRECTION: SUMMARY is a TOGGLEABLE FLOATING WIDGET ANCHORED AT TOP RIGHT of Spaces workspace, NOT a permanent fourth sidebar or column and MUST NOT shrink the conversation. Match the supplied reference organisation:
ACTIVITY: actual subagents usage/count, task/runs and blockers;
OUTPUTS: real created/modified artifacts or truthful “No outputs yet”;
SOURCES: real used/attached web, memory (where genuinely accessed), files and context with appropriate + source-management action.
Widget scopes to the current Space/conversation/run, scrolls within viewport bounds, follows CUI theme and avoids covering required controls. No fabricated statistics.
PROPOSED (NOT LOCKED): Action Graph toggle may live in Summary's header, opening canonical Home-rendered Dulche ActionGraphID. Owner said “Maybe”; keep status PROPOSED. Other run/task context actions must still allow graph access.
CONFIRMED: Add optional TAB VIEW entry button in Spaces, with horizontal tabs containing genuine separate conversations, real Browse pages (proper browser engine/history/profile), other real first-party embedded CUI apps, artifacts/images, and safe generated UI when appropriate. In tab mode the Spaces interface itself remains a tab. Keep V2 LE-04 stable TabIDs, switch/close/reorder/restore, session/draft integrity, isolation of unrelated tab context, correct background runs, Save/Discard/Cancel for unsaved work, “Open in App” canonical object parity and security. Disabling Tab View hides the strip without destroying open tabs. Do not replace apps with fake reduced copies or tabs with a simulated browser.
CONFIRMED: Temporary Chat entry is the chat type selector; its current-session display/close/save/retention behaviours must respect Temporary privacy and storage policies.

### COMPANION-B0011 · companion lines 70–72

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

9 — ACCEPTANCE AND STATUS
These owner decisions are SPECIFICATIONS ONLY. No actual implementation or acceptance verification was performed in this review. Real acceptance must check: Professional vs Glow theme separation; rail/Manage Spaces and sidebar customisation/collapse; global Assistants with Spaces conversation views and complete persistent functionality; owned Specialist launch; Group/Project features + one-way conversion; Chat types/locations/conversions; genuine @ picker/chips + answered quick question; Voice pill using live full Vision & Voice APIs (including screen/camera control); Summary truly floating with correct data; Tab View hosting actual Browse and other 9to1 apps; state recovery, permissions, responsive/keyboard/touch/reduced-motion and real failures. Existing V2 seven acceptance gates remain mandatory; no screenshots/mockups count as tested product.
SPACES IS IN PROGRESS, NOT FINISHED. Present (Professional screenshot) and Glow were supplied as references; their reviews are not declared complete here.

### COMPANION-B0014 · companion lines 91–92

13 OCTOBER 2026 — OWNER-CONFIRMED CORRECTION TO MODEL/REASONING PICKER AND TELEMETRY HEADER
STATUS: CONFIRMED; supersedes prior “100% unknown” language. Applies to Spaces and the shared Universal Model Picker wherever the same control appears.

### COMPANION-B0033 · companion lines 232–234

SPUI-147 — SHOWN TASKS SCOPE (OWNER CONFIRMED)
The Tasks Space contextual sidebar provides a discoverable 'Shown Tasks >' submenu through right-click/context menu and an equivalent accessible overflow/ellipsis menu. Its exactly specified scope choices are 'All Tasks' and 'Task Space Only'. DEFAULT for new users MUST be 'Task Space Only'. This reinforces that Spaces are first-party contextual work/conversation containers; using Tasks does not, by default, mean displaying every AI task from the whole account.
'Task Space Only' lists eligible canonical tasks originated within the Tasks Space. 'All Tasks' expands the view to other authorised canonical Tasks/Runs initiated through Chat and other Spaces/apps (for example Assistants, Specialists, Dev, Study or Automations where they create an actual TaskID). Choosing All Tasks is a DISPLAY/FILTER preference, not a migration, TaskID/RunID duplication, ownership conversion, extra background execution permission or a new task store. Opening a linked external task must resolve the SAME canonical TaskID, events, checkpoints, active workers, permissions and outputs, and preserve its originating context. Task visibility respects principal, workspace, account, project and storage scope; never reveal inaccessible tasks because 'All Tasks' is selected. The filter MUST NOT suspend hidden tasks or hide independently important user notifications/approval requests. Integrate the choice with existing revisioned Spaces sidebar/user-preference and CUI menus; expose both pointer context-click and keyboard/touch accessible equivalents. Do not confuse the sidebar scope filter with Chat type or Local/Mesh/Cloud storage location.

### COMPANION-B0034 · companion lines 237–238

SPUI-148 — TASK TILES AND SIDEBAR STRUCTURE (OWNER CONFIRMED)
Within Tasks, replace the generic small single-line recent-chat rows with LARGER RICH TASK TILES conceptually similar to the T3 Code screenshot: a readable task title, contextual secondary information (for example where it originated, associated project or task, latest useful activity/time where actually recorded), a conspicuous status badge and applicable contextual task actions. Current activity must be intelligible without opening the entire dashboard. The precise choice of secondary fields and line lengths is still a visual refinement, not a fixed pixel specification. Preserve the existing Tasks sidebar action/navigation proposals where applicable (New Task, Overview, Needs Input, Active Tasks etc) and existing Spaces sidebar customisation/collapse/extension precedence. Do not replace the Spaces rail, force other Spaces to use Tasks-specific tiles or treat the screenshot's projects/repositories as 9to1 requirements. Ensure meaningful active/hover/selected/focus states, truncation with accessible full text, appropriate scrolling and responsive behaviour under existing CUI. A Task row should open the canonical task detail/dashboard, not recreate the Task.
