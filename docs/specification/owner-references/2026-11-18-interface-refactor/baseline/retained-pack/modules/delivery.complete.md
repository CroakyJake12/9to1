# Delivery governance and historical process

Module ID: `delivery` · 9to1 Development Specification v3

## Prerequisites

- **Authority, scope and implementation reading map** — Current source precedence, narrow owner overrides, source accounting and the required module structure.

## Overview

This module retains product-delivery constraints and historical process provenance without promoting expired schedules or agent-operation instructions into current authorisation. Future/deferred scope remains clearly separate from the current release contract.

Current owner overrides: V3-RECON-002.

## Layouts & visible states

- **V3-DELIVERY-LAY-01** — Documentation exposes current implementation/verification status honestly. A design mock-up, planned feature or compiling placeholder is not labelled complete functionality.
- **V3-DELIVERY-LAY-02** — Keep deferred Training Lab/Android-ROM expansion and undefined capabilities visibly out of current acceptance claims.

## Features & behaviour

- **V3-DELIVERY-FEA-01** — Preserve retained source, licensing, donor parity, migration and quality requirements that affect the product deliverable.
- **V3-DELIVERY-FEA-02** — Historical deadlines, worker schedules and operational handoff instructions are archived with their source IDs and original wording, not followed as new tasks.
- **V3-DELIVERY-FEA-03** — Use module/source traceability to identify implementation work and unresolved dependencies; do not silently redefine completed scope from a visual review.

## Canonical entities & persistence

- **V3-DELIVERY-ENT-01** — Maintain versioned source/override ledgers, module boundaries and acceptance evidence references. Original source bytes remain available for audit.
- **V3-DELIVERY-ENT-02** — Distinguish planned, implemented, tested and independently accepted status records.

## Permissions & recovery

- **V3-DELIVERY-PER-01** — External deployment, spending and communication still require the operation’s current authority; historical prose is not authorisation.
- **V3-DELIVERY-PER-02** — Record unresolved contradictions or absent references rather than inventing missing backend guarantees.

## Acceptance checks

- **V3-DELIVERY-ACC-01** — Check historical process is excluded from active technical execution instructions while substantive product requirements remain retained.
- **V3-DELIVERY-ACC-02** — Verify future/deferred scope is not shown as implemented or accepted.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

LE-01, LE-15, UX27-ACCEPT-001, UX27-ACCEPT-002, UX27-ACCEPT-003, UX27-ACCEPT-004, UX27-ACCEPT-005, UX27-ACCEPT-006, SECTION-14, SECTION-15, UX27-COVERAGE-001, UX27-COVERAGE-002, UX27-COVERAGE-003, UX27-COVERAGE-004, SECTION-16, UX27-SOURCES-001, SHA-256, UX27-SOURCES-002, SECTION-17, UX27-MAIN-001, UX27-MAIN-002, UX27-MAIN-003

## Technical reading map

- Delivery governance and historical process — Pre-pass baseline, merge and user test drop; Scope expansion (part 01) — `technical/delivery-01.md` (2,348 words; V2-B2156, V2-B2157, V2-B2159…).
- Delivery governance and historical process — Verify what the user actually launches (part 02) — `technical/delivery-02.md` (215 words; V2-B2385, V2-B2386, V2-B2387…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Delivery governance and historical process — Pre-pass baseline, merge and user test drop; Scope expansion (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2156 · v2 lines 12542–12542

Pass 2: Terra-form

### V2-B2157 · v2 lines 12545–12547

Pass 2 operating contract
Pass identity and model
Terra-form is the codename for this development pass only. It MUST NOT be interpreted as a model family, capability tier or instruction to use a model named Terra. All Terra-form implementation workers and the coordinator MUST use GPT-6.1 Sol wherever model selection is configurable. The intended execution shape is six parallel implementation workers plus the coordinator unless an external concurrency limit temporarily prevents it.

### V2-B2159 · v2 lines 12554–12555

Pre-pass baseline, merge and user test drop
Before Terra-form feature implementation begins, the final accepted Astra la Vista head MUST complete its exact-head acceptance, then be merged into main while preserving history. Do not squash, force-push, destructively reset or discard accepted evidence. The resulting main commit is the Terra-form baseline.

### V2-B2160 · v2 lines 12558–12558

Before the pass begins, build usable test artifacts for every currently shippable 9to1 product or platform target and deliver clearly named copies to the user's local PC. Include a manifest mapping each product to its artifact path, version, exact commit, cryptographic hash, platform/runtime requirements and launch instructions, plus a concise known-limitations record. An artifact that has not passed its required build/launch acceptance MUST be labelled honestly rather than represented as release-ready. User testing and feedback from this drop becomes a priority defect stream during Terra-form.

### V2-B2161 · v2 lines 12561–12562

Scope expansion
All mandatory requirements from the existing specification remain in scope and MUST NOT regress. The previously deferred Files organisation, Link, Prototype, Paths, Replay and Variants tranche below is now mandatory Terra-form scope. There is no generic Future or out-of-scope escape hatch for these named features. Only concepts explicitly left OPEN, such as the separate Playgrounds naming/product question, remain non-committal unless the user explicitly expands scope again.

### V2-B2162 · v2 lines 12565–12566

Implementation and evidence contract
At pass start, regenerate a requirement-by-requirement index from the entire authoritative specification and this Terra-form tranche. Track every requirement as VERIFIED, IMPLEMENTED-UNVERIFIED, MISSING or BLOCKED. A source file, authored test, worker statement or isolated draft does not establish completion. Completed cohorts MUST be integrated continuously through bounded source capture, owning tests, regression/resource/native checks where applicable, accepted-root integration and exact-head CI rather than saved for a final mega-merge.

### V2-B2176 · v2 lines 12922–12923

Pass 1 incorporated requirements: Lunar Eclipse (LE-01–LE-15)
This is the final feature-completion, integration and polish pass before release. The LE contracts below make every named requirement executable and testable.

### V2-B2178 · v2 lines 12936–12937

Modularity
Users will not need to install the entire 9to1 suite. They can choose individual modules/apps to install.

### V2-B2179 · v2 lines 12940–12941

Mandatory Modules include anything that is shared by multiple apps (dulche, cui, node editor, etc). These MUST be installed to use a 9to1 app.
Home must also be installed.

### V2-B2180 · v2 lines 12944–12944

Every app other than Home is optional to select. Single-app frameworks required for startup or typical functions MUST install automatically with their app and cannot be removed independently while required. Other non-mandatory niche-feature modules may install on demand when their feature is requested. Shared modules remain mandatory under the rule above; installation does not require eagerly loading them.

### V2-B2181 · v2 lines 12947–12947

The apps shouldn’t be distributed as standalone msix installers. Instead, there should be a single installer that installs everything mandatory + the app you want.

### V2-B2340 · v2 lines 13513–13513

## 14. Acceptance protocol and executable scenario catalogue

### V2-B2341 · v2 lines 13515–13515

### UX27-ACCEPT-001 — Seven gates and exact-artifact review

### V2-B2342 · v2 lines 13517–13517

**B.** Every complete app and substantial journey must pass Usability, Product fit, Functionality, Integration, Separation and authenticity, User fit and Refinement against its full scoped manifest. A numerical average or a working subset cannot compensate for a failed gate. The implementation author’s checks, source review, automated runtime tests, visual review and independent acceptance are separate evidence categories.

### V2-B2343 · v2 lines 13519–13519

Each result records the exact source revision, build/artifact hashes, dependency pins, OS/architecture, display scale, viewport, theme, input device, test profile, scenario parameters, commands/exit codes, reviewer identity and author/reviewer separation. A test status is NOT RUN until that scenario actually executes; writing this specification does not change any product’s acceptance state. Missing native Windows, browser, stylus or independent visual evidence stays explicitly missing.

### V2-B2344 · v2 lines 13521–13521

### UX27-ACCEPT-002 — Input-to-model-to-persistence proof

### V2-B2345 · v2 lines 13523–13523

**B/D.** For an interactive operation, verify four things together: the real routed control receives the intended input; the visible response matches the specified state; the canonical model changes correctly; and undo/save/reopen retain the intended result. Source declarations, a standalone component constructor, a menu label and a unit-test mock are insufficient proof of host integration.

### V2-B2346 · v2 lines 13525–13525

For example, Enter must produce two actual BlockIDs, not simply two painted lines; a drag must change the stored parent/order while preserving identity; pen input must produce persisted stroke geometry; Save As must create a valid editable file at the selected destination. An inspection adapter may expose model IDs to a test runner, but developer diagnostics must not impersonate ordinary user UI.

### V2-B2347 · v2 lines 13527–13527

### UX27-ACCEPT-003 — Fixtures and parameter coverage

### V2-B2348 · v2 lines 13529–13529

**D/B.** Use disposable test profiles and clearly labelled synthetic test artifacts. Never mutate Jacob’s live board, account, browser profile or source merely to make a test convenient. Test at least an empty artifact, a typical mixed artifact, a large/virtualised artifact and meaningful failures for each owning domain.

### V2-B2349 · v2 lines 13531–13531

Run applicable scenarios with mouse/keyboard and the supported pen/touch route; with representative light and dark themes; at 100%, 125%, 150% and 200% scaling; at narrow and wide viewports; and through both standalone and integrated entry points where specified. Use pairwise selection for routine regression runs and a documented broader matrix at integration/release; do not claim every combination was run from one representative case. Separate geometry/input failures from unavailable device/environment coverage.

### V2-B2350 · v2 lines 13533–13533

### UX27-ACCEPT-004 — Independence and visual evidence

### V2-B2351 · v2 lines 13535–13535

**B.** Independent app acceptance requires a reviewer other than the implementing agent to inspect the exact candidate and independently exercise representative workflows. Copying the implementing agent’s pass counts is not independent execution. A text-only agent can inspect source, accessibility trees and logs, and may capture screenshots for a capable reviewer; it cannot claim visual inspection of images it could not process.

### V2-B2352 · v2 lines 13537–13537

Screenshots prove only what is visible at their capture time. Preserve interaction traces or short recordings for dynamic properties such as click-off dismissal, drag previews, scroll direction, caret behaviour and continuous ink. Reference mockups establish intended design, not successful implementation. Never compare screenshots from a different branch/build as though they accepted the current executable.

### V2-B2353 · v2 lines 13539–13539

### UX27-ACCEPT-005 — Regression scope and large content

### V2-B2354 · v2 lines 13541–13541

**B/D.** Each coherent change runs focused tests for its altered contracts and known rejection cases. Shared CUI input, theme, route, storage or command changes trigger impacted downstream journeys; unchanged exact-source evidence can be reused only with an explicit dependency-impact justification. Run broader integrated regressions before release instead of blocking every keystroke-sized change on every app’s entire suite.

### V2-B2355 · v2 lines 13543–13543

Measure cold/warm load, pointer-to-preview latency, typing responsiveness, frame times, memory, save/reopen, deep navigation and full export at declared workload sizes. Preserve all content under resource pressure. A faster result obtained by omitting off-screen blocks, dropping strokes or silently truncating an export is a failure. Record observed numbers and environment, not guessed benchmark claims.

### V2-B2356 · v2 lines 13545–13545

### UX27-ACCEPT-006 — Scenario catalogue is a floor

### V2-B2357 · v2 lines 13547–13547

**B/D.** The companion acceptance catalogue provides concrete reproductions for the interactions clarified in this edition. Each scenario references the relevant UX27 requirements and begins NOT RUN. It supplements the full baseline/donor requirement manifest rather than replacing it. Add any missing donor workflow, security, collaboration, platform or accessibility scenario discovered during implementation.

### V2-B2358 · v2 lines 13549–13550

A scenario passes only when all its stated expectations hold. A failed setup is BLOCKED with evidence, not PASS. A discovered defect is FAIL with exact reproduction. A prerequisite that genuinely does not apply requires a reason tied to the specification; absence of implementation is not a reason to mark NOT APPLICABLE.
<!-- UX27:END SECTION-14 -->

### V2-B2359 · v2 lines 13552–13553

<!-- UX27:BEGIN SECTION-15 -->
## 15. Coverage, migration and explicit remaining source gaps

### V2-B2360 · v2 lines 13555–13555

### UX27-COVERAGE-001 — What this expansion covers

### V2-B2361 · v2 lines 13557–13557

This edition supplies detailed interaction contracts for shared CUI controls, motion, icons, focus, menus, scrolling and state; the Boards shell, hierarchy, block editor, dragging, ink, layout, tables, graphs and files; host/browser integration; Spaces/composer/models; Automations; Build; Write; Present; Canvas; Data; Files; and additional concrete workflows for the remaining app families.

### V2-B2362 · v2 lines 13559–13559

The complete baseline text is retained in the source-preserving master. Requirements not repeated here remain binding, including mature donor functionality, APIs, security, permissions, performance, accessibility, collaboration, data ownership and platform parity. The additional rules do not imply a previously missing baseline capability is now implemented.

### V2-B2363 · v2 lines 13561–13561

### UX27-COVERAGE-002 — Topology recovered; exact historic images not invented

### V2-B2364 · v2 lines 13563–13563

The 25 October Boards decisions establish the top toolbar, bottom Add/user area, nested pages, top-left Boards identity, removal of the top-right AppFlowy badge, Add page/section popup and theme-independent layout. Those decisions are incorporated explicitly.

### V2-B2365 · v2 lines 13565–13565

The original screenshot set and completed Boards design-review PDF from that discussion were not recovered as a complete visual reference pack in this run. Therefore new dimensions, spacing, animation durations and gesture thresholds are labelled D. They must not be represented as measured pixels or timings from those images. Preserve any accessible approved reference in the implementation’s source-authority register and reconcile differences without throwing away the owner’s explicit layout decisions.

### V2-B2366 · v2 lines 13567–13567

### UX27-COVERAGE-003 — Adoption into the canonical specification

### V2-B2367 · v2 lines 13569–13569

The connected Google Doc remains unchanged because no document-write operation was exposed during this task. To adopt this edition, use an authorised editor to incorporate the UX27 additions alongside their corresponding canonical sections, preserving the baseline’s native tables, images, links, styles and revision history. Do not replace the original structured document with a flattened text export merely to make the update easy.

### V2-B2368 · v2 lines 13571–13571

The package includes a text master with insertion markers and a hash-verified baseline snapshot. Remove only its generated insertion regions to reconstruct the exact exported baseline bytes. The manifest records the actual preservation check. Native Google Docs formatting/embedded images are outside that text-byte claim; they remain in the original document.

### V2-B2369 · v2 lines 13573–13573

Update source pins only through the existing documented revision process after an authorised adoption. The old pin and this edition’s hashes remain traceable; a new requirement document must not silently masquerade as the earlier pinned revision. No milestone status, runtime setting or worker permission is changed by creating this package.

### V2-B2370 · v2 lines 13575–13575

### UX27-COVERAGE-004 — Implementation and defect provenance

### V2-B2371 · v2 lines 13577–13577

The owner’s reports of broken Enter, dragging, pen input and unwanted Files/folder requirements are binding correction inputs, not claims that this task reproduced their code-level causes. Sampled recording frames independently support the visible observations in the evidence notes. The existing worker’s claims about routes, one-shot leases or engines remain hypotheses unless separately verified against source/runtime evidence.

### V2-B2372 · v2 lines 13579–13580

Branch divergence, stale executables, incorrect route registration and event routing are possible investigation directions. Do not declare any of them the universal cause from screenshots alone. Preserve observed behaviour, reproduction steps, source findings and causal conclusions as separate fields.
<!-- UX27:END SECTION-15 -->

### V2-B2373 · v2 lines 13582–13583

<!-- UX27:BEGIN SECTION-16 -->
## 16. Sources and provenance register

### V2-B2374 · v2 lines 13585–13585

### UX27-SOURCES-001 — Primary product authority

### V2-B2375 · v2 lines 13587–13591

1. **Existing V2 master:** https://docs.google.com/document/d/17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY/edit — native document read and full text export retrieved 27 October 2026; raw export SHA-256 is recorded in §0 and the package manifest. Contains the CUI, acceptance, donor, app and prior amendment baseline.
2. **Scalable Content & No Arbitrary Size Caps, 20 October:** https://docs.google.com/document/d/1B7XG1fuW7auRuzXMRZQeuq2dp59pqrS0jHckg9B-ol4/edit — retrieved during this task; preserved as a compatible source requirement, not a measured performance result.
3. **Direct owner amendments:** this conversation’s request for hyper-specific behaviour and its Enter/grip/canvas/pen corrections, optional `.9to1board` saving without Files connection or a required folder, and safe convergence of development onto main. Recent retrieved 21–26 October decisions are summarised in §0 and identified in individual clauses.
4. **Earlier owner context retained where applicable:** Model Groups/lazy responder loading, reasoning-effort display, Summary/Task/Research/Shopping/Assistants presentation. These clarify related sections; they do not grant new execution or spending permissions.
5. **Current recording:** `Screencast from 2026-10-27 12-48-26.webm`, approximately 240.4 seconds. The evidence folder contains selected captured frames and a timestamped observation note. The recording is a current-state example, not the approved design reference.

### V2-B2376 · v2 lines 13593–13593

### UX27-SOURCES-002 — Narrow external behaviour references

### V2-B2377 · v2 lines 13595–13595

The following official pages were consulted on 27 October 2026. Their role is limited to the short factual behaviours described here. All newly chosen geometry, timing, model transitions and detailed acceptance cases in this edition are 9to1 design requirements, not claims that Notion/AppFlowy use the same implementation or exact values.

### V2-B2378 · v2 lines 13597–13604

- **Notion writing/editing basics:** https://www.notion.com/help/writing-and-editing-basics — block insertion and the distinction between opening a six-dot action menu and dragging its block.
- **Notion columns/headings/dividers:** https://www.notion.com/help/columns-headings-and-dividers — block movement, visible drop guidance and column organisation as user-facing reference concepts.
- **Notion keyboard shortcuts:** https://www.notion.com/help/keyboard-shortcuts — ordinary Enter versus a soft line break within a block, with context-specific editing shortcuts. This edition explicitly defines its own heading/code/table exceptions rather than guessing every donor edge case.
- **AppFlowy documentation:** https://docs.appflowy.io/docs — the donor documentation entry point. No complete running donor audit or exact animation measurement was performed here; selected-revision parity remains an implementation/acceptance obligation.
- **WAI-ARIA tree pattern:** https://www.w3.org/WAI/ARIA/apg/patterns/treeview/ — accessible hierarchy navigation and the distinction between focus, expansion and selection.
- **WAI-ARIA menu-button pattern:** https://www.w3.org/WAI/ARIA/apg/patterns/menu-button/ — semantic menu activation, keyboard operation and state exposure. Native CUI accessibility should expose equivalent semantics through its platform adapter, not pretend the native tree is HTML.
- **WCAG contrast explanation:** https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html — ordinary text contrast of 4.5:1 and large text 3:1 where applicable. This is one criterion, not a claim of complete WCAG conformance.
- **WCAG interaction-animation explanation:** https://www.w3.org/WAI/WCAG22/Understanding/animation-from-interactions.html — disabling nonessential interaction-triggered motion. The listed 9to1 timing tokens are new design defaults, not W3C-prescribed durations.

### V2-B2379 · v2 lines 13606–13607

External product names describe interoperability/donor behaviour, not permission to copy proprietary source, protected assets, branding, account systems or inaccessible services. Preserve the original licensing and provenance rules.
<!-- UX27:END SECTION-16 -->

### V2-B2380 · v2 lines 13609–13610

<!-- UX27:BEGIN SECTION-17 -->
## 17. Development-source convergence and real-build identity

### V2-B2381 · v2 lines 13612–13612

### UX27-MAIN-001 — One authoritative integration line

### V2-B2382 · v2 lines 13614–13614

**O/B.** For development of 9to1 itself, converge legitimate unique work into the existing main branch and establish one documented canonical build/test/launch checkout. Inventory actual local and authorised remote branches, worktrees, pending integration, unpushed commits, dirty/untracked files and their current owners before integration. Do not infer branch value from its name or age.

### V2-B2383 · v2 lines 13616–13616

Preserve recoverable original tips and uncommitted work in an authorised private archive/bundle. Account for every candidate as integrated, duplicate, superseded, incompatible with newer explicit requirements or unresolved. Do not bulk-merge obsolete interfaces merely to reduce the branch count. Do not discard donor functionality to resolve a conflict more quickly.

### V2-B2384 · v2 lines 13618–13618

### UX27-MAIN-002 — Non-destructive integration and retirement


# Delivery governance and historical process — Verify what the user actually launches (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2385 · v2 lines 13620–13620

**O/B.** Integrate in dependency order, preserving source authority, data formats, permissions and acceptance evidence. Run focused tests at coherent checkpoints and integrated user journeys on the resulting main build. Do not force-push, bypass branch protections, reset away work, delete an active worktree or overwrite another worker’s files. An unresolved active owner must be reconciled, not presumed absent.

### V2-B2386 · v2 lines 13622–13622

Retire only fully accounted-for inactive first-party development branch references through normal authorised operations, retaining recoverability. Do not flatten third-party/upstream/vendor histories or unrelated repositories into main. A branch left temporarily for unresolved work is an explicit exception, not grounds to delete it to manufacture success.

### V2-B2387 · v2 lines 13624–13624

### UX27-MAIN-003 — Verify what the user actually launches

### V2-B2388 · v2 lines 13626–13626

**O/B.** Record final local main, published remote main where authorised, canonical checkout, build inputs, packaged dependencies and executable/artifact hashes separately. Verify the normal launcher opens that exact candidate and the intended host routes. A successful component test from another checkout cannot prove a visible fix in the running app.

### V2-B2389 · v2 lines 13628–13629

Consolidated source is not product acceptance. Retest the owner’s actual launch, Present, Boards, browser and persistence failures against the resulting build, preserving every unmet requirement. Existing paused sessions, the separate laptop connection, models, spending limits and schedules remain unchanged unless separately authorised. Expired historical deadlines do not permit destruction or fabricated completion.
<!-- UX27:END SECTION-17 -->
