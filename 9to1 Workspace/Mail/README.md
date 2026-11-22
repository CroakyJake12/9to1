# Mail in Connect

Mail is Connect’s email surface and a compatible route/package alias. Use the provider-neutral account, folder, message, thread, draft and outgoing-operation contracts here. The active account and permissions determine which operation may execute.

The encrypted local cache, provider acknowledgements and draft lifecycle have different responsibilities. Keep an unsent draft recoverable across closing the view. Do not mark a send, move or remote deletion complete until the provider confirms the original operation. Exercise offline recovery and duplicate submission as well as the normal route.

## Donor parity

See `DONOR-PARITY.md` for the Thunderbird capability mapping and the revision pin blocker. This implementation does not copy Thunderbird source or UI.

## Validation

Mail-owned tests cover encrypted cache persistence/key refusal, revision compare-and-swap, local search, draft conflicts, idempotent offline send queueing, and the client-side undo window. Provider integration against live Google, Microsoft, and generic IMAP/SMTP servers, native UI/visual QA, Home API/notification/OAuth/key integration, and platform package validation remain unverified.

[Account and message contracts](Contracts) · [Cache](Storage) · [Providers](Providers) · [Tests](Tests) · [Connect and Mail](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
