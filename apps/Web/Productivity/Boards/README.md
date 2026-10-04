This portable core calls the unchanged `BoardsWorkspaceService` over an injected
real `INotesRepository`. It keeps original Notes notebook/section/page/block/card
objects and owner-generated IDs, page Edit/View and Locked/Unlocked enforcement,
and the backend's actual expected-version CAS and commit receipt. A transparent
repository decorator observes owner create/save payloads and receipts; every
method delegates to the same real backend. It provides no storage, counter,
validator, document schema or alternative editor.

Typed changes run on a canonical snapshot through the real owner, preserving the
existing draft if the owner denies an operation. Unknown commit outcome blocks
blind replay and close until explicit durable inspection confirms the complete
intended snapshot or an unchanged base. Recovery metadata cannot confirm an
uncertain current commit. Read-only presentation does not assert backend ACLs.

The route prepares a separate real core and fences the active document ID,
acknowledged revision and edit generation before accepting its presentation.
A surface must be injected, use that exact core for bindings/actions, and satisfy
the parent's real CUI lowering/admission lifecycle. Rejection leaves the prior
core and UI intact. The feature never substitutes a default board renderer.

The existing desktop `BoardsPage` owns private mutable document/editor state,
loads through its own service, and saves internally. Its entire original control
closure and a reviewed canonical document/input/lifecycle seam are needed before
using it with this staged browser core. No parent registration is authored.

The real-repository core seed/fresh-process verify/unknown-receipt controls
passed 11, 6 and 6 assertions. Original owner seed/restart controls passed 11
and 10 assertions, including real recoverable trash/restore. The staged feature
compiles against the actual registry but has no rendered owner-surface acceptance.
Exact source, commands and oracles are recorded in the Team B
`continuation/b2/productivity-gates/handoff.json` evidence.
Native/browser editor integration and the full canonical
Boards requirements, donor parity, attachments, rich live components, history,
collaboration and permissions remain required and unverified. This focused
existing notebook-owner path does not replace other maintained shared Board
models or rich renderers. See OWNER-CONTRACT-REQUEST.json for exact owner seams.
