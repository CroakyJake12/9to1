# Platform, app registry and host integration — Integration; Large board keeps every block and stroke (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2338 · v2 lines 13509–13510

Embedded organisation pages in Boards clearly identify the owning artifact, read/edit capability and Open original action. Returning to the board restores context. A visually similar grid of labels is not acceptance of the real information model.
<!-- UX27:END SECTION-13 -->

### V2-B2900 · v2 lines 15187–15187

## Integration

### V2-B2901 · v2 lines 15189–15189

### UX27-TEST-100 — Large board keeps every block and stroke

### V2-B2902 · v2 lines 15191–15191

**Requirements:** UX27-SHARED-020, UX27-ACCEPT-005 · **Status:** NOT RUN

### V2-B2903 · v2 lines 15193–15193

**Given:** Declared large board fixture with known counts, IDs and content hashes.

### V2-B2904 · v2 lines 15195–15197

**When:**
1. Edit early/late blocks, drag to a distant target and draw near the viewport.
2. Search unloaded content, save/reopen and export all.

### V2-B2905 · v2 lines 15199–15202

**Then:**
- Input remains responsive within measured hardware-specific budgets.
- All blocks/strokes are retained regardless of virtualisation.
- Full output hash/count comparison catches silent omissions; no arbitrary limit is invented.

### V2-B2906 · v2 lines 15204–15204

### UX27-TEST-101 — Independent review of actual main build

### V2-B2907 · v2 lines 15206–15206

**Requirements:** UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-004 · **Status:** NOT RUN

### V2-B2908 · v2 lines 15208–15208

**Given:** Reconciled candidate with all original branch work accounted for and a separate reviewer available.

### V2-B2909 · v2 lines 15210–15213

**When:**
1. Inspect integration inventory and source/artifact identities.
2. Launch using Jacob's documented normal entry point.
3. Independently run representative repaired journeys and record all seven gates.

### V2-B2910 · v2 lines 15215–15218

**Then:**
- No unmerged/dirty work was deleted to achieve one branch.
- Launched build corresponds to the recorded main candidate.
- Consolidation alone is not acceptance; missing runtime/platform/independence gates stay unpassed.

### V2-B2911 · v2 lines 15220–15220

### UX27-TEST-102 — Full manifest and honest unavailable coverage

### V2-B2912 · v2 lines 15222–15222

**Requirements:** UX27-AUTH-003, UX27-AUTH-006, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-004, UX27-ACCEPT-003, UX27-ACCEPT-006 · **Status:** NOT RUN

### V2-B2913 · v2 lines 15224–15224

**Given:** Baseline, expansion and selected donor/reference versions available to a reviewer.

### V2-B2914 · v2 lines 15226–15229

**When:**
1. Map every applicable requirement to app/entry point/engine/storage/evidence.
2. Compare the scenario catalogue to that manifest.
3. Record absent devices, images, platforms and tests explicitly.

### V2-B2915 · v2 lines 15231–15235

**Then:**
- Catalogue is a floor, not a scope reduction.
- Draft defaults are not represented as measured historic screenshot details.
- Every claim has source/runtime provenance, and NOT RUN is not relabelled PASS.
<!-- UX27:END ACCEPTANCE-CATALOGUE -->

### SCALABILITY-B0001 · scalability lines 1–3

9to1 V2 — Scalable Content and No Arbitrary Size Caps
Normative product and engineering annex — 20 October 2026
Canonical V2 master: https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit

### SCALABILITY-B0002 · scalability lines 6–6

AUTHORITY AND SCOPE — This owner-approved normative addition supplements V2's shared performance, CUI, Files, Write, Present, Canvas, Boards, Data and relevant media/editor contracts, both native and web. Preserve all existing donor-functionality, security, accessibility, reliability and independent acceptance requirements. It specifies scalable content handling; it does not claim literally infinite resources.

### SCALABILITY-B0003 · scalability lines 9–9

SCALE-001 — NO ARBITRARY SIZE CEILINGS. First-party 9to1-native documents/projects MUST NOT impose fixed global character, word, page, slide, block, object or file-byte caps just to simplify UI rendering, opening or saving. Allow practical growth subject to available disk/remote quota, supported format, filesystem/address-space and security limits. Avoid hard-coded application-wide caps when scalable indexing, streaming or chunking can address the bottleneck. Disclose every real limiting factor and prevent silent truncation.

### SCALABILITY-B0004 · scalability lines 12–12

SCALE-002 — USER-FACING PRIORITY. Pointer/keyboard input, caret, selection, active-region edits, scroll, drag and undo/redo are highest-priority work. Near-viewport prefetched rendering and user-requested navigation are next. Unrelated distant pagination/layout, content analysis, indexing, thumbnails, previews and sync are lower priority. Use responsive scheduling, cancellation, backpressure and background work; off-screen processing MUST NOT be a prerequisite for normal editing in the visible region.

### SCALABILITY-B0005 · scalability lines 15–15

SCALE-003 — CANONICAL STATE SEPARATE FROM VIEW. The persisted editable model, identities, relationships, undo/operation history and search semantics MUST be independent of materialised UI nodes, caches, page thumbnails or rendered slides. Never equate unrendered with nonexistent. Unloaded content remains consistent, accessible, searchable, exportable and editable after hydration, with correct reopen behaviour.

### SCALABILITY-B0006 · scalability lines 18–18

SCALE-004 — VIRTUALISED / STREAMED RENDERING. Render the visible pages/slides/tiles/cells/blocks/nodes/tracks plus a bounded adaptive buffer. Use region-based culling, placeholder sizing, lazy hydration, thumbnail/preview caching and eviction; do not instantiate the entire artifact's UI tree. Adapt prefetch to scrolling speed, device resources and expected jumps. Maintain memory budgets and gracefully reduce preview fidelity or buffer size under pressure, not document fidelity.

### SCALABILITY-B0007 · scalability lines 21–21

SCALE-005 — INCREMENTAL CALCULATION. Edit the current region immediately and invalidate only affected dependent layout/index/cache ranges. Reflow, paginate, rebuild derived views and thumbnails progressively with chunk or section indexes, layout checkpoints, dependency-aware invalidation and version/generation checks so stale computations cannot overwrite newer edits. Show provisional page counts or progress honestly until exact values are known; eventual output must be correct.

### SCALABILITY-B0008 · scalability lines 24–24

SCALE-006 — INDEXED NAVIGATION. Deep jumps, headings/outline, bookmarks, slide navigator, find/replace and selection across distant or unloaded regions use stable content IDs and persisted/incremental indexes. Do not require scanning/rendering all earlier pages to find a position. Show genuine progress for expensive uncached navigation instead of freezing or reporting guessed locations as certain.

### SCALABILITY-B0009 · scalability lines 27–27

SCALE-007 — CHUNKED PERSISTENCE. Prefer addressable chunks/objects, changed-range delta/operation saves, transactional/atomic commit, versioning, integrity checks, crash-safe recovery and bounded undo/snapshot retention. Autosave MUST NOT repeatedly serialise the whole large artifact per keystroke. Detect and resolve concurrent mutations rather than silently overwriting user content; safely clean orphaned chunks, temp files and caches.

### SCALABILITY-B0010 · scalability lines 30–30

SCALE-008 — STREAMING IMPORT, EXPORT AND MEDIA. Read/write/transfer large data incrementally; support resuming and cancelling long work with truthful job status and error recovery. Avoid full-file memory duplication wherever possible. Do not claim complete export until all required chunks, relationships and media have been validated and committed. Where a third-party format/provider has a genuine size/count limit, state it and offer a truthful native-format or partitioned workflow, never a fake success.

### SCALABILITY-B0012 · scalability lines 36–36

SCALE-010 — SAFETY / RELIABILITY. Preserve disk-space margins, bounded caches and memory/CPU/VRAM budgets. Implement backpressure, malicious-file and decompression-bomb protection, limits for untrusted parser operations and safe failure handling. On insufficient resources, report the resource constraint, preserve changes, allow a safe retry/resume or lower preview resolution; never corrupt or silently discard content. Security constraints are valid even when they require rejecting malicious inputs.

### SCALABILITY-B0013 · scalability lines 39–39

SCALE-011 — CROSS-DEVICE / CROSS-APP. The native Windows/Linux and browser versions must share the same canonical scalable-content semantics, with truthful platform-specific limits. Embedded documents/slides/boards, shared search, AI features and cross-app conversion must not force whole-project eager loading. Maintain accessibility and keyboard navigation across virtualised regions. No downgraded web implementation may falsely display a truncated artifact as complete.

### SCALABILITY-B0016 · scalability lines 48–48

OTHER-APPS-SCALE-201 — ANALOGOUS DOMAIN DESIGN. Boards and Canvas virtualise blocks/spatial regions. Data virtualises rows/cells/results and streams large data operations. Picture uses tiled or region-aware image processing. Wave and Motion load, cache and process time ranges/tracks/media chunks under strict playback/resource budgets. All other applicable apps use corresponding index/stream/cull methods while retaining their real workflows.

### SCALABILITY-B0017 · scalability lines 51–51

SCALE-AC-201 — TESTING AND MEASUREMENT. Independent acceptance MUST include representative small/medium/large and stress real-artifact tests across supported platforms. Example stress targets: >=1,000 pages in Write, >=10,000 slides in Present where reasonable on test hardware, sizeable binary files, nested/embedded artifacts and large tables/boards. Capture cold/warm load, input and scroll latency, frame times, memory/VRAM/disk consumption, deep-jump latency, edits near start/end, asynchronous reflow, incremental save, recovery after interruption, search across unloaded regions, reopen and full export/import integrity. Define platform/device-specific budgets and report actual results; no fake data, hidden drops or assertions based only on successful startup.

### SCALABILITY-B0018 · scalability lines 54–54

SCALE-AC-202 — MANDATORY FAILURES. FAIL when responsiveness is achieved by silently omitting content, nonfunctional editing/find/navigation, quietly disabled save/export, pretend background completion, runaway caching, data loss, unsupported format misrepresentation or hidden arbitrary limits. Existing application-specific verification and donor-parity gates remain in force.

### SCALABILITY-B0019 · scalability lines 57–57

IMPLEMENTATION STATUS — This annex changes requirements only. It does not assert that Write, Present, Files or any other 9to1 app currently implements or has passed these capabilities.
