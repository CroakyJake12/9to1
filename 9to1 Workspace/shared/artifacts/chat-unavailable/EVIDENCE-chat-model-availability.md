# Chat model-availability / Spaces retirement lane — evidence

Branch: `opencode/m3-shell-wiring-20261016`
Owner lane: `NewChatPage` + original lifetime, `OverlayChatSessionFactory`,
`SpacesBrowseAppContentSession`, directly necessary canonical adapters/tests.
All heavy commands under `flock --close /tmp/9to1-authorised-build.lock`.

## Commands and true exits

| # | Command | Exit | Result |
|---|---------|------|--------|
| 1 | `dotnet build ".../Haven.Desktop/Haven.Desktop.csproj" -c Debug -v m` | 0 | 1 warning (pre-existing WebRtcVad), 0 errors — `haven-desktop-build.log/.exit` |
| 2 | `HAVEN_DATA_DIR=/tmp/opencode/9to1-dt-data dotnet test ".../Haven.Desktop.Tests" -c Debug -f net10.0 -p:EnableWindowsTargeting=true --filter <4 owned classes>` | 0 | `Passed: 39, Failed: 0, Skipped: 0` — `desktop-focused.log/.exit` |
| 3 | `dotnet build ".../Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj" -c Debug -v m` | 0 | 1 warning, 0 errors |
| 4 | `dotnet test ".../HavenOS.Browse.Host.Tests" -c Debug --no-build` (full suite) | 0 | `Passed: 154, Failed: 0, Skipped: 0` — `browse-host-full.log/.exit`, `browse-host-full.trx` |
| 5 | 25 x `dotnet test ... --filter <two new rig tests>` | 0 (all) | 0/25 failures — deterministic |
| 6 | Negative control: revert admission gate publish + catalogue-membership gate, rebuild, run (5) | 1 | BOTH new rig tests FAIL on the intended assertions — `negative-control.log/.exit` |
| 7 | Baseline reproduction: restore HEAD `Abstractions.cs`, `DesktopOriginalWorkLifetime.cs`, `SpacesBrowseAppContentSession.cs`, `NewChatPage.cs`, `OverlayChatInitializationTests.cs` (my new source files moved aside), rebuild, 20 x filtered `Conversation_selection_waits...` | 1 (all) | 20/20 FAILED at HEAD — `baseline-build.log` |

## Behaviour verified by 1/2/3/4/5

- Typed availability boundary `ChatModelAvailability { Unknown, Discovering, Available, Unavailable }`
  plus `ChatModelDiscoveryPolicy.Classify` over the COMPLETE terminal payload
  (`ModelProviderDiscoveryAvailability.IsExpectedUnavailable` is the narrow expected
  connectivity/status boundary). Unexpected invariant/parse/programmer faults, cancellation and
  stop/cleanup siblings keep refusal + retirement semantics and are retained with exact identity.
- `NewChatPage.ModelAvailability` / `ModelAvailabilityCause`: an `Unavailable` observation is
  terminal across Fresh/Restore; `SelectModel` cannot promote `Unknown/Discovering/Unavailable`
  and cannot clear a real cause. Only membership in the latest successful catalogue
  (`_admittedCatalogue`, `AdmittedCatalogueEntry`) makes a selection actionable, so a removed
  descriptor handed back by an older open picker stays configuration only.
- `CanUseSelectedModel` requires positive current availability + exact admitted catalogue
  membership; the exact admitted model is captured once and used across awaits
  (`admittedModel` into `RunDualComparisonAsync`, Tasks send, `_sessions.SendAsync`), so a held
  retry or a later degraded refresh cannot swap the model mid-send.
- Discovery admission (`RefreshModelsOriginalBodyAsync`) publishes the truthful send gate
  (`PublishOriginalModelGateVisual`) BEFORE the provider await, so a held `Discovering` retry
  cannot leave an enabled Send control visible; `SelectModel` and both catalogue-read outcomes
  publish the same gate only (no transcript re-sync for a read that changed no message).
- `ObserveTerminalAsync<T>` exposes the COMPLETE terminal payload (`TerminalObservation<T>` with
  every direct sibling cause) instead of an await-selected first exception.
- `SpacesBrowseAppContentSession` failed retirement now uses the canonical CUI path
  (`CuiSceneHost` + `BuildFailedRetirementScene` with `CuiSceneAvailabilityState.Unavailable` and
  code `spaces-retirement-failed`, retained in `_retiredDiagnosticHost`); clean retirement sets
  `_root.Content = null`; raw `TextBlock`/`ScrollViewer` presentation removed.

## Pre-existing, separate, unaccepted boundary (not this lane)

The maintained page admits a background transcript scroll (`ScrollToEndIfFollowing`) whose
publication guard is bound to the conversation presentation generation. When the Fresh/Load
transition (or close) replaces that target first, the queued scroll faults with
`InvalidOperationException: The original conversation presentation was replaced.` and is retained
by the page owner. This exists at HEAD: measured **20/20 failures** for
`Conversation_selection_waits_for_actual_page_initialization_then_settled_page_closes(restore: False)`
on baseline sources. With this lane's sources the same isolated filtered regime is **3/10**, and
the full suite passes 154/154. The existing test is left unchanged (assertions/cleanup preserved)
and this boundary remains separately unaccepted, as directed.

The two new rig cases therefore join the construction publication before the Fresh transition via
`OverlayChatSessionFactory.CreateNewWithConstructionJoinAsync` (internal owner-observation seam,
the same two real production steps) plus `DrainBackgroundPageOriginalsAsync`, which joins the same
admitted originals and still surfaces every real failure a joined original produces.

## Environment note (not this lane)

`Haven.Desktop.Tests` full-suite runs on this machine are blocked by the shared credential root
`/home/jacob/.config/Haven` being mode `775`: `LinuxProviderSecretStore` refuses insecure existing
custody, which fails 206 `[AvaloniaFact]` tests in unrelated classes during App initialization
(`desktop-tests-full.log`, EXIT=1). Running with a private fresh `HAVEN_DATA_DIR` makes those tests
execute their real bodies, which exceeds the bounded 30-minute window used here. The focused owned
subset (2 above) is unaffected and green. This is pre-existing and outside the lane.
