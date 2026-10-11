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

Image insertion is a genuine authoring journey rather than a placeholder. The
composition root supplies an `IWriteBrowserImageSource`, which opens the
platform's own image chooser and stages the picked file, and an
`INotesAttachmentStore`, which owns the attachment identity, the stored copy and
the digest. `InsertImage` imports the staged file through that store like the
Desktop host does, so the browser never assigns an attachment identity or a
storage path; without a store the action reports `AttachmentStoreMissing` and
without a chooser it reports `ImageSourceMissing` instead of accepting an image
no save could carry. The inserted block then edits through the same editor
operations as every other block: alt text, caption and wrapping through
`UpdateMedia`, size through `ResizeSelectedMedia`, rotation through
`RotateSelectedMedia` and the four crop fractions through `SetSelectedMediaCrop`,
each exposed as a two-way binding in `Write.cui` that writes the selected image's
own properties, and each taking part in undo, the dirty flag, save and the
read-only gate. The scene journey asserts the stored attachment keeps the exact
picked bytes with its digest and size, that every property writes through, and
that the saved document and the native package both carry the media block and its
canonical attachment reference.

Two ownership gaps remain open and are not covered by these assertions. The browser
composition root must supply both the chooser and the store: today
`apps/Web/BrowserFeatureComposition.cs` registers neither `BrowserWriteImageSource` nor an
`INotesAttachmentStore`, so a real browser run cannot pick an image until that root is wired.
That file is the shared composition root and belongs to its own owner, so this lane does not
edit it. Separately, `WriteNativeDocumentPackageStore` writes `manifest.json` and
`document.json` only, so an exported `.9to1w` carries the attachment reference but not the
attachment bytes; donor package parity for embedded media is an Infrastructure-owned gap.

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
