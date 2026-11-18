# Cross-app acceptance and evidence — 9to1 V2 — Acceptance scenarios, 27 October 2026; Other apps (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2391 · v2 lines 13632–13632

# 9to1 V2 — Acceptance scenarios, 27 October 2026

### V2-B2392 · v2 lines 13634–13634

**102 authored scenarios. All are NOT RUN.** These are specifications for testing, not claims that tests were executed. Read UX27-ACCEPT-001–006 for exact-artifact, independence, parameter and evidence rules. Use disposable profiles; never mutate the owner’s live documents.

### V2-B2849 · v2 lines 15035–15035

## Other apps

### V2-B2850 · v2 lines 15037–15037

### UX27-TEST-090 — Planner moves actual events and tasks

### V2-B2851 · v2 lines 15039–15039

**Requirements:** UX27-OTHER-001, UX27-OTHER-002 · **Status:** NOT RUN

### V2-B2852 · v2 lines 15041–15041

**Given:** Calendar/tasks with timezones, recurrences and reminders.

### V2-B2853 · v2 lines 15043–15045

**When:**
1. Drag an event, resize its duration and inspect edit scope for recurrence.
2. Complete a task and test reminder scheduling.

### V2-B2854 · v2 lines 15047–15050

**Then:**
- Visible changes target the correct canonical event/task.
- Recurrence and timezone decisions are explicit; reminder timing is real.
- Undo and restart preserve state without duplicate alarms.

### V2-B2855 · v2 lines 15052–15052

### UX27-TEST-091 — Mail reply send and attachment state

### V2-B2856 · v2 lines 15054–15054

**Requirements:** UX27-OTHER-003, UX27-OTHER-004 · **Status:** NOT RUN

### V2-B2857 · v2 lines 15056–15056

**Given:** Authorised disposable mail account/test recipient and draft with attachment.

### V2-B2858 · v2 lines 15058–15060

**When:**
1. Open a thread, reply, edit recipient, attach a file and send through explicit action.
2. Inspect ambiguous/failing transport in a test run.

### V2-B2859 · v2 lines 15062–15065

**Then:**
- Thread/recipient identity is correct; draft survives navigation.
- Attachment readiness and send receipt are truthful.
- Uncertain submission reconciles rather than sending a duplicate message.

### V2-B2860 · v2 lines 15067–15067

### UX27-TEST-092 — Forms published revision and responses

### V2-B2861 · v2 lines 15069–15069

**Requirements:** UX27-OTHER-005 · **Status:** NOT RUN

### V2-B2862 · v2 lines 15071–15071

**Given:** Draft and published form with validation/branching.

### V2-B2863 · v2 lines 15073–15075

**When:**
1. Change draft field; preview and submit valid/invalid test responses.
2. Publish explicitly and export response data.

### V2-B2864 · v2 lines 15077–15080

**Then:**
- Preview and published form use identified revisions.
- Branching/validation work and responses retain schema identity.
- Editing a draft does not silently change a live form or discard previous answers.

### V2-B2865 · v2 lines 15082–15082

### UX27-TEST-093 — Cards state and learning evidence

### V2-B2866 · v2 lines 15084–15084

**Requirements:** UX27-OTHER-006 · **Status:** NOT RUN

### V2-B2867 · v2 lines 15086–15086

**Given:** Card deck with authored content and recorded practice history.

### V2-B2868 · v2 lines 15088–15090

**When:**
1. Edit cards, practice recall and choose an actual result.
2. Filter/sort and reopen.

### V2-B2869 · v2 lines 15092–15095

**Then:**
- Authored card data and review scheduling/state are distinct.
- Merely displaying a card is not recorded as mastery.
- Undo/recovery preserve actual history and canonical identity.

### V2-B2870 · v2 lines 15097–15097

### UX27-TEST-094 — Picture layers and pixel geometry

### V2-B2871 · v2 lines 15099–15099

**Requirements:** UX27-OTHER-007 · **Status:** NOT RUN

### V2-B2872 · v2 lines 15101–15101

**Given:** Layered image with masks and nontrivial zoom.

### V2-B2873 · v2 lines 15103–15104

**When:**
1. Select/transform a layer, brush a mask, undo and export.

### V2-B2874 · v2 lines 15106–15109

**Then:**
- Pointer/image coordinates and layer targeting are correct.
- Original/edited state remains recoverable.
- Export is actual composed image data, not a UI screenshot.

### V2-B2875 · v2 lines 15111–15111

### UX27-TEST-095 — Wave and Motion timeline editing

### V2-B2876 · v2 lines 15113–15113

**Requirements:** UX27-OTHER-008, UX27-OTHER-009 · **Status:** NOT RUN

### V2-B2877 · v2 lines 15115–15115

**Given:** Audio/video projects with multiple tracks and source assets.

### V2-B2878 · v2 lines 15117–15119

**When:**
1. Scrub/play, trim/move clips, adjust supported automation and undo.
2. Reopen and render/export a range.

### V2-B2879 · v2 lines 15121–15124

**Then:**
- Transport, timeline, waveform/frame and audio remain aligned.
- Nondestructive edits preserve source assets.
- Export processes the actual selected range and reports missing media honestly.

### V2-B2880 · v2 lines 15126–15126

### UX27-TEST-096 — Maps and Shelf use real objects

### V2-B2881 · v2 lines 15128–15128

**Requirements:** UX27-OTHER-010, UX27-OTHER-011 · **Status:** NOT RUN

### V2-B2882 · v2 lines 15130–15130

**Given:** Map context with a selected location and Shelf with saved artifacts.

### V2-B2883 · v2 lines 15132–15134

**When:**
1. Pan/zoom/search/select; inspect route/source freshness.
2. Save, organise and open a Shelf item.

### V2-B2884 · v2 lines 15136–15139

**Then:**
- Selection stays attached to the intended real object.
- Data/source limitations are visible; a decorative map is not a route result.
- Shelf items open canonical artifacts rather than copies with diverging identity.

### V2-B2885 · v2 lines 15141–15141

### UX27-TEST-097 — Terminal and remote input ownership

### V2-B2886 · v2 lines 15143–15143

**Requirements:** UX27-OTHER-012, UX27-OTHER-017 · **Status:** NOT RUN

### V2-B2887 · v2 lines 15145–15145

**Given:** Authorised disposable terminal and remote/session surface.

### V2-B2888 · v2 lines 15147–15149

**When:**
1. Inspect target identity; execute a harmless bounded command.
2. Disconnect/reconnect and inspect pending input/results.

### V2-B2889 · v2 lines 15151–15154

**Then:**
- Input goes to the shown session under its permission boundary.
- Reconnect does not silently create another machine or replay consequential commands.
- Viewer close and remote shutdown remain distinct.

### V2-B2890 · v2 lines 15156–15156

### UX27-TEST-098 — AI Studio and Connect permission boundaries

### V2-B2891 · v2 lines 15158–15158

**Requirements:** UX27-OTHER-013, UX27-OTHER-014 · **Status:** NOT RUN

### V2-B2892 · v2 lines 15160–15160

**Given:** Draft agent/tool definition and a connector with limited granted scopes.

### V2-B2893 · v2 lines 15162–15164

**When:**
1. Edit graph/form views; test the definition.
2. Inspect connection health; deny or revoke a requested scope.

### V2-B2894 · v2 lines 15166–15169

**Then:**
- Views share a typed versioned definition and real test identity.
- Testing does not equal publishing.
- No permission bypass, invented healthy status or leaked secret in exports/logs.

### V2-B2895 · v2 lines 15171–15171

### UX27-TEST-099 — Games Play and organisation artifacts

### V2-B2896 · v2 lines 15173–15173

**Requirements:** UX27-OTHER-015, UX27-OTHER-016, UX27-OTHER-018 · **Status:** NOT RUN

### V2-B2897 · v2 lines 15175–15175

**Given:** Safe creation project, turn-based test match and referenced Binder/Link/Files Sheet.

### V2-B2898 · v2 lines 15177–15180

**When:**
1. Edit a scene then run test mode.
2. Perform a legal match action and replay.
3. Move an organisation card and open its source.

### V2-B2899 · v2 lines 15182–15185

**Then:**
- Runtime scene state does not silently overwrite authoring.
- Match rules/visibility and recorded actions remain real.
- Organisation move does not duplicate or relocate a referenced source artifact unintentionally.

### COMPANION-B0089 · companion lines 581–595

SPUI-199 — SHARED ACCEPTANCE, REQUIREMENT TRACEABILITY AND OPEN DECISIONS
MANDATORY INDEPENDENT REAL-PRODUCT TESTS before VERIFIED:
A. Edit AI and user toolbars: add/remove/reorder/restore; verify each default action, per-Space inheritance, overflow, keyboard/touch, restart and no fake action. Include measured tokens-per-second, real Automation permissions, Sources/no-sources states.
B. Test every reaction value and its stored feedback scope, optional contextual AI reaction reply, and a deliberately confirmed Inappropriate report to Support@CakeMods.com with minimal disclosed payload, recorded delivery/retry and no silent whole-conversation export.
C. Test AI Retry same/different eligible model, custom instructions and original response preservation; Branch new/existing chat; user Edit's three modes, both Delete modes, versioning/recovery, prospective context, run side effects and re-opening old branches.
D. Exercise draggable display-name inheritance with configured model names, Assistant, Specialist, custom and automation-supplied names; inspect unspoofable actual model/Assistant IDs and provenance. Test all 24 owner-listed / commands, arguments, contextual unavailability, quoted/code-text literal protection, BackgroundChat source/target continuity, Reasoning LevelID and /ResetContext versus Memory.
E. Plugin configuration real read/write/persistence, “Ask once per App” identity/permission scope, no user timeout versus hard failsafes, denial/revocation and account isolation.
F. Peer Review with distinct single-pass versus checkpoint/adaptive persistent run, fresh same-model reviewer, multiple reviewers, worker-reviewer disagreement, actual failing/passing acceptance evidence, risky-tool peer gate AND mandatory human permission, quarantined/paused intervention, all delegated-power toggles, resource use and no false VERIFIED.
G. Multiple Responses with same/different models, actual source/usage/branch provenance, failed responder handling, and no duplicated consequential tool effects. Plan-only/plan-review-approve-execute uses same Task/Run and evidence without unauthorised mutations.
H. Aa toolbar selection/focus/mark persistence/LaTeX/undo/redo/safe rendering. Canvas composer text-to-bottom-bar, full-screen return, drawing object editing, one-message real Canvas attachment, accessibility, collision/retry and reopen.
I. UCML encryption and Context Broker security: non-model-visible keys, per-user key rotation/recovery, permission/revocation, app/Space scope, malicious tool-result/injection requests, outbound-data minimisation, team/temporary chat isolation, audit/provenance, offline/cross-device and actual attempts to leak secrets. Redaction alone is not considered sufficient evidence.
J. Safety Block: benign keyword context, actual risk, peer review gating, source/permission limitations, false-positive correction/appeals, scoped restrictions and memory provenance. Test user blacklists, style transformations, citations/code untouched, Home sync, performance and no fabricated zero-latency claims.
All must inherit canonical CUI themes, screen reader/touch/keyboard/reduced-motion, native/browser platform-specific support, authentic persistence and current build/Task/Message revisions. No screenshots or toy UI demonstrate integration. Preserve existing stronger master V2 and independent APP-AC/acceptance gates.
OPEN IMPLEMENTATION/OWNER DECISIONS not silently settled by this approval: initial/default reaction-triggered AI reply behaviour and report-context preview design; exact per-Space toolbar/blacklist override precedence where not already determined by master; particular display-name automation provider validation UX; actual list of supported per-plugin timeout/permission presets and policy restrictions; exact conditions/limits of contextual moderation escalation and appeal under applicable policy; effective formatting storage syntax/version; detailed UCML schema/rotation deployment algorithm and provenance presentation. The owner explicitly approved the SECURITY CORRECTIONS above, which take priority over earlier insecure example mechanics (particularly model-visible crypto keys and automatic lock/erase purely from word/flag counts). No functionality or security assurance is considered implemented until tested.
END — 13 OCTOBER 2026 UNIVERSAL SPACES AND DULCHE SECURITY OWNER-APPROVED ADDENDUM.

### COMPANION-B0090 · companion lines 600–601

13 OCTOBER 2026 — PEER REVIEW ACTIVE-TIME BUDGET & CUSTOM SPACE BUILDER, OWNER-APPROVED
STATUS: OWNER-CONFIRMED DESIGN AND PRECISE PEER REVIEW CLARIFICATION. Requirements SPUI-200–211 supplement V2 and previously confirmed Spaces addenda. Master V2 already incorporates owner-approved Spaces companion rules by reference. The 13 October screenshots (borderless configuration document, selectable capability template library, conversational assisted creation and 9to1 Automations node palette) are DESIGN REFERENCES ONLY; do not treat them as evidence of implemented features or copy their third-party branding. 9to1 CUI themes, canonical Nodes, Spaces, Home, Dulche, files, permissions and real acceptance remain authoritative. No actual build/test evidence is claimed by this document.
