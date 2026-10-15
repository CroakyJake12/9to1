# C1 independent review of B1 Home dispatch fix

Exact reviewed/replayed head: 830f747b70df07797c586f6124890fa5028452b2. Reviewed every changed line of d4c18169ac52fee2bd0ad47e39da8de7a2a001a0, ebfa07b13966be508bca5a1a47f45da0ef8933ed and README 830f747, plus surrounding BrowserApplication, registry and owning Home/CUI/vendor asset/build contracts. Git ancestry confirms A source candidate a63d77fe5a9dfea56c938eaa85678a9e170368ec is included. Detached exact source at /workspace/team-c/c1-review-b1-fixed; checkout remains clean. No release or B source edits.

## Prior P2 disposition: closed at source/domain-dispatch level

BrowserApplication.cs:69-75 now calls the shared production BrowserRouteDispatcher before fallback rendering. BrowserRouteDispatcher.cs:16-19 consults registered canonical owner routes first and immediately returns the owning result, with IsUnavailableHome=false. Failed, denied, permission-required, unavailable, mismatched-view and owner-exception results cannot reach unavailableHome. A context clear that removes a previously observed owner also produces the owning registry failure without fallback. Only absent routes invoke the explicitly unavailable Home binding (lines21-23). Non-Home absent routes remain unavailable without artifact substitution. Cancellation precedes fallback. Surrounding application still checks disposal, navigation version and cancellation before rendering and disposes late surfaces.

New B1-DISPATCH-01 tests all three previously intercepted Home routes and exact request identity/opaque context. B1-DISPATCH-02 tests all three routes against PermissionDenied, PermissionRequired and HomeServiceUnavailable; fallback call count must be zero and original result/request preserved. Additional tests cover absent Home/non-Home, actual owning Home host exception mapping and cancelled fallback. These exercise the exact dispatcher used by BrowserApplication, rather than bypassing the fixed decision through registry alone. Full mounted application rendering remains a separate gate.

## Asset/SDK/theme review

The host keeps actual source CUI, Avalonia browser and Home project references. New props/targets imports match the existing pinned source browser adapter's packaged build contracts; they provide WebGL/linker exports and actual static-web-asset manifests. New explicit guard requires both avalonia.js and storage.js before resolving asset inputs. No fake browser module, replacement NuGet UI runtime, copied mock shell, deleted failing acceptance test or source-contract fork is introduced by these three commits. Legacy main JS/HTML/AppBundle assumptions are replaced with the installed .NET10 SDK static-web-asset flow.

ShouldIncludeNativeSkiaSharp/HarfBuzzSharp=false disables the adapter's explicit older-version static-library glob, not the transitive native package references: source Avalonia.Skia and Avalonia.HarfBuzz each retain IncludeWasmSkia=true; pinned build props select their WebAssembly native packages. The source graphs therefore preserve native dependencies. This reviewer did not run WASM publish or inspect emitted linked variants, so correct SDK ABI/variant and actual output content remain UNVERIFIED runtime gates rather than inferred from these flags.

BrowserApplication.Initialize and Program startup call existing CuiNativeHost.InitialisePrimitiveTheme/ConfigureFonts. Helpers retain shared primitive palette/theme/Inter behavior and do not start local Home services. Source calls do not establish actual browser visual/interaction/theme acceptance.

README accurately retains production browser rendering/scroll, deployment, supported-browser matrix, real service negotiation, shared AI bar, full application continuity, accessibility and resource gates. Its Bun1.3.4 selection is disclosed as environment-selected tooling; donor lock/build sources remain authority. Existing donor auto build invokes dnx Bun.Unofficial.Tool without a donor-pinned version; the isolated frozen-lock build procedure does not imply all future auto build executions are toolchain-pinned. Release pipeline should record exact effective tool/artifact identity; this is an existing prerequisite, not a waiver or hidden PASS.

## Independent replay and evidence

Command (cwd /workspace/team-c/c1-review-b1-fixed):
DOTNET_CLI_HOME=/workspace/team-c/evidence/c1/review-b1-fixed/dotnet-home NUGET_PACKAGES=/workspace/team-c/evidence/c1/review-b1-fixed/nuget DOTNET_CLI_TELEMETRY_OPTOUT=1 /workspace/.tools/dotnet/dotnet run --project apps/Web/Tests/BrowserNavigation.Tests.csproj -c Release

Observed exit 0: 22 discovered,22 executed,22 passed,0 failed. Raw route-tests.log retained in this directory. Tests use actual source-linked browser codec/registry/dispatcher, current owning Home contracts/actor guard and CUI Core, with fixture handlers and fallback surfaces. No hosted/provider/full-browser acceptance implied. No shared browser port/session or heavy vendor build used.

Source SHA256:
- BrowserRouteDispatcher.cs 19ad2399168ee064fe1c4ec24ce0a5988ed6419ba478b73226fe378f6af134b7
- BrowserApplication.cs 3cf04f6ccc0c89a75c90de1012dded5252258bc83511537e5cd887eeb479823e
- NineToOne.Web.csproj a1333852cd9e18121427e417ce0cc3f75a57668c1b91d144fec95fc3081f6ace
- Tests/Program.cs 0204b5dbae120b34cdd1cfd865842aab15852a35f05dbb3a46585d5b6b74bd7d
- route-tests.log 737cfef4f594613091c87c5b36c6a5d0fc10d1e53fa96cd29e9223d21bfb772d

No new actionable correctness finding in the requested fix. Prior P2 source dispatch obstruction closed; final integrated candidate/full-host/provider/semantic gates still require fresh owning acceptance. Root is sole integrator.
