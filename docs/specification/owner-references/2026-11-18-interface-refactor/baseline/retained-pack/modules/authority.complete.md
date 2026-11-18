# Authority, scope and implementation reading map

Module ID: `authority` · 9to1 Development Specification v3

## Prerequisites

This module establishes the shared foundation and has no prerequisite product UI.

## Overview

This v3 pack reconciles the supplied expanded v2 development specification, the normative Professional annex, the incorporated Spaces companion and the current owner review. It provides current copyable contracts while retaining detailed technical requirements and source provenance. It does not declare any application implemented or independently accepted.

Current owner overrides: V3-OWNER-001, V3-OWNER-009, V3-RECON-002.

## Layouts & visible states

- **V3-AUTHORITY-LAY-01** — The current atlas supplies exact screen/state references. Presentation slides 1–16 are authoritative design references. Later slides are historical inspiration; the older Study concept remains an owner-approved rough layout/behaviour reference under the current wrapper.
- **V3-AUTHORITY-LAY-02** — Each module uses the same section order: prerequisites, overview, layouts, features, entities, permissions/recovery, acceptance, source IDs and technical reading map. A complete Markdown version includes its retained source requirements.

## Features & behaviour

- **V3-AUTHORITY-FEA-01** — Apply current owner directions within their stated scope, then explicit later addenda, normative annexes and unsuperseded baseline clauses. Do not turn an illustrative screenshot into permission to remove an unpictured backend feature.
- **V3-AUTHORITY-FEA-02** — Keep confirmed requirements distinct from proposed visual options and unresolved product decisions. A current mock-up may resolve geometry, not an unrelated unresolved policy.
- **V3-AUTHORITY-FEA-03** — Treat each screen as one observable state, not the whole application. Implement the source-backed workflows, error paths, supported platforms and substantive donor capabilities preserved in the module.
- **V3-AUTHORITY-FEA-04** — The technical source appendices are part of the implementation contract. Concise primary chapters are a reading and design layer, not permission to ignore data models, APIs, compatibility or acceptance gates.

## Canonical entities & persistence

- **V3-AUTHORITY-ENT-01** — Every nonblank source block has a stable ledger ID, original text, destination, status and digest. Replaced wording retains its original form in the history ledger.
- **V3-AUTHORITY-ENT-02** — Current requirements have V3 module IDs; source clauses retain their original IDs. Repeated source text may reference a retained copy without losing its original location.

## Permissions & recovery

- **V3-AUTHORITY-PER-01** — Historical agent instructions, deadlines and deployment process are archived as provenance. They do not create fresh authority to spend, deploy, send messages or access user data.
- **V3-AUTHORITY-PER-02** — Where current scope changes one clause, preserve adjacent security, persistence, platform and backend requirements. Record the amendment narrowly and retain the original.

## Acceptance checks

- **V3-AUTHORITY-ACC-01** — Verify every registered screen maps to an owning module and every source block has a retention or explicit historical status.
- **V3-AUTHORITY-ACC-02** — Verify the copied Spaces chapter works as an implementation unit with explicit prerequisite contracts and source-backed acceptance rather than only links.
- **V3-AUTHORITY-ACC-03** — Do not mark runtime acceptance passed from mock-ups, screenshots, compilation or source accounting alone.

## Exact mock-up and state map

This is a shared contract or backend module. Its visible implementation is exercised by dependent app screens rather than a new standalone application.

## Source clause IDs

SECTION-00, UX27-AUTH-001, UX27-AUTH-002, UX27-AUTH-003, UX27-AUTH-004, SHA-256, UX27-AUTH-005, UX27-AUTH-006, CUI-CONF-001, CUI-THEME-001, APP-AC-001, LAUNCH-001, WRITE-AC-001, WRITE-UX-101, WRITE-AC-101, PRESENT-UX-101, PRESENT-INT-101, CUI-EDITOR-101, STARTUP-UX-101, STARTUP-UX-102, STARTUP-UX-103, PRESENT-AC-101, CUI-PRO-201, CUI-PREF-201, PRESENT-DES-201, PRESENT-INT-201, PRESENT-AC-201, APP-VIS-201, PARITY-001, DESIGN-001, STAGING-001, ACCEPT-001, ACCEPT-002, ACCEPT-003, ACCEPT-004, ACCEPT-005, ACCEPT-006, GOV-001, GOV-002

## Technical reading map

- Authority, scope and implementation reading map — 9to1 Development Specification v2 — Expanded 27 October 2026; Authority, provenance and how to use this edition (part 01) — `technical/authority-01.md` (2,243 words; V2-B0001, V2-B0002, V2-B0003…).
- Authority, scope and implementation reading map — DEFINITIONS AND INVARIANTS; PART 1 — FULL CORE PLATFORM + DONOR-COMPLETE RCVP FOUNDATIONS (part 02) — `technical/authority-02.md` (1,422 words; V2-B0050, V2-B0051, V2-B0052…).
- Authority, scope and implementation reading map — ALV SUCCESSOR — PRODUCT-FIRST EXECUTION DIRECTIVE; USER-AUTHORISED PRIORITY ORDER (part 03) — `technical/authority-03.md` (1,447 words; V2-B0057).
- Authority, scope and implementation reading map — Agentic Update pull-forward; Sequencing rules (part 04) — `technical/authority-04.md` (2,047 words; V2-B0092, V2-B2266, V2-B2267…).

Technical clauses retain APIs, data models, platform, recovery, donor and acceptance details. The technical files are part of this module, not optional background reading. Shared prerequisites mean stable service/interface contracts must be available; they do not require every dependent app UI to be complete. Use the named chunk for the relevant subtask or the complete module file for its full retained contract.


# Retained technical requirements

# Authority, scope and implementation reading map — 9to1 Development Specification v2 — Expanded 27 October 2026; Authority, provenance and how to use this edition (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0001 · v2 lines 2–3

<!-- UX27:BEGIN EDITION-HEADER -->
# 9to1 Development Specification v2 — Expanded 27 October 2026

### V2-B0002 · v2 lines 5–5

Source-preserving text edition. The original Google Doc was not modified. New UX27 requirements are inserted alongside the relevant source sections; all original exported text remains present. Later owner decisions explicitly identified by this expansion supersede conflicting historical assumptions without deleting their provenance. Runtime and acceptance status is not changed.

### V2-B0003 · v2 lines 7–8

Companion files: behavioural-expansion.md (new requirements alone), acceptance-scenarios.md / .json (102 NOT RUN scenarios), requirements.json (stable rule index), adoption-notes.md and source-preservation-manifest.json.
<!-- UX27:END EDITION-HEADER -->

### V2-B0004 · v2 lines 10–11

<!-- UX27:BEGIN SECTION-00 -->
## 0. Authority, provenance and how to use this edition

### V2-B0005 · v2 lines 13–13

### UX27-AUTH-001 — Meaning of the labels

### V2-B0006 · v2 lines 15–15

**O — Owner decision:** a direct requirement in the current conversation or a specifically retrieved recent owner instruction. **B — Baseline elaboration:** detailed behaviour necessary to fulfil an existing V2 requirement. **D — Drafted design default:** a new, explicit value or interaction choice introduced by this requested specification expansion; it is not a measured fact about Notion/AppFlowy and is not falsely attributed to an earlier owner approval. **R — Reference behaviour:** a narrowly documented behaviour in an official reference product or accessibility pattern. **E — Evidence:** a reported or observed defect, with the evidence type identified.

### V2-B0007 · v2 lines 17–17

MUST means mandatory for this expanded edition. A D value supplies a concrete default where the original source is silent; a more specific, compatible owner-approved value takes precedence. Do not stop implementation for every ordinary D choice. Do not silently replace an explicit owner decision with a convenient default. When two explicit requirements genuinely conflict, preserve both in the decision record and resolve that specific conflict without discarding unrelated work.

### V2-B0008 · v2 lines 19–19

### UX27-AUTH-002 — Scope preservation and priority

### V2-B0009 · v2 lines 21–21

**O/B.** The current priority cohort is Haven/core foundations → Spaces/AI continuity → Automations and execution backends → Build/Dev → the full Write/Present/Boards/Canvas productivity suite. Bring forward Files/account/cloud services only where a real dependency requires them. This ordering includes both implementation and acceptance. Later products remain in V2; the amendment does not delete them or downgrade their baseline. Earlier historical deadlines retained in the baseline are historical, not current operating deadlines.

### V2-B0010 · v2 lines 23–23

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

Build retains the specified Dev, Sites and Stacks capabilities. Assistants and Specialists are global systems available through Home and editors; Spaces retains their canonical conversation views. Historical source identifiers such as Haven, CakeOS and Browse are not permission for destructive namespace or data-format renames. Main-convergence instructions in §17 concern development of 9to1 itself, not removal of the Build product’s version-control features.

### V2-B0011 · v2 lines 25–25

### UX27-AUTH-003 — No implementation by analogy alone

### V2-B0012 · v2 lines 27–27

**O/B.** “Like Notion”, “AppFlowy-style”, “mature word processor”, “normal browser” and similar phrases identify comparison products; they are not complete acceptance criteria. For each applicable donor capability, the implementation manifest MUST name its real entry point, interaction states, commands, persisted model, undo/recovery semantics, permission boundary, accessibility behaviour and executable acceptance scenario. The selected donor revision and any justified replacement remain traceable. This expansion supplies concrete contracts for the most failure-prone interactions; the donor-parity manifest still covers additional applicable capabilities rather than treating these examples as the entire donor.

### V2-B0013 · v2 lines 29–29

### UX27-AUTH-004 — Baseline and recent amendments

### V2-B0014 · v2 lines 31–31

**B/O.** Baseline source: Google Doc `17BoC2kINJzizCCA0ffqaeHukTtEtg_1FAWeO6zIGIFY`, read on 27 October 2026; metadata last modified 20 October 2026. The UTF-8 export contains 194,630 whitespace-delimited words and 12,459 lines. Raw export SHA-256: `4d17ab8801ce12367f002df2f93a5d05dc5334ea45e33d421cbdcdcaa0fac277`. These counts describe the retrieved text, not test coverage.

### V2-B0015 · v2 lines 33–33

Recent owner decisions incorporated: rounded filled colourful app icons with theme variants (21 October); selected-tab contrast and contextual composer shelf (22 October); Boards topology, nested pages, bottom Add/user area, branding and theme-independent layout (25 October); app launch, Chromium, Boards input/table/popup/editing/snap failures (25–26 October); main convergence and the current Enter/grip/pen/file-saving clarifications (27 October). The exact pixel geometry of the 25 October image set has not been re-measured here. Recovered explicit topology is authoritative; new dimensions below are labelled D rather than asserted as screenshot measurements.

### V2-B0016 · v2 lines 35–35

### UX27-AUTH-005 — Explicit supersessions

### V2-B0017 · v2 lines 37–37

**O.** Any implementation assumption that Boards requires a Files connection, a linked account, a cloud provider, or a designated “Boards” folder before creating/editing/saving a normal local board is rejected. A user can optionally save an editable `.9to1board` file anywhere the operating system permits. The shared platform’s internally packaged services and normal filesystem permissions remain valid; “no Files connection” does not mean bypass operating-system access controls or maintain an incompatible private document engine.

### V2-B0018 · v2 lines 39–39

**O/B.** A Native Boards page is one seamless editing surface with flow blocks, explicit absolute-layout content and a page drawing layer. It is not a text area followed by an unexplained second “Freeform canvas” application. This does not remove the intentional Canvas PageType, legitimate embedded Canvas artifacts or free drawing in Locked layout.

### V2-B0019 · v2 lines 41–41

### UX27-AUTH-006 — Source and review coverage

### V2-B0020 · v2 lines 43–43

The entire baseline text was retrieved and structurally indexed, including app sections, requirement identifiers and reference-style statements. Detailed expansion concentrated on shared interactions, Boards, host/browser integration, Spaces, persistence, priority applications and their cross-app dependencies. Later-product contracts below add concrete interaction coverage while retaining their much larger baseline requirements. This is not a claim that every donor revision was run, every historic screenshot was recovered, every one of the baseline’s 194,630 words was independently rewritten, or any product passed acceptance.

### V2-B0021 · v2 lines 45–50

V3 editorial amendment: V3-OWNER-004, V3-RECON-002. Original wording is preserved in the replacement ledger.

The current recording is evidence of the installed UI, not the intended design. Sampled frames across its 240.4-second duration show a large blank text region, repeated “Freeform canvas” empty-state text and an embedded white canvas; pen and grip failures are also directly owner-reported. Input-event traces and source-level causes were not available from those frames alone. Never promote a screenshot-derived hypothesis to a reproduced code diagnosis.
<!-- UX27:END SECTION-00 -->
9to1 V2 — CUI, THEMING AND REAL-PRODUCT CONFORMANCE CORRECTION
Effective 11 October 2026. User-authorised clarification and refactor contract. The original V2 document remains authoritative; this is not a replacement specification. The canonical CUI and Write sections and the application acceptance rules below have been tightened in place.
CUI-CONF-001–004, CUI-THEME-001–004, APP-AC-001–009, LAUNCH-001–006 and WRITE-AC-001–003 are mandatory. A renamed AXAML surface, native-theme workaround, disconnected test harness, missing donor functionality or attractive but unusable shell cannot satisfy these requirements. Launch/start screens must be distinct product experiences, not headers or introductory sections stacked above the working app; the Launch Screens section defines their identity, transitions and acceptance. Preserve mature engines and working functionality while correcting their public CUI integration.
16 OCTOBER 2026 — Browse becomes the primary app branded “9to1”; merge the previous main shell into it. Tabs open websites/search or real native 9to1 apps. Every actual app retains its standalone executable. A Home-icon tab exposes Apps, Dashboard and Models/Discover sidebar views; New Tab exposes them through a compact icon rail and adds a New Tab view. Preserve Home Core, the New Tab canvas, canonical app data and all compatible requirements. Historical delivery target (17–18 October 2026, Europe/London): archived; not a current deadline. Prioritise this host integration within the existing delivery plan; 9to1OS remains separately staged. Completion requires the retained full scope and independent acceptance.

### V2-B0022 · v2 lines 53–57

9to1 V2 — 12 OCTOBER 2026 WRITE UI/UX ACCEPTANCE ADDENDUM
Owner-approved normative changes to Write are recorded in Productivity Apps → Write → Acceptance requirements, IDs WRITE-UX-101–120 and WRITE-AC-101–109. They require a rich contextual floating toolbar, more visible everyday formatting, a refined contextual Properties inspector, usable Sections/sidebar, genuine document interactions, a polished launch screen, CUI compliance and repeatable real-runtime visual/functional verification. Preserve the existing Home/backstage, persistent +/Eye/[…], no-permanent-ribbon rule, genuine LibreOffice Writer/LibreOfficeKit donor parity and Part 1 RCVP / Part 2 Full staging. All pre-existing specifications and acceptance criteria continue to apply.
9to1 V2 — 12 OCTOBER 2026 PRESENT EDITOR, CUI UX AND STARTUP ACCEPTANCE AMENDMENT
STATUS AND AUTHORITY
This is a user-authorised normative amendment to the existing V2 specification, not a replacement. It supplements the canonical Present, CUI, Home/startup, shared productivity, donor-conformance and APP-AC sections. It applies to Present's current RCVP implementation wherever the requirement is essential to a mature presentation editor, and to Full where explicitly dependent on a later 9to1-specific extension. Existing donor parity, security, accessibility, APIs, persistence and cross-app requirements remain intact. The active four-part delivery order is unchanged. More specific compatible requirements below control the described Present UI/interaction behaviour; other apps inherit shared CUI components and quality rules, not Present's exact layout. Observations of UI defects are acceptance evidence, not proof of particular source-code root causes.

### V2-B0023 · v2 lines 60–61

A. PRESENT HEADER, WORKSPACE AND DIRECT EDITING UI
PRESENT-UX-101 — Three integrated header tiers. The normal Present editor MUST show (1) a compact document/application header with Home navigation, Present identity, editable document name, truthful save/autosave state (including unsaved, saving, saved and error/conflict), appropriate history/comments/collaboration controls when available and a prominent Present action; (2) a conventional compact menu row with File, Edit, Insert, Slide, Arrange, View and Help, each opening a real usable dropdown; (3) exactly ONE persistent primary editing toolbar below the menus. These three rows form one coherent shell but the menu row MUST remain ABOVE the toolbar. Document identity and save state form a deliberately aligned cohesive group; header controls and top-right Save/History/Present MUST share predictable vertical alignment and spacing. Never introduce duplicated scattered permanent command strips or a second ribbon.

### V2-B0029 · v2 lines 79–80

B. PRESENT POINTER HIERARCHY AND MANIPULATION ENGINE
PRESENT-INT-101 — Separate object/content/interaction layers. The engine MUST distinguish each persistent slide ObjectID (e.g. text-box parent object, shape, image), its actual editable/rendered child content, and transient interaction adorners/selection overlay. Text content MUST be edited as the content of its parent text box, not treated as an indistinguishable competing flat object when selected. Keep document identity and hierarchy canonical, object stacking order deterministic and controls outside persisted authored data. Inspect whether current issues originate in the data model, hit-testing or event routing; the observed interaction failures do NOT themselves prove the stored model is flat. Do not unnecessarily rewrite correct donor/document engines.

### V2-B0036 · v2 lines 101–102

C. FLOATING INTERACTION SURFACES AND VISUAL LANGUAGE
CUI-EDITOR-101 — Shared floating menu contract. First-party File/Edit/Insert/Slide/Arrange/View/Help menus, + Insert menu, shape gallery, font dropdowns, colour popovers, toolbar overflow, right-click context menus and related tool surfaces MUST follow the same CUI-driven design language. Use dark floating surfaces with generous rounded corners, controlled elevation/soft contrast, comfortable but proportional padding, legible typography, icons where helpful, tidy section separators, shortcut labels and correct hover/pressed/focus/selected/disabled states. The dark rounded permissions-menu screenshot supplied during acceptance is the visual style reference, NOT its permission content. Short menus may be compact; this is not a requirement to oversize all popovers. Avoid native Avalonia-styled dropdowns or OS-default chrome in first-party UI.

### V2-B0041 · v2 lines 117–118

D. REFRESH, STARTUP AND CROSS-APP PERFORMANCE
STARTUP-UX-101 — No prolonged blank refresh. The observed Present refresh shows a white 'Opening 9to1...' screen for approximately 5–10 seconds. MUST instrument and correct the underlying delay, not merely hide it. Record cold start, warm start, page refresh, within-app navigation, document reopen and loading of larger presentations separately. Measure shell-visible time, document-visible time, time-to-interactive, stage timings and variability. Investigate repeated CUI/Avalonia/service initialisation, blocking UI-thread work, redundant layout or rendering, file/deserialisation, font/assets, state restoration and unnecessary requests based on actual trace evidence.

### V2-B0042 · v2 lines 121–121

STARTUP-UX-102 — Theme-safe progressive startup. The earliest host/shell render MUST use the appropriate CUI theme with no avoidable white flash. Render the real navigation/editor shell promptly where feasible, then contextual truthful loading states for document-dependent regions; do not force full-screen blocking for a small delayed component. Use correctly persisted theme preference/fallback per CUI-THEME rules, meaningful retry/error/recovery states and no fake progress or indefinite splash. Do not expose a native themed interim shell.

### V2-B0043 · v2 lines 124–124

STARTUP-UX-103 — Provisional performance budgets. Target initial visible shell under 1 second, typical warm/reopened document under 2 seconds and ordinary basic editor interactivity under 3 seconds on the specified test hardware/runtime, where practical. These are engineering acceptance goals, not guarantees or previously measured facts. Any missed target requires baseline/after metrics, bottleneck and environment documentation, not invented performance claims. Avoid performance gains through correctness loss, data staleness, uncontrolled caching or skipped security/restore checks.

### V2-B0044 · v2 lines 127–128

E. DELIVERABLES, CONCURRENCY AND ACCEPTANCE
PRESENT-AC-101 — Do not replace earlier assignments. The UI/theme/toolbar/menu worker and interaction/hit-testing/viewport worker are complementary lanes; any separate CUI/startup audit is another bounded lane. They MUST NOT interrupt, replace, reset, overwrite or silently invalidate one another's in-flight work. Inspect current commits and dirty changes first, allocate non-overlapping file/contract ownership, use isolated worktrees/branches and typed integration seams; coordinate unavoidable shared edits with checkpoints. Preserve previous working functions and donor capabilities. Do not treat receipt of a prompt as proof a change was implemented; track REQUESTED, IMPLEMENTED, TESTED and VERIFIED separately. The documented four-part delivery plan and app owners remain authoritative.

### V2-B0048 · v2 lines 140–140

END OF 12 OCTOBER 2026 PRESENT/CUI UX AMENDMENT — ACTIVE FOUR-PART DIRECTIVE CONTINUES BELOW

### V2-B0049 · v2 lines 143–147

V3 editorial amendment: V3-RECON-002. Original wording is preserved in the replacement ledger.

9to1 V2 — 12 OCTOBER 2026 SHARED PROFESSIONAL THEME, PRESENT VISUAL REFERENCE AND ACCEPTANCE ADDENDUM
STATUS: NORMATIVE INCORPORATION BY REFERENCE. The linked annex is part of this V2 specification, effective 12 October 2026, and consolidates the owner's approved CUI Professional theme, Present visual reference, genuine interaction, Home account-wide themes/accent overrides, custom theme creator and real browser acceptance requirements. It defines CUI-PRO-201–204, CUI-PREF-201–205, PRESENT-DES-201–207, PRESENT-INT-201–202, PRESENT-AC-201–204 and APP-VIS-201. These apply alongside the prior 12 October Present/CUI amendment and existing donor/APP-AC gates. Preserve existing functionality and the four-part plan; this is not an interface redesign from zero.
Full normative annex: https://docs.google.com/document/d/12iLllJXQl_YdGM1J-MTbZuAyrhZN-oTpvdedRfo47nY/edit
9to1 V2 — ACTIVE FOUR-PART DELIVERY AND VISIBLE-CONVERGENCE DIRECTIVE
Effective 16 October 2026. Historical delivery target (17–18 October, Europe/London): archived; not a current deadline. Browse is the primary 9to1 host; integrate its native app tabs, merged Home, shared Home/New Tab sidebar and standalone app support as immediate core dependencies. Preserve the full V2/original/incorporated scope and all stage/acceptance requirements. This supersedes conflicting main-shell placement, navigation and delivery order only. Assess current source/backlog before estimating feasibility. 9to1OS remains separately staged.


# Authority, scope and implementation reading map — DEFINITIONS AND INVARIANTS; PART 1 — FULL CORE PLATFORM + DONOR-COMPLETE RCVP FOUNDATIONS (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0050 · v2 lines 150–153

1. DEFINITIONS AND INVARIANTS
RCVP means Release Candidate Viable Product, not a prototype or stripped-down MVP. An RCVP app has a real accessible CUI-authored/controlled product interface, usable actual donor-backed functionality, supported file formats, launch/open/create/edit/save/close/reopen/export and relevant playback/rendering, canonical persistent identities, proper permissions, security, error handling, and an authentic build plus interactive smoke and independent RCVP acceptance. ALL applicable baseline functionality from its designated mature donor(s) is available through the running app; donor source merely being downloaded, a placeholder engine or an isolated source test does not qualify. 'Ported to CUI' means present donor features through the canonical CUI user experience and typed adapters while retaining suitable mature donor engines and their capabilities; do not rewrite Firefox/Chromium, LibreOffice, Rnote or other engines in markup. Any unavoidable incompatibility requires an explicit owner-reviewed recorded defect, not a fictional PASS.
RCVP deliberately defers only NEW 9to1-specific enhancements beyond the donor-complete baseline and nonessential expansion features to the later Full stage. Essential Home/CUI/Dulche dependencies, privacy, accessibility, durability, recovery, integration, licensing and security needed for safe real usage are NOT deferrable. Where a browser version is applicable to an app's RCVP, it must also be genuine, usable and backed by canonical providers; an explicitly documented authoritative web exemption (for instance a browser-within-browser exception for Browse) still applies. Track native and browser gates separately.
Full means the complete applicable V2/original/post-release functionality, full approved donor parity, all 9to1-specific extensions, relevant native and web versions, cross-app integration, product quality, and independent requirement-by-requirement acceptance. An app may be RCVP-VERIFIED but not FULL-VERIFIED. Engineering states (WORKING, CHECKPOINT, COMPLETED, VERIFIED, STALLED, BLOCKED) remain distinct from stage/product acceptance. An unexecuted or fixture-only test cannot be counted as full acceptance.

### V2-B0051 · v2 lines 156–159

V3 editorial amendment: V3-OWNER-002. Original wording is preserved in the replacement ledger.

2. PART 1 — FULL CORE PLATFORM + DONOR-COMPLETE RCVP FOUNDATIONS
FULL, including genuine launchable required native AND web counterparts: Dulche (model/inference routing, native tools, Skills, Plugins, MCP, agent/runtime/action/task/permission infrastructure and privacy-safe cross-app memory/context with explicit consent), CUI (complete canonical language, rendering, authoring controls, binding, platform integration and developer tooling), common secure identity/authorisation/storage/application APIs and foundations necessary for Part 1; Home; Spaces (including complete Study/agentic workflows); global Assistants, configured Specialists and the shared Agents/Subagents/task-run capabilities; Dev (the FULL development application, not a reduced editor); Files (including its mandatory canonical storage/revisions/permissions/features); and required web versions of Home, Spaces, Assistants, Dev, Files with real login, cloud persistence/sync and shared canonical identity. Shared services needed to make these truly usable are Part 1 FULL even if their separate administrative interface is scheduled later. 'Agents' as a capability and the global Assistants/Specialists experiences must not be conflated. Do not substitute a simulated agent for a real workflow.
RCVP in Part 1: Write, Present, Boards, Canvas and Browse. Integrate the declared donor functionality end-to-end via actual engines and CUI interfaces: real word processing, rich presentations and playback, flexible notebooks/boards, drawing, and real web rendering/navigation respectively. Where web counterparts are applicable, ship genuinely useful matching RCVP browser surfaces with canonical identity and persistence; Browse retains its documented web exemption. Advanced original 9to1-only enhancements above those donor-capable baselines are staged for Part 2. The donor baseline is a high standard, not a collection of demonstration buttons.
Part 1 release condition: a routinely launchable Windows/development build, working core apps and real RCVP donor apps, functioning appropriate web routes, live create/edit/save/reopen journeys, meaningful security/recovery evidence and separately recorded independent stage acceptance. Produce and continuously refresh previewable integrated builds DURING Part 1 rather than waiting for every Part 1 item to be independently verified. Label provisional/experimental work truthfully; unverified apps cannot masquerade as release-ready.

### V2-B0052 · v2 lines 162–164

3. PART 2 — COMPLETE THE FIRST FIVE + DATA/SITES; RCVP OTHER APPLICATIONS
Upgrade Write, Present, Boards, Canvas and Browse from RCVP to FULL V2, including all additional 9to1-native features, AI/Dulche controls, complex editing, canonical cross-app artifacts, browser parity where required and full applicable independent acceptance. Deliver Data (complete spreadsheets, sources, analysis and specified Data applications) and Sites (full web authoring, real publishing/hosting, permissions and integrations) to FULL, including their appropriate browser experiences and cloud dependencies.
Build RCVP for every remaining in-scope application and service with an applicable genuine donor/base-product capability, respecting donor provenance and licences: Picture, Wave, Motion, Forms, Planner, Mail/Connect, Cards, Automations, Terminal, Stacks, Shelf, Translate, Games, Mini Computer, additional AI/developer/utilities and any other apps enumerated anywhere in the authoritative source. Prioritise user-visible applications that can be completed independently. Where no donor exists, RCVP means a viable real product-category baseline with genuine end-to-end functionality, not a mock or artificial donor claim. Document each app's full future delta. Mini Computer and other explicitly exempt products need not fabricate impractical hosted browser capability.

### V2-B0053 · v2 lines 167–169

4. PART 3 — EVERY-APP RCVP CONVERGENCE THEN FULL NON-OS ECOSYSTEM
First reconcile the exhaustive application roster and demonstrate that EVERY applicable non-OS app has achieved its truthful RCVP baseline, including any missed in Part 2. Then upgrade ALL remaining non-OS apps/services, shared workflows and required web versions to FULL V2 acceptance. Include Link (Flow and Binder), expanded Files collection paradigms, Cards/Study, specialised AI Studio, all applicable retained legacy/source-donor capabilities, required first-party API parity, business/account/integration capabilities, automation, and accessible mobile/web equivalents where required. No orphaned or unclassified application is omitted. All non-OS global and cross-app specification obligations must be either FULL-VERIFIED or documented genuinely BLOCKED/NOT RUN; no unverified scope counts as completed.
Part 3 closes the full application/product suite. It is not a claim that 'every app already has RCVP' merely because Part 2 attempted that work; inventory and independently verify it before promoting the stage.

### V2-B0054 · v2 lines 172–174

5. PART 4 — 9to1OS FULL OPERATING SYSTEM DELIVERY
Implement, integrate, package and independently accept the 9to1OS operating-system product derived from the specified maintained Ubuntu/GNOME donor baseline. Preserve applicable mature OS hardware, desktop and accessibility behaviours while delivering the full CUI-native shell, Go/start/search/action surface, taskbar/layers, windows/desktops, Quick Settings, system utilities, accounts/admin policy, updates/rollback, package management, compatibility, diagnostics, default first-party applications, installation images, recovery, security and OS-specific cross-platform/system acceptance. The authoritative 9to1OS and OS integration sections remain mandatory.
Part 4 defers OS DISTRIBUTION and OS-specific polish, not the Part 1 Windows/Home application runtime, CUI/Dulche infrastructure or native security/platform contracts. Earlier apps must remain buildable and usable on their already supported hosts. OS donor preservation and explicit platform-specific acceptance are required; do not substitute a themed desktop mockup for an operating system.

### V2-B0055 · v2 lines 177–182

V3 editorial amendment: V3-RECON-002. Original wording is preserved in the replacement ledger.

6. EXECUTION, SAFE DELEGATION AND VISIBLE BUILDS
Use Part 1 as the primary critical path. Permit safe, independently owned work on later stages in parallel without draining required Part 1 integration resources. Advance each product once an integrated meaningful release checkpoint is achieved; independent full verification continues as a separate gate. Stage dependencies unlock only the minimal required source/API seam, not total completion of every unrelated app. Shared contracts have a single owner and stable API; other workers integrate through documented typed interfaces instead of duplicating stores, runtime or permission brokers.
Historical named worker/PR-path assignment is archived in the original source ledger. Implementation must use current explicit ownership and preserve existing work; this paragraph does not assign present workers, sessions or credentials.
Continuous tangible convergence is a mandatory process outcome: maintain an installable/launchable Windows development build and accessible genuine web test deployment as soon as possible; refresh at meaningful validated integration checkpoints. Show all working functionality as it converges rather than requiring users to wait for total release acceptance. Every preview identifies source/artifact SHA, available features, missing functionality, experimental status and supported platform. Do not silently publish externally or incur unapproved expenses. A working demo or source-only unit-test green run is not product acceptance.
Development cycle per vertical user journey: select uncovered acceptance requirement; reuse mature donor and already-tested code; implement the complete actual UI/engine/storage/API workflow; run changed-graph build and focused authentic smoke; integrate reviewed source into the release candidate; independently inspect/accept the exact artifact; continue immediately. Use pinned revision-indexed requirement manifests and incremental inspection; do not reread the entire specification or run exhaustive cross-suite validation on every tiny edit. Run full native/web regressions at significant release and integration gates. Protect security, privacy, permissions, accessibility and data fidelity throughout. Prioritise completed usable journeys per hour over token output, unbounded audits, duplicative test scaffolding or vanity test counts.
Track per application: assigned part; RCVP requirement manifest; Full requirement manifest; explicit donor baseline; owner/branch and source identity; running native/web artifacts; functional smoke; security, persistence and interoperability results; independent RCVP acceptance; independently verified Full acceptance; remaining blockers. Do not report an arbitrary percentage or projected deadline as proof. Claims require evidence. Once every application is Full-verified and 9to1OS Part 4 is Full-verified, the entire plan is complete.

### V2-B0056 · v2 lines 185–185

END OF ACTIVE FOUR-PART DIRECTIVE — HISTORICAL EXECUTION CONTEXT AND FULL SPECIFICATION, INCLUDING THE IN-PLACE CONFORMANCE CORRECTIONS, CONTINUE BELOW


# Authority, scope and implementation reading map — ALV SUCCESSOR — PRODUCT-FIRST EXECUTION DIRECTIVE; USER-AUTHORISED PRIORITY ORDER (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0057 · v2 lines 188–230

V3 editorial amendment: V3-OWNER-005. Original wording is preserved in the replacement ledger.

ALV SUCCESSOR — PRODUCT-FIRST EXECUTION DIRECTIVE
Historical successor execution context for ALL 9to1 applications. Its sequencing and interim acceptance instructions are subordinate to the active four-part directive and 11 October 2026 conformance correction. The full product specification follows.
STATUS: HISTORICAL EXECUTION CONTEXT; not the current delivery order. The active four-part directive controls current sequencing, and APP-AC governs product acceptance. Retain the compatible source-preservation, safe handoff and implementation-quality instructions below. All original functional, security, privacy, cross-platform and donor-parity requirements remain mandatory. This document does not itself stop or launch any Codex task.
1. USER-AUTHORISED PRIORITY ORDER
1 — Frameworks: canonical CUI, rendering, shared product engines, action/event/API routing, identity/permission, testing and required platform foundations. Historical framework names do not authorise alternate application-markup formats or public donor-control aliases.
2 — Home: genuine shell, app/service lifecycle, login, permissions, native launch, ownership, routing and package management.
3 — Spaces: the complete persistent AI workspace and required integrated product workflows.
4 — Dev: real developer workspace, agents, source/project operations, permissions, tasks, persistence and recovery.
5 — Assistants: full persistent named Assistant identities, configurations, memory/context, tasks, tools and cross-conversation continuity; not saved prompts.
6 — Files: canonical artifact/file identity, real storage, revisions, safe authorised access, app interoperability and recovery.
7 — Sites: the actual website design, preview, publishing and managed-site product.
8 — Web versions of Home, Spaces, Dev, Assistants, Files and Sites PLUS real cloud storage: genuine browser applications with secure sign-in, canonical state, permissions, server-side storage/sync and durability. Shared framework web support is prerequisite, not a substitute for the products.
9 — Write: the complete usable document editor and required interoperability.
10 — Present: the complete usable slide/presentation editor and playback workflow.
11 — Canvas: the complete drawing/visual canvas and required manipulation workflows.
12 — Boards: the complete board/project workspace and required operations.
13 — Cards: the complete rich flashcard and review product, including private reviews and Study linkage.
14 — Web versions of Write, Present, Canvas, Boards and Cards: genuine application parity, not generic browser shells.
15 — All remaining applications, including Wave, Motion, Picture and every other application in the original authoritative roster.
ORDER RULE: This order governs the main critical path and broad engineering resources. Move the main focus onward only after a genuine integrated CHECKPOINT for the preceding stage; full acceptance still follows. Safe independent work may continue only if it doesn't delay critical dependencies or cause source collisions. Preserve all verified prior work. Do not silently reorder priorities, delete requirements or treat remaining apps as cancelled.
2. RESTART THE DEVELOPMENT RUN SAFELY
Retire the old whole-project development worker after immutable handoff; do not merely replace the Motion worker. Before stopping, preserve and independently verify all uncommitted source/overlays, HEADs/submodules, current private Google Drive recovery archives, existing task ownership, receipts, snapshots, UI evidence, actual packages, prior passing tests, failures and unresolved source. Preserve hashes, sizes, safe file paths and restore instructions. Do not delete, move or overwrite historical evidence, working trees or the original specification.
Only the authorised user or connected task-owner controls can actually terminate the old Codex Cloud task. This specification cannot stop it. Avoid concurrent writers to the same source ownership. Once the old worker is genuinely stopped, start a successor ALV 6.1 UltraFast worker if that tier is available in the actual UI; do not falsely claim to have set an unavailable runtime speed. Restore current source and ownership, reconcile private successors and retain passing work; do not restart the repository from zero.
3. ACCEPTANCE: REAL PRODUCT FIT, NOT A CHECKLIST OF BUTTONS
IMPLEMENTED means a normal user can complete the defining workflow in the REAL running application, using meaningful user content and the canonical providers: create/open, interact/edit, view result, save, close, reopen and export/use the expected artifact. A class, API stub, mock dashboard, button grid, developer panel, partial donor re-skin or unit-test-only component is not a completed feature or application.
A video editor without an actual playable viewer, imported media, usable multitrack timeline and output/export fails product fit. An audio workstation without real waveform, monitoring/playback and editable multitrack workflow fails. Write without genuine document formatting/layout and saved editable content fails. Present without real slide editing and slideshow fails. Canvas without drawing/editing fails; Boards without real board operations fails; Cards without real rich flashcards/review fails. Reusing a mature donor MUST preserve its relevant functionality rather than replace it with a handful of controls.
Layout quality is a functional gate: primary work surfaces must be visible, proportionate, resizable or navigable, and accessible. Scrolling that technically reveals clipped controls is not proof of a usable interface. No acceptance for a nonfunctional preview or an app that acts like a different software category.
4. FAST VERTICAL-SLICE DELIVERY AND CONTINUOUS VISUAL QA
Build each product as complete end-to-end workflow slices with real data, canonical providers, permissions, renderer, persistence and resulting output, instead of long sequences of isolated commands/UI followed by late integration. The first runnable slice must genuinely resemble and function as the advertised product; it is a CHECKPOINT, not full VERIFIED status. Complete every remaining original feature subsequently.
After every meaningful user-facing change, run a targeted cached build and authentic smoke on the affected running product, using keyboard/pointer and representative content. Inspect its primary work surface, semantic state and actual screenshots; test relevant edit/save/reopen/output and reject clipping or unreachable panels immediately. Use 1200x900 and 800x600 where applicable, plus the declared narrow/wide/mobile and scaling baselines. Framework/theme integration and release gates cover all eight current built-in CUI Themes (Professional, Glow, Playful, Frosted Glass, Bubble, Metallic, Retro and Western), explicit Cinematic compatibility, custom/user_preference inheritance, applicable light/dark appearances, contrast and reduced motion. A focused edit need not repeat the entire matrix when exact-revision dependency evidence is still valid. CUI is the sole public application UI contract; reuse bundled Montserrat, shared CUI components and mature engines.
Visual smoke runs must exercise the real viewer, timeline, canvas, document, editor, inspector or equivalent primary surface, not merely a test window. Preserve exact source/build identity, screenshots, attempted actions, results and any defect. Fix usability failures before expanding the same broken workspace.
5. PARALLEL IMPLEMENTER, SMOKE AND ACCEPTANCE LANES
Use one coordination owner with a dependency map, immutable source/version references and explicit non-overlapping path ownership. When capacity allows, separate implementation into real UI/workspace, canonical backend/provider, and independently runnable integration slices. Smoke Tester installs/launches the actual artifact and performs authentic workflows with real data as soon as slices are runnable; it records reproducible failures. Independent Acceptance Tester checks the product category, all applicable spec requirements, donor parity, integration, accessibility and real supported platforms. Minor bounded fixes may stay in the QA lane; major mismatches return upstream.
If Codex Cloud cannot directly coordinate persistent parallel workers, use separate user-authorised tasks, PRs, SHA-pinned Drive recovery packets and evidence handoffs. No fictitious shared filesystem or agent-to-agent chat. Never mark smoke or acceptance passed from the implementing agent's assertion alone.
6. SPEED, USAGE AND TRUE PROGRESS
Reuse mature approved frameworks, media pipelines, document engines and donor implementations rather than duplicating them. Maintain warm cached test/build runners, run focused changed-graph checks continuously and broader integration tests at meaningful checkpoints, reuse exact-source evidence where actually applicable, and avoid re-reading enormous specs on every turn. Measure implementation lead time, compile/test wait, rework, real acceptance yield and blockers. Optimise usable workflows delivered per hour, not raw lines of code, button count, screenshot count, test count or number of agents.
Do not perform unsafe data cleanup, unapproved model/provider spend, source overwrites or public deployments to gain speed. Resolve competing owners and bottlenecks early. Keep short, durable progress updates and accurate expected remaining effort rather than unsupported guarantees.
7. VERIFIED DELIVERY CONTRACT
Keep the progress statuses WORKING, STALLED, BLOCKED, CHECKPOINT, COMPLETED and VERIFIED with their exact prior meanings. Separately show actual source built, core workflow smoke passed, Windows package, web staging, integration and full product acceptance. A staged or compiled application is NOT accepted. Missing checks are NOT RUN, not passed; later revisions do not inherit older evidence without proven unchanged inputs.
Acceptance must cover the FULL original functional requirement list; real auth/permissions, data fidelity and recovery; normal user workflows; CUI/appearance/accessibility; pointer/keyboard/touch; honest unsupported OS capabilities; real native/web packaging; required API parity; provider integration; full donor parity; and project-specific export/output. A failed product-fit gate blocks implemented/verified stage promotion even if hundreds of unit tests pass.
Every release candidate must carry source/package SHA, exact runtime/SDK/dependency set, version, test artifact identity, screenshots with genuine content, relevant device/resolution/theme evidence, unresolved failures and next-owner handoff. Report priority phase completion/staging separately, including both platform counts. No false claims about private handoffs, model choice, worker termination or acceptance.
END OF NEW DEVELOPMENT DIRECTIVE — FULL ORIGINAL SPEC CONTINUES BELOW
The rest of this native document remains an unchanged copy of the existing authoritative specification.


# Authority, scope and implementation reading map — Agentic Update pull-forward; Sequencing rules (part 04)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0092 · v2 lines 265–281

V3 editorial amendment: V3-RECON-002. Original wording is preserved in the replacement ledger.

Agentic Update pull-forward
The Agentic Update defined in 9to1 Post-Release Updates is pulled forward into the current development push and is no longer gated on initial launch. The objective is to establish a production-capable agentic harness early enough that cloud models and eligible local models can execute the same durable work through the same typed app/tool interfaces, checkpoints, permissions, task state and recovery semantics. Cloud quota exhaustion, provider unavailability and equivalent model-routing failures MUST be recoverable routing conditions where an authorised eligible fallback exists. Switching models MUST resume the same canonical Task/Run rather than start a disconnected replacement chat, discard valid checkpoints or lose accepted work.
Sequencing rules
Priority is dependency-first, not a requirement to stop healthy already-running independent work. Work required to unblock a higher-priority layer MAY proceed in a lower layer, and verified work MUST be preserved. However, new broad completion effort MUST NOT skip ahead to lower-priority apps while mandatory framework, Spaces, Dev or Home prerequisites remain unfinished, except for genuinely independent work that does not consume critical capacity or create integration churn. Each priority stage SHOULD reach at least an integrated CHECKPOINT before the next stage becomes the main focus; final VERIFIED status still requires the complete acceptance contract.
Delivery order update — 5 October 2026: staging first
Historical 6 October 2026 delivery-schedule paragraph is archived in the original source ledger. The adjacent substantive native/web, real-login and acceptance requirements remain active; no expired deadline or new live-deployment authority is imposed.
Clarification — working apps and real login, not merely a protected preview
The staging milestone requires the actual in-scope Windows and browser applications with their required functionality implemented and connected, including genuine application sign-in and session handling. Deferring deep acceptance is not permission to defer implementation, substitute mock or partial applications, or call a launch-only shell complete. Short staging checks are not proof of full acceptance; known missing or broken required app/login functionality remains delivery work. Ready apps may be staged progressively while the remainder is finished.
The separate public-facing main/marketing website, its finished public content/design and production WordPress migration/cutover need not be completed for this staging milestone if they are not ready. This does not defer the web apps themselves, working account/login routes, required API/backend services, or functionality inside the Sites authoring app. Keep the current public website unchanged unless a separate instruction authorises changing it.
An outer server/edge login gate is additional protection for private staging, not a replacement for genuine CAKE ID/application authentication. An admitted tester must be able to use the application's real login and authorised account-backed workflows; no fake user, shared administrator session, authentication bypass or inert login page counts. Isolated test accounts/data and existing permissions continue. Complete the working app set on both surfaces first, then concentrate on exhaustive acceptance of those exact builds. Label builds STAGED — DEEP ACCEPTANCE PENDING until that later verification is complete.
Jacob's latest explicit decision is to prioritise staging the full in-scope applications on BOTH the web, behind gated login access, and Windows. Deep acceptance follows once all applications are staged. Acceptance is NOT dropped, weakened or marked passed by staging. This sequencing instruction supersedes earlier directions that made complete app/pass/platform acceptance a prerequisite for private staging or candidate downloads. The missed 07:00 milestone remains history; the immediate delivery target is 11:00 Europe/London today, Monday 5 October 2026, for the working app set on Windows and gated web with real app login, not an unsupported claim of final release acceptance.
Scope is the current launch specification, including incorporated Lunar Eclipse requirements, reconciled with 9to1 Post-Release Updates. The Agentic Update from that roadmap is explicitly pulled forward into the current development push under the priority order above; it MUST NOT be deferred merely because older roadmap wording described it as post-launch. Other expressly post-release additions remain post-release unless they are a necessary dependency of the pulled-forward framework/agentic work or a later explicit instruction brings them forward. Existing baseline launch requirements remain launch requirements and MUST NOT be silently deferred. Record exact app/platform applicability rather than blindly reusing the historical 30-app denominator.
Stage real integrated applications and their existing functionality, not replacement shells, mock dashboards or source-only archives. Publish ready apps progressively while completing the rest. Supply usable Windows packages with their required dependencies and a protected web deployment with working navigation. Before private staging, perform a short build/launch/main-workflow smoke check and verify the actual access boundary and isolated test-data configuration. For an editor, include one basic save/reopen check with disposable data. These are staging checks, not the complete acceptance programme. Record remaining defects clearly; do not label an app complete when its principal workflow is still broken.
Web staging must restrict access to Jacob and explicitly authorised testers at a server/edge boundary protecting application routes and private APIs, not just hide the UI behind a client-side login. Use isolated staging accounts/data/storage and no production secrets or customer-data migration. Windows staging uses separate test profiles/data and the existing supported trust mechanisms; do not bypass Home authorisation or disable OS security. Keep paid/live billing, external mail/inference and production cutover disabled unless separately authorised. Block or isolate only an unsafe/unusable affected operation; do not make unrelated deep acceptance suites prerequisites for the entire staging set.
Team A owns Windows/native/runtime work, Team B browser applications, and Team C the single integration/deployment and handoff path. Preserve reviewed work and use the existing private delivery route. Do not start another broad worker rollout or repeatedly reconstruct/retest unchanged evidence. Batch focused checks, return only useful failure summaries, and preserve sufficient execution capacity for packaging, deployment and handoff rather than spending it all on repeated audits.
After the web AND Windows staging set is available, prioritise deep acceptance on those exact versions: full functional/donor parity, accessibility, permissions/privacy, recovery/concurrency, resource and platform matrices, provider integration and release signing/install requirements. Keep every required test and every failure/not-run state. Existing independent validation may continue when it does not delay staging; do not terminate healthy running tests merely to reorder the queue. Production/RC acceptance and acceptedHead remain separate from staged status.
Report web staged n/N, Windows packages available n/N, apps with both applicable staging surfaces n/N, and acceptance verified separately. Provide real URLs/downloads, source/package/deployment identities, known issues and a resumable handoff. Keep artifacts immutable and version later fixes. If execution ends before deep acceptance, preserve the accessible staging builds and pending tests, explicitly unverified; do not claim background work continues or that final release acceptance passed.

### V2-B2266 · v2 lines 13345–13346

9to1 V2 — AUTHORITATIVE AMBIGUITY-RESOLUTION AND ACCEPTANCE CONTRACT (11 OCTOBER 2026)
STATUS: Normative, owner-approved clarification. Applies to all applications, CUI, Dulche, RCVP, Full, web and native releases. Supersedes inconsistent older wording on these specific subjects without removing unrelated requirements. No developer or acceptance agent may self-authorise exceptions. The canonical component registry and approved app feature manifests are required implementation artifacts; absence is a blocker, not permission to improvise.

### V2-B2267 · v2 lines 13349–13349

PARITY-001 — Complete donor functionality. Preserve and make accessible every feature of each version-pinned designated donor, except: (A) third-party paid/subscription/licensed functionality that cannot legally be provided for free; (B) proprietary donor AI surfaces, whose unique permitted capabilities migrate into Dulche and whose duplicated interfaces may be removed; or (C) features irreconcilably conflicting with 9to1's direction after a documented attempt to adapt or replace them (e.g., a fixed traditional animation workflow incompatible with Present's AI/node-based approach). Technical difficulty, rarity, time pressure and developer preference are not exceptions. Prefer equivalent capability through the new experience; UI parity is not required. Each exception needs evidence, classification, impact, proposed preservation/migration/replacement/removal and explicit owner approval. No approval means BLOCKED, not PASS. Maintain per-donor versioned feature inventories with user-visible workflows and test evidence.

### V2-B2270 · v2 lines 13358–13358

DESIGN-001 — Category-derived layout. Before implementing an app, document an evidence-based comparison of 3–5 established applications in its category, including their common workspace hierarchy, navigation, command grouping, tool placement, canvas/document area, properties panels, contextual controls and creation/open workflows. Write/Present/Data should benchmark familiar Docs/Slides/Sheets-style layout conventions, without copying branding or visual design; Picture should compare relevant capabilities of Paint, Photoshop, GIMP and Illustrator, recognising differing target audiences. Derive a shared layout blueprint, document justified deviations and use 9to1's distinct CUI visual identity. Approval tests must examine actual packaged screenshots at representative window sizes, keyboard navigation, density, alignment, hierarchy, overflow and discoverability. Attractive screenshots alone do not establish functionality; functioning controls alone do not establish acceptable design.

### V2-B2271 · v2 lines 13361–13361

STAGING-001 — RCVP versus Full. RCVP includes all non-exempt donor functionality and everything necessary for genuine everyday use: canonical CUI, launch/create/open/edit/save/close/reopen/export as applicable, accessible navigation, permissions, security, persistence, recovery, error handling, essential integrations and supported formats. Full adds 9to1-specific differentiating enhancements such as generative UI, unless a specific enhancement is explicitly essential to baseline functionality. Every feature is assigned to RCVP or Full in an approved per-app manifest; unassigned necessary features cannot silently be deferred. The four-part delivery order remains unchanged.

### V2-B2272 · v2 lines 13364–13364

ACCEPT-001 — Real-feature proof. A feature is VERIFIED only when a real user can perform its complete intended workflow in the actual packaged application using genuine engines/providers and persistent state where applicable, with correct results, error handling and reopen/recovery checks. Unit tests, source presence, mock responses, screenshots or disconnected harnesses alone are insufficient. Where a live provider cannot be exercised, report NOT VERIFIED or BLOCKED, never simulate a PASS. Independent acceptance uses the specification and feature inventory, not only implementer-authored tests. Record artifact/build hash, platform, test steps, observed outcomes, screenshots where relevant, defects and reviewer.

### V2-B2273 · v2 lines 13367–13367

ACCEPT-002 — Cross-app integration. For each specified integration, test a real round trip between the two packaged applications or real deployed services, including permission prompts, data fidelity, error paths and persistence. A link, button, shared library or stub is not sufficient.

### V2-B2274 · v2 lines 13370–13370

ACCEPT-003 — Security and Dulche privacy. Cross-app personal context is denied by default and shared only following granular, informed, revocable user permission. Encrypt sensitive stored context and protected transfers; minimise data exposed to models and providers. Applications and non-model APIs must not receive protected Den context without explicit permission. Enforce these boundaries technically at runtime; model instructions alone are not a security boundary. Test denied, allowed, revoked, offline and provider-failure cases. Never claim a model can guarantee secrecy solely by being instructed not to relay data.

### V2-B2275 · v2 lines 13373–13373

ACCEPT-004 — Local/cloud behaviour. Define each app operation's local/cloud capability, data movement, user consent, fallback eligibility, offline behaviour, conflict resolution and user-visible status. Never silently send private content to cloud providers or treat unavailable services as working. Test each declared mode and transition.

### V2-B2276 · v2 lines 13376–13376

ACCEPT-005 — Accessibility and quality. Every app must document supported target platforms and accessibility standards, keyboard/focus navigation, screen-reader semantics, scaling, contrast and reduced-motion behaviour; verify these in packaged builds. Define app-specific measurable performance budgets (startup, common interactions, save/open, memory and large-document behaviour) and test on declared reference hardware. Missing approved budgets or untested required platforms block final acceptance rather than permitting invented numbers.

### V2-B2277 · v2 lines 13379–13379

ACCEPT-006 — Packaging and errors. The supported OS/version/architecture matrix, installation/upgrade/uninstall expectations, file associations and recovery expectations must be declared per release. For each significant operation define success, failure, retry/cancel, data durability and user-facing error behaviour. Failure must not be hidden behind success-looking UI. Native and web parity means equivalent supported user outcomes unless an owner-approved platform limitation is recorded, not necessarily identical layout.

### V2-B2278 · v2 lines 13382–13382

GOV-001 — Precedence and ambiguity handling. Later explicit owner-approved normative directives override earlier conflicting instructions only in their stated scope; historical schedules do not supersede the active four-part plan. Preserve all compatible prior requirements. If two requirements conflict, an implementation detail is unspecified, a donor feature cannot be reproduced or a quality threshold is absent, log the precise ambiguity and options, stop the affected acceptance decision and obtain owner approval. Do not infer approval from a deadline, successful build or test. Maintain a versioned decision log and traceability from requirement -> implementation -> test -> evidence -> independent verdict. Statuses must distinguish WORKING, CHECKPOINT, BLOCKED, COMPLETED and VERIFIED; COMPLETED does not imply VERIFIED.

### V2-B2279 · v2 lines 13385–13385

GOV-002 — Mandatory release gates. No RCVP or Full PASS unless (1) CUI static and runtime conformance, (2) donor inventory/approved exceptions, (3) category-layout review and screenshot inspection, (4) packaged end-to-end workflows, (5) privacy/security/accessibility, (6) platform and performance evidence, (7) real cross-app integrations where required, and (8) independent acceptance are satisfied. Any mandatory gate failure is a release blocker, not an averaged score. Preserve existing stronger app-specific rules.

### V2-B2280 · v2 lines 13390–13390

12 October 2026 Spaces UI decisions (WIP) — companion addendum: https://docs.google.com/document/d/12AbDnWynZjVbKZ5XhkkiGMmJkINOSqMfnPuHuI6XDBE/edit . Updated by evergreen handoff.
