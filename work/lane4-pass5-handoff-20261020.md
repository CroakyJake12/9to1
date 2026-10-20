# Lane 4 handoff — Pass 5 (shared header + Assistants rail read projection; exact shared-control admission; source/tests only)

Date: 2026-10-20
Worker: Mercury implementation worker (lane 4)
Tree: `/workspace/9to1-mercury-haven`, branch `codex/mercury-haven-20261020`, HEAD `fb70840d`
(base; plus frozen pass3 dirty source checkpoint `8631490897ce8dceac910df170aa28cee27375fb`).
Pass: `parallel6-lane4-haven-pass5.prompt.md`; read pass4 prompt, pass4 readonly review, pass3 prompt,
pass3 readonly review `lane4-pass3-readonly-review-86314908.md`, pass2 peer review, owner references and
the canonical spec (SHA `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`, verified).

## Owner references re-read this handoff (sole product authority)
- `docs/specification/owner-references/2026-10-20-single-app-implementation/owner-correction.json`;
  one owning app surface/engine across standalone and embedded; shared controls owned by Haven when
  embedded; do not ship parallel divergent implementations.
- `docs/specification/owner-references/2026-10-20-haven-shared-header/owner-correction.json`;
  embedded app header exposes only search; model/notifications/profile are on the persistent Browse/Haven
  rail and unified across apps; standalone exposes all four; preserve genuine Home identity, never
  fabricate/deduplicate state; shared host-context/presentation contracts.
- `docs/specification/owner-references/2026-10-20-spaces-svg-icons/owner-correction.json`;
  Assistants button = running assistant, else most-used, else generic; resolve from real authorised state,
  never fabricate running/usage.
- `docs/specification/owner-references/2026-10-20-sidebar-separation/owner-correction.json`;
  distinct semantic surfaces for icon rail vs contextual sidebar; use existing CUI semantic surfaces, no
  hardcoded colours (reused in header/rail surface choices).

## Identity / constraints honored
- No edits to `App.UnifiedWorkspaceHost.cs` (lane5-owned) or `App.OriginalWindowsHome.cs` (root-owned).
  All lane4 host behavior lives in `App.UnifiedWorkspaceHost.SharedControls.cs` partials; root-applied
  integration/registration hunks are in `work/lane4-pass5-integration-patch-20261020.md`.
- NO .NET/MSBuild/restore/build/test/app/display/native/model/provider/network/config/credential/cap
  changes; no image/SVG image tooling; no model subagents; no commit/push/deploy/external writes;
  TEXT ONLY; execution NOT RUN without a root grant.
- Preserved `work/` checkpoints; no reset/clean/checkout/cherry-pick; no shared registry/status update.

## What pass 5 adds (over pass 4)

### B. Shared header component (owner shared-header + single-app)
- `Views/Pages/Home/HavenAppHeader.cui` — authored canonical shared app-header component. It exposes the
  search box always; model/notifications/profile controls are bound to `ShowModel`/`ShowNotifications`/
  `ShowProfile`, so embedded surfaces render only search (per owner) while standalone surfaces show all
  four. Uses only existing CUI semantic resources (`CuiPanelBrush`, `CuiLineBrush`, `CuiInputSurfaceBrush`,
  `CuiTextBrush`, `CuiTransparentBrush`); no hardcoded accent/no new native presentation.
- `HavenAppHeaderBindings.cs` — trusted typed host-context projection implementing `ICuiBindingContext`,
  `ICuiActionDispatcher`, `INotifyPropertyChanged`. Every flag derives from the real `IHavenHostContext`
  (`IsEmbedded`, `ModelSupported`, `NotificationsSupported`, `ProfileSupported`), never from visual/route
  text. Dispatch routes `9to1.Haven.OpenModel/OpenNotifications/OpenProfile` to the same typed shared
  gateway and `9to1.Haven.Search` to the app-owned search. Uses `IHavenHostContext`/
  `HavenSharedOpenResult` from `Haven.Application/Haven/HavenHostContext.cs` (lane4-authored).

### B. Assistants rail icon read projection (owner spaces-svg-icons)
- `AssistantRailIconProjection.cs` — genuine bounded read projection. Resolution order is the owner's:
  running configured assistant, else most-used configured assistant, else generic only when authorised
  reads prove none configured. It distinguishes typed states: `Available`, `NoneConfigured`,
  `Unavailable` (access denied/catalogue missing — NOT a truthful "none"), and `UsageNotRecorded`
  (configured exist but no genuine usage aggregate and nothing running). It never invents running/usage.
- `HostAssistantRailIconSource.cs` — concrete host source. Reads the authorised configured-Assistant
  catalogue from the SAME composed canonical bridge (when available); truthfully reports running/usage
  sources are not yet composed (returns null), so the projection surfaces Unavailable/UsageNotRecorded
  rather than fabricating a most-used selection.

## Fundamental A fixes confirmed this pass (A.1–A.6)
See pass4 handoff for A.1–A.3 detail; pass5 confirms and adds:
- **A.4** — PATCH D corrected: `ownership.Identity.Profiles/StateStore` (was `identity.*`), exact hunk.
- **A.5** — dispatchers (`HomeModelPickerDispatcher`, notifications) reserve the original task BEFORE owner
  work (`Dispatch`/`Retain` track before action) and surface structured stale/malformed refusals via
  `PublishSharedRefusal` (new channel) instead of silent `CompletedTask`.
- **A.6** — helperIB001 documented BLOCKED on canonical migration of legacy `.hui` assets (consumers
  enumerated; conformance guard retained; capabilities not deleted).

## Files changed / created (pass 5) — SHA-256 (post-edit)
| path | SHA256 |
|---|---|
| `Haven.Desktop/App.UnifiedWorkspaceHost.SharedControls.cs` | `d07254a98adb4ae593e72ae48a490a8e1926a1281422f7b70454d7e0f1b5899d` |
| `Haven.Desktop/AssistantRailIconProjection.cs` | `49b004af10f29bbcf2bc3419981ec10d4fe086985ee9eb54e92916544c940fab` |
| `Haven.Desktop/HostAssistantRailIconSource.cs` | `5e321da943339b7ae9ba6574b022e07dda29505537f498dc2ee5915039f523ac` |
| `Haven.Desktop/HavenAppHeaderBindings.cs` | `9dd3b961919ac42dc97a9cecd76b9d87aef5f6436573bd2699f961eb4b17e52b` |
| `Haven.Desktop/Views/Pages/Home/HavenAppHeader.cui` | `9068e0012ea1966de203b217df646d3bf0a57e451d32bdd171b946bee6df6322` |
| `Haven.Desktop/Views/Pages/Home/HavenNotifications.cui` | `18dd0dcdff589c7a118f116a6e2fa0ff3afccb6c36a65dd4c20178334392d515` |
| `Haven.Desktop/Views/Pages/Home/HavenSharedPanelShell.cui` | `5312593134116022c5ed5cdc7aa9b45bd90781f54600743f9384707a67e0c07f` |
| `Browse/NativeUI/BrowseNativeWorkspace.SharedControls.cs` | `7d538e8030a7fa7f789370fc9b69f5634595dc0577cdedaf945d898ace8967c5` |
| `Haven.Application/Haven/HavenHostContext.cs` | `27ebde8d381bfe98722bb124bcd195586c9d41518367582ea7d6e5e1313f19d3` |
| `Haven.Desktop/Haven.Desktop.csproj` | `796c2a71f895e2bc507571f87003112e113ef23b7d58b4a352e4cdaa05179052` |
| `work/lane4-pass5-integration-patch-20261020.md` | (new) |

`App.UnifiedWorkspaceHost.cs` and `App.OriginalWindowsHome.cs` remain clean vs HEAD.

## Tests authored (NOT RUN)
- `Haven.Desktop.Tests/AssistantRailIconProjectionTests.cs` (`d090ec46…`): running-first; most-used when
  nothing runs; none-configured→generic; disabled/archived excluded; unavailable-catalogue distinct from
  none; access-denial distinct; usage-not-recorded distinct when configured but no usage aggregate.
- `Haven.Desktop.Tests/HavenAppHeaderBindingsTests.cs` (`41b7d053…`): embedded exposes search only;
  standalone exposes all four through the real gateway; standalone respects actual support flags; dispatch
  routes shared controls to the same gateway; search routes to app-owned search.
- Both registered in `Haven.Desktop.Tests.csproj` (hash `36c13da8…`, net10.0 compile group) with a
  "Lane4 shared header/Assistants rail read projections (source-only, NOT RUN)" comment.

## NOT-RUN commands (when root grants the serialized slot)
- `dotnet test "9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj" --filter "FullyQualifiedName~AssistantRailIconProjectionTests|FullyQualifiedName~HavenAppHeaderBindingsTests"`
- NativeUI.Tests and Home.Tests filters as in pass4.
- Host composition/close settlement/header mount: exercised after PATCH A/E applied (root-owned slot).

## Truthful residuals (NOT closed by this lane)
- **Running/usage sources for the Assistants rail icon are not composed** → the host projection returns
  typed `Unavailable`/`UsageNotRecorded`; it never invents most-used. Composing real running Task/Den
  execution ownership and a canonical usage aggregate is pending storage-owner work (next task).
- **Compiled CUI transport (PATCH B) BLOCKED (CUIR070)** + **CUIB001** (legacy `.hui` migration) → the
  header (`HavenAppHeader.cui`) and panel surfaces (`HavenNotifications.cui`, `HavenSharedPanelShell.cui`)
  cannot be loaded/verified yet; their source is authored and embedded, but host-level mount is deferred to
  the root slot after lane2's typed transport + the `.hui` migration are merged.
- **Host-level show/hide/header mount tests**: blocked on lane5/root wiring (consistent with pass4
  residual); no builds/tests run per grant.
- **Synchronous dispatcher actor fence** for `HomeModelPickerDispatcher` remains (notifications re-observe
  the actor); full async re-observation for every dispatcher command is the documented refinement.

## For the coordinator
1. Apply PATCH A (lane5 hooks), PATCH D (route-owner registration), PATCH E (header seams), PATCH F
   (assistant rail source registration) after lane5/root freezes; reconcile PATCH B/C with lane2.
2. Compile (guarded dotnet, `-m:1`): Browse NativeUI, Home, Haven.Desktop, Haven.Desktop.Tests.
3. Run the test filters above; confirm nonzero executed counts.
4. Independent acceptance: embedded header renders search only; standalone header shows all four through the
   real gateway; rail Assistants button reflects running/most-used/generic only from real authorised state;
   panels load under authored shell frame; close joins all retained originals; retired-panel callbacks
   never mutate the replacement.
