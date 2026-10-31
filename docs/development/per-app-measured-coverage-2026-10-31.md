# Measured per-app requirement coverage — instrument and first measurements, 31 October 2026

Owner request on 31 October 2026, verbatim: **"yes for per app measured percentages"**, replacing the rough
ranges I had been quoting in the progress report. This document records the instrument that now produces those
numbers, and the first measured results — including what the instrument cannot yet answer honestly.

## What is measured exactly

The canonical specification (`docs/specification/9to1-development-specification-v2.md`, SHA-256
`e1779d22…`, matching the digest AGENTS.md pins — verified in this run) is parsed into **normative statements**:

| Definition | Measured |
|---|---:|
| Lines containing a normative keyword (outside code fences) | 3,360 |
| …of which MUST-level lines | 2,799 |
| **Normative statements** (sentence level) | **5,125** |
| **MUST-level statements** — the implementation denominator | **3,982** |
| SHOULD-level (required by default, deviation needs a recorded exception) | 372 |
| MAY-level (intentionally optional) | 338 |

The line and statement figures differ because one line can carry several requirement sentences. **Quote the
statement figure and say so**: the earlier "3,461 normative MUSTs" in my progress reporting was a line/keyword
count, not a statement count, and it is superseded by 3,982 under the definition above.

MUST statements by delivery scope, measured:

| Scope | MUSTs | Basis |
|---|---:|---|
| In Shipmas scope | **2,659** | the owner's scoping of 30/31 October |
| Provisional (Cards, Planner) | 66 | owner: "Cards, then Planner" |
| Explicitly out of scope | 392 | Games, Play, Picture, Wave, Motion, Mini Computer, 9to1 OS distribution |
| **Scope unresolved — the owner's list does not name these apps** | **865** | asked, not decided: see `OPEN-2026-10-31-SCOPE-UNRESOLVED-APPS` |

The 865 is the largest single uncertainty in the whole plan: 22% of the specification's MUSTs sit in apps the
scoping answer does not name, while the specification treats several of them as mandatory. Biggest lines:
Dulche 414, TF (the specification keeps this product's own name OPEN at L1037) 116, other apps 97, packaging 63,
Admin 61, Voice & Vision 73 is *named* but its requirements were split, Forms 42, shared-settings 39,
Study Space 33.

Per-app MUST denominators are in `register.json` (5,125 rows, one per statement, each with the specification
line number, section path, scope class and a content-addressed id). The register is committed and pinned to the
specification digest: if the specification changes, `spec_register.py` refuses until the pin is changed
deliberately.

## Per-app measured evidence tiers (the honest part)

`assess.py` scores every MUST against the source that exists, using the app's **recorded** roots
(`app-roots.json`, with reasons: Build = Build+Dev+Sites+Stacks per the one-app rule; Connect = Connect+Mail;
Automations/Voice & Vision/Dulche have no workspace directory, their code measured in `shared/src`).
Donor checkouts are excluded **by the donor registry**, not by directory-name guessing.

| App | MUSTs | test-asserted | code-present | weak | **no evidence** | unsearchable |
|---|---:|---:|---:|---:|---:|---:|
| Haven | 240 | 2 | 14 | 141 | 27 | 56 |
| Spaces | 218 | 3 | 19 | 125 | 32 | 39 |
| Build | 170 | 1 | 6 | 113 | 19 | 31 |
| Boards | 138 | 2 | 13 | 95 | 10 | 18 |
| Canvas | 157 | 2 | 9 | 99 | 25 | 22 |
| Write | 91 | 1 | 9 | 63 | 8 | 10 |
| Present | 223 | 0 | 11 | 134 | 41 | 37 |
| Data | 89 | 0 | 6 | 52 | 16 | 14 |
| Files | 182 | 0 | 6 | 120 | 22 | 34 |
| Connect | 197 | 2 | 14 | 124 | 29 | 28 |
| Automations | 53 | 0 | 4 | 28 | 10 | 11 |
| AI Studio | 80 | 1 | 7 | 48 | 7 | 17 |
| Accounts | 290 | 3 | 10 | 182 | 38 | 57 |
| Games (out of scope) | 100 | 0 | 0 | 0 | 99 | 1 |

## What these tiers do and do not prove — measured, not asserted

I tried two settings and measured both failure modes, so this is evidence about the instrument itself:

* **Loose setting**: 88–90% "evidence-credited" for nearly every app — including **Games at 89% and
  Play at 85%, and Games reported 72% test-asserted, although no Games or Play source exists in this
  repository at all**. Cause: their recorded roots were the whole workspace, and long common words co-occur
  somewhere in 4,000+ files.
* **Strict setting**: credited share collapsed to ~3–17% overall and **Boards fell to 2 test-asserted of 138**,
  which is plainly wrong in the other direction: `Haven.Desktop.Tests` carries hundreds of real Boards tests.
  Cause: requiring an identifier-like matched term made prose-only requirements unsearchable.

So the current tiers are deliberately the **strict, conservative** setting, and they are **candidate evidence,
not verdicts**:

* **NO-EVIDENCE is the reliable measurement**: that requirement's distinctive vocabulary appears in none of the
  app's recorded roots. It is falsifiable by a single counterexample, which is why it is the number I would
  quote to you. Boards: 10 of 138. Spaces: 32 of 218.
* **CODE-PRESENT / TEST-ASSERTED are lower bounds** on implementation, and the gap between the loose and strict
  settings (for Boards: 10.9% credited strictly, 89.9% loosely) **is itself the measured finding**: static text
  matching cannot decide implementation state. Any per-app percentage produced this way would be an
  instrument artefact, not a product fact.
* **NO-PROBE-TERMS / unsearchable** rows are requirements written purely in prose ("MUST feel clean and
  comfortable"); they are recorded as a separate bucket rather than folded into either side.

## First measured per-app state: Boards, 138 of 138 MUSTs given an explicit state

The instrument's second axis is a **hand state pass**: every MUST row for an app gets an explicit
`IMPLEMENTED` / `PARTIAL` / `NOT-IMPLEMENTED` / `NOT-ASSESSED` with evidence and a reason, in
`eng/spec-coverage/assessments/<App>.json`, and a hand state always beats the probe. Boards is done first,
because I know its surface from today's framework work.

**Measured result for Boards (31 Oct 2026), 138 MUST requirements:**

| State | Rows | Share |
|---|---:|---:|
| IMPLEMENTED | **15** | **10.9%** |
| PARTIAL | **95** | 68.8% |
| NOT-IMPLEMENTED | **22** | 15.9% |
| NOT-ASSESSED (this pass did not ground it) | 6 | 4.3% |
| state coverage (rows given a grounded state) | 132/138 | 95.7% |

So the honest per-app sentence is: **Boards is ~11% fully implemented and ~80% implemented-or-partial against its
138 MUST requirements, with 16% measured as not implemented.** Not "Boards is 80% done" — the PARTIAL rows are
half-built surfaces, and a percentage that quietly credits them would be the same kind of dishonesty as my old
±15-point ranges.

How the pass works (`eng/spec-coverage/write_boards_state.py`, reproducible): 31 requirement clusters, each with
named evidence and a reason; 19 rows judged individually. Cluster-level states are labelled `basis: cluster` in
the file so the coarser grain is visible, not hidden. Six rows are `NOT-ASSESSED` because I did not verify them —
recorded as a gap in the review rather than guessed either way.

The gaps that are **grounded by measured absence** (the grep is written into the reason so anyone can falsify it):

* `9to1.Boards.*` action namespace: **zero occurrences in the whole repository**. Every user-visible Boards action
  is required to have an equivalent API action; there is no exposed Boards API surface at all.
* Scoped Chat: the term `ScopedChat` appears **nowhere** in first-party source → the entire Scoped Chat page type
  cluster (4 MUSTs) plus its grounding/provenance requirements is unimplemented.
* Chat page type and Chat source subpages (7 MUSTs): no Chat page surface in Boards.
* Virtualisation/lazy loading: **zero** matches in Boards → the performance cluster (4 MUSTs) is unimplemented.
* `BoardNotFound`/`PageNotFound`/`BlockNotFound` structured failures: absent in Boards.
* Home-absent "Install Home" bootstrap: absent in Boards' launch path.
* Recoverable delete: `Trash`/`undelete` absent in Boards (retention exists in `shared`, not wired for Boards).
* The **Rnote-derived ink engine is consumed by nothing**: Boards has ink support of its own (pressure, eraser,
  selection, survive-reopen tests) but not through Home's shared engine, which is what spec L9459 requires and
  what your answer on 31 October confirmed is the shared piece.
* Boards' use of Home's Shared Productivity Engine: NOT-IMPLEMENTED, and the evidence is Boards' own
  `DonorParityManifest.md` line 20, which says the shared service still stores Boards as a `NotesDocument` with
  `ProductKey=boards` and "does not yet establish the canonical Board/Page schema required by the specification".

What IS genuinely implemented is not small either, and it is asserted by named tests I read: graph blocks
(expression safety, sliders, inequalities, full lifecycle survive-reopen), locked/unlocked layout modes with no
silent geometry conversion, Heading 4–6 styles with migration, custom styles as versioned stable-id definitions,
crash-safe persistence with backup recovery (`Corrupt_primary_recovers_previous_durable_backup`,
`Invalid_save_is_rejected_and_previous_valid_file_survives`, `BoardsSaveDurabilityRegression`), Board document-space
absolute placement surviving a mode switch, a real Recent-board catalogue, and typed command mapping instead of
hidden donor service calls. Boards carries 113 test methods in its own suites plus ~54 Boards-relevant tests in
Haven.Desktop.Tests.

This is a code-and-test reading. **No Boards surface was rendered in this pass**, so no row here is acceptance
evidence, and nothing about the 102 UX27 scenarios moved. The per-app number for the other apps arrives as their
own passes are written; until then those apps report only the denominator and the probe tiers.

## Measured precision of each tier (21 hand verdicts)

I hand-judged 21 probe-credited rows by reading the requirement next to the file the probe pointed at. The
verdicts are recorded in `eng/spec-coverage/audit.json` with a reason each, and `assess.py precision` computes:

| Tier | Audited | True | False | **Measured precision** |
|---|---:|---:|---:|---:|
| TEST-ASSERTED | 3 | 3 | 0 | **1.00** (n=3 — small sample; treat as "trustworthy, not proven at scale") |
| CODE-PRESENT | 18 | 4 | 14 | **0.22** |

Fourteen false positives out of eighteen. The causes were concrete and three of them are now closed:

* **documentation credited as implementation** — four pointers landed on app markdown files, two of them on
  `Boards/DonorParityManifest.md`, which records what is *missing*. A gap record was being scored as proof.
  Markdown is no longer an evidence class.
* **my own tooling credited as product** — a 9to1 OS requirement matched `eng/donor-sources.json` on the words
  "compatibility" and "installed". `.json` is no longer an evidence class, and `eng/` is a root only for the
  packaging bucket whose requirements genuinely are about that tooling.
* **prose-word coincidence** — "disposable/identified" matched `IAsyncDisposable` cleanup; "affected/appropriate"
  matched recovery-telemetry field names; "materially/necessary" matched agent prompt text; "expansion/filtering"
  matched composer panels. These are not fixable by tuning: they are what happens when requirement prose is
  searched against 6,793 files.

A fourth finding improved the instrument's reach rather than its precision: the tokeniser swallowed snake_case
test names whole, so `Matching_challenge_commits_site_bound_ownership_and_survive_reopen` was invisible to a
search for "ownership". Test names are the most behaviour-descriptive vocabulary in the repository, so their
sub-tokens are now indexed.

**Whole-specification tier totals after all corrections:** of 3,982 MUSTs — TEST-ASSERTED 29, CODE-PRESENT 196,
WEAK-SINGLE-TERM 2,299, NO-EVIDENCE 700, unsearchable prose 756. The dominance of WEAK-SINGLE-TERM is the
honest signal: **most requirements cannot be decided by text matching at all.**

## Measured against your pinned rule: the running shells are still foreign rendering

Measured with `eng/spec-coverage/surface_conformance.py` (13 tests, baseline committed, CI suite
`surface-conformance`), excluding every pinned donor checkout and every test fixture:

| Measured | Count |
|---|---:|
| First-party foreign-rendering UI files (`.xaml`/`.axaml`/`.hui`) | **93** |
| …of which app-shell or page surfaces | **55** |
| First-party `.cui` documents, whole product | **72** |
| Stage A donor mirror `.cui` documents | 1,784 |

Per app, first-party `.cui`: Boards 11, Assistants 8, Spaces 7, Home 5, Canvas/Data/Dev/Files 2 each, and
**Build, Write, Present, Connect have zero** first-party `.cui` — their CUI documents are mirrors only
(Build 289, Write 232, Data 188, Present 89, Connect 19).

What the 55 shell/page files are, named: `Haven.Desktop/App.axaml`, `MainWindow.axaml`,
`Interface/Shell/MainView.axaml`, `Views/Shell/Sidebar/Sidebar.axaml`, `Views/Shell/TopRail/*` (3 files) and 30+
`Views/Pages/*Page.axaml` — Chat, Mail, Home, NewDashboard, Settings, Terminal, Maps, Play, Tasks, Plan, Go,
Automations, Browser, Call, Canvas, Data, Notes, Present, Write, Studio, Translate, Training, WorkspaceEditor,
WorkspaceHome, ProjectCreator, Experiences, Catalog, LessonSettings, ContainerSettings, ModeLibrary, Archive,
ActivityLog. Plus 23 `.hui` production files: 19 in `Haven.Desktop/DynamicUI/` and `Prefabs/` (including
`Chatbox.hui` and `ChatAddMenu.hui`) and 4 in `Haven.UI/Resources/`.

Two consequences worth your attention, both measured rather than argued:

1. **The Stage A mirror work and the shell work are different jobs.** 1,784 donor-UI documents now exist in CUI,
   but the surfaces a user actually runs in the desktop build are still AXAML/HUI. The two-stage plan
   (donor → CUI mirror → spec adaptation) does not by itself remove those 93 files; the shell and page views are
   a separate conversion, and 55 of them are main structure, not transient pop-ups.
2. **`.hui` is still in production**, which the CUI mandate treats as a retired authoring language — including
   `Prefabs/Chatbox.hui`, in a specification amendment the composer replaced legacy chatbox with the CUI
   composer. That file being present does not by itself prove the legacy composer is what users see; whether it
   is loaded at runtime is **NOT RUN** here. The measurement is file presence, which is exactly what it claims.

The baseline is recorded as a **violation level**: it may only move by reducing foreign files and increasing
first-party `.cui`, and `check` fails both if someone adds an `.axaml` view and if someone deletes a CUI
document to improve the ratio.

## The path to per-app percentages you can use

A measured percentage needs a **state per requirement**, so the instrument is built as: denominator (done,
exact) → state with evidence pointer (needs a review pass) → acceptance state (separate, still NOT RUN).

`assess.py` now accelerates the review pass rather than replacing it:

* `assess.py row <id>` prints one requirement with its best candidate file and the matched terms;
* `assess.py sample --n 40 --seed 20261031 --tier CODE-PRESENT,TEST-ASSERTED` gives a **reproducible random
  sample** for hand-verdicts, with file snippets, so each tier gets a **measured precision** instead of an
  assumed one;
* `assessments/<App>.json` holds hand-made states (IMPLEMENTED / PARTIAL / NOT-IMPLEMENTED) each with an
  evidence string and a reason, and **overrides always win over the probe**; an override with no evidence is
  refused outright;
* `assess.py precision` computes per-tier measured precision from `audit.json` and reports each app as a
  **band** scaled by those audited precisions.

Until a state pass exists for an app, the report gives that app's **exact MUST denominator** and its measured
NO-EVIDENCE count, and states that the implementation percentage is not yet measured — rather than publishing a
number the instrument cannot support. That is the difference between a measured percentage and a guess with
decimals.

## Acceptance is a separate number, and it is still zero-ish

Independent acceptance remains paused by owner instruction, so per-app **Accepted** stays near zero and is not
the same axis: nothing in this instrument is on-screen, packaged or accepted, and none of the 102 `UX27-`
scenarios has moved off NOT RUN.

## Instrument tests

`eng/spec-coverage/test_spec_register.py` (30) and `test_assess.py` (26) are wired into
`development-infrastructure.yml` as `spec-requirement-register` and `spec-coverage-evidence`, with a CI step that
rebuilds the register, re-scores the tiers and re-checks every pointer (`verify-pointers`) before them. The
tests pin: the denominator figures above, the scope split, the out-of-scope apps measuring zero evidence, one
row per MUST, pointer staleness, override-without-evidence refusal, no app scored against the whole workspace,
donor material excluded by the registry, and the empty-corpus refusal that stops a degenerate run publishing
zeroes as if they were results.

One earlier suite run failed and I could not identify the test; the concrete per-app tier pins above were added
so a recurrence names the app and tier instead of being an unexplained red run. Since adding them: three clean
runs of 56 tests, and six clean runs before that.
