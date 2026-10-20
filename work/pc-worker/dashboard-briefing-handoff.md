# Lane 14 handoff — Home Dashboard source-backed briefing (TODAY agenda + RECENT WORK) — pass2

## Source identity
- Assignment checkpoint (base): `ddb2a8b813c8189b31556ef2ee491ac4f6f2b4eb` (HEAD of this worktree).
- Worktree: `/workspace/9to1-mercury-extra14-dashboard`
- Author: Mercury implementation worker, lane 14 (extra14). Pass2 continuation of the retained
  session from frozen `01081c452d0167fa5a7aa60dc3d5d4e36087a39d`.
- No model subagents used; no dotnet/MSBuild/restore/test/app run; no commit/push; no config/
  credential/cap changes; source/tests/static only.

## Changed paths (exclusive write scope)
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/Dashboard.cui` (modified) — briefing
  subtree: `<Actions>` (OpenBriefingRow / RefreshBriefing), TODAY agenda and RECENT WORK sections with
  canonical `<Repeat>` + `<If Condition>` + typed bindings, truthful loading/empty/stale/error/revoked
  states, source provenance + time/status, plus a visible `dashboard-briefing-refusal` `<Text>` bound
  to `briefing.refusal`/`briefing.hasRefusal` so a refusal is surfaced in the authored CUI.
- `.../NewDashboardPage/NewDashboardPage.Briefing.cs` (new) — typed projection
  (`DashboardBriefingTarget`, `DashboardAgendaRow`, `DashboardWorkRow`, `DashboardBriefingViewState`/
  `DashboardBriefingPhase` incl. `Revoked`), `NewDashboardPageBriefingBindings`
  (`ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
  ICuiRepeatItemBindingContext`), and page navigation/refusal/lifecycle methods: reserved-original
  live-source revalidation, refusal surfacing, and honest refusal on missing/stale/unknown/revoked
  targets. Id/Kind/ActionKey alone are never authorization; navigation is re-validated against the
  live source inside the reserved Home original-work and only `BriefingNavigator`'s verified route is
  admitted.
- `.../NewDashboardPage/NewDashboardPage.Cui.cs` (modified, bounded) — connects the authored briefing
  subtree through the canonical CUI loader (`SetBindingContext`/`SetActionDispatcher`/`WireBindings`);
  no native substitution for the new cards.
- `.../NewDashboardPage/NewDashboardPage.Widgets.cs` (modified, bounded) — `UpdateBriefing` publishes
  the authorized current source **before** the unrelated-provider `Task.WhenAll` (one held provider no
  longer hides available Today/Recent); on read error the catch path calls `RevokeBriefing(...)` so
  sensitive row content is purged instead of retained (`_briefingLastSnapshot` removed). The briefing
  update is guarded by active/mounted so a late-resolved stale source is never published as content.

## Test files (new, NOT run)
- `shared/tests/Haven.Desktop.Tests/HomeDashboardBriefingCuiTests.cs` — xunit.v3
  (`TestContext.Current.CancellationToken`); Window `Close()` in finally (no `using var` on Window,
  which is not IDisposable). Covers: typed projection; stale/error/revoked/empty states; bindings
  dispatch/availability; valid-route navigation after live revalidation; unknown-target refusal that
  is surfaced in the authored CUI; held-source revalidation (revoke-between-reads forbids
  navigation / waits then navigates exactly once); inactive page refuses with zero reads; close joins
  a held navigation original; deactivated page rejects a late-resolved source; narrow/wide render.
- `shared/tests/Haven.Infrastructure.Tests/DashboardBriefingSourceFixtureTests.cs` — genuine SQLite
  fixture; `TaskAt(Guid id, ...)` seeds the intended canonical task id and asserts that exact
  persisted taskId (plus event/call/container identities), with bounded agenda/recent reads.

## Integration patches (real unified diffs, not applied; root reconciles)
- `work/pc-worker/dashboard-briefing-integration.patch` — real applicable unified diff against
  read-only `UnifiedHomePage.cs`: sets `NewDashboardPage.BriefingNavigator = TryRouteBriefingTarget`
  when mounting, nulls it on cleanup, and adds `TryRouteBriefingTarget`. Only the verified exact
  conversation route `9to1://spaces/chat/{id}` is emitted (same route `OnConversation` uses). OTHER
  kinds (Planner task/event, call, container group/subject/project) have no verified exact entity
  route in this host, so the host returns false (honest refusal) rather than forging a URI; the
  dashboard surfaces that truthful owning-app/opening limitation. No retired aliases
  (`9to1://call`/`study`/`studio`/`planner`) are invented. Verified with `git apply --check`
  (INTEGRATION_PATCH_OK).
- `work/pc-worker/dashboard-briefing-test-registration.patch` — real applicable unified diff against
  read-only `Haven.Desktop.Tests.csproj`, adding
  `<Compile Include="HomeDashboardBriefingCuiTests.cs" />` to the existing net10.0 group (default
  Compile items are disabled there), preserving all other lane registrations. Verified with
  `git apply --check` (REGISTRATION_PATCH_OK). The Infrastructure test project does not disable
  default Compile items, so `DashboardBriefingSourceFixtureTests.cs` is included automatically; no
  infrastructure csproj registration change was required.

## Static checks performed
- Verified canonical CUI consumer path exists (`CuiControlLoader` SetBindingContext/SetActionDispatcher/
  WireBindings; `ICuiWritableBindingContext`/`ICuiRepeatItemBindingContext`/`ICuiActionDispatcher`/
  `ICuiActionAvailability`; `<Actions>` + bound action parameters on repeat items). No framework gap,
  no SourceText fallback, no C# UI-tree/flags presentation cache, no Fluent/SimpleTheme fallback.
- Verified dashboard data contracts (`DashboardSnapshot.Agenda/RecentWork`, `DashboardAgendaItem`,
  `DashboardWorkItem`) and the real page lifecycle (`ActivateAsync`/`Deactivate`/`CloseAndDrainAsync`/
  `DesktopOriginalWorkLifetime.Original.AwaitAsync`/`RunAsync<T>`/`Executing`/`IsRetiring`).
- Confirmed existing widget/layout/customization/page-switch/save-ACK/close-lifetime behavior is
  preserved; no new store, provider registration or synthetic state introduced.

## Result NOT run
- All source and tests are NOT RUN here (root owns the build/test/native-acceptance slot). No
  dotnet/MSBuild/restore/tests/apps were executed. No execution/acceptance claim is made.

## Concrete remaining mandatory scope (not claimed by this slice)
- Root: apply both patch files (`dashboard-briefing-integration.patch`,
  `dashboard-briefing-test-registration.patch`), preserving other lanes' registrations and the lane5
  Home/models + lane4 shared-header work.
- Root: build and run authentic checks — both new test files compiled; positive executed counts;
  SQLite fixture exact IDs/bounds; authored Today/Recent bind/render/action and stale/revoked/refusal
  states; current-owner/held-close lifecycle; preserved existing HomeDashboardCuiRenderTests/manual
  widget/layout/persistence and the normal native app route.
- Full Dashboard requirement coverage (AI generated tiles/layout controls, provider contract, layout
  revision/undo sync, permissions) remains owned by other lanes / broader scope; this bounded slice
  is not a claim those are finished.
