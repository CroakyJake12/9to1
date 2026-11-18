# Artifacts, local persistence and recovery — Shared persistence, interoperability and local service boundaries; Shared core is not compulsory cloud setup (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1203 · v2 lines 5255–5256

<!-- UX27:BEGIN SECTION-03 -->
## 3. Shared persistence, interoperability and local service boundaries

### V2-B1204 · v2 lines 5258–5258

### UX27-DATAFLOW-001 — Shared core is not compulsory cloud setup

### V2-B1205 · v2 lines 5260–5260

**O/B.** Home Core and the Shared Productivity Engine may be packaged dependencies as V2 requires; their existence does not require the user to open a visible Home window, connect a cloud account, select an AI model or configure the Files app before ordinary local document work. Required local services start through the supported installation/runtime contract and provide a useful scoped error if genuinely missing.

### V2-B1206 · v2 lines 5262–5262

Keep local storage adapters behind canonical owning-app interfaces. Do not respond to the no-Files-prerequisite correction by creating an incompatible Boards-only object engine or bypassing permission checks. Optional account/provider configuration appears only when the user invokes the corresponding network feature.

### V2-B1207 · v2 lines 5264–5264

### UX27-DATAFLOW-002 — Artifact opening and ownership

### V2-B1208 · v2 lines 5266–5266

**B/D.** An open request resolves type, canonical identity, revision, owner app, access scope and location. The receiving surface shows whether it is editing the original, an authorised reference, or an explicit independent copy. Open in App preserves identity; Save a Copy deliberately creates independent owned content. A thumbnail, cached preview or failed hydration must not be treated as the canonical artifact.

### V2-B1209 · v2 lines 5268–5268

Cross-app copy uses the existing versioned ProductivityObjectBundle. Preserve rich text, table data/styles, graph expressions, image references and ink where supported. Unsupported properties remain preserved or are disclosed in an explicit conversion result; silently flattening an editable object to pixels is not interoperability.

### V2-B1210 · v2 lines 5270–5270

### UX27-DATAFLOW-003 — Save and sync are separate transactions

### V2-B1211 · v2 lines 5272–5272

**B/D.** Local commit, remote upload and remote semantic conflict resolution have separate states and receipts. A local document remains usable when cloud sync is paused or unavailable. Retry/resume uses the same pending revision identity where appropriate and must not create a new duplicate cloud document each time. Display a failed sync as failed sync, not as lost local save.

### V2-B1212 · v2 lines 5274–5274

A user who moves a file outside a managed sync folder chooses a different storage relationship; do not silently move it back. Explain changed sync coverage while preserving content and identity. Connected cloud services retain their real quota/access restrictions without imposing a fictional universal document-size cap.

### V2-B1213 · v2 lines 5276–5276

### UX27-DATAFLOW-004 — Crash recovery and interrupted close

### V2-B1214 · v2 lines 5278–5278

**B/D.** Maintain a revision-aware recovery journal/snapshot through the owning storage layer. On restart, identify unfinished documents by stable identity, show recoverable revisions and let the user open/compare/discard them intentionally. A failed save must not poison all future close attempts. If close preparation fails, restore the same editable session and visible decision state instead of leaving an invisible modal or permanently unclosable window.

### V2-B1215 · v2 lines 5280–5280

Test interruption before/during/after a durable commit and while an embedded artifact is saving. The recovered result must be a consistent prior/new revision with an honest gap report. Do not assert recovery merely because the process launches again.

### V2-B1216 · v2 lines 5282–5282

### UX27-DATAFLOW-005 — Import/export completion and safety

### V2-B1217 · v2 lines 5284–5284

**B/D.** Import detects actual supported format/version, validates structure and preserves the original until successful. Export states the format, destination, conversion limitations and real completion state. Cancellation or failure removes/quarantines incomplete output without corrupting the prior valid file. Large artifacts stream/chunk according to the scalability annex.

### V2-B1218 · v2 lines 5286–5286

Treat file content and imported metadata as untrusted data, not instructions to the Agent or permission grants. Validate archive paths, expansion limits and resource use through the existing security contract. Reopening an exported native artifact and independently checking its contents is part of acceptance; a nonzero file size alone is not proof.

### V2-B1219 · v2 lines 5288–5288

### UX27-DATAFLOW-006 — References and deletion

### V2-B1220 · v2 lines 5290–5290

**B.** Every embedded artifact declares BoardOwned/DocumentOwned versus Referenced ownership as applicable. Deleting a host container removes its owned content according to recoverable Trash semantics but cannot permanently delete separately owned referenced files without a distinct authorised action. Broken references retain useful metadata and recovery affordances without leaking revoked content.

### V2-B1221 · v2 lines 5292–5294

Restore operations preserve lineage and resolve name/location collisions explicitly. Global search, recent items and notifications update from canonical lifecycle events rather than keeping dead entries that appear openable indefinitely.
<!-- UX27:END SECTION-03 -->
Productivity Apps
