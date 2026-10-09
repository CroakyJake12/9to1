Write browser adapter
=====================

`WriteBrowserSession` consumes the existing `WriteDocumentEditor`, an authorised
`INotesRepository`, and the existing `IWriteNativeDocumentPackageStore`. It owns
presentation state and document lifecycle, never a second document model or
backend. Local CUI commands dispatch to the same typed session operations.
The composition root must supply repository access appropriate to its actor.
The session's explicit read-only flag restricts presentation; it is not an ACL
authority.

The focused executable suite links unchanged production `NotesRepository`,
`AppPaths`, `ProductionDiagnostics` and `WriteNativeDocumentPackageStore`.
Run `Tests/WriteBrowser.Persistence.Tests.csproj` with `seed` and then `verify`
in two processes, using one fresh explicitly configured `HAVEN_DATA_DIR`.
The suite validates the owning durable local backend and editor adapter. It
does not prove browser rendering, browser durable storage or deployed parity.

The source repository serializes revisions for one configured root within a
process. Sequential stale revisions are rejected by its actual compare-and-save.
Cross-process concurrent writer locking and actor ACL enforcement require the
owning backend contract. Hosted/browser transports must use that owner and must
not expose local filesystem paths or emulate storage with a WASM filesystem.

Native import is an explicit independent-copy operation consistent with existing
WritePage: it preserves structured content but assigns a new document identity
and saves it through the repository. Export saves canonical current first and
uses the real package codec. Conflict drafts remain open; a host must provide
recovery/resolution before disposing a dirty session.

`WriteBrowserFeature` registers the device-local `app.write` route. Its constructor
accepts the repository, native package store, reduced-motion callback and optional
uncertain-commit classifier supplied by the root. The initial route lists actual
repository documents without creating a default artifact; the picker opens the
original `NotesDocumentSummary.Id`. The feature owns an asynchronous session
lifetime and implements the root's `IBrowserCloseParticipant`. The root must await
successful preparation before asynchronous full route removal. Registry methods
alone do not establish native/browser close integration; that remains a root gate.

An uncertain save acknowledgment preserves the exact submitted real document and
its prior owner revision. It freezes authoring and leaving/retry commands. The
explicit saved-state inspection reads the same canonical ID through the repository;
an exact content match at the next stored revision acknowledges that commit.
Inspection of an unchanged base permits a subsequent explicit Save of the same
draft/ID. A different revision/content or recovery copy remains blocked. Only
durable publication fields (version, update timestamp, recovery metadata) are
excluded from the authored-content comparison. The browser storage module must
settle prior uncertain same-module mutations before providing these inspection
reads. This mechanism does not create a competing revision or Files identity.

The optional `IWriteBrowserPackageBroker` enables native package chooser/download
buttons. The browser broker copies exact binary bytes into a unique temporary
directory solely for the original path-based `.9to1w` codec, then attempts to delete
that directory. An ancillary cleanup failure retains a warning and its real error
without turning an acknowledged import/download into a failed effect. This
filesystem staging does not provide durable browser storage;
the injected repository owns canonical commits. Import remains the owner's
explicit independent-copy operation. Export commits current edits, invokes the
actual package codec, and starts a browser download. It does not claim an external
file write receipt. The separate Packages project links only the unchanged native
codec and Application contracts, without the Infrastructure/AppPaths graph.

Browser rendering must reuse the existing retained `WriteDocumentSurface` and
Haven scene renderer through CUI registration. Per-surface registry and exact
original-source dependency links are coordinated with the common browser owner.
Donor Writer engine/format coverage, independent view/layout combinations and
nested section model requirements remain separate unresolved owner work.
