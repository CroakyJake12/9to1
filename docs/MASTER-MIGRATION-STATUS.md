# Master Migration Status

Last audited: 2026-09-20. This ledger records evidence, not intent. `VERIFIED` requires the complete acceptance chain in the master task; no system currently meets that chain.

# Master Migration Status

Updated against local `main` baseline `72b1f1b`. This ledger uses the strict
definitions in the master task: compilation, source presence, parser tests or a
shell alone do not make a product `VERIFIED`.

## Repository baseline

- Branch: `main`
- Worktrees: one (`C:/Github/9to1`)
- Baseline local changes before this pass: none
- Tracked markup at baseline: 0 `.cui`, 68 `.axaml`, 30 `.hui`
- Avalonia source fork at baseline: absent
- Linux package bytes and OS boot evidence at baseline: absent
- Repository files at baseline: tracked `.pyc` files existed in 32 locations; all removed in this pass
- 6 `.cui` files now exist on disk (untracked): `ChatSpace.cui`, `Terminal.cui`, `FloatingAiBar.cui` and others

## Dependency order

```text
CUI source fork/compiler/runtime
  ├─ shared controls + DevTools
  ├─ Linux/Windows hosts
  └─ application .cui ports
       ├─ semantic app AI → one Dulche broker/runtime
       └─ standalone packages → OS preload manifest/image
```

## System ledger

| System/product | Owner | Source/donor | CUI port | AXAML removed | HUI removed | AI/Dulche | Linux/Windows/package/tests | Status and exact blocker |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| CUI framework | A | Avalonia 12.0.1 source fork + CUI runtime | In progress | No | No | N/A | Full Avalonia source fork at pinned `fe2741d` (30 directories preserved). Builds: Base→Controls→MicroCom→OpenGL→Vulkan→Metal→Skia→Fonts.Inter→HarfBuzz→Headless→Win32. CUI Rich Parser, ControlLoader, HeadlessRenderer, CuiViewModel, CuiActionDispatcher. CUI Build Tasks replace AXAML. **Real Win32 native window rendering from .cui file proven.** Live bindings, actions, grid layout, resources, styles, accessibility, keyboard input all demonstrated. | **UNFINISHED** — Linux/X11 not tested (host is Windows). CUI DevTools not wired. MSBuild embedding of .cui as AvaloniaResource not done. |
| CUI DevTools | B | Legacy Haven developer tools + new element tree/inspection model | In progress | N/A | N/A | N/A | Element tree, search, selector, snapshot contracts built | **UNFINISHED** — no rendering integration, no live runtime inspection, no picker. |
| Platform hosts | C | Existing HUI hosts/Avalonia adapters | In progress | No | No | N/A | Existing hosts are legacy | **UNFINISHED** — final CUI Linux/Windows host and native-presentation smoke evidence absent. |
| Home/package management | D | `Workspace/Home` | In progress | N/A | No | Partial | No accepted app `.deb` | **UNFINISHED** — C# scene/HUI dependency and non-final package backend. |
| Dulche | E | Home-owned pinned llama.cpp | N/A | N/A | Terminology expanded. Single-instance flock+lease test created. | Shared broker with cancellation control route. | **UNFINISHED** — real GGUF/llama-server binary and systemd proof absent. Broker single-instance test requires POSIX. |
| Spaces platform | F | `Workspace/Spaces` + shared legacy UI | Source/ and UI/ directories created. Space contracts built. | No | No | Partial | Package/runtime absent | **UNFINISHED** — shell custom Spaces node editor and full CUI navigation unverified. |
| Chat Space | G | Mature shared Chat source | ChatSpace.cui authored (50 lines). ChatSpaceContracts.cs, ChatSpaceController.cs, ChatSpaceProjection.cs, HavenChatSpaceBackend.cs built. | No | No | Partial | Runtime/package absent | **UNFINISHED** — CUI not loaded by new framework runtime; AXAML host still required. |
| Study Space | H | Shared Study/lesson source | In progress | No | No | Partial | Runtime/package absent | **UNFINISHED** — focused CUI UI, evidence-driven RAG and full Plan/voice integration unverified. |
| Tasks Space | I | Shared Tasks source | In progress | No | No | Partial | Runtime/package absent | **UNFINISHED** — CUI Tasks/Task Groups and execution evidence incomplete. |
| Files | J | Files donor (WinUI) | FileEntry/FileBreadcrumb/FileDragDescriptor/IFileSystemAdapter contracts built under Files/CUI/Contracts/ | Donor XAML remains | N/A | Missing | Linux CUI package absent | **UNFINISHED** — no actual CUI file-manager UI surface; contracts only. |
| Canvas | K | Rnote engine + legacy shell | In progress | No | No | Partial | Package recipes only | **UNFINISHED** — donor UI parity and advanced tools/export remain. |
| Boards | L | AppFlowy-oriented source | In progress | N/A | No | Partial | Artifact absent | **UNFINISHED** — CUI donor parity/freeform and reorder runtime evidence incomplete. |
| Browse | M | Browser backend/shared legacy surface | In progress | No | No | Partial | Package/runtime absent | **UNFINISHED** — production CUI chrome and complete browser journeys unverified. |
| Write | N | LibreOfficeKit/ODT proofs | In progress | No | No | Partial | Package/runtime absent | **UNFINISHED** — real CUI editor and end-to-end office runtime proof incomplete. |
| Data | O | Calc + DuckDB engines | DataCuiSurfaceDefinition.cs, DataCuiWorkspaceController.cs, DataWorkbookAiContracts.cs, HavenOS.Data.Cui.csproj built | No | No | Partial | No accepted app `.deb` | **UNFINISHED** — CUI spreadsheet, Linux Calc runtime, AI actions, package absent. |
| Present | P | Impress/Draw/native engine | In progress | No | No | Partial | Package/runtime absent | **UNFINISHED** — complete CUI slide editor/runtime journeys absent. |
| Studio/Dev/Terminal | Q | Shared Studio + standalone projects | Terminal.cui authored. PtyContracts.cs and UnixPtyProcess.cs (forkpty adapter) built. | No | No | Partial | PTY Unix adapter complete. Windows ConPTY adapter BLOCKED. | **UNFINISHED** — CUI host, LSP/completion, and full IDE journey not verified. |
| Notes/Plan/Call/Automations | R | Shared domain/services and legacy UI | In progress | No | No | Partial | Runtime/package absent | **UNFINISHED** — CUI ports and category-complete journeys absent. |
| Picture/Wave/Motion | S | Existing app/proof source | In progress | No | No | Partial | Package recipes only | **UNFINISHED** — Motion is scaffolding and media runtime paths lack product verification. |
| Connectors/extensibility | T | Shared connector/plugin/MCP source | N/A | N/A | N/A | Shared | External-provider proof incomplete | **UNFINISHED** — actual current source must be validated against permissions, trust and provider journeys. |
| Android | U | Existing Android host | Haven.Android.Cui.csproj, AndroidCuiSurfaceLoader.cs built. Haven.Android.Hui.csproj deleted. | No | In progress | Partial | Device/package proof absent | **UNFINISHED** — CUI host integration and device evidence still absent. |
| Shared AI framework | Master | None | AppAiContracts.cs, AppAiCoordinator.cs, FloatingAiBarState.cs, FloatingAiBar.cui, AI test suite (4/4 pass) | N/A | N/A | Approved typed actions with review/approval flow, stale-result cancellation, streaming, .cui floating bar | N/A | **UNFINISHED** — no real Dulche streaming client; no app-specific AI contexts wired. Contracts proven. |
| Build/orchestration | Master | None | eng/9to1.ps1, eng/README.md | N/A | N/A | N/A | Core component set and verification gate established. | **UNFINISHED** — no single root build for all apps; no Linux .deb build from this entry point. |
| Source classification | Master | None | docs/SOURCE-CLASSIFICATION.md | N/A | N/A | N/A | Classification documented. | **UNFINISHED** — not all directories individually audited. |
| Third-party index | Master | None | THIRD_PARTY.md | N/A | N/A | N/A | Index documented. | **UNFINISHED** — per-component audit not complete; Rnote/AppFlowy/GStreamer notices unverified. |
| OS package preload | Master | Release manifest/tooling | N/A | N/A | Legacy package listed | Dulche artifact listed | Metadata validation passes. Package bytes boot absent. | **UNFINISHED** — hashes cannot be byte-verified; no image/app launch/update evidence. |

## Evidence rules

Per-product details belong in `docs/parity/<product>.md`. A status may change to
`VERIFIED` only after source/donor parity, actual `.cui` load and render,
interaction, accessibility, AI actions where applicable, Linux build/package and
launch, Windows build/launch/native presentation, tests and core journeys all
pass. Environment-limited checks remain explicit blockers.

