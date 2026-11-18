# External integrations and provider connections — External App Integrations; Core connection model (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2151 · v2 lines 12235–12306

External App Integrations
External App Integrations is the shared 9to1 framework for connecting, authenticating and operating third-party accounts/services. Authentication, provider scopes, transport, sync, webhooks, rate limits, provider-specific errors and remote-entity identity MUST be encapsulated by this shared layer rather than reimplemented independently inside each consuming app.
Core connection model
The framework MUST define stable typed entities for IntegrationProvider, Connection, ExternalAccount, IntegrationCapability, CredentialReference, RemoteEntityReference, SyncCursor, WebhookSubscription and ConnectionHealth or equivalent contracts. A Connection represents one authenticated provider/account relationship and has stable identity independent of whichever 9to1 app first created it.
Multiple accounts/connections for the same provider MUST be supported. For example, a user may connect separate personal and work Google accounts or multiple GitHub identities without those accounts being merged or ambiguously selected.
Connect once, reuse through authorised capabilities
A provider account should be connected once through the integration framework and then reused by authorised 9to1 surfaces. Consuming apps MUST request capabilities from the existing Connection rather than maintaining private duplicate credentials or running their own hidden authentication flows.
Examples include a Google connection supplying Drive capabilities to Files, Gmail capabilities to Mail, Calendar capabilities to Planner/Calendar and Contacts capabilities to relevant people pickers; a GitHub connection may supply repository/issue/pull-request capabilities to Stack, Dev or authorised Dulche Agents. The exact provider capabilities available remain determined by granted provider scopes and the integration adapter.
Provider scope versus 9to1 permission
Provider authorisation and 9to1 authorisation are separate gates. A Connection may possess provider scopes that a particular app, Agent, Skill, Plugin or extension is not authorised to use. Both the remote provider scope and the applicable 9to1 permission/capability policy MUST allow an operation before it is executed.
Connecting an account MUST NOT implicitly grant every 9to1 surface unrestricted access to that account. Permission decisions should be made at an appropriate capability/resource scope and remain inspectable/revocable.
Typed capability contracts
Consuming apps SHOULD depend on provider-neutral typed capabilities where equivalent semantics exist instead of embedding provider-specific endpoint logic. Examples include Files.List/Read/Upload, Mail.Search/Read/Send, Calendar.ListEvents/CreateEvent, Contacts.Search, Git.ListRepositories/CreateIssue and Chat.ListMessages or equivalent canonical contracts.
Provider adapters map those typed capabilities onto the provider's API. Provider-specific features that cannot be represented faithfully through a common contract may be exposed through explicit provider-namespaced capabilities; the common abstraction MUST NOT silently discard important provider semantics merely to force false parity.
Capability schemas MUST declare input/output shape, supported operations, required provider scopes, relevant 9to1 permissions, pagination/streaming behaviour, provider limitations and error states.
Authentication and credential storage
The integration framework centrally owns supported authentication flows such as OAuth/OIDC, PKCE, device-code flows, API-key authentication or provider-specific enterprise methods where appropriate. Apps MUST NOT request or persist reusable provider credentials directly when the integration framework can broker them.
Credential material such as access tokens, refresh tokens, API keys, passwords and private keys MUST live in approved secure credential storage. Integration records, Den records, logs, exports and app settings store credential references/connection identities rather than plaintext reusable secrets.
Token refresh, expiry, revocation and provider reauthentication are lifecycle concerns of the integration framework. A consumer receives a capability result or typed authentication-required failure, not raw credentials it can reuse arbitrarily.
Scope escalation
If a Connection exists but lacks a provider scope required by a new capability, the consuming surface requests scope escalation through the shared integration framework. The user must be shown which additional provider access is being requested and why. The framework extends/replaces the existing authorisation where the provider supports it rather than creating a hidden duplicate connection.
Remote identity and canonical 9to1 entities
External objects MUST retain their remote identity and provenance. A RemoteEntityReference should identify the provider, Connection, remote object ID, remote revision/version where available, remote URL/reference and any mapped canonical 9to1 entity.
Opening or referencing a remote object does not automatically create a separate canonical copy. The UI and APIs MUST distinguish operations such as Open remote, Import/copy into 9to1, Link/reference and Keep synchronised.
Where an external object is mapped to a canonical 9to1 entity, the relationship must remain explicit so the system does not create multiple disconnected local copies of the same remote item or accidentally treat provider IDs as globally unique without Connection/provider context.
Live, cached, imported and synchronised state
The integration layer MUST distinguish live remote data, cached metadata, cached content, imported local copies and synchronised mirrors. Consuming apps must be able to determine whether displayed information is current, cached, stale or locally diverged.
Caching policies must respect provider terms, account permissions, data sensitivity and user settings. A cached object must not be presented as live merely because a prior request succeeded.
Sync, webhooks and change feeds
Where a provider supports webhooks, push notifications or incremental change feeds, the integration framework SHOULD use them rather than each app independently polling the same account. It owns webhook registration/renewal/validation, change cursors, deduplication, retry/backoff and background refresh.
Polling remains available where required by the provider or capability, but should be centralised enough to avoid redundant traffic and quota consumption. Sync state must include sufficient cursor/revision information to continue incrementally after restart or temporary offline periods.
Offline and queued-operation behaviour
Consumers may continue to display authorised cached state while a provider is unavailable, but the stale/offline state and last successful sync/freshness information MUST remain visible when material. The framework MUST NOT silently claim remote mutations succeeded when they have only been queued locally.
If queued remote writes are supported, each operation needs stable identity, retry/idempotency semantics, dependency ordering where applicable, visible pending/failed state and explicit conflict handling. Reconnection must not blindly replay an action that is no longer valid or that already completed remotely.
Conflicts and versioning
For synchronised/mirrored objects, use provider revision identifiers, ETags/version tokens or equivalent change metadata where available. Wall-clock timestamps alone MUST NOT be the only conflict authority.
Compatible disjoint edits may be merged where the owning capability can prove a safe merge. Otherwise preserve both versions or surface a conflict to the owning app/user. Provider-side deletion, permission loss and remote moves/renames require explicit typed states rather than being treated as generic missing-file errors.
Connection health and failure states
Connections expose typed health/lifecycle states such as Connected, AuthenticationRequired, ScopeRequired, RateLimited, ProviderUnavailable, Degraded, SyncError, Revoked and Disabled or equivalent. Apps consume these states and present appropriate user-facing recovery rather than leaking raw HTTP/provider exceptions.
Health information should include the failing capability/provider account where relevant, last successful contact/sync, retryability and the action required from the user when one exists.
Rate limits and reliability
The shared layer MUST centrally support pagination, retry-after handling, provider quotas, transient network retries, exponential backoff/jitter, idempotency, request cancellation, partial response handling and provider-specific service limits. Consuming apps MUST NOT each invent incompatible retry/quota behaviour.
Connection management UI
9to1 MUST provide a central integrations management surface, hosted under the appropriate Home/Settings area, showing provider, connected account identity, connection status, provider-granted scopes/capabilities, authorised 9to1 consumers where exposed, recent/last successful activity, errors, reconnect/re-authorise, permission management and disconnect/revoke actions.
Individual apps may deep-link into this management flow or expose capability-specific connection prompts, but connection state remains owned by the shared framework.
Dulche and Agent access
Dulche, Agents and AI surfaces use External App Integrations through the same typed capabilities and permissions as first-party apps. Dulche does not own a parallel account-authentication layer.
For example, a request to find an email uses an authorised Mail capability for an existing connection; a request to create a GitHub issue uses the relevant Git capability. Normal Dulche tool/action permissions and consequential-action confirmation requirements still apply. Merely connecting an account does not grant every Agent permission to mutate it.
Relationship to Plugins and MCP
External App Integrations, Plugins and MCP are distinct abstractions. External App Integrations represent durable authenticated third-party account/service relationships and their capabilities. Plugins are installable packages that may add executable/integrated functionality. MCP is a protocol by which Dulche can discover/use tools/resources.
They may interoperate: a Plugin may register a provider adapter; an integration capability may be surfaced to Dulche through an approved tool/MCP bridge. Such composition MUST preserve the underlying Connection identity, provider scopes and 9to1 permission checks rather than laundering authority through another abstraction.
Provider SDK and extension
9to1 SHOULD expose a versioned Integration Provider SDK/manifest contract so additional providers can be implemented without changing every consuming app. A provider definition can declare provider identity/branding metadata, authentication method, capability schemas, provider scopes, remote entity types, webhook/change-feed support, rate-limit semantics, health checks and provider-specific settings.
Third-party provider adapters remain sandboxed/permissioned according to their implementation model. Registering a provider does not grant unrestricted access to other Connections, credential stores or unrelated local data.
Disconnect and revocation
Disconnect semantics MUST be explicit and predictable. Disconnecting a Connection should stop sync/background activity, invalidate or remove local credential references as appropriate, disable provider capabilities and clear/expire caches according to policy. Where supported and requested, provider-side token revocation should also be attempted.
Disconnecting an account MUST NOT automatically delete unrelated local 9to1 artifacts previously imported from it. For linked/synchronised objects, the UI must explain what local data remains, what becomes unavailable and whether any pending remote operations are abandoned.
Security invariants
* Possessing a provider credential does not grant any app, Agent, Skill, Plugin or extension unrestricted access to the associated account.
* Every external operation must pass both provider scope/capability validation and the relevant 9to1 permission boundary.
* Consuming apps receive capability access/results, not raw reusable credentials.
* Connection/account identity, remote-object identity, provenance and provider limitations must remain explicit across app boundaries.
* Logs, exports and Den records must not contain plaintext reusable provider secrets.
External App Integrations acceptance requirements
* One authenticated Connection can be reused safely by multiple authorised 9to1 apps without duplicated credentials or hidden per-app OAuth implementations.
* Multiple accounts per provider are unambiguous and stable.
* Typed provider-neutral capabilities work where semantics align, with provider-specific extensions exposed explicitly rather than silently discarded.
* Provider scopes and 9to1 consumer permissions are enforced as independent gates.
* Live/cached/imported/synchronised states, remote identity and conflict/version state remain explicit and recoverable.
* Webhooks/change feeds, polling, retries, rate limits, queued operations and connection health are handled centrally with deterministic failure semantics.
* Dulche/Agents use the same connection/capability layer and cannot bypass account permissions.
* Disconnect/revocation, secure credential storage and provider SDK integration are implemented without deleting unrelated local artifacts or exposing reusable secrets.

### V2-B2324 · v2 lines 13481–13481

### UX27-OTHER-014 — Connect and account health

### V2-B2325 · v2 lines 13483–13483

**B/D.** Connection cards display provider/account identity, granted capabilities, actual health and last successful observation. Connect/reconnect shows the real provider authorisation flow; disconnect explains affected features and revokes future access through the supported contract. A healthy local app must not become unusable because an optional connector is absent.

### V2-B2326 · v2 lines 13485–13485

Do not store credentials in exported documents, previews or logs. An account shown in a list is not proof that every requested scope is granted. Failed permission requests remain failed; the Agent cannot silently select a different account to avoid a denial.
