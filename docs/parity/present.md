# Present Parity Record

Audit date: 2026-09-20

## Sources inspected

| Role | Source |
| --- | --- |
| Donor sketch | `reference/cakeos/phase2b-worker2-app-sketches/apps/Present/App/PresentEngineContracts.cs`, `PresentEngineImpl.cs`, and `Hui/PresentHuiScene.cs` |
| Current standalone host | `9to1 Workspace/Present/PresentApplication.cs`, `PresentAppHost.cs`, `Program.cs`, and `HavenOS.Present.csproj` |
| Current host test | `9to1 Workspace/Present/Tests/PresentAppSurfaceTests.cs` |
| Current native engine | `9to1 Workspace/Present/present-engine/` (engine, semantic layer, worker, smoke executable, and contract tests) |

## Exact status

| Capability | Donor sketch | Current source | Status | Evidence / blocker |
| --- | --- | --- | --- | --- |
| Standalone desktop host | Sketch app project only | Avalonia `PresentApplication` hosts `PresentPage` through `PresentAppHost` | Implemented | Focused headless test initializes the host and exercises existing document editing/playback objects. |
| Repository, import, and export integration | Donor native wrapper does not establish Desktop DI | `PresentAppHost.CreateDefault` resolves the existing infrastructure services | Implemented | Source inspection; the focused test uses supplied in-memory/no-op services. |
| Slide lifecycle, render, worker protocol, and ODP/PPTX round trip | Donor LOK wrapper contract | Native `present-engine` implements a C++ engine and framed worker | Implemented in the native engine; not wired to the standalone host | The current `.csproj` references existing Desktop/Application/Infrastructure projects, not `present-engine`. No integration claim is made. |
| Element text editing, selection, and move-slide worker operations | Donor sketch exposes broader native methods | Native engine has bounded snapshot-scoped element text replacement and slide move; worker advertises capabilities conditionally | Implemented in the native engine; runtime not revalidated locally | Requires Linux LibreOfficeKit, nlohmann-json, CMake, and fixtures. CMake is unavailable on this host. |
| Finished HUI Present surface | Donor HUI scene sketch | No current host connection to the native HUI/worker path | Not established | The native engine README explicitly excludes a finished HUI visual surface. |
| CUI parity | No `.cui` artifact | No `.cui` artifact | Not assessed | No CUI runtime validation exists; CUI parity is not claimed. |

## Local validation

- The current host has a focused Avalonia headless test project.
- Native engine build/runtime validation requires Linux LibreOfficeKit dependencies and CMake, which are unavailable in this workspace.
