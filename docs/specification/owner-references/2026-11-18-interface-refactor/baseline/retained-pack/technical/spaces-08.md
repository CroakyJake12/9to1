# Spaces — conversations, workspaces and custom Space Builder — CONFIGURABLE PER-MESSAGE ACTION TOOLBARS; DEFAULT AI RESPONSE ACTION SEMANTICS (part 08)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### COMPANION-B0069 · companion lines 457–458

13 OCTOBER 2026 — UNIVERSAL SPACES MESSAGE/COMPOSER FEATURES, PLUGINS AND DULCHE SECURITY ADDENDUM
STATUS: OWNER-APPROVED WITH THE EXPLICIT SECURITY AND LOGISTICAL CORRECTIONS ACCEPTED IN THE 13 OCTOBER REVIEW. Sections SPUI-180–199 supplement the master V2 and this Spaces companion, including the shared canonical CUI, Home-owned user preferences and permissions, Spaces message/branch/Task/Run APIs, Dulche agents/models/Den/Memory/Autoreview, Vision & Voice and owning-app typed APIs. The master incorporates owner-confirmed companion decisions by reference. These are REQUIREMENTS, not evidence that a product has been built or tested; unresolved definitions are identified rather than guessed. Scope is universal across relevant built-in/user-created Spaces and any other 9to1 surfaces that use the shared component, subject to their explicit capability/access restrictions. Do not weaken the Privacy Ceiling or independent acceptance because an interface is convenient.

### COMPANION-B0070 · companion lines 461–465

SPUI-180 — CONFIGURABLE PER-MESSAGE ACTION TOOLBARS
The standard action row BELOW an AI response is a genuine, accessible, CUSTOMISABLE CUI toolbar. The user may Add Items, Remove Items, Rearrange/drag (including keyboard reordering), restore defaults and manage overflow. The corresponding action row below a USER message is independently customisable. Maintain sensible shared user/account defaults, with optional supported per-Space overrides under Home/Spaces preference precedence; preserve customisation through restart, account-scope synchronisation where authorised, theme switch, collapse, touch and viewport change. Every visible action uses the real canonical MessageID/ConversationID/BranchID and appropriate owner API. Invisible/disabled actions must have truthful applicability/permissions and never simulate success.
DEFAULT AI row: Copy, Speak, Retry, Branch, React, Sources.
DEFAULT USER row: Copy, Edit, Delete.
The user can add an OPTIONAL measured average TOKENS PER SECOND text to AI responses and can insert authorised CUSTOM AUTOMATION actions into the AI toolbar. Per-message token-speed values must be based on actual provider/Dulche output-token counts and measured generation duration, distinguish unknown/unavailable from zero, and label the metric sufficiently to avoid treating tool wait time or model reasoning effort % as generation throughput. Never fabricate provider/token values. Custom Automation items use canonical AutomationID, input schema, permissions, resource limits, confirmation and idempotency, not arbitrary unauthorised code or a second automation runtime.

### COMPANION-B0071 · companion lines 468–469

SPUI-181 — DEFAULT AI RESPONSE ACTION SEMANTICS
COPY exposes a clear choice to copy that AI message or the whole relevant conversation/thread, with explicit active branch/selection semantics and appropriate source/tool inclusion policy. SPEAK reads the actual selected response via shared authorised Dulche/Vision & Voice speech output. RETRY supports the SAME or a DIFFERENT eligible model (through the canonical Universal Model Picker), with an optional custom retry instruction; original attempts/provenance remain inspectable. Retry/Regenerate/Resume are NOT conflated: retries and regenerated alternatives use an appropriate new attempt/branch, while a true Resume continues from saved state; completed consequential tool actions must not blindly repeat. BRANCH can open a NEW conversation branch/chat or intentionally branch/transfer into an EXISTING compatible chat, preserving origin and branch provenance, scope and permissions; it cannot erase the original, silently merge unrelated histories, copy private context into a broader chat, or claim to undo external side effects. REACT uses SPUI-182. SOURCES opens actually used/supporting provenance, passages, files/web references or an honest no-source state; merely offered tools are not listed as used. All actions support focus/keyboard/touch and accessible labels, with correct empty/error/permission states.

### COMPANION-B0072 · companion lines 472–475

SPUI-182 — RESPONSE REACTIONS, PREFERENCES AND REPORTING
React supports the owner's exact meaningful choices:
👍 Like; 👎 Dislike; 🙂 Good; 😂 Funny; 🥲 Sad; 😨 Scary; 😡 Upsetting; 🗑️ Rubbish; 🏴 Inappropriate.
These are RESPONSE FEEDBACK EVENTS tied to stable MessageID, author/model provenance and user identity, stored subject to explicit preferences, consent, retention and memory policy. They may improve authorised user-level response-style preferences, but no reaction automatically grants memory access, deduces protected personal traits or authorises training/disclosure unrelated to the user's settings. The AI MUST be able to respond contextually to a reaction as a subsequent message when appropriate; do not force unwanted chatter or fabricate a model response on every click. An inappropriate reaction begins a REAL content-reporting workflow addressed to Support@CakeMods.com (or its authenticated canonical reporting gateway). Show exactly which message, excerpts, context and identifiers will be submitted, obtain the necessary deliberate user confirmation, limit disclosure to the report's purpose, and record actual delivery/reference or failure; do not silently email the entire private thread or pretend a report was sent. Reports respect applicable moderation/privacy procedures and appeals.

### COMPANION-B0073 · companion lines 478–479

SPUI-183 — USER MESSAGE ACTIONS AND NONDESTRUCTIVE HISTORY
DEFAULT USER COPY copies only that selected user message. EDIT offers three visibly distinct operations: (1) “Replace messages below”: change the message and supersede downstream messages in the CURRENT ACTIVE VIEW/BRANCH while preserving their recoverable historical branch/event records and actual external side effects; (2) “Update context and new messages only”: create a versioned, prospective correction used for subsequent model context/replies WITHOUT retroactively changing existing responses; (3) “Edit in branch”: construct a new, versioned conversation branch at the selected point while retaining the original branch and provenance. DELETE offers “Delete message” and “Delete message and response”; deletion from the visible active conversation is not assumed to destroy audit, approval or external action history, and genuine data erasure must follow canonical authorised storage/retention/privacy lifecycle. For responses with multiple alternatives, deletion targets must be explicit. Never promise that rewriting history automatically reverses purchases, publishes, tool calls or files. Preserve correct MessageID/BranchID/TaskID links, conflict handling, undo/recoverability where valid, notifications to affected in-flight workers and user-visible scope.

### COMPANION-B0074 · companion lines 482–483

SPUI-184 — USER-REORDERABLE AI DISPLAY NAME INHERITANCE
Users can choose what name is displayed ABOVE AI messages through a drag/keyboard-reorderable priority list of name providers. Canonical starting list: “Model Name” | “Assistant” | “Specialist” | “+ Add”. “+ Add” accepts custom user-defined name entries and/or suitable authorised automation-connected dynamic name providers. DEFAULT without a user override is the model's USER-CONFIGURED displayed name (e.g. “GPT 6 Astra”, “Qwen 3.8”); do not substitute an invented vendor label. Apply highest-priority eligible available provider, with explicit fallback when that type is absent. Model/Assistant/Specialist entries are distinct types; user may reorder to prioritise an Assistant's named identity or a Specialist in suitable contexts. User-visible alias is PRESENTATION ONLY: author principal, canonical Assistant/Specialist IDs, actual provider/model/route, response provenance, permissions, safety/audit and Team speaker identity remain recoverably distinguishable and cannot be falsified by custom names or automation values. Version settings and avoid retroactively misattributing old messages.

### COMPANION-B0075 · companion lines 486–512

SPUI-185 — UNIVERSAL COMPOSER SLASH-COMMAND INVENTORY
A leading “/” in an eligible 9to1 conversation composer opens an accessible, searchable, keyboard-operable command picker with canonical typed command IDs, descriptions, context-specific input fields and suggestions. Preserve ALL owner-listed initial commands (names are UI labels and are not to be silently omitted):
/NewChat — create a conversation;
/Branch — open branching UI;
/Model — select an eligible model;
/Reasoning — select the actual advertised reasoning tier;
/Pin — pin the current chat;
/Bug — prepare a bug report;
/ResolveIssues — inspect/recover genuine issues;
/Archive — archive current chat;
/Stop — stop current response/request;
/ResetContext — reset/reconstruct active working context;
/Restart — restart the current response safely;
/BackgroundChat — prompt an existing or newly created other chat WITHOUT leaving this chat;
/Memory — open relevant authorised memory controls;
/Permission — inspect/adjust permitted scopes;
/Attach — open shared attachment picker;
/Copy — copy current whole thread;
/CopyLatest — copy latest AI response;
/Read — speak/read current whole thread;
/ReadLatest — speak/read latest message;
/Move — move chat to an eligible Project/Chat Group;
/Dictate — toggle dictation;
/Summary — toggle the anchored floating Summary;
/Tabs — toggle optional Tab View;
/Voice — toggle canonical voice mode.
Commands can show typed arguments, target selectors, confirmation, preview, entitlement/permission notes and extra fields as appropriate. The picker can grow with safely registered system/plugin/Space commands. Use canonical names above as documented aliases, while maintaining any required existing API compatibility. /Reasoning maps to the actual model's discrete provider LevelIDs per SPUI-133; do not pass percentages through incompatible rounding. /Tabs hides/reveals view without destroying tabs; /Voice uses the full shared Vision & Voice session; /Summary toggles the existing top-right floating widget, not an invented extra sidebar.

### COMPANION-B0076 · companion lines 515–516

SPUI-186 — SLASH COMMAND EXECUTION, CONTEXT AND SAFETY
Slash actions are explicit structured commands routed by Home/Dulche through typed APIs, never uncontrolled text or string substitution. Quoted text, pasted scripts, Markdown/code blocks and normal text containing slashes MUST NOT accidentally execute; preserve slash-literal entry, autocomplete escape/cancel, reliable selections, undo/draft retention and keyboard/IME. Contextual availability must be truthful across Spaces, Assistants, Teams and other shared composing surfaces. /NewChat, /Move, /Branch and /BackgroundChat preserve canonical object IDs, target permissions and current conversation draft; BackgroundChat requires correct chosen target/new chat and actual authorised send, never a simulated background delivery. /Stop, /Restart and /ResolveIssues honour stopped/resumable/cancelled/failed distinctions and consequential side-effect idempotency; /Restart is not secretly Resume and must preserve source history. /ResetContext does NOT itself delete original messages, memories or source history. /Memory and /Permission expose only scopes the signed-in user may legitimately modify; /Bug previews reporting data and requires permission where needed. /Read uses the genuine speech runtime with cancellation. No command silently elevates privileges, leaks a hidden user's source context or starts a tool call without a legitimate execution boundary.

### COMPANION-B0077 · companion lines 519–520

SPUI-187 — CLICK-TO-CONFIGURE PLUGIN TILES; PERMISSION AND TIMEOUT
Clicking a Plugin tile (e.g. Computer Use) opens its genuine CUI configuration panel, with supported editable options including the requested “Permissions” policy (example: “Ask once per App”) and “Timeout per Call” (example: “None”). Controls bind to actual canonical PluginID, revisioned configuration, authorisations, tool-call semantics and Home-owned identity/permission provider, not decorative local state. The actual set of permission choices is capability-/policy-dependent, with “Ask once per App” scoped to the VERIFIED target app identity, bounded action classes, selected resources/session, user and applicable policy. It must not become blanket permission to control unrelated apps, new accounts, credentials, destructive actions or future changed scopes. Review/revoke/update and clear effect descriptions are required. “Timeout per Call = None” disables only the user-imposed time limit, NOT mandatory runtime watchdogs, safe cancellation, execution backstops, network constraints or recovery limits. Explicitly show when a plugin cannot support a setting, rather than pretending it is enforced. Requests remain within the actual API contract and Home/Dulche approval checks.

### COMPANION-B0078 · companion lines 523–527

SPUI-188 — PEER REVIEW PLUGIN: MODES, MODEL CHOICE AND SCHEDULE
Add configurable “Peer Review” Plugin using the EXISTING Dulche agentic/Task/Run/Autoreview/verification services. Two distinct behaviours:
SINGLE-THREAD: after a bounded agentic pass, one or more independently configured reviewer models check output against the original objective, constraints, accepted plan, tests, artifacts and evidence for mistakes, deviations, stale tests, security/permission or integration failures. May use another configured model OR an isolated fresh instance of the same model with separate appropriate context; never fabricate independence or copy private hidden reasoning/credentials.
PERSISTENT: review ongoing work on meaningful CHECKPOINTS and/or roughly every 20–30 minutes at a natural safe break; an evidence-based reviewer can adapt the interval (e.g. every 1–2 hours after sustained compliance or around every 5 minutes during repeated mistakes/repair), respecting checkpoints, actual activity, model cost, worker responsiveness and limits. The worker and reviewer MAY discuss disputed findings and request evidence, but the reviewer MUST independently inspect observable work, requirements and tests rather than simply accept a worker's assertion. Review decisions are versioned, inspectable and linked to correct TaskIDs/RunIDs; no duplicate alternate task store or invented automatic “PASS”.
Peer Review settings MUST expose “Models” with user-selectable configured reviewers and “+ Add Model”; default illustrated selection “Qwen 3.8 27B” is an EXAMPLE, not a mandate to install/claim availability. Expose “Max Review Time” as a user-configurable percentage of PRODUCTIVE ACTIVE TASK WORKING TIME, with the owner's selected default example of 25%. The meaning and enforcement are NOW RESOLVED in SPUI-200: the aggregate active peer-review duration cannot exceed that percentage of genuine productive task execution time. This is not a wall-clock fraction, token budget or 25% of combined work plus reviews. Mandatory safety, human approval and independent acceptance gates remain binding.

### COMPANION-B0079 · companion lines 530–536

SPUI-189 — PEER REVIEW POWERS, APPROVAL AND INDEPENDENT ACCEPTANCE
The owner-specified configurable DELEGATED POWERS and proposed initial checkbox states are:
CHECKED: Pause, Resume, and Restart Work; Perform Interventionary Work (e.g. correcting a detected code mistake).
UNCHECKED: Restart Worker; Reset Worker Context; Change Worker Model; Stop/Quarantine Workers.
Additional CHECKED option: “Require All Risky Worker Tool calls to be Approved by a Peer”.
Each enabled capability requires real, explicit Home/Dulche delegated authority, scope and target restrictions, event audit, revocation and recovery. Distinguish restarting WORK from terminating/replacing a WORKER, and NEVER lose valid approved checkpoints, task identity or accepted outputs. Interventionary work follows normal source ownership, diff, tests, permission and conflict rules. Changing a worker model or context respects capability, model route/privacy, recoverability, memory boundaries and user's current authorisation. Quarantine prevents further potentially harmful actions under a documented recoverable policy; it does not stealthily destroy work or erase evidence.
When “Require All Risky Worker Tool calls to be Approved by a Peer” is ON, actual policy-classified risky calls require independent reviewer approval BEFORE executing; if the reviewer is unavailable, HOLD just the gated action while permitting independent safe work where valid. No model peer's approval EVER replaces mandatory human consent, administrator policy or canonical safety enforcement. Failed reviewer checks, unverified output and wrong model interpretations remain visible; a peer's reported PASS alone does not establish VERIFIED. Relevant approval evidence, reviewer identity, task revision, rationale and risk classification are inspectable. The reviewer cannot grant itself unapproved powers or weaken tests to finish.

### COMPANION-B0080 · companion lines 539–540

SPUI-190 — MULTIPLE RESPONSES PLUGIN
Add optional “Multiple Responses” Plugin producing multiple distinguishable assistant answers to one eligible request. UI settings “Responders” let users add/remove/reorder independently configured model/route instances, INCLUDING REPEATED INSTANCES OF THE SAME MODEL (e.g. two Qwen 3.8 27B instances and one Qwen 3.6 27B), with “+ Add”. Responses retain their own generation AttemptID/MessageID/actual model/provider identity, routing, elapsed/usage information where knowable and review/selection state. Provide genuine side-by-side/stacked inspection, comparison, choosing an answer and safe branch/follow-up operations without overwriting other valid responses. Do not claim repeated models are statistically independent simply because run twice. Shared user resources/permissions and privacy checks apply independently to each call. Prevent unreviewed duplicate consequential tool actions across responders: such effects require explicit authorised coordination/idempotency; do not multiply paid or destructive operations merely by multiplying generated responses. Show truthful resource spending/limits and failures rather than concealing them.
