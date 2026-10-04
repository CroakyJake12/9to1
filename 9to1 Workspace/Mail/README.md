# 9to1 Mail

Mail owns provider-neutral account, message, thread, draft, outgoing-message, search, and local-operation contracts. The cache is an atomically replaced encrypted state document; its encryption key and provider credentials must be supplied by Home through opaque secure references. Mail never writes credentials or encryption keys to its state document.

`HavenOS.Mail.csproj` targets .NET 10 and pins MailKit 4.18.0 for the generic IMAP/SMTP protocol adapter and MimeKit-backed message parsing. MailKit is used as a protocol library, not as a donor UI. The current adapter fetches the generic IMAP inbox by UID cursor and sends through SMTP; Gmail/Google native-label behaviors, Microsoft Graph behaviors, full folder sync, server change-token reconciliation, and an OAuth account-consent UI are not yet integrated.

The CUI workspace is authored in `UI/MailWorkspace.cui`. It defines Mail-specific typed intents and semantic state; a native app host must register those intents and connect them to `MailDomainService` through Home's permission broker. Until that host/key-service wiring is supplied, no provider send is allowed and encrypted-cache startup fails closed if Home cannot supply the key.

## Donor parity

See `DONOR-PARITY.md` for the Thunderbird capability mapping and the revision pin blocker. This implementation does not copy Thunderbird source or UI.

## Validation

Mail-owned tests cover encrypted cache persistence/key refusal, revision compare-and-swap, local search, draft conflicts, idempotent offline send queueing, and the client-side undo window. Provider integration against live Google, Microsoft, and generic IMAP/SMTP servers, native UI/visual QA, Home API/notification/OAuth/key integration, and platform package validation remain unverified.
