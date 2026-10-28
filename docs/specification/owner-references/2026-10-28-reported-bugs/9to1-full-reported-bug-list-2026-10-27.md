# 9to1 — consolidated reported bugs and follow-ups

Basis: the supplied `9to1-pc-live-astra.json` export, direct owner reports on 26–27 October 2026, the Beam development plan embedded in that export, and the retained desktop-recording triage. This is not a fresh audit of the current executable or an exhaustive V2 feature inventory. Screenshot-only details not described in those sources are not reconstructed.

## How to use this register

Reproduce each applicable item in the actual current build before editing. Preserve already-working behaviour. A source patch, a successful build, a worker's completion statement or a window appearing does not close a user-facing defect. Record actual source/build/route, reproduction, repair, regression result and native retest in the existing evidence system.

Status terms:
- **Latest report:** failure explicitly present in late owner feedback; current runtime still needs checking.
- **Recheck:** historical failure or an attempted/source-only fix; do not assume it remains broken or is fixed.
- **Regression:** the owner or retained focused checks reported improvement; preserve and retest rather than rewrite.
- **Investigate:** an observed mismatch or historical diagnostic whose cause/current status is unestablished.
- **Request:** an explicitly requested feature/refinement, not necessarily a malfunction of an implemented feature.

The IDs below are handoff labels, not canonical V2 requirement IDs.

## A. Launch, restoration and shutdown

| ID | Reported issue | Evidence status |
|---|---|---|
| A01 | Boards does not open through the normal entry point. | Latest owner report, 27 October 21:06. |
| A02 | Spaces opens a screen that only says “new tab” instead of Spaces. | Latest owner report, 21:06. |
| A03 | Numerous other apps/surfaces fail to launch; Present, Write and Mesh are explicitly named. The owner estimated over half/most, but no complete affected count was established. | Recheck each actual entry; do not extrapolate from one working app. |
| A04 | Saved Boards tabs fail to restore and show an app-could-not-start/refused-to-start error. A stale one-shot Files lease was a worker-reported cause to verify. | Later owner reports still describe failed restored Boards tabs. |
| A05 | Failed Boards tabs cannot be closed; clicking close produces an action-could-not-complete error. | Three remained unclosable at 19:48. Stale generic New Tab tabs were separately reported closeable. |
| A06 | The main 9to1 window will not close normally with its X button. | Repeated owner reports; later worker still acknowledged close-preflight failure. |
| A07 | Shutdown failure expands into an enormous repeated/nested error wall. | Recheck bounded visible error reporting and the underlying cleanup failure separately. |
| A08 | A generic action-failed banner persists across page/Space changes, detached from the action that caused it. | Historical report; recheck error lifetime and recovery. |
| A09 | The reload button remains greyed out and unusable. | Direct owner report; recheck its actual availability and action. |
| A10 | A launch reports “no registered native surface”. The highlighted AI Studio tile did not establish which route actually failed. | Investigate the actual originating route; do not invent an AI Studio-specific diagnosis. |

## B. Boards creation, saving and document identity

| ID | Reported issue | Evidence status |
|---|---|---|
| B01 | Boards opens a confusing generic Library splash instead of a recognisable Boards landing with recent boards and creation/opening actions. | Explicit late owner report; a later route-fix claim is not a successful user journey. |
| B02 | Create/Cancel controls are missing, inaccessible or unclear during board creation; name/destination/actions are scattered. | Recheck historical reports and attempted layout fixes. |
| B03 | The creation experience appears as a separate pop-out instead of inside the tab, contrary to the owner's request. | Recheck. |
| B04 | Creation labels overlap and the Library shows through the dialog; background/contrast/layout are wrong. | Focused layout fixes were recorded; installed UI needs retesting. |
| B05 | Board creation is gated behind Files setup, folder selection or other setup instead of permitting a local recoverable draft without an account or special Boards folder. | Required owner correction; verify actual production creation and Save As paths. |
| B06 | A cancellation notice from an earlier create attempt remains visible when a new attempt starts. | Historical feedback-lifetime defect. |
| B07 | Files-backed Board saving waits on its own Home-store lease and times out. | Regression: focused save repair/tests were recorded; real save/reopen must remain covered. |
| B08 | Save retry/audit handling classifies ordinary provider failures as conflicts. | Historical source finding in `RetryAuditAndReceiptAsync`; distinguish from the separately repaired normal-return conflict classification. |
| B09 | Filename/creation name and editor title disagree: a named board appears in Library while the editor says “Untitled board”. | Investigate intended title policy and canonical identity; not proof that the wrong file opened. |

## C. Boards text, blocks, layout and drawing

| ID | Reported issue | Evidence status |
|---|---|---|
| C01 | Enter fails to create/split a real block. It must remain distinct from Shift+Enter's within-block line break, with list/code/table/IME exceptions preserved. | Still explicitly reported at 19:48. |
| C02 | The six-dot buttons become unresponsive, including their normal context action. | Explicit later owner report. |
| C03 | Six-dot handles do not perform usable block/list-row reordering. | Recheck separately from whether clicking the handle opens a menu. |
| C04 | Ordinary typing is awkward: activating the intended block/cell, caret/focus and editing continuity are not reliably usable. | Earlier reported defect; reproduce rather than infer a specific input cause. |
| C05 | An unexplained Freeform canvas/second workspace is forced into ordinary boards/pages. | Repeated report despite an earlier source-fix claim. Preserve intentional canvas pages and saved objects. |
| C06 | Tables expose several rows of controls, including irrelevant/inactive actions, overwhelming the editor. | Earlier report; “roughly 50 buttons” was an overload description, not a measured count. |
| C07 | Edit/View controls appear to have vanished or become inaccessible. | Recheck actual toolbar/state/overflow. |
| C08 | Layout/snap-to-grid controls appear to have vanished or become inaccessible. | Recheck separately from Edit/View; restoring labels alone is insufficient. |
| C09 | Selecting Draw/Pen does not activate usable drawing. | Regression: the owner later confirmed pen operation. |
| C10 | Pen strokes are offset from the actual pointer position. The owner clarified that the defect was offset, not progressive growth. | Recheck coordinates after scroll/zoom/resize; later general “pen works” is not a complete coordinate test. |
| C11 | Eraser does not erase ink. | Regression: the owner explicitly said both pen and eraser worked at 19:48. |

## D. Boards theme, menus and controls

| ID | Reported issue | Evidence status |
|---|---|---|
| D01 | Opening Boards forces light appearance. | Explicit 20:24 report; later source-fix claim requires real retest. |
| D02 | Opening Boards also changes unrelated Home views to light while chrome remains dark; pale text becomes hard to read. | Earlier theme-scope/readability report; check global and per-app state independently. |
| D03 | Hover backgrounds have sharp corners rather than following rounded controls. | Explicit 20:24 report. |
| D04 | The Add menu is a giant text list with no proper hover feedback or usable scrolling, and remains open after minimising the window. | Explicit 20:24 report; retain all four symptoms. |
| D05 | Save-status text and the board name have incorrect spacing. | Explicit 20:24 report. |
| D06 | Toolbar pills contain an input inside a dropdown instead of a coherent selector such as “Default” plus a chevron. | Explicit 20:24 report. |
| D07 | Icons are vague or wrong for their action; the pen/brush entry resembles a downward arrow, and Pen/Highlighter/Eraser/Select need recognisable icons. | Explicit late report; later icon-change claim requires retest. |
| D08 | Shared Glow/control styling uses inappropriate borders and too many accent-filled pills rather than the requested control hierarchy. | Retained owner design correction; compare against current themes before changing. |

## E. Chromium, Gecko and browser input

| ID | Reported issue | Evidence status |
|---|---|---|
| E01 | Chromium stops working or the normal build does not contain the claimed renderer fix. | Later reports; subsequent installation/wiring claim is not verified in-app rendering. |
| E02 | Firefox/Gecko is unavailable or not genuinely integrated into the host. | Repeated report; owner explicitly placed Gecko work after Boards. |
| E03 | Web content is stretched or has distorted proportions. | Earlier report when Chromium could render. |
| E04 | Web content is low-resolution/soft/enlarged compared with the host UI. | Earlier report; separate from aspect-ratio distortion. |
| E05 | Scrolling moves in the wrong direction. | Earlier report; recheck platform settings and actual event mapping. |
| E06 | Scrolling barely moves the page. | Separate magnitude/unit problem to reproduce; do not close it merely by fixing direction. |
| E07 | Google human-verification challenges are repeatedly rejected. | Reported compatibility failure; exact cause not established. No security/challenge bypasses. |

## F. Tabs, navigation and performance

| ID | Reported issue | Evidence status |
|---|---|---|
| F01 | Editing the address bar takes multiple clicks and Ctrl+A instead of a predictable focus/replacement action. | Recorded report. |
| F02 | Address-bar text being typed is replaced by the existing URI/title. | Recorded report; exact triggering event needs reproduction. |
| F03 | Web search and internal 9to1 navigation are unclear; entering “boards” can become a Google search. | Recorded report. |
| F04 | Split tabs do not have the requested combined tab pill. | Direct owner correction; source changes need runtime checking. |
| F05 | A split is not retained as one tab object that restores its split view when selected again. | Direct owner correction; test switching away/back and restart. |
| F06 | Tab groups behave like renamed individual tabs instead of genuine groups. | Direct owner report. |
| F07 | Tab overflow/scrollbar behaviour and shell lag make the testing copy awkward. | Earlier reported issues with attempted fixes; check actual pointer/keyboard access and responsiveness. |

## G. Spaces and composer

| ID | Reported issue | Evidence status |
|---|---|---|
| G01 | The sidebar does not update for the selected Space. | Direct owner report. |
| G02 | Saved-chat rows overlap Search/Clear and the recent-chat notice. | Historical footage report with source layout repair; recheck populated history. |
| G03 | The attachment popover appears underneath composer/status text. | Regression: focused native/fallback fixes were recorded; current installed route still needs checking. |
| G04 | Emoji UI expands the composer and clips a grid row. | Historical observation; clipping is reported, but inline placement alone was not established as a specification violation. |
| G05 | Formatting controls remain over Library after navigating away from the chat. | Historical report; recheck ownership, dismissal and draft/focus preservation. |
| G06 | Model-unavailable recovery in Spaces has not been demonstrated to lead to a usable model-manager destination. | Verification gap, not proof that the recovery button is dead. |

## H. Shared popups, Home and Files

| ID | Reported issue | Evidence status |
|---|---|---|
| H01 | Popups do not dismiss on outside click. | Direct report across shared menus and Boards. |
| H02 | Account and nested Switch & Manage Users menus persist across Dashboard/Models/New Tab/Library/Events, with another dropdown underneath. | Recorded lifetime/stacking problem. |
| H03 | Dashboard spacing and hierarchy are confusing. | Owner requested redesign; a later redesign claim needs actual review. |
| H04 | Library repeats its title, has pale status text and exposes raw owner/profile IDs and bulky technical metadata. | Recorded clarity/readability defect. |
| H05 | Events repeats its title and exposes permission-pipeline stages/technical IDs instead of a clear user operation and outcome. | Recorded clarity defect; preserve detailed audit data behind the summary. |
| H06 | Files presents a flat collection of controls rather than its intended donor-derived navigation/content hierarchy. | Earlier report; some topology repair was recorded, so recheck current behaviour rather than replace it. |

## I. Models, account integration and storage diagnostics

| ID | Reported issue | Evidence status |
|---|---|---|
| I01 | 9to1's own model runtime is unavailable/nonfunctional despite a separate development model running. | Earlier owner report; actual inference must be established through 9to1. |
| I02 | Model discovery reports no usable models despite installed artefacts; provider errors and model readiness are confusingly mixed. | Earlier report; later footage reported one connected model, not verified inference. |
| I03 | Discover shows partial results, an Ollama query failure and an active-profile-change notice. | Recorded state; diagnose errors without treating every model as unavailable. |
| I04 | Ordinary model selection opens a large route-administration interface with raw route IDs instead of a readable model list. | Recorded usability defect. |
| I05 | A route snapshot scope/category mismatch prevents a request. | Regression: a focused contract fix was recorded; current request path needs checking. |
| I06 | Dashboard “Edit with 9to1” uses an unavailable/wrong model route, historically including an Ollama localhost refusal. | Regression: source was changed to the selected Home route; verify real execution. |
| I07 | Linux app sign-in remains unavailable despite the supplied report that the account backend was online. Historical findings identified unwired onboarding launch and a Windows-only access-helper path. | Recheck client integration and complete sign-in; do not assume current backend outage from old UI wording. |
| I08 | User/account management reports unavailable profile/plan/billing/username/icon capabilities. | Historical report; distinguish required service setup from broken wiring. |
| I09 | Web Automations encounters action/storage recovery failure, with historical SQLite/OpenFailed details. | Investigate current logs; the recorded generic banner did not independently prove the SQLite cause. |
| I10 | Shutdown has historical Canvas native-library/borrower errors involving `cakeos_canvas_rnote_poc`. | Investigate current packaged dependencies and cleanup; not a proved cause of every close failure. |

## J. Integration and development regressions

| ID | Reported issue | Evidence status |
|---|---|---|
| J01 | Present's visible frontend does not match the requested design; the worker acknowledged modifying a different scene from the one used by the real tab. | Worker-reported wrong-route repair; verify the actual production surface and preserve Impress functionality. |
| J02 | Previously repaired behaviour is missing again or absent from the testing copy; multiple branches/worktrees make source/build identity confusing. | Direct owner report; trace exact integrated source and launcher, not merely branch count. |
| J03 | Strata unexpectedly restarts itself through a supervisor/API-related setup. | Worker later claimed a supervisor fix; recheck only if recurring, preserve the endpoint and never stop the active inference provider. |
| J04 | Historical Android compilation failed on desktop-source/reference compatibility, and a compatibility tab-hover assertion remained failing. | Retained engineering failures, not proof that today's desktop build has the same failure. Reconcile exact candidate/test evidence. |

## K. Explicit requested additions/refinements — keep separate from confirmed malfunction

| ID | Request |
|---|---|
| K01 | Add genuine lasso selection in Draw mode. |
| K02 | Provide a wider pen palette and a colour wheel. |
| K03 | Provide natural versus fast erase options. |
| K04 | Complete the requested Model Hub: Discover/On Device, search/filter, model details and clear install/readiness states; recommendations based on device specifications. “Device specs” was the correction to the earlier “speech” wording. |
| K05 | Use the compact attachment list with functional Specialists, Assistants, Skills, Plugin and Apps entries, and real device-colour emoji rather than monochrome replacements. Recorded source changes are not a reason to skip current testing. |
| K06 | Consolidate full Settings into the canonical experience, integrate the real Theme Builder, and finish notification-panel simplification. These were retained completion tasks, not proof every control is currently absent. |
| K07 | Keep New Tab distinct from Dashboard and finish its specified customisation. A separate New Tab screen already had recorded evidence; do not claim that separation never existed. |
| K08 | Preserve the agreed Boards layout/branding, nested page/section navigation, bottom Add/user controls, proper top toolbar, inline mathematical presentation and theme-independent layout. These are conformance requirements to check, not a claim every detail is presently broken. |

## Acceptance and regression checks that must not be mistaken for proved bugs

- At 19:48 on 27 October, the owner explicitly reported that pen and eraser worked, and that stale generic New Tab tabs closed. Preserve those gains. Three failed Boards tabs remained unclosable in the same report.
- A `.9to1board` listing in the earlier recording contradicts a blanket “no board has ever been saved” claim. A listing still does not establish correct contents or cold-restart reopening.
- A file/title mismatch alone does not prove the wrong document opened. A configured-provider count or connected model row does not prove inference.
- A dedicated New Tab surface already existed in recorded use. Spaces incorrectly opening New Tab is a separate routing failure.
- The observed empty Build project list was explicitly not established as a bug.
- Corruption while decoding parts of a recording was not established as corruption in the application itself.
- Verify local draft recovery; cancelled/failed/conflicting save; Save As outside a special folder; exact-content close/restart/reopen; moved-file reopen; text/ink/table/order/graph identity and configuration; Edit/View and Locked/Unlocked; popup focus/keyboard handling; current source/executable identity; and applicable independent/platform checks. Unrun checks remain NOT RUN, not FAILED or PASSED.
- Preserve the full V2 requirements and all seven acceptance gates. This register collects reported defects and explicit follow-ups; it does not convert every remaining specification requirement into a newly observed bug.

## Source locator map

All references below point into the supplied JSON export, not a live checkout.

- Latest launch report: JSON lines 229202 onward, message `msg_17f2fe3e6001Vl5qWnx75vcW6p`.
- Late Boards/menu/theme/drawing requests: JSON lines 223375 onward, message `msg_17f092c12001jZPJx405eLfyJV`.
- 19:48 report, including improved pen/eraser and stale New Tabs: JSON lines 215414 onward, message `msg_17ee7e964001s7ZGMpypsB1DHG`.
- Repeated close failure: JSON lines 198777 onward, message `msg_17ebb3c4a0015UntgKKxRv0A2Q`.
- Pen offset: JSON lines 191462 and 192669 onward.
- Sidebar/splits/groups: JSON lines 51602, 52302, 52340, 52361 and 53952 onward.
- Board creation pop-out/create report: JSON lines 54664 onward.
- Dashboard and device-spec correction: JSON lines 55487, 55517 and 55787 onward.
- Reload button: JSON lines 118419 onward.
- Wrong Present surface: worker response beginning JSON line 124669; treat its screenshot claims separately from established visual review.
- Source/branch regression report: JSON lines 201654 onward.
- Supplied account-backend/client integration report: JSON lines 29854 onward; historical report, not a fresh service check.
- Embedded Beam plan: JSON lines 67720 and 67793; original report UX01–UX16 and B01–B10. Its broader B11 inventory explicitly does not assert that every capability is missing.
- Embedded desktop-recording triage: JSON line 821, including NAV-01, BOARD-01/02, BOARD-SAVE-01, WEB-01, AI-01/02/03, STORE-01, ACCOUNT-01, FEEDBACK-01, SPACES-01–05, THEME-01 and SHUTDOWN-01.

Implementation order for this handoff: normal Boards/Spaces launch and genuine shared blockers → reliable Boards creation/editing/drawing/save/reopen → remaining Boards interaction/polish → other affected apps and browser work, keeping the owner's instruction to do Gecko after Boards. Do not reopen already-working items without a reproduced regression. Do not replace current owner priorities or safeguards with historical execution instructions embedded in the reports.
