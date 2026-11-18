9to1 V2 — Scalable Content and No Arbitrary Size Caps
Normative product and engineering annex — 20 October 2026
Canonical V2 master: https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit


AUTHORITY AND SCOPE — This owner-approved normative addition supplements V2's shared performance, CUI, Files, Write, Present, Canvas, Boards, Data and relevant media/editor contracts, both native and web. Preserve all existing donor-functionality, security, accessibility, reliability and independent acceptance requirements. It specifies scalable content handling; it does not claim literally infinite resources.


SCALE-001 — NO ARBITRARY SIZE CEILINGS. First-party 9to1-native documents/projects MUST NOT impose fixed global character, word, page, slide, block, object or file-byte caps just to simplify UI rendering, opening or saving. Allow practical growth subject to available disk/remote quota, supported format, filesystem/address-space and security limits. Avoid hard-coded application-wide caps when scalable indexing, streaming or chunking can address the bottleneck. Disclose every real limiting factor and prevent silent truncation.


SCALE-002 — USER-FACING PRIORITY. Pointer/keyboard input, caret, selection, active-region edits, scroll, drag and undo/redo are highest-priority work. Near-viewport prefetched rendering and user-requested navigation are next. Unrelated distant pagination/layout, content analysis, indexing, thumbnails, previews and sync are lower priority. Use responsive scheduling, cancellation, backpressure and background work; off-screen processing MUST NOT be a prerequisite for normal editing in the visible region.


SCALE-003 — CANONICAL STATE SEPARATE FROM VIEW. The persisted editable model, identities, relationships, undo/operation history and search semantics MUST be independent of materialised UI nodes, caches, page thumbnails or rendered slides. Never equate unrendered with nonexistent. Unloaded content remains consistent, accessible, searchable, exportable and editable after hydration, with correct reopen behaviour.


SCALE-004 — VIRTUALISED / STREAMED RENDERING. Render the visible pages/slides/tiles/cells/blocks/nodes/tracks plus a bounded adaptive buffer. Use region-based culling, placeholder sizing, lazy hydration, thumbnail/preview caching and eviction; do not instantiate the entire artifact's UI tree. Adapt prefetch to scrolling speed, device resources and expected jumps. Maintain memory budgets and gracefully reduce preview fidelity or buffer size under pressure, not document fidelity.


SCALE-005 — INCREMENTAL CALCULATION. Edit the current region immediately and invalidate only affected dependent layout/index/cache ranges. Reflow, paginate, rebuild derived views and thumbnails progressively with chunk or section indexes, layout checkpoints, dependency-aware invalidation and version/generation checks so stale computations cannot overwrite newer edits. Show provisional page counts or progress honestly until exact values are known; eventual output must be correct.


SCALE-006 — INDEXED NAVIGATION. Deep jumps, headings/outline, bookmarks, slide navigator, find/replace and selection across distant or unloaded regions use stable content IDs and persisted/incremental indexes. Do not require scanning/rendering all earlier pages to find a position. Show genuine progress for expensive uncached navigation instead of freezing or reporting guessed locations as certain.


SCALE-007 — CHUNKED PERSISTENCE. Prefer addressable chunks/objects, changed-range delta/operation saves, transactional/atomic commit, versioning, integrity checks, crash-safe recovery and bounded undo/snapshot retention. Autosave MUST NOT repeatedly serialise the whole large artifact per keystroke. Detect and resolve concurrent mutations rather than silently overwriting user content; safely clean orphaned chunks, temp files and caches.


SCALE-008 — STREAMING IMPORT, EXPORT AND MEDIA. Read/write/transfer large data incrementally; support resuming and cancelling long work with truthful job status and error recovery. Avoid full-file memory duplication wherever possible. Do not claim complete export until all required chunks, relationships and media have been validated and committed. Where a third-party format/provider has a genuine size/count limit, state it and offer a truthful native-format or partitioned workflow, never a fake success.


SCALE-009 — FILES AND CLOUD. Files MUST support stable asset identity, metadata indexing, byte-range or chunk-aware operations, integrity-verified resumable uploads/downloads, revisions, permissions, transactional commits and recovery. External cloud/storage quotas and device capacity may constrain actual sizes, but MUST be made transparent; no disguised universal file-size cap. Preserve privacy, secure storage and offline access according to the rest of V2.


SCALE-010 — SAFETY / RELIABILITY. Preserve disk-space margins, bounded caches and memory/CPU/VRAM budgets. Implement backpressure, malicious-file and decompression-bomb protection, limits for untrusted parser operations and safe failure handling. On insufficient resources, report the resource constraint, preserve changes, allow a safe retry/resume or lower preview resolution; never corrupt or silently discard content. Security constraints are valid even when they require rejecting malicious inputs.


SCALE-011 — CROSS-DEVICE / CROSS-APP. The native Windows/Linux and browser versions must share the same canonical scalable-content semantics, with truthful platform-specific limits. Embedded documents/slides/boards, shared search, AI features and cross-app conversion must not force whole-project eager loading. Maintain accessibility and keyboard navigation across virtualised regions. No downgraded web implementation may falsely display a truncated artifact as complete.


WRITE-SCALE-201 — LONG DOCUMENTS. Write MUST support content well beyond conventional single-document character limits using visible/adjacent page rendering, section-based hydration, incremental line wrapping and pagination, indexed find/replace, accessible semantic headings, deep jumps, and correct undo/redo. Inserting text near page 1 of a very long document must not synchronously reflow every distant page. Preserve authentic document layout/export correctness once background work completes.


PRESENT-SCALE-201 — VERY LARGE DECKS. Present MUST lazily create selected-slide and near-slide surfaces, virtualise slide lists/thumbnails, incrementally update slide order/section indexes, retain persistent SlideIDs and NotesIDs, and load embedded media on demand. Inserting/reordering a slide cannot require reconstructing all slide UI. Playback and export must process every applicable slide accurately via bounded batches or truthful progress. No arbitrary native slide-count or file-byte ceiling.


OTHER-APPS-SCALE-201 — ANALOGOUS DOMAIN DESIGN. Boards and Canvas virtualise blocks/spatial regions. Data virtualises rows/cells/results and streams large data operations. Picture uses tiled or region-aware image processing. Wave and Motion load, cache and process time ranges/tracks/media chunks under strict playback/resource budgets. All other applicable apps use corresponding index/stream/cull methods while retaining their real workflows.


SCALE-AC-201 — TESTING AND MEASUREMENT. Independent acceptance MUST include representative small/medium/large and stress real-artifact tests across supported platforms. Example stress targets: >=1,000 pages in Write, >=10,000 slides in Present where reasonable on test hardware, sizeable binary files, nested/embedded artifacts and large tables/boards. Capture cold/warm load, input and scroll latency, frame times, memory/VRAM/disk consumption, deep-jump latency, edits near start/end, asynchronous reflow, incremental save, recovery after interruption, search across unloaded regions, reopen and full export/import integrity. Define platform/device-specific budgets and report actual results; no fake data, hidden drops or assertions based only on successful startup.


SCALE-AC-202 — MANDATORY FAILURES. FAIL when responsiveness is achieved by silently omitting content, nonfunctional editing/find/navigation, quietly disabled save/export, pretend background completion, runaway caching, data loss, unsupported format misrepresentation or hidden arbitrary limits. Existing application-specific verification and donor-parity gates remain in force.


IMPLEMENTATION STATUS — This annex changes requirements only. It does not assert that Write, Present, Files or any other 9to1 app currently implements or has passed these capabilities.
