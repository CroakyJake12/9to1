# Rendered evidence and visual corrections — status

**Date:** 2026-10-14 · Branch `opencode/spaces-handoff-20261014` · Base `2c4d151b`

Native and browser evidence are tracked **separately**. This file records **native** only.
Browser evidence: **NOT RUN** (`apps/Web` declares no `package.json` scripts and no dev server).

---

## 1. Environment resolved

Host is **Windows ARM64**; only the arm64 .NET was installed while the desktop chain is
x64-pinned, so no native test could start. Installed the **x64 .NET SDK 10.0.401** side-by-side
at `C:\dotnet-x64` via the official zip (no elevation, existing arm64 install untouched).
Verified `RID: win-x64` / `Architecture: x64`, then confirmed the native test host resolves it.

Use `C:\dotnet-x64\dotnet.exe` for all native work.

## 2. First rendered evidence — produced

`HavenUiVisualCaptureTests.Capture_chat_composer_states_when_requested` builds the **real**
`ChatSidebarHavenScene` + `ChatHavenScene` through `HavenSceneControl` at the reference
viewport **1527 x 947** and captures the three pictured composer states.

- `chat-01-compact.png`, `chat-02-expanded.png`, `chat-03-add-panel.png`

Fixture transcript is **neutral placeholder data** in D04 order (user request, AI
acknowledgement, recorded work, clarification, user answer, further work, result) —
deliberately **not** the reference's illustrative restaurant text.

**Capture composition limit:** the harness composes sidebar + chat only. It does **not** include
the Space rail, TopRail header, workspace inset or footer note. Their absence in these frames
is a **harness limitation, not established as a product defect.**

## 3. Correction 1 — palette (VERIFIED)

**Observed:** whole Chat surface rendered navy/violet.
**Cause:** `SurfacePaletteCatalog.Assemble`, SuperDark branch blended the per-app accent into
every large neutral — `Blend(Parse("#FF0B0E18"), accent, 0.25)` and 0.10–0.24 elsewhere. Chat's
accent is indigo `#3527FF`.
**Fix:** neutrals now resolve from `CuiSurfacePaletteCatalog.For(..., CuiTheme.Professional)`,
so **CUI owns the values**. `Haven.Desktop` already referenced `CakeOS.Cui.Runtime`. Accents
still drive controls per `CUI-PREF-203`.
**Result:** purple cast gone; neutral charcoal. Confirmed by re-capture.

## 4. Correction 2 — sidebar section typography (VERIFIED)

**Observed:** sidebar section labels rendered **larger than the conversation body**.
**Cause:** five labels used `Level="H3"`; `TextDefaults.For(H3)` = **24px/700**. These are
section labels in a navigation list, not third-level headings.
**Fix:** those five only → `Level="Caption"` (12px/600, TextSecondary), matching the
reference's small muted "Recent Chats".
**Result:** correct hierarchy — nav item > muted section label > conversation item.

`TextDefaults` itself was **not** changed: H3 = 24px is correct for genuine headings
(Dialog, HeaderBar, panel titles). The defect was misapplied level, not a wrong default.
Follow-up still owed: `TextDefaults` hardcodes sizes instead of sourcing CUI's typography
roles.

**Process note:** my first attempt at this edit used PowerShell `Set-Content -Encoding UTF8`,
which re-encoded the file and corrupted the `•` character, breaking
`Sidebar_existing_rows_can_refresh_after_inline_action_variables_are_removed`. Detected by the
test, reverted with `git checkout`, and redone with a byte-safe editor. **11/11 pass after
redo.**

## 5. Known remaining differences vs `references/intended/`

| # | Difference | State |
|---|---|---|
| 1 | Conversation surface renders darker than the reference `#14151A` — CUI Professional SuperDark `TideColour` is much nearer black | **open** |
| 2 | Sidebar vs conversation surfaces are barely distinguishable; reference separates them (`#1F2127` shell vs `#14151A` conversation) | **open** |
| 3 | Composer has no Voice waveform control (reference compact state shows one right of the placeholder) | **open** |
| 4 | Space rail absent from capture | **harness limitation**, not established as a product defect |
| 5 | Header/model pill absent from capture | **harness limitation** |
| 6 | Sidebar default sections differ (`File Library / Chat Groups / Pinned / Unread / Chats` vs reference `New Chat / Chat Groups / Specialists / Recent Chats`) | **open** |
| 7 | Composer placeholder reads "Ask Haven anything"; reference reads "Ask Anything". Carries the legacy product name | **open**, part of naming consolidation |
| 8 | Icons are thin strokes; reference uses prominent filled icons | **open** (L1, no spec-conformant asset set exists) |

**This is CUI supplying colour to a still-HUI-authored surface. It is not completed CUI
migration.** `CuiThemePreferenceContext` / `HomeThemePreferenceService` still do not drive
this route.

## 6. Acceptance status

No visual assertion is PASS. Per `CURRENT-REQUIREMENTS.md` §4 and `06-ACCEPTANCE-AND-EVIDENCE.md`,
a required check that is unrun or environment-limited stays **Unvalidated**, and no approved
numerical tolerance exists (O22) to declare fidelity. These frames are **evidence for
diagnosis**, not acceptance.

Gate B: **NOT MET**. Gate C: **NOT RUN**. Gate G (independent review): **NOT RUN**.

## 7. Pre-existing failures retained

| Suite | Result | Note |
|---|---|---|
| `HavenOS.Home.Tests` | 218 pass / **1 fail** | `HomeNativeWindowsCompositionTests.Close_publishes_same_task_before_original_profile_cancellation_and_waits_held_finally`; deterministic; exonerated from this work by dependency evidence (Home does not reference Mail) |
| `Haven.Core.Tests` | 945 pass / **50 fail** | proven pre-existing by stash/rebuild/re-run giving identical counts |

Both remain **undiagnosed** and are not claimed as passing.