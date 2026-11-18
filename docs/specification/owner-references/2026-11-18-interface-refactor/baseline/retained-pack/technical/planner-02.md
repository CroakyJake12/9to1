# Planner — calendars, burrows and tasks — Planner Burrows contract; Planner calendar and task scheduling (part 02)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2245 · v2 lines 13240–13246

LE-11 — Planner Burrows contract
A Burrow is a named planning context over existing Planner entities, not a duplicate calendar engine or a copy of events/tasks/assignments. It has stable BurrowID, name/icon, owner/scope, ordering, archive state and revision. Native items may have one BurrowID or be Unassigned; the All view includes authorised items across burrows without duplicate identities. Existing items remain Unassigned unless the user assigns them or an existing explicit association provides a deterministic migration.
Study has one canonical Study burrow per owning Study scope, created idempotently and referenced by ID rather than the display name. A custom Minecraft burrow is an independent user-created context. Study's Planner button opens its Study burrow inside the real Planner surface, and Open in App preserves the burrow selection and canonical item identities.
Users MUST be able to create, rename, reorder, select, set a default, archive/recover and delete a burrow; assign/reassign/unassign existing events, PlannerTasks, assignments and other supported planning items; and filter normal Planner views by burrow or All/Unassigned. New items created within a selected burrow inherit that assignment. Moving between burrows changes organisation only, not event time, recurrence, provider calendar, attendees or reminder identity.
Provider-backed items retain provider IDs and permissions. Burrow membership may be local 9to1 metadata and MUST NOT silently move or duplicate the external item. Recurring-item assignment requires an explicit occurrence/series scope using Planner's existing recurrence rules. A burrow's visibility never grants access to otherwise private contained items.
Deleting a burrow MUST preview its item count and reassign its items to a chosen authorised burrow or Unassigned; it must not delete the events/tasks/assignments. Archive hides the context without destroying membership. Reminders continue under existing item policies, regardless of whether a burrow is currently visible.
Acceptance: keep Study and Minecraft items distinct while All shows both once; create and move items without changed IDs/times/reminders; restart; edit a recurring series with explicit scope; handle a read-only external event's local grouping; delete/recover a burrow without losing planning items; verify embedded and standalone Planner use the same state.

### V2-B2285 · v2 lines 13403–13403

### UX27-OTHER-001 — Planner calendar and task scheduling

### V2-B2286 · v2 lines 13405–13405

**B/D.** Day/week/month/agenda views are projections of the same canonical Events and PlannerTasks. Clicking an empty time slot opens a draft with the clicked date/time; dragging previews start/end before commit; resizing changes duration with visible time labels. A task dragged from an unscheduled list into the calendar schedules that same TaskID, not a duplicate Event. Removing its scheduled window does not delete the task or due date.

### V2-B2287 · v2 lines 13407–13407

Recurring edits explicitly choose occurrence/following/series scope. All-day items remain date-based and do not drift with timezone conversion. Undo, cross-view updates, reminders and reopen use the same state. Overlaps and unavailable calendars are visible rather than silently shifted to a different time.

### V2-B2288 · v2 lines 13409–13409

### UX27-OTHER-002 — Planner schedules, Burrows and reminders

### V2-B2289 · v2 lines 13411–13411

**B.** Schedule progress shows actual current item, elapsed and remaining time, with the timezone and paused/ended state where relevant. Burrows retain the baseline’s intended organisational/task semantics rather than becoming a second calendar database. Reminder snooze changes reminder delivery, not the underlying event’s start or due time.

### V2-B2290 · v2 lines 13413–13413

Navigation from a reminder opens its canonical source entity. Completing a task updates every view and its history; dismissing a notification is not task completion. Source-linked places/routes and artifacts remain canonical references with clear unavailable states.
