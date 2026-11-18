# Connect — conversations, contacts and Mail — Platform behaviour; Acceptance requirements (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0743 · v2 lines 3522–3522

Mail MUST return structured failures including AccountNotFound, AuthenticationRequired, ProviderUnavailable, ProviderCapabilityUnsupported, Offline, SyncFailed, MessageNotFound, ThreadNotFound, DraftConflict, SendFailed, SendRejected, AttachmentUnavailable, MailboxQuotaExceeded, RateLimited, PermissionDenied, InvalidRecipient, EncryptionKeyUnavailable, CertificateError and structured partial bulk outcomes in addition to the global error contract.

### V2-B0744 · v2 lines 3525–3525

Platform behaviour

### V2-B0745 · v2 lines 3528–3528

Native Windows/9to1-OS Mail MUST support configured local caching/offline behaviour and Home-integrated notifications/files/model services. Web Mail uses authenticated server/provider APIs and the same canonical account/message identities where available, but browser storage/offline capabilities MUST be capability-negotiated rather than assumed identical to native.

### V2-B0746 · v2 lines 3531–3531

Acceptance requirements

### V2-B0747 · v2 lines 3534–3554

Mail is implementation-complete only when acceptance coverage verifies at least:
* The final UI passes dedicated visual QA for a cohesive modern 9to1 CUI shell with responsive pane behaviour, clean message/compose surfaces, consistent spacing/typography and no exposed legacy donor UI.
* Applicable mature donor email capabilities are accounted for in the donor-parity manifest independently from visual design.
* At least Gmail/Google, Outlook/Microsoft and generic IMAP+SMTP accounts can coexist with Unified Inbox without provider-semantics loss.
* Gmail-style label semantics and generic IMAP folder semantics remain distinguishable and correct.
* Incremental push/IDLE/provider sync updates a connected mailbox without complete rescans for ordinary changes.
* Offline cached mail is readable and queued mutations survive app/device restart then reconcile on reconnect.
* Draft autosave/recovery preserves content and surfaces provider/local conflicts rather than overwriting silently.
* Offline send enters Outbox/Queued and send failure remains visible/retryable rather than being reported Sent.
* Client-side Undo Send cancels before provider submission and stops presenting undo once sending is irreversible.
* Scheduled send accurately identifies provider/cloud/device execution semantics and does not imply guarantees the selected executor cannot provide.
* Attachments can be opened/saved through Files and Attach from Files works with authorised local/9to1 Drive content.
* Search works across the defined fields and saved smart views do not duplicate messages.
* Remote email HTML/script cannot execute trusted app code and remote-image tracking obeys privacy controls.
* OpenPGP/S/MIME donor-supported behaviour works or has an explicit scoped parity exception.
* The AI bar can summarise/draft against explicit context while respecting local/cloud privacy policy.
* AI cannot bypass Mail.Send permission simply because it drafted a message.
* Provider reauthentication/revocation does not erase valid cached messages/drafts.
* Home notification deep links open the exact intended message/thread/account.
* Home/Dulche service restart produces a recoverable/degraded state without mailbox database corruption.
* Core UI operations and provider actions have equivalent typed API coverage.

### V2-B2291 · v2 lines 13415–13415

### UX27-OTHER-003 — Mail reading and conversation navigation

### V2-B2292 · v2 lines 13417–13417

**B/D.** Account/folder navigation, message list and reading pane remain distinct, with unread, selected, flagged, attachment and sync states. Selecting a message displays its real content and identity; loading another cannot flash the previous message under the new sender/title. Thread expand/collapse preserves individual message provenance and accessible navigation.

### V2-B2293 · v2 lines 13419–13419

Remote images and active content follow privacy/security settings. A blocked resource explains its state without falsely claiming the message is empty. Search identifies the actual account/folder scope and returns accessible messages, not generated summaries presented as mailbox records.

### V2-B2294 · v2 lines 13421–13421

### UX27-OTHER-004 — Mail compose, attachments and send

### V2-B2295 · v2 lines 13423–13423

**B/D.** Compose retains recipients, subject, rich/plain content, attachments, sender identity and draft revision. Recipient chips resolve actual addresses and expose ambiguity rather than guessing. Attachment upload/availability is visible; remove acts on the selected attachment only. Send validates required fields and makes the actual sender/destination clear.

### V2-B2296 · v2 lines 13425–13425

Outbox, provider submission, sent-folder confirmation, scheduled send and Undo Send are distinct states with real supported semantics. A timeout cannot be blindly resent under a new identity. AI drafting is not implicit permission to send. Closing compose preserves a recoverable draft unless the user deliberately discards it.
