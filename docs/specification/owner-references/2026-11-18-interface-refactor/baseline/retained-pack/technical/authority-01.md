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
