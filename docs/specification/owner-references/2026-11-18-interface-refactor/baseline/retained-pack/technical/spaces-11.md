# Spaces — conversations, workspaces and custom Space Builder — F. REVISION BANK — A UNIFIED RESOURCE EXPERIENCE OVER ACTUAL APPS (part 11)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### STUDY-B0009 · study lines 52–58

F. REVISION BANK — A UNIFIED RESOURCE EXPERIENCE OVER ACTUAL APPS
STUDY-UI-221 — Preserve existing LE-09 Revision Bank as the catalogue of canonical first-party artifacts, NOT a new file store. It contains owner-authorised created/enrolled sources including Cards flashcards, Forms quizzes/tests, Write notes/documents, past papers, Present slides, Canvas objects, Boards pages, other permitted resources; linked resources remain owned/edited/saved/permissioned by their actual application.
STUDY-UI-222 — Default navigation-category TABBER is Recommended | All | Flashcards | Notes | Papers | Quizzes; this is a category filter within Revision Bank, NOT the overarching Spaces horizontal Tab View. Provide custom sections such as Homework, stable category membership and optional auto-added uncovered resource categories as defined in LE-09. Recommended draws from recorded difficult/neglected topics and linked sources, not fake recommendations.
STUDY-UI-223 — Improve resource discovery with clear search, filtering and sorting by appropriate subject, topic, type, date/status and associated source; scannable resource titles, icons, preview metadata, owner/location when relevant, and useful empty/loading/unavailable states. Avoid turning the Bank into a huge card collage or burying navigation behind repeated popovers. Resource previews/open actions must use real owners and permissions; if no safe/authorised preview exists, explain.
STUDY-UI-224 — Retain + Create New right-aligned on category-tabber row, with explicit actions Describe a Resource, Flashcard (Cards), Quiz (Forms), Document (Write), Presentation (Present), Canvas, Board (Boards), Other (Dev). Create through canonical APIs and maintain same underlying artifact identity; failed/cancelled creation cannot manufacture a bank entry. An imported existing resource is enrolled only through authorised reference; Bank category mutation must not copy/degrade its original.
STUDY-UI-225 — Clicking resource opens its REAL owning-app viewer/interactor embedded in Spaces Tab View, normally in viewing/interacting mode first, with a visible permitted Edit transition; Open in App remains accessible from the resource, tab context menu and keyboard-equivalent option. Moving from embedded to standalone must preserve exact canonical ArtifactID, current revision and permissions. A flashcard set stays a Cards object and a quiz stays a Forms object. Removing from Revision Bank removes its catalogue link, not the source document/card/quiz; source deletion remains a distinct owning-app operation.
STUDY-UI-226 — Make creating and reviewing Cards lightweight despite Cards' richer full editor; allow quick deck creation, clear front/back, next/flip/rate interactions and contextual focus on difficult items through the existing Cards app engine and its red/amber/green personal review records. Use Cards' real editor when richer formatting, drawing, graphs or bulk editing is needed; don't clone a second reduced Card store. Persist card attempts/ratings linked to TopicIDs as attributed Study evidence as established in LE-08/LE-10.

### STUDY-B0010 · study lines 61–64

G. PLANNER BURROW AND REVISION SCHEDULING
STUDY-UI-227 — Study's Planner sidebar item and relevant Home/topic actions open the ONE canonical Study Planner BURROW through actual Planner interface, embedded in a Spaces tab where appropriate, preserving BurrowID, event/task/assignment IDs, existing reminders, recurrence and permissions per LE-11. Do not build a parallel Study-only calendar or automatically move unrelated Planner events into Study.
STUDY-UI-228 — Where evidence recommends a review date or task, offer a clear action to schedule/re-schedule it in the Study burrow, with the linked SubjectID/TopicID and relevant source references. Suggested review timing should be based on recorded attempts/review evidence and documented policy, not fake deadlines. Do not silently create external events, modify recurrence, contact attendees or expose learning records to an external calendar without applicable user instruction/authorisation. A planned session can be reopened directly into its topic-bound Study work.
STUDY-UI-229 — Study Home should surface upcoming relevant work and review sessions via the same Planner records; changes in Planner or Study propagate to both. Mark overdue only when due dates are real and known; absent/unknown schedules must not create red warning visuals.

### STUDY-B0011 · study lines 67–70

H. SUBJECT-ADAPTED INPUTS, TEACHING, FEEDBACK
STUDY-UI-230 — While the shared conversation is the primary interaction, exercises should use subject-appropriate, editable and accessible real UI components: a maths problem may need structured equations, intermediate working, graphs and correctly marked final answer; a programming exercise may require syntax-aware code editor/input and, where safely supported, actual execution/evaluation through canonical Dev/allowed tools; an essay/legal-reasoning problem may require text response, criteria-based argument feedback and cited sources. A-level Maths, Computer Science and Law are ILLUSTRATIVE subject examples, not mandatory hardcoded subjects/boards. The interface should adapt without multiplying separate private editors.
STUDY-UI-231 — For objective/deterministic questions, prefer actual mathematical/structured validators when feasible; for qualitative LLM feedback, represent uncertainty, rubric/source basis and review/correction channels rather than inventing official marks or claiming examiner-level authority. Feedback should distinguish conceptual misunderstanding, missing steps, incorrect calculations, inconsistent reasoning and procedural mistakes when supported by visible working. Keep hints, attempts, explanation and marking distinct in stored evidence.
STUDY-UI-232 — Preserve human agency and normal questions: learner can ask “why?”, request an explanation, show own approach, ask for easier/harder practice or change source scope without losing the attempt or getting forcibly funnelled into a rigid quiz. Learning mode and progress are evidence-based helpers, not gatekeeping. Any goal/task/revision recommendation must be editable, dismissible or changeable using normal permissions.

### STUDY-B0012 · study lines 73–76

I. LAYOUT, THEME, STATES, ACCESSIBILITY AND CONTEXT
STUDY-UI-233 — Study Home, Subject Workspace and Study Session are separate active views within the same Spaces SpaceID/navigation scope; never render all three as long vertically stacked panels with inactive focusable editor under a dashboard, nor force an extra process/window. Preserve normal Spaces floating Summary anchored top-right, optional app/browser/artifact tabs, collapsible contextual sidebar and bottom shared composer/Voice pill. Overlays and menus use CUI styled popovers/dialogs without native Avalonia theme leaks. Expand main reading/work area sensibly when sidebar is hidden or viewport narrows.
STUDY-UI-234 — All screens must have clear major headings/actions, generous spacing, consistent icons, readable Montserrat where supported, accurate hover/focus/pressed/selected/disabled/validation states, genuine cursor/click targets, accessible names, screen reader reading order, reduced motion, keyboard and touch alternatives. For the topic progress RAG palette, redundant text/shape/icons explain state without relying only on colour. The top 3 learning recommendations and upcoming work must not obstruct normal dialogue/composer.
STUDY-UI-235 — Empty/first-use, no sources, source unavailable, offline local-only, provider unavailable, denied permission, failed quiz/marking, unavailable embedded app and missing Planner/Files state must all be truthful and recoverable. Nonexistent support or evidence cannot be transformed into optimistic “Complete”/green UI. Preserve pending edits, attempts, ongoing authorised runs and data through navigation/restart according to shared Spaces/Dulche state contracts.

### STUDY-B0013 · study lines 79–98

J. FULL ACCEPTANCE MATRIX — NOT VERIFIED AT THE TIME OF THIS SPEC
STUDY-AC-201 — Enter Study without a previous session: see genuine Study Home, not an empty Chat or editor underneath a hero. Click Resume/New/Subject/Revise recommendations and reach correct canonical destinations.
STUDY-AC-202 — In the default sidebar, verify New Chat, Subjects, My Progress, Planner (Study burrow), Revision Bank, THREE total Revise These slots and Recent Chats; then customise/reorder/collapse and confirm precedence/recovery without losing workflows.
STUDY-AC-203 — Create/open two distinct subjects with hierarchical topics; configure source/course metadata, link known specification where supplied, rename topic and verify stable TopicID/reference/attempts.
STUDY-AC-204 — Open a topic workspace and follow linked notes, cards, quiz/test, source, status and new topic-bound session; verify source permissions and no unrelated information leaks.
STUDY-AC-205 — Exercise all four Learn, Practise, Test, Review modes within the SAME canonical Study conversation; switch during session without dropping messages, drafts, progress, Voice or sources. Verify mode changes actually alter activity workflow.
STUDY-AC-206 — In Learn, ask source-based follow-up and inspect a worked example + optional understanding check; ensure showing examples alone does not improve mastery.
STUDY-AC-207 — In Practise, submit correct, incorrect and partial step-by-step answers; receive meaningful feedback, show hints, adjust difficulty and verify stored attempt/proficiency evidence.
STUDY-AC-208 — In Test, start/submit/mark an actual Forms-backed assessment with rubric, timing policy, results and explicit uncertainties; reopen same canonical results/IDs and validate no duplicate records.
STUDY-AC-209 — In Review, practise an overdue and a recorded weak topic via real Cards or stored attempts, update review history appropriately; flip-only content must not produce false mastery.
STUDY-AC-210 — Toggle My sources only versus Wider knowledge allowed; ensure closed-corpus genuinely blocks disallowed outside retrieval, provenance distinguishes input materials/external sources, citations don't fabricate support and source policy persists as appropriate.
STUDY-AC-211 — My Progress shows Not assessed neutral, Needs work red, Developing amber, Confident green with actual evidence; self-reported confidence is separate. New learner with no answers must have no false green/RAG proficiency.
STUDY-AC-212 — Across subjects show no more than three Revise These items, correctly ordered with explanatory reasons; revise a selected recommendation and verify change only after genuine attributed evidence.
STUDY-AC-213 — Revision Bank category tabs and search/filter/previews work; + Create New genuinely creates Cards, Forms, Write, Present, Canvas, Boards and Dev resource types through owning services; restart, reopen/edit embedded and standalone and verify same artifact identity.
STUDY-AC-214 — Enrol and remove a linked resource from Bank without deleting the owning document; follow rename/move/revocation and preserve authorised state/empty/unavailable behaviour.
STUDY-AC-215 — Use Planner Study burrow to schedule topic review; check the same item in standalone Planner, change date and see Study Home update without duplicate events/reminders.
STUDY-AC-216 — Exercise realistic subject-adapted maths working, code input and a written-response marking scenario where supported; confirm real parsers/evaluators and labelled uncertain feedback rather than fake grading.
STUDY-AC-217 — Close/reopen and recover an interrupted attempt/conversation, switch Spaces/tab views/themes, collapse sidebar, open Summary and start/stop shared Voice Mode without losing canonical state.
STUDY-AC-218 — Verify keyboard/focus, screen-reader, touch, desktop/narrow layouts, contrast, reduced motion, and CUI/Professional/Glow inheritance. Validate actual packaged native and supported web product surfaces, not static mockups.
STUDY-AC-219 — Independently verify real Home/Dulche/Spaces/Cards/Forms/Planner/Files typed API paths, permission denial, local/cloud consent, provenance, model routing and safe failure/recovery. Run full seven APP acceptance gates with genuine build identity and artifacts; if unavailable report BLOCKED or NOT RUN, never fabricated PASS.

### STUDY-B0014 · study lines 101–109

IMPLEMENTATION / OWNERSHIP MAP
Spaces/Study owns learning-state orchestration, Study Home/Subjects/topic views, source policy, contextual Study UI and calls to the shared progress/recommendations API.
Home owns shared CUI, model/usage/permission broker, tab/native embedding shared services, shared Productivity Engine and canonical UI prefabs.
Dulche owns AI reasoning/model/tool runtime, appropriate retrieval and task execution; no private Study model runner.
Cards owns flashcard content, viewing/editing, review ratings and user ReviewRecord IDs.
Forms owns formal quiz/test content, submission, marking and results.
Planner owns Study burrow and calendar/task/assignment identities.
Write/Present/Canvas/Boards/Dev/Files own the associated revision materials and canonical resource/data mutation; Study is catalogue/context overlay, not a duplicate editor.
All CUI controls must be genuine interactive and data-bound; no placeholder nav, no pretending source presence, status or completed work.

### STUDY-B0015 · study lines 112–114

OPEN ISSUES FOR LATER DESIGN REFINEMENT (NOT AN EXCUSE TO DROP APPROVED FEATURES)
Final pixel-level Study Home design and subject-page composition; per-course topic tree editor specifics; detailed maths/code/essay question widget variants; chosen evidence weighting/threshold/review interval policy; exact positioning/density of the four-mode selector; selection of source-policy defaults; concrete per-platform performance budgets. These require further spec refinement and must NOT be guessed as approved implementation-specific constants.
Do not treat this as the end of Spaces review, or as verification that any of these interfaces have been implemented. Continue one app/Space at a time according to the latest owner instructions.

### STUDY-B0016 · study lines 117–117

END — OWNER-APPROVED STUDY UI ADDENDUM, 13 OCTOBER 2026
