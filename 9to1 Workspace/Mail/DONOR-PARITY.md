# Mail donor-parity manifest

**Preferred technical donor:** Mozilla Thunderbird (`mozilla/releases-comm-central`, `comm-release`).

**Revision status:** OPEN — exact source commit/tag was not available from the current checkout or external Git transport. A read-only Git source lookup failed with Windows Schannel `SEC_E_NO_CREDENTIALS`; no Thunderbird source was imported. Before claiming donor parity or shipping a derivative, the release owner must pin the exact Thunderbird revision and review that revision's MPL and third-party notices.

| Capability | Mail disposition | Current evidence/state |
|---|---|---|
| IMAP/SMTP, STARTTLS/TLS, cancellable protocol calls | Replace with 9to1-native equivalent | MailKit 4.18.0 adapter uses secure transport options and Home-provided opaque credential resolution; live provider exercise is not run. |
| MIME parsing and composing | Replace with 9to1-native equivalent | MimeKit is brought by MailKit; generic inbox MIME fields and text/HTML bodies are projected. |
| Draft persistence and conflict preservation | Replace with 9to1-native equivalent | Encrypted local state, revisions, and two-version conflict field are implemented and covered by focused tests. |
| Offline operations and queued sending | Replace with 9to1-native equivalent | Durable operation journal and queued-send/undo lifecycle are implemented; replay/reconciliation host is not wired. |
| Folders, labels, threading and provider-specific behavior | Preserve where supported; OPEN for provider completeness | Generic IMAP folders and account-scoped local thread grouping are represented. Gmail labels, Outlook folders/categories and cross-folder sync need provider-specific validation. |
| Search | Replace with 9to1-native equivalent | Local indexed-content scan covers sender, recipients, subject, body, attachment names, date/account/folder/read/star/category filters; smart views persist as queries. |
| Junk/spam | Preserve via provider-aware operation | Junk capability is declared when provided by an adapter, but mark-junk/not-junk operations need full adapter and UI integration. |
| OpenPGP and S/MIME signing/encryption | OPEN | MimeKit supports PGP/MIME and S/MIME, but platform key/certificate stores and user workflows are not wired. |
| Message source, raw MIME, advanced headers | Replace with 9to1-native equivalent | Typed message metadata includes Internet Message-ID and authentication results; raw MIME/header inspection UI is not implemented. |
| Thunderbird chrome, dialogs, pane styling and proprietary AI | Not applicable | Product requirement prohibits donor-native UI and donor AI/subscription features. |

No Thunderbird source code or artwork is included. The exact donor revision and resulting license/notice obligations remain release blockers.
