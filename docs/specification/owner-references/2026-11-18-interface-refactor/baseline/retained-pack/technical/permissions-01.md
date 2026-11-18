# Permissions, approvals and execution scope — App APIs and automation permissions; API parity requirement (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0132 · v2 lines 437–439

App APIs and automation permissions
API parity requirement
Every 9to1 app must expose a first-party automation API whose action surface is exhaustive for user-accessible app functionality. An authorised MCP, plugin, script, agent or other client must be able to perform anything the user can perform in the app through supported UI, subject to the same account, object, platform and security restrictions. APIs must not be intentionally limited to a small automation subset while important user actions remain UI-only.

### V2-B0133 · v2 lines 442–447

Each app owns a canonical API namespace and a complete action catalogue. Example:
9to1.Boards.ListBoards();
9to1.Boards.CreateBoard();
9to1.Boards.AddPage();
UserBoards = 9to1.Boards;
UserBoards.AddPage();

### V2-B0134 · v2 lines 450–450

Aliases/references such as UserBoards are permitted for convenience but do not create a new identity, permission boundary or trust grant. Language bindings may use a syntactically valid representation of the 9to1 namespace where a language cannot use 9to1 as a literal identifier; canonical app/action names and behaviour remain unchanged.

### V2-B0135 · v2 lines 453–453

The action catalogue is part of the app contract. It must include read, create, edit, move, organise, import/export, collaboration, configuration, deletion/recovery and other actions that the corresponding user can perform. Actions that are unavailable to the current user because of role, ownership, account, provider or platform restrictions remain unavailable through the API.

### V2-B0136 · v2 lines 456–456

Every action must declare its name, arguments/schema, result schema, required target-app permissions, risk level, whether it is reversible, whether it can produce external side effects, and the objects/scopes it affects. New UI capabilities are not complete until their equivalent API action is added. Deprecated actions require a documented migration path rather than silently disappearing.

### V2-B0137 · v2 lines 459–460

Home permission broker
9to1 Home is the central permission broker for app APIs. A caller never dispatches a protected action directly to the target app without Home first resolving its permission state. If Home is not open, 9to1 opens it and shows the approval prompt before the target action executes. A caller may submit the request to the broker, but the requested app action remains PendingApproval until Home grants it.

### V2-B0138 · v2 lines 463–463

The prompt MUST identify the requesting app/script/MCP, verified caller identity/origin when available, target 9to1 app, requested action names/scopes, elevated/destructive risk and affected object identifiers/counts whenever they can be determined before execution. If impact cannot be determined safely before execution, the prompt MUST display that impact as unknown rather than omit it. Generic wording such as “allow access” is insufficient for scoped or powerful actions.

### V2-B0139 · v2 lines 466–466

Permission requests MAY group actions only when they share the same caller identity, target trust boundary and compatible risk level, and the prompt exposes the complete grouped scope before approval. A grant covers only the exact actions/scopes shown or already trusted. Adding a new action, target scope or higher risk level requires a new approval decision.

### V2-B0140 · v2 lines 469–474

Prompt actions
* Decline
* Decline and Block
* Accept
* Accept and Trust
* Accept and Always Trust

### V2-B0141 · v2 lines 477–477

Decline denies the current permission request without blocking future requests.

### V2-B0142 · v2 lines 480–480

Decline and Block denies the request and blocks that caller from requesting or using 9to1 app API permissions until the user explicitly unblocks it in Settings. Block state is tied to the caller identity, not merely its display name.

### V2-B0143 · v2 lines 483–483

Accept approves only the current requested action set/scope. When that request/session scope ends, later protected actions require approval again unless another trust grant applies.

### V2-B0144 · v2 lines 486–487

Accept and Trust
Accept and Trust approves the requested ordinary action scopes and trusts that caller for 30 days. During that period, matching ordinary actions can execute without repeatedly opening an approval prompt. The 30-day trust period expires automatically and can be revoked at any time in Settings.

### V2-B0145 · v2 lines 490–490

Accept and Trust does not automatically authorise high-risk or suspicious actions. Actions such as deleting a board, mass deleting pages/items, bulk destructive changes, changing security/permission settings, destructive account actions or similarly sensitive operations require explicit approval each time unless the caller has an applicable Accept and Always Trust grant.

### V2-B0146 · v2 lines 493–494

Accept and Always Trust
Accept and Always Trust is intended only for software the user deliberately grants persistent trust, including self-developed scripts/apps. Within the exact granted scopes and the user’s underlying authority, it allows the trusted caller to use app APIs without routine 9to1 approval prompts, including high-risk actions explicitly covered by the grant.

### V2-B0147 · v2 lines 497–500

Before Always Trust is granted, Home must show a prominent warning that this level of access can allow the caller to read, modify or destroy user data without asking again. The warning presents:
* Go Back
* Grant Temporary Trusted Access
* I Understand, Continue

### V2-B0148 · v2 lines 503–503

Grant Temporary Trusted Access lets the user choose either a limited number of actions or a limited duration. The user also chooses what happens when that temporary grant ends: return to manual approval or roll down to the normal 30-day Accept and Trust level. The temporary grant must show its remaining action count/time and can be revoked early.

### V2-B0149 · v2 lines 506–506

I Understand, Continue grants Always Trust only after the warning step. Always Trust has no automatic expiry, but remains visible and revocable in Settings.

### V2-B0150 · v2 lines 509–509

Always Trust bypasses 9to1's routine approval prompts only. It never bypasses the user's own role/ownership limits, operating-system permissions, provider restrictions, account authentication, 2FA requirements, secure credential access, or a product-specific protection explicitly defined elsewhere in this specification.

### V2-B0151 · v2 lines 512–513

Caller identity and trust binding
Trust must be bound to a stable caller identity rather than a mutable display name. Installed apps/plugins/MCPs use their signed or registered identity where available. Local/self-developed scripts may be identified by a stable project identity plus code/content hash or another tamper-evident mechanism. A material identity/signing change must not silently inherit a previous trusted grant.

### V2-B0152 · v2 lines 516–516

Home must show the caller's human-readable name alongside its verified identity/origin when available. Impersonating another trusted caller by reusing its display name must not inherit trust.

### V2-B0153 · v2 lines 519–520

Risk classification and sensitive actions
Risk level is declared by the target 9to1 app/action definition, not by the requesting caller. Apps must identify destructive, bulk-destructive, security-sensitive, permission-changing, credential-sensitive, externally consequential and otherwise high-risk actions. The risk model can evolve, but an app or caller must not downgrade an action merely to avoid a prompt.

### V2-B0154 · v2 lines 523–523

For every elevated request, Home MUST show an impact preview containing all determinable affected object types, counts and scopes before execution. Values that cannot be determined without performing the action MUST be shown as unknown. Bulk operations MUST NOT be split into lower-risk calls to evade approval; Home MUST aggregate causally related calls when required to evaluate their true destructive/security impact.

### V2-B0155 · v2 lines 528–529

Revocation, expiry and failure behaviour
Users can inspect, narrow, revoke and block API grants from Home/Settings at any time. Revocation applies before future protected actions are dispatched. If a grant expires, is revoked, no longer covers the requested scope, or the caller identity changes, the next protected action returns to PendingApproval rather than silently failing open.

### V2-B0156 · v2 lines 532–532

If Home cannot display a required prompt, the action must not execute. Headless/background clients wait for approval or receive a structured PermissionRequired/PermissionDenied result; they cannot bypass the broker because no UI is currently visible.

### V2-B0157 · v2 lines 535–536

Audit and transparency
Every API request and permission decision MUST be auditable. Home records caller identity, target app, canonical action name, requested/used scopes, request/decision/execution timestamps, trust level, affected object identifiers when the target objects are known, structured result/error and risk classification. Block, trust, expiry, revocation and Always Trust changes MUST also be logged.

### V2-B0158 · v2 lines 539–539

The audit trail must distinguish a caller requesting an action from the target app actually executing it. Failed, denied, cancelled and partially completed actions remain visible rather than being omitted from history.

### V2-B0159 · v2 lines 542–543

Implementation rule
This API requirement applies to every current and future 9to1 app. App-specific sections define their exhaustive action sets. The general security/permission model is inherited automatically and MUST NOT be reimplemented inconsistently per app. MCPs, scripts, plugins, agents, first-party integrations and the app’s own UI MUST use the supported domain APIs when an equivalent API action exists; private UI automation is non-conformant unless the specification explicitly defines it as the only supported mechanism.

### V2-B0458 · v2 lines 2354–2354

Home owns the operational permission/trust broker defined by App APIs and automation permissions. It stores and evaluates caller identity, scoped grants, blocks, temporary trust, 30-day Accept and Trust grants, Accept and Always Trust grants, expiry/revocation state and audit history.

### V2-B0459 · v2 lines 2357–2357

Settings MUST expose a Permissions & Trust management surface where the user can inspect caller identity/origin, granted apps/actions/scopes, trust level, creation/expiry, last-use metadata where available, and revoke/block/unblock/narrow grants. Revocation MUST affect future actions immediately subject to in-flight action semantics defined by the target API.

### COMPANION-B0091 · companion lines 604–606

SPUI-200 — PEER REVIEW MAX REVIEW TIME: PRECISE ACTIVE-TIME DENOMINATOR (OWNER CONFIRMED)
Resolve SPUI-188 and the former SPUI-199 open question definitively. “Max Review Time: 25% of Task” means: for every X minutes of ACTUAL PRODUCTIVE TASK EXECUTION WORK, the allowed cumulative time spent by peer-review models is at most 25% × X (or the user-selected configured percentage). At 4 hours productive work, up to 1 hour aggregate review work is allowed. Formal budget equation: R <= (p/100) × W, where W is real productive active task-work duration EXCLUDING reviewer execution, waiting, pausing, approvals, idle time or merely keeping a host connected; R is AGGREGATE REAL ACTIVE PEER-REVIEW EXECUTION DURATION across all reviewer instances, not an independent allowance per reviewer. Review after each checkpoint or adaptive intervals draws from the SAME budget, including deliberation with workers, reinspection and intervention-related review activity; reviewers must not hide their time in the worker counter or create additional independent caps. Active overlapping productive worker intervals use an honest canonical task clock rather than summing simultaneous worker durations to artificially inflate W; track actual review-resource cost separately when parallel reviewers operate.
Show used/allowed peer-review time and remaining headroom clearly, update using actual measured Task/Run events, and allow the user to change the percentage subject to policy/resource caps and explicit authorisation. This percentage is an overhead LIMIT, NOT a quota peer review must consume and NOT a guarantee of review completeness. If the remaining budget is exhausted, do not silently keep spending reviewer time, pretend a review was performed or mark acceptance VERIFIED. Necessary policy/permission/independent-review gates still apply: suspend only the risky gated action, continue safe independent work where valid, or seek explicit authorised adjustment/review capacity. Never use budget exhaustion to bypass a required human approval, acceptance test or safety review. Validate worker clock, reviewers with different model backends, parallel reviews, pause/restart/reconnection and partial review recovery without duplicate accounting. The default percentage can be user-configured; the owner-confirmed example is 25%.
