# Privacy, data boundaries and context isolation — Privacy and data-protection baseline; UCML APP PERMISSIONS, OUTPUT CONTROL, AND PROMPT-INJECTION DEFENCE (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0112 · v2 lines 376–377

Privacy and data-protection baseline
9to1 MUST apply a platform-wide Privacy Ceiling baseline to every app, framework, cloud service, hosted component, telemetry path, AI feature and operating-system integration that processes personal information. Product teams MUST NOT treat privacy as an app-specific optional enhancement.

### V2-B0113 · v2 lines 380–380

The Privacy Ceiling is derived from the strongest compatible protection required across the supported United States jurisdictions, the United Kingdom and the European Union/EEA, plus deliberate engineering headroom. The implementation MUST maintain a versioned LegalPrivacyProfile describing the currently applicable statutory/regulatory requirements for each supported jurisdiction. Where multiple requirements are compatible, the most protective requirement becomes the global default. Where applicable laws genuinely conflict, the exception MUST be narrowly jurisdiction-scoped, documented, legally reviewed and MUST NOT lower protection for unaffected users or data.

### V2-B0114 · v2 lines 383–383

Engineering headroom means 9to1 SHOULD collect less data, retain it for less time, expose it to fewer systems, process it locally more often and encrypt it more comprehensively than the minimum legally permissible where doing so does not prevent the requested product function. A legal maximum retention period, collection scope or disclosure permission MUST NOT automatically become the product default.

### V2-B0115 · v2 lines 386–386

Privacy by design and by default is mandatory throughout the product lifecycle. Every processing activity MUST have a declared purpose, lawful/authorised basis where required, data categories, data subjects, recipients/processors, locality/residency constraints where applicable, retention/deletion policy, access-control model and security classification. New processing purposes MUST NOT silently reuse personal data collected for an unrelated purpose merely because the data is technically available.

### V2-B0116 · v2 lines 389–389

Data minimisation is mandatory. A component MUST request, collect, transmit, persist and expose only the personal data necessary for its declared purpose and current authorised scope. Optional analytics, diagnostics, personalisation, AI context and convenience features MUST NOT enlarge the mandatory data footprint of the core product without a separate valid basis and user/admin control where required.

### V2-B0117 · v2 lines 392–392

Privacy-protective defaults MUST be the ordinary out-of-box state. Settings that materially broaden sharing, retention, discoverability, telemetry, cloud processing, contactability or third-party disclosure MUST be explicit and understandable. Consent or approval surfaces MUST NOT use dark patterns, misleading symmetry, preselected optional consent or unnecessarily difficult withdrawal.

### V2-B0118 · v2 lines 395–395

High-risk processing MUST undergo an appropriate documented privacy/risk assessment before release and again after material architectural or purpose changes. The assessment MUST address foreseeable misuse, unauthorised disclosure, inference risk, vulnerable users, children where relevant, cross-border processing, AI/model exposure, account compromise, metadata leakage and recovery/incident handling.

### V2-B0119 · v2 lines 398–398

Encryption MUST be applied in transit and at rest for sensitive personal information unless a documented technical reason makes a specific layer impossible. Where the product promises end-to-end encryption, only intended endpoint participants may possess the content-decryption capability under the declared trust model. A server-side copy of a universal decryption key, silent escrow path or moderation backdoor is incompatible with an E2EE claim unless the user-facing product explicitly describes a materially different encryption model.

### V2-B0120 · v2 lines 401–401

E2EE protects content but does not automatically hide all metadata. 9to1 MUST separately minimise and protect metadata such as participant identifiers, relationship graphs, IP/network information, timestamps, device identifiers, routing information and notification data. User interfaces and documentation MUST NOT imply that metadata is cryptographically hidden when the transport/service necessarily observes it.

### V2-B0121 · v2 lines 404–404

Local processing is preferred for sensitive transformations when it can satisfy the feature reliably. A local application being able to decrypt or access data does NOT automatically grant Dulche, a Plugin, a cloud model, telemetry or another subsystem access to that data. AI access is a separate capability decision subject to the normal permission/privacy model.

### V2-B0122 · v2 lines 407–407

When a feature can use either a local or cloud model/service, the privacy boundary MUST be explicit. Private content MUST NOT leave the device merely because a cloud provider is configured as a model fallback. Uploading personal content to a cloud model/service requires an applicable authorised route, and the UI MUST make materially relevant remote processing understandable to the user.

### V2-B0123 · v2 lines 410–410

Secrets, authentication tokens, cryptographic keys and recovery material MUST use dedicated secure storage and least-privilege access. Logs, crash reports, analytics and Action Graph/FullLog data MUST redact secrets and unnecessary personal content rather than creating secondary plaintext stores.

### V2-B0124 · v2 lines 413–413

Retention MUST be purpose-bound and enforceable. Personal data MUST have a defined deletion/expiry policy or a documented reason for durable retention. Deletion MUST propagate to replicas, caches, indexes and derived stores according to their declared lifecycle rather than only hiding the primary UI record. Legally required preservation or an explicitly created safety/evidence record MUST be segregated from ordinary user-visible retention and governed by its own scope and expiry rules.

### V2-B0125 · v2 lines 416–416

Users MUST receive the applicable rights and controls required by the active LegalPrivacyProfile, including access/export, correction, deletion, objection/restriction and consent withdrawal where applicable. These controls SHOULD be easier and more complete than the legal minimum when technically reasonable. Export MUST use interoperable formats where practical and MUST NOT require the user to manually reconstruct data from screenshots or unrelated pages.

### V2-B0126 · v2 lines 419–419

9to1 MUST maintain an inspectable data-flow inventory and processor/subprocessor register for hosted services. Third parties receive only the scopes/data required for their declared role. A provider's terms permitting additional processing do not by themselves authorise 9to1 to send data for that additional purpose.

### V2-B0127 · v2 lines 422–422

Telemetry and product analytics MUST be purpose-limited, minimised and separated from message/document content unless that content is genuinely required for a specifically disclosed diagnostic purpose. Sensitive content MUST NOT be collected merely to improve general analytics. Privacy-preserving aggregation/pseudonymisation SHOULD be used where it meaningfully reduces risk.

### V2-B0128 · v2 lines 425–425

Children and other protected/vulnerable-user contexts MUST receive any higher protection required by the applicable LegalPrivacyProfile. Age-related protections MUST NOT be implemented by collecting disproportionate additional identity data when a less invasive compliant mechanism is available.

### V2-B0129 · v2 lines 428–428

Security/privacy failures MUST fail closed where continuing would expose personal content or silently weaken a promised protection. If an encryption, permission, privacy-relay or policy control required for a sensitive operation is unavailable, the operation MUST stop or visibly degrade to a clearly described lower-protection mode only when the user is allowed to choose that mode.

### V2-B0130 · v2 lines 431–431

No app-specific specification may silently weaken this baseline. An app MAY impose stricter privacy rules. Any exception to the Privacy Ceiling MUST identify the exact requirement, jurisdiction/product scope, necessity, user impact, data affected, compensating controls, approval authority and review/expiry date.

### V2-B0131 · v2 lines 434–434

Privacy acceptance MUST include automated and manual tests for least-privilege access, unauthorised cross-app data access, cloud/local boundary enforcement, retention/deletion propagation, export correctness, consent/withdrawal, encryption-state truthfulness, metadata minimisation, log redaction, provider failure and jurisdiction-profile changes. A product is not privacy-complete merely because its privacy policy text exists.

### COMPANION-B0085 · companion lines 560–561

SPUI-195 — UCML APP PERMISSIONS, OUTPUT CONTROL, AND PROMPT-INJECTION DEFENCE
Home/Dulche must provide a USER-VISIBLE PER-APP permission screen for context access, including consent to specific context categories/purposes, allow/ask/deny where relevant, scopes, current grants, expiry, revoke and audited access, with default denial for unauthorised apps or newly requested privileged scopes. Plugins/MCP/tools and third-party apps cannot automatically read the full user context simply because connected, installed or named in a prompt. Shared/team context must be specifically authorised, and temporary chats/closed-source corpus restrictions are upheld. Before an action that transfers sensitive context to ANY app/tool/provider, the broker validates the actual TARGET, requested data categories, user intent, action necessity, authorised recipient and potential exfiltration; block unrelated or disguised arbitrary data requests, including tool-result/prompt-injection instructions, at CODE-ENFORCED BOUNDARIES with model-level recognition as a secondary layer. Do not claim a model can guarantee detection of every cleverly phrased malicious request. Data minimisation, selective retrieval, signed/audited access, typed outbound payload controls, consent and independent policy checks are primary enforcement. Stream/output secret-pattern redaction remains OPTIONAL DEFENCE IN DEPTH, never a substitute for not exposing keys. No unauthorised automatic model-to-app data transfer for convenience. Provide user-inspectable provenance, access/revocation and applicable delete/retention controls compatible with Privacy Ceiling.

### COMPANION-B0086 · companion lines 564–565

SPUI-196 — DULCHE BUILT-IN SAFETY BLOCK: CONTEXTUAL LOW-OVERHEAD CHECKS
Build an efficient, integrated safety layer for Dulche that does not normally stall harmless generation. A lightweight CPU-friendly streaming detector MAY flag risk signals/patterns, but simple blacklisted WORDS, WORD PAIRS or short topic lists are NOT sole evidence of policy violation. Words with common benign legal, educational, historical, fictional or technical uses must receive CONTEXTUAL classification rather than automatic refusal. Use layered policy: concise consistent baseline guidance to models (not a pretend exhaustive few-hundred-token “security wall”); streamed output checks; contextual risk assessment; independent pre-execution tool-call/app/permission/scope checks; and where uncertainty or high risk warrants, isolated peer-model or authorised human review. Assessment should consider user intent, whether a request is actually prohibited, severity, legitimate educational/helpful exceptions, and the true capabilities of the chosen model/tool. A reviewer may run asynchronously AFTER low-risk material where safe; genuinely high-risk material/tool actions can be HELD before output or execution. Display “Checking Compliance” only when an actual review/pause occurs, with truthful state rather than cosmetic delay. Response latency should be optimised and measured rather than promising zero overhead or relying exclusively on text scanning. No arbitrary cloud disclosure of private content to a review provider outside current permission/locality policy.

### COMPANION-B0087 · companion lines 568–569

SPUI-197 — PROPORTIONATE SAFETY ENFORCEMENT, REVIEW AND MEMORY INTEGRITY
Where a response is actually adjudicated disallowed, use a clear policy-appropriate refusal or unavailable-content state (“Sorry - I can't respond to this request” may be used when accurate); do not replace legitimate educational/safety/helpful content simply because of a keyword. Pattern hits, two flagged messages, or counts of reviewer suspicions are NOT by themselves grounds to irreversibly lock a conversation, prevent all branching, or destroy memory. Escalation is based on evidenced severity/repetition and contextual independent review; provide proportionate scoped restrictions, reason/status, correction or appeal/review where applicable, and reversible error handling. A serious substantiated violation can trigger genuine action restrictions under the platform policy, but no fixed “two strikes locks everything” rule or automatic permanent lock based solely on three model votes. When content legitimately must become unavailable, make rendering/export and related references enforce the adjudicated visibility decision with proper provenance and necessary audit retention; do not pretend historic physical-side effects were undone. Memory derived solely from conclusively invalid/deleted user data may be reviewed for targeted invalidation under canonical Memory provenance and retention rules, but no broad automatic erasure of unrelated/valid memories or silent history destruction. Preserve independent permission/safety guards, inspectable decisions, correct state across branches/restarts, and false-positive recovery.

### COMPANION-B0088 · companion lines 572–578

SPUI-198 — HOME USER CUSTOM BLACKLISTS AND OUTPUT-STYLE FILTERS
Home settings MUST support user-configurable:
• TOPIC BLACKLIST / TOPIC AVOIDANCE preferences: model is instructed to avoid discussing selected topic areas where possible and appropriate to the user's intent, without overriding mandatory safety/helpful information, security policy or law;
• WORD/PHRASE BLACKLIST: user-specified terms/phrases detected in eligible generated prose via efficient local matching (normalisation, safe word boundaries and user-previewable match semantics where appropriate), requesting safe contextual rewrites or bounded transformation; not false guarantees of semantic detection;
• “Remove EM Dashes”: replaces model-authored em dashes with normal hyphens in eligible prose;
• “Remove Emojis”: removes emojis from model-authored prose where appropriate.
These controls must be real versioned Home-owned account preferences, ideally with supported explicitly selected per-Space overrides, and persist/recover/synchronise within normal permissions. Preserve ordinary user messages, quoted source passages, code, LaTeX, citations, external/file content, 9to1 UI/status icons and authentic document artifacts unless the user explicitly chooses a controlled transformation of that specific content. Never silently alter a programmer's literal text, source evidence or shared canonical file just because the output-style option is active. Distinguish display/output reformats from true source edits, respect Accessibility and CUI theme, and give users inspectable settings and practical exception/disable options. Lightweight local filtering SHOULD avoid unnecessary extra model turns; where a meaning-preserving rewrite is not safe, explain or mark an exception instead of corrupting data. Blacklists do not create a bypass around Dulche safety, higher-tier policy or essential user-support responses.
