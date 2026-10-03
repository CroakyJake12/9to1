# B3 manual acceptance procedures

Canonical revision: `AHj4eMRBcI3x5FFYbDM2m-hiWUCk_SbZphbsfCtNVrsp0bqkXxEmCGQMUSBbZxWMB76lDNvgrMwyepTjeCqVIpuSFZx5BKabswqtngHgOTo`.

Each requirements.csv/json row contains the exact source clause, required UI, canonical domain, one test ID and expected content/side-effect assertion. Source-range IDs preserve traceability. All tests are NOT-RUN. Family procedures below are shared setup and interaction steps; the exact row clause is a mandatory additional assertion and compound clauses require every subcase. No browser support policy or performance threshold is invented.

Preconditions: approved non-production deployed frontend/backend identities; exact candidate commit; declared supported browser versions; authorised fixtures/providers; owner/reader/unrelated-account clean profiles; native client for continuity. Missing preconditions block their cases. No real recipient sending, public publishing, paid API expenditure or live charge is implied by this procedure document.

## COMMON-CONTINUITY

Required UI: All browser editor/storage controls; exact artifact deep link; Files rename/move/history/share

Sign in in two clean browser profiles and native client using authorised test identities. Create populated artifact A; record canonical IDs and committed revision from authenticated owner API. Edit/save in browser, open native, edit/save, reopen browser. Rename/move while open; inspect revision history; share with reader then revoke. Make concurrent incompatible edits from two clients using identical base revision, interrupt save, reconnect/retry with same operation ID. Compare actual structure/content/revisions and affected source objects; do not accept success labels.

## COMMON-AI

Required UI: Persistent floating AI bar; visible Read-only/Write switch; shared compose invocation

Select a real object with off-screen siblings. Request semantic analysis in Read-only and record permitted context IDs. Request a mutation and verify unchanged authoritative revision; switch Write and perform authorised owning-app action. Repeat with read-only host/reader ACL, denied/expired trust and unavailable AI. Verify no mutation or private cloud upload in denied cases, one revision/audit on authorised action, manual editing remains usable, cross-app invocation retains target owner ID.

## COMMON-A11Y

Required UI: Owning editor panes, menus, dialogs and populated special controls

At equivalent desktop viewport and narrow/touch sizes, use keyboard alone through every named control. Inspect screen-reader names, roles, selection, values and item structure. Exercise 200% zoom/scaling, themes/high contrast, reduced motion; inspect focus/return focus, hover/selected/disabled/loading/empty/error/read-only/conflict/missing-source states. Record real screenshots/traces and interactions. Ensure AI bar does not cover Save/Close/Back and pointer-only actions have usable equivalents.

## COMMON-PERFORMANCE

Required UI: Populated editor, long lists/graphs/timelines, close/reopen/navigation

Use progressively larger representative production-format fixtures with recorded sizes/seed. Measure startup, editing, scrolling, playback where relevant, memory, request counts and retained subscriptions on repeat open/close/navigation. Run background work concurrently; cancel it. Check bounded streaming/virtualisation/cache cleanup and no silent content truncation. Record measurements against canonical thresholds; unresolved runtime/capacity targets remain BLOCKED, never invented.

## COMMON-FAILURE

Required UI: Error, reconnect, retry, job progress/cancellation and recovery surfaces

In isolated non-production test data, exercise each named error with invalid/missing IDs, incompatible schema, denied permissions, unavailable provider, cancellation, disconnection and stale expected revision. Interrupt one mutation before authoritative commit and retry same operation identity. Inspect structured code, scope, recoverability, precise partial outcomes and job revision/result. Reopen second client; verify prior valid state/draft/source survived and consequential effects were not duplicated.

## COMMON-DONOR

Required UI: Entire applicable mature donor menu/command/shortcut/settings surface

Pin real selected donor source/revision/licence and inspect its applicable operation inventory including QoL, import/export, security, recovery, keyboard and performance. For each donor operation, record Preserve/9to1 replacement/exact canonical exception and corresponding real browser journey and expected content effect. Independently compare donor behaviour with canonical replacement. Source inclusion and build graph do not prove parity; no unidentified donor operation can disappear from coverage.

## COMMON-API

Required UI: Corresponding owning-app command/menu/shortcut and typed API action

Provision authorised populated canonical fixture; invoke the exact canonical API entry in this row with stable IDs and declared typed arguments; repeat equivalent user operation through actual UI. Compare content, revision, owner permission/risk, result/error, idempotency and audit events. Enumerations exercise pagination; subscriptions resume from cursor; jobs cancel and reconnect. No actual browser control or endpoint has been verified; record missing wiring before execution rather than inventing routes.

## COMMON-IDENTITY

Required UI: Owning object details, rename/reorder/open/save and cross-app references

Create two objects with duplicate display names where allowed. Record stable IDs and schema/revision before rename, reorder and Files move. Open via source owner and secondary reference; inspect both canonical results. Save/reopen/export/import with supported native format, unknown/newer schema and denied source role. Verify typed identities, lifecycle, ownership, no unmanaged copy or guessed schema and conflicts preserve both changes.

## COMMON-SECURITY

Required UI: Sign-in, account context, trust prompt, source/content privacy and audit

Use owner, reader and unrelated-account clean sessions. Attempt named read/mutation before grant, after revoke/expiry, and after account/organisation switch. Verify backend rejection and absence of private cache/AI/network leaks. Inspect approval impact/risk/caller identity, structured denied outcome and audit. Execute only isolated harmless effects; no actual sends, charges, public publishing or production faults without explicit approved test scope.

## MAIL

Required UI: Three panes/navigation, message reader, compose, Outbox, scheduled send, rules/settings

Connect isolated Gmail, Microsoft and generic IMAP/SMTP accounts through real authenticated services. Populate distinct messages with matching subjects/Internet Message-ID, labels versus folders, safe/hostile HTML, attachments and aliases. Exercise each row’s reading/compose/provider operation using exact AccountID-scoped IDs. Offline-cache/read and queue edits/send, restart and reconnect; test draft conflict, provider revoke and send refusal. Verify provider state and second-client content, Outbox lifecycle, Undo hold before irreversible submission, truthful scheduling executor and no tracking/script leakage. Test attachments through canonical Files; do not send outside explicitly authorised test recipients.

## GAMES

Required UI: Development and Creation/Rendering workspaces, scene/components, inspector, graphs, modeller, run/debug/export

Create a real maintained Godot/Blender-derived canonical project with 2-player shared/individual state, C# plus C++ exposed component, controller/WASD movement, starter Player/vehicles/water/lava and a CUI HUD. Exercise row’s creation/control/renderer operation, direct manual authoring and typed API; persist/reopen scene/script/graph/component IDs. Run/inspect actual gameplay and standalone modelling/rendering; rebind per-player keys, hot swap devices, test component dependency/version migration, graph type rejection, real debugging/profiling/export and unsupported hardware fallback. Inspect actual native structure/results, not labelled prefab controls or opaque AI bitmap.

## PLANNER

Required UI: Today/Day/Week/Month/Agenda/Tasks grids, Schedule/Countdown/Assignments, recurrence/provider/details

Create timezone-aware and all-day events spanning DST, a due-only task, weighted nested required/optional assignment, schedule with current timed item, linked countdown and a recurring series. Exercise row operation by keyboard/direct controls and typed API. Schedule/unschedule same task, move linked event, snooze reminder, edit occurrence/this-and-following/whole series. Observe live schedule percentages, next countdown and notification occurrence identity. Import/export .ics; connect real provider calendar, edit concurrently/offline. Attach canonical Maps/Mail/Files references and test authorised overdue Automation→Mail with denied control. Verify original times, IDs, required-item completion and no duplicate Events.

## PICTURE

Required UI: Create/Edit/Generate/Browse, canvas/object/selection tools, Manual Layers, adjustments, export and Variants

Create Raster, Vector and Hybrid documents with shared editable text/ink, paths/Bézier handles, raster subject, mask, linked asset and artboard. Exercise each stated tool/operation and inspect actual ElementGraph/LayerGraph and revision. Switch automatic/manual modes, circle/extract/move subject with scoped background repair, generate/sketch/region-edit through real configured model, retain source/provenance. Save/reopen; export raster/vector/animated samples with profile/metadata options; verify full quality and explicit flattening. Run layer/mask/transform/adjustment Variants comparisons including conflicting raster changes, stale merge and interrupted save. Confirm ink/text/shared engines and canonical FileID continuity.

## WAVE

Required UI: Multitrack timeline/transport, mixer/effects, spectral, sequencer/piano roll, SFX graph, TTS/transcript/censorship/stems

Create a multitrack project with a recorded take, imported mixed audio, sequence notes/patterns/instrument, native SFX graph and SpeechSynthesis text/voice/prosody. Exercise each row operation on stable TrackID/ClipID/component IDs with real audio output. Test split/trim/ripple/fades/mix/routing/effects/automation, time-frequency repairs, enhancement/denoise preview and noise profile. Generate music/SFX/TTS through real configured provider then manually edit their structured native sources. Review exact phrase and semantic-theme censorship ranges; apply/undo treatment; Split Stems creates derived tracks preserving original. Interrupt recording and recover chunks. Export requested scopes/codecs and verify timing/sample quality plus Motion/Present same canonical references.

## MOTION

Required UI: Media/Viewer/Inspector/Timeline, node editor, captions/curves/proxies, render queue

Create populated multi-track sequence with video, still Picture asset, editable title, linked audio, mask/keyframes and nested sequence. Execute row’s frame-exact NLE/property/graph operation; compare structured canonical source ranges and revisions, then undo/reopen. Branch a MotionCompositionGraph, use custom transition progress and procedural/keyframe/tracking contribution; reject incompatible ports; correct mask manually. Generate captions/transcript ripple edits, proxies and typed AI highlights/reframe/subtitles. Edit audio in Wave and return same reference. Export original quality/captions; interrupt/cancel revision-pinned render, edit source during job and verify pinned result and explicit partial failure; relink offline media without new ElementID.

## DEV

Required UI: New Project/editor, Problems/Test Explorer/debugger, dependencies/toolchains, AI review, CUI Builder/preview/environment selector

Open real C#/.NET, C/C++ and Python multi-root projects with known diagnostics/tests, unrelated pre-existing user edits and native manifests/lockfiles. Select explicit authorised remote environment for browser execution; read/edit/save canonical source. Exercise row operation with real LSP, build stage, DAP, package manager, test identity or CUI source model; validate resulting files/logs/provider state. Denied/untrusted workspace must not execute. AI multi-file edit records only its distinct change set; review hunks and revert amid overlapping user edits. Preview bidirectional CUI while preserving unsupported code and last-valid render on failure. Test multi-target extension isolated host and Sites/AI Studio/Stack exact-ID handoffs. VM actions remain unavailable under Mini Computer exemption until approved provider exists.

## STACKS

Required UI: Home/project tabs, lineage/domain tree, changes/history/diff/conflicts/Change Requests, Root/Subroot/Freeze, release/audit

Provision isolated Files-backed and dedicated GitHub-backed project with Main→Branch→Twig→Leaf. Record IDs/base/effective revisions; edit two domains concurrently, cascade safe ancestor change and conflict one resource. Verify owned +A/-D excludes inherited delta, tombstones/renames preserve lineage. Promote pinned CR with checks/approvals, then mutate source to invalidate stale approval. Test reviewed AI/manual conflict choices, Root lock/nonoverlapping Subroot, scoped Freeze expiry. Inspect protected companion ref and deterministic restore after isolated damage. Attempt private-ancestry projection and guard-protected raw Git; verify backend refusal. Sweep preview/consolidation, prune/restore same DomainID, blocked purge, opt-in autocommit and remote-failure states. Cross-client source/ACL/history inspect real provider result.

## TERMINAL

Required UI: Real PTY tabs/panes, AI/Command switch, $Ask, verification, environment/profiles/jobs/history

Connect approved isolated remote/server PTY provider with explicit EnvironmentID and host-key identity. Run interactive REPL/full-screen ANSI program, resize, stdin/signals and sustained output. Ordinary commands bypass LLM. $Ask scoped error explanation must cause zero process/file/API mutation; suggestions are not executed. AI resolved low-risk request and denied dangerous request show exact target/impact and protected typed action. Switch modes preserving session/cwd; reopen panes without replaying commands. Create persistent harmless scheduled action in actual scheduler, close tab then inspect/cancel same ID. Test SSH mismatch, disconnect/reconnect proof, multiline paste exactness, secret exclusion, truncation, Stack guards and canonical Dev/Data/Files integration. Without provider execution remains BLOCKED.

## BROWSE

Required UI: Tab/profile/workspace/groups/split/orientation, internal routes/newtab Search/AI, permissions/extensions/Site Enhancer

Use actually supported execution environment; create Firefox-default and Chromium tabs with separate engine-private storage and mixed ProfileIDs in one workspace/split. Persist per-site engine rule, switch engine without changing TabID; test all four tab orientations/group operations and renderer recovery. Open registered native 9to1:// surface and distinct cui://, browse:// and https:// namespaces; confirm real canonical owner APIs and unprivileged origin denial. Customize New Tab, Search default/dual-provider two real tabs, canonical Dulche mentions, scoped browsing and denied consequential action. Install authorized test extension/Enhancer and inspect permissions/engine capability state. Validate upstream update/promote/rollback with pinned builds. Browser-hosted web engine/profile interception is an unresolved capability, never imitated by iframe.

## MAPS

Required UI: Map/search-AI/location picker/place detail, routes/journeys/POI layers/offline regions

Connect real approved map/search/routing/offline/transit providers. Search ambiguous address and near-me with precise permission denied; verify provenance/confidence and no fabricated position. Drop private coordinate landmark, route to off-graph final point, explicitly submit community POI after duplicate check and then verify/correct/report. Create saved journey with travel/bench stop/wait/activity/manual shortcut and required intent; reroute disruption without deleting required steps. Share with private dependencies denied; download licensed region and test offline search/routing plus freshness/storage limits. Attach RouteID to Planner and change route while viewed. Test shared embeds and exact disclaimer; no public unlimited OSM/Nominatim dependency or implicit OSM publication.

## LAUNCHER

Required UI: Canonical home pages/drawer/dock/folders/widgets/customisation configuration; actual OS host integration

Respect Android/9to1-OS home-launcher purpose; no browser can claim to replace OS home assignment. Through an authorised same-model configuration/service capability if approved, create pages/drawer/dock/folders and configure grid/icon/gestures/widgets, save/reopen and apply on real enrolled native host. Verify InstalledApplicationIDs survive update and work-profile separation; denied widget/package capability remains unavailable. Donor Lawnchair parity requires actual native-host behaviour. Global web inclusion versus native-only boundary needs coordinator clarification; no invented exemption.

## SHELF

Required UI: LaunchItem library/search-AI, manual/smart collections, context actions, unavailable-target state

Register real authorised app/project/FileID/URL/game targets from owning providers. Put same LaunchItem in two collections, reorder/tag/favourite and use reliable typed smart predicate; verify no duplicate target. Discover permission-scoped inventory/manual import and deny unrelated enumeration. Sync to second client lacking target and preserve unavailable entry. Open through actual owner/provider (browser URL or canonical app deep link; enrolled device only if approved), inspect real action result and target identity. AI collection examples use real discovered items and risk controls.

## VARIANTS

Required UI: Picture document/version Variants panel, branch status/deep links, compare/navigation/selected merge/conflict recovery

In Picture create variant from base RevisionID with stable VariantID; edit layer/mask/transform/adjustment and incompatible raster content. Switch with unsaved changes, rename/duplicate, reopen deep link at chosen branch. Compare explicit base/source/target; navigate additions/removals/conflicts with textual status and preview selected change effects. Independently edit source/target to invalidate preview; reject stale/idempotent retry. Resolve Keep target/Use source/manual or faithful whole-version choice into new target revision preserving both histories. Cancel/interrupt merge and archive/recover without deleting only content. Picture structural assertions are mandatory; other six apps handed to owning B2/B4, never counted B3 verified.

## TERRA-FORM

Required UI: Owning specialist embedded source/editor, shared context/selection/reference/permission, Picture Variants

Reference populated Picture/Wave/Motion/Maps/Planner/Dev/Stack source from authorised Files organisation/Link/Boards/Paths/Replay surface. Inspect exact canonical source/section/revision policy; edit through source-owner API with same selection, save state and current permissions. Move/rename/delete selected source section, revoke source while host remains shared, close popup and return focus/viewport. Remove placement versus original deletion; native export dependency manifest and explicit portable disclosure; second client validates no duplicated or leaked sources. Historical Replay checkpoint stays at pinned revision after source changes. Unsupported combinations state precise limitation; no flattening substitution.

## OPERATING-CONTRACT

Required UI: Coordinator/reviewer evidence and actual runtime/release procedures

Review exact clause against repository authority, owned diffs, source/test integrity, real donor/runtime evidence, safe external scope and independent reviewer. Record a reproducible artifact or blocker, not self-attestation. Recheck canonical revision and rerun affected acceptance on final integrated frontend/backend candidate; no unit/build result substitutes for runtime, subjective/objective or Definition of Done gates.

