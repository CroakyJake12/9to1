"""Generate B4 evidence from the live canonical snapshot; never product authority.

Includes descriptive behaviour and wrongly formatted long headings, not only MUST
keyword hits. Candidate decomposition is for independent review, not a conformance
claim. Execution outcomes are stored separately from requirement states.
"""
import csv
import hashlib
import json
import re
from collections import Counter
from pathlib import Path

BASE = Path(__file__).resolve().parent
SOURCE = BASE.parent / "source"
PARAGRAPHS = json.loads((SOURCE / "paragraphs.json").read_text())
MANIFEST = json.loads((SOURCE / "manifest.json").read_text())
ROOT = Path("/workspace/team-b-worktree")
REVISION = MANIFEST["revisionId"]
BASELINE = "98a08827c9fbc486fe987da73f4aee8398120d31"

# IDs below identify reproducible procedures, not existing executable tests.
PLANS = {}
def plan(key, title, steps, oracle, dependency="B1 browser runtime; Team A canonical domains; C4 authenticated non-production services", kind="runtime"):
    PLANS[key] = {"test_id": key, "title": title, "kind": kind,
        "preconditions": "Use authorised non-production deployment with recorded frontend/backend identities and actual declared browser versions. Seed dedicated B4 owner/editor/viewer/other-account principals and populated artifacts through real APIs. Record IDs, revisions, grants and content before each action. No production data or live charges.",
        "steps": steps, "expected": oracle, "dependencies": dependency,
        "outcome": "NOT_RUN", "requirement_state": "BLOCKED", "evidence": [],
        "replay_rule": "Replay each consequential UI operation through the owning typed API with the same principal, payload, base revision and idempotency key. Compare content, IDs, revisions, permissions, events and side effects. Repeat denied/read-only requests and stale/duplicate requests; do not assert only message text."}

plan("B4-GOV", "Authority, donor and release evidence", [
    "Read recorded canonical revision and recheck the live revision before release; compare requirement inventory to source and preserve unresolved decisions.",
    "Verify canonical remote, accepted head, active claims, disjoint ownership, integrator and readback before integration.",
    "Inspect donor source/license and enumerate every meaningful visible command/workflow/QoL capability; assign Preserve/Replace/Not applicable with canonical basis.",
    "Review every changed line and downstream interaction independently; rerun affected suite and exact integrated candidate acceptance.",
    "Inspect deliverable manifests, commit/hash/deployment identity, actual test discovery/execution/exit logs, screenshots/traces and measured workloads; retain every failed/not-run gate."],
    "Subjective Test, Objective Test and Definition of Done all require real integrated behavior. No mock, build, shell, source presence or worker assertion promotes completion. No new production routing, billable services, live charges or destructive infrastructure actions without relevant explicit authorisation.", "Root/B6/Team C release integration; donor source and licence evidence", "review")
plan("B4-IDENTITY", "Canonical state, schema and atomicity", [
    "Create named artifacts with duplicate permitted names, record canonical IDs, revisions and schema; rename, reorder and move while a second client has the artifact open.",
    "Save meaningful content, restart both clients and compare authoritative content/IDs/history; use unsupported schema and interrupted save in isolated data.",
    "Submit two incompatible operations against the same expected revision, then replay a committed operation with identical and changed arguments.",
    "Exercise a multi-object action with one denied/missing target and inspect every succeeded, failed, skipped and rolled-back item."],
    "Names/order/path never determine identity; schema unknowns preserved/rejected; user-visible operations atomic unless declared partial; conflicts recoverable without silent last-write-wins; replay does not duplicate effects.")
plan("B4-PERMISSION", "Broker, independent source permissions and privacy", [
    "Run each mapped UI/API action as owner/editor/viewer/other-account, with container rights and source rights varied independently.",
    "Exercise Decline, Block, Accept, 30-day Trust, temporary trust count/time and Always Trust warning using the shared authenticated broker; change caller identity, scope and risk, expire/revoke grants.",
    "Switch account and organisation, logout, revoke/expire sessions and revisit cached routes, previews/search/AI/export; inspect access checks and audited results.",
    "Select private content with cloud fallback configured; inspect actual requests, redacted logs and context source/revision. Disable AI and unavailable required privacy controls.",
    "Exercise configured retention/deletion propagation, export/correction/withdrawal and encryption/metadata-state reporting under the active privacy profile."],
    "IDs/host access/trust never grant forbidden object rights; required approvals fail closed; scope and impact known/unknown displayed honestly; access checked before future dispatch; caches separate; cloud access separate; secrets and prohibited source metadata not disclosed; all audit outcomes recorded.", "Team C identity/auth/entitlement/broker/privacy services; B5 integration; A shared permissions")
plan("B4-UI", "Mature desktop/narrow/touch and accessibility interactions", [
    "At equivalent desktop viewport compare populated product journeys to donor interaction models and native CUI information architecture.",
    "Repeat with narrow/touch layout, 200% scaling, supported themes/high contrast/reduced motion; inspect normal/selected/loading/read-only/conflict/missing-source and all hover/focus/disabled/error states.",
    "Complete each journey using keyboard and screen reader; inspect accessible names/outline, selection, menus, dialogs, visible focus and return focus.",
    "Type text while canvas shortcuts are available; exercise pointer/tap/long-press/drag alternatives, multi-select/clipboard, undo/redo, Escape innermost surface, panel collapse/resize and Save/Close/Back with AI bar.",
    "Open artifact deep links, refresh, Back/Forward, direct child links and restored account context; return from nested/embedded editors to identical selection/zoom/scroll."],
    "Usable mature product semantics and faithful content across declared browsers; no pane blocks essential controls; meaningful compatibility/error states; no simulated saves, inaccessible drag-only controls, original-edit ambiguity or discarded draft.", "B1 CUI browser runtime; B6 live browser/device/screen-reader access; supported browser/version policy remains OPEN")
plan("B4-RESOURCE", "Populated workloads and bounded work", [
    "Record declared thresholds/capability limits before execution; seed a large Files hierarchy, long Flow, nested Binder, branching Path, multi-source Replay, Sites page/CMS/asset lists and variant history.",
    "Measure cold/warm startup, authoring latency, scrolling, incremental change propagation, CPU/memory/network, background work and subscription counts at recorded sizes.",
    "Repeat open/close/navigation with embeds computing/media/capture; verify cancellation and no retained uncontrolled subscriptions.",
    "Exceed a declared query/dependency/evaluation/export/import/merge limit and inspect preflight plus exact failure/partial result; create recursive references safely in isolated data."],
    "Bounded paging/hydration/virtualisation and traversal; explicit limits; no silent truncation/full rescan per change/uncontrolled playback or computation; measured results compared only with approved declared thresholds.", "Team C/A capacity and supported workload policy; real runtime resource instrumentation")
plan("B4-RECOVERY", "Interrupted real operations and drafts", [
    "Interrupt actual hydration, collection edit, native import/export, Prototype evaluation, Path progress write, Replay finalisation and variant merge at controlled isolated checkpoints.",
    "Restart clients/backend test jobs; inspect persisted journal/revisions, original content, recoverable draft, actual pending/failed status and committed progress.",
    "Retry with same operation identity then reconnect a second client; independently read authoritative state, side effects and quota reservations.",
    "Run a controlled negative fixture that violates a persistence/permission guarantee and confirm the acceptance assertion fails; never inject faults into production."],
    "Originals/only-unsynced copies retained; no fabricated captured interval/completion or duplicated actions/quota; exact recoverable state; negative controls demonstrate sensitivity.")

plan("B4-FILES-SHELL", "Files donor shell, providers and ordinary operations", [
    "Open first-class Drive alongside accessible providers; exercise sidebar/tabs/history/breadcrumb/address, list/grid/details, selection/sort/group/columns, tags/favourites, context commands/properties/previews/search, archives and session restore.",
    "Create/rename/move/copy/duplicate/trash/restore/colour an ordinary folder before and after adding organisation objects; open folder from Project/Tree/Matrix and return without moving it.",
    "Disconnect a restored location; deny/remove a provider capability; check every affected UI and API command.",
    "Use colour presets and full arbitrary colour picker/reset on local/hosted/connected compatible folders; move folder and reopen by ID.",
    "Test clipboard/drag cross-provider intent and destination failure before cut-source deletion; registered context/open-with handlers, protected project metadata and explicit reference-versus-symlink semantics."],
    "Donor workflows preserved where applicable; unsupported capabilities explicit; unavailable locations retained; folder presentation linked to ID; private credentials absent from config/logs; no hidden shell injection or source deletion before destination commit.")
plan("B4-FILES-CONTINUITY", "Drive, push sync, collaboration and diagnostics", [
    "Browser create/upload populated artifact; desktop reopen/edit/save; browser reopen with backend content/revision readback.",
    "Create/rename/move/share/change metadata while both clients are open and an owning app has the file open; measure actual live event propagation without manual refresh.",
    "Disconnect, queue create/rename/move/delete/restore and local content changes, restart, reconnect from durable cursor and journal; replay events/operations.",
    "Submit incompatible raw-byte writes and hierarchy edits; inspect preserved revisions or deterministic conflict and owning-app durable revision sink.",
    "Use Sync now/item/folder/selection, pause/resume/retry/reconcile; inspect pending/conflict/errors, last contact, pinned/cache usage and local/base/cloud revisions."],
    "Same canonical objects and meaningful content across clients; ordered immediate incremental push; no generic semantic merges/full rescan/duplicate effects/LWW/data loss; structured diagnostics and LocationChanged preserve owning-app attachment.")
plan("B4-FILES-TRANSFER", "Authorised transfer, hydration, quota and integrity", [
    "Pick real browser-authorised files/folders and destination; reject selection permission, path traversal and unsupported operations; browser never claims arbitrary local filesystem access.",
    "Start large resumable upload; record TransferID, authoritative reservation/chunks; interrupt and restart; pause/resume/cancel/retry and inspect progress from backend.",
    "Run concurrent uploads near quota; induce checksum mismatch and metadata-commit failure in isolated test environment; inspect reservations/orphans and every result.",
    "Download protected bytes as allowed/revoked/cross-account principals; browser save denial/cancellation must remain distinct from hosted transfer status.",
    "Open CloudOnly, validate durable bytes before AvailableOffline; restart offline and test AlwaysAvailable cleanup protection; deny eviction of unsynced/stale/unverified revision."],
    "No upload completion until content+authoritative metadata commit; exact integrity; durable resumable identity; no orphan/quota duplication; permissions on protected routes; pinned/only-unsynced copies retained. No public r2.dev substitute.")
plan("B4-FILES-HISTORY", "Versions, Trash, ACLs and owning-app creation", [
    "Create each Folder/Write/Present/Data/Board/Canvas/Stack entry through New menu and typed API; inspect owner-created artifact and Files destination, Create Stack from existing folder without nested duplication.",
    "Edit durable revision, restore older revision then inspect newer history; trash/restore by same ID; purge with separate destructive approval and impact.",
    "Grant direct/inherited viewer/editor to stable principal; preview move across differently permissioned folders; inspect explicit configured inheritance outcome.",
    "Open Shared with Me as canonical reference; revoke while source remains open; test protected raw URL/link share expiry/revocation.",
    "Search and preview with current grants, namespaced metadata and registered read-only owner renderers; undo actual inverse mutation with fresh revision and then stale revision."],
    "Owner authority preserved; no cloned Shared with Me, unsupported document internals, semantic merge or implicit cascade purge; later history retained; direct/inherited policy explicit; source permissions rechecked; stale undo rejected.")

plan("B4-SITES-AUTHOR", "Source-backed editor, components, layouts and responsive tokens", [
    "Create template/framework project in canonical Sites Files directory; open existing Stack project in full/partial/preview-only capability and inspect real source mapping.",
    "Create/select/move/nest/duplicate/hide/delete/configure each registered core component category through canvas/tree/Inspector and owning API; inspect canonical structured content.",
    "Create linked reusable definition with exposed property/slot override; edit definition across pages then explicitly detach one instance.",
    "Exercise flow/stack/grid/wrap/alignment/gaps/margins/sizing/aspect/container/sticky/fixed/overlay/freeform layouts with declared support; set/reset only changed desktop/tablet/phone/custom breakpoint properties.",
    "Change semantic typography/colour/spacing/radius/shadow/breakpoint/container/variant/theme tokens; force component state in preview and verify saved runtime state unchanged."],
    "Source/user-owned structured project remains authoritative; stable component identities and ordinary editing through shared domain; sparse overrides; tokens update consumers without rewriting content; no screenshot canvas or destructive theme switch.")
plan("B4-SITES-SOURCE", "Dev/Stack round-trip and unsupported constructs", [
    "Open Main/Branch/Twig/Leaf exact ProjectID/DomainID/revision in Sites; change visual property and inspect normal reviewable source diff in same domain.",
    "Open same selection in Dev, modify supported source and reconcile back to Sites; use Dev Code | Preview with Sites provider.",
    "Introduce valid unsupported code and generated content with provenance; check Code-defined/partial capability and preservation.",
    "Rename/move Files project directory and asset; reopen Sites/Dev without export/import or anonymous fork.",
    "Run concurrent visual/source edits to same expected revision and inspect semantic conflict/history."],
    "Canonical Files/Stack identity/source preserved; no export-to-code requirement, flattened unsupported code or silent overwrite; selected domain and exact source revision retained.")
plan("B4-SITES-PREVIEW", "Editor/public isolation, manifests and last-valid preview", [
    "Seed homepage public header/footer/style and route-specific script, ordinary page and component/CMS preview; open all rendering contexts.",
    "Inspect actual editor DOM/styles and isolated preview context; compare preview/live route asset manifests, fonts and rendering with same revision.",
    "Switch interactive/full-screen/shareable preview; change valid source then invalid build, disconnect and pause; inspect Live/Building/Reloading/Failed/Disconnected/Paused status and retained last-valid render.",
    "Attempt public preview script access to editor/admin/session/storage; include cross-site customer-origin tests under the approved isolation design."],
    "Public chrome/scripts cannot leak to editor/unrelated route; preview faithful and revision-linked; failed render retains last valid content; customer/editor origin privilege separation independently tested. Shared paths alone do not isolate browser origins.", "C4 real builds/preview deployment; A preview contracts; SH-08/SH-13 customer isolation design OPEN")
plan("B4-SITES-GRAPH", "Real simple/node behavior and trusted boundaries", [
    "Configure Button → Navigate in Inspector, open Node Editor and execute unchanged behavior; create click → permitted query → state update → show result.",
    "Exercise registered event/condition/action classes, typed ports, validation, grouping/comments/copy/paste/undo/Draft/Published semantics and source mapping.",
    "Debug actual runtime path with values/timing/errors/provider failures and supported breakpoints; configure runtime states and simple/advanced animation.",
    "Attempt unsafe client↔server flow and credential/private Data operation; inspect pre-publish diagnostics, actual trusted API checks and browser bundle/log redaction."],
    "Shared NodeGraph semantics; actual executed path inspectable; no duplicate automation product, width-node substitute for responsive layout, browser secret or client-only access control.")
plan("B4-SITES-CMS", "CMS, dynamic routes, rich content and bindings", [
    "Create BlogPost/Product collections, stable typed records and one-to-one/one-to-many/many-to-many supported relations; edit list/table and records through UI/API.",
    "Bind /blog/:slug template to published records; conflict duplicate slug and missing record; choose representative preview record without changing template or record accidentally.",
    "Insert supported rich headings/paragraphs/lists/links/media/tables/quotes/code/registered components; paste untrusted content and inspect sanitisation.",
    "Bind stable Data IDs/fields/queries and Forms version; exercise validation/response/assessment/accessibility/anti-abuse events and denied/stale/loading/empty/unavailable state.",
    "Use filtered/sorted/paged collection renderer and site search with draft/private content excluded; locale/fallback/language/alternate SEO where enabled."],
    "Native structured content with stable identities; owning Data/Forms APIs and safeguards; no hidden raw database mutation or executable pasted privilege; record/template separated; published routes determine sitemap.")
plan("B4-SITES-QUALITY", "Assets, SEO, accessibility and measured diagnostics", [
    "Use image/video/icon/document/font assets with provenance/licence; rename/move original, crop/focal point/responsive variants/format/lazy load/alt then inspect original and derivative provenance.",
    "Delete asset used by two pages and inspect usage/recovery impact; test external embed privacy/performance disclosure.",
    "Configure title/description/canonical/robots/social/schema/sitemap/defaults; seed missing/duplicate metadata and verify rendered routes and tied diagnostics.",
    "Seed contrast/alt/label/heading/focus/colour-only/touch/motion faults and inspect structured PageID/ComponentID/source diagnostics; keyboard/screen-reader author the page.",
    "Measure oversized media/bundle/fonts/render-blocking/payload/third-party costs; unsupported metrics remain unknown; inspect optimisation source diff."],
    "Canonical originals and permissions preserved; no untracked duplicate originals, fabricated metrics/business claims or inaccessible design; actual live SEO and measurable diagnostic findings.")
plan("B4-SITES-PUBLISH", "Real revision-pinned deployment, failure and rollback", [
    "Build canonical source to artifact with recorded hash/config revision/framework/provenance; exercise restore/build/validate/package/deploy/verify stage jobs and cancellation.",
    "Deploy authorised preview with assets/routes and required Forms/CMS/auth/server dynamic behavior; independently verify content and exact source/config/deployment IDs.",
    "Inspect production impact/env/domain/source/check warnings and authorised action; no production action is exercised without Team C-authorised target.",
    "Fail build/provider/verification and confirm prior working deployment intact; rollback known successful revision without deleting later source history.",
    "Simulate provider loss/drift and rebuild from canonical Files/Stack source onto authorised alternative test provider; inspect routing verification and deployment provenance.",
    "Inspect customer build isolation, public/private secret references, redacted errors, env/runtime config and separate first-party site workload allocation."],
    "Real provider results and dynamic runtime; external output not sole/canonical editable source; no success from static-only publishing, destructive rollback, credential leakage or customer privileges inherited from CAKE.", "C4 hosting/build/archive/domain adapters and test deployments; C6 integration; production action approval where applicable")
plan("B4-SITES-DOMAIN", "Namespace, name screening, ownership and scoped DNS", [
    "Reserve cake slug concurrently; reject mixed case/digits/underscore/Unicode/hyphen edges/repeated path/invalid/reserved names; change slug explicitly and test retained ID/redirect policy.",
    "Request safe/impersonation/ambiguous first-party name and IDN domain; inspect deterministic+AI rules/model/version/reason/review states and stable VerificationID.",
    "Begin site/account-bound random DNS TXT challenge, expire/replay/rotate it and attempt duplicate domain claim/cross-account takeover; name screening and ownership remain separate.",
    "Connect test provider to least-privilege authorised zone/hostname; configure exact required records and compare unrelated mail/verification records before/after.",
    "Inspect Pending/Verified/Misconfigured/Conflict/failure, manual exact instructions, apex/www/canonical/redirect/TLS readiness; no live URL success before verified provider result.",
    "Enforce configured hosting allocation/inactivity policy through server and verify offline site retains source."],
    "Default sites.9to1.uk/<site-slug> remains path-based; atomic claim, safe review and separate ownership; no unrelated DNS mutation/unchecked takeover or invented expiry/inactivity policy.", "C4 name review/DNS/hosting real test services; canonical policy values and isolation approval required")
plan("B4-SITES-EXTENSION", "Extensions, AI and account/consent integration", [
    "Register representative component/inspector/CMS field/data/provider/template/node/preview contribution with API version/platform/scopes via shared host; crash and disable isolated extension.",
    "Create/refactor normal editable source-backed page/layout/CMS/binding/graph/SEO through contextual AI with selected Site/Project/domain/revision/component/breakpoint/diagnostics.",
    "Inspect available build and visual QA for overflow/clipping/contrast/responsive faults before claiming AI result correct; disable AI and verify manual core.",
    "Enable configured member/analytics/locale providers explicitly; inspect trusted private-route authorisation and selected roles in preview without real private credentials.",
    "Load donor template and verify no silent third-party tracking; apply consent category/withdrawal and inspect scripts/events in each environment."],
    "Shared host/permissions and source model; typed AI actions with real scope; unavailable/denied honest; client visibility not authorisation; secrets absent; tracking not silently enabled.")

TF_STEPS = {
 "TF-01": ("Terminology and ownership", ["Inspect catalogue/New menus/artifact types: Files ten types; Link Flow/Binder; Prototype; Paths; Replay; Variants integrated in seven owners.", "Arrange, edit original, author contextual block and make independent copy; inspect distinct domain authority and shared engine/renderer/graph use."], "No Sheet in Link, Models rename, Projects/Variants app or parallel model/broker/content-merge engine."),
 "TF-02": ("Launch, shell, author/read and navigation", ["Create/open/import real artifact; Recent/Pinned/Shared/search/empty templates and direct artifact route; return Home and reopen.", "Perform B4-UI suite across launch/editor/view/run, spatial pan/zoom/fit/outline, nested pop-ups, text focus, touch and all specified status states."], "Saved work/session preserved, no fictitious recent/templates, properties-only workflow or inaccessible hidden controls."),
 "TF-03": ("References, atomic state and native interchange", ["Create live and explicitly pinned stable section references; place same source twice with local labels/layout.", "Rename/move/revoke/delete section and inspect safe exact placeholders; share host separately from source.", "Remove appearance versus delete original; duplicate preserving references versus explicit independent owner-created copy.", "Save/reopen/offline/concurrent edit/undo; export native manifest and portable bytes with distinct permission/disclosure; import unknown schema/identity collision/quota/missing dependencies."], "Separate per-user viewport, canonical source identity and revision policy preserved; no widened access, substituted whole source, flattened native objects or lost only-unsynced draft."),
 "TF-F01": ("Ten types and complete nesting", ["For all ten types create usable empty organisation with explicit name/type/backing location and provider metadata capability preflight.", "Add existing/New item/Open/new tab/window/original location/properties/share/layout/remove appearance/original-delete plus bulk partial outcomes through UI/API.", "Run every one of the 100 parent→nested target pairs with folder-style alternative, acyclic containment and reference recursion controls; test smart self-ingestion.", "Create owner artifact at inspectable explicit backing destination, then add reference; external import commits before managed reference; check drop Add reference/Move/Copy/Reorder."], "First-class versioned IDs/memberships; contextual membership no second filesystem parent; normal view retains authored metadata; Tree viewer exception enforced by actual permission."),
 "TF-F02": ("Ordinary Folder regression", ["Create/rename/move/copy/trash/restore/colour/sort/navigate folder before/after organisations.", "Open ordinary folder through Project/Tree/Matrix and return to restored context."], "Existing folder semantics preserved, no mandatory organisation metadata or accidental physical move."),
 "TF-F03": ("Homework Project", ["Create Maths/French sections containing named Present/Write homework plus nested Maths folder and another type.", "Rename/reorder/colour sections, collapse/search outline, follow SectionID deep link, move appearance, independent section sort/project-wide sort.", "Delete section with keep-Unsectioned and remove-appearance choices; reopen and inspect authored order/membership from second authorised client and viewer."], "Sections not folders; originals unchanged; per-user collapse distinct; stable deep link and shared authored order; no viewer mutations."),
 "TF-F04": ("Sheet canvas and original editor", ["Add Write/image/sticky/rich text/labelled connector/ink/shape/nested Stash; Select/Pan/Pen/Eraser and geometry/layer/style inspector.", "Multi-select align/distribute/group/ungroup/duplicate/remove, snap/grid/fit/outline; connector follows stable endpoint and delete impact.", "Open same original from two contexts, edit permitted original, close with prior viewport/focus; default Pop-up and explicit alternate route/Owning app policy.", "Save/reopen exact layout and perform keyboard/touch create/select/open/reposition."], "Context geometry undo never source rollback; safe registered editor or read-only+Open original; authored strokes/groups/geometry persist, per-user viewport separate."),
 "TF-F05": ("Tree roles and protected layout", ["Create Maths group with Maths/Maths2/Algebra presentations and nested folder; connect/disconnect/reparent/arrange/collapse/search/fit, open group pop-up.", "Reference same file in two Trees, rename/move original; reject cycle; delete group retaining/reassigning/removing appearances.", "Editor Tree/View switch; viewer direct route/API/embed/Boards request for protected visual layout; viewer logical View navigation."], "No unmanaged copy/shortcut or incidental original deletion; viewer never obtains visual layout; source edit grants separate."),
 "TF-F06": ("Smart rules and safe freshness", ["Select locations and tags/type/status/date, All/Any operators; inspect matching rule via Why included.", "Change tag/date to enter/exit, pin nonmatch/unpin, remove dynamic appearance, revoke location, disconnect source and observe unknown/partial/offline freshness.", "Review inspectable AI rule/scope proposal and nested organisation pin/match; reject recursion/private search."], "No inferred false/zero, opaque-only classifier, source move/copy/access widening or offline false nonmatch."),
 "TF-F07": ("Timeline chronology and local assignment", ["Select Created/Modified/Deadline/Assigned; add labelled periods, range/Undated/overlap/nested object and open period.", "Pan/zoom/fit/date/filter; modify Assigned locally, attempt drag under source dates, explicit authorised source date edit.", "Test timezone/date-only boundaries, live source modification, switch away/back and reopen."], "All four sources truthful; assigned dates/periods retained; source dates not secretly rewritten; every overlapping/undated item discoverable."),
 "TF-F08": ("Pipeline gating and local/global scope", ["Personal Pipeline begins ungated; create/reorder/rename stages; move by pointer/keyboard/API.", "Add checklist and authorised reviewer gate; reject unmet/pending, approve/revoke authority, submit simultaneous transitions.", "Preview explicit mapping target/conversion/rights/conflict then fail its global update; delete stage with retained destination/removal choice.", "Reference same file in another Pipeline and compare source metadata before/after."], "Local stages/history/approval IDs; pending not completed; no implicit global mapping, bypassed reviewer or partial success misreport; originals retained."),
 "TF-F09": ("Catalogue typed local fields", ["Same canonical file in two Catalogues with different notes/rating; Cards/Table/Grouped and entry info in each.", "Create/reorder/hide/resize typed text/person/status/tags/date/number/rating fields and card preview; actual source versus local/read-only global field scope.", "Edit genuine global property through owner, invalid value, bulk compatible edit, incompatible type conversion preview and recoverable field removal.", "Rename original and reopen stable entry source."], "Filename cell not source reference; validation preserves rest of edit; values retained until explicit conversion; local notes do not affect other catalogue/source."),
 "TF-F10": ("Stash ordered reader and comparison", ["Save nonalphabetical sequence, read Previous/Next/overview via keyboard/touch, mark Reviewed, restart same EntryID.", "Reorder via second client; remove current entry and inspect neighbour; explicit earlier/later commands.", "Compare two entries with independent source IDs/slot navigation; narrow A/B retains slots without advancing main; denied/missing source.", "Open Stash on Sheet and close to same viewport."], "Files remain separate, per-user position/review not global work status; no expansion/cloning; reorder retains current surviving entry."),
 "TF-F11": ("Matrix classification and orientation", ["Topic A/B/C rows × Writing/Data/Presentations columns; populate nested folder; open cell/row/column.", "Rename/reorder axes, keyboard add/move/open with row/column announcement, move appearance, scroll/filter normal contents on large grid.", "Remove axis with reassign/Unassigned/remove appearance; repeated canonical items coalesced or marked; inspect permission-safe displayed-unit counts; reopen."], "Stable row/column/CellID independent coordinates; local classification only, no source deletion/private counts; readable orientation."),
 "TF-L01": ("Link source/authored content shell", ["Create Flow/Binder by distinct preview/name with Files destination; place in compatible organisation/Spaces/Boards.", "Inspect outline/search/Insert/Sources/view/history/share; author supported shared text/table/media/drawing directly, reference independent original.", "Reveal placement/change source/section/pin/Open original and duplicate document with references."], "No Link Sheet/Boards alias; authored and original edits visibly distinct; normal shared semantic objects, no new file per note."),
 "TF-L02": ("Annual report continuous Flow", ["Build Report.data → Report.write → image → authorised second stable section of Report.data → report-scoped assistant.", "Use one primary vertical scroll on desktop/mobile, reorder outline/insertion, duplicate placement/remove/layout/source chrome; edit permitted original.", "Revoke source, delete referenced section, unsupported/offline renderer, visible revision pin, live source update and AI unavailable.", "Undo reorder; save/reopen reading position; open original/back; long populated Flow measured virtualisation."], "Coherent inline content, no nested vertical panels/icon or pop-up list; source IDs/rights preserved, stable reading context, AI only authorised report sources."),
 "TF-L03": ("Binder authored containers and exploration", ["Author rich folder, tabbed binder, ordered stack, note/card, nested content, original reference and connection; resize/group/layer/label.", "Explore in nonlinear reader order, tabs/stack contents, close to same overview, reduced motion, outline locate off-canvas object and renamed stable deep link.", "Delete authored container with descendant impact while original remains; explicit extract via owning create/export.", "Open from Sheet and Boards page; save/reopen authored hierarchy/geometry and per-user open tabs."], "Single rich interconnected document, real containers not decorated filesystem shortcuts; no fixed Paths route, flattened/split unwanted files or lost focus/context."),
 "TF-P01": ("Prototype actual controls and surfaces", ["Create Prototype, use object library/hierarchy/Design/Logic/Run/diagnostics with selected contextual AI.", "Place/group/size input/output/slider/switch/button/selector/table/chart/container; accessible label/type/limits/units/binding/action inspector.", "Switch Design/Logic/Run while preserving same model, disable AI."], "Controls real typed model behavior; no Models name or disconnected slider/copy; manual useful core."),
 "TF-P02": ("Prototype typed evaluation and session inputs", ["Build two-input typed formula/output calculator; bind slider/switch/chart, reusable explicit component inputs/outputs.", "Reject incompatible types/connections/circular dependency/division error/unavailable input; rename property, remove with dependency preview/repair.", "Build bounded explicit timestep example; Run/Pause/Stop/Reset/cancel long evaluation; inspect diagnostics and repeat deterministic revision/inputs with declared seeds.", "Viewer session changes input then restart; explicit apply-default author action distinct; saved authoring default and unintended external actions unchanged."], "Stable IDs, validated dependencies/shared graph+formula editor; errors not plausible zero, time feedback explicit; no blocked navigation or shared-session corruption."),
 "TF-P03": ("Prototype sandbox, reuse and interoperability", ["Run untrusted script attempting file/network/process/credential/device access in declared sandbox, inspect denied actual access and authorised owner action route.", "Review measured/imported/assumption/calculated output labels/units/limitations and AI typed undo/validation.", "Update reusable definition/version without silently breaking instance; missing component payload repaired.", "Embed actual model in Files/Spaces/Link/Boards/Paths; native interchange dependencies and separately labelled static noninteractive export; save/reopen/API same behavior."], "No implicit capability by opening/running/viewing; model inspectable if blocked; reader local inputs do not grant authoring; source/model preserved."),
 "TF-H01": ("Path authoring and validated routes", ["Build information/choice/activity/checkpoint/outcome steps, two outcomes, starting step, stable choices/conditions/actions and shared embedded content.", "Reorder outline independently graph positions; connect/disconnect, fallback/no eligible route, multiple eligible automatic priority.", "Detect missing target, accidental automatic loop and unreachable required outcome; explicitly bounded retry loop.", "Preview both routes at identified draft revision with disposable reset progress and actual transition cause inspection; deliberate validated authored commit."], "Human choices stable, no random/storage-order routing or preview publishing/real-progress mutation."),
 "TF-H02": ("Path attempts, safe resume and revision binding", ["Start real attempt, satisfy required activity via typed result, checkpoint/Continue and authorised Back without repeated side effect.", "Save progress/response and restart/cross-device resume same AttemptID/StepID/RevisionID; guest/local scope labelled, responses private.", "Complete twice via Restart with separate attempts retained; clear saved progress via separate confirmed delete.", "Update Path, tell active reader newer revision; explicit validated mapping/fresh attempt, changed/deleted step, interrupted/duplicate transition request."], "Durable separate progress/revision-bound attempts; no fake percentage, guessed pixel completion, duplicate completion or external action, silent route jump or leaked response."),
 "TF-H03": ("Path cross-app risk and accessible completion", ["Embed interactive Prototype in required activity with explicit permitted typed result, source revoke and real reader rights.", "Attempt author-embedded high-risk app/Automation action without reader rights; use AI explanation/typed author edits while reader follows committed route.", "Complete identical route keyboard/screen-reader/narrow and inspect all previous H01/H02 acceptance results."], "Shared graph/objects, owner permissions/risk; no author-granted reader rights, screenshot Prototype or AI replacement of committed routing."),
 "TF-R01": ("Replay scoped explicit capture", ["Open Replay without recording; pick two supported sources/subobjects and exclude unrelated open file; inspect capabilities/destination/retention/export/media scope before Start.", "Explicitly permit optional mic/camera/screen, deny each separately; show Recording/Paused/source/elapsed/checkpoint/annotation.", "Pause/resume/change scope timestamp, revoke source and inspect stopped future capture; Stop and verify subscriptions/media released plus durable finalisation status."], "Semantic capture distinct from images; no unrelated/private recording, implicit media permission or cached permission bypass."),
 "TF-R02": ("Replay historical inspection and independent continuation", ["Capture events and annotated checkpoint; advance original; chronological tracks/filter/chapters/timeline/inspector, play/pause/seek/speed/prev-next.", "Read checkpoint exact historical revision and read-only content with declared transcript/caption; restart from retained snapshots/bases.", "Remove base dependency and inspect incomplete checkpoint; chapter order never rewrites underlying event provenance.", "Continue from here via supported owner create with copy rights/destination/scope; open new artifact, verify original newer revision unchanged."], "Stable sessions/events/checkpoints and pinned reconstructable history; no latest-source substitution, invented gaps or restoring over original."),
 "TF-R03": ("Replay review/redaction and interruption", ["Review included sources/revisions/annotations/media/recipient implications; distinguish live access versus independent exported snapshot.", "Exclude/redact content then scan thumbnails/search/transcripts/dependent snapshots; trim is not permanent redaction; view-only export denied.", "Interrupt capture/finalisation, resume journal checkpoint and inspect missing interval; quota/owner/media failure states Not started/Recording/Paused/Finalising/Ready/Incomplete/Error.", "Retain valid semantic checkpoints on media failure; delete/retention affects Replay assets only."], "No removed-secret metadata leakage, capture fabrication/source deletion or improper source byte export; bounded visible recoverable failure."),
 "TF-V01": ("Seven-app branch identity and switch", ["In each Write/Data/Present/Canvas/Picture/Prototype/Sites open Variants near version controls; create/name/duplicate/archive/recover.", "Edit alternative, switch with saved/recoverable unsaved decision; active title/save destination, autosave/undo/collaboration/search/deep link and restart remain branch-aware."], "Stable VariantID/base/head, not filename copy/shortcut; starting revision retained; variant deep link never silently Main."),
 "TF-V02": ("Semantic compare, preview and safe merge", ["Choose explicit base/source/target, navigate labelled addition/removal/edit/conflict in appropriate compare; inspect selected impact without colour-only distinction.", "Combine independent changes, expose overlap Keep target/Use source/manual, invalidate preview on source/target change.", "Commit selected semantic changes or clearly labelled safe whole-version fallback; stale revision conflict, interruption/cancel/retry idempotency and recover both branches."], "Owner validates merge at explicit revisions; no generic byte safety claim, guessed conflict or erased target history/only-retained content."),
 "TF-V03": ("Per-owner content-family comparisons", ["Write blocks/text/format/references; Data sheets/tables/stable rows-columns-cells/value/formula/schema/dependencies; Present slides/order/object/content/layout/dependencies.", "Canvas geometry/ink/groups/connectors/references; Picture layer/mask/transform/adjustment/raster choose path; Prototype typed objects/formulas/bindings/definitions/relationships validated.", "Sites page/component/route/content/asset/settings compare; switch/merge and inspect production deployment unchanged, no secrets/live provider state copied.", "Run V01/V02 acceptance in all seven apps with each listed content family and stale/overlap/fallback/interruption case."], "Real owner-specific comparison/merge required; shared button bar not coverage; Sites branch action never publishes."),
 "TF-X01": ("Canonical embeds and conversational editing", ["Flow/Binder/Files Sheet as Boards pages; Open original and Back preserve host context, roles independent.", "Sheet contains Binder/Flow/Stash under default/selected opening policy; interactive Prototype in Link/Boards/Path; Replay historical checkpoint.", "Spaces/contextual AI typed create/edit/organise with explicit artifact/selection/source/active branch/permissions; review significant rule/dependency/route suggestions and undo; AI disabled."], "Same IDs/actual content, no flatten/copy/screenshot; host doesn't grant source or cloud access; unsupported exact limitation."),
 "TF-X02": ("Exhaustive registered action schema and equivalent effects", ["Enumerate UI menus/keyboard/editor actions and owning canonical namespaces; inspect typed schemas/permissions/risk/external effects/limits/partial results.", "Execute every organisation type-specific CRUD/settings/membership/layout operation; Link blocks/containers/source/preview/interchange; Prototype property/formula/run/session/default actions.", "Execute Paths author/attempt/progress/read-scoped actions; Replay capability/capture/history/copy/review/redact/export/delete; seven owners branch/compare/merge/recover actions.", "Run same consequential UI/API requests and duplicate/stale/denied requests; inspect stable job/events/cursors/pages and all required errors without forbidden names."], "Targets/payload/op key/revisions mandatory or declared create precondition; no unvalidated generic JSON, bypassed owner/broker or arbitrary-ID authority."),
 "TF-X03": ("Full web adaptation and bounded resources", ["Repeat actual useful author/reader core in Windows/Linux/browser shared format/account/domain/permissions.", "Run B4-UI and B4-RESOURCE for each mapped surface; browser file/process restrictions explicit, remote configured capability truthful.", "Record Android/Go new-surface-only temporary incomplete-authoring exception separately; no inherited deadline/fallback in Sol Happy or web exemption."], "Same models and faithful narrow core, no scaled screenshot/reduced app; complete declared browser support unresolved gate; bounded traversal/loading/compute with shown limits."),
 "TF-X04": ("Independent acceptance and exact candidate", ["Run every numbered group and 100-pair Files matrix plus all named cross-app examples with backend IDs/content readback and second client.", "Run ordinary/editor/viewer/source-denied/loading/empty/error/move-rename-delete/undo/reopen/offline/concurrent case dimensions.", "Run B4-UI/B4-RESOURCE/B4-RECOVERY plus API equivalence; independent reviewer reproduces critical cases and rechecks exact final candidate/deployments."], "All required groups in denominator, exact commit/runtime/evidence; no skipped/not-run/stale/environment-blocked PASS or test-count-based completion."),
 "TF-X05": ("OPEN decisions and representation safety", ["Preserve Playgrounds naming/product OPEN without adding overlapping app; inspect real data before schema changes.", "Review narrow low-level format/adapter choice against specified controls/ownership/permissions/native structure; material changes require canonical decision."], "No speculative product, dropped/deferred named feature, guessed public contract or silent unsupported platform.")
}
for group, (title, steps, oracle) in TF_STEPS.items():
    plan("B4-"+group, title, steps, oracle)

plan("B4-SH", "Online same-product and operational gates", [
    "Fresh clean browser account reaches eligible app and saves actual persistent artifact without local Home/dev tooling; complete cross-device and account/org cache isolation journey.",
    "Validate frontend/backend exact revision-pinned identities, independent runtime without developer PC, configured auth/email delivery/revoke/recovery, real permission and entitlement services.",
    "Run Drive/Sites real-provider procedures and actual dynamic website behavior, workload isolation, server-side allocations/inactivity rules and canonical backup restore.",
    "Coordinate billing/AI/job/global operational gates with owning B5/Team C; no local entitlement/job authority, live charge or invented commercial/browser/capacity policy.",
    "Keep code-complete, behavior-tested, deployed and commercially enabled distinct; record blocked production isolation/merchant/policy/cutover decisions while independent work continues."],
    "Full Sol Happy remains active; staging not production, browser not reduced companion; no mocked online claim or developer-PC runtime dependency.", "Team C real production architecture and authorised staging; B5 billing/account/AI; OPEN SH-13 affected decisions")

# Exact text/index coverage. Short genuine headers are context; long headings
# containing actual prose are always indexed as requirements.
RANGES = [("GENERAL",1,57534),("FILES",218588,245455),("SITES",862474,911016),
          ("TERRA",1238694,1315675),("SOLHAPPY",1315675,1342106)]
ROWS = []
CONTEXT = []
def is_header(p):
    t=p['text'].strip()
    return p['heading'] != 'NORMAL_TEXT' and len(t)<160 and not re.search(r'\b(MUST|SHOULD|SHALL|Every|All requirements)\b|[.;]',t)

def choose(product, heading, text):
    t=(heading+' '+text).lower()
    if product=='TERRA':
        match=re.search(r'TF-[A-Z]?\d+',heading)
        return ['B4-'+match.group()] if match else ['B4-GOV']
    if product=='GENERAL':
        if any(x in t for x in ['privacy','trust','permission','security','secret','caller','revocation','audit','encryption','retention','consent']):return ['B4-PERMISSION']
        if any(x in t for x in ['identity','stable ident','atomic','schema','persistent','data integrity','migration']):return ['B4-IDENTITY','B4-RECOVERY']
        if any(x in t for x in ['accessib','visual','professional','originality','consistent','subjective','objective','quality','interface']):return ['B4-UI','B4-GOV']
        if 'performance' in t:return ['B4-RESOURCE']
        if 'recovery' in t or 'error handling' in t:return ['B4-RECOVERY']
        return ['B4-GOV','B4-SH']
    if product=='SOLHAPPY':
        if 'sh-07' in heading.lower():return ['B4-FILES-TRANSFER','B4-FILES-CONTINUITY','B4-FILES-HISTORY']
        if 'sh-08' in heading.lower():return ['B4-SITES-PUBLISH','B4-SITES-DOMAIN','B4-SITES-PREVIEW']
        if 'sh-06' in heading.lower():return ['B4-UI','B4-SH']
        return ['B4-SH','B4-GOV']
    if product=='FILES':
        if '9to1.Files.' in text:
            if any(x in text for x in ['Upload','Download','Transfer.','SetAvailability']):return ['B4-FILES-TRANSFER']
            if any(x in text for x in ['Sync.','Changes.']):return ['B4-FILES-CONTINUITY']
            if any(x in text for x in ['Share.','Versions','Version','CreateArtifact','CreateStack','Delete','Restore','Purge','Search']):return ['B4-FILES-HISTORY']
            return ['B4-FILES-SHELL','B4-IDENTITY']
        if any(x in heading.lower() for x in ['transfer','availability','offline','quota']):return ['B4-FILES-TRANSFER','B4-RECOVERY']
        if any(x in heading.lower() for x in ['sync','collaboration','journal','drive integration']):return ['B4-FILES-CONTINUITY','B4-IDENTITY']
        if any(x in heading.lower() for x in ['version','trash','sharing','creation','search']):return ['B4-FILES-HISTORY']
        if 'privacy' in heading.lower():return ['B4-PERMISSION']
        if 'acceptance' in heading.lower():return ['B4-FILES-SHELL','B4-FILES-CONTINUITY','B4-FILES-TRANSFER','B4-FILES-HISTORY']
        return ['B4-FILES-SHELL','B4-IDENTITY']
    # Sites section grouping by actual semantic owning operations.
    if product=='SITES' and '9to1.Sites.' in text:
        if any(x in text for x in ['Slug','PublicName','Domain','Hosting']):return ['B4-SITES-DOMAIN','B4-SITES-PUBLISH']
        if any(x in text for x in ['Deploy','Rollback','Build(']):return ['B4-SITES-PUBLISH']
        if 'Preview' in text:return ['B4-SITES-PREVIEW']
        if any(x in text for x in ['Collection','Content','BindData','BindForm']):return ['B4-SITES-CMS']
        if any(x in text for x in ['SEO','Diagnostics']):return ['B4-SITES-QUALITY']
        return ['B4-SITES-AUTHOR','B4-SITES-SOURCE']
    h=heading.lower()
    if any(x in h for x in ['domain','url namespace','name verification','directory','hosting']):return ['B4-SITES-DOMAIN','B4-SITES-PUBLISH']
    if any(x in h for x in ['deploy','build pipeline','secrets','server functions']):return ['B4-SITES-PUBLISH']
    if any(x in h for x in ['editor-shell','preview fidelity','live preview','trust boundaries','security and abuse']):return ['B4-SITES-PREVIEW','B4-PERMISSION']
    if any(x in h for x in ['node','interaction','graph']):return ['B4-SITES-GRAPH']
    if any(x in h for x in ['cms','collection','dynamic route','rich content','data integration','forms integration','localisation']):return ['B4-SITES-CMS']
    if any(x in h for x in ['asset','media','seo','accessibility','quality diagnostics']):return ['B4-SITES-QUALITY']
    if any(x in h for x in ['round-trip','stack','dev integration','collaboration']):return ['B4-SITES-SOURCE']
    if any(x in h for x in ['extension','dulche','analytics','authentication']):return ['B4-SITES-EXTENSION','B4-PERMISSION']
    if 'performance' in h:return ['B4-RESOURCE']
    if 'recovery' in h:return ['B4-RECOVERY','B4-SITES-PUBLISH']
    if 'acceptance' in h:return ['B4-SITES-AUTHOR','B4-SITES-SOURCE','B4-SITES-PREVIEW','B4-SITES-GRAPH','B4-SITES-CMS','B4-SITES-QUALITY','B4-SITES-PUBLISH','B4-SITES-DOMAIN','B4-SITES-EXTENSION']
    return ['B4-SITES-AUTHOR','B4-GOV']

def domain(product, heading, text):
    if product=='FILES': return {'owner':'Team A Files domain; C4 service implementation; B4 browser adapter', 'existing_contract':str(ROOT/'9to1 Workspace/Files/CUI/Contracts/HostedFilesContracts.cs'),'operation': re.findall(r'9to1\.Files\.[^;\n]+',text) or [heading], 'concrete_backend':'MISSING; no concrete IFilesService/hosted provider in baseline'}
    if product=='SITES':return {'owner':'Team A Sites/shared domain; C4 hosted build/publishing; B4 browser surface','existing_contract':str(ROOT/'9to1 Workspace/Sites/Domain/SiteModels.cs'),'operation':re.findall(r'9to1\.Sites\.[^;\n]+',text) or [heading],'concrete_backend':'MISSING; only local FileSiteWorkspaceStore application services and hosting interfaces'}
    if product=='TERRA':
        namespace='9to1.Files.Organisation' if 'TF-F' in heading else '9to1.Link' if 'TF-L' in heading else '9to1.Prototype' if 'TF-P' in heading else '9to1.Paths' if 'TF-H' in heading else '9to1.Replay' if 'TF-R' in heading else 'owning-app.Variants' if 'TF-V' in heading else 'existing shared owner namespaces'
        return {'owner':'Team A shared models/registered contracts; owning apps; B4 browser surface','existing_contract':None,'operation':[namespace,heading],'concrete_backend':'MISSING in baseline; exact authenticated service transport requires C4/A acknowledgement'}
    return {'owner':'Root/B6 plus Team A/C owners for shared/global gates','existing_contract':None,'operation':[heading],'concrete_backend':'External integration/operating evidence required'}

for product,start,end in RANGES:
    heading=product
    section_group=product
    for p in PARAGRAPHS:
        if not(start<=p['index']<end) or not p['text'].strip():continue
        text=p['text'].strip()
        if is_header(p):
            heading=text
            if product=='TERRA' and text.startswith('TF-'):section_group=text.split(' —')[0]
            if product=='SOLHAPPY' and text.startswith('SH-'):section_group=text.split(' —')[0]
            CONTEXT.append({'product':product,'source_index':p['index'],'source_end':p['end'],'text':text})
            continue
        # Clause-level split retains full original paragraph too. No keyword
        # filter: prose examples/API entries/descriptive requirements are kept.
        clauses=re.split(r'(?<=[.!?])\s+(?=[A-Z])',text)
        for n,clause in enumerate(clauses,1):
            row_id=f"B4-{product}-P{p['index']}-C{n}"
            tests=choose(product,heading,clause)
            obligation='MUST' if re.search(r'\b(must|shall|required)\b',clause,re.I) else 'SHOULD_DEFAULT_REQUIRED' if re.search(r'\bshould\b',clause,re.I) else 'MAY_CONDITIONAL_RETAINED' if re.search(r'\b(may|optional)\b',clause,re.I) else 'DESCRIPTIVE_CONTRACT_RETAINED'
            ROWS.append({'requirement_id':row_id,'product':product,'group':section_group if product in ['TERRA','SOLHAPPY'] else heading,
                'heading':heading,'source_revision':REVISION,'source_index':p['index'],'source_end':p['end'],'source_heading_style':p['heading'],
                'paragraph':text,'criterion':clause,'obligation':obligation,'state':'BLOCKED' if product in ['GENERAL','SOLHAPPY'] else 'MISSING',
                'browser_capability':'NOT_IMPLEMENTED_OR_RUNTIME_UNVERIFIED','ui_entrypoint':'MISSING in baseline; required operation stated by criterion',
                'domain':domain(product,heading,clause),'test_ids':tests,
                'criterion_test_id':row_id+'-TEST','criterion_assertion':clause,
                'test_outcome':'NOT_RUN','evidence':[],'exceptions':[],
                'review_status':'CANDIDATE_MAPPING_REQUIRES_B6_CHALLENGE; source/index completeness checked, no behavior PASS inferred'})

def dump(name,value): (BASE/name).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n')
dump('requirements.json',ROWS)
dump('context-headings.json',CONTEXT)
dump('test-procedures.json',list(PLANS.values()))

TYPES=['Folder','Project','Sheet','Tree','Smart','Timeline','Pipeline','Catalogue','Stash','Matrix']
NEST=[{'test_id':f'B4-NEST-{a}-{b}','parent':a,'nested_target':b,'state':'MISSING','outcome':'NOT_RUN','exception':None,
       'steps':f'Create {a} parent and {b} target with stable IDs, nest/open/back via normal and specialised view; save/reopen/second-client readback; validate owned containment cycle rejection and reference recursion; viewer roles and source permissions separate.',
       'expected':'Openable unit; canonical IDs/actual content preserved; normal view non-destructive; no originals moved/copied. Tree viewers cannot read protected Tree layout. Unsupported provider combination requires exact reason and alternative, not default exclusion.'} for a in TYPES for b in TYPES]
dump('files-nesting-matrix.json',NEST)
APPS=['Files','Sites','Link','Prototype','Paths','Replay']
CAPS=['launch/new/recent/pinned/shared/search','create/open/edit/save/reopen','history/undo/redo','native import/export','sharing/independent source permissions','direct links/refresh/back/forward','account/org switching/revoked sessions','offline/interruption/recovery/conflict','typed API/AI parity','desktop/narrow/touch/accessibility/themes/200percent','bounded workload/subscription cleanup','browser→desktop→browser canonical continuity']
dump('capability-matrix.json',[{'app':app,'platform':platform,'operation':cap,'state':'MISSING' if platform=='browser/web' else 'BLOCKED','outcome':'NOT_RUN','implementation':None,'adaptation':None,
     'gate':'B1 browser runtime + Team A owning domain + C4 real authenticated non-production services; no approved supported browser versions yet' if platform=='browser/web' else 'Team A installed native client; canonical backend required; no native result inferred from source',
     'exception':None} for app in APPS for platform in ['browser/web','Windows','9to1-OS'] for cap in CAPS])
FAMILIES={'Write':'stable text/blocks/format/references','Data':'sheets/tables/stable rows-columns-cells/values/formulas/schema/named deps','Present':'slides/order/objects/content/layout/dependencies','Canvas':'objects/geometry/strokes/groups/connectors/references','Picture':'layers/masks/transforms/adjustments/raster choose/manual','Prototype':'typed objects/properties/formulas/bindings/definitions/relationships','Sites':'pages/components/routes/content/assets/settings; no publish/secrets/live provider copy'}
dump('variants-matrix.json',[{'app':a,'owner_worker':'B4' if a in ['Sites','Prototype'] else 'B2/B3 owning app worker','content_families':f,'test_ids':['B4-TF-V01','B4-TF-V02','B4-TF-V03'],'state':'MISSING','outcome':'NOT_RUN','exception':None} for a,f in FAMILIES.items()])

coverage={'source_revision':REVISION,'source_sha256':hashlib.sha256((SOURCE/'canonical.txt').read_bytes()).hexdigest(),'baseline_commit':BASELINE,
    'scope_ranges':RANGES,'source_nonempty_paragraphs':sum(bool(p['text'].strip()) and any(a<=p['index']<b for _,a,b in RANGES) for p in PARAGRAPHS),
    'indexed_paragraphs':len(set((r['product'],r['source_index']) for r in ROWS)),'context_headers':len(CONTEXT),'criterion_rows':len(ROWS),
    'groups':dict(Counter(r['group'] for r in ROWS)),'requirement_states':dict(Counter(r['state'] for r in ROWS)),
    'outcomes':dict(Counter(r['test_outcome'] for r in ROWS)),'procedure_count':len(PLANS),'nesting_pairs':len(NEST),
    'verified_required_browser_gates':0,'passed_runtime_tests':0,'applicability_exceptions':0,
    'warning':'Sentence/paragraph-derived candidate coverage preserves all scoped text but requires independent semantic challenge. Number of rows is not an acceptance denominator until review; all required browser behavior remains unverified. No unknown external endpoint invented.'}
dump('coverage.json',coverage)
assert coverage['source_nonempty_paragraphs']==coverage['indexed_paragraphs']+coverage['context_headers']
assert all(r['test_ids'] and all(t in PLANS for t in r['test_ids']) for r in ROWS)
assert len(NEST)==100
print(json.dumps({k:coverage[k] for k in ['criterion_rows','indexed_paragraphs','context_headers','source_nonempty_paragraphs','procedure_count','nesting_pairs','requirement_states','outcomes']},indent=2))
