# Acceptance handoff — Haven Home Apps launcher (R03 scope slice)

Worker: single PC implementation worker. Time: 2026-10-18 22:44 BST (21:44 UTC).
This is an implementation handoff for **independent** acceptance. Nothing here is self-certified;
acceptance status is NOT_RUN and the seven criteria below are for the reviewer to judge.

## Exact source / build identity
- Branch `opencode/v2-pc-sequential-20261018`, HEAD `7fee45011965edd6268033425bac71cb62f5bf8c`.
  Launcher commits: `58d82393` (authored CUI launcher + CUIR053/layout repairs), `b72bc513`
  (fixture corrections), `7fee4501` (exact-activation assertion + prepare-before-mutate).
- Canonical specification: `docs/specification/9to1-development-specification-v2.md`,
  SHA-256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`.
- Native artifact used for the runtime proof:
  `9to1 Workspace/shared/src/Haven.Desktop/bin/x64/Debug/net10.0/Haven`, built with
  `/home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1`
  → `BUILD_EXIT:0`, `0 Error(s)` (`r02-apps-native-build-2.log`). The `x64` path segment is the
  PlatformTarget folder; the ELF is native aarch64.
- Runtime profile for the proof (worker-owned, disposable): `/tmp/opencode/r03-apps-20261018T2125Z`,
  PID `2303823`, window `0x180001b`, frame `9to1`. Owner preview PID `1885357` /
  `/tmp/opencode/haven-data-4` untouched.

## Owned changed paths
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomeApps.cui` (sha256
  `5f46074d3dd0c1dc286dc531c9903a6986cc174ef4a1bdd00d877290ea31895f`)
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/HomeAppsCuiBindings.cs` (new)
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/HomeAppsCuiDocument.cs` (new)
- `9to1 Workspace/shared/src/Haven.Desktop/Views/Pages/Home/UnifiedHomePage.cs`
- `9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj` (links the existing canonical
  `apps/Web/BrowserCanonicalIcons.cs`; no new image-source capability)
- `9to1 Workspace/Browse/Host.Tests/HomeAppsLauncherTests.cs` (new) + its csproj registration

## What was actually repaired
1. **CUIR053 publication failure** (the cause of the owner-visible "app list is now empty?"): the
   apps `CuiControlLoader` had no image-source resolver, so the authored `Image Source="icon:close"`
   was rejected and `BuildAppsView` threw *before* the mode catalogue was read. The loader now owns a
   bounded registry using the same canonical symbolic icon resolver the browser and Spaces surfaces
   register. No file/network schemes, no global resolver.
2. **Layout defects**: explicit Grid `Row`s on every child; `Repeat Panel="Wrap"` (without it the
   Repeat is a single Vertical Stack and cannot wrap tiles); tile spacing via `Margin` because
   `CellGap` is rejected on a Repeat element.
3. **Failure semantics**: the launcher is prepared before any previous surface is mutated. On
   publication failure the prior section keeps its content, heading and search-row state, and the
   real parser/loader diagnostic is reported through the retained original. A retained Dashboard can
   no longer be labelled "Apps".
4. **Legacy controls removed**: the `ListBox` + separate full-width "Open app" button and the
   `BindAuthoredControl` substitution are gone; the tiles, search field and empty states are CUI
   elements bound through `HomeAppsCuiBindings` and typed commands
   (`home.open-app`, `home.set-app-query`, `home.clear-app-query`).

## Actual checks (true results, failures retained)
- `dotnet test -m:1 Browse/Host.Tests/HavenOS.Browse.Host.Tests.csproj --filter FullyQualifiedName~HomeAppsLauncherTests`
  → `TEST_EXIT:0`, **Failed 0 / Passed 4 / Total 4** (`/tmp/opencode/r02-launcher-tests-16.log`).
  The four cases pin: the CUIR053 cause on an unregistered host; zero loader errors on the production
  registry with an empty catalogue; tile rendering with real resolved vector icons (including an
  unrecognised catalogue key) and exactly one `home.open-app` dispatch with the exact identity; and
  search narrowing, honest no-match text, and clear restoring the full set.
- Native runtime proof (r03 profile above): **29 real registered tiles with icons**, `Search apps`
  entry, `29 apps` count label, honest status line; search narrowed 29 → 2 and revealed
  `Clear app search`; a non-matching query produced 0 tiles plus a two-line no-match state naming the
  query; clear restored 29; clicking `Open Files` reached `9to1://files` with no failure banner.
  Screenshot `native-r03/launcher-populated.png` (sha256
  `d00c500ef3f88215fc6af745b9344d9060fd5b23331ab9c490e05dae9e1301e7`), trees
  `inspect-1.json`, `inspect-search.json`, `inspect-nomatch.json`, `inspect-cleared.json`,
  `inspect-files.json`.

## Explicitly NOT proven / still FAIL (do not accept on the above)
- **Same-tab activation is NOT RUN.** The earlier claim was withdrawn after independent tab-identity
  review (`work/9to1-coordinator/cycle-20261018T213434Z/launcher-tab-identity-review.json`): the r03
  Files journey produced **two real tabs** (the protected non-closable Home tab plus Files) because
  current code opens a new tab when `source.IsHome`. The required journey is New Tab → Apps → select
  app, measured with actual tab identity and count. The protected Home tab must not be removed to
  make that journey pass.
- **Canonical catalogue is FAIL.** The launcher now truthfully shows that Dashboard, Chat, Study,
  Tasks, Translate and the other absorbed identities are still registered and projected as standalone
  applications. `HavenAppRoutePolicy.IsDeprecatedStandalone` still excludes only `assistants`. The
  correction must preserve stored IDs, pins, recents, grants, saved routes and compatibility aliases.
- Keyboard activation and focus behaviour were not measured on the native route.
- Empty/unavailable states were proven for "no apps" and "no match"; a genuinely unavailable or
  permission-blocked app entry was not exercised.
- R01 shutdown remains OPEN and blocks nothing here except the shared runtime: the CUI scene-host
  retained-dispatch path still prevents normal close from completing
  (`work/pc-worker/evidence/R01/shutdown-diag-20261018T2234Z/THIRD-PATH-FINDING.md`).
- `shared/tests/Haven.Desktop.Tests` cannot build on this Linux host (NETSDK1100) and
  `shared/tests/Haven.Infrastructure.Tests` is BLOCKED by the forced x64 testhost on arm64, so no
  Windows-only or infrastructure coverage exists from this worker.

## Reproduction steps for the reviewer
1. `cd "9to1 Workspace/shared/src/Haven.Desktop" && /home/jacob/.local/bin/dotnet build Haven.Desktop.csproj -c Debug -f net10.0 -m:1`
2. Create a fresh `0700` disposable profile root with `HAVEN_DATA_DIR` and `XDG_CONFIG_HOME`,
   `XDG_DATA_HOME`, `XDG_CACHE_HOME`, `XDG_STATE_HOME` under it, `DOTNET_ROOT=/home/jacob/.local/share/dotnet`, `umask 077`.
3. Launch `bin/x64/Debug/net10.0/Haven`; the Home surface publishes with the Apps section selected.
   To reach it deliberately, use the browser chrome's Home/New Tab route and the Home sidebar `Apps`
   button. Do **not** use `9to1://home/apps`: `home` is not a registered native app authority and is
   correctly refused.
4. Inspect with `work/blank-window/atspi_tool.py` (`inspect`, `set-text --name "Search apps" --role entry`,
   `click --name "Clear app search" --role button`) and `window_tool.py capture`.
5. Normal close only (`window_tool.py close` = `WM_DELETE_WINDOW`). Never treat a signal exit as a
   passing shutdown.
