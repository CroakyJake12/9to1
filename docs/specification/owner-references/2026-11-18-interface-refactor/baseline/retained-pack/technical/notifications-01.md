# Notifications and ecosystem activity — Notifications — activity history; Notifications (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0437 · v2 lines 2244–2244

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Notifications — activity history

### V2-B0438 · v2 lines 2247–2247

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Notifications includes Home’s ecosystem activity/timeline surface. It is not Planner’s calendar. Activity history MUST aggregate authorised typed activity from apps/services such as Agent/Automation runs, package/update events, permission/trust decisions, important artifact changes, Mesh/device events, notifications and other meaningful system activity.

### V2-B0439 · v2 lines 2250–2250

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Each Event MUST have a stable EventID, timestamp, source app/service, event type, actor/caller identity where relevant, object/entity references, severity/category, human-readable summary and typed deep-link/action references where available. Activity history MAY reference RequestID/ActionGraphID for AI/agentic activity.

### V2-B0440 · v2 lines 2253–2253

V3 editorial amendment: V3-OWNER-003. Original wording is preserved in the replacement ledger.

Activity history MUST preserve source provenance and MUST NOT invent event history from generated summaries. Home MAY generate a concise natural-language digest of events, but the underlying event records remain the authority.

### V2-B0505 · v2 lines 2521–2521

Notifications

### V2-B0506 · v2 lines 2524–2524

Home owns the native 9to1 notification service/centre. Apps publish typed notifications containing NotificationID, source app/service, category/severity, title/body, created time, read/dismissed state, related entity references and typed actions/deep links. Notifications MUST NOT encode consequential behaviour only as arbitrary executable callbacks.

### V2-B0507 · v2 lines 2527–2527

Home MUST support notification history, read/unread state, dismiss/clear subject to retention policy, per-source/category preferences and deep-link actions. Where Windows/9to1-OS system notifications are available, Home MAY bridge eligible notifications to the OS while retaining the canonical 9to1 notification record/state.

### V2-B0508 · v2 lines 2530–2530

Shared search and Go indexing
