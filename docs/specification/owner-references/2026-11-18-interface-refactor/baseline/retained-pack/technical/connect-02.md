# Connect — conversations, contacts and Mail — Connect API and events; Failure and recovery (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0660 · v2 lines 3236–3246

Connect API and events
Connect MUST expose typed APIs for conversations/messages, organisations/memberships/roles, contacts/privacy controls, calls, SMS/RCS routing where supported, reporting, safety state and user-authorised actions. Email-specific operations remain under the canonical 9to1.Mail API and are reachable through Connect without duplicating them.
Minimum Connect API families MUST include conversation list/get/create/update/membership, message send/edit/delete/react/thread, contact/block/mute/settings, organisation/membership/role, call start/join/leave/media-state/screen-share/record-request/record-state, report/create/status/appeal, and change/event subscriptions. Every consequential action MUST use stable IDs, structured results, revision/conflict semantics and the global permission/audit contract.
Trust & Safety internal APIs MUST be separately permissioned from ordinary client APIs. Direct user clients MUST NOT receive privileged moderation-model prompt access or arbitrary enforcement mutation authority.
Failure and recovery
Connect MUST provide structured failures including ConversationNotFound, MembershipDenied, EncryptionStateUnavailable, DeviceNotTrusted, KeyRecoveryRequired, TransportUnavailable, SMSFallbackRequired, CallUnavailable, MediaPermissionDenied, RecordingConsentRequired, RecordingConsentDeclined, ParticipantConsentPending, ReportNotFound, EvidenceUnavailable, EnforcementDenied, AppealUnavailable, BanEvasionReviewRequired and ProviderCapabilityUnsupported in addition to global errors.
Network interruption MUST preserve unsent messages/call state truthfully, reconcile delivered/read state after reconnect and avoid duplicate sends. Encryption/session recovery MUST NOT silently reset conversation security or create a new unverified device identity without disclosure.
Acceptance requirements
Connect is implementation-complete only when acceptance coverage verifies at least: native one-to-one and group chat with E2EE; multi-device key/device trust and recovery; threads/reactions/typing/read-state/presence controls; organisation/team hierarchy; complete Thunderbird-derived Mail mode; SMS/RCS transport-state truthfulness; mature one-to-one/group voice/video calls; screen sharing and standard call controls; local media effects where applicable; unanimous recording consent including future joiners; distinct recording/transcription/AI permissions; block/mute/contact privacy controls; disappearing messages; participant-initiated encrypted-chat reporting without universal server decryption; structured AI-assisted report triage without user prompt access to the moderation model; proportionate restrictions; severe-only Connect-wide bans; appeals; privacy-preserving ban-evasion detection; and enforcement of the platform-wide Privacy Ceiling.
Mail
Product role and donor strategy

### V2-B0661 · v2 lines 3249–3249

9-1 Mail is the native CUI email client for connected accounts. It MUST be a fully capable manual email client with AI disabled or unavailable. AI is additive: it may summarise, search, draft, rewrite and triage, but core reading, composition, sending, organisation, search, offline use and account management MUST remain directly usable without AI.

### V2-B0662 · v2 lines 3252–3252

Thunderbird is the preferred mature technical donor/reference for email protocol and compatibility behaviour, including IMAP, SMTP, OAuth, MIME parsing, attachments, offline mail, drafts, folders, threading, search foundations, junk/spam handling, certificates and mature email edge cases. Donor reuse MUST comply with the selected source version and licensing.

### V2-B0663 · v2 lines 3255–3255

Donor parity applies to mature email capabilities and behaviours, NOT to Thunderbird’s visual shell. Legacy donor chrome, menus, dialogs, pane styling or dated interaction patterns MUST NOT be reproduced merely for parity. The final product MUST be a modern, clean, native CUI Mail interface designed for 9to1.

### V2-B0664 · v2 lines 3258–3258

A Mail donor-parity manifest MUST identify the selected donor revision and classify applicable capabilities as Preserve, Replace with 9to1-native equivalent or Not applicable. Proprietary AI/subscription services from any donor/provider are not copied.

### V2-B0665 · v2 lines 3261–3261

Modern CUI shell and responsive layout

### V2-B0666 · v2 lines 3264–3264

The default desktop layout MUST use a clean three-pane structure: navigation/account sidebar, message/conversation list and reading pane. Visual separation MUST be subtle and CUI-native rather than relying on heavy boxes, legacy toolbars or nested window chrome. Message content should receive the majority of visual emphasis.

### V2-B0667 · v2 lines 3267–3267

Users MUST be able to select reading-pane right, reading-pane bottom or reading-pane off, plus supported density options. Smaller windows MUST responsively collapse the layout into two-pane or single-pane navigation without squeezing unreadable columns.

### V2-B0668 · v2 lines 3270–3270

The message list MUST prioritise sender/avatar or initials, subject, concise preview, timestamp, unread/important state and attachment/category indicators. Low-frequency controls MAY appear on hover/selection while common quick actions such as archive, delete, star and snooze remain directly discoverable.

### V2-B0669 · v2 lines 3273–3273

The reading pane MUST present a compact sender/recipient/date header, clear reply/reply-all/forward/other actions and an uncluttered message body. Long conversations MUST support collapsing older messages/quoted sections without losing access to full content.

### V2-B0670 · v2 lines 3276–3276

The final client MUST not expose donor-native UI surfaces. Settings, account management, compose, security prompts and advanced inspection must all use 9to1 CUI unless an OS-level credential/security dialog is explicitly required.

### V2-B0671 · v2 lines 3279–3279

Sidebar and navigation

### V2-B0672 · v2 lines 3282–3282

Mail MUST provide a unified top-level navigation including Unified Inbox, Starred/Favourites, Snoozed, Drafts, Sent, Scheduled, Outbox, Archive, Junk and Trash where the effective connected-account capabilities permit them. Individual accounts appear below with collapsible provider-native folders/labels.

### V2-B0673 · v2 lines 3285–3285

Unified views are query/projection surfaces over canonical provider/account messages and MUST NOT duplicate messages into a separate mailbox. Actions taken from a unified view route to the owning AccountID/provider semantics.

### V2-B0674 · v2 lines 3288–3288

Provider-native structures MUST be preserved. Gmail-style labels, Outlook categories/folders and generic IMAP folders MUST NOT be flattened into one simplistic hierarchy when that would break provider behaviour.

### V2-B0675 · v2 lines 3291–3291

Account and provider model

### V2-B0676 · v2 lines 3294–3294

Every connected mailbox MUST have stable AccountID identity independent of mutable email address/display name. MailAccount MUST store at least provider type, provider account identity, primary address/display name, supported capability set, authentication reference, sync configuration, signature configuration and sending configuration.

### V2-B0677 · v2 lines 3297–3297

Mail MUST support generic IMAP + SMTP as a baseline and provider-native adapters where beneficial, including Gmail/Google and Outlook/Microsoft. Provider adapters MAY expose richer push, label, thread, archive, snooze, scheduled-send or search behaviours while remaining behind the common Mail contract.

### V2-B0678 · v2 lines 3300–3300

Provider capabilities MUST be discovered/declared explicitly, including labels vs folders, archive, snooze, scheduled send, native search, push/change feed, undo send, aliases/from identities, read receipts, encryption/security features and provider-specific actions. Unsupported actions MUST be hidden/disabled or return ProviderCapabilityUnsupported rather than appearing functional.

### V2-B0679 · v2 lines 3303–3303

OAuth 2.0 is preferred when supported. Credentials/tokens MUST be stored through secure credential references. Token refresh, revoked access, reauthentication, app-password/provider-password flows and certificate errors MUST be handled without deleting valid local cached mail.

### V2-B0680 · v2 lines 3306–3306

Message, thread and folder identity

### V2-B0681 · v2 lines 3309–3309

Mail MUST maintain stable local canonical identities for AccountID-scoped messages and threads. Message records MUST include MessageID, AccountID, provider-native message ID, Internet Message-ID where present, ThreadID/provider conversation identity where applicable, folder/label state, timestamps, sender/recipients, flags and provider change/revision token where available.

### V2-B0682 · v2 lines 3312–3312

Internet Message-ID MUST NOT be assumed globally unique enough to serve as the sole database identity. Provider-native IDs are authoritative only within their AccountID/provider scope.

### V2-B0683 · v2 lines 3315–3315

Threads/conversations preserve ordered references to individual messages, participants, subject/summary metadata, unread count and latest-message state. Provider-native thread identity MUST be preserved when available. Generic IMAP MAY use a deterministic local threading model, but Mail MUST distinguish local grouping from provider-native conversation identity.

### V2-B0684 · v2 lines 3318–3318

Mail MUST NOT automatically merge threads across separate accounts solely because subjects or Message-ID relationships appear similar.

### V2-B0685 · v2 lines 3321–3321

Message rendering and privacy

### V2-B0686 · v2 lines 3324–3324

Mail MUST safely render plain-text and HTML email, inline images, quoted replies, signatures and attachments. HTML MUST be sanitised; arbitrary message script/executable application code MUST NOT execute in the trusted Mail surface.

### V2-B0687 · v2 lines 3327–3327

Remote images/external tracking resources MUST be blocked, privacy-proxied or otherwise privacy-controlled by default according to Mail privacy settings. The UI MUST provide a clear per-message/per-sender Load remote content/trust action rather than silently leaking message opens.

### V2-B0688 · v2 lines 3330–3330

Mail MAY offer tracking-protection features such as known tracking-pixel blocking or privacy-preserving remote-image proxying, but protection state MUST be visible and breakage must be recoverable by the user.

### V2-B0689 · v2 lines 3333–3333

Compose and drafts

### V2-B0690 · v2 lines 3336–3336

Compose MUST use a clean CUI editor that can operate as a focused pane/window and provides From identity/account, To, Cc, Bcc, subject, rich-text body, plain-text mode, attachments, inline images, signatures, reply/reply-all/forward and provider-supported message options. Recipient fields SHOULD progressively disclose Cc/Bcc rather than permanently consuming space.

### V2-B0691 · v2 lines 3339–3339

The composer SHOULD use Home’s Shared Productivity Engine for supported rich-text primitives, while exporting through an email-compatible formatting pipeline that produces valid email-safe HTML plus a plain-text alternative. Unsupported rich shared objects MUST be explicitly converted/attached rather than silently mangled.

### V2-B0692 · v2 lines 3342–3342

Drafts MUST autosave and survive app/process restart. Local/provider draft conflicts MUST preserve both revisions or surface a conflict rather than silently overwriting newer user text.

### V2-B0693 · v2 lines 3345–3345

FromIdentity MUST be separate from AccountID so provider-supported aliases can define address, display name, Reply-To and signature independently.

### V2-B0694 · v2 lines 3348–3348

Attachments and Files integration

### V2-B0695 · v2 lines 3351–3351

Attachments MUST expose stable AttachmentID scoped to MessageID plus filename, MIME type, size, Content-ID where relevant, download/cache state and security state. Attachment bytes MAY hydrate on demand according to offline/cache policy.

### V2-B0696 · v2 lines 3354–3354

Attachment actions MUST integrate with Files/Home handlers, including Open, Save, Save to 9to1 Drive, Save all, Add to Board and Open in compatible owning 9to1 app where applicable. Attachment saving creates a normal Files item and MUST not alter the original message.

### V2-B0697 · v2 lines 3357–3357

Compose MUST support Attach from Files across local files, 9to1 Drive and authorised connected providers. A future/share-link mode MAY send a hosted Files link instead of attaching bytes, but it MUST be an explicit access-semantic change rather than an invisible optimisation.

### V2-B0698 · v2 lines 3360–3360

Sync, push and offline behaviour

### V2-B0699 · v2 lines 3363–3363

Mail sync MUST use the fastest reliable provider mechanism available: provider push/change streams, webhooks or IMAP IDLE/incremental change tokens first; periodic polling/full mailbox scans only as fallback/recovery. Every account maintains durable sync/change cursor state and pending local operation state.

### V2-B0700 · v2 lines 3366–3366

Mail MUST provide Sync all, Sync this account, Sync this folder, Retry failed sync, Pause sync and Resume sync controls plus clear sync diagnostics including last successful contact, pending operations and errors.

### V2-B0701 · v2 lines 3369–3369

Offline use MUST support configurable caching of headers, recent/full message bodies and attachments on demand, plus selected folders/messages available offline. Cached mail remains readable without network.

### V2-B0702 · v2 lines 3372–3372

Offline-safe mutations including read/unread, star, label/category, move, archive, delete/restore, draft updates and queued sends MUST enter a durable operation journal and reconcile on reconnect using provider change tokens/revisions. Blind timestamp-based last-write-wins is prohibited for conflicts.

### V2-B0703 · v2 lines 3375–3375

Send pipeline, Outbox and Undo Send

### V2-B0704 · v2 lines 3378–3378

Outgoing messages MUST have explicit lifecycle states including Draft, Queued, Sending, Sent, Failed and Cancelled. An offline Send enters Outbox/Queued and MUST NOT be represented as Sent until the provider accepts the message.

### V2-B0705 · v2 lines 3381–3381

Mail MUST support client-side Undo Send through a configurable local hold delay before provider submission, even when a provider has no native undo. During the hold window the user can cancel. After irreversible provider submission, Mail MUST NOT claim that the send remains undoable unless the provider genuinely supports cancellation.

### V2-B0706 · v2 lines 3384–3384

Send failures MUST remain visible/retryable in Outbox with the actual provider/transport error and must not silently discard the draft/message.

### V2-B0707 · v2 lines 3387–3387

Scheduled send

### V2-B0708 · v2 lines 3390–3390

Scheduled send MUST use provider-native scheduling when available. Otherwise Mail may use a reliable 9to1 server/cloud scheduler or device-local scheduler only when its execution guarantees are accurately represented.

### V2-B0709 · v2 lines 3393–3393

The UI MUST identify execution location/guarantee as Provider scheduled, 9to1 cloud scheduled or This device scheduled. Device-local schedules MUST clearly state that execution depends on the device/runtime being available; they MUST NOT masquerade as guaranteed server scheduling.

### V2-B0710 · v2 lines 3396–3396

Search and smart views

### V2-B0711 · v2 lines 3399–3399

Mail search MUST support sender, recipient, subject, message body where indexed/available, attachment name, date range, account, folder/label, read/unread, attachment presence, categories/labels and starred state. Provider-native search MAY be used but cached/offline content MUST also support local indexed search where policy permits.

### V2-B0712 · v2 lines 3402–3402

Users MUST be able to save supported queries as smart views/folders. Smart views are dynamic queries over canonical messages and MUST NOT duplicate messages.

### V2-B0713 · v2 lines 3405–3405

Rules and automation integration

### V2-B0714 · v2 lines 3408–3408

Mail MUST support convenient message rules/filters for provider/local-supported triggers such as sender, recipient, subject and message metadata with actions such as move, label/category, mark read, star, notify or hand off to Automations.

### V2-B0715 · v2 lines 3411–3411

Cross-app consequential workflows MUST be represented through Automations/typed app APIs rather than hidden arbitrary scripts inside Mail rules. Rule execution MUST be auditable and idempotent where replay is possible.

### V2-B0716 · v2 lines 3414–3414

Snooze, archive and delete semantics

### V2-B0717 · v2 lines 3417–3417

Snooze MUST hide/deprioritise a message/thread until a specified time and restore it to the appropriate attention view. Provider-native snooze is preferred where available; a 9to1 metadata-layer implementation MAY be used only if it preserves provider consistency.

### V2-B0718 · v2 lines 3420–3420

Archive and Delete MUST be provider-aware. Archive may mean removing an Inbox label or moving to a configured Archive folder depending on provider. Delete MUST distinguish remove-current-label/folder, move to Trash and permanent delete where the provider exposes those concepts. Permanent deletion is a high-risk explicit action.

### V2-B0719 · v2 lines 3423–3423

Signatures, aliases and contacts

### V2-B0720 · v2 lines 3426–3426

Mail MUST support per-account and per-FromIdentity signatures with rich-text plus plain-text fallback. Signature selection may be automatic per identity but remains user-editable in the composer.

### V2-B0721 · v2 lines 3429–3429

Recipient autocomplete SHOULD use authorised shared Contacts/people services and provider contacts where configured, with local recipient history as fallback. Mail MUST NOT make its message database the canonical contacts store.

### V2-B0722 · v2 lines 3432–3432

Notifications

### V2-B0723 · v2 lines 3435–3435

Mail MUST publish typed notifications through Home Notifications for events such as new important mail, send failure, authentication required and significant sync error. Notifications MUST deep-link to the exact AccountID/ThreadID/MessageID or relevant settings surface.

### V2-B0724 · v2 lines 3438–3438

Mail MUST NOT implement a second incompatible notification centre; Home owns notification history/preferences/OS bridging.

### V2-B0725 · v2 lines 3441–3441

Persistent AI bar and AI action boundary

### V2-B0726 · v2 lines 3444–3444

Mail MUST host the global persistent bottom AI bar. Its context snapshot may include AccountID, current folder/view, selected MessageIDs/ThreadIDs, DraftID and selected recipients/content according to current permissions.

### V2-B0727 · v2 lines 3447–3447

Supported AI experiences MAY include summarise thread, identify action items, draft/rewrite a reply, conversational mailbox search, triage suggestions and creation of typed cross-app actions such as Planner items or saving attachments.

### V2-B0728 · v2 lines 3450–3450

Ordinary conversational AI MUST NOT silently send email merely because it generated a draft. Sending remains a consequential typed Mail action subject to the Home permission/risk model. An explicitly authorised Agent/Automation MAY send autonomously only when its effective permission/trust scope permits Mail.Send.

### V2-B0729 · v2 lines 3453–3453

Mail content provided to Dulche MUST obey local/cloud privacy policy. Selecting a mailbox/thread MUST NOT implicitly upload the whole mailbox to a cloud model. Context scope such as selected messages, current thread or explicit mailbox search MUST be represented and permission-filtered.

### V2-B0730 · v2 lines 3456–3456

Security, phishing and remote-content safety

### V2-B0731 · v2 lines 3459–3459

Mail MUST provide conventional safety signals without claiming infallible detection, including clear actual sender address vs display name, suspicious-link/attachment warnings, executable attachment restrictions and provider/header authentication information such as SPF/DKIM/DMARC results where available.

### V2-B0732 · v2 lines 3462–3462

Read receipts MUST require user/provider policy approval and MUST NOT be automatically sent solely because a sender requested one.

### V2-B0733 · v2 lines 3465–3465

Provider spam/junk systems SHOULD be used when available; generic accounts may use local classification/rules. Mark Junk/Not Junk MUST update provider state when supported.

### V2-B0734 · v2 lines 3468–3468

OpenPGP, S/MIME and advanced mail features

### V2-B0735 · v2 lines 3471–3471

Where supported by the selected mature donor source and target platform, Mail MUST preserve applicable OpenPGP and S/MIME signing/encryption workflows, certificate/key management and message verification/decryption behaviour unless a documented parity exception exists.

### V2-B0736 · v2 lines 3474–3474

Advanced message inspection MUST expose message source/raw MIME, headers, Internet Message-ID and provider/debug details without making these advanced controls dominate the default reading experience.

### V2-B0737 · v2 lines 3477–3477

Print and export

### V2-B0738 · v2 lines 3480–3480

Messages/threads MUST support print and supported export such as PDF, .eml and applicable text/HTML forms. Export MUST preserve attachments only when the selected format/workflow explicitly supports them.

### V2-B0739 · v2 lines 3483–3483

Mail API

### V2-B0740 · v2 lines 3486–3513

9to1.Mail.ListAccounts();
9to1.Mail.GetAccount(accountID);
9to1.Mail.Sync(accountID?);
9to1.Mail.SyncFolder(accountID, folderID);
9to1.Mail.ListFolders(accountID);
9to1.Mail.ListMessages(query);
9to1.Mail.GetMessage(messageID);
9to1.Mail.GetThread(threadID);
9to1.Mail.MarkRead(messageID, value);
9to1.Mail.Star(messageID, value);
9to1.Mail.Move(messageID, destination);
9to1.Mail.Archive(messageID);
9to1.Mail.Delete(messageID);
9to1.Mail.Restore(messageID);
9to1.Mail.Draft.Create(config);
9to1.Mail.Draft.Update(draftID, patch, expectedRevision?);
9to1.Mail.Draft.Delete(draftID);
9to1.Mail.Send(draftID);
9to1.Mail.ScheduleSend(draftID, time, executionPolicy?);
9to1.Mail.CancelQueued(messageID);
9to1.Mail.Reply(messageID, config?);
9to1.Mail.Forward(messageID, config?);
9to1.Mail.Attachment.Get(attachmentID);
9to1.Mail.Attachment.SaveToFiles(attachmentID, destination);
9to1.Mail.Search(query);
9to1.Mail.Rules.List();
9to1.Mail.Rules.Create(rule);
9to1.Mail.Changes.Subscribe(accountID?, cursor?);

### V2-B0741 · v2 lines 3516–3516

These are minimum canonical entry points. Every additional user-visible Mail operation and preserved donor capability MUST have an equivalent typed API/domain action under the global API-parity rule.

### V2-B0742 · v2 lines 3519–3519

Failure behaviour
