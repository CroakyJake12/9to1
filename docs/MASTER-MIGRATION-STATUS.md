# Master Migration Status

Last reconciled: 2026-09-23 against the dirty canonical checkout on `main` at `108404713bdd6a1a90400e5357abd63d38e65bf7`. The initial 2026-09-21 rows below retain their historical scope; the current framework validation is recorded here and in `docs/9TO1_COMPLETION_STATE.md`.

This is an evidence ledger. `PASS` means only the named check passed; it does
not mean the product is complete. A row can be `VERIFIED` only after its full
source, runtime, interaction, accessibility, platform, package, and visual
acceptance chain has evidence.

## Reconciliation completed in this pass

| Area | Current evidence | State |
| --- | --- | --- |
| Authoritative CUI language and document model | `framework/CUI/Language` and `framework/CUI/Core`; parser rejects legacy file extensions and preserves root metadata, literal attributes, actions, and direct text. | Focused parser tests: **4/4 pass**. |
| Retired competing parser | `framework/CUI/src/NineToOne.Cui.Markup.csproj` is no longer a compiled parser and active consumers use `CakeOS.Cui.Language.CuiRichParser`. | Project-reference audit required on future additions. |
| Canonical Home surface | `9to1 Workspace/Home/UI/Home.cui` is the authored Home surface; `apps/Home/ui/Home.cui` remains deleted in the dirty tree and the native host copies the canonical file. | Current Home tests: **8/8 pass**; native host Release build **PASS**, 0 warnings/errors. Native render and interaction **UNVERIFIED**. |
| CUI-dependent projects | Home, Spaces, Data CUI, Terminal CUI, Android CUI, and AI tests now reference CUI Core rather than missing historical parser paths. | Spaces tests: **11/11 pass**; Data and Terminal focused builds passed earlier in this reconciliation. |
| Core orchestration | `eng/9to1.ps1 -Action test -Component core -Configuration Release` uses the live CUI, Home, and Spaces projects. | **PASS** on 2026-09-21 (4 + 6 + 11 tests). |

## Blocking evidence

| Requirement | State | Exact boundary |
| --- | --- | --- |
| CUI runtime and native Home rendering | **BUILD PASS / RENDER UNVERIFIED** | Current CUI Runtime Release suite **52/52**, AI **4/4**, markup **4/4**; `dotnet build apps/Home/src/AvaloniaHome/AvaloniaHome.csproj -c Release -p:CuiBuildTasksLocation=<locally built vendored CakeOS task DLL> -p:UsedAvaloniaProducts= -p:TreatWarningsAsErrors=false` **PASS**, 0 warnings/errors. Previous missing vendor input and `MSB4006` target cycle did not recur in the current checkout. No native Home visual or interaction claim follows from compilation. |
| CUI DevTools | **UNVERIFIED** | Contracts exist but there is no live CUI runtime inspection or picker evidence. |
| Global persisted CUI theme setting | **UNVERIFIED** | Five CUI palettes exist, but no verified single persisted global setting bridges Home and every migrated host. |
| Home app navigation and package manager | **UNVERIFIED** | The host projects observed domain state. Unconnected navigation and package backend operations report unavailable; they do not claim success. |
| Boards, Browse, Data, Spaces, Terminal, Android end-to-end migrations | **UNFINISHED** | Some parser references and source contracts were migrated; active legacy hosts and the required real user journeys remain. |
| Firefox/Gecko Browse default | **BLOCKED** | No Gecko source, package provenance, adapter, factory registration, or rendered-page evidence exists in the checkout. The current Browse scene still depends on legacy HUI controls. See `docs/architecture/browse.md`. |
| Linux and Windows delivery | **UNVERIFIED** | No Linux package/install/launch or Windows native package/launch/presentation evidence was captured in this pass. |
| Visual reference comparison | **UNVERIFIED** | No accessible native Home visual reference and no physical Windows render/interaction evidence were captured in this pass. The former vendor build prerequisite is resolved; a successful host build is not a screenshot or interactive comparison. |

2026-09-23 framework update: the vendor prerequisite no longer blocks the current Windows Home Release build. The repository `eng/9to1.ps1 -Action verify -Component core -Configuration Release` passed with a user-authorized invocation-scoped execution-policy override. Shared solution Release tests passed 1,863/1,863. The Android app's missing `Haven.Android.Hui` project was restored from the repository's own pre-deletion commit; its portable tests passed 5/5 and the Android Release build and shared solution Release build now pass with zero warnings/errors. Native Home visual comparison, Android device validation and package bytes remain unverified.

The follow-on native-host headless test found and corrected lost authored CUI component IDs at the Avalonia loader boundary. The actual Home host now shows and arranges its canonical CUI route in the headless backend, with observed unavailable catalog/runtime state; Home host Debug/Release tests pass 1/1 each, CUI Runtime Release 53/53 and Boards app Release 50/50. Physical UI and screenshot comparison remain unverified.

## Non-negotiable verification gates

- Do not call a build, static parser test, source file, or package recipe a product verification.
- Do not reintroduce product AXAML or HUI as CUI input. Legacy material must remain classified as donor/reference until actively replaced.
- Do not state that Gecko, Chromium, WebView2, Dulche, a package manager, or a device integration works without direct runtime evidence.
- Preserve unrelated dirty work and vendor provenance. Any repair to the Avalonia vendor boundary must identify its source, revision, license, and rebuild/rebase path.

## Smallest concrete next actions

1. Capture a real native Home rendering and interaction pass against the supplied visual reference (once available); the Release host now builds.
2. Verify shared framework Debug and Release integration and platform/device rendering before declaring the framework pass converged.
3. Add a licensed Gecko integration with an engine-selection model, profile isolation, and a real rendered-page smoke test before registering it as Browse's default engine.
4. Complete one app journey at a time behind the same runtime evidence gates, starting with Browse and Home navigation.
