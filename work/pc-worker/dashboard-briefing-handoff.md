# Lane 14 handoff — Home Dashboard source-backed briefing (TODAY agenda + RECENT WORK)

## Source identity
- Assignment checkpoint (base): `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb` (HEAD of this worktree).
- Worktree: `/workspace/9to1-mercury-extra14-dashboard`
- Author: Mercury implementation worker, lane 14 (extra14). 16-worker authorization noted; no model
  subagents used; no dotnet/MSBuild/restore/test/app run; no commit/push; no config/credentials
  changes.

## Changed paths (exclusive write scope)
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/Dashboard.cui` (modified) — added
  briefing subtree: `<Actions>` (OpenBriefingRow / RefreshBriefing), TODAY agenda and RECENT WORK
  sections using canonical CUI `<Repeat>` + `<If Condition>` + typed bindings, truthful loading/
  empty/stale/error states, source provenance + time/status, responsive no-horizontal-scroll layout.
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/NewDashboardPage/NewDashboardPage.Briefing.cs`
  (new) — typed projection (`DashboardBriefingTarget`, `DashboardAgendaRow`, `DashboardWorkRow`,
  `DashboardBriefingViewState`/phase), `NewDashboardPageBriefingBindings`
  (`ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
  ICuiRepeatItemBindingContext`), and the partial-page navigation/refusal/lifecycle methods.
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/NewDashboardPage/NewDashboardPage.Cui.cs`
  (modified, bounded) — connects the authored briefing subtree through the canonical CUI loader path
  (`SetBindingContext` / `SetActionDispatcher` / `WireBindings`); no new BindAuthoredControl/native
  substitution for the new cards.
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/NewDashboardPage/NewDashboardPage.Widgets.cs`
  (modified, bounded) — shares the real bounded `IDashboardRepository` snapshot already read for the
  widget surface; updates briefing state on success (fresh) and on error (retained snapshot surfaced
  only as stale/non-openable; no data → truthful Error, no fabricated zeros).
- Tests (new, NOT run): `shared/tests/Haven.Desktop.Tests/HomeDashboardBriefingCuiTests.cs`,
  `shared/tests/Haven.Infrastructure.Tests/DashboardBriefingSourceFixtureTests.cs`.

## Integration-only patch (not applied; root reconciles)
- `work/pc-worker/dashboard-briefing-integration.patch` — exact seam wiring for the read-only
  `UnifiedHomePage.cs`: sets `NewDashboardPage.BriefingNavigator` when mounting the dashboard and
  unsubscribes on cleanup. Only the verified conversation route `9to1://spaces/chat/{id}` is emitted
  as an exact entity deep link; Planner tasks/events, calls and container rows route to their owning
  app only, and unverified destinations return false so the page reports an honest refusal. No
  arbitrary URI, UI automation, duplicate entity creation or task/status mutation.

## Static checks performed
- Verified canonical CUI consumer path exists (CuiControlLoader SetBindingContext/SetActionDispatcher/
  WireBindings; ICuiWritableBindingContext/ICuiRepeatItemBindingContext/ICuiActionDispatcher/
  ICuiActionAvailability; Repeat Panel default Vertical Stack; `Interactive` property; `<Actions>` +
  bound action parameters on repeat items). No framework contract gap; no SourceText fallback, no
  LocalToggle, no C# UI flags, no Fluent/SimpleTheme fallback.
- Verified agendas/recent data contracts (`DashboardSnapshot.Agenda/RecentWork`, `DashboardAgendaItem`,
  `DashboardWorkItem`) and genuine SQLite LIMIT-12 reads; typed row guards require current snapshot +
  well-formed canonical GUID + allowed Kind/ActionKey — revoked/stale/unknown rows are non-actionable
  and refused.
- Confirmed existing widget/layout/customization/page-switch/save-ACK/close-lifetime behavior is
  preserved; no new store, provider registration or synthetic personalized state introduced.

## Result NOT run
- All source and tests are NOT RUN here (root owns the build/test/native-acceptance slot). No
  dotnet/MSBuild/restore/tests/apps were executed. No execution/acceptance claim is made.

## Concrete remaining mandatory scope (not claimed by this slice)
- Root: run the .NET build + the focused test sets (Desktop briefing + Infrastructure source fixture)
  and the existing HomeDashboardCuiRenderTests under the real dashboard load.
- Root: reconcile `work/pc-worker/dashboard-briefing-integration.patch` against UnifiedHomePage and
  verify owning-app destinations (`9to1://planner`, `9to1://call`, `9to1://study`, `9to1://studio`,
  `9to1://spaces`).
- Full Dashboard requirement coverage (AI generated tiles/layout controls, provider contract, layout
  revision/undo sync, permissions) remains owned by other lanes / broader scope; this bounded slice
  is not a claim those are finished.
